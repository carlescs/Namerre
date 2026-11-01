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

User accounts and renaming history are saved in the data directory.
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
        "AutoProvision": true,
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
    },
    "Limits": {
      "MaxFilesPerRun": 200,
      "MaxStepsPerRun": 32,
      "MaxQueuedJobs": 32
    },
    "Integrations": {
      "Jellyfin": { "Url": "", "ApiKey": "" },
      "Emby": { "Url": "", "ApiKey": "" },
      "Plex": { "Url": "", "Token": "" },
      "Webhook": { "Url": "", "IncludePaths": false }
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

Exported signals: ASP.NET Core, SignalR and outgoing `HttpClient` (TMDB) traces and
metrics, Entity Framework Core database command traces (the SQLite store; commands are
parameterized and parameter values are not recorded, and only commands inside a request or
job span are traced, so idle queue polling exports nothing), .NET runtime, authentication and
Polly (TMDB resilience) metrics, Namerre's own spans and metrics below, and application logs
(with their formatted message). Query strings are redacted by the instrumentation, and library
refresh and webhook requests are never traced by URL, but request paths and log messages may
contain media file and folder names, so send telemetry only to a collector you trust. Invalid
settings fail startup.

Namerre spans (source `Namerre`): `rename.preview`; one root span per durable job —
`rename.prepare`, `rename.execute`, `rename.recover` — linked to the request that queued it and
tagged with `namerre.job.id`, `namerre.job.kind` and the final `namerre.job.status`, with
child spans for its phases (`rename.facts`, `rename.fingerprint`, `rename.preflight`,
`rename.stage`, `rename.place`, `rename.cleanup`, `rename.recovery.evidence`); `watch.scan`
per watch folder scan; `library.notify` per finished run with one child per target.

| Metric (meter `Namerre`) | Type | Tags |
| --- | --- | --- |
| `namerre.rename.job.duration` (s) | histogram | `namerre.job.kind` (`Prepare`, `Rename`, `Rollback`, `Recover`), `namerre.job.status` |
| `namerre.rename.job.files` | counter | `namerre.job.kind`, `namerre.file.status` |
| `namerre.rename.preview.duration` (s) | histogram | `namerre.preview.result` (`ready`, `blocked` or an error code) |
| `namerre.rename.rejections` | counter | `namerre.rejection.reason` (`PreviewBusy`, `QueueFull`, `RecoveryPending`) |
| `namerre.rename.hashed` (bytes) | counter | — |
| `namerre.rename.queue.jobs` | gauge | — (jobs waiting in the durable queue) |
| `namerre.rename.runs.recovery` | gauge | — (runs awaiting or undergoing recovery; mutations are blocked while it is above 0) |
| `namerre.tmdb.lookups` | counter | `namerre.tmdb.result` (`cache_hit`, `ok` or the error code, e.g. `TmdbRateLimited`) |
| `namerre.watch.files` | counter | `namerre.watch.folder`, `namerre.watch.outcome` (`prepared`, `unchanged`, `completed`, `failed`, `ignored`) |
| `namerre.library.notifications` | counter | `namerre.library.target`, `namerre.library.outcome` (`delivered`, `http_error`, `failed`, `dropped`) |
| `namerre.auth.sign_ins` | counter | `namerre.auth.method` (`password`, `oidc`), `namerre.auth.outcome` (`success`, `failure`, `denied`) |

Metric tags never contain file paths or record IDs. Logs record queued plans and runs, each
job's outcome and duration, runs that need recovery, deletions, sign-ins (failed attempts with
the remote address but never the attempted username), watch folder batches and library
notification results.

```powershell
$env:Namerre__Telemetry__OtlpEndpoint = 'http://otel-collector:4317'
```

## Library and webhook notifications (optional)

After a run finishes, Namerre can ask a media server to rescan its libraries and/or call a
webhook. Each target is disabled unless its `Url` is set; with nothing configured, no request
is ever made. A notification is sent when a rename or rollback run completes
(`Completed`) and when a recovery finishes (`Recovered`).

| Setting | Request |
| --- | --- |
| `Jellyfin:Url`, `Jellyfin:ApiKey` | `POST {Url}/Library/Refresh` with header `X-Emby-Token`. |
| `Emby:Url`, `Emby:ApiKey` | `POST {Url}/emby/Library/Refresh` with header `X-Emby-Token`. |
| `Plex:Url`, `Plex:Token` | `GET {Url}/library/sections/all/refresh` with header `X-Plex-Token`. |
| `Webhook:Url`, `Webhook:IncludePaths` (default `false`) | `POST {Url}` with a JSON body, see below. |

URLs must be absolute `http`/`https` URLs (a trailing slash is ignored). The key or token is
required whenever its URL is set, and a key without a URL is rejected; invalid settings fail
startup. Keys and tokens are sent only as request headers from the backend and are never
logged, stored in plans or runs, or exposed to the frontend.

Notifications are best effort: they are queued and sent in the background with a 10 second
timeout, a failing or unreachable endpoint is logged as a warning, and none of this can delay
or fail a rename job. Notifications still queued when Namerre stops are dropped.

Webhook body:

```json
{
  "event": "run.finished",
  "runId": "3f2c...",
  "kind": "Rename",
  "status": "Completed",
  "files": { "total": 3, "completed": 3, "failed": 0 }
}
```

`kind` is `Rename` or `Rollback`; `status` is `Completed` or `Recovered`. Media file paths
can be sensitive, so they are left out by default. With `Webhook:IncludePaths` set to `true`,
`files.items` also lists `{ "sourcePath": "...", "targetPath": "..." }` for every file.

```powershell
$env:Namerre__Integrations__Jellyfin__Url = 'http://jellyfin:8096'
$env:Namerre__Integrations__Jellyfin__ApiKey = '<api-key>'
```

## Authentication (required)

Namerre has local user accounts, no self-registration, and no default credentials.
When no account has been configured, the browser displays an installation assistant
that creates the first administrator. First configure `Namerre:RootPath` and
`Namerre:DataPath`: users are stored in the **existing SQLite renaming/history
database**, not a credentials file.
For example, with an existing media folder:

