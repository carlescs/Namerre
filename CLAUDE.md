# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

Namerre is a self-hosted web file renamer: an ASP.NET Core (.NET 10) API that hosts an Angular 22
SPA, with user-ordered rename pipelines, previews, durable journaled execution, persistent history
(SQLite), selective rollback and recovery. `README.md` is the authoritative user/operator doc.

## Commands

Run from the repo root (PowerShell). The first restore needs GitHub Packages credentials for
CMediator via `NuGetPackageSourceCredentials_Github`
(`Username=...;Password=<PAT with read:packages>;ValidAuthenticationTypes=Basic`); `nuget.config`
maps CMediator packages to the `Github` source (CI uses the `CMEDIATOR_PACKAGES_TOKEN` secret).
The frontend declares `npm@11.13.0` as its package manager; use npm with the committed lockfiles.

```powershell
npm ci --prefix .\Namerre.Front
dotnet build .\Namerre.slnx                 # also runs `npm run build` (BuildAngularFrontend target)
dotnet test .\Namerre.slnx --no-build
dotnet test .\Namerre.Application.UnitTests\Namerre.Application.UnitTests.csproj --no-build --filter "FullyQualifiedName~PipelineEngineTests"
$env:Namerre__RootPath = 'D:\Media'; $env:Namerre__DataPath = 'D:\NamerreData'
dotnet run --project .\Namerre.Api\Namerre.Api.csproj --launch-profile https   # https://localhost:7151
dotnet run --project .\Namerre.Api\Namerre.Api.csproj -- --hash-password       # interactive admin hash
dotnet publish .\Namerre.Api\Namerre.Api.csproj -c Release -o .\artifacts\publish
```

- `BuildAngularFrontend` runs before content collection, so builds and publishes include the
  freshly generated `wwwroot`. `/p:BuildFrontend=false` skips the npm build when `wwwroot` is
  already built (used in Docker).
- Launch profiles: `https` serves `https://localhost:7151` and `http://localhost:5251`; `http` uses 5251.
- First run without configured credentials prints a one-time installation token to the console.
- Docker: `docker build -f .\Namerre.Api\Dockerfile -t namerre --secret id=github_packages_credentials,env=NuGetPackageSourceCredentials_Github .`
  then `docker run --rm -p 8080:8080 namerre` (`http://localhost:8080`). The Dockerfile builds Angular
  in a Node stage, publishes the API in an SDK stage, and runs only ASP.NET Core in the final image.

Frontend, from `Namerre.Front` (Angular unit-test builder with Vitest + jsdom — always go through
`npm test`, never invoke Vitest directly):

```powershell
npm test -- --watch=false
npm test -- --watch=false --include=src/app/app.spec.ts --filter="should render title"
npm run lint      # Biome (TS/CSS/SCSS lint + format) and Prettier (HTML templates)
npm run format    # apply Biome safe fixes and formatting, Prettier on templates
npm run watch     # development build in watch mode
npm run ng -- generate component component-name
```

Test globals come from `vitest/globals` (`tsconfig.spec.json`). Component tests import the
standalone component into `TestBed`, create a fixture and `await fixture.whenStable()` before
asserting on rendered content.

`npm start` (port 4200) has no API proxy; for anything touching `/api`, run the API process,
which serves Angular same-origin. Browser checks should target the API URL; `.mcp.json` configures
a headless, isolated Playwright MCP server for that.

## Architecture

Clean architecture, dependencies point inward:
`Domain` ← `Application` ← `Infrastructure` ← `Api` (composition root; also references Application).
Application must not reference Infrastructure or ASP.NET Core; define interfaces in Application,
implement in Infrastructure. Each project registers its own services through a `DependencyInjection`
extension — `AddApplication()`, `AddInfrastructure(configuration)` and the Api's `AddApi()` (web host
services, incl. the `IRenameNotifier` implementation) — and `Namerre.Api/Program.cs` only calls them.

