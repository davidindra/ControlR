# Phase 4 — Authentication schemes

Five credential types reach the server. `CustomSchemes.Dynamic` picks between them per request.

## Scheme selection

`Startup/AuthenticationRegistrationExtensions.cs`, `ForwardDefaultSelector`, in order:

1. Path starts with `/device-access` **and** a `logonToken` query parameter → logon-token scheme
2. `EnableInteractiveBearerLogin` **and** an `Authorization: Bearer` header → Identity bearer
3. A PAT header present → PAT scheme
4. A service-account API key header present → service-account scheme
5. Otherwise → Identity application cookie

Checked for downgrade: there is none. The order is first-match, and every branch terminates in a
handler that validates a real credential; no branch falls back to a weaker one on failure. A
request carrying both a PAT header and a valid cookie authenticates as the PAT and fails if the
PAT is bad — surprising for a user, not a security hole.

## Handler review

**PAT** (`PersonalAccessTokenAuthenticationHandler`) — good. Two-axis throttling, per source IP
and per token, five failures in a five-minute window, with a comment explaining why combined keys
would be weaker. Malformed token prefixes collapse to a single `"invalid"` cache key so the cache
cannot be flooded with attacker-chosen entries. Lockout is re-checked after validation. Validation
itself goes through `IPasswordHasher`, so the comparison is constant-time.

**Service account** (`ServiceAccountCredentialAuthenticationHandler`) — same shape, with the
limits configurable via `ServiceAccountAuthFailureLimit` / `...WindowMinutes`. Failure counters
are cleared on success.

**Logon token** (`LogonTokenAuthenticationHandler`) — single-use, consumed on validation, requires
a matching `deviceId`, re-checks lockout, and issues a principal scoped to that one device via
`UserClaimTypes.DeviceSessionScope`. Well constrained. The weakness is transport, not logic: the
token is a query parameter, and with no `Referrer-Policy` set it leaks to any third-party resource
the page loads ([F-07](findings.md#f-07)).

**Identity cookie** — standard. `ConfigureApplicationCookie` converts login redirects into 401/403
for `/api` paths, which is the right behavior for an SPA.

**External providers** — Microsoft and GitHub, registered only when both client ID and secret are
configured.

Both memory-cache-based throttles share the per-instance limitation in
[F-10](findings.md#f-10).

## Identity configuration

```csharp
options.User.RequireUniqueEmail = appOptions.RequireUserUniqueEmail;       // default true
options.SignIn.RequireConfirmedEmail = appOptions.RequireUserEmailConfirmation; // default false
options.Password.RequiredLength = 8;
options.Password.RequireNonAlphanumeric = false;
```

Lockout is left at ASP.NET Core defaults (5 attempts, 5 minutes), which is reasonable. An
eight-character minimum without a complexity requirement is defensible in 2026 — length plus
lockout beats character-class rules — but it should be paired with a breached-password check or a
higher minimum for administrator accounts, since the admin bootstrap account is the highest-value
credential on the server.

`RequireConfirmedEmail = false` by default interacts with [F-01](findings.md#f-01) in the
operator's favor: `ForgotPassword` returns early unless the account's email is confirmed, so on a
default deployment many accounts are not reset-eligible at all. That is an accident of
configuration rather than a control, and it should not be relied on.

## Registration paths

Three ways an account can come into existence, all appropriately gated:

- First-user bootstrap — available only while the server has zero users, then self-disables;
  `DisableFirstUserSelfRegistration` turns it off entirely. Covered by
  `DisableFirstUserSelfRegistrationTests` and `FirstUserTests`.
- `EnablePublicRegistration` — ongoing open signup, default `false`.
- Invites — anonymous `POST /api/invites/accept` with an invite token in the body. Covered by
  `InviteAcceptanceTests` and `InvitesControllerTests`.

`IdentityApiRegisterFilter` blocks the `MapIdentityApi` built-in `/register` endpoint, so enabling
interactive bearer login does not silently open a fourth path. That is a good catch on the
project's part.

## The account flows

This is where the review found its most serious issue. `/Account/ForgotPassword` builds the reset
link from the request host, and `AllowedHosts` is `*` — see [F-01](findings.md#f-01). The
email-confirmation link in `UserCreator.cs` is built the same way and should be fixed alongside
it.

## Recommendations

1. Fix [F-01](findings.md#f-01): pin `AllowedHosts`, derive callback URLs from configuration.
2. Set `Referrer-Policy: no-referrer` so logon tokens cannot leak via `Referer`.
3. Consider a higher password floor or a breached-password check for administrators.
4. If the server is ever scaled beyond one replica, move the throttle counters to a shared store.
