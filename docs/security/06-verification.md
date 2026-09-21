# Phase 6 — Verification and regression guards

This review was static. These are the checks that should run against a live instance, plus the
tests that would keep the findings from returning.

## Regression tests to add

The project already has 111 test files under `Tests/ControlR.Web.Server.Tests/`, including
`AuthIntegrationTests`, `OpenApiSecurityRequirementsTests` and `InternalV1ParityGuardrailTests`.
The guardrail pattern used by that last one — enumerate reality, compare against an explicit
allowlist, fail on drift — is exactly right for the perimeter, and these fit alongside it.

**Anonymous endpoint allowlist.** Enumerate `EndpointDataSource` at runtime, collect every
endpoint whose metadata resolves to anonymous, and assert the set equals a hard-coded list. This
catches a forgotten `[Authorize]` on a new controller, and it is the test that makes
[F-06](findings.md#f-06) safe to live with if a fallback policy is not adopted. Enumerating the
endpoint data source rather than grepping attributes is what makes it trustworthy — it sees the
effective policy after defaults and inheritance.

**Agent hub rejects unsigned and unknown devices.** Assert that the obsolete `UpdateDevice` is
gone (or refuses every input), and that `UpdateDeviceSigned` rejects a device whose stored
`PublicKey` is empty when self-bootstrap is off — and, once [F-02](findings.md#f-02) is fixed, when
it is on.

**Self-bootstrap tenant restriction.** Assert that an explicit foreign `TenantId` is rejected on a
multi-tenant server, not just `Guid.Empty`.

**Callback URLs ignore the `Host` header.** Drive `/Account/ForgotPassword` with a forged `Host`
and assert the emailed link uses the configured base URL.

**Cloudflare ranges.** Assert the parsed IPv4 and IPv6 lists differ — the current bug passes any
test that only checks "some networks were added".

**Security headers.** Assert the response carries CSP, `X-Content-Type-Options` and
`Referrer-Policy` on both an API route and a UI route.

## CI additions

- CodeQL for C# on push and PR.
- `dotnet list package --vulnerable --include-transitive` as a failing step.
- Container image scanning (Trivy or Grype) in the publish workflow.
- GitHub secret scanning with push protection enabled on the repository.

`dependabot.yml` already exists, which covers update PRs but not alerting on what is currently
deployed.

## Dynamic testing against a staging instance

Ordered by what this review could not determine statically:

1. **Confirm [F-01](findings.md#f-01) end to end.** Forge the `Host` header on the forgot-password
   form for a test account with a confirmed email and read the resulting message. This is the one
   finding worth proving before anything else, because it is the only chain that goes from "knows
   an email address" to "holds a password-reset token".
2. **Measure the [F-02](findings.md#f-02) exposure.**
   `SELECT count(*) FROM "Devices" WHERE "PublicKey" IS NULL OR "PublicKey" = '';` — if the answer
   is zero, the takeover path has no targets today and the fix is about keeping it that way.
   If it is not zero, it is a live issue.
3. **Attempt the takeover** on a throwaway device row with an empty key, from an unauthenticated
   SignalR client, and confirm whether `ConnectionId` is rewritten.
4. **Rate-limit behavior** on `/hubs/agent`, `/relay` and `/api/agent/*` — all unmetered today.
   Establish what connection rate a single IP can sustain.
5. **Relay squatting** — connect to `/relay` with a chosen session ID before a legitimate session
   starts and confirm the legitimate peers are locked out.
6. **Agent update integrity** — whether the agent verifies a signature on a `/downloads` bundle
   before executing it, and what happens to an agent pointed at a hostile server. This was not
   reachable from the server-side review and is the largest remaining unknown.
7. **Automated scanning** — OWASP ZAP baseline and nuclei against a staging instance, mainly as a
   check on headers, TLS configuration and information disclosure rather than for logic flaws.

## Manual review still outstanding

- `ControlR.Agent.Common` update and installation flow — signature verification, download path
  handling, privilege use during self-update.
- The named-pipe IPC between agent and desktop client. Local rather than network-facing, so out of
  this review's scope, but it is the boundary that separates a user-session process from a service
  running as SYSTEM or root.
- The `novnc` submodule's pinned revision.