**Request flow.** Controllers (`Namerre.Api/Controllers`, namespace `Namerre.Controllers`,
routes always start with `api/` — `RenamingController` uses per-action routes like
`api/rename-plans/{id}`) send CMediator requests via `ISender`. Each request and its handler
live in separate files (`XQuery.cs` + `XQueryHandler.cs`), grouped by feature folder with a
matching namespace (`Application/Renaming/{Plugins,Previews,Plans,Runs,Rollback,Recovery,Metadata}`).
Renaming handlers are thin pass-throughs to `IRenameService` (the run export handler also formats
through `Runs/RunExportWriter`: RFC 4180 CSV with BOM and formula-injection guard, or JSON); the real
logic lives in `Namerre.Infrastructure/Renaming/RenameService.cs` (a singleton). DTOs are one
type per file: feature-specific ones (e.g. `RunView`, `PlanView`, `TmdbMatch`) sit in their
feature folder; shared ones (`PipelineRequest`, `PluginInstance`, `RenameIssue`, `JobAccepted`, …),
the `IRenameService`/`ITmdbMetadata` contracts and `RenameException(code, message, status, fileId)`
(which carries API error codes) stay directly in `Application/Renaming`.

**Renaming pipeline.** Each plugin is an `IRenamePlugin` in `Application/Renaming/Plugins`
(`LiteralPlugin`, `ReplacementTablePlugin`, `RegexPlugin`, `CasePlugin`, `ExtensionCasePlugin`, `AffixPlugin`, `SeparatorsPlugin`,
`ReleaseTagsPlugin`, `CharactersPlugin`, `InsertPlugin`, `RemovePlugin`, `TruncatePlugin`, `PadNumbersPlugin`,
`CounterPlugin`, `DatePlugin`, `TmdbPlugin`; counter and date share `NameInsertion` for prefix/suffix/template
placement, insert/remove/truncate count grapheme clusters through `TextElements`), usually derived from
`RenamePlugin<TOptions>` (typed options, camelCase JSON). Its `PluginDescriptor` carries the type ID,
version, name, description, default options, the option `Form` the frontend renders and
`SingleInstance` (at most one instance per pipeline, enforced by the engine and the editor; set
for TMDB). Form fields are `OptionFieldKind`s (`text`, `textarea`, `checkbox`, `select`, `multiSelect`,
`number` with optional min/max/step, and `list` of non-nested row columns); groups can require checkboxes (`Requires`) and fields can show only while a select holds given values
(`VisibleWhen`, e.g. counter/date templates via `NameInsertion`); hidden values are still sent; `OptionBinding` checks
options against the form before typed binding (malformed values → `InvalidConfiguration` naming the
field) and rejects forms the editor can't render when the engine is built. Plugins are registered as singletons in `AddApplication()` (registration order =
catalog order); `PipelineEngine` takes them via DI, keys them by `(TypeId, Version)`, and applies
enabled plugin instances in the request's order — catalog order never determines execution order.
Plugins return `PathParts`; the engine rejects extension changes other than case (only
`ExtensionCasePlugin` makes those), empty stems and separators, then validates the path. With
`PipelineRequest.OnCollision = "AddNumber"` the engine gives taken targets a ` (n)` suffix
(`PreviewFile.Numbered`, `collision` trace step) before related files are named; prepare stores the result. With `PipelineRequest.IncludeCompanions`, `CompanionFiles` pairs subtitles, `.nfo` and
artwork with a video in the same folder; those skip the steps and take the video's final folder and stem plus
their suffix (`PreviewFile`/`PlanFile`/`RunFile.CompanionOf`), and the frontend mirrors the rules in
`companions.ts` to group the collection — keep both in sync. Steps also get a `FilePosition` (request and source-folder index and counts) and
`FileFacts` (size, modified and creation time via `IRenameFileSystem.Facts`); `PrepareJob` snapshots the
facts into `PlanView.Facts` once and previews with them, so a plan never re-reads them. Tests build
the engine with the built-in plugins through `TestPipeline.Create`. Lifecycle:
preview (no mutation) → prepare (hash every source file, persist an immutable plan) →
execute (queued job) → optional rollback of selected files / explicit recovery. Run state changes
must satisfy `Domain/Renaming/RunTransitions.Allows`; naming rules are in `Domain/Renaming/NamingRules.cs`.
`RenameWorker` (hosted service) polls `RenameService.ProcessNext`, so jobs run independently of
the browser. Runs are journaled (with staging paths), not atomic; interrupted mutations are never
blindly resumed.

