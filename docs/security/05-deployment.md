# Phase 5 — Deployment and HTTP hardening

The code can be correct and the deployment still open. This phase covers what the shipped
`docker-compose/` files and the HTTP pipeline expose.

## Published ports

| Service | Compose | Assessment |
|---|---|---|
| `controlr` | `ports: "5120:8080"` | intended; the documented perimeter |
| `postgres` | `expose: "5432"` | correct — internal only, not published |
| `aspire` | `ports: "18888:18888"` | published on all host interfaces ([F-05](findings.md#f-05)) |

The Aspire dashboard is a second internet-facing service carrying the server's logs and telemetry.
It requires a browser token (`${ControlR_ASPIRE_BROWSER_TOKEN:?error}` — mandatory, good), so this
is exposure rather than an open door, but the README's perimeter guidance never mentions port
18888. Bind it to `127.0.0.1` and reach it through the reverse proxy that the `PublicWebUrl`
comment already assumes exists.

## Secrets handling

Well done. Both an environment-variable and a Docker Secrets compose file are shipped, every
sensitive value uses `${VAR:?error}` so a missing secret fails the deployment loudly instead of
silently defaulting, and `appsettings.json` ships `null`/empty for every credential rather than a
placeholder that might survive into production. No secrets are committed.

## Forwarded headers

`Startup/ForwardedHeadersRegistrationExtensions.cs` has two branches: `EnableNetworkTrust` (trust
everything, `KnownProxies` cleared) and the normal path that builds a trust list from
`DockerGatewayIp`, `KnownProxies`, `KnownNetworks` and optionally Cloudflare's published ranges.
The default is the safe one, and the README explains the proxy chain concept clearly.

Two issues: the Cloudflare IPv6 list is fetched and then discarded because the IPv4 response is
read twice ([F-08](findings.md#f-08)), and `ForwardLimit = null` on both branches
([F-09](findings.md#f-09)).

Client IP is not cosmetic here. It is the partition key for the anonymous auth rate limiter and
for both token-handler throttles, it is recorded in installer-key usage history, and it populates
each device's `PublicIpV4`/`PublicIpV6`. When header trust silently fails, all of those degrade at
once and in the unsafe direction — every client behind the untrusted hop shares one rate-limit
bucket.

## HTTP response headers

None are set ([F-07](findings.md#f-07)). `Program.cs` configures HSTS, HTTPS redirection and the
exception handler outside development, and nothing else. Missing: `Content-Security-Policy`,
`X-Content-Type-Options`, `Referrer-Policy`, `X-Frame-Options` / `frame-ancestors`,
`Permissions-Policy`.

A starting point, to be tightened against the actual Blazor WASM and MudBlazor requirements:

```csharp
app.Use(async (ctx, next) =>
{
  var h = ctx.Response.Headers;
  h["X-Content-Type-Options"] = "nosniff";
  h["Referrer-Policy"] = "no-referrer";
  h["Permissions-Policy"] = "camera=(), microphone=(), geolocation=()";
  h["Content-Security-Policy"] =
    "default-src 'self'; script-src 'self' 'wasm-unsafe-eval'; " +
    "style-src 'self' 'unsafe-inline'; img-src 'self' data: blob:; " +
    "connect-src 'self' ws: wss:; frame-ancestors 'none'; base-uri 'self'";
  await next();
});
```

Put it before `UseStaticFiles` so it covers static assets too, and verify the remote-control page
and the novnc viewer still work — `connect-src` and `blob:` are the ones most likely to need
adjustment.

## Host binding

`"AllowedHosts": "*"` in `appsettings.json` is the enabling half of
[F-01](findings.md#f-01). Set it to the deployment's real hostnames.

## Other configuration defaults

Reviewed and correct: `AllowAgentsToSelfBootstrap`, `EnablePublicRegistration`,
`EnableNetworkTrust`, `EnableScalarUi`, `EnableSignalrDetailedErrors`,
`EnableDatabaseDetailedErrors`, `EnableCors`, `EnableInteractiveBearerLogin`, `UseHttpLogging` —
all default to `false`. `AgentClockSkewTolerance` defaults to one minute rather than to the
`null` that would disable replay protection. This is a well-chosen set of defaults; the project
is failing safe.

`KeyProtectionOptions:EncryptKeys = false` is the one default that fails open
([F-11](findings.md#f-11)), mitigated by the production warning the startup code prints.

## Container and image

`image: bitbound/controlr:latest` with a comment recommending a pinned tag — the comment is right
and the default is not; pinning by digest in the shipped file would be better. The Aspire and
Postgres images are pinned to major versions. No image or base-layer scanning runs in CI
([F-13](findings.md#f-13)).

## Recommendations

1. `127.0.0.1:18888:18888` for the Aspire dashboard, and document it in the README's proxy section.
2. Pin `AllowedHosts`.
3. Add the security-headers middleware.
4. Fix the Cloudflare IPv6 read; set `ForwardLimit` to the real hop count.
5. Document that `/relay` and `/device-access` URLs carry credentials in the query string, so
   proxy access logs need scrubbing or exclusion.
