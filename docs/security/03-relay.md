# Phase 3 — WebSocket relay

`/relay` carries the actual remote-control payload: screen frames one way, input events the other.
It is the one path where a successful attack means watching or driving someone's desktop.

## How a session is established

1. The viewer generates a session ID (`Guid`) and a 64-byte access token client-side
   (`RemoteControl.razor.cs:437`, `RandomGenerator.CreateAccessToken()`).
2. It builds two URLs with `RelayUriBuilder.Build` — one `role=responder` for the agent, one
   `role=requester` for itself — both carrying `sessionId` and `accessToken` in the query string.
3. The responder URL is handed to the agent through `ViewerHub.RequestRemoteControlSession2`,
   which *is* authorized (`DeviceResourcePolicies.RemoteControlConnect`).
4. Both peers connect to `/relay`. `WebSocketRelayMiddleware` matches them by session ID and pipes
   the sockets together.

Step 3 is the real authorization checkpoint, and it is properly enforced. Everything at `/relay`
itself is secondary — but it is what an external attacker can reach directly.

## What the middleware checks

```csharp
var requireAuth = role == RelayRole.Requester
  ? options.RequireAuthenticationForRequester    // true
  : options.RequireAuthenticationForResponder;   // false — never set

var policy = role == RelayRole.Requester
  ? options.AuthorizationPolicyForRequester      // null — never set
  : options.AuthorizationPolicyForResponder;     // null
```

So: the responder half is anonymous, and the requester half requires only that *someone* is
signed in — no tenant check, no device check. Both halves then rest on the token comparison, which
is `accessToken == _accessToken` rather than a fixed-time comparison, and on `GetOrAdd`, where the
first connection to a given session ID defines that session's token.

Full detail in [F-04](findings.md#f-04).

## Risk assessment

The tokens are cryptographically sound: 64 bytes from `RandomNumberGenerator`, so guessing is not
the threat. The threat is disclosure, and the design maximizes the number of places a token can
leak, because both the session ID and the token sit in the request line of the URL. Reverse-proxy
access logs record request lines by default. So does `UseHttpLogging` when enabled. Browser
history holds the viewer's copy.

That is why the requester policy matters: today, a token that leaks into a shared proxy log can be
replayed by any authenticated account on the server, regardless of tenant or device permissions.
With a tenant-scoped policy, a leaked token is only useful to someone who could already reach that
device.

## Recommendations

1. Set `AuthorizationPolicyForRequester` to a policy that checks tenant, and ideally device,
   scope — not just authentication.
2. Replace the token comparison with `CryptographicOperations.FixedTimeEquals`.
3. Mint the session ID and token server-side during `RequestRemoteControlSession2` and pre-register
   the `SessionSignaler`, so `GetOrAdd` cannot be squatted by whoever arrives first.
4. Move the token out of the query string — a `Sec-WebSocket-Protocol` value or a short-lived
   header keeps it out of access logs. If it must stay in the URL, exclude `/relay` from HTTP
   logging and document the proxy log-scrubbing requirement.
5. Bound concurrent sessions per principal and per device; the store is otherwise unbounded.
