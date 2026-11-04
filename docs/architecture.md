# Architecture notes

Developer-oriented description of how Namerre is put together: which types live where and how
the pieces cooperate. User and operator documentation (configuration, deployment, security,
workflow) lives in [`README.md`](../README.md); the rules contributors and coding assistants must
follow, including the C#/TypeScript "keep in sync" pairs, are in [`CLAUDE.md`](../CLAUDE.md).

## Layering and composition

Clean architecture, dependencies point inward:
`Domain` ← `Application` ← `Infrastructure` ← `Api` (composition root; also references Application).
Application must not reference Infrastructure or ASP.NET Core; define interfaces in Application,
implement in Infrastructure. Each project registers its own services through a `DependencyInjection`
extension — `AddApplication()`, `AddInfrastructure(configuration)` and the Api's `AddApi()` (web host
services, incl. the `IRenameNotifier` implementation) — and `Namerre.Api/Program.cs` only calls them.

## Request flow

Controllers (`Namerre.Api/Controllers`, namespace `Namerre.Controllers`, routes always start with
`api/` — `RenamingController` uses per-action routes like `api/rename-plans/{id}`) send CMediator
requests via `ISender`. Each request and its handler live in separate files (`XQuery.cs` +
`XQueryHandler.cs`), grouped by feature folder with a matching namespace
(`Application/Renaming/{Plugins,Previews,Plans,Runs,Rollback,Recovery,Metadata}`, `Application/Folders`,
`Application/WatchFolders`, `Application/Authentication`, `Application/Users`). There is no exception:
`AuthController` and `UsersController` send account requests too, and keep only HTTP concerns (cookie
sign-in/out and renewal, claims, antiforgery, OIDC challenges and link tickets).

Renaming handlers are thin pass-throughs to `IRenameService` (the run export handler also formats
through `Runs/RunExportWriter`: RFC 4180 CSV with BOM and formula-injection guard, or JSON); the real
logic lives in `Namerre.Infrastructure/Renaming` (`RenameService` is the singleton behind it), split by concern:

- `RenameService`: the `IRenameService`/`IUserHistory` surface: validation, history scoping (`Viewer`),
  preview slots and queueing. It builds the internal collaborators below.
- `RenameJobRunner`: `ProcessNext` takes the next queued job and runs prepare, execute (rename or
  rollback) or recovery, recording job telemetry and failures.
- `JournaledMover`: journal intents and completions around staging moves, directory creation and
  empty-folder cleanup; `FileVerifier` re-checks the prepared hash or size/time stamp.
- `RollbackPlanner` / `RecoveryPlanner`: build the rollback and recovery previews that both the
  commands and the jobs rely on; `RollbackCheckTracker` keeps per-file progress of in-flight checks.
- `RenameCoordinator`: the gate that serializes durable state changes, the "storage unavailable" stop
  flag, queueing and the trace origin of each job; `RenameRecorder`: durable plan/run writes followed
  by change and library notifications.

DTOs are one type per file: feature-specific ones (e.g. `RunView`, `PlanView`, `TmdbMatch`) sit in
their feature folder; shared ones (`PipelineRequest`, `PluginInstance`, `RenameIssue`,
`JobAccepted`, …), the `IRenameService`/`ITmdbMetadata` contracts and
`RenameException(code, message, status, fileId)` (which carries API error codes) stay directly in
`Application/Renaming`.

## Renaming pipeline

### Plugins

Each plugin is an `IRenamePlugin` in `Application/Renaming/Plugins` (`LiteralPlugin`,
`ReplacementTablePlugin`, `RegexPlugin`, `CasePlugin`, `ExtensionCasePlugin`, `AffixPlugin`,
`SeparatorsPlugin`, `ReleaseTagsPlugin`, `CharactersPlugin`, `InsertPlugin`, `RemovePlugin`,
`TruncatePlugin`, `PadNumbersPlugin`, `CounterPlugin`, `DatePlugin`, `TmdbPlugin`; counter and date
share `NameInsertion` for prefix/suffix/template placement, insert/remove/truncate count grapheme
clusters through `TextElements`), usually derived from `RenamePlugin<TOptions>` (typed options,
camelCase JSON).

