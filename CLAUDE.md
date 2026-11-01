# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

Namerre is a self-hosted web file renamer: an ASP.NET Core (.NET 10) API that hosts an Angular 22
SPA, with user-ordered rename pipelines, previews, durable journaled execution, persistent history
(SQLite), selective rollback and recovery. `README.md` is the authoritative user/operator doc; `docs/architecture.md` describes the internals.

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

## Where things are

Architecture and behaviour are described in [`docs/architecture.md`](docs/architecture.md): layers,
request flow, plugin catalog and form model, engine, lifecycle, workers, live updates, TMDB,
configuration, users/auth, hosting, frontend, CI/release. `README.md` is the authoritative
user/operator doc (setup, configuration, security, deployment, telemetry). Read the relevant section
before changing a feature, and update it with the change.

## Architecture rules

- Dependencies point inward: `Domain` ← `Application` ← `Infrastructure` ← `Api` (composition root).
  Application must not reference Infrastructure or ASP.NET Core; define interfaces in Application,
  implement in Infrastructure.
- Each project registers its services in its own `DependencyInjection` extension
  (`AddApplication()`, `AddInfrastructure(configuration)`, `AddApi()`); `Namerre.Api/Program.cs`
  only calls them.
- Controllers (`Namerre.Api/Controllers`, namespace `Namerre.Controllers`) use routes starting with
  `api/` and send CMediator requests via `ISender`. Request and handler live in separate files
  (`XQuery.cs` + `XQueryHandler.cs`) in a feature folder with a matching namespace. Renaming handlers
  stay thin; logic belongs in `Namerre.Infrastructure/Renaming/RenameService.cs`.
- DTOs: one type per file in the feature folder; only shared ones and the `IRenameService` /
  `ITmdbMetadata` contracts stay in `Application/Renaming`.
- Execution order is the request's plugin order, never catalog order. Run state changes must satisfy
  `Domain/Renaming/RunTransitions.Allows`.
- A new plugin needs no frontend change unless it has extra UI (`plugin-editor` renders the
  descriptor's `form`).
- Add EF migrations with `dotnet ef migrations add <Name> -o Renaming/Migrations` from
  `Namerre.Infrastructure`.
- Angular builds into `Namerre.Api/wwwroot` (generated); put static assets in `Namerre.Front/public`.
  Use same-origin `/api/...` URLs. `@angular/service-worker` pins `@angular/core` exactly: upgrade
  them together.

## Safety invariants

- **Filesystem:** all path access goes through `BoundedFileSystem` (`IRenameFileSystem`) and
  `ServerFolderBrowser` (`IFolderBrowser`), rooted at `Namerre:RootPath`. Never allow escape from the
  root or traversal via symlinks/junctions; unsupported filesystems fail closed; no overwrites, no
  cross-device copy fallback. Data directory and media root must not contain each other.
- **Journaling:** runs are journaled, not atomic; interrupted mutations are never blindly resumed.
- **TMDB:** `Namerre:Tmdb:ReadAccessToken` stays server-side; never put it in frontend config, plan
  snapshots, build args or committed files.
- **Integrations:** tokens only in request headers, never logged.
- **Service worker:** caches only the app shell; never add `/api` to its asset or data groups.
- **Telemetry:** keep metric tags low-cardinality (never paths or IDs); mark requests whose URL may
  hold a secret with `NamerreTelemetry.Untraced`.
- **History scoping:** every plan/run read in `RenameService` goes through `Viewer`, so another
  user's record is `NotFound`; `HubRenameNotifier` only nudges administrators and the owner. Every
  access change must replace the user's credential stamp so sessions end at once.
- **Secrets:** never commit real credentials; mark test fixtures with `betterleaks:allow` or add
  them to `.betterleaksignore`.

## Keep in sync

Changing one side requires changing the other (details in `docs/architecture.md`):

- `CompanionFiles` ↔ `companions.ts`
- `TmdbPlugin.OptionsHash` ↔ `Namerre.Front/src/app/modules/renamer/options-hash.ts`
- `TmdbPlugin.Render` (and `{episode}` / `episodeRange` rendering) ↔ `tmdb-template-preview.ts`
- `TmdbFileName` ↔ `tmdb-filename.ts`
- `MatchConfidence.High` ↔ `HIGH_CONFIDENCE`
- `modules/renamer/simple-presets.json` ↔ plugin options (checked by
  `Namerre.Application.UnitTests/Renaming/SimplePresetTests`)
- Telemetry spans and metrics ↔ the list in `README.md`

## Conventions

- C# nullable reference types and implicit usings are enabled. Controllers use `[ApiController]`
  and verb attributes.
- CMediator v1.1.3 (its upstream code-fix package is named `CMediato.CodeFixes`): requests are
  `sealed`, handlers are `internal sealed` and stay in Application (the source generator only sees
  that compilation); registration comes from `AddApplication()` — no manual handler registration
  or assembly scanning. `MED001/002/003/005` are build errors. Pass the request
  `CancellationToken` through `ISender.Send`.
- Don't make handlers public for tests; `InternalsVisibleTo` exposes them to
  `Namerre.Application.UnitTests`. API and Application unit tests use xUnit + NSubstitute
  (substitute collaborators, not the class under test). Other test projects are described in
  `docs/architecture.md`.
- Frontend: Biome (`biome.json`) for TS/CSS/SCSS, Prettier only for Angular `.html` templates (Biome
  can't parse Angular control flow yet); 2 spaces, single quotes, 100 columns, OS-native line
  endings. Keep the strict compiler flags enabled (TypeScript `noImplicitOverride`,
  `noPropertyAccessFromIndexSignature`, `noImplicitReturns`, `noFallthroughCasesInSwitch`; Angular
  strict injection parameters and input access modifiers). Component style (standalone, signals,
  external templates, `app` prefix) is in `docs/architecture.md`.
- Workload limits are enforced server-side through `RenameLimits` (`Namerre:Limits`; defaults and
  ceilings in `docs/architecture.md`).
- CI runs on PRs to `main`/`next` (backend on Ubuntu and Windows, so keep path handling
  cross-platform) and is reused by `release.yml` and `docker.yml`; details in
  `docs/architecture.md`.
- Commits follow Conventional Commits (`feat:`, `fix:`, `docs:`, `chore:`, …; `!` or a
  `BREAKING CHANGE:` footer for majors) — they drive the version bump. CI lints PR
  commits with commitlint; root `npm ci` installs the lefthook `commit-msg` hook (`lefthook.yml`).