```powershell
$env:Namerre__RootPath = 'D:\Media'
$env:Namerre__DataPath = 'D:\NamerreData'
dotnet run --project .\Namerre.Api\Namerre.Api.csproj --launch-profile https
```

Open `https://localhost:7151`, copy the one-time setup token printed in the server
console, and choose an administrator username and a password of at least 12
characters. Confirm the password and select **Create administrator**, then sign in.
Only the username, role and salted password hash are saved in the database's
`Users` table. Setup only runs while there are no users, cannot replace an existing account, requires HTTPS and CSRF protection, and shares the
login attempt limit. Protect console/log access: the token authorizes first-time
installation. It changes on each restart until setup succeeds, is never returned
by the API, and becomes unusable after the account is created.

To change your password, sign in and open **Account** from the menu under your username.
Enter the current password and the new password twice, then select **Change password**.
The new hash replaces the stored one; your current browser session stays signed in and
every other session is signed out. Password changes require the current password, CSRF
protection, and a new password of at least 12 characters, and share the login attempt limit.
An existing shorter password keeps working until it is changed.

Keep `Namerre:DataPath` persistent across restarts and container replacement; changing
or deleting this database also loses the user accounts and renaming history.
Corrupt or inaccessible stored credentials fail startup rather than reopening setup.
The media root and database must be available for database-backed sign-in.

Server-configured credentials also remain supported as a **break-glass administrator**
and skip the assistant. That account is never stored, always has the `Admin` role, is listed
read-only on the **Users** page, and works alongside the users in the database.
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
installation, or to keep a way in if every stored administrator is locked out. Never commit
credentials or include them in Angular assets or Docker build arguments. Changing either setting
and restarting the app invalidates its existing sessions. To recover access, generate a
new hash, update the server configuration, and restart. Server-configured passwords cannot be
changed from the **Account** page.

### Users and roles

Every person signs in as a local user. A password and single sign-on are only ways of
signing in to that user, and a user can have either or both. Roles are managed in Namerre
only; an identity provider never sets or changes them.

| Role | Can |
| --- | --- |
| `User` | Rename files, manage presets, and see every run (runs show who started them). |
| `Admin` | Everything a user can, plus manage users, see and control watch folders, and delete runs. |

Administrators manage users from **Options** (the sliders button in the header) → **Users**:
create users with a password, a single sign-on identity, or both; change roles; disable or
enable users; set, reset or remove passwords; link, unlink or move single sign-on identities;
and delete users. The server keeps every account usable:

- At least one enabled administrator remains (the break-glass administrator counts).
  Administrators cannot remove their own `Admin` role, disable themselves or delete themselves.
- An enabled user always keeps at least one sign-in method: the last password or linked
  identity cannot be removed, and a disabled user without one cannot be enabled.
- Disabling a user, resetting or removing their password, changing their role, and linking or
  unlinking an identity replace that user's credential stamp, which ends their existing sessions
  (and live updates) right away.
- Usernames are unique ignoring case. A user without a password fails password sign-in exactly
  like a wrong password, without revealing that the account exists.

Rename history belongs to the user who prepared each plan: a user sees, exports, undoes and
recovers only their own plans and runs, including undos an administrator started for them, and
anyone else's reads as not found. Administrators see and manage every run, attributed to the user
who prepared, executed, undid or recovered it; watch-folder runs (which show the folder instead)
and history from before users are visible to administrators only. Presets are shared. Run
exports include `runCreatedBy` and `runOrigin` columns. Deleting a user keeps their runs, still
showing their name, and merging a user moves their history to the target user. User management and linking actions are logged with who did what to which
user (never passwords, tokens or claims).

The user management API is `api/users` (administrators only): `GET` lists users with their
sign-in methods, `POST` creates one (`username`, `role`, and a `password`, a single sign-on
`subject`, or both), `PATCH /{id}` changes `role` or `disabled`, `PUT`/`DELETE /{id}/password`
sets or removes the password, `POST /{id}/logins` links an identity (`subject`) or moves one
(`loginId`, optional `deleteSource`), `POST /{id}/merge` merges another user into it (`sourceUserId`),
`DELETE /{id}/logins/{loginId}` unlinks it and
`DELETE /{id}` deletes the user. Users manage their own account under `api/auth`: `POST password`
(`currentPassword` may be omitted only by a user without a password, after a recent sign-in),
`DELETE password`, `POST reauthenticate`, `POST oidc/link` (returns the address that starts linking),
`GET logins` and `DELETE logins/{id}`. `GET api/auth/session` reports the user's `role`,
`hasPassword` and linked `logins`, so the app hides what the user cannot use. Refusals return
`{ "code", "message" }`, e.g. `LastAdmin`, `OwnAccount`, `LastSignInMethod`, `LoginTaken` or
`RecentSignInRequired`.

**Upgrading from a single administrator.** The `AddUsers` migration runs on the first start
after upgrading: the existing administrator becomes the first `Admin` user and keeps its
username and password. Sessions from before the upgrade sign in again. Single sign-on users
get a local `User` the first time they sign in after the upgrade (when provisioning is on);
promote them on the **Users** page if they need administrator rights.

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
these automatically. Login, setup, password changes, re-confirming the password and starting a
single sign-on link are limited to five attempts
per minute for each client address (IPv6 clients are grouped by their /64 network) and
60 attempts per minute across all clients. One noisy client therefore cannot lock
everyone out, while guessing from many addresses stays bounded. Behind a reverse
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
the password form; local passwords keep working. `RequiredRole` decides who may sign in
through single sign-on and is checked on every sign-in, also for linked users; it does not
decide what they can do in Namerre, which is the local user's role.

Every single sign-on resolves to a local user, keyed on the provider's issuer and stable
`sub` claim (never on `preferred_username` or email, which can change or be reused):

