# ControlR — External Perimeter Security Review

Review date: 2026-09-21
Reviewed commit: `2f65333` (branch `claude/upbeat-brahmagupta-oprt6e`)
Method: manual source review of the server's externally reachable surface. No dynamic testing, no
deployed instance, no dependency CVE scan (see [06-verification.md](06-verification.md) for what
still needs to run).

## Scope

The question this review answers is: **can someone outside the deployment get in?**

In scope:

- Anonymous traffic from the internet to the server's HTTP, WebSocket and SignalR surfaces.
- Holders of a leaked deployment artifact that never touched the operator's infrastructure —
  an installer key from a deployment script or MDM payload, a PAT committed to a repo, a relay
  access token, an agent's private key from a stolen laptop. These are outsiders, not insiders.
- Deployment topology as shipped in `docker-compose/` and documented in `README.md`.

Explicitly out of scope, at the requester's direction:

- A malicious authenticated user attacking their own tenant.
- A malicious operator or anyone with database or container host access.
- Cross-tenant attacks between legitimately registered tenants, except where a finding below
  notes one as a side effect.

## What the perimeter looks like

One process is internet-facing: `ControlR.Web.Server`. Postgres is `expose`-only. The Aspire
dashboard is not — see [05-deployment.md](05-deployment.md).

| Entry point | Authentication | Notes |
|---|---|---|
| `/hubs/agent` | none at the transport level | `Hubs/AgentHub.cs` carries no `[Authorize]`; identity is asserted inside the hub methods via Ed25519 signatures |
| `/api/agent/*` | `[AllowAnonymous]` | installer key ID + secret in the request body |
| `/relay` | requester yes, responder no | `RequireAuthenticationForResponder` is left at `false` |
| `/downloads/*` | none | agent bundles, installers, `Version.txt` |
| `/api/auth/*`, `MapIdentityApi` | anonymous flows | login, password reset, 2FA; fixed-window rate limit |
| `/device-access?logonToken=` | custom scheme | single-use token bound to a device ID, carried in the query string |
| `/api/*`, `/hubs/viewer` | cookie / PAT / `x-api-key` / bearer | scheme chosen by `ForwardDefaultSelector` |
| `/Account/*` | Razor pages, cookie | includes the anonymous forgot-password flow |

## Documents

| Phase | Document |
|---|---|
| Consolidated findings | [findings.md](findings.md) |
| 1 — Attack surface inventory | [01-attack-surface.md](01-attack-surface.md) |
| 2 — Agent perimeter | [02-agent-perimeter.md](02-agent-perimeter.md) |
| 3 — WebSocket relay | [03-relay.md](03-relay.md) |
| 4 — Authentication schemes | [04-authentication.md](04-authentication.md) |
| 5 — Deployment and HTTP hardening | [05-deployment.md](05-deployment.md) |
| 6 — Verification and regression guards | [06-verification.md](06-verification.md) |

## Overall assessment

The authorization core is in good shape and not where the risk lives. Permission policies,
resource-scoped device authorization, claims-driven EF query filters, tenant immutability enforced
at the innermost write path, PBKDF2-hashed installer keys and PATs, two-axis throttling on both
token handlers — these are done carefully and several attacks that looked plausible on the way in
turned out to be closed by a guard one layer down.

The risk concentrates in three places, all of them at the edge rather than in the authorization
model: the unauthenticated agent hub, host-header-derived absolute URLs in the account flows, and
the deployment defaults shipped in the quick-start compose file.
