# Repository instructions

## Project purpose

Namerre is a web-based file renamer intended to help users maintain files in a
self-hosted environment. The Angular frontend is the browser interface, and the
ASP.NET Core API hosts the application and will handle file-renaming operations.
Keep future functionality focused on this self-hosted file-maintenance purpose.

The app supports browsing server folders under a configured root and listing
their files. File-renaming operations are not yet implemented.

## Build, run, test, and formatting

The API requires the .NET 10 SDK. The frontend uses Angular 22 and declares
`npm@11.13.0` as its package manager; use npm with the committed `package-lock.json`.

Run API commands from the workspace root:

```powershell
npm ci --prefix .\Namerre.Front
dotnet build .\Namerre.slnx
dotnet run --project .\Namerre.Api\Namerre.Api.csproj --launch-profile https
dotnet publish .\Namerre.Api\Namerre.Api.csproj -c Release -o .\artifacts\publish
```

Normal .NET builds and publishes run `npm run build` in `Namerre.Front`.
`BuildAngularFrontend` runs before content collection and includes the freshly
generated `wwwroot` files in publishing. `/p:BuildFrontend=false` skips the npm
build when assets have already been built, as in Docker.

The `https` launch profile exposes `https://localhost:7151` and
`http://localhost:5251` in Development. The `http` profile uses port 5251.
The API process serves Angular at `/` and controllers under `/api`.

Build and run the complete app in Docker from the workspace root:

```powershell
docker build -f .\Namerre.Api\Dockerfile -t namerre --secret id=github_packages_credentials,env=NuGetPackageSourceCredentials_Github .
docker run --rm -p 8080:8080 namerre
```

The container serves the app at `http://localhost:8080`. The Dockerfile builds
Angular in a Node stage, publishes the API with those assets in a .NET SDK
stage, and runs only ASP.NET Core in the final image.

Run frontend commands from `Namerre.Front`:

| Task | Command |
| --- | --- |
| Install dependencies | `npm ci` |
| Development server, port 4200 | `npm start` |
| Production build into API `wwwroot` | `npm run build` |
| Development build in watch mode | `npm run watch` |
| Unit tests in watch mode | `npm test -- --watch` |
| Unit tests, single run | `npm test -- --watch=false` |
| One test file | `npm test -- --watch=false --include=src/app/app.spec.ts` |
| One test case | `npm test -- --watch=false --include=src/app/app.spec.ts --filter="should render title"` |
| Lint and check formatting (Biome + Prettier for templates) | `npm run lint` |
| Apply safe lint fixes and formatting | `npm run format` |
| Generate a component | `npm run ng -- generate component component-name` |

Tests use Angular's `@angular/build:unit-test` builder with Vitest and jsdom.
Use the Angular test command rather than invoking Vitest directly so Angular
components and templates are compiled. Test globals come from `vitest/globals`
in `tsconfig.spec.json`.

## Architecture

- Backend projects follow clean architecture: Domain has no dependencies;
  Application references Domain; Infrastructure references Application; API
  references Application and Infrastructure and is the composition root.
  Domain is an empty scaffold. Infrastructure implements `IFolderBrowser` for
  server filesystem access. `Namerre:RootPath` must be configured explicitly;
  never allow browsing outside that root or through symbolic links/junctions.
  Application configuration is bound to `NamerreOptions` and consumed through
  `IOptions<NamerreOptions>`. Keep paths under `Namerre` and authentication/TMDB
  settings in its nested `Authentication` and `Tmdb` sections.
- Application uses CMediator v1.1.3 from GitHub Packages. Requests are sealed;
  handlers are internal sealed. Keep both in Application, where the generator
  runs, and rely on `AddApplication()` for generated scoped registration.
  `MED001`, `MED002`, `MED003` and `MED005` are treated as build errors.
  The upstream code-fix package is named `CMediato.CodeFixes`.