- **First sign-in.** With `AutoProvision` on (the default), an identity that is not linked yet
  gets a new local user with the `User` role, named after `UsernameClaim` (with a `-2`, `-3`, …
  suffix if the name is taken). An administrator promotes it if needed. With `AutoProvision`
  off, unlinked identities are refused until someone links them. Until the first administrator is
  installed with the setup token (or configured), no users are provisioned, so single sign-on
  cannot end installation without an administrator.
- **No automatic linking.** An identity whose username matches an existing local user is never
  attached to it silently: that would let anyone controlling the identity provider take over
  local accounts. Linking is always explicit.
- **Linking yourself.** A signed-in user opens **Account** and selects **Link _DisplayName_**.
  Linking needs a sign-in within the last ten minutes or the user's password; Namerre then asks
  the provider to sign in again and attaches the returned identity. An identity that already
  belongs to another user is refused.
- **Linking by an administrator.** On the **Users** page, link an identity by its subject (the
  `sub` claim, e.g. the Keycloak user ID), or move an identity from another user. The user it
  leaves can be kept, deleted (its runs keep its name), or **merged** into the target: all of its
  identities move too, its plans and runs are attributed to the target, and it is deleted with its
  password. The target keeps its own role and password. Merging follows the deletion rules (not
  yourself, not the last administrator).
- **Passwords for single sign-on users.** A user who only signs in with single sign-on can set a
  password on **Account** after a recent single sign-on (there is no current password to
  confirm). "Recent" is the provider's authentication time (`auth_time`), not the callback's, and
  **Sign in again** asks the provider to authenticate afresh (`prompt=login`, `max_age=0`), so a
  provider session left open in a browser is not enough. Users can also unlink identities or remove their password, but never their last
  sign-in method.
- A disabled local user cannot sign in through single sign-on either.

| Setting | Default | Description |
| --- | --- | --- |
| `Authority` | empty | Issuer URL, for Keycloak `https://keycloak.example.com/realms/<realm>`. Must be HTTPS. |
| `ClientId` | empty | Required. |
| `ClientSecret` | empty | Secret of a confidential client (recommended). Treat as a secret. Omit for a public client; PKCE is always used. |
| `RequiredRole` | empty | Required. Role (or group) the ID token must contain. |
| `RoleClaim` | `realm_access.roles` | ID token claim holding the roles. Dots navigate nested JSON, e.g. `resource_access.namerre.roles` for client roles, or a flat claim such as `groups`. |
| `UsernameClaim` | `preferred_username` | Claim that names users created on first sign-in and linked identities; falls back to `sub`. |
| `AutoProvision` | `true` | Create a local `User` on an identity's first sign-in. When `false`, identities must be linked first. |
| `DisplayName` | `Single sign-on` | Button label (1-64 characters). |
| `Scopes` | `[]` | Scopes requested in addition to `openid` and `profile`. |
| `RequireHttpsMetadata` | `true` | Set to `false` only for a development provider on plain HTTP. |

Keycloak setup:

1. In your realm, create an **OpenID Connect** client (for example `namerre`) with
   **Client authentication** on and only the **Standard flow** enabled. Set
   **Valid redirect URIs** to `https://<namerre-host>/api/auth/oidc/callback` and
   **Web origins** to `https://<namerre-host>`, and **Valid post logout redirect URIs** to
   `https://<namerre-host>/`. Copy the client secret from **Credentials**.
2. Create a realm role (for example `namerre-user`) and assign it to the users or group
   allowed to sign in to Namerre. Their rights in Namerre come from their local role.
