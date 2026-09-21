# Phase 2 — Agent perimeter

This is the highest-value part of the review: the agent surface is anonymous by design, and a
device record is the thing that grants remote control.

## The trust chain

1. An operator creates an installer key (`AgentInstallerKeyManager.CreateKey`) — a random 64-byte
   token, stored as a PBKDF2 hash, optionally usage-limited and expiring.
2. The agent POSTs `/api/agent/devices` with the key ID, key secret, the device DTO and
   (optionally) an Ed25519 public key.
3. The server validates and consumes the key, then `AddOrUpdate`s the device row.
4. From then on the agent connects to `/hubs/agent` and calls `UpdateDeviceSigned`, signing each
   update with its private key. The server verifies against the key stored on the device row.

The chain is sound where it is followed. The problems are at its edges: what happens before a key
is stored, and what happens on the paths that skip it.

## Device creation — well guarded

`Api/Agent/DevicesController.cs` does considerably more than validate the installer key. For a
device that already exists it re-authorizes by key creator kind — a user must pass
`CanInstallAgentOnDevice`, a tenant service account likewise, a server service account must be
enabled. It rejects a device ID that exists in another tenant outright (line 127), validates that
a supplied `CustomerId` belongs to the same tenant, and requires `DeviceTagsWrite` on the target
before honoring `TagIds`. Key consumption happens after all of that, so a failed authorization
does not burn a usage.

The residual risk here is not a flaw in the code but the nature of the credential: an installer
key is a deployment artifact that lives in scripts, images and MDM payloads. Whoever holds one can
re-provision an existing device they are authorized for — and re-provisioning overwrites
`PublicKey`. A leaked key is therefore a path to device impersonation, not just to registering new
devices. Usage-based keys expiring after 24 hours is the right mitigation and should be the
documented default for scripted deployments.

## Hub device update — where it breaks

Detailed in [F-02](findings.md#f-02) and [F-03](findings.md#f-03). The short version:

- `AgentHub` has no `[Authorize]`.
- `UpdateDeviceSigned` verifies against the caller's own key when the device has no stored key and
  self-bootstrap is on.
- The `[Obsolete]` unsigned `UpdateDevice` is still reachable and accepts any device with an empty
  `PublicKey`.
- A successful update rewrites `ConnectionId` — which is how commands are routed — and adopts the
  supplied key permanently.

The gating question for the deployment is: **how many device rows have an empty `PublicKey`?**
That is a one-line query and it converts this from a theoretical finding into a measured one:

```sql
SELECT count(*) FROM "Devices" WHERE "PublicKey" IS NULL OR "PublicKey" = '';
```

## What held up under scrutiny

Several plausible attacks turned out to be closed, and by guards placed at the right layer rather
than at the entrance:

- Moving a device between tenants throws in `DeviceManager.UpdateDeviceEntity`, the innermost
  write path, so it holds even on the `AddOrUpdate` route that skips the outer check. This is the
  pattern `AGENTS.md` recommends — explicit predicates rather than reliance on query filters — and
  it is what stopped the most serious attack this review looked for.
- `UpdateDevice` in `DeviceManager` refuses a device that does not exist, so without self-bootstrap
  the hub cannot create devices at all.
- Timestamp verification is on by default with a one-minute tolerance, and the `null`-disables-it
  hazard is documented in `AppOptions.cs` rather than left implicit.

## Recommendations

1. Remove the obsolete unsigned `UpdateDevice` hub method.
2. Never adopt a public key from an unauthenticated hub message — require the installer-key path.
3. Measure and then eliminate devices with no stored public key; consider refusing hub updates for
   them once the count is zero.
4. Make the self-bootstrap single-tenant check unconditional.
5. Rate-limit `/hubs/agent` connection attempts per IP.
