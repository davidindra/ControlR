# Consolidated findings

Severity reflects the external-attacker threat model in [README.md](README.md). "Confirmed" means
the behavior was read directly in the source and the control flow traced end to end; "needs
verification" means the reasoning holds on paper but was not exercised against a running instance.

| ID | Severity | Finding | Status |
|---|---|---|---|
| [F-01](#f-01) | High | Password-reset link is built from the request `Host` header, and `AllowedHosts` is `*` | Confirmed in source |
| [F-02](#f-02) | High | Agent hub is unauthenticated; a device with no stored public key can be taken over and re-keyed by an anonymous client | Confirmed in source |
| [F-03](#f-03) | Medium | `AllowAgentsToSelfBootstrap` lets an anonymous client create devices, and its single-tenant restriction is bypassed by naming a tenant explicitly | Confirmed in source |
| [F-04](#f-04) | Medium | Relay: responder is unauthenticated, requester has no policy, session token compared non-constant-time, first arrival defines the session | Confirmed in source |
| [F-05](#f-05) | Medium | Aspire dashboard is published on all host interfaces by the quick-start compose | Confirmed in source |
| [F-06](#f-06) | Medium | No fallback authorization policy: an endpoint added without `[Authorize]` is anonymous | Confirmed in source |
| [F-07](#f-07) | Medium | No security response headers anywhere; logon tokens travel in the query string | Confirmed in source |
| [F-08](#f-08) | Medium | Cloudflare IPv6 ranges are never trusted — the IPv4 response is read twice | Confirmed in source |
| [F-09](#f-09) | Low | `ForwardLimit = null` on both forwarded-headers branches | Confirmed in source |
| [F-10](#f-10) | Low | Rate-limit and throttle state is per-instance in memory | Confirmed in source |
| [F-11](#f-11) | Low | Data Protection keys stored unencrypted in the database by default | Confirmed in source |
| [F-12](#f-12) | Low | `ServeUnknownFileTypes = true` on the vendored `/novnc` static provider | Confirmed in source |
| [F-13](#f-13) | Info | No CodeQL or dependency-vulnerability workflow | Confirmed in source |

---

## F-01

**Password-reset link is built from the request `Host` header — High**

`ControlR.Web.Server/Components/Account/Pages/ForgotPassword.razor:58`

```csharp
var callbackUrl = NavigationManager.ToAbsoluteUri("Account/ResetPassword").AbsoluteUri;
var result = await PasswordManager.ForgotPassword(new InternalDtos.ForgotPasswordRequestDto(Input.Email), callbackUrl);
```

On a server-rendered Razor page, `NavigationManager.ToAbsoluteUri` derives its base from the
incoming request, so the host in the emailed link is whatever the attacker put in the `Host`
header. `ControlR.Web.Server/appsettings.json` sets `"AllowedHosts": "*"`, so the host filtering
middleware will not reject a forged value, and no configured public base URL overrides it.
`PasswordManager.ForgotPassword` then appends the real reset code to that attacker-controlled
origin and mails it to the victim.

Attack: POST the forgot-password form with the victim's email and `Host: attacker.example`. The
victim receives a genuine email from the real server containing
`https://attacker.example/Account/ResetPassword?code=<valid reset token>`. One click hands the
attacker a working password-reset token for that account. Nothing but the victim's email address
is needed, and the email itself is authentic.

Preconditions worth stating honestly: SMTP must be configured, the victim's email must be
confirmed (`ForgotPassword` returns early on `!IsEmailConfirmedAsync`), a reverse proxy that pins
the upstream `Host` blocks it, and the victim has to click. None of those are guarantees, and the
first and third are deployment-dependent rather than properties of the code.

Fix: set `AllowedHosts` to the deployment's real hostnames, and build account-flow callback URLs
from a configured public base URL rather than from the request. `UserCreator.cs:336-344` builds
the email-confirmation link the same way and should move with it — it already accepts a
`confirmationBaseUrl` and falls back to `NavigationManager` when one is not supplied.

## F-02

**Agent hub is unauthenticated; a device with no stored public key can be taken over — High**

`ControlR.Web.Server/Hubs/AgentHub.cs`

`AgentHub` carries no `[Authorize]` attribute — `ViewerHub.cs:19` does, `AgentHub` does not. Any
anonymous client can open the SignalR connection and invoke hub methods; identity is asserted
inside the methods. Two paths accept a device whose stored `PublicKey` is empty:

`UpdateDeviceSigned` (line 320) picks the key to verify against:

```csharp
var publicKeyBase64 = !string.IsNullOrEmpty(storedPublicKey)
  ? storedPublicKey
  : signedDto.PublicKey;
```

When the device has no stored key and `AllowAgentsToSelfBootstrap` is on, the signature is checked
against the key the caller supplied in the same message — which always verifies.

`UpdateDevice` (line 240, marked `[Obsolete]`) is still a live, reachable hub method. Its only
guard is:

```csharp
if (device is not null && !string.IsNullOrEmpty(device.PublicKey))
{
  return HubResult.Fail<InternalDtos.DeviceResponseDto>("Device requires signed updates.");
}
```

A device with an empty `PublicKey` passes it with no signature at all.

Either path lands in `DeviceManager.UpdateDeviceEntity`, which writes:

```csharp
entity.ConnectionId = context.ConnectionId;   // now the attacker's connection
...
entity.PublicKey = publicKeyBase64;           // adopts the attacker's key permanently
```

Consequences: commands a viewer issues for that device are routed to the attacker's SignalR
connection, the attacker reports arbitrary status, and the adopted key makes the takeover survive
— from then on the attacker is the only party that can produce valid signed updates for that
device ID.

Preconditions: the attacker needs the device GUID and the tenant GUID. With self-bootstrap on a
single-tenant server the tenant GUID is not needed, because `TenantId == Guid.Empty` is resolved
to the only tenant. Which devices have an empty `PublicKey`? Ones registered through
`/api/agent/devices` without one — `requestDto.PublicKey` is optional there — and any device
predating signed updates.

What limits the blast radius, and is worth crediting: `AgentHub.UpdateDeviceEntity` routes to
`DeviceManager.UpdateDevice` when self-bootstrap is off, which refuses a device that does not
already exist, and `DeviceManager.UpdateDeviceEntity` throws on any attempt to move a device
between tenants. Cross-tenant theft is closed.

Fix: delete the obsolete unsigned `UpdateDevice` rather than leaving it reachable; make key
adoption happen only through the installer-key-authenticated `/api/agent/devices` path, never
from an unauthenticated hub message; treat a device row with no public key as a migration backlog
item and report how many exist.

## F-03

**Self-bootstrap allows anonymous device creation and its tenant restriction is bypassable — Medium**

`ControlR.Web.Server/Hubs/AgentHub.cs:549`

```csharp
if (_appOptions.Value.AllowAgentsToSelfBootstrap)
{
  var device = await _deviceManager.AddOrUpdate(agentDto, context, publicKeyBase64: publicKeyBase64);
  return HubResult.Ok(device);
}
```

With the flag on, the hub switches from `UpdateDevice` (which requires the device to exist) to
`AddOrUpdate` (which creates it). An anonymous client can then register arbitrary devices into the
tenant with no installer key — device-list pollution, unbounded row growth, and a believable
social-engineering target, since an operator who connects to an attacker-registered "device" is
connecting to the attacker.

Separately, the "self-bootstrap is only allowed on single-tenant servers" restriction only runs
inside `if (... && agentDto.TenantId == Guid.Empty)`. A caller who names an existing tenant GUID
explicitly skips the `tenants.Count > 1` check entirely and only has to clear
`_appDb.Tenants.AnyAsync(x => x.Id == agentDto.TenantId)`. The stated invariant is therefore not
enforced against a caller who does not want it enforced. The tenant-immutability guard in
`DeviceManager` still prevents moving an *existing* device, so this is creation into a chosen
tenant, not theft from one.

Default is `false` in `appsettings.json`, which is the right default. Fix: enforce the
single-tenant check whenever self-bootstrap is the authority for the write, not only when the
tenant is unspecified, and document the flag as single-tenant-lab-only.

## F-04

**Relay session controls are thin — Medium**

`Libraries/ControlR.Libraries.WebSocketRelay.Common/Middleware/WebSocketRelayMiddleware.cs`,
`.../Sessions/SessionSignaler.cs`

Four issues in one component:

1. The responder side is unauthenticated. `WebApplicationBuilderExtensions.cs` sets only
   `options.RequireAuthenticationForRequester = true`; `RequireAuthenticationForResponder`
   defaults to `false`. This is deliberate — the agent cannot do a browser login — but it means
   the responder half of every remote-control session is protected by the access token alone.
2. The requester has no authorization policy. `AuthorizationPolicyForRequester` is never set, so
   the check is `IsAuthenticated` only. Any authenticated principal on the server, of any tenant,
   can attach as requester to any session whose ID and token they hold.
3. `SessionSignaler.ValidateToken` is `return accessToken == _accessToken;` — an ordinary
   short-circuiting string comparison, not `CryptographicOperations.FixedTimeEquals`. A remote
   timing attack on a 64-character token is not practical today; the fix is one line, so there is
   no reason to carry it.
4. `streamStore.GetOrAdd(sessionId, id => new SessionSignaler(accessToken))` — whoever connects
   first defines the token for that session ID. An attacker who learns or predicts a session ID
   before the real peers connect can squat it and lock both of them out.

The session ID and access token are generated on the client
(`ControlR.Web.Client/Components/Pages/DeviceAccess/RemoteControl.razor.cs:437`) using
`RandomGenerator.CreateAccessToken()`, which is `RandomNumberGenerator.GetBytes(64)` base64url —
a proper CSPRNG, so the tokens themselves are sound. Both values travel in the WebSocket URL's
query string, which is where the exposure risk sits: proxy access logs, browser history, and
anything that sees the request line.

Fix: set an authorization policy for the requester so tenant scope is checked rather than mere
authentication; use a fixed-time comparison; have the server, not the client, mint and register
the session so `GetOrAdd` cannot be squatted.

## F-05

**Aspire dashboard published on all interfaces — Medium**

`docker-compose/docker-compose.yml:293`

```yaml
  aspire:
    ports:
      - "18888:18888"
```

Docker publishes this on `0.0.0.0`, so following the README's quick start puts a second
internet-facing service alongside ControlR, carrying the server's full logs and telemetry. The
README's perimeter guidance discusses port 5120 and the agent route allowlist and does not mention
18888.

The browser token is mandatory (`${ControlR_ASPIRE_BROWSER_TOKEN:?error}`), so this is exposure
rather than an open door. Still: telemetry is exactly where reset codes, tokens and internal
hostnames surface in logs, and `UseHttpLogging` makes that richer.

Fix: bind to loopback — `"127.0.0.1:18888:18888"` — and reach it through the reverse proxy the
`PublicWebUrl` comment already assumes. Say so in the README's proxy section.

## F-06

**No fallback authorization policy — Medium**

`ControlR.Web.Server/Startup/AuthorizationRegistrationExtensions.cs:52`

```csharp
.AddAuthorizationBuilder()
.SetDefaultPolicy(new AuthorizationPolicyBuilder()...RequireAuthenticatedUser().Build());
```

`SetDefaultPolicy` governs endpoints that already carry `[Authorize]` with no named policy. It
does nothing for an endpoint with no authorization metadata at all — that endpoint is anonymous.
There is no `SetFallbackPolicy` anywhere in the solution.

So the default for a newly added controller is "public", and whether an endpoint is protected
depends on nobody having forgotten an attribute. `Api/Internal/VersionController.cs` is the
current example: no `[Authorize]`, no `[AllowAnonymous]`, reachable anonymously. That one is
harmless by content, but it demonstrates that the safety net is absent rather than merely unused.

Worth noting as a near miss: `Api/Internal/ServerStatsController.cs:7` has its `[Authorize(Policy
= ...)]` indented by two spaces, which reads like a stray line. It is a real attribute and the
endpoint is protected.

Fix: `SetFallbackPolicy` to the same authenticated-user policy, add explicit `[AllowAnonymous]` to
the handful of endpoints that need it, and add the guardrail test in
[06-verification.md](06-verification.md) so the anonymous set can only grow deliberately.

## F-07

**No security response headers — Medium**

A search across `ControlR.Web.Server` and `ControlR.Web.Client` for `Content-Security-Policy`,
`X-Content-Type-Options`, `X-Frame-Options`, `Referrer-Policy` and `Permissions-Policy` returns
nothing. `Program.cs` sets HSTS in non-development and nothing else.

`Referrer-Policy` is the one that interacts with an existing design choice. The logon-token flow
authenticates on `/device-access?logonToken=...`
(`Startup/AuthenticationRegistrationExtensions.cs:47`), so a live credential sits in the URL. With
no referrer policy, any third-party resource loaded by that page receives the full URL, token
included, in the `Referer` header. The token is single-use and device-bound, which limits the
window, but it does not need to be leaked at all.

Fix: a small middleware setting `Content-Security-Policy` (Blazor WASM needs `wasm-unsafe-eval`),
`Referrer-Policy: no-referrer`, `X-Content-Type-Options: nosniff`, a frame-ancestors restriction,
and a `Permissions-Policy` that denies what the UI does not use.

## F-08

**Cloudflare IPv6 ranges are never trusted — Medium**

`ControlR.Web.Server/Startup/ForwardedHeadersRegistrationExtensions.cs:37`

```csharp
using var ip6Response = await httpClient.GetAsync("https://www.cloudflare.com/ips-v6");
ip6Response.EnsureSuccessStatusCode();
var ip6Content = await ip4Response.Content.ReadAsStringAsync();   // reads ip4Response
```

`ip6Content` is read from `ip4Response`. The IPv6 ranges are fetched, checked, and discarded; the
IPv4 list is parsed twice.

Consequence for an operator who enables `EnableCloudflareProxySupport`: over IPv6, the Cloudflare
edge is not a known proxy, `ForwardedHeadersMiddleware` refuses to rewrite, and every IPv6 request
is attributed to the proxy's address rather than the client's. That silently degrades exactly the
controls that depend on client IP — the per-IP partition in `AnonymousAuthRateLimitPolicy`, the
two-axis throttles in the PAT and service-account handlers, installer-key usage IP history, and
the `PublicIpV4`/`PublicIpV6` recorded per device. It fails quietly and in the unsafe direction:
all IPv6 clients collapse into one rate-limit bucket.

This also matches the README's symptom description ("If the public IP for your connected devices
is not showing correctly"), so it may already be generating support traffic.

Fix: read `ip6Response`. It is a one-word change; add a test that asserts the two parsed lists
differ.

## F-09

**`ForwardLimit = null` — Low**

Same file, both branches: `options.ForwardLimit = null` removes the cap on how many entries of
`X-Forwarded-For` are processed. With a correct `KnownProxies`/`KnownIPNetworks` set this is
bounded by the trust check, so it is not directly exploitable — but it removes the second line of
defence, and it is exactly the setting that turns a proxy misconfiguration into client-IP
spoofing. Set it to the real hop count.

## F-10

**Rate-limit state is per-instance — Low**

`AnonymousAuthRateLimitPolicy` uses an in-process fixed-window limiter, and both token handlers
use `IMemoryCache`. Behind more than one replica the effective limit is multiplied by the replica
count, and every restart clears the counters. Fine for the single-container deployment the
compose file describes; worth documenting as a constraint before anyone scales out.

## F-11

**Data Protection keys unencrypted at rest by default — Low**

`Startup/DataProtectionRegistrationExtensions.cs` persists keys to the database and, with the
default `KeyProtectionOptions:EncryptKeys = false`, does not encrypt them. The code already prints
a warning in production, which is the right instinct. Read access to the database therefore yields
forgeable authentication cookies. Not reachable from outside on its own; it is what turns a
database disclosure into full account access.

## F-12

**`ServeUnknownFileTypes` on the vendored novnc provider — Low**

`Program.cs`:

```csharp
app.UseStaticFiles(new StaticFileOptions
{
  FileProvider = new PhysicalFileProvider(Path.Combine(..., "novnc")),
  RequestPath = "/novnc",
  ServeUnknownFileTypes = true,
});
```

The directory is a git submodule (`.gitmodules`), not user-writable, so the immediate risk is low.
It is third-party JavaScript served from the application's own origin, so it inherits the session:
pin the submodule to a reviewed commit and keep it in the dependency-update loop, and drop
`ServeUnknownFileTypes` unless a specific extension needs it.

## F-13

**No CodeQL or dependency scanning — Info**

`.github/workflows/` has build, test, publish and packaging workflows; none run CodeQL, a
dependency vulnerability check, or container image scanning. `dependabot.yml` is present, which
covers update PRs but not alerting on what is currently deployed. See
[06-verification.md](06-verification.md).

---

## Attacks that were checked and are closed

Recording these matters as much as the findings — they are the paths not to re-investigate, and
each is a control that is actively doing its job.

- **Cross-tenant device theft via the agent API.** `Api/Agent/DevicesController.cs:127` explicitly
  looks for the device in another tenant with `IgnoreQueryFilters()` and rejects it.
- **Cross-tenant device move via the hub.** `DeviceManager.UpdateDeviceEntity` throws on any
  attempt to re-home a device, including through the `AddOrUpdate` path that skips the outer
  check. Defence in depth, correctly placed at the innermost write.
- **Rogue device creation without self-bootstrap.** `DeviceManager.UpdateDevice` refuses a device
  that does not exist, so the hub cannot create one.
- **Signature forgery against a registered device.** Once `PublicKey` is stored, it is always the
  key used for verification; the caller-supplied key is ignored.
- **Replay of signed agent messages.** `AgentClockSkewTolerance` defaults to one minute in
  `appsettings.json`. The `null`-disables-it behavior is documented in `AppOptions.cs` with an
  explicit warning.
- **Installer key and PAT brute force.** Both are stored via `IPasswordHasher` (PBKDF2, constant
  time internally), and both handlers throttle independently per IP and per credential — with a
  comment explaining why the axes are separate, which is the right reasoning.
- **Token-shaped cache poisoning.** `PersonalAccessTokenAuthenticationHandler` collapses malformed
  token prefixes to a single `"invalid"` cache key so an attacker cannot plant unbounded entries.
- **Weak token generation.** `RandomGenerator.GenerateString` uses
  `RandomNumberGenerator.GetBytes`.
- **Open signup.** `EnablePublicRegistration` and `AllowAgentsToSelfBootstrap` default to `false`;
  `EnableScalarUi`, `EnableNetworkTrust`, `EnableInteractiveBearerLogin` and
  `EnableSignalrDetailedErrors` likewise.
- **Postgres exposure.** `expose`-only in the compose file, not published.