3. Keycloak does not put roles in the ID token by default. Under **Client scopes** →
   `roles` → **Mappers** → **realm roles**, enable **Add to ID token** (or add a
   dedicated "User Realm Role" mapper to the client's dedicated scope with that option).
4. Configure Namerre, for example:

```powershell
$env:Namerre__Authentication__Oidc__Authority = 'https://keycloak.example.com/realms/media'
$env:Namerre__Authentication__Oidc__ClientId = 'namerre'
$env:Namerre__Authentication__Oidc__ClientSecret = '<client-secret>'
$env:Namerre__Authentication__Oidc__RequiredRole = 'namerre-user'
$env:Namerre__Authentication__Oidc__DisplayName = 'Keycloak'
```

Namerre uses the authorization code flow with PKCE, validates the ID token and
discards the access and refresh tokens after sign-in. The session is built from the local
user (ID, username, role and credential stamp), not from the provider's claims, and keeps the
ID token only as the sign-out hint, in the same encrypted eight-hour, SameSite=Strict session
cookie as local sign-in. Users without the role are rejected. Changing `Authority`, `ClientId`, `RequiredRole`, `RoleClaim` or
`UsernameClaim` invalidates existing single sign-on sessions after a restart; removing
the role in Keycloak takes effect at the next sign-in. **Sign out** of a single sign-on
session also ends the Keycloak session: Namerre ends its own session, then sends the
browser to the provider's `end_session_endpoint`, which returns to Namerre afterwards.
If the provider advertises no such endpoint or is unreachable, only the Namerre session
ends, and the button may sign straight back in while Keycloak still remembers the user.
Behind a reverse proxy, configure trusted proxies
(see above) so the callback URL uses the public `https` host. Incomplete settings fail
startup; an unreachable provider shows an error on the login page.

## Installing as an app

Namerre is an installable Progressive Web App. In Chrome or Edge, open Namerre and
select **Install** in the address bar or the browser menu. On Android, use **Add to
Home screen**. On iOS and iPadOS, use Safari's **Share → Add to Home Screen**.
The installed app opens in its own window with the Namerre icon and still talks to
the same server, so the server must be running and reachable.

- **Secure context required.** Browsers only offer installation over HTTPS, or on
  `http://localhost`. Behind a reverse proxy, install from the public `https` URL.
  A plain-HTTP LAN address is not installable, and Namerre's `/api` refuses
  plain HTTP anyway.
- **What is cached.** A service worker (generated by the production Angular build
  as `ngsw-worker.js` and `ngsw.json`) caches only the app shell: `index.html`,
  scripts, styles, icons and the manifest. `/api` requests, including the
  `/api/hubs/rename` live-update hub and sign-in, are never cached, and nothing
  is queued offline. When the server is unreachable, the app shows an "Unable
  to connect" message and reconnects when the network comes back.
- **Updates.** The server sends `index.html`, the manifest and the service-worker
  files with `Cache-Control: no-cache`. After an upgrade, the app finds the new
  version the next time its window is shown and offers **Reload**. Rename jobs
  keep running on the server either way.
- **Sign-in.** Password and single sign-on both work from the installed window.
  The OpenID Connect redirect goes to the identity provider and comes back to `/`.
- Development builds (`npm run watch`, `ng serve`) do not register the service
  worker. To remove it from a browser, uninstall the app or clear the site data.

## Renaming workflow

### Simple and Advanced modes

The renamer has two modes. The first time someone signs in, Namerre asks which one to use;
the choice is kept in that browser only (`localStorage`) and can be changed at any time with
the **Simple / Advanced** switch in the header's **Options** menu (sliders icon), next to the theme
selector. **Decide later**
uses Simple for that visit and asks again next time. Run history is available in both modes.

- **Simple** has four stages: **Select files**, **Choose a preset**, **Match TMDB metadata**
  (only for TMDB presets) and **Review and rename**, where the preview and the confirmation of
  the prepared plan share one screen. Pick one of the built-in presets and, if you like, adjust
  its few key options:
  - *TV episodes (TMDB)*: names episodes from TMDB (name style for Emby, Plex or
    Jellyfin/Kodi, and the metadata language), then tidies the spacing. Files stay in their folders.
  - *Movies (TMDB)*: names movies `Title (Year)` in the chosen metadata language.
  - *Clean up names*: removes release tags, turns dots and underscores into spaces, simplifies
    ligatures and symbols, and sets the case.
  - *Number files*: adds a running number before or after the name, with start and digits.

  Run options are not shown and use the defaults: related files follow their video (as chosen
  in the file selection), colliding names are reported rather than numbered, files are checked
  by size and modification time, and folders left empty are removed. Built-in presets are plain
  pipelines (`Namerre.Front/src/app/modules/renamer/simple-presets.json`), validated by the
  server like any other request; a backend test previews each of them.
- **Advanced** is the full wizard described below: plugin configuration with saved presets,
  TMDB matching, a preview with every run option, and a separate confirmation.

Switching from Simple to Advanced keeps everything: the steps the preset built open in the
plugin editor with all their options, and the files, TMDB matches and any prepared plan carry
over. Switching from Advanced to Simple keeps the steps when they are exactly what a Simple
preset builds (or there are none); otherwise Namerre warns that the advanced steps will be
discarded and lets you keep Advanced or start a fresh pipeline (the files stay selected).
Queued or running jobs are never affected by the mode: it only changes the editing screens.

### Files and steps

Add files using the existing folder picker, optionally including subfolders.
Hidden and system entries (names starting with `.` or `@`, such as `.DS_Store` and
Synology's `@eaDir`, plus `Thumbs.db`, `desktop.ini` and recycle-bin folders) are
skipped unless **Show hidden files and folders** is ticked in the picker.
The collection can contain files from multiple folders. Start with an empty plugin
stack and add as many independently configured instances as needed: text replacement,
regular-expression replacement, case change, extension case, prefix and suffix, number padding, counter, date, or TMDB resolution. Repeated plugin
types are allowed, except TMDB: a recipe holds at most one TMDB step, since each one needs its own
confirmed match for every file. Drag a step by its handle or move it up or down with its arrows
to choose the execution order; the catalog order does not determine execution order.

### Presets

**Presets** at the top of the plugin stage save the current steps and run options (content
check, empty-folder cleanup, collision numbering) on the server under a name, so they work from
any browser and several can be kept. Loading a preset replaces the current steps; **Save changes
to …** overwrites the preset last loaded or saved, **Rename only** changes its name and **Delete**
asks for confirmation. TMDB matches are per file and never saved. Names are unique ignoring case
(1–100 characters) and a server keeps at most 100 presets; steps are checked like a pipeline
request when saved (known plugin versions, at most the step limit, options under 8 KB each). A
preset holding a step whose plugin version this server no longer has still loads: the step is
flagged in the editor and left out of previews until removed. The browser draft remains as
before. API: `GET/POST /api/rename-presets`, `GET/PUT/PATCH/DELETE /api/rename-presets/{id}`
(`PATCH` takes `{ "name": … }`); presets live in the `Presets` table of the SQLite database.

### Applying a step to some files

Every step has an **Apply to** section. List extensions (`srt, ass`, with or without the dot)
and/or a .NET regular expression tested against the current file name, extension included; a
file must match both to get the step, and **Apply to the files that do not match instead**
inverts the filter. Patterns ignore case unless that box is cleared and share the 100 ms regex
timeout. The filter looks at the step's input, so earlier steps can change which files a later
step applies to. A step that does not apply passes the name through unchanged and shows as
skipped in the preview trace; a TMDB step skipped this way needs no match, so the matching stage
lists only the files it applies to. The API field is `condition` on each plugin instance
(`{ "extensions": ["srt"], "pattern": "\\.en\\.", "ignoreCase": true, "invert": false }`);
instances without one apply to every file, as before, and plans keep it in their snapshot.

### Related files

With **Include related files** ticked (the default), files that belong to a video follow it
instead of being renamed on their own: subtitles (`Movie.srt`, `Movie.en.hi.srt`, `.ass`, `.ssa`,
`.sub`, `.idx`, `.vtt`), the `Movie.nfo` and artwork (`Movie-poster.jpg`, `-fanart`, `-thumb` as
`.jpg`, `.jpeg`, `.png`, `.webp` or `.tbn`) in the same folder as a video in the collection. The
name must be the video's stem followed directly by one of these suffixes, so `Movie 2.srt` belongs
to `Movie 2.mkv`, never to `Movie.mkv`; when several videos match, the longest stem wins. The
collection lists each related file under its video, moving or sorting a video carries its related
files along, and removing a video removes them too. Related files skip the steps and take the
video's final folder and name plus their own suffix, so a confirmed TMDB match, folder moves and a
hand-edited name of the video apply to them, and they need no TMDB match of their own; step
counters number the videos only. They stay separate files in the plan and the run (marked with
`companionOf`, the video's file ID), so journaling, rollback and recovery still work per file —
selecting a video for undo selects its related files too — and they count toward the 200-file
limit. The API option is `includeCompanions` on the pipeline request (off unless sent).

Plugins run on the server. Each enabled instance receives the preceding instance's
output, and the preview shows the intermediate names and final relative destination.
Text, regex, case, and prefix/suffix steps preserve the extension and folder; a
prefix or suffix is added around the name, before the extension. Regex replacements
use .NET syntax, including `$1` and `${name}` captures. TMDB matches require explicit
confirmation and can optionally change the destination folder through templates.
No files or directories are changed by previews.
Each step also learns the file's zero-based position in the request and among the files
of its source folder, the batch and folder sizes, and the file's size, last-modified and
creation time (creation time is null or approximate on filesystems that don't record it).

**Case change** converts the name to UPPERCASE, lowercase, Title Case (every word
capitalized), Sentence case (`My file name`), Title Case with short words lowercase
(`The Lord of the Rings`), `snake_case`, `kebab-case`, `camelCase` or `PascalCase`.
The short-words title case keeps articles, conjunctions and prepositions lowercase unless
they are the first or last word or follow `:`, `.`, a dash or an opening bracket; its
**Short words** list is English, Spanish, Catalan, French, Italian or Portuguese (English
when unset), and in Catalan, French and Italian it treats elisions such as `l'` and `dell'`
as short words (`El Mar i l'Àvia`). The identifier styles split words at spaces,
punctuation and case changes (`XMLHttpRequest` → `xml_http_request`), keep accented and
non-Latin letters, and drop the apostrophe of short endings (`don't` → `dont`). Casing is
culture-invariant.
**Extension case** lowercases or uppercases the file extension (`IMG_01.JPG` →
`IMG_01.jpg`). It is opt-in, and the only change to an extension the pipeline allows: any
other step that alters the extension, or this one changing more than its case, fails the
file. On case-insensitive filesystems (Windows, macOS) the rename still goes through a
staging path, so the file is never mistaken for a collision with itself, and a rollback
restores the original case. Related files keep their own extension.
**Replacement table** applies several find/replace rows in one step, top to bottom, each
to the previous row's result, with the same case and scope options as **Literal
replacement**; a row with empty find text is rejected.
**Clean up separators** turns dots and underscores (and, if chosen, dashes) into spaces,
keeps the dot of short numbers such as `v1.2` or `5.1` (one or two digits each side, not
followed by a letter, so `S01E02.1080p` still splits), collapses repeated whitespace and
trims spaces, dots, underscores and dashes from both ends (`Show.Name.S01E02.1080p` →
`Show Name S01E02 1080p`); each clean-up can be turned off.
**Strip release tags** removes resolution (`1080p`, `2160p`, `4K`), video codec (`x264`,
`H.265`, `HEVC`, `10bit`), source (`WEB-DL`, `WEBRip`, `BluRay`, `Remux`), audio (`DDP5.1`,
`AAC2.0`, `DTS-HD MA`, `TrueHD`, `Atmos`) and HDR (`HDR10+`, `DV`, `Dolby Vision`) tags by
category, bracketed groups (`[...]`; parenthesised groups such as years only when chosen),
a trailing `-GROUP` that directly follows a release tag (so `Spider-Man` keeps its last
word) and extra tokens listed one per line or comma-separated (up to 100). Matching is
case-insensitive and on whole tokens only, so a tag inside a word is never removed; the
separators around a removed token collapse into one and none are left at either end
(`Movie.2010.1080p.BluRay.x264-GRP` → `Movie.2010`).
**Remove accents and normalise characters** transliterates ligatures and typographic
symbols (`æ` → `ae`, `ß` → `ss`, `’` → `'`, `–` → `-`), removes diacritics (`é` → `e`,
`ç` → `c`, `ñ` → `n`), optionally replaces or removes every character still outside ASCII
(per visible character, with `_` as the default replacement) and finally normalises to
NFC (the default) or NFD. Names that end up identical are reported as clashing targets.
**Insert text** inserts text a number of characters from the start or the end of the name;
**Remove characters** removes a number of characters from the start, the end, or a range
that starts some characters in from either end; **Truncate** shortens names longer than a
maximum, optionally at the last space that fits and with a marker such as `…` that counts
toward the maximum. These three count visible characters (grapheme clusters), so emoji and
letters with combining accents are never split, and positions past the end of the name are
clamped to it.
**Pad numbers** zero-pads every, the first or the last run of digits in the name to a
minimum width (`Episode 3` → `Episode 03`); numbers already that wide are never shortened,
and numbers that look like years (four digits from 1900 to 2099) can be skipped.
**Counter** inserts `start + step × position`, zero-padded to a width, before or after the
name with a separator, or through a template such as `{name} - {n}`; it follows the
collection order, optionally restarting in each source folder. The preview's **#** column
shows that order. **Date** inserts the file's last-modified or creation date with a .NET
custom format string (`yyyy-MM-dd`, `yyyyMMdd_HHmmss`; invariant culture) in a chosen time
zone (an IANA or Windows ID, the server's local zone when empty), the same way or through a
`{date}` template. Invalid formats, unknown time zones and missing creation dates are
reported per file. Prepared plans use the facts snapshot and the same order, so they
produce the names shown by the preview.

Prepare the approved configuration to hash the source files and store an immutable
plan. Preparation reads each file's size and dates once and keeps them in the plan
(`facts`, keyed by file ID), so steps see the values it recorded rather than re-reading
the files; execution's source checks still catch files that changed afterwards. Hashing reads the full contents of each file and can take substantial disk I/O
for large media collections. The web UI therefore defaults to **Quick check** on the
preview step (`verifyContent: false` on `POST /api/rename-plans`; the API itself still
defaults to hashing); untick it to hash contents. With Quick check, preparation records
each file's size and last-modified time instead of a content hash, and execution,
rollback and recovery compare those values rather than re-reading the files. This is
much faster but will not notice a content change that keeps both size and timestamp,
and identical-looking copies are more likely to make recovery ambiguous.
Review the prepared paths, then confirm execution.
To override a calculated name, select the pencil beside it on the confirmation step and
edit the full root-relative path in place — folder, name and extension can all change —
then accept with the check mark or discard with the cross (or Escape). The edited
pipeline is previewed first; a rejected path (invalid, clashing or already taken) leaves
the prepared plan as it was, and an accepted one prepares the plan again. Overrides are
sent as `overrides` (`fileId` and `targetPath`) on the pipeline request, applied after
the plugins, and snapshotted with the plan; edited names are marked and can be restored
to the calculated one.
Changing the collection, plugin options, order, or metadata selections invalidates
the previous preparation; plugin and metadata changes also discard name overrides. Accepted jobs continue independently of the browser;
their progress and outcome are available in persistent history.
Open pages follow progress live: the server pushes a short change signal (`renameChanged`
with `kind`, `id` and `status`) over SignalR after every durable plan or run update, and the
browser re-reads the record from the REST API. The signal carries no file paths and only reaches
administrators and the user whose history changed. The hub
requires the same session cookie as the API and closes when the session expires; a fallback
refresh every 15 seconds covers any missed signal.

Existing targets and duplicate destinations block execution unless **Add a number when
names collide** is ticked on the preview step (`onCollision: "AddNumber"` on the pipeline
request; `"Report"`, the default, keeps the blocking behaviour). Then, in collection order,
the first file keeps its name and each later file whose name is taken, by another file of
the batch, a file that stays where it is, or an existing file that is not being renamed
away, gets the first free ` (1)`, ` (2)`, … suffix before the extension (`Name (1).mkv`),
following the filesystem's case rules. Related files follow their video's numbered name,
manual overrides are never numbered, and the preview marks each numbered name
(`numbered: true`). Numbers are chosen when the plan is prepared and stored in it, so
execution never picks names; if a numbered target appears in the meantime, execution
stops for that file and nothing is overwritten. Files can move within
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

A run's details offer its file list as a download for audits or other tools
(`GET /api/rename-runs/{id}/export?format=csv|json`, CSV by default). Each file has its
source and target path, final status, error, and the most recent rollback run that
included it (ID, status and time), next to the run's ID, kind, status and creation time.
CSV follows RFC 4180 with a UTF-8 byte order mark so spreadsheets read accents correctly,
and a cell starting with `=`, `+`, `-`, `@`, a tab or a carriage return is prefixed with
`'` so it is never evaluated as a formula. Like every API response, exports require a
signed-in session and are sent with `Cache-Control: no-store`.

Administrators can delete settled runs (completed or recovered) from history, either from the
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

## Watch folders (optional)

Watch folders rename new files automatically with a saved [preset](#presets). Configure them under
`Namerre:WatchFolders` (they are off while the list is empty):

```json
"WatchFolders": [
  {
    "Name": "Downloads",
    "Path": "downloads/tv",
    "Preset": "TV for Jellyfin",
    "Recursive": false,
    "IntervalSeconds": 60,
    "StableSeconds": 60,
    "IncludeCompanions": true,
    "TmdbMediaType": "tv",
    "TmdbAutoConfirm": 0.9,
    "MaxAttempts": 5
  }
]
```

Every `IntervalSeconds` (10–86400) the server lists the folder (with subfolders when `Recursive`),
skipping hidden and system entries, links and Namerre's staging files, which is more reliable than
filesystem events on network shares. A file is taken once its size and modification time have not
changed for `StableSeconds` (0–86400, measured between scans, so a file is always seen at least
twice). Stable files — up to the per-run file limit — are previewed with the preset's steps and run
options, and the ones that can be renamed become a normal plan and run, prepared and executed
by the same durable job queue, journal and history; such runs say **Started by watch folder …** in
the history and can be undone like any other (`origin` is `watch:<name>` on the plan and run). A
folder runs one batch at a time. Renamed files are recorded under their new name, so a file renamed
inside the watched folder is not taken again; a file whose name the preset leaves unchanged is
recorded as done without a run. With `IncludeCompanions` (the default), related files follow their
video, and a video and its related files only go together.

A file that cannot be renamed (an occupied destination, a failing step, a TMDB match below the
threshold) is retried after the interval, then after twice that, and so on up to a day; after
`MaxAttempts` (1–20) failures it is ignored until its size or modification time changes. TMDB being
busy or rate-limited, a full job queue or pending recovery only postpones the batch to the next
scan without counting against the files. The history page lists the configured folders with their
state, the files that were not renamed and why, a **Retry now** button per file, and
**Pause**/**Resume** (kept in the database across restarts); only administrators see the panel. The
API (administrators only) is `GET /api/watch-folders`, `POST /api/watch-folders/{name}/pause`,
`…/resume` and `…/retry` with `{ "path": … }`. Watch-folder runs have no user.

**TMDB.** Matches normally need a person's explicit confirmation, so a preset with an enabled TMDB
step is refused unless the folder sets `TmdbMediaType` (`movie` or `tv`) and `TmdbAutoConfirm`, a
confidence from 0.5 to 1 above which the [automatic suggestion](#tmdb-metadata) is accepted on the
person's behalf (0.85 is the interactive "high confidence" mark; higher is stricter). A suggestion
below it fails the file with its confidence, so it can be renamed by hand. The accepted match is
confirmed and snapshotted into the plan exactly like a manual one.

Folder names are 1–50 letters, digits, spaces, `-` or `_` and unique; paths are root-relative with
forward slashes and may not overlap (the same folder twice, or a folder inside a recursive one).
Invalid entries fail startup. A missing preset, or one with steps this server no longer knows, is
reported on the folder until fixed.

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
modification dates. Its order is the renaming order that steps see as each file's
position: new files are added at the end, and **Order** sorts the whole collection once
(name in natural order, folder then name, size or modification date, either direction).
Fine-tune it by dragging a row's handle or with its up and down arrows; with **Include
related files** on, a video's subtitles, `.nfo` and artwork are listed under it and drag
and move with it as one group. Reordering is disabled while a filter hides part of the
collection. The collection, the renaming rules and the run options are kept in
the browser's local storage, so a reload picks up where you left off; confirmed TMDB
matches are not kept and are matched again. The saved collection is dropped once a rename
starts.
Navigation stays within the configured root; symbolic links
and junctions are excluded. `GET /api/folders?path=Movies` accepts a relative
folder path; omit `path` to list the root. This endpoint lists metadata only,
not file contents.

For browsing only, Docker can mount the desired server folder read-only and set
`Namerre__RootPath` to its path **inside the container**. Expose only folders
you intend signed-in users to see, and restrict network access to trusted users.
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
Complete installation using the token in `docker logs <container>`; the first administrator
and later users are saved alongside history in the mounted `/data` database. To skip installation,
also pass your protected `Namerre__Authentication__Username` and `Namerre__Authentication__PasswordHash`
environment values.

The host-bind example requires a supported underlying filesystem; on Docker Desktop,
use a native Linux filesystem/volume when the Windows host mount is rejected.
Use a disposable media directory first. Removing the container must not remove
its data volume. Authentication does not sandbox signed-in users or replace network
restrictions; exposing this mutation-capable API to an untrusted network is unsafe.

## TMDB metadata

Configure `Namerre:Tmdb:ReadAccessToken` through server secrets or the
`Namerre__Tmdb__ReadAccessToken` environment variable. Never put it in Angular configuration,
pipeline options, run snapshots, Docker build arguments, or committed files.
The token is sent from the backend to TMDB in an authorization header.
Local text/regex/case transformations do not require TMDB.

The same rule applies to the optional integration credentials
(`Namerre:Integrations:Jellyfin:ApiKey`, `Namerre:Integrations:Emby:ApiKey`,
`Namerre:Integrations:Plex:Token`): set them through server secrets or environment variables
(for example `Namerre__Integrations__Plex__Token`), never in Angular configuration, run
snapshots, Docker build arguments, or committed files. A webhook URL may embed a secret as
well, so treat it the same way.

Search for a movie or TV show, correct the query when necessary, and confirm the
intended match. Episodes require a confirmed show, season, and episode. Metadata
defaults to English; names default to `Movie Title (Year)` or
`Show Title - S01E02 - Episode Title`, preserving the extension. For subtitle files
(`.srt`, `.ass`, `.ssa`, `.sub`, `.idx`, `.vtt`) the trailing language and flag tokens
before the extension (`.en`, `.pt-BR`, `.eng`, `.hi`, `.sdh`, `.cc`, `.forced`, `.default`) are kept too, so
`Episode.en.hi.srt` becomes `Show Title - S01E02 - Episode Title.en.hi.srt`. Folder changes are
optional and off by default. When folder changes are enabled, episodes can additionally
be placed in a season folder inside the show folder (for example
`TV/Show Title (2010)/Season 01/`). The season folder template defaults to
`Season {season}` (two-digit, `Season 01`); use `{seasonNumber}` for the unpadded number
(`Season 1`) or any other single-segment format such as `S{season}`. Movies ignore it.

Templates can use `{title}`, `{originalTitle}` (title in the original language), `{year}`,
`{showYear}` (first-air year of the show, episodes only), `{airDate}` (episode air date or movie
release date, `yyyy-MM-dd`), `{tmdbId}` (movie or show ID), `{season}`, `{seasonNumber}`,
`{episode}` and `{episodeTitle}`. Numeric fields take a zero-padding format of 1 to 6 zeros, such as
`{episode:000}` (`007`) or `{season:0}` (`7`). A template that uses a value TMDB did not provide
(for example a missing air date) is reported in the preview instead of rendering. For Jellyfin and
Plex, `{title} ({year}) [tmdbid-{tmdbId}]` identifies an item reliably. Write `{{` and `}}` for
literal braces, as in Plex's `{{tmdb-{tmdbId}}}` (`{tmdb-603}`). Templates only depend on the
metadata language for confirmation, so editing them never invalidates confirmed matches.
Confirmed metadata is snapshotted for the run so a
later TMDB change or outage does not alter an approved execution or rollback.

**Multi-episode files** (double-length premieres and finales) take a **Last episode** in the
picker: the file is confirmed for every episode from the first to the last (consecutive, one
season, at most 10), and each one is looked up, so a range past the season's end is refused. The
range is part of the confirmation, so it cannot change without confirming again. `{episode}`
then writes the range in the step's **Multi-episode numbers** style — `S01E01-E02` (default),
`S01E01E02` or `S01E01-02`, with any number format applied to both ends — and `{episodeTitle}`
joins the titles: identical titles stay one, titles that differ only by a part suffix
(`The End (1)` / `The End (2)`, `Exodus, Part 1` / `Exodus, Part 2`, `Part One`, `Pt. II`) give the
common name (`The End`), and others are joined with ` + ` (`Title A + Title B`). `{airDate}` is the
first episode's. The picker and automatic suggestions read ranges such as `S01E01-E02`,
`S01E01E02`, `S01E01-02` and `1x01-1x02` from file names. Single-episode matches and existing
drafts are unchanged; the API field is `episodeEnd` on the confirmation request and the match.

**Naming preset** fills in the filename, folder and season folder templates (and turns on
folder changes, plus season folders for TV) with a media server's recommended layout; it
leaves the base folder and language alone. Editing any filled-in option afterwards switches
the preset back to **Custom**, and the preset never invalidates confirmed matches. Each
preset is for movies or for TV shows, since episode fields are unavailable for movies. For
The Matrix (TMDB ID 603) and Game of Thrones S01E02 (show ID 1399), with `Movies` or `TV` as
base folder, the presets give:

| Preset | Movies | TV shows |
| --- | --- | --- |
| Plex | `Movies/The Matrix (1999) {tmdb-603}/The Matrix (1999).mkv` | `TV/Game of Thrones (2011) {tmdb-1399}/Season 01/Game of Thrones (2011) - s01e02 - The Kingsroad.mkv` |
| Jellyfin | `Movies/The Matrix (1999) [tmdbid-603]/The Matrix (1999).mkv` | `TV/Game of Thrones (2011) [tmdbid-1399]/Season 01/Game of Thrones S01E02 - The Kingsroad.mkv` |
| Emby | `Movies/The Matrix (1999) [tmdbid=603]/The Matrix (1999).mkv` | `TV/Game of Thrones (2011) [tmdbid=1399]/Season 1/Game of Thrones - S01E02 - The Kingsroad.mkv` |
| Kodi | `Movies/The Matrix (1999)/The Matrix (1999).mkv` | `TV/Game of Thrones (2011)/Season 01/Game of Thrones S01E02 - The Kingsroad.mkv` |

The TMDB matching stage appears automatically when an enabled TMDB plugin is added.
**Find movie matches** and **Find episode matches** propose a match for every file that still needs
one: the server reads the title, year, season and episode (or episode range) from the step's input
name (`Show.Name.S02E05.1080p`, `Show 2x05`, `Movie.Name.2019.1080p.BluRay`, a bare `S01E05.mkv`
inside a show folder), searches TMDB, scores the results and verifies the best one. Lookups use the
same TMDB concurrency limit, timeouts and cache as manual searches, one file at a time, and a busy
or rate-limited TMDB (HTTP 429) is retried after 1, 2 and 4 seconds before the row reports it. The
confidence (0–100%) is three quarters title similarity — exact after ignoring case, accents and
punctuation, against the title or original title, else partial — and one quarter year (same year,
one year apart, or unknown); an equally good rival without a year to tell them apart (remakes,
same-named shows) lowers it. A proposal is never applied by itself: **Accept high-confidence
matches** confirms every proposal at 85% or more in one go, and the rest are marked **Check
proposal** to accept or correct one by one. The API is `POST /api/metadata/tmdb/suggestions` with
the step binding (`fileId`, `instanceId`, `inputPath`, `optionsHash`), `mediaType` and `language`;
it returns the parsed name, the candidates, the verified `match` (with its confirmation token) and
its `confidence`, or a `problem`.
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

By default a run accepts up to 200 files and 32 plugin instances, and the durable queue
accepts up to 32 queued jobs, reporting overload explicitly. `Namerre:Limits` changes them
(`MaxFilesPerRun` up to 2000, `MaxStepsPerRun` up to 64, `MaxQueuedJobs` up to 256, for
example `Namerre__Limits__MaxFilesPerRun=500`); a value outside 1 to its ceiling stops
startup. Larger runs mean longer preparation, since every file is hashed or checked, and
larger journal writes while they execute. Signed-in sessions receive the effective limits
(`limits` on `GET /api/auth/session`), so the app shows the configured numbers. These
bounds do not prescribe which types to use or their order; repeated instances are
independently configured. Larger collections can still be browsed, but must be split
before preparing a run.
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
Application; keep HTTP concerns in the API. Type-level internals (request flow, plugin model,
TMDB, users and authentication, frontend structure) are described in
[docs/architecture.md](docs/architecture.md).

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
that can read the upstream packages. CI and the Docker workflow use it for
restore. Fork pull requests cannot access this secret and require a trusted run
with package access; do not expose package credentials to untrusted code.
Backend CI runs on both Windows and Linux so filesystem-sensitive behavior is not
validated only through platform-independent string comparisons.

The pipeline has three workflows; shared Node.js setup lives in the
`.github/actions/setup-node` composite action:

- **CI** (`.github/workflows/ci.yml`) runs on pull requests to `main` and `next`, and
  is reused by the other two workflows. It never builds or publishes a Docker image.
  Its jobs:
  - `backend` builds the solution and runs the backend tests on Ubuntu and Windows;
  - `frontend` lints and tests the Angular app;
  - `betterleaks` scans the full Git history with a pinned, checksum-verified
    [Betterleaks](https://github.com/betterleaks/betterleaks) release and fails on any
    finding (redacted in the log). Mark a false positive with a `betterleaks:allow`
    comment on the line, or add its fingerprint to `.betterleaksignore`;
  - `commitlint` (pull requests only) rejects any commit that does not follow
    [Conventional Commits](https://www.conventionalcommits.org).
- **Release** (`.github/workflows/release.yml`) runs on every push to `main` and
  `next`. It runs CI, then [semantic-release](https://semantic-release.gitbook.io)
  derives the next version from the commits since the last release: `fix:`/`perf:`
  bump the patch version, `feat:` the minor version, and a `!` after the type or a
  `BREAKING CHANGE:` footer the major version. Other types (`docs:`, `chore:`,
  `refactor:`, `test:`, `ci:`, `build:`, `style:`) publish nothing. `main` produces
  stable `vX.Y.Z` tags; `next` produces `vX.Y.Z-pre.N` pre-releases, and merging
  `next` into `main` promotes them. It pushes the tag, creates the GitHub release with
  a changelog grouped by commit type, and then calls the Docker workflow for the tag
  (without testing the same commit again). Nothing is committed back to the repository.
- **Build and publish Docker image** (`.github/workflows/docker.yml`) runs only for
  version tags (`vX.Y.Z` or `vX.Y.Z-pre.N`). It publishes the image to `ghcr.io` and
  appends the image tags and digest to the release notes (creating the release with
  GitHub's generated notes if the tag was pushed by hand). A tag pushed or dispatched
  by hand runs CI first. Stable tags are published as `latest`; pre-release tags as
  `prerelease`. Both also get the version tag and the commit SHA as image tags.
  Dispatching it on anything other than a version tag fails.

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
