# Repository instructions

## Project purpose

Namerre is a web-based file renamer intended to help users maintain files in a
self-hosted environment. The Angular frontend is the browser interface, and the
ASP.NET Core API hosts the application and will handle file-renaming operations.
Keep future functionality focused on this self-hosted file-maintenance purpose.

The current code is an application scaffold; file-renaming features are not yet
implemented. The weather forecast endpoint is starter code, not project functionality.

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
`Namerre.Api\Namerre.http` contains a sample `/api/weatherforecast` request.

Build and run the complete app in Docker from the workspace root:

```powershell
docker build -f .\Namerre.Api\Dockerfile -t namerre .
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
| Check source formatting with installed Prettier | `npx prettier --check "src/**/*.{ts,html,scss}"` |
| Generate a component | `npm run ng -- generate component component-name` |

Tests use Angular's `@angular/build:unit-test` builder with Vitest and jsdom.
Use the Angular test command rather than invoking Vitest directly so Angular
components and templates are compiled. Test globals come from `vitest/globals`
in `tsconfig.spec.json`.

## Architecture

- `Namerre.slnx` includes only `Namerre.Api`; its build target builds the frontend.
  The workspace root is not a Git repository; `Namerre.Front` has its own Git
  repository. These instructions are at the workspace root, outside that repository.
- The frontend is a client-rendered standalone Angular application:
  `src\main.ts` bootstraps `App` using `appConfig`. Application-wide providers
  belong in `src\app\app.config.ts`, and routes in `src\app\app.routes.ts`.
  `App` imports `RouterOutlet`; the route list is currently empty and the page
  is still Angular's starter template.
- The API uses ASP.NET Core controllers. `Program.cs` registers controllers
  and OpenAPI, exposes OpenAPI only in Development, enables HTTPS redirection
  and authorization middleware, and serves default/static files from `wwwroot`.
  Controllers use an `api/` prefix in their route attributes so routing and
  generated OpenAPI paths agree.
- `WeatherForecastController` serves `GET /api/WeatherForecast`, returning five
  generated `WeatherForecast` values. This is currently the API's only
  application endpoint. Use same-origin `/api/...` URLs for frontend API calls
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
- Frontend formatting is defined by `.editorconfig` and `.prettierrc`:
  two-space indentation, single-quoted TypeScript strings, a 100-column
  Prettier width, and Angular parsing for HTML.
- TypeScript enables `noImplicitOverride`, `noPropertyAccessFromIndexSignature`,
  `noImplicitReturns`, and `noFallthroughCasesInSwitch`; Angular enables strict
  injection parameters and input access modifiers. Preserve these compiler checks.
- API namespaces currently use `Namerre` and `Namerre.Controllers`, despite the
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
