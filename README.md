<p align="center">
  <img src="Namerre.Front/public/favicon.svg" alt="Namerre logo" width="96" height="96">
</p>

<h1 align="center">Namerre</h1>

<p align="center">
  A self-hosted, web-based file renamer with user-configured transformation pipelines,
  previews, persistent run history, and selected-file rollback.
</p>

## Getting started

These steps run the complete app locally from source. For containers, see
[Persistent storage and deployment](#persistent-storage-and-deployment), including
the required browser-facing HTTPS and reverse-proxy configuration.

1. Install the **.NET 10 SDK**, a Node.js version compatible with Angular 22, and
   **npm 11.13.0**. Clone this repository and open a terminal at its root.
2. Configure access to the CMediator GitHub Packages feed before the first build.
   Follow [Restore, build and test](#restore-build-and-test) to provide a GitHub
   token with `read:packages` through your local secret-management mechanism.
   This package-feed token is unrelated to the installation token used below.
3. Choose an existing media folder and a persistent data directory. The API needs
   read/write access to both; neither directory may contain the other. Namerre
   creates the data directory and initializes its SQLite database on startup.
   Start with disposable media files. Leave `Namerre:Authentication:Username` and
   `Namerre:Authentication:PasswordHash` unset to use the installation assistant.

Run the following in PowerShell, replacing the example paths with your own:

```powershell
npm ci --prefix .\Namerre.Front
dotnet dev-certs https --trust
$env:Namerre__RootPath = 'D:\Media'
$env:Namerre__DataPath = 'D:\NamerreData'
dotnet run --project .\Namerre.Api\Namerre.Api.csproj --launch-profile https
```

The API build also builds and hosts Angular; do not start a separate frontend
server. Keep this terminal open.

1. Open **https://localhost:7151**. Copy the one-time installation token printed
   in the server terminal into the installation assistant.
2. Choose an administrator username and a strong, unique password of at least 12
   characters, confirm the password, and select **Create administrator**. Then sign in with that account.
   There are no default credentials.
3. Select **Choose folder** to add files, then **Configure renaming and history**
   to build a pipeline. Preview and review the prepared changes before confirming
   execution. TMDB credentials are optional and are not needed for local
   text-replacement, regex, or case-change steps.

The administrator account and renaming history are saved in the data directory.
Keep that directory and the media-root mapping unchanged across restarts, and
back them up together while the app is stopped. The environment variables above
apply to the current terminal; configure the same paths for subsequent runs.
After installation, restarting the app shows sign-in rather than setup.

## Configuration

Application settings live in one `Namerre` section in `Namerre.Api\appsettings.json`:

```json
{
  "Namerre": {
    "RootPath": "",
    "DataPath": "",
    "Authentication": {
      "Username": "",
      "PasswordHash": "",
      "Oidc": {
        "Authority": "",
        "ClientId": "",
        "ClientSecret": "",
        "RequiredRole": "",
        "DisplayName": "Single sign-on",
        "RoleClaim": "realm_access.roles",
        "UsernameClaim": "preferred_username",
        "Scopes": [],
        "RequireHttpsMetadata": true
      }
    },
    "Tmdb": {
      "ReadAccessToken": "",
      "Language": "en"
    },
    "ReverseProxy": {
      "KnownProxies": [],
      "KnownNetworks": []
    },
    "Telemetry": {
      "OtlpEndpoint": "",
      "Protocol": "Grpc",
      "Headers": "",
      "ServiceName": "namerre"
    }
  }
}
```

The backend binds this section to `NamerreOptions` and consumes it through
`IOptions<NamerreOptions>`. Logging, allowed hosts, and other ASP.NET Core settings
remain at the top level. Keep credentials in user secrets or protected runtime
configuration, not in committed settings.

When upgrading, move the former `FileBrowser:RootPath` and `Rename:DataPath` values
to `Namerre:RootPath` and `Namerre:DataPath`, and move `Authentication` and `Tmdb`
under `Namerre`. Update environment variables and user secrets accordingly;
the old top-level application sections are no longer read.

## Telemetry (optional)

Namerre can export OpenTelemetry traces, metrics and logs over OTLP to an
OpenTelemetry Collector (or any OTLP-compatible backend). It is disabled unless
`Namerre:Telemetry:OtlpEndpoint` is set; with no endpoint, no telemetry is collected.

| Setting | Default | Description |
| --- | --- | --- |
| `OtlpEndpoint` | empty (disabled) | Absolute `http`/`https` collector URL, e.g. `http://otel-collector:4317`. |
| `Protocol` | `Grpc` | `Grpc` (usually port 4317) or `HttpProtobuf` (usually port 4318). For `HttpProtobuf`, give the base URL; `/v1/traces`, `/v1/metrics` and `/v1/logs` are appended. |
| `Headers` | empty | Optional exporter headers as `key1=value1,key2=value2` (for example collector authentication). Treat as a secret. |
| `ServiceName` | `namerre` | Reported as `service.name`. |

Exported signals: ASP.NET Core and outgoing `HttpClient` (TMDB) traces and metrics,
.NET runtime metrics, and application logs. Query strings are redacted by the
instrumentation, but request paths and log messages may contain media file and folder
names, so send telemetry only to a collector you trust. Invalid settings fail startup.

```powershell
$env:Namerre__Telemetry__OtlpEndpoint = 'http://otel-collector:4317'
```

## Authentication (required)

Namerre has one administrator account, no registration, and no default credentials.
When no account has been configured, the browser displays an installation assistant.
First configure `Namerre:RootPath` and `Namerre:DataPath`: the administrator is
stored in the **existing SQLite renaming/history database**, not a credentials file.
For example, with an existing media folder:

```powershell
$env:Namerre__RootPath = 'D:\Media'
$env:Namerre__DataPath = 'D:\NamerreData'
dotnet run --project .\Namerre.Api\Namerre.Api.csproj --launch-profile https
```

Open `https://localhost:7151`, copy the one-time setup token printed in the server
console, and choose an administrator username and a password of at least 12
characters. Confirm the password and select **Create administrator**, then sign in.
Only the username and salted password hash are saved in the database's
`Administrators` table. The database enforces a single administrator. Setup cannot
replace an existing account, requires HTTPS and CSRF protection, and shares the
login attempt limit. Protect console/log access: the token authorizes first-time
installation. It changes on each restart until setup succeeds, is never returned
by the API, and becomes unusable after the account is created.

To change the password of an installed administrator, sign in and open **Account**.
Enter the current password and the new password twice, then select **Change password**.
The new hash replaces the stored one; your current browser session stays signed in and
every other session is signed out. Password changes require the current password, CSRF
protection, and a new password of at least 12 characters, and share the login attempt limit.
An existing shorter password keeps working until it is changed.

Keep `Namerre:DataPath` persistent across restarts and container replacement; changing
or deleting this database also loses the administrator account and renaming history.
Corrupt or inaccessible stored credentials fail startup rather than reopening setup.
The media root and database must be available for database-backed sign-in.

Existing server-configured credentials also remain supported and skip the assistant.
Both `Namerre:Authentication:Username` and `Namerre:Authentication:PasswordHash` must be set together;
a partial or invalid configuration fails startup. To generate a hash manually,
use the API's interactive command (the password is hidden and is not passed as a
command-line argument):

```powershell
dotnet run --project .\Namerre.Api\Namerre.Api.csproj --no-launch-profile -- --hash-password
```

Use a long, unique password, preferably generated by a password manager; the command
requires at least 12 characters. Copy the resulting hash into server configuration, not the password.
For local development, store the username and hash in .NET user secrets:

```powershell
dotnet user-secrets set 'Namerre:Authentication:Username' 'admin' --project .\Namerre.Api
dotnet user-secrets set 'Namerre:Authentication:PasswordHash' '<generated-hash>' --project .\Namerre.Api
dotnet run --project .\Namerre.Api\Namerre.Api.csproj --launch-profile https
```

For deployment, supply `Namerre__Authentication__Username` and `Namerre__Authentication__PasswordHash`
through the server's secret/configuration mechanism if you want to bypass browser
installation. These settings take precedence over a saved administrator. Never commit
credentials or include them in Angular assets or Docker build arguments. Changing either setting
and restarting the app invalidates existing sessions. To recover access, generate a
new hash, update the server configuration, and restart. Server-configured passwords cannot be
changed from the **Account** page.

**Browser-facing HTTPS is required**, but Traefik can terminate TLS and forward
requests to Namerre over **HTTP on port 8080**. No backend certificate is needed.
Configure `Namerre:ReverseProxy:KnownProxies` with Traefik's IP address, or
`Namerre:ReverseProxy:KnownNetworks` with a dedicated proxy network's CIDR.
For example, set `Namerre__ReverseProxy__KnownProxies__0=172.30.0.2`.
Namerre processes `X-Forwarded-Proto` and `X-Forwarded-For` before HTTPS checks,
redirects, and authentication. Traefik must send `X-Forwarded-Proto: https`.
Only configured proxies/networks and ASP.NET Core's default loopback proxies are
trusted. Keep the backend port private and trust only proxy-controlled networks;
do not trust arbitrary clients or enable unrestricted forwarded-header trust.
Secure cookies and CSRF protection remain enabled.
Live progress uses a SignalR WebSocket at `/api/hubs/rename`; Traefik forwards WebSocket
upgrades by default. If a proxy blocks them, SignalR falls back to Server-Sent Events or
long polling, and the browser keeps refreshing active jobs every second while it cannot connect.
For direct local access, use the `https` launch profile (`https://localhost:7151`).
The separate Angular development server is not configured for same-origin API access;
use the API-hosted frontend.

Sign in through the browser and use **Sign out** when finished. Passwords are hashed
using ASP.NET Core's PBKDF2-HMAC-SHA512 password hasher (210,000 iterations). Authentication
uses an encrypted, HttpOnly, Secure, SameSite=Strict cookie, not browser storage or
bearer tokens. Sessions expire after eight hours without sliding renewal, and are
not persisted as remember-me cookies. When `Namerre:DataPath` is set, the ASP.NET Core
Data Protection keys that encrypt session and antiforgery cookies are stored in its
`keys` folder, so sessions survive restarts and container replacement. Protect that
folder like the database: anyone who can read the keys can forge a session cookie.
Without `Namerre:DataPath`, ASP.NET Core's default key location is used.
Signing out removes the browser cookie; a stolen copy remains usable until its
expiry or a credential change. Keep the server, browser, and TLS keys protected.

All controllers and development OpenAPI require authentication, except session
discovery, first-time setup, login, and the single sign-on redirect and callback. The public HTML/assets contain no server file
information.
API responses are not cached. State-changing requests (including login and logout)
require an antiforgery cookie and `X-XSRF-TOKEN` header; the Angular client manages
these automatically. Login, setup and password changes are limited to five attempts
per minute for each client address (IPv6 clients are grouped by their /64 network) and
60 attempts per minute across all clients. One noisy client therefore cannot lock the
administrator out, while guessing from many addresses stays bounded. Behind a reverse
proxy, configure trusted proxies (see below) so the limit applies to real client
addresses rather than the proxy. Even so, keep network access restricted to trusted
users or a VPN rather than exposing the app directly to the public internet.

Every response carries browser hardening headers: a Content Security Policy that allows
only same-origin scripts plus the exact inline scripts of the served `index.html` (by
SHA-256 hash, recomputed when the file changes), forbids framing and plugins, and
restricts connections, images and fonts to the app's own origin; plus
`X-Content-Type-Options: nosniff`, `X-Frame-Options: DENY`, `Referrer-Policy: no-referrer`,
`Cross-Origin-Opener-Policy: same-origin` and a restrictive `Permissions-Policy`.

### Single sign-on with OpenID Connect (optional)

Namerre can also sign users in through an OpenID Connect provider such as Keycloak.
It is off while `Namerre:Authentication:Oidc:Authority` is empty. When enabled, the
login (and installation) page shows a **Sign in with _DisplayName_** button next to
the local administrator form; the local account keeps working. Every user who holds
`RequiredRole` acts as the administrator, so the role is mandatory: grant it only to
people you would give the administrator password.

| Setting | Default | Description |
| --- | --- | --- |
| `Authority` | empty | Issuer URL, for Keycloak `https://keycloak.example.com/realms/<realm>`. Must be HTTPS. |
| `ClientId` | empty | Required. |
| `ClientSecret` | empty | Secret of a confidential client (recommended). Treat as a secret. Omit for a public client; PKCE is always used. |
| `RequiredRole` | empty | Required. Role (or group) the ID token must contain. |
| `RoleClaim` | `realm_access.roles` | ID token claim holding the roles. Dots navigate nested JSON, e.g. `resource_access.namerre.roles` for client roles, or a flat claim such as `groups`. |
| `UsernameClaim` | `preferred_username` | Claim shown as the signed-in user; falls back to `sub`. |
| `DisplayName` | `Single sign-on` | Button label (1-64 characters). |
| `Scopes` | `[]` | Scopes requested in addition to `openid` and `profile`. |
| `RequireHttpsMetadata` | `true` | Set to `false` only for a development provider on plain HTTP. |

Keycloak setup:

1. In your realm, create an **OpenID Connect** client (for example `namerre`) with
   **Client authentication** on and only the **Standard flow** enabled. Set
   **Valid redirect URIs** to `https://<namerre-host>/api/auth/oidc/callback` and
   **Web origins** to `https://<namerre-host>`, and **Valid post logout redirect URIs** to
   `https://<namerre-host>/`. Copy the client secret from **Credentials**.
2. Create a realm role (for example `namerre-admin`) and assign it to the allowed users
   or a group.
3. Keycloak does not put roles in the ID token by default. Under **Client scopes** →
   `roles` → **Mappers** → **realm roles**, enable **Add to ID token** (or add a
   dedicated "User Realm Role" mapper to the client's dedicated scope with that option).
4. Configure Namerre, for example:

```powershell
$env:Namerre__Authentication__Oidc__Authority = 'https://keycloak.example.com/realms/media'
$env:Namerre__Authentication__Oidc__ClientId = 'namerre'
$env:Namerre__Authentication__Oidc__ClientSecret = '<client-secret>'
$env:Namerre__Authentication__Oidc__RequiredRole = 'namerre-admin'
$env:Namerre__Authentication__Oidc__DisplayName = 'Keycloak'
```

Namerre uses the authorization code flow with PKCE, validates the ID token and
discards the access and refresh tokens after sign-in: it keeps the subject, username
and ID token (used only as the sign-out hint) in the same encrypted eight-hour,
SameSite=Strict session cookie as local sign-in. Users without
the role are rejected. Changing `Authority`, `ClientId`, `RequiredRole`, `RoleClaim` or
`UsernameClaim` invalidates existing single sign-on sessions after a restart; removing
the role in Keycloak takes effect at the next sign-in. **Sign out** of a single sign-on
session also ends the Keycloak session: Namerre ends its own session, then sends the
browser to the provider's `end_session_endpoint`, which returns to Namerre afterwards.
If the provider advertises no such endpoint or is unreachable, only the Namerre session
ends, and the button may sign straight back in while Keycloak still remembers the user. **Change password** applies to the local administrator only and is
not offered in single sign-on sessions. Behind a reverse proxy, configure trusted proxies
(see above) so the callback URL uses the public `https` host. Incomplete settings fail
startup; an unreachable provider shows an error on the login page.

## Renaming workflow

Add files using the existing folder picker, optionally including subfolders.
Hidden and system entries (names starting with `.` or `@`, such as `.DS_Store` and
Synology's `@eaDir`, plus `Thumbs.db`, `desktop.ini` and recycle-bin folders) are
skipped unless **Show hidden files and folders** is ticked in the picker.
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
for large media collections. The web UI therefore defaults to **Quick check** on the
preview step (`verifyContent: false` on `POST /api/rename-plans`; the API itself still
defaults to hashing); untick it to hash contents. With Quick check, preparation records
each file's size and last-modified time instead of a content hash, and execution,
rollback and recovery compare those values rather than re-reading the files. This is
much faster but will not notice a content change that keeps both size and timestamp,
and identical-looking copies are more likely to make recovery ambiguous.
Review the prepared paths, then confirm execution.
Changing the collection, plugin options, order, or metadata selections invalidates
the previous preparation. Accepted jobs continue independently of the browser;
their progress and outcome are available in persistent history.
Open pages follow progress live: the server pushes a short change signal (`renameChanged`
with `kind`, `id` and `status`) over SignalR after every durable plan or run update, and the
browser re-reads the record from the REST API. The signal carries no file paths. The hub
requires the same session cookie as the API and closes when the session expires; a fallback
refresh every 15 seconds covers any missed signal.

Existing targets and duplicate destinations block execution. Files can move within
the configured root on the same filesystem, including swaps and case-only renames.
Missing destination folders may be created. There is no overwrite, cross-filesystem
copy-and-delete fallback, directory renaming, or automatic companion-file discovery.
Folders created for a run are retained after rollback and recovery: a path journal
cannot prove that a directory at that path is still the one the app created.
To tidy up, tick **Delete folders left empty** on the preview step
(`deleteEmptyFolders: true` on `POST /api/rename-plans`). Once every file of the run is
placed, each folder a moved file came from is removed if it is now empty, deepest first,
so parents emptied in turn go too. The configured root is never removed, and a folder
that still holds anything, even an empty subfolder, stays.
Each removal is journaled as `DeleteDirectory`. Cleanup is best effort: a folder that
cannot be removed is journaled as `Failed` and the run still completes. Rollbacks of
such a run inherit the option by default, so folders the rename created are removed again
when rolling back empties them. The undo review lets you change that for one rollback
(`deleteEmptyFolders` on `POST /api/rename-runs/{id}/rollback`; omitted inherits the
run's choice). Missing original folders are recreated on rollback and recovery.

Select individual files in a saved run to preview rollback. Rollback checks the
expected current path and original content hash, never overwrites an occupied
original path, and reports unsafe selections. A swap may require selecting all
participating files. While a check runs, the page shows how many selected files have
been verified (`GET /api/rename-runs/{id}/rollback-preview/{operationId}`). A hash proves content equivalence, not unique physical identity
when different files contain identical bytes. Each rollback is saved as its own run:
its details link back to the original run, and the original run's details list every
rollback run created from it (`rollbackRuns` on `GET /api/rename-runs/{id}`).

Settled runs (completed or recovered) can be deleted from history, either from the
list or the run details, after an inline confirmation (`DELETE /api/rename-runs/{id}`).
Deleting a rename run also deletes its rollback runs and its prepared plan; deleting a
rollback run keeps the original run. Files on disk are never touched, but deleted
history can no longer be used for rollback. Runs awaiting recovery, or with a queued or
unrecovered rollback, cannot be deleted.

To delete several records at once, tick them in the history list and choose **Delete
selected**. The selection is kept while moving between pages. Once every deletable run on
a page is ticked, **Select all deletable runs on every page** selects the newest 1000
deletable runs: settled runs without a pending or unrecovered rollback
(`GET /api/rename-run-deletions/candidates`, which returns `ids` and `hasMore`). A
selection larger than 1000 runs must be trimmed before it can be deleted.
Deletion uses `POST /api/rename-run-deletions` with `{ "runIds": [...] }` (1–1000 distinct
IDs) and returns every deleted run ID, rollbacks included. The batch is all-or-nothing: if
any selected run cannot be deleted, nothing is. Runs that were already deleted, for example
from another tab, are skipped.

Runs are journaled, not whole-batch atomic: a failure can leave some files renamed
or in recorded temporary staging paths. Inspect the run and use explicit recovery.
Interrupted mutations are not blindly resumed after a restart. Do not delete staging
files or history to clear an error; that can destroy recovery evidence.

## Server folder browsing

Set `Namerre:RootPath` in the server configuration, or use the environment
variable `Namerre__RootPath`, to an existing folder the API process can read.
Relative roots resolve from the API content root. No folders are exposed until
this setting is configured.

```powershell
$env:Namerre__RootPath = 'D:\Media'
dotnet run --project .\Namerre.Api\Namerre.Api.csproj --launch-profile https
```

Open the folder picker and confirm a folder to add its immediate files to the
renaming collection. Subsequent selections add files without duplicating
server-relative paths. The collection displays source folders, sizes and
modification dates. The collection, the renaming rules and the run options are kept in
the browser's local storage, so a reload picks up where you left off; confirmed TMDB
matches are not kept and are matched again. The saved collection is dropped once a rename
starts.
Navigation stays within the configured root; symbolic links
and junctions are excluded. `GET /api/folders?path=Movies` accepts a relative
folder path; omit `path` to list the root. This endpoint lists metadata only,
not file contents.

For browsing only, Docker can mount the desired server folder read-only and set
`Namerre__RootPath` to its path **inside the container**. Expose only folders
you intend the administrator to see, and restrict network access to trusted users.
Renaming requires a writable media mount.
The configured folder tree must not be writable by untrusted processes while
browsing or renaming.

## Persistent storage and deployment

Set `Namerre:DataPath` (environment variable `Namerre__DataPath`) to a writable data
directory **separate from the browsable root, with neither directory containing the
other**. This holds SQLite history, prepared plans,
the durable job queue, journals, and the cookie encryption keys (`keys` folder). Keep it persistent across upgrades and restarts.
Data access uses EF Core's SQLite provider with versioned EF migrations in
Infrastructure; database concerns stay out of Domain and Application.
When `Namerre:DataPath` is configured, pending migrations run during API startup,
before requests are accepted. Migration or storage-initialization failures prevent
startup. Leaving the setting empty keeps browsing and read-only previews available
without a database only when using server-configured authentication credentials.
The API user needs read/write access to this directory and rename/create-directory
permissions within the media root. Missing or unavailable durable storage must not
be worked around by renaming without a journal.

Storage initialization also opens `owner.lock` in the data directory and
`.namerre-owner` in the media root for exclusive read/write access. Even before any
renames, database-backed authentication requires a writable media root for this lock;
a read-only media mount works only without durable storage and with server-configured
authentication. If startup reports `StorageUnavailable`, inspect the failing step
and inner exception in the container logs. Check mount permissions for the application
UID, read-only mounts, and competing app instances. Do not delete the database or lock
files to bypass the error.

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
For a Traefik deployment, attach Namerre to the existing private Docker network
shared with Traefik. The following example assumes that network is named `traefik`
and Traefik has the fixed address `172.30.0.2`; replace both with your actual values.
Traefik's `websecure` entrypoint must already have a valid browser-facing TLS
certificate configured:

```powershell
docker volume create namerre-data
docker run --rm --name namerre --network traefik `
  --mount type=bind,source=D:\Media,target=/media `
  --mount type=volume,source=namerre-data,target=/data `
  -e Namerre__RootPath=/media `
  -e Namerre__DataPath=/data `
  -e ASPNETCORE_HTTP_PORTS=8080 `
  -e Namerre__ReverseProxy__KnownProxies__0=172.30.0.2 `
  --label 'traefik.enable=true' `
  --label 'traefik.docker.network=traefik' `
  --label 'traefik.http.routers.namerre.rule=Host(`namerre.example.com`)' `
  --label 'traefik.http.routers.namerre.entrypoints=websecure' `
  --label 'traefik.http.routers.namerre.tls=true' `
  --label 'traefik.http.services.namerre.loadbalancer.server.port=8080' `
  --label 'traefik.http.services.namerre.loadbalancer.server.scheme=http' `
  namerre
```

Open `https://namerre.example.com` through Traefik. The backend HTTP port is not
published to the host. Docker environment values are visible to host administrators; use
your platform's protected runtime configuration when that is not acceptable.
Complete installation using the token in `docker logs <container>`; the administrator
is saved alongside history in the mounted `/data` database. To skip installation,
also pass your protected `Namerre__Authentication__Username` and `Namerre__Authentication__PasswordHash`
environment values.

The host-bind example requires a supported underlying filesystem; on Docker Desktop,
use a native Linux filesystem/volume when the Windows host mount is rejected.
Use a disposable media directory first. Removing the container must not remove
its data volume. Authentication does not sandbox an administrator or replace network
restrictions; exposing this mutation-capable API to an untrusted network is unsafe.

## TMDB metadata

Configure `Namerre:Tmdb:ReadAccessToken` through server secrets or the
`Namerre__Tmdb__ReadAccessToken` environment variable. Never put it in Angular configuration,
pipeline options, run snapshots, Docker build arguments, or committed files.
The token is sent from the backend to TMDB in an authorization header.
Local text/regex/case transformations do not require TMDB.

Search for a movie or TV show, correct the query when necessary, and confirm the
intended match. Episodes require a confirmed show, season, and episode. Metadata
defaults to English; names default to `Movie Title (Year)` or
`Show Title - S01E02 - Episode Title`, preserving the extension. Folder changes are
optional and off by default. When folder changes are enabled, episodes can additionally
be placed in a season folder inside the show folder (for example
`TV/Show Title (2010)/Season 01/`). The season folder template defaults to
`Season {season}` (two-digit, `Season 01`); use `{seasonNumber}` for the unpadded number
(`Season 1`) or any other single-segment format such as `S{season}`. Movies ignore it. Confirmed metadata is snapshotted for the run so a
later TMDB change or outage does not alter an approved execution or rollback.

The TMDB matching stage appears automatically when an enabled TMDB plugin is added.
Existing matches survive unrelated plugin edits and collection additions; changed
step inputs or TMDB options require confirmation again. Reopening a picker restores
its search and episode fields. A previous show choice can be reused for another file,
but its exact season and episode must still be verified and confirmed separately.
While any file still needs a confirmed match, the stage says how many and can jump to
the first one; a file that should not be renamed can be removed from the collection
from its matching row.

When rendering metadata fields, filesystem-invalid characters become hyphens,
braces become parentheses, and trailing spaces and periods are removed. Reserved
metadata names receive an underscore prefix. Custom template text and the final
paths still undergo normal validation; preview the resulting names before execution.

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
Regex matching has a 100 ms timeout. Each TMDB lookup gets 15 seconds overall and 5 seconds per attempt;
transient failures (network errors, timeouts, 408 and 5xx responses) are retried twice with backoff, TMDB rate
limits (429) are reported immediately, and after repeated failures TMDB calls pause for 30 seconds.
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

The pipeline is split by purpose:

- **Build and test** (`.github/workflows/backend.yml`) runs on pull requests to
  `main` and `next`. It only builds the solution and runs the backend and frontend
  tests; it never builds or publishes a Docker image.
- **Check commit messages** (`.github/workflows/commits.yml`) runs on the same pull
  requests and rejects any commit that does not follow
  [Conventional Commits](https://www.conventionalcommits.org).
- **Scan for secrets** (`.github/workflows/secrets.yml`) runs on the same pull
  requests and on pushes to `main` and `next`. It scans the full Git history with a
  pinned, checksum-verified [Betterleaks](https://github.com/betterleaks/betterleaks)
  release and fails on any finding (redacted in the log). Mark a false positive with a
  `betterleaks:allow` comment on the line, or add its fingerprint to
  `.betterleaksignore`.
- **Release** (`.github/workflows/release.yml`) runs on every push to `main` and
  `next`. It reruns the tests, then [semantic-release](https://semantic-release.gitbook.io)
  derives the next version from the commits since the last release: `fix:`/`perf:`
  bump the patch version, `feat:` the minor version, and a `!` after the type or a
  `BREAKING CHANGE:` footer the major version. Other types (`docs:`, `chore:`,
  `refactor:`, `test:`, `ci:`, `build:`, `style:`) publish nothing. `main` produces
  stable `vX.Y.Z` tags; `next` produces `vX.Y.Z-pre.N` pre-releases, and merging
  `next` into `main` promotes them. It pushes the tag, creates the GitHub release with
  a changelog grouped by commit type, and dispatches the Docker workflow for the tag.
  Nothing is committed back to the repository.
- **Build and publish Docker image** (`.github/workflows/docker.yml`) runs only for
  version tags (`vX.Y.Z` or `vX.Y.Z-pre.N`). It reruns the tests, publishes the image
  to `ghcr.io` and appends the image tags and digest to the release notes (creating
  the release with GitHub's generated notes if the tag was pushed by hand). Stable
  tags are published as `latest`; pre-release tags as `prerelease`. Both also get the
  version tag and the commit SHA as image tags. Dispatching it on anything other than
  a version tag fails.

Commit messages use the form `type(optional scope): summary`, for example
`feat(tmdb): match episodes by air date` or `fix!: reject renames across devices`.
Running `npm ci` in the repository root installs the release tooling and Git hooks
([lefthook](https://lefthook.dev), configured in `lefthook.yml`): `commit-msg` checks
the message, and `pre-commit` scans staged changes for secrets when `betterleaks` is
on your `PATH` (install it from the
[releases page](https://github.com/betterleaks/betterleaks/releases); without it the
scan is skipped and left to CI). This is separate from the frontend's
`npm ci --prefix .\Namerre.Front`.

## AI-assisted development

Much of Namerre's code, tests and documentation was written with the help of AI coding
assistants (mainly [Claude Code](https://claude.com/claude-code)), working under the
maintainer's direction. Commits where an assistant contributed carry a `Co-Authored-By:`
trailer naming the model.

AI-generated changes are reviewed, built and tested like any other change, but they can still
contain mistakes. Namerre renames real files: preview every plan, keep backups of anything you
cannot afford to lose, and remember that the software is provided as-is under the
[MIT License](LICENSE.txt), without warranty.