**Live updates.** After each durable plan/run write, `RenameService` calls `IRenameNotifier`
(Application contract, best effort, never throws into the job). `HubRenameNotifier`
(`Namerre.Api/Notifications`) broadcasts a `RenameChange` nudge as `renameChanged` on the
authorized `RenameHub` (`/api/hubs/rename`). In the frontend, `rename-events.ts` (`RenameEvents.watch`
and `refreshOn`) re-reads records over REST on each nudge, falling back to polling while
disconnected; specs substitute `FakeRenameEvents` from `rename-events.testing.ts`.

**Filesystem safety.** All path access goes through `BoundedFileSystem` (`IRenameFileSystem`) and
`ServerFolderBrowser` (`IFolderBrowser`), rooted at `Namerre:RootPath`. Never allow escape from
the root or traversal via symlinks/junctions; unsupported filesystems fail closed; no overwrites
and no cross-device copy fallback. The data directory and media root must not contain each other.

**Persistence.** `RenameStore` wraps EF Core SQLite (`RenameDbContext`) in `Namerre:DataPath`; it
runs `Migrate()` on startup and refuses unknown applied migrations. Migrations live in
`Namerre.Infrastructure/Renaming/Migrations`. The single administrator account is stored in the
same database (`Administrators` table).

**TMDB.** `TmdbMetadata` calls TMDB server-side through `CachedTmdbClient` (singleton holding a
private 256-entry, 10-minute `MemoryCache`), which takes a fresh typed `TmdbClient` per lookup. The client's
Polly pipeline (`TmdbResilience`: four-lookup concurrency limiter, 15 s total / 5 s attempt timeouts, retries
that skip 429, circuit breaker) sits in front of it, and `TmdbClient` maps its rejections to API errors; requests carry
`Namerre:Tmdb:ReadAccessToken` (never put the token in frontend config, plan snapshots or build args). Matches must be confirmed with a
`ConfirmationToken` bound to `TmdbPlugin.OptionsHash` — SHA-256 of the plugin's `language` only (the `preset` option, which the
frontend's `tmdb-presets.ts` uses to fill templates, is ignored by rendering and the hash). Templates
write literal braces as `{{`/`}}`; `TmdbPlugin.Render` and `tmdb-template-preview.ts` must agree. The frontend
reimplements this in `Namerre.Front/src/app/modules/renamer/options-hash.ts` (pure-JS SHA-256 so it
works on plain-HTTP origins); keep both sides in sync. Confirmed metadata is snapshotted into the plan.

**Configuration.** Everything app-specific is under the `Namerre` section, bound to
`NamerreOptions` (`Infrastructure/Configuration`) and consumed via `IOptions<NamerreOptions>`:
`RootPath`, `DataPath`, `Limits` (bound to the Application `RenameLimits` singleton; out-of-range
values fail startup; signed-in `GET api/auth/session` reports them to the frontend's `WorkloadLimits`), `Authentication` (incl. optional `Oidc` single sign-on, e.g. Keycloak — see
`Namerre.Api/Authentication/OidcLogin.cs`; the OIDC scheme is hidden by `OidcAwareSchemeProvider` unless
`Authority` is set), `Tmdb`, `ReverseProxy`, `Telemetry` (optional OTLP
export, wired in `Namerre.Api/Telemetry/TelemetrySetup.cs`; off unless `OtlpEndpoint` is set).

**Hosting.** `Program.cs` requires HTTPS for `/api` (400 otherwise), sets `no-store` on API
responses, requires authorization on all controllers, returns 404 for unmatched `/api/*`, and
falls back to `index.html` otherwise. OpenAPI is exposed only in Development. Static files go through
`Hosting/StaticFileCaching` (`no-cache` for unhashed shell files: `index.html`, the manifest and the service worker).