Its `PluginDescriptor` carries the type ID, version, name, description, default options, the option
`Form` the frontend renders and `SingleInstance` (at most one instance per pipeline, enforced by the
engine and the editor; set for TMDB).

- Form fields are `OptionFieldKind`s: `text`, `textarea`, `checkbox`, `select`, `multiSelect`,
  `number` with optional min/max/step, and `list` of non-nested row columns.
- Groups can require checkboxes (`Requires`); fields can show only while a select holds (or a
  multi-select includes) given values (`VisibleWhen`, e.g. counter/date templates via
  `NameInsertion`, case short words, separators' short numbers). Hidden values are still sent.
- `OptionBinding` checks options against the form before typed binding (malformed values →
  `InvalidConfiguration` naming the field) and rejects forms the editor can't render when the
  engine is built.
- Plugins are registered as singletons in `AddApplication()` (registration order = catalog order).

### Engine

- The engine and its helpers (`PipelineEngine`, `IRenameFileSystem`, `CompanionFiles`/`Companion`,
  `StepCondition`, `StepTrace`, `FileFacts`, `FileInput`, `TargetOverride`) live in
  `Application/Renaming/Pipeline` (namespace `Namerre.Application.Renaming.Pipeline`); contracts and
  shared DTOs stay directly in `Application/Renaming`.
- `PipelineEngine` takes the plugins via DI, keys them by `(TypeId, Version)`, and applies enabled
  plugin instances in the request's order — catalog order never determines execution order.
- Plugins return `PathParts`; the engine rejects extension changes other than case (only
  `ExtensionCasePlugin` makes those), empty stems and separators, then validates the path.
- With `PipelineRequest.OnCollision = "AddNumber"` the engine gives taken targets a ` (n)` suffix
  (subtitles a `.n` token before their language tokens, `Name.1.en.srt`)
  (`PreviewFile.Numbered`, `collision` trace step) before related files are named; prepare stores
  the result.
- With `PipelineRequest.IncludeCompanions`, `CompanionFiles` pairs subtitles, `.nfo` and artwork with
  a video in the same folder; those skip the steps and take the video's final folder and stem plus
  their suffix (`PreviewFile`/`PlanFile`/`RunFile.CompanionOf`). The frontend mirrors the rules in
  `companions.ts` to group the collection.
- Steps also get a `FilePosition` (request and source-folder index and counts) and `FileFacts`
  (size, modified and creation time via `IRenameFileSystem.Facts`); `PrepareJob` snapshots the facts
  into `PlanView.Facts` once and previews with them, so a plan never re-reads them.
- Tests build the engine with the built-in plugins through `TestPipeline.Create`.

### Lifecycle

preview (no mutation) → prepare (hash every source file, persist an immutable plan) → execute
(queued job) → optional rollback of selected files / explicit recovery. Run state changes must
satisfy `Domain/Renaming/RunTransitions.Allows`; naming rules are in `Domain/Renaming/NamingRules.cs`.

- `RenameWorker` (hosted service) polls `RenameService.ProcessNext`, so jobs run independently of
  the browser.
- `WatchFolderWorker` calls `IWatchFolders.Scan` every few seconds. The rules live in
  `Application/WatchFolders/WatchFolderService`: configured `Namerre:WatchFolders` (validated by
  `WatchFolderOptions.Problem`, exposed to Application through `IWatchFolderConfiguration`) are
  listed through `IFolderBrowser`, stable files previewed with their preset and prepared through
  `IRenameService.Prepare(request, origin, …)` (`origin` = `watch:<name>` on `PlanView`/`RunView`),
  then executed. Per-file state (`WatchFileState`, persisted as strings) with back-off and pausing
  go through the `IWatchedFileStore` port (Infrastructure `WatchedFileStore` over the `WatchedFiles`
  table and `Settings`); telemetry goes through `IWatchFolderDiagnostics`. TMDB steps need the
  folder's `TmdbAutoConfirm` threshold. `WatchFolderServiceTests` (Application unit tests, with
  `FakeTimeProvider`) cover the rules; `Namerre.Api.Tests/WatchFolderTests` covers the database path.
