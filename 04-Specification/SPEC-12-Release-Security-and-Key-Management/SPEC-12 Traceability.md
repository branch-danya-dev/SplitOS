# SPEC-12 — Traceability

## 1. Purpose

This document maps Release Security & Key Management back to the canonical Trust, Builder and Update/Recovery layers.

---

## 2. Primary source chain

```text
03-Analysis-and-Design/10-Trust/
  Trust Model.md
  Artifact Build and Update Trust.md
  Security Control Matrix.md

03-Analysis-and-Design/11-Synthesis/
  System Synthesis.md
  Specification Handoff.md

04-Specification/SPEC-10-Builder-and-Component-Matrix/
→ trusted BuildManifest / package / BuildReceipt inputs

04-Specification/SPEC-11-Update-and-Recovery/
→ signed Release Envelope
→ previous-release Recovery Capsule
→ exact update/recovery transaction semantics

SPEC-12
→ concrete key hierarchy / signing / repository / rotation / anti-rollback rules
```

---

## 3. A&D Trust mapping

| Trust requirement | SPEC-12 realization |
|---|---|
| transport != artifact authenticity | TUF metadata chain + artifact hash binding |
| release private keys absent from installed runtime | offline/HSM signing boundary |
| code signing separate from release manifest | Authenticode + TUF dual control |
| key-purpose separation | role-specific private keys |
| key rotation without validation bypass | TUF root/delegation rotation |
| revocation lifecycle | signed SecurityRevocationSet + role rotation |
| valid old signature != allowed downgrade | releaseSequence/securityEpoch + RecoveryAuthorization |
| explicit recovery target | delegated recovery role exact-edge authorization |
| release/build artifacts bound to known issuer | TUF targets/delegations + Authenticode publisher policy |
| trust failures deny sensitive actions | normalized signature/rollback/revocation outcomes |

---

## 4. SPEC-10 Builder mapping

Builder trust inputs:

```text
BuildManifest
Component Matrix snapshot
SplitOS packages
Builder release
```

Production publication requires those artifacts to become authenticated release targets under SPEC-12.

`BuildReceipt` proves one build instance composition/verification; it does not possess signing authority.

```text
BuildReceipt valid
!= may publish release
```

Release promotion still requires signing/publishing policy.

---

## 5. SPEC-11 Update mapping

SPEC-11 semantic term:

```text
signed SplitOS Release Envelope
```

SPEC-12 concrete trust interpretation:

```text
preinstalled trusted Root
→ TUF metadata refresh
→ splitos-release delegated role
→ exact Release Envelope size/hash
→ Release Envelope semantics
→ artifact hash + publisher checks
```

---

## 6. Recovery mapping

SPEC-11 requires:

```text
N → N+1
→ preserve N Recovery Capsule before activation
```

SPEC-12 adds:

```text
Recovery Capsule N
→ authenticated target release material
→ valid RecoveryAuthorization(N+1 → N)
→ current local recovery security floor permits N
```

Thus:

```text
capsule exists
!= rollback authorized
```

---

## 7. Update transaction trust gates

| SPEC-11 stage | SPEC-12 gate |
|---|---|
| DISCOVERED | trusted non-expired TUF repository view |
| DOWNLOADING | target path/hash/size authenticated |
| VERIFYING_ARTIFACTS | digest + publisher policy |
| STAGING | no revoked target/key/publisher |
| PREPARING_RECOVERY | exact recovery authorization available |
| READY_TO_APPLY | releaseSequence/securityEpoch allowed |
| APPLYING | only already authenticated immutable staged content |
| VERIFYING_TARGET | installed bytes/publisher identity match release |
| COMMITTING | trusted release identity persisted with security floors |
| ROLLING_BACK | recovery authorization/context/floor verified |

---

## 8. Failure mapping

SPEC-12 failures map to existing Failure semantics:

```text
INVALID_SIGNATURE
METADATA_EXPIRED
KEY_REVOKED
PUBLISHER_REVOKED
RELEASE_REVOKED
ROLLBACK_DENIED
RECOVERY_AUTHORIZATION_INVALID
CLOCK_INDETERMINATE
ROOT_TRUST_LOST
```

Response remains:

```text
deny sensitive activation
→ preserve current/safe state
→ diagnostics/audit evidence
→ trusted update/recovery path
```

There is no `trust anyway` fallback.

---

## 9. Component ownership

### Release Authority

Owns:

- releaseSequence/securityEpoch assignment;
- release promotion;
- delegated release metadata;
- security/revocation policy publication.

### Root/Targets custodians

Own:

- trust-anchor keys;
- role/key/threshold changes;
- delegations.

### Authenticode Signing Service

Owns only use of Windows code-signing key according to policy.

It does not own release eligibility.

### Runtime Update Orchestration

Consumes trusted metadata and decides applicability transactionally.

It never owns production signing keys.

### Recovery Coordination

Consumes exact recovery authorization.

It cannot create it.

---

## 10. External standards mapping

```text
TUF
→ repository role separation, root rotation, targets/snapshot/timestamp, rollback/freeze protection

Windows Authenticode / WinVerifyTrust
→ executable publisher verification

RFC 3161 timestamping
→ long-lived signing-time evidence

NIST SP 800-57
→ key lifecycle/custody/rotation/compromise management principles
```

These standards constrain mechanisms but do not become owners of SplitOS product state.

---

## 11. Verification handoff

SPEC-14 must include executable cases for at least:

```text
valid root rotation
invalid old-root/new-root threshold
expired timestamp
snapshot rollback
malicious CDN target substitution
release key compromise/revocation
code-signing key compromise/revocation
wrong Authenticode publisher
artifact changed after signing
recovery authorization wrong source/target
hard-revoked recovery target
clock rollback
cache deletion does not reset floors
offline recovery with last trusted security metadata
emergency key rotation
```

---

## 12. Remaining implementation choices

Not yet fixed by SPEC-12:

- specific HSM/cloud signing vendor;
- code-signing CA/vendor;
- exact TUF client library/language binding;
- exact operational metadata expiry durations;
- exact certificate/publisher identity serialization;
- SLSA/in-toto provenance format;
- out-of-band customer recovery process after root-threshold compromise.

These choices may be resolved by implementation/procurement without weakening the normative trust model.
