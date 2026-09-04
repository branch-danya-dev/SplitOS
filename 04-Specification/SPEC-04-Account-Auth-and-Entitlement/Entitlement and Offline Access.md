# SPEC-04 — Entitlement and Offline Access

## 1. Purpose

This document defines the v1 authorization model that turns SplitOS Account evidence into FREE/PRO runtime capability access, including bounded offline use.

Core rule:

```text
account identity
!= entitlement
!= local runtime access decision
```

---

## 2. Server entitlement object

SplitOS Backend is canonical owner.

v1 entitlement response SHOULD contain:

```json
{
  "entitlementVersion": 42,
  "accountId": "acc_...",
  "plan": "FREE | PRO",
  "status": "ACTIVE | EXPIRED | SUSPENDED | CANCELLED_AT_PERIOD_END",
  "validFrom": "...",
  "validUntil": "...",
  "capabilities": ["runtime.managed_modes"],
  "offlineEligible": true
}
```

The response is server data delivered over authenticated HTTPS; local persistence is evidence/cache, not a new authority.

---

## 3. Capability-first authorization

Runtime SHOULD authorize concrete product behavior by capability.

Examples:

```text
runtime.managed_modes
game.launcher
game.profiles
game.optimization
game.shared_apps
runtime.advanced_device_orchestration
```

`plan` is a convenient commercial grouping.

Therefore:

```text
plan = PRO
but missing runtime.managed_modes
→ managed Work/Game runtime is NOT authorized
```

This keeps future packaging changes from coupling every module to tier strings.

---

## 4. Managed runtime decision algorithm

Inputs:

```text
account association
current online entitlement if available
valid protected offline assertion if online unavailable
account/auth state
local trust/clock state
required capability set
```

Decision examples:

### FREE online

```text
associated account
+ valid FREE entitlement
→ DISABLED / FREE_ENTITLEMENT
```

### PRO online

```text
associated account
+ ACTIVE entitlement
+ runtime.managed_modes present
→ ENABLED / PRO_ONLINE_CONFIRMED
```

### PRO offline

```text
associated account
+ backend unavailable
+ valid OfflineEntitlementAssertion
+ required capability present
→ ENABLED / PRO_OFFLINE_ASSERTION_VALID
```

### Cannot prove PRO

```text
account known
+ no current online proof
+ offline assertion missing/expired/invalid
→ DISABLED or REAUTH_REQUIRED according to auth state
→ base Windows remains usable
```

---

## 5. OfflineEntitlementAssertion v1

### 5.1 Serialization

v1 uses:

```text
JWS Compact Serialization
payload = UTF-8 JSON claims
```

Exact JWS signing algorithm, signing key hierarchy, rotation and revocation are specified later in `SPEC-12`.

Desktop MUST verify signatures using trusted SplitOS release/account trust configuration; it does not possess the signing private key.

---

## 6. Required claims

```text
ver                  = 1
iss                  = SplitOS entitlement issuer
sub                  = canonical accountId
jti                  = unique assertion ID
installationId       = current SplitOS installation identity
associationId        = local Windows-user↔account association identity
entitlementVersion   = server entitlement version
plan                 = FREE | PRO
capabilities[]       = bounded capability list
iat                  = issued-at UTC
nbf                  = not-before UTC
exp                  = expiration UTC
```

Optional future claims require explicit version/compatibility handling.

Unknown critical claims MUST fail verification rather than be silently ignored.

---

## 7. Issuance conditions

Backend may issue offline assertion only when:

```text
account authenticated
entitlement current/active
client installation association acceptable
requested offline capability allowed
```

Assertion generation MUST be server-side.

Desktop MUST NOT locally extend or re-sign `exp`.

---

## 8. Offline validity window

Maximum v1 issuance window:

```text
7 days
```

Server calculates:

```text
exp = min(
    iat + 7 days,
    authoritative entitlement validUntil
)
```

If commercial entitlement has no explicit short period end (for example lifetime capability), the 7-day offline proof still requires periodic online refresh to keep theft/revocation exposure bounded.

Server MAY issue a shorter assertion based on risk/account/device policy.

---

## 9. Validation algorithm

Runtime Host MUST validate all of:

```text
JWS parse succeeds
signature/key trusted
issuer exact match
ver supported
sub == current associated accountId
installationId == current installationId
associationId == current local associationId
entitlementVersion acceptable/current enough for local policy
nbf <= now + allowedClockSkew
now <= exp + allowedClockSkew
required capability present
account not locally known signed-out/revoked by newer evidence
clock rollback not suspected
```

v1 allowed clock skew:

```text
5 minutes
```

Failure of any mandatory validation means the assertion cannot authorize premium capability.

