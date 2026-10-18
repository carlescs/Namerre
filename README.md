# Namerre

A self-hosted, web-based file renamer with user-configured transformation pipelines,
previews, persistent run history, and selected-file rollback.

## Renaming workflow

Add files using the existing folder picker, optionally including subfolders.
The collection can contain files from multiple folders. Start with an empty plugin
stack and add as many independently configured instances as needed: text replacement,
regular-expression replacement, case change, or TMDB resolution. Repeated plugin
types are allowed. Move instances up or down to choose the execution order; the
catalog order does not determine execution order.

Plugins run on the server. Each enabled instance receives the preceding instance's
output, and the preview shows the intermediate names and final relative destination.
Text, regex, and case steps preserve the extension and folder. Regex replacements
use .NET syntax, including `$1` and `${name}` captures. TMDB matches require explicit
confirmation and can optionally change the destination folder through templates.
No files or directories are changed by previews.

Prepare the approved configuration to hash the source files and store an immutable
plan. Hashing reads the full contents of each file and can take substantial disk I/O
for large media collections. Review the prepared paths, then confirm execution.
Changing the collection, plugin options, order, or metadata selections invalidates
the previous preparation. Accepted jobs continue independently of the browser;
their progress and outcome are available in persistent history.

Existing targets and duplicate destinations block execution. Files can move within
the configured root on the same filesystem, including swaps and case-only renames.
Missing destination folders may be created. There is no overwrite, cross-filesystem
copy-and-delete fallback, directory renaming, or automatic companion-file discovery.
Folders created for a run are retained after rollback and recovery: a path journal
cannot prove that a directory at that path is still the one the app created.

Select individual files in a saved run to preview rollback. Rollback checks the
expected current path and original content hash, never overwrites an occupied
original path, and reports unsafe selections. A swap may require selecting all
participating files. A hash proves content equivalence, not unique physical identity
when different files contain identical bytes.

Runs are journaled, not whole-batch atomic: a failure can leave some files renamed
or in recorded temporary staging paths. Inspect the run and use explicit recovery.
Interrupted mutations are not blindly resumed after a restart. Do not delete staging
files or history to clear an error; that can destroy recovery evidence.

## Server folder browsing

Set `FileBrowser:RootPath` in the server configuration, or use the environment
variable `FileBrowser__RootPath`, to an existing folder the API process can read.
Relative roots resolve from the API content root. No folders are exposed until
this setting is configured.

```powershell
$env:FileBrowser__RootPath = 'D:\Media'
dotnet run --project .\Namerre.Api\Namerre.Api.csproj --launch-profile https
```

Open the folder picker and confirm a folder to add its immediate files to the
renaming collection. Subsequent selections add files without duplicating
server-relative paths. The collection displays source folders, sizes and
modification dates and remains in memory while the renamer is open.
Navigation stays within the configured root; symbolic links
and junctions are excluded. `GET /api/folders?path=Movies` accepts a relative
folder path; omit `path` to list the root. This endpoint lists metadata only,
not file contents.

For browsing only, Docker can mount the desired server folder read-only and set
`FileBrowser__RootPath` to its path **inside the container**. Expose only folders
you intend users to see, and restrict network access to trusted users: the app
does not implement authentication. Renaming requires a writable media mount.
The configured folder tree must not be writable by untrusted processes while
browsing or renaming.

## Persistent storage and deployment

Set `Rename:DataPath` (environment variable `Rename__DataPath`) to a writable data
directory **separate from the browsable root, with neither directory containing the
other**. This holds SQLite history, prepared plans,
the durable job queue, and journals. Keep it persistent across upgrades and restarts.
Data access uses EF Core's SQLite provider with versioned EF migrations in
Infrastructure; database concerns stay out of Domain and Application.
When `Rename:DataPath` is configured, pending migrations run during API startup,
before requests are accepted. Migration or storage-initialization failures prevent
startup. Leaving the setting empty keeps browsing and read-only previews available
without a database.
The API user needs read/write access to this directory and rename/create-directory
permissions within the media root. Missing or unavailable durable storage must not
be worked around by renaming without a journal.

Run only one app instance against the root and data directory. Keep the configured
root mapping stable across restarts; do not remap an existing history database to an
unrelated media tree. A mount within the root can be a different filesystem and is
not a supported move destination. Avoid external writers during preparation,
execution, rollback, and recovery: hash/path rechecks cannot eliminate races with
another process replacing files or directories.

