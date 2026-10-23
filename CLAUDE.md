# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

Namerre is a self-hosted web file renamer: an ASP.NET Core (.NET 10) API that hosts an Angular 22
SPA, with user-ordered rename pipelines, previews, durable journaled execution, persistent history
(SQLite), selective rollback and recovery. `README.md` is the authoritative user/operator doc;
`.github/copilot-instructions.md` holds older but mostly still valid conventions (its claim that
renaming is "not yet implemented" and that Domain is empty is outdated).

## Commands

Run from the repo root (PowerShell). The first restore needs GitHub Packages credentials for
CMediator via `NuGetPackageSourceCredentials_Github`
(`Username=...;Password=<PAT with read:packages>;ValidAuthenticationTypes=Basic`); `nuget.config`
maps CMediator packages to the `Github` source.

```powershell
npm ci --prefix .\Namerre.Front
dotnet build .\Namerre.slnx                 # also runs `npm run build` (BuildAngularFrontend target)
dotnet test .\Namerre.slnx --no-build
dotnet test .\Namerre.Application.UnitTests\Namerre.Application.UnitTests.csproj --no-build --filter "FullyQualifiedName~PipelineEngineTests"
$env:Namerre__RootPath = 'D:\Media'; $env:Namerre__DataPath = 'D:\NamerreData'
dotnet run --project .\Namerre.Api\Namerre.Api.csproj --launch-profile https   # https://localhost:7151
dotnet run --project .\Namerre.Api\Namerre.Api.csproj -- --hash-password       # interactive admin hash
```

- `/p:BuildFrontend=false` skips the npm build when `wwwroot` is already built (used in Docker).
- First run without configured credentials prints a one-time installation token to the console.
- Docker: `docker build -f .\Namerre.Api\Dockerfile -t namerre --secret id=github_packages_credentials,env=NuGetPackageSourceCredentials_Github .`

Frontend, from `Namerre.Front` (Angular unit-test builder with Vitest + jsdom — always go through
`npm test`, never invoke Vitest directly):

```powershell
npm test -- --watch=false
npm test -- --watch=false --include=src/app/app.spec.ts --filter="should render title"
npx prettier --check "src/**/*.{ts,html,scss}"
```

`npm start` (port 4200) has no API proxy; for anything touching `/api`, run the API process,
which serves Angular same-origin. Browser checks should target the API URL.

## Architecture

Clean architecture, dependencies point inward:
`Domain` ← `Application` ← `Infrastructure` ← `Api` (composition root; also references Application).
Application must not reference Infrastructure or ASP.NET Core; define interfaces in Application,
implement in Infrastructure, register in `Namerre.Api/Program.cs`.

**Request flow.** Controllers (`Namerre.Api/Controllers`, namespace `Namerre.Controllers`,
routes always start with `api/` — `RenamingController` uses per-action routes like
`api/rename-plans/{id}`) send CMediator requests via `ISender`. Renaming handlers in
`Namerre.Application/Renaming/Requests.cs` are thin pass-throughs to `IRenameService`; the real
logic lives in `Namerre.Infrastructure/Renaming/RenameService.cs` (a singleton). All DTOs and the
`IRenameService`/`ITmdbMetadata` contracts are in `Application/Renaming/Contracts.cs`;
`RenameException(code, message, status, fileId)` carries API error codes.

**Renaming pipeline.** `PipelineEngine` (Application) owns the plugin `Catalog`
(`literal`, `regex`, `case`, `tmdb`, each versioned) and applies enabled plugin instances in the
request's order — catalog order never determines execution order. Lifecycle:
preview (no mutation) → prepare (hash every source file, persist an immutable plan) →
execute (queued job) → optional rollback of selected files / explicit recovery. Run state changes
must satisfy `Domain/Renaming/RunTransitions.Allows`; naming rules are in `Domain/Renaming/NamingRules.cs`.
`RenameWorker` (hosted service) polls `RenameService.ProcessNext`, so jobs run independently of
the browser. Runs are journaled (with staging paths), not atomic; interrupted mutations are never
blindly resumed.