- `nuget.config` maps CMediator packages to the `Github` source. Supply credentials
  through `NuGetPackageSourceCredentials_Github` or local NuGet credentials.
  Docker requires `--secret id=github_packages_credentials,env=NuGetPackageSourceCredentials_Github`;
  Actions requires the `CMEDIATOR_PACKAGES_TOKEN` secret. Never commit tokens.
- `Namerre.slnx` includes the backend layers and test projects, plus the
  frontend website entry excluded from solution builds. The API build target builds the frontend.
  Run all backend tests with `dotnet test .\Namerre.slnx --no-build` after building
  the solution. `Namerre.Api.UnitTests` and `Namerre.Application.UnitTests` use
  xUnit and NSubstitute for isolated tests. Application grants its unit-test
  assembly access to internal handlers with `InternalsVisibleTo`; do not make
  handlers public for testing. `Namerre.Api.Tests` contains integration tests.
- The frontend is a client-rendered standalone Angular application:
  `src\main.ts` bootstraps `App` using `appConfig`. Application-wide providers
  belong in `src\app\app.config.ts`, and routes in `src\app\app.routes.ts`.
  `App` imports `RouterOutlet`; the root route lazy-loads the renamer feature.
  Folder browsing uses same-origin `HttpClient` requests to `/api/folders`.
- The API uses ASP.NET Core controllers. `Program.cs` registers controllers
  and OpenAPI, exposes OpenAPI only in Development, enables HTTPS redirection
  and authorization middleware, and serves default/static files from `wwwroot`.
  Controllers use an `api/` prefix in their route attributes so routing and
  generated OpenAPI paths agree.
- Use same-origin `/api/...` URLs for frontend API calls
  when hosted by ASP.NET Core.
- Unmatched `/api` requests return 404. Other non-file paths fall back to
  `index.html` for Angular routing; missing asset files remain 404.
- Angular's build output is `Namerre.Api\wwwroot` with no `browser` subdirectory.
  Treat this directory as generated: put source assets in
  `Namerre.Front\public`, because Angular clears its output on rebuild.
  `npm start` remains a separate Angular dev server on port 4200.
  Use the API process for same-origin integration; a dev-server proxy is not
  configured.

## Codebase conventions

- Use standard Angular component naming guidelines and the Angular CLI
  component generator. Components are standalone with explicit component
  imports, external templates, and `styleUrl`. The selector prefix is `app`,
  and generated component styles default to SCSS.
- Component state currently uses Angular signals (`App.title` is
  `protected readonly`). Component tests import the standalone component into
  `TestBed`, create fixtures, and await `fixture.whenStable()` before checking
  rendered content.
- Frontend linting and formatting of TypeScript, CSS and SCSS use Biome
  (`biome.json`, recommended rules); Angular HTML templates are still
  formatted by Prettier (`.prettierrc`, Angular parser) because Biome cannot
  parse Angular control-flow syntax yet. Both use two-space indentation,
  single quotes, a 100-column width and OS-native line endings.
- TypeScript enables `noImplicitOverride`, `noPropertyAccessFromIndexSignature`,
  `noImplicitReturns`, and `noFallthroughCasesInSwitch`; Angular enables strict
  injection parameters and input access modifiers. Preserve these compiler checks.
- API controllers use the `Namerre.Controllers` namespace, despite the
  project being named `Namerre.Api`. Controllers use `[ApiController]`,
  `[Route("api/[controller]")]`, and verb attributes. C# nullable reference types
  and implicit usings are enabled.

## MCP configuration

`.github\mcp.json` configures Playwright MCP through
`npx -y @playwright/mcp@latest` in headless, isolated mode. Reload the trusted
Git workspace in Copilot CLI to load project-level configuration. When working
from this non-Git workspace root, register it in the user configuration instead:

```powershell
copilot mcp add playwright -- npx -y @playwright/mcp@latest --headless --isolated
```

Browser checks should target the running API's URL to exercise both the frontend
and `/api`.