---

## 10. Local storage and copy resistance

The signed assertion is not confidential by protocol, but Runtime Host SHOULD store it inside the DPAPI-protected per-user secret blob.

Thus ordinary copying of the file to another Windows user/machine is insufficient because:

```text
DPAPI user/machine context
+
signed installationId
+
signed associationId
```

must all match.

This does not claim resistance to unrestricted local Administrator/kernel attackers.

---

## 11. Trusted time / rollback model

Offline authorization cannot rely only on an arbitrary local clock without rollback checks.

Runtime Host stores protected:

```text
lastTrustedServerUtc
lastTrustedServerObservationLocalUtc
lastValidAssertionJti
```

Whenever authenticated backend response is received, Runtime updates trusted server time evidence from the HTTPS response/application timestamp.

### 11.1 Rollback detection

If:

```text
currentUtc < lastTrustedServerUtc - 5 minutes
```

then:

```text
CLOCK_ROLLBACK_SUSPECTED
→ offline assertion cannot authorize PRO
→ require online validation
```

If current time later recovers but remains ambiguous, Runtime SHOULD still prefer fresh online entitlement before restoring offline trust.

### 11.2 Forward jump

If clock jumps beyond assertion `exp`, assertion is expired.

SplitOS MUST NOT move expiry backward to compensate for user clock changes.

---

## 12. Online refresh policy

When online and authenticated, Runtime SHOULD refresh entitlement:

```text
at Runtime startup
after auth/token refresh when stale
before a premium action if current entitlement evidence is insufficient
after checkout completion/return
when offline assertion has < 24h remaining and connectivity exists
periodically during long-running sessions with bounded cadence
```

Exact background cadence may be tuned operationally without changing authorization semantics.

---

## 13. Revocation / suspension semantics

If online backend returns:

```text
SUSPENDED
EXPIRED
revoked capability
```

this newer server evidence supersedes an older local offline assertion.

Runtime MUST invalidate local offline proof for those capabilities/account state.

If currently in managed runtime, Runtime Access owner initiates safe convergence according to `SPEC-05`; it MUST NOT simply kill applications or corrupt Windows state.

---

## 14. Subscription cancellation

`CANCELLED_AT_PERIOD_END` means:

```text
current paid entitlement may remain ACTIVE until validUntil
future renewal disabled
```

Offline assertion MUST NOT exceed authoritative `validUntil`.

Immediate refund/revocation policy is backend/product policy and may cause earlier server invalidation.

---

## 15. Upgrade activation

After checkout:

```text
Payment Provider success evidence
→ SplitOS Backend validates
→ Entitlement version increments/changes
→ Runtime retrieves new entitlement
→ old offline assertion superseded
→ new offline assertion may be issued
```

A local browser return alone has no entitlement authority.

---

## 16. Multiple installations

Structural cardinality:

```text
one SplitOS Account
→ zero or more active installation associations
```

Each offline assertion is bound to exactly one:

```text
installationId + associationId
```

Backend MAY enforce plan-specific active-installation limits.

The numeric limit is commercial product policy and remains intentionally outside this technical spec.

---

## 17. Entitlement evidence precedence

Recommended precedence:

```text
newer authenticated backend entitlement
>
valid signed offline assertion
>
local display/cache metadata
>
no evidence
```

A stale local `plan=PRO` field MUST NOT outrank a valid server FREE/suspended result.

---

## 18. Failure matrix

| Evidence condition | Runtime access result |
|---|---|
| online ACTIVE PRO capability | ENABLED |
| online FREE | DISABLED |
| online account suspended | DISABLED / REAUTH or account-policy UX |
| backend offline + valid assertion | ENABLED with offline evidence mode |
| backend offline + expired assertion | DISABLED |
| assertion bad signature | DISABLED + trust diagnostic |
| assertion wrong installation/account | DISABLED + trust diagnostic |
| clock rollback suspected | DISABLED pending online validation |
| newer backend revocation vs older assertion | backend result wins |
| checkout callback only | no change until backend entitlement refresh |

---

## 19. Acceptance criteria

1. entitlement is backend canonical;
2. premium behavior checks capabilities, not only tier string;
3. offline authorization is signed, bounded and context-bound;
4. assertion lifetime never exceeds seven days nor server entitlement end;
5. local app cannot extend assertion expiry;
6. clock rollback blocks offline premium until online validation;
7. newer backend evidence supersedes old offline proof;
8. copied assertion cannot authorize a different installation/association under normal v1 threat model;
9. cancellation-at-period-end does not prematurely remove already-paid time;
10. missing premium proof degrades to base Windows, not an unbootable system.