- Runs are journaled (with staging paths), not atomic; interrupted mutations are never blindly resumed.

## Live updates

After each durable plan/run write, `RenameRecorder` calls `IRenameNotifier` (Application contract,
best effort, never throws into the job). `HubRenameNotifier` (`Namerre.Api/Notifications`)
broadcasts a `RenameChange` nudge as `renameChanged` on the authorized `RenameHub`
(`/api/hubs/rename`). In the frontend, `rename-events.ts` (`RenameEvents.watch` and `refreshOn`)
re-reads records over REST on each nudge, falling back to polling while disconnected; specs
substitute `FakeRenameEvents` from `rename-events.testing.ts`.

## Filesystem

All path access goes through `BoundedFileSystem` (`IRenameFileSystem`) and `ServerFolderBrowser`
(`IFolderBrowser`), rooted at `Namerre:RootPath`. No escape from the root or traversal via
symlinks/junctions; unsupported filesystems fail closed; no overwrites and no cross-device copy
fallback. The data directory and media root must not contain each other.

## Persistence

`RenameStore` wraps EF Core SQLite (`RenameDbContext`) in `Namerre:DataPath`; it runs `Migrate()` on
startup and refuses unknown applied migrations. Migrations live in
`Namerre.Infrastructure/Renaming/Migrations`. Add migrations with
`dotnet ef migrations add <Name> -o Renaming/Migrations` from `Namerre.Infrastructure`.

Ended sessions are stored there too (`RevokedSessions` and `SessionEpochs`, `SessionRegistry` behind
the Application `ISessionRegistry`; see Users and authentication). Local users are stored in the same
database (`Users` and `UserLogins` tables, `UserStore` behind the
Application `IUserStore`), and so are pipeline presets (`Presets` table, `PresetStore` behind the
Application `IPresetStore`; `PresetService` validates through `PipelineEngine.ValidatePlugins`,
served by `RenamePresetsController` at `api/rename-presets`).

## TMDB

`TmdbMetadata` calls TMDB server-side through `CachedTmdbClient` (singleton holding a private
256-entry, 10-minute `MemoryCache`), which takes a fresh typed `TmdbClient` per lookup. The client's
Polly pipeline (`TmdbResilience`: four-lookup concurrency limiter, 15 s total / 5 s attempt
timeouts, retries that skip 429, circuit breaker) sits in front of it, and `TmdbClient` maps its
rejections to API errors. Requests carry `Namerre:Tmdb:ReadAccessToken`.

- Matches must be confirmed with a `ConfirmationToken` bound to `TmdbPlugin.OptionsHash` — SHA-256
  of the plugin's `language` only (the `preset` option, which the frontend's `tmdb-presets.ts` uses
  to fill templates, is ignored by rendering and the hash). The frontend reimplements this in
  `Namerre.Front/src/app/modules/renamer/options-hash.ts` (pure-JS SHA-256 so it works on
  plain-HTTP origins). Confirmed metadata is snapshotted into the plan.
- Templates write literal braces as `{{`/`}}`; `TmdbPlugin.Render` and `tmdb-template-preview.ts`
  must agree.
- Automatic suggestions run server-side in `Application/Renaming/Metadata/TmdbSuggester` (parse
  with `TmdbFileName`, score with `MatchConfidence`, verify the best candidate); the frontend's
  `tmdb-filename.ts` mirrors the parser to prefill the picker and `HIGH_CONFIDENCE` mirrors
  `MatchConfidence.High`.
- A multi-episode match carries `EpisodeEnd` (titles joined by `EpisodeTitles.Join`); `{episode}`
  renders the range in the step's `episodeRange` style on both sides.

## Configuration

Everything app-specific is under the `Namerre` section, bound to `NamerreOptions`
(`Infrastructure/Configuration`) and consumed via `IOptions<NamerreOptions>`:

