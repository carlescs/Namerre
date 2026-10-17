# NamerreFront

Namerre is a web-based file renamer intended to help users maintain files in a
self-hosted environment. Its Angular frontend provides the browser interface,
while the ASP.NET Core API hosts the application and will handle file-renaming
operations.

The project currently contains the application scaffold; file-renaming features
are not yet implemented.

This project was generated using [Angular CLI](https://github.com/angular/angular-cli) version 22.2.3.

## Hosting with the API

From the parent workspace directory, install frontend dependencies and start the complete app:

```powershell
npm ci --prefix .\Namerre.Front
dotnet run --project .\Namerre.Api\Namerre.Api.csproj --launch-profile https
```

Open `https://localhost:7151/` for Angular. API endpoints share the same origin
under `/api` when implemented. Normal API builds and publishes
build Angular automatically. Unmatched frontend routes serve `index.html`;
unmatched API routes return 404.

To publish or build a container, run from the parent workspace directory:

```powershell
dotnet publish .\Namerre.Api\Namerre.Api.csproj -c Release -o .\artifacts\publish
docker build -f .\Namerre.Api\Dockerfile -t namerre .
docker run --rm -p 8080:8080 namerre
```

The container serves Angular and the API at `http://localhost:8080`.

## Development server

To start a local development server, run:

```bash
ng serve
```

Once the server is running, open your browser and navigate to `http://localhost:4200/`. The application will automatically reload whenever you modify any of the source files.

This is the standalone frontend dev server. Use the API-hosted URL above for
same-origin frontend/API integration; no dev-server proxy is configured.

## Code scaffolding

Angular CLI includes powerful code scaffolding tools. To generate a new component, run:

```bash
ng generate component component-name
```

For a complete list of available schematics (such as `components`, `directives`, or `pipes`), run:

```bash
ng generate --help
```

## Building

To build the project run:

```bash
ng build
```

This compiles the project into `..\Namerre.Api\wwwroot\`, where ASP.NET Core
serves it. This directory is generated and cleared on rebuild; keep source assets
in `public/`. By default, the production build optimizes the application for
performance and speed.

## Running unit tests

To execute unit tests with the [Vitest](https://vitest.dev/) test runner, use the following command:

```bash
ng test
```

## Running end-to-end tests

For end-to-end (e2e) testing, run:

```bash
ng e2e
```

Angular CLI does not come with an end-to-end testing framework by default. You can choose one that suits your needs.

## Additional Resources

For more information on using the Angular CLI, including detailed command references, visit the [Angular CLI Overview and Command Reference](https://angular.dev/tools/cli) page.