Mutation support is currently Windows and Linux. Windows checks per-directory case
semantics. Linux currently supports ext4 and btrfs without casefold, tmpfs, and
overlayfs; other filesystems (including XFS, ZFS, CIFS, and FUSE) fail closed rather
than guess their naming semantics. This can exclude some NAS mounts and Docker
Desktop host bind mounts. Use a supported media filesystem; a working folder listing
does not establish that mutations are supported.

Back up the data directory together with the media tree while the app is stopped
and no jobs are mutating files. Copying only the SQLite main file while it is in use
is not a reliable backup. Restoring history without the matching media state can
make rollback unsafe. Keep interrupted-operation evidence until recovery is resolved.

The container runs as its non-root application user. Ensure writable media and data
mounts are owned by, or grant access to, that user's numeric UID. Do not grant broad
host filesystem access or use privileged mode to bypass mount permissions.
For example, after building the image:

```powershell
docker volume create namerre-data
docker run --rm -p 8080:8080 `
  --mount type=bind,source=D:\Media,target=/media `
  --mount type=volume,source=namerre-data,target=/data `
  -e FileBrowser__RootPath=/media `
  -e Rename__DataPath=/data `
  namerre
```

The host-bind example requires a supported underlying filesystem; on Docker Desktop,
use a native Linux filesystem/volume when the Windows host mount is rejected.
Use a disposable media directory first. Removing the container must not remove
its data volume. Restrict access to trusted users or put authentication at a trusted
reverse proxy; exposing this mutation-capable API to an untrusted network is unsafe.

## TMDB metadata

Configure `Tmdb:ReadAccessToken` through server secrets or the
`Tmdb__ReadAccessToken` environment variable. Never put it in Angular configuration,
pipeline options, run snapshots, Docker build arguments, or committed files.
The token is sent from the backend to TMDB in an authorization header.
Local text/regex/case transformations do not require TMDB.

Search for a movie or TV show, correct the query when necessary, and confirm the
intended match. Episodes require a confirmed show, season, and episode. Metadata
defaults to English; names default to `Movie Title (Year)` or
`Show Title - S01E02 - Episode Title`, preserving the extension. Folder changes are
optional and off by default. Confirmed metadata is snapshotted for the run so a
later TMDB change or outage does not alter an approved execution or rollback.

TMDB's non-commercial API requires attribution; commercial use requires appropriate
licensing. See the [TMDB FAQ](https://developer.themoviedb.org/docs/faq) and
[application authentication](https://developer.themoviedb.org/docs/authentication-application).
This product uses the TMDB API but is not endorsed or certified by TMDB.

## Workload limits

A run accepts up to 200 files and 32 plugin instances. These bounds do not prescribe
which types to use or their order; repeated instances are independently configured.
Larger collections can still be browsed, but must be split before preparing a run.
The durable queue accepts up to 32 queued jobs and reports overload explicitly.
Up to four previews and four TMDB calls can run concurrently.
Regex matching has a 100 ms timeout; TMDB HTTP requests have a 15-second timeout.
History is paginated (at most 100 runs per page) and is not automatically pruned.

## Backend architecture

| Project | Responsibility | Project dependencies |
| --- | --- | --- |
| `Namerre.Domain` | Domain entities, value objects and business rules | None |
| `Namerre.Application` | Use cases, requests, handlers, DTOs and infrastructure interfaces | Domain |
| `Namerre.Infrastructure` | Bounded filesystem access, SQLite persistence, durable execution and TMDB HTTP adapters | Application |
| `Namerre.Api` | HTTP controllers, composition root and Angular hosting | Application, Infrastructure |
| `Namerre.Api.Tests` | Mediator registration and HTTP integration coverage | API |
| `Namerre.Api.UnitTests` | Isolated controller tests using xUnit and NSubstitute | API |
| `Namerre.Application.UnitTests` | Handler and DTO unit tests using xUnit, with NSubstitute available for dependencies | Application |

Dependencies point inward. Domain has no framework dependencies; Application
must not reference Infrastructure or ASP.NET Core. Define interfaces needed by
use cases in Application and implement them in Infrastructure. Register concrete
adapters and hosted workers from the API composition root. Plugin catalog order
is unrelated to execution order: requests carry the user's ordered plugin instances,
and approved plans retain that exact sequence and metadata snapshot.

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
Backend CI runs on both Windows and Linux so filesystem-sensitive behavior is not
validated only through platform-independent string comparisons.