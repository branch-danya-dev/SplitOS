# Signing Pipeline and Release Promotion

## 1. Purpose

This document defines how build outputs become production-trusted SplitOS release artifacts without exposing production private keys to CI workers.

The release pipeline separates:

```text
build
sign
approve
publish
```

These are different authorities.

---

## 2. Pipeline overview

```text
source commit / release input
↓
controlled build
↓
unsigned artifact set
↓
tests / scans / provenance
↓
freeze immutable digests
↓
release approval
↓
Authenticode signing service
↓
verify final signed artifact
↓
compute final signed hashes
↓
create Release Envelope
↓
metadata signing service
↓
publish immutable targets
↓
publish targets/snapshot
↓
publish timestamp last
```

---

## 3. Build worker trust

A CI worker is allowed to produce candidate bytes.

It is not allowed to decide:

```text
this is production release
```

and does not hold:

- Root keys;
- Targets keys;
- Recovery keys;
- raw Authenticode private key;
- raw release metadata private key.

Compromise of a normal build worker therefore cannot alone mint a production-trusted release.

---

## 4. Immutable artifact identity

Before signing, every candidate artifact receives:

```text
buildArtifactId
source revision
build job identity
unsigned SHA-256 digest
artifact type
expected releaseId
```

After Authenticode signing, the final signed artifact is re-hashed and receives the digest used by release metadata.

Signed output becomes immutable.

---

## 5. Release approval

Production release signing requires an explicit promotion record.

Conceptual `ReleaseApproval`:

```text
releaseId
channel
target Windows compatibility decision
artifact set digest
required validation results
approver identity/reference
approval time
policy version
```

The signer verifies that the requested artifact/release matches the approved promotion record.

---

## 6. Workload identity

CI-to-signing-service access uses authenticated workload identity, not a static universal signing password.

The exact identity provider is implementation-specific, but the service must know:

```text
which pipeline requested signing
which repository/project
which releaseId
which role/capability
```

A request from an unrecognized workload is denied before key use.

---

## 7. Signing service capabilities

Separate endpoints/capabilities exist conceptually:

```text
SignAuthenticodeArtifact
SignReleaseDelegationMetadata
SignKnowledgeDelegationMetadata
SignSnapshotMetadata
SignTimestampMetadata
```

There is no general production capability:

```text
SignBytes(keyId, arbitraryBytes)
```

available to ordinary CI.

Root/Targets/Recovery signatures occur through separate ceremony tooling.

---

## 8. Signer policy checks

Before using a production key, signer validates:

- caller/workload identity;
- requested role;
- allowed repository/path namespace;
- artifact type;
- release channel;
- expected approval;
- digest equality;
- metadata version monotonicity where applicable;
- key active/not revoked;
- request not replayed.

Denied requests are audit events.

---

## 9. Authenticode order

Correct order:

```text
unsigned artifact
→ Authenticode sign
→ RFC 3161 timestamp
→ verify signature
→ final signed SHA-256
→ release metadata binding
```

Wrong order:

```text
hash unsigned artifact
→ publish hash
→ sign file later
```

because Authenticode signing changes file bytes and therefore final artifact digest.

---

## 10. Metadata publication order

Repository publication follows:

```text
1. upload immutable target bytes
2. publish delegated targets metadata
3. publish top-level targets if delegation changed
4. publish snapshot
5. publish timestamp last
```

Timestamp exposure makes the new repository view discoverable.

A client must never be expected to trust half-published release state.

---

## 11. Recovery authorization ceremony

For each production release that requires previous-version rollback capability:

```text
source = N+1
recovery target = N
```

release engineering prepares exact `RecoveryAuthorization` content.

The recovery threshold signers independently verify:

- exact source/target identity;
- target is release-validated;
- target security epoch policy;
- capsule compatibility/schema rollback bridge;
- no hard revocation applies.

Only then is the recovery authorization signed/published.

SPEC-11 update activation is blocked if the mandatory recovery authorization cannot be authenticated/captured.

---

## 12. Root/Targets ceremony

Root/Targets ceremonies are out-of-band from normal CI.

A ceremony record includes:

```text
ceremonyId
old metadata version
new metadata version
changed roles/keys/thresholds
participants
public-key fingerprints
signatures collected
verification result
publication result
```

Before publication, the new root is independently verified under both old and new thresholds.

---

## 13. Release signing is not deployment signing

The production signer does not sign local machine-specific recovery capsule state.

Client-generated machine data cannot be submitted to production signer as a normal release artifact.

This prevents installed endpoints from using the publisher service as an arbitrary signing oracle.

---

## 14. Artifact storage

After signing/verification, production artifacts are stored under immutable/content-addressed release identity.

Replacing bytes at the same logical immutable target path is forbidden.

A corrected artifact requires a new target identity/release metadata version.

---

## 15. Build provenance

Production releases SHOULD retain build provenance linking:

```text
source revision
builder identity
build environment/toolchain identity
artifact digest
releaseId
```

The exact SLSA/in-toto attestation format is a release-engineering choice, but provenance must be sufficient for incident investigation and reproducibility analysis.

Provenance does not replace release signatures.

---

## 16. Reproducibility

Byte-for-byte reproducible builds are desirable but not required for v1 conformance unless a specific artifact pipeline can guarantee them.

The mandatory requirement is traceable immutable build input/output and verified signing/promotion.

---

## 17. Failed signing behavior

If signing or timestamping fails:

```text
release remains unpublished
```

No unsigned fallback artifact is promoted.

If metadata signing succeeds but target upload/verification fails, timestamp is not advanced to expose the incomplete state.

---

## 18. Emergency release path

Emergency releases use a pre-defined fast approval path with the same keys/signatures.

Emergency does not mean:

```text
skip review
skip publisher signature
skip recovery authorization
skip metadata verification
```

Any deliberately reduced non-security validation is explicitly recorded and limited to policy-approved areas.

---

## 19. Separation from account backend

Account/entitlement backend may tell a client whether it is entitled to an update capability.

It cannot sign or invent release metadata.

```text
backend says entitled
!= release exists/trusted
```

Release repository trust remains independent.

---

## 20. Result

A production artifact must cross several independent gates:

```text
build output
→ approval
→ publisher signature
→ final digest
→ release metadata signature
→ repository consistency/freshness
→ client compatibility/security policy
```

No ordinary CI credential is enough to collapse all gates into one.