- `RootPath`, `DataPath`, `PublicOrigin` (optional public HTTPS origin; `Namerre.Api/Authentication/PublicOrigin.cs` validates it and
  `Passkeys:Origin`, each falling back to the other; pins passkey ceremonies and the OIDC post-logout return address
  instead of trusting the request `Host`; set `AllowedHosts` to the public host too).
- `Limits`: bound to the Application `RenameLimits` singleton; out-of-range values fail startup;
  signed-in `GET api/auth/session` reports them to the frontend's `WorkloadLimits`. Defaults:
  ≤200 files and ≤32 plugin instances per run, ≤32 queued jobs, ≤4 concurrent previews
  (`MaxConcurrentPreviews`; ceilings 2000/64/256/16); fixed regex
  timeout 100 ms for user patterns (1 s for `ReleaseTagsPlugin`'s built-in ones), TMDB lookups 15 s overall (5 s per attempt).
- `Authentication`, incl. optional `Oidc` single sign-on, e.g. Keycloak — see
  `Namerre.Api/Authentication/OidcLogin.cs`; the OIDC scheme is hidden by `OidcAwareSchemeProvider`
  unless `Authority` is set — and `Passkeys` (`Enabled`, optional `Origin` override of `PublicOrigin`; see
  `Namerre.Api/Authentication/PasskeyLogin.cs`). Without a pinned origin `PasskeyLogin.RelyingParty` yields a
  relying party only for `localhost` and null otherwise (passkey endpoints 404, `passkeysEnabled` false).
- `Tmdb`, `ReverseProxy`.
- `DataProtection`: optional PFX (`Certificate`, `CertificatePassword`, `PreviousCertificates`) that
  encrypts the Data Protection key ring in `DataPath/keys`. `DataProtectionCertificates`
  (Infrastructure) loads and checks the files (RSA key with private key, `DefaultKeySet` so it works on
  Windows and Linux; messages never include passwords) and `DataProtectionOptionsValidator` fails startup
  on a problem. The Api applies it in `Security/KeyRingEncryption.cs`: `CertificateKeyEncryptor` /
  `CertificateKeyDecryptor` reimplement ASP.NET Core's `EncryptedXml` scheme lazily from
  `IOptions<NamerreOptions>`, because `ProtectKeysWithCertificate` needs the certificate while services
  are registered, before the final configuration exists. Retired certificates only decrypt. Existing
  unencrypted keys stay readable. Without a certificate `Program.cs` logs a one-time startup message
  (Warning, Information in Development) that the keys are unencrypted. DPAPI is intentionally not used.
- `Telemetry`: optional OTLP export, wired in `Namerre.Api/Telemetry/TelemetrySetup.cs`; off unless
  `OtlpEndpoint` is set. Namerre's own `ActivitySource`/`Meter` and instruments live in
  `Infrastructure/Diagnostics/NamerreTelemetry`, backlog gauges in `RenameStateMetrics`. Requests
  whose URL may hold a secret are marked with `NamerreTelemetry.Untraced`. README lists every span
  and metric.
- `Integrations`: optional Jellyfin/Emby/Plex library refresh and webhook after a
  `Completed`/`Recovered` run. `RenameRecorder.Complete` calls `ILibraryNotifier.RunFinished`,
  implemented by `Infrastructure/Integrations/LibraryNotifier`, a bounded channel drained by a
  hosted service; best effort. `NamerreOptionsValidator` fails startup on bad URLs or
  missing/orphaned keys.

## Users and authentication

Every session belongs to a local user (`Application/Authentication`: `UserRecord`,
`UserLoginRecord`, `UserPasskeyRecord`, `UserRole` `Admin`/`User`, `IUserStore`, `ICurrentUser`).

- Account rules live in `Application/Authentication/UserAccounts` (a singleton registered by
  `AddApplication()`, caching users in memory, mutations under one lock, compare-and-swap on each
  user's random credential stamp): unique NOCASE usernames, `PasswordPolicy` (12 characters for new
  passwords), at least one enabled admin, no self-demotion/disable/delete, confirmation of sensitive
  own changes (`ConfirmOwnChange`: a recent sign-in or the password), and an enabled user always
  keeps a password, a passkey or a linked identity (`UserRecord.SignInMethods`). It depends only on ports: `IUserStore`, `IPasswordHasher`
  (`Infrastructure/Authentication/PasswordHasher`, Identity V3 PBKDF2-SHA512 at 210,000 iterations)
  and `IAdministratorConfiguration` (`AdministratorConfiguration` over `Namerre:Authentication`), so
  `Namerre.Application.UnitTests` covers the rules with an in-memory store and a fast test hasher.
- Controllers reach the rules through thin requests: own account and setup in
  `Application/Authentication` (`RequiresSetupQuery`, `GetUserQuery`, `CompleteSetupCommand`,
  `PasswordSignInCommand`, `VerifyPasswordQuery`, `ConfirmOwnChangeCommand`, `SetPasswordCommand`,
  `RemoveOwnPasswordCommand`, `UnlinkOwnLoginCommand`, `EndSessionCommand`, `EndOtherSessionsCommand`,
  `SessionEpochQuery`, the passkey requests below), administration in `Application/Users`
  (`ListUsersQuery`, `CreateUserCommand`, `UpdateUserCommand`, `RemovePasswordCommand`,
  `LinkLoginCommand`, `UnlinkLoginCommand`, `MoveLoginCommand`, `MergeUsersCommand`, `DeleteUserCommand`). Authentication
  plumbing that runs outside a controller — cookie validation, the OIDC ticket handler and
  `HubRenameNotifier` — uses `UserAccounts` directly.
- Every access change (role, disabled, password, link, passkey) replaces the stamp, which
  `AuthenticationSetup.IsValidSession` checks on each request, so sessions end at once.
- Sessions are stateless cookies, so ending one on purpose needs server state. Each session carries
  a random `session-id` and the user's `session-epoch` from when it was issued (`UserSession.Create`;
  `Renew` keeps both unless told otherwise). The Application port `ISessionRegistry` (implemented by
  `Infrastructure/Authentication/SessionRegistry`) holds revoked session IDs and a per-user epoch;
  `UserSessions` (singleton, `AddApplication()`) turns that into `IsActive(sessionId, userId, epoch)`,
  which `IsValidSession` calls on every request, and into `End` (sign-out) and `EndAll`. A session is
  valid while its ID is not revoked and its epoch is not below the user's current one; a cookie
  without an ID (issued before this existed) is rejected. `POST api/auth/logout` sends
  `EndSessionCommand`; `POST api/auth/sessions/revoke-others` sends `EndOtherSessionsCommand`, which
  advances the epoch, and the controller re-issues the current session with the new epoch so only it
  survives (`SessionEpochQuery` supplies the epoch for new sign-ins). The epoch is keyed by user ID
  without a foreign key, so the never-stored break-glass administrator (ID 0, stamp derived from
  configuration) gets it too. The registry follows the `UserAccounts` approach: this process owns the
  database exclusively, so `RevokedSessions` (`SessionId`, `ExpiresAtUnix`) and `SessionEpochs`
  (`UserId`, `Epoch`) are loaded once and served from memory, with every change written through
  (epoch changes persist before they take effect and fail the request with 503 otherwise; a failed
  revoke write is logged and still revokes in memory until restart). Revoked IDs are kept for
  `SessionPolicy.Lifetime` (the eight-hour cookie lifetime) and pruned on load and on each revoke.
  A server-side session table (ASP.NET `ITicketStore`) was not used: it would add a database read or
  cache entry per live session and change the cookie format, while a revocation list and an epoch stay
  small and keep sessions stateless until someone ends them.
