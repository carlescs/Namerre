# Namerre

A self-hosted, web-based file renamer. The application is currently a scaffold;
file-renaming features and application API endpoints are not yet implemented.

## Backend architecture

| Project | Responsibility | Project dependencies |
| --- | --- | --- |
| `Namerre.Domain` | Domain entities, value objects and business rules | None |
| `Namerre.Application` | Use cases, requests, handlers, DTOs and infrastructure interfaces | Domain |
| `Namerre.Infrastructure` | File-system, persistence and other external adapters | Application |
| `Namerre.Api` | HTTP controllers, composition root and Angular hosting | Application, Infrastructure |
| `Namerre.Api.Tests` | Mediator registration and HTTP integration coverage | API |
| `Namerre.Api.UnitTests` | Isolated controller tests using xUnit and NSubstitute | API |
| `Namerre.Application.UnitTests` | Handler and DTO unit tests using xUnit, with NSubstitute available for dependencies | Application |

Dependencies point inward. Domain has no framework dependencies; Application
must not reference Infrastructure or ASP.NET Core. Define interfaces needed by
use cases in Application and implement them in Infrastructure. Register concrete
adapters from the API composition root when they are introduced. Domain and
Infrastructure intentionally contain no placeholder business types or services.

Organize future commands and queries by feature in
Application; keep HTTP concerns in the API.

## CMediator conventions and automation

The backend uses [CMediator](https://github.com/carlescs/CMediator) v1.1.3:

- Requests implement `CMediator.Abstractions.IRequest<TResponse>` and are sealed.
- Handlers implement `IRequestHandler<TRequest, TResponse>` and are internal sealed
  classes. Keep requests and handlers in Application together: discovery is
  limited to the compilation containing the generator.
- Application references the runtime, generator/analyzer and code-fix packages.
  The upstream code-fix package is named **`CMediato.CodeFixes`** (without the
  second `r`); this matches its project/package name at v1.1.3.
- `AddApplication()` calls `AddInProcessMediator()` and the source-generated
  `AddGeneratedMediator()`. Handlers, middleware and dispatchers are registered
  automatically with scoped lifetimes; do not add manual handler registrations
  or runtime assembly scanning.
- The generator emits into the assembly-name namespace, `Namerre.Application`.
  It belongs in Application, not just the API, so it can discover internal handlers.
- `MED001`, `MED002`, `MED003` and `MED005` are build errors in Application,
  enforcing sealed handlers, non-public handlers, sealed requests and matching
  handlers. The code-fix package provides IDE fixes.
- Future middleware can implement `IRequestMiddleware<TRequest, TResponse>`,
  including open generic middleware. It is discovered automatically. Use
  `[MiddlewareOrder(n)]` when ordering matters: lower numbers execute first;
  unspecified order is `0`.
- Pass the HTTP cancellation token through `ISender.Send` to the handler and
  any external operations.

CMediator's upstream CI builds and tests every change and publishes NuGet packages
to GitHub Packages on `v*` tags. Package versions here are pinned; updates should
follow a published upstream tag, not its development branch.

## Restore, build and test

Install the .NET 10 SDK and the frontend's declared Node/npm toolchain.
`nuget.config` maps CMediator packages exclusively to the `carlescs` GitHub
Packages feed; other packages come from nuget.org.

Before the first restore, provide GitHub Packages credentials through the
`NuGetPackageSourceCredentials_Github` environment variable (or an existing
NuGet credential configuration for the source named `Github`). Its format is:

```text
Username=YOUR_GITHUB_USERNAME;Password=YOUR_TOKEN;ValidAuthenticationTypes=Basic
```

Use a GitHub personal access token (classic) with `read:packages` and permission
to read the CMediator packages. Set it through your local secret management;
never commit credentials or place them in build arguments.

From the workspace root:

```powershell
npm ci --prefix .\Namerre.Front
dotnet build .\Namerre.slnx
dotnet test .\Namerre.slnx --no-build
dotnet run --project .\Namerre.Api\Namerre.Api.csproj --launch-profile https
```

The API serves Angular at `https://localhost:7151` and API endpoints under `/api`.
Normal builds and publishes also build Angular into `Namerre.Api\wwwroot`.
Use `/p:BuildFrontend=false` only when the frontend assets have already been built.

Unit-test projects use xUnit and NSubstitute. Application exposes its internal
handlers only to `Namerre.Application.UnitTests` through `InternalsVisibleTo`;
handlers remain internal sealed. Keep unit tests isolated from HTTP hosting and
external services; use NSubstitute for collaborators rather than mocking the
class being tested. `Namerre.Api.Tests` remains the integration-test project.
Domain and Infrastructure unit-test projects can be added when those layers have
behavior to cover.

To run only unit tests after building:

```powershell
dotnet test .\Namerre.Api.UnitTests\Namerre.Api.UnitTests.csproj --no-build
dotnet test .\Namerre.Application.UnitTests\Namerre.Application.UnitTests.csproj --no-build
```

## Docker and CI

With the same credentials environment variable set, pass it as a BuildKit secret:

```powershell
docker build -f .\Namerre.Api\Dockerfile -t namerre --secret id=github_packages_credentials,env=NuGetPackageSourceCredentials_Github .
docker run --rm -p 8080:8080 namerre
```

The app is available at `http://localhost:8080`. The secret is mounted only for
restore and is not stored in an image layer.

Configure the repository Actions secret `CMEDIATOR_PACKAGES_TOKEN` with a token
that can read the upstream packages. The backend and Docker workflows use it for
restore. Fork pull requests cannot access this secret and require a trusted run
with package access; do not expose package credentials to untrusted code.