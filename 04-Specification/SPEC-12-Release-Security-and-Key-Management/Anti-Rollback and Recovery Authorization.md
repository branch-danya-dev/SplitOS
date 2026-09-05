# Anti-Rollback and Recovery Authorization

## 1. Purpose

This document defines the difference between:

```text
old release is cryptographically authentic
```

and:

```text
old release is currently authorized to activate
```

The distinction protects SplitOS from downgrade attacks while preserving SPEC-11 local previous-release recovery.

---

## 2. Normal update rule

Normal update activation MUST satisfy:

```text
metadata chain valid
+
artifact/publisher trust valid
+
releaseSequence >= normal update floor
+
securityEpoch >= local security floor
+
source→target transition allowed
+
not recovery-only
```

A lower version is rejected even if all files remain correctly signed.

---

## 3. Persistent release floors

Machine security state persists:

```text
highestTrustedReleaseSequence
minimumAllowedReleaseSequence
highestTrustedSecurityEpoch
minimumAllowedSecurityEpoch
```

These values are not rebuildable update cache.

Removing `%ProgramData%\SplitOS\Cache` or redownloading metadata does not reset them.

---

## 4. Release sequence

`releaseSequence` is a monotonic integer assigned by release authority.

It is independent of human semantic version ordering.

Example:

```text
releaseSequence 120 → SplitOS 1.9.4
releaseSequence 121 → SplitOS 2.0.0
releaseSequence 122 → SplitOS 1.9.5 emergency LTS backport
```

Normal updater compares sequence/security policy, not string sorting of `1.9.5` vs `2.0.0`.

---

## 5. Security epoch

`securityEpoch` represents a security floor generation.

A severe incident can advance the epoch even when product feature version changes little.

Example:

```text
Release A sequence 500, epoch 17
Release B sequence 501, epoch 18
```

If local minimum epoch becomes `18`, an otherwise valid release from epoch `17` cannot activate normally.

---

## 6. Recovery exception

Recovery is permitted through an explicit signed exception object:

```text
RecoveryAuthorization
```

authenticated by the dedicated `splitos-recovery` delegated role.

This object grants only a bounded recovery capability.

---

## 7. RecoveryAuthorization schema

Conceptual fields:

```text
authorizationId
schemaVersion
sourceReleaseId
sourceReleaseSequence
targetReleaseId
targetReleaseSequence
targetReleaseEnvelopeDigest
minimumSecurityEpoch
allowedOperation = RESTORE_PREVIOUS_RELEASE
context = RECOVERY_ONLY
issuedForChannel
validFrom / optional operational expiry
policyVersion
reasonCode
```

The exact JSON schema is versioned and signed/bound by TUF target metadata.

---

## 8. Exact edge binding

Authorization is an edge, not a wildcard.

Allowed:

```text
Release 1.5 sequence 105
→ Release 1.4 sequence 104
```

Not equivalent to:

```text
any release
→ any older signed release
```

The recovery tool checks both source transaction context and exact target identity.

---

## 9. Capsule capture

Before `N → N+1` activation, SPEC-11 requires capsule N.

SPEC-12 adds:

```text
capsule N
MUST contain
valid RecoveryAuthorization(N+1 → N)
```

or equivalent release-specific trusted recovery authorization available to the offline recovery tool.

If authorization is unavailable:

```text
RECOVERY_AUTHORIZATION_MISSING
→ update activation blocked
```

---

## 10. Recovery security floor

Recovery authorization does not automatically override every later security revocation.

Before rollback, recovery checks locally persisted trusted security policy:

```text
target epoch >= effective recovery floor?
target release hard-blocked?
recovery key revoked?
authorization explicitly revoked?
```

If a later trusted policy hard-blocks target N, automatic recovery to N is denied even if capsule bytes remain valid.

---

## 11. Offline recovery behavior

Offline WinRE recovery uses the most recent locally trusted security state available before failure.

It cannot discover revocations published after disconnection.

Therefore:

```text
offline rollback authorization
= valid against locally trusted security state
```

not:

```text
guaranteed globally current authorization
```

This limitation must be documented in recovery UX/support policy.

---

## 12. Source release unknown

If machine state is corrupted and source release cannot be established:

```text
sourceRelease = UNKNOWN
```

normal exact-edge authorization cannot be assumed.

Recovery escalates to a separately specified safe recovery/manual path.

It MUST NOT pick the newest-looking capsule and bypass edge validation.

---

## 13. Failed update before commit

Example:

```text
canonical N
→ activation of N+1 partially applied
→ commitDurable = false
```

Source remains N.

Returning to N is transaction rollback/source restoration and does not require pretending N+1 became canonical.

The signed recovery authorization still protects offline/major restoration cases, but semantic transaction ownership remains defined by SPEC-11.

---

## 14. Failure after commit

Example:

```text
canonical N+1 committed
→ later mandatory health failure
```

Restoring N is a true downgrade and requires valid `RecoveryAuthorization(N+1 → N)` plus current recovery security policy.

---

## 15. Manual user rollback

Manager may offer:

```text
Restore previous SplitOS version
```

only when the same recovery authorization checks pass.

User intent does not override signature/security-floor rules.

There is no production UI checkbox:

```text
Ignore security and downgrade anyway
```

---

## 16. Developer/test mode

Development builds may have separate developer roots/recovery roles and looser version policy.

A production installation cannot enter developer trust mode through an ordinary local setting.

Changing root of trust requires an explicit unsupported/development installation trust bootstrap outside normal production recovery.

---

## 17. Recovery authorization revocation

Trusted security metadata may revoke:

```text
authorizationId
recovery keyId
target releaseId
security epoch range
```

Revocation does not need to delete the capsule immediately.

It changes whether the capsule is an allowed automated recovery target.

---

## 18. Downgrade attack examples

Rejected:

```text
attacker serves authentic SplitOS 1.2 package
current = 2.0
no recovery authorization
→ DOWNGRADE_DENIED
```

Rejected:

```text
old RecoveryAuthorization says 1.5 → 1.4
current = 2.0
→ CONTEXT_MISMATCH
```

Rejected:

```text
valid 1.4 capsule
but target release hard-revoked locally
→ RECOVERY_TARGET_REVOKED
```

Allowed:

```text
current 1.5 fails
capsule 1.4 valid
authorization 1.5 → 1.4 valid
security floor permits 1.4
→ controlled recovery
```

---

## 19. Result

SplitOS obtains two independent transition models:

```text
NORMAL UPDATE
monotonic release/security progression

RECOVERY
exact signed bounded exception
```

A signature proves origin/integrity. It does not by itself grant transition authority.
