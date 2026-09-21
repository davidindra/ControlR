# Phase 1 — Attack surface inventory

Goal: enumerate everything reachable from outside and establish what authorization each entry
point actually carries, rather than what it appears to carry.

## Method

Every controller under `ControlR.Web.Server/Api/` was read for class-level and action-level
`[Authorize]` / `[AllowAnonymous]` / `[Authorize(Policy = ...)]`, both hubs for `[Authorize]`, and
`Program.cs` for endpoints mapped outside MVC (static files, the relay middleware, Identity
endpoints, Razor components, Scalar/OpenAPI).

Reading attributes is necessary but not sufficient: what matters is the *effective* policy after
the default policy, the fallback policy and attribute inheritance are applied. That is where the
first finding came from.

## Route roots

| Root | Prefix | Audience |
|---|---|---|
| `Api/Agent` | `/api/agent/*` (+ legacy aliases) | unattended agents, anonymous |
| `Api/Internal` | `/api/*` | the Blazor UI (BFF), cookie-authenticated |
| `Api/V1` | `/api/v1/*` | external consumers: users, PATs, service accounts |

`AGENTS.md` is explicit that the V1 root describes contract stability, not audience or scope —
authorization must be read per controller. That guidance held up: V1 controllers do carry their
own policies and do not inherit an audience from the route root.

## Anonymous endpoints

Eight `[AllowAnonymous]` sites, each of which is defensible:

| Location | Why |
|---|---|
| `Api/Agent/DevicesController.cs:13` | device registration, authenticated by installer key in the body |
| `Api/Agent/AgentUpdateController.cs:11` | agent update metadata |
| `Api/V1/VersionController.cs:14` | version probe |
| `Api/V1/PublicServerSettingsController.cs:15` | settings the login page needs before login |
| `Api/Internal/AuthController.cs:37,109,151` | `change-password-with-credentials`, `complete-password-reset`, `interactive-login` — all three rate-limited |
| `Api/Internal/InvitesController.cs:14` | invite acceptance, token in the body |

The three anonymous `AuthController` actions all carry
`[EnableRateLimiting(AnonymousAuthRateLimitPolicy.PolicyName)]`. That is consistent and correct.

## The gap: implicit anonymity

Three Internal controllers have no class-level authorization attribute at all:

- `PublicServerSettingsController` — intended; its V1 twin says `[AllowAnonymous]` explicitly
- `VersionController` — intended; its V1 twin says `[AllowAnonymous]` explicitly
- `ServerStatsController` — *has* `[Authorize(Policy = RequireServerTelemetryRead)]`, merely
  mis-indented at line 7

The first two are anonymous by omission rather than by declaration, and nothing in the build
distinguishes "we meant this to be public" from "someone forgot the attribute". `SetDefaultPolicy`
does not close this — it only applies where `[Authorize]` is already present. See
[F-06](findings.md#f-06).

## Hubs

`ViewerHub` declares `[Authorize]` and additionally re-authorizes every device-touching method
against a resource policy — `TryAuthorizeAgainstDevice(request.DeviceId, DeviceResourcePolicies.X)`
appears on roughly twenty methods, with a distinct permission per operation. This is the strongest
part of the surface.

`AgentHub` declares nothing. That is a deliberate design decision — agents cannot hold a browser
session — but it moves the entire authentication burden into the hub method bodies, which is where
[F-02](findings.md#f-02) lives.

## Non-MVC endpoints

- `/relay` — `MapWebSocketRelay()`, covered in [phase 3](03-relay.md).
- `/downloads/*` — served by `UseStaticFiles` from `wwwroot`; anonymous by design, since agents
  fetch updates before any credential exists. The integrity question (are bundles signed and is
  the signature verified by the agent before execution?) is the open item; `WindowsBundleSign.Build.targets`
  exists, so signing is at least present on Windows.
- `/novnc/*` — separate `PhysicalFileProvider` with `ServeUnknownFileTypes = true`
  ([F-12](findings.md#f-12)).
- `MapIdentityApi<AppUser>` under `/api/auth` — only when `EnableInteractiveBearerLogin` is on
  (default off), and filtered by `IdentityApiRegisterFilter` so the built-in `/register` endpoint
  cannot be used to bypass the invite flow.
- `MapScalarApiReference` + `MapOpenApi` — only when `EnableScalarUi` is on (default off).
- `MapRazorComponents<App>` for everything not under `/api`.

## Middleware order

`Program.cs` interleaves `MapHub` calls with `UseCors`/`UseRateLimiter`/`UseAuthentication`, which
reads alarmingly. It is not a defect: with endpoint routing, `Map*` registers an endpoint that
executes at the end of the pipeline regardless of where the call appears in source, so
authentication still runs before the hub. The real gap is not order but coverage — the rate
limiter has no global limiter, only the one named policy, so `/hubs/agent`, `/relay` and
`/api/agent/*` are unmetered.

## Recommendations

1. Add `SetFallbackPolicy`, then mark the genuinely public endpoints `[AllowAnonymous]`.
2. Add the anonymous-endpoint guardrail test from [phase 6](06-verification.md).
3. Add a global rate limiter, or at minimum a named policy on `/api/agent/*` and the relay.
4. Fix the `ServerStatsController` indentation so the attribute is not mistaken for dead text.