- `UserSession` builds the cookie principal from the local user (ID, name, role claim, stamp,
  method `password`/`passkey`/`oidc`, sign-in time; SSO sessions add the `OidcLogin.Stamp`) and
  defines the live-update group.
- Rate limits (`AuthenticationSetup.AddRateLimiter`, 429): `LoginPolicy` (`login`, `setup`, `passkey/login`) allows
  5 attempts/min per client address (IPv6 by /64), checked first, then a shared 60/min ceiling
  (`ClientThenSharedLimiter`). The ceiling lives in the policy, not in a `GlobalLimiter`: the middleware takes the
  global permit before the endpoint's, so a client rejected by its own budget would still burn shared permits.
  `AccountPolicy` (signed-in `password`, `reauthenticate`, `passkey/reauthenticate`, `passkeys/options`,
  `oidc/link`) allows 5/min per user ID with no shared ceiling, so anonymous traffic cannot block signed-in users.
- A configured `Namerre:Authentication` account is a never-stored break-glass `Admin` with ID 0.
- Usernames (`Application/Authentication/UsernameSkeleton`, used by `UserAccounts.IsValidUsername`, the
  single rule for setup, creation and provisioning): new names are trimmed and normalized to NFKC, and
  refused (`InvalidUsername`) if they hold control, format (bidi overrides, zero-width characters, BOM),
  line/paragraph separator, surrogate-half, private-use, unassigned or noncharacter code points, Hangul
  fillers, or any space other than U+0020. `UsernameSkeleton.Of` is a pragmatic confusable key (NFKC,
  diacritics and format characters dropped, case folded, a small Cyrillic/Greek/Latin look-alike table,
  and `0`→`o`, `1`/`i`/`l`/`|`→`l`); creating a user whose skeleton equals an existing user's (the
  configured administrator included) is refused with 409 `UsernameConfusable`, and provisioning
  (`ProvisionedUsername`) strips what `Clean` drops from the claim and moves on to the next `-N` suffix.
  Uniqueness stays NOCASE in the database. Existing stored users and the configured administrator are
  never revalidated, so legacy names keep signing in; only new names are checked.