**Frontend.** Standalone Angular app (signals, explicit component imports, external templates +
`styleUrl`, `app` selector prefix, SCSS component styles, Tailwind CSS v4 via PostCSS in
`src/styles.css`). `src/main.ts` bootstraps `App` with `appConfig`; app-wide providers go in
`src/app/app.config.ts`, routes in `src/app/app.routes.ts`. Use same-origin `/api/...` URLs.
The root route lazy-loads `modules/renamer`; `rename-api.ts` is the HTTP client for the
resource-style renaming endpoints (`/api/rename-plugins`, `/api/rename-previews`,
`/api/rename-plans`, …). The workflow is a staged wizard (`rename-workflow` component) across file
selection, plugin configuration, TMDB matching, preview, confirmation and run history. The
`plugin-editor` renders each step's options from the descriptor's `form`, so a new plugin needs no
frontend change unless it has extra UI (like TMDB's template preview). Angular builds into `Namerre.Api/wwwroot` with no `browser`
subdirectory (generated, cleared on rebuild) — put static assets in `Namerre.Front/public`.
Missing asset files stay 404 rather than falling back to `index.html`. The app is an installable
PWA: `public/manifest.webmanifest` plus `@angular/service-worker` (`ngsw-config.json`, production builds
only), which caches only the app shell — never add `/api` to its asset or data groups. `app-update`
offers a reload when a new version is ready. `@angular/service-worker` pins `@angular/core` exactly, so
upgrade them together.

## Conventions

- C# nullable reference types and implicit usings are enabled. Controllers use `[ApiController]`
  and verb attributes.
- CMediator v1.1.3 (its upstream code-fix package is named `CMediato.CodeFixes`): requests are
  `sealed`, handlers are `internal sealed` and stay in Application (the source generator only sees
  that compilation); registration comes from `AddApplication()` — no manual handler registration
  or assembly scanning. `MED001/002/003/005` are build errors. Pass the request
  `CancellationToken` through `ISender.Send`.
- Don't make handlers public for tests; `InternalsVisibleTo` exposes them to
  `Namerre.Application.UnitTests`. `Namerre.Api.UnitTests` and `Namerre.Application.UnitTests` use
  xUnit + NSubstitute (substitute collaborators, not the class under test).
  `Namerre.Infrastructure.UnitTests` covers `TmdbMetadata` (stub `HttpMessageHandler`) and
  `BoundedFileSystem` against temp directories. `Namerre.Api.Tests` holds HTTP integration tests
  (`AuthWebApplicationFactory`).
- Frontend linting/formatting: Biome (`biome.json`) for TS/CSS/SCSS, Prettier only for Angular
  `.html` templates (Biome can't parse Angular control flow yet); 2 spaces, single quotes,
  100 columns, OS-native line endings. Keep the strict compiler flags enabled (TypeScript
  `noImplicitOverride`, `noPropertyAccessFromIndexSignature`, `noImplicitReturns`,
  `noFallthroughCasesInSwitch`; Angular strict injection parameters and input access modifiers).
- Workload limits enforced server-side through `RenameLimits` (configurable under `Namerre:Limits`):
  by default ≤200 files and ≤32 plugin instances per run, ≤32 queued jobs (ceilings 2000/64/256);
  fixed regex timeout 100 ms, TMDB lookups 15 s overall (5 s per attempt).
- CI (`.github/workflows/ci.yml`: backend build/tests on Ubuntu and Windows, frontend lint/tests,
  Betterleaks, commitlint on PRs) runs on PRs to `main`/`next` and is reused by `release.yml` and
  `docker.yml` — keep path handling cross-platform. Shared Node setup is the
  `.github/actions/setup-node` composite action. `release.yml` runs CI, then semantic-release
  (`release.config.mjs`) on pushes to `main` (stable) and `next` (`-pre.N`), creates the tag and
  GitHub release, and calls `docker.yml` for the new tag; Docker images are built and published
  only for version tags (a hand-pushed tag runs CI in `docker.yml` first).
- Commits follow Conventional Commits (`feat:`, `fix:`, `docs:`, `chore:`, …; `!` or a
  `BREAKING CHANGE:` footer for majors) — they drive the version bump. CI lints PR
  commits with commitlint; root `npm ci` installs the lefthook `commit-msg` hook (`lefthook.yml`).
- CI's `betterleaks` job scans the full Git history with a pinned Betterleaks release on PRs and pushes;
  the lefthook `pre-commit` hook scans staged changes when `betterleaks` is on PATH. Never commit
  real credentials; mark test fixtures with `betterleaks:allow` or add them to `.betterleaksignore`.