**Filesystem safety.** All path access goes through `BoundedFileSystem` (`IRenameFileSystem`) and
`ServerFolderBrowser` (`IFolderBrowser`), rooted at `Namerre:RootPath`. Never allow escape from
the root or traversal via symlinks/junctions; unsupported filesystems fail closed; no overwrites
and no cross-device copy fallback. The data directory and media root must not contain each other.

**Persistence.** `RenameStore` wraps EF Core SQLite (`RenameDbContext`) in `Namerre:DataPath`; it
runs `Migrate()` on startup and refuses unknown applied migrations. Migrations live in
`Namerre.Infrastructure/Renaming/Migrations`. The single administrator account is stored in the
same database (`Administrators` table).

**TMDB.** `TmdbMetadata` calls TMDB server-side with `Namerre:Tmdb:ReadAccessToken` (never put the
token in frontend config, plan snapshots or build args). Matches must be confirmed with a
`ConfirmationToken` bound to `OptionsHash` — SHA-256 of the plugin's `language` only. The frontend
reimplements this in `Namerre.Front/src/app/modules/renamer/options-hash.ts` (pure-JS SHA-256 so it
works on plain-HTTP origins); keep both sides in sync. Confirmed metadata is snapshotted into the plan.

**Configuration.** Everything app-specific is under the `Namerre` section, bound to
`NamerreOptions` (`Infrastructure/Configuration`) and consumed via `IOptions<NamerreOptions>`:
`RootPath`, `DataPath`, `Authentication`, `Tmdb`, `ReverseProxy`, `Telemetry` (optional OTLP
export, wired in `Namerre.Api/Telemetry/TelemetrySetup.cs`; off unless `OtlpEndpoint` is set).

**Hosting.** `Program.cs` requires HTTPS for `/api` (400 otherwise), sets `no-store` on API
responses, requires authorization on all controllers, returns 404 for unmatched `/api/*`, and
falls back to `index.html` otherwise. OpenAPI is exposed only in Development.

**Frontend.** Standalone Angular app (signals, external templates, `app` selector prefix, SCSS
component styles, Tailwind CSS v4 via PostCSS in `src/styles.css`). The root route lazy-loads
`modules/renamer`; `rename-api.ts` is the HTTP client for the resource-style renaming endpoints
(`/api/rename-plugins`, `/api/rename-previews`, `/api/rename-plans`, …). The workflow is a staged
wizard (`rename-workflow` component) across file selection, plugin configuration, TMDB matching,
preview, confirmation and run history. Angular builds into `Namerre.Api/wwwroot` (generated,
cleared on rebuild) — put static assets in `Namerre.Front/public`.

## Conventions

- CMediator v1.1.3: requests are `sealed`, handlers are `internal sealed` and stay in Application
  (the source generator only sees that compilation); registration comes from `AddApplication()` —
  no manual handler registration or assembly scanning. `MED001/002/003/005` are build errors.
  Pass the request `CancellationToken` through `ISender.Send`.
- Don't make handlers public for tests; `InternalsVisibleTo` exposes them to
  `Namerre.Application.UnitTests`. Unit tests use xUnit + NSubstitute (substitute collaborators,
  not the class under test). `Namerre.Api.Tests` holds HTTP integration tests (`AuthWebApplicationFactory`).
- Frontend formatting: Prettier, 2 spaces, single quotes, 100 columns. Keep the strict TypeScript
  and Angular compiler flags enabled.
- Workload limits enforced server-side: ≤200 files and ≤32 plugin instances per run, ≤32 queued
  jobs, regex timeout 100 ms, TMDB HTTP timeout 15 s.
- CI (`.github/workflows/backend.yml`) builds/tests on Ubuntu and Windows — keep path handling
  cross-platform.