- SSO identities are keyed on (issuer = configured authority, `sub`); `OidcLogin.Read` checks
  `RequiredRole`, then `AuthenticationSetup.OnTicketReceived` resolves the linked user, provisions a
  `User` when `Oidc:AutoProvision` allows (never matching by username), or links the identity when
  the challenge carries the protected link items from `AuthController.LinkSingleSignOn` (needs a
  recent sign-in or the password).
- Passkeys (WebAuthn): `Application/Authentication/PasskeyAccounts` (singleton) runs the ceremonies
  through the `IPasskeyCeremonies` port and applies `UserAccounts` rules (`AddPasskey`,
  `RenamePasskey`, `RemovePasskey`, `FindPasskey`, `TryRecordPasskeySignIn`, which updates the counter only if it is
  still the one verified against and fails the sign-in otherwise, storage failures included; at most
  20 per user, merges included, credential IDs unique
  across users, one random user handle per user). `Infrastructure/Authentication/PasskeyCeremonies`
  implements the port with Fido2NetLib: discoverable credentials, user verification required, no
  attestation; its opaque state holds the original options and expected origin. Thin requests:
  `BeginPasskeySignInQuery`, `PasskeySignInCommand`, `BeginPasskeyRegistrationCommand` (confirms
  like linking SSO), `CompletePasskeyRegistrationCommand` (pinned to the confirmed stamp),
  `RenamePasskeyCommand` (own account; a label only, so the stamp stays), `RemoveOwnPasskeyCommand` (own account)
  and `RemovePasskeyCommand` (`api/users`). `Namerre.Api/Authentication/PasskeyLogin`
  derives the relying party (configured `Origin`, else the request's scheme and host) and carries
  the state through the browser in time-limited Data Protection tickets bound to a purpose (and
  user + stamp for registration), redeemed once against an in-memory nonce set; a random protector
  subpurpose per process makes tickets die with it, so a restart cannot revive a redeemed one. The
  registration state leaves out the excluded credentials to stay small. A passkey sign-in
  is a fresh sign-in (`UserSession.IsRecent`). Merging moves passkeys with the identities.
  `Namerre.Infrastructure.UnitTests/Authentication/SoftwareAuthenticator` (linked into
  `Namerre.Api.Tests`) is an ES256 software authenticator that answers real WebAuthn JSON.
- Moving a link can merge its user into the target (`MergeUsersCommand`: `UserAccounts.Merge`, then
  `IUserHistory.Reattribute`, implemented by `RenameService` under its job gate, rewrites
  `CreatedBy`/`RecoveredBy`; a failure there is `HistoryNotMerged` (503) and keeps the merge).
- History is per user: `RenameDocument.OwnerId` (the plan's `CreatedBy`; a run, rollbacks included,
  takes its plan's owner; null for watch folders and pre-user history) scopes every plan/run read in
  `RenameService` through `Viewer` (null for administrators and background work, from
  `ICurrentUser.IsAdmin`), so someone else's record is `NotFound`, and `HubRenameNotifier` only
  nudges administrators and the owner (`RenameChange.OwnerId`, never serialized).
- `UsersController` also carries `RecentSignInFilter` (an authorization filter ordered after
  `[Authorize]`): safe methods pass, every other request needs `UserSession.IsRecent` (an injectable
  `TimeProvider`, 10 minutes) and is otherwise answered exactly like
  `AccountException.RecentSignIn()` (`403 { code: "RecentSignInRequired", message }`, built by
  `AccountExceptionFilter.Result`). It applies to the whole controller, so new endpoints are covered
  by default. The Users page catches that code, shows the shared `ConfirmIdentity` component (password
  via `POST api/auth/reauthenticate`, passkey, or single sign-on through
  `oidc/login?returnTo=users`) in a dialog and sends the refused change again.
- `AuthenticationSetup.AdminPolicy` (`RequireAdmin`) guards `api/users`, watch folders and run
  deletion; `AccountException` (Application) carries refusals, which `AccountExceptionFilter` maps to
  `{ code, message }` with the exception's status. `HttpCurrentUser` feeds
  `RenameService`, which records `CreatedBy` on plans/runs and `RecoveredBy` on recoveries.

## Hosting

`Program.cs` requires HTTPS for `/api` (400 otherwise), sets `no-store` on API responses, requires
authorization on all controllers, returns 404 for unmatched `/api/*`, and falls back to `index.html`
otherwise. OpenAPI is exposed only in Development. Static files go through
`Hosting/StaticFileCaching` (`no-cache` for unhashed shell files: `index.html`, the manifest and the
service worker).

## Frontend

Standalone Angular app (signals, explicit component imports, external templates + `styleUrl`, `app`
selector prefix, SCSS component styles, Tailwind CSS v4 via PostCSS in `src/styles.css`).
`src/main.ts` bootstraps `App` with `appConfig`; app-wide providers go in `src/app/app.config.ts`,
routes in `src/app/app.routes.ts`. Use same-origin `/api/...` URLs.

- **Auth.** `Auth` (`src/app/auth/auth.ts`) holds the session (`role`/`isAdmin`, `hasPassword`,
  `logins`, `passkeys`/`passkeysEnabled`, `recentSignIn`) and runs the passkey ceremonies through
  `auth/passkeys.ts` (base64url ↔ `navigator.credentials` and WebAuthn JSON; `passkeysSupported`
  needs a secure context); `auth/account` manages the user's own password, passkeys and SSO links; `users/`
  is the admin-only user management page (route guarded by `isAdmin`, linked from `settings-menu`);
  admin-only controls (watch folders, run deletion) are hidden for users.
- **Renamer.** The root route lazy-loads `modules/renamer`; `rename-api.ts` is the HTTP client for
  the resource-style renaming endpoints (`/api/rename-plugins`, `/api/rename-previews`,
  `/api/rename-plans`, …) plus `renameError`/`operationId`, and `rename-models.ts` holds the
  request/response shapes and pure helpers over them. The workflow is a staged wizard
  (`rename-workflow` component) across file selection, plugin configuration, TMDB matching,
  preview, confirmation and run history. Its state and transitions (stage, step draft, run options,
  preview → prepare → execute lifecycle, TMDB suggestions and picker memory, Simple/Advanced
  switches) live in `WorkflowStore` (`rename-workflow/workflow-store.ts`), a signals-based service
  the component provides and renders; the component only feeds it the collection and moves focus
  when `stageEntered` fires. Test state logic in `workflow-store.spec.ts` and rendering in the
  component specs.
  Completed stages in its progress list are buttons (`goToStep`) that jump back with the same
  cleanup as **Back**; forward moves only go through `next()` and its checks.
- **Modes.** Chosen per browser through the `WorkflowMode` service (`src/app/workflow-mode`,
  `localStorage` like `Theme`; switch in the header options menu (`settings-menu`), first-run
  onboarding, and a warning before a switch to Simple discards steps). **Advanced** is the full
  wizard; **Simple** runs files → preset (`preset-picker`) → [matching] → review (preview and
  confirmation on one screen) with fixed `simpleRunOptions`.
- **Simple presets.** Built-in presets are data in `modules/renamer/simple-presets.json`
  (`simple-presets.ts` builds and recognises them);
  `Namerre.Application.UnitTests/Renaming/SimplePresetTests` links that file and previews every
  preset and select choice against the built-in catalog.
- **Dialogs and confirmations.** Every confirmation is a modal `<dialog>` (`src/app/dialogs`;
  `modal.ts` opens and closes one, falling back to the `open` attribute where `showModal` is
  missing). For a yes/no question, `await inject(Confirmations).ask({ heading, message,
  confirmLabel, danger })`: `ConfirmationHost` (in the app shell) shows it and the promise resolves
  to the answer. When the dialog must stay open for progress or errors (run deletion), render
  `<app-confirm-dialog>` with `@if`, project the content and bind `busy`; it opens when rendered,
  closes when removed, and Escape counts as Cancel. Destructive dialogs (`danger`) focus Cancel
  first. Dialogs with a form (passkey rename, "Confirm it is you", add user) are `<dialog>`s in the
  page that keep server errors inside the dialog.
- **Plugin editor.** `plugin-editor` renders each step's options from the descriptor's `form`, so a
  new plugin needs no frontend change unless it has extra UI (like TMDB's template preview).
- **Build output.** Angular builds into `Namerre.Api/wwwroot` with no `browser` subdirectory
  (generated, cleared on rebuild) — put static assets in `Namerre.Front/public`. Missing asset files
  stay 404 rather than falling back to `index.html`.
- **PWA.** Installable: `public/manifest.webmanifest` plus `@angular/service-worker`
  (`ngsw-config.json`, production builds only), which caches only the app shell. `app-update` offers
  a reload when a new version is ready. `@angular/service-worker` pins `@angular/core` exactly, so
  upgrade them together.

## Tests and release pipeline

`Namerre.Infrastructure.UnitTests` covers `TmdbMetadata` (stub `HttpMessageHandler`),
`BoundedFileSystem` against temp directories and the `PasswordHasher` hash format; `Namerre.Api.Tests` holds HTTP integration tests
(`AuthWebApplicationFactory`).

CI (`.github/workflows/ci.yml`: backend build/tests on Ubuntu and Windows, frontend lint/tests,
Betterleaks, commitlint on PRs) runs on PRs to `main`/`next` and is reused by `release.yml` and
`docker.yml`. Shared Node setup is the `.github/actions/setup-node` composite action. `release.yml`
runs CI, then semantic-release (`release.config.mjs`) on pushes to `main` (stable) and `next`
(`-pre.N`), creates the tag and GitHub release, and calls `docker.yml` for the new tag; Docker images
are built and published only for version tags (a hand-pushed tag runs CI in `docker.yml` first).
CI's `betterleaks` job scans the full Git history with a pinned Betterleaks release on PRs and
pushes; the lefthook `pre-commit` hook scans staged changes when `betterleaks` is on PATH.
Issue and PR labels are declared in `.github/labels.json` (defaults, `security`, `pentest`,
`breaking change`, `dependencies` and `area: …` labels); `labels.yml` creates or updates them on
pushes to `main` that touch the file (or on demand), and never deletes labels missing from it.
