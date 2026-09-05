# SPEC-12 — Release Security & Key Management

## Purpose

SPEC-12 defines the production trust chain for SplitOS releases, update metadata, executable publisher signatures, key rotation/revocation, recovery downgrade authorization and signing-system compromise response.

It converts the A&D Trust model and SPEC-11 update/recovery semantics into an implementable release-security contract.

Core principle:

```text
transport authenticity
!= repository metadata authenticity
!= artifact publisher authenticity
!= release authorization
!= recovery authorization
```

No single online key is allowed to become a universal SplitOS signing authority.

---

## 1. Security objectives

The release system MUST provide:

1. a durable offline root of trust;
2. role-separated metadata signing;
3. repository freshness and rollback/freeze protection;
4. exact artifact digest/size binding;
5. independent Windows executable publisher signing;
6. key rotation without disabling verification;
7. key revocation and emergency response;
8. explicit recovery downgrade authorization;
9. local persistence of previously trusted metadata/version floors;
10. auditability of every signing and promotion event.

---

## 2. v1 trust architecture

SplitOS adopts The Update Framework (TUF) role model and client verification workflow as the repository-metadata security substrate.

Top-level roles:

```text
ROOT
TARGETS
SNAPSHOT
TIMESTAMP
```

SplitOS delegated target roles:

```text
splitos-release
splitos-knowledge
splitos-recovery
```

Executable signing remains a separate Windows-native trust system:

```text
TUF release authorization
+
artifact digest binding
+
Authenticode publisher signature
+
RFC 3161 timestamp where applicable
```

A PE/DLL/EXE artifact MUST satisfy both repository authorization and publisher-signature policy before privileged activation.

---

## 3. Key hierarchy summary

```text
                    Offline Root Authority
                     2-of-3 threshold
                           │
          ┌────────────────┴────────────────┐
          │                                 │
   Top-level Targets                 root rotation
   2-of-3 threshold
          │
    delegates paths
          │
  ┌───────┼───────────┐
  │       │           │
release knowledge  recovery
online   online     offline/
HSM      HSM        controlled threshold
  │       │           │
  ▼       ▼           ▼
release  knowledge   exact rollback
metadata packages    authorization

Snapshot key   → protected online signer
Timestamp key  → separate automated online signer

Authenticode publisher key
→ separate non-exportable code-signing key/certificate
```

Production trust material MUST remain separate from development/test roots and keys.

---

## 4. Root trust bootstrap

A supported SplitOS installation MUST ship with an initial trusted `root.json` and root version.

The installed runtime stores:

```text
trustedRootVersion
trustedRootMetadata
trustedMetadataVersionFloor
trustedReleaseSequenceFloor
trustedSecurityEpoch
trustedTimeFloorUtc
```

These values are durable security state, not rebuildable cache.

Deleting the update cache MUST NOT reset rollback/freeze protection.

---

## 5. Production root threshold

Production v1 root role:

```text
3 independent root keys
threshold = 2
```

Root private keys MUST be offline and independently controlled.

Acceptable storage includes separate hardware cryptographic devices or equivalent offline protected key custody.

Root keys MUST NOT exist in:

- CI runners;
- release CDN;
- account backend;
- installed SplitOS clients;
- developer laptops as ordinary exportable files;
- production application secrets.

Development/test repositories MAY use lower thresholds, but their roots MUST NOT be trusted by production clients.

---

## 6. Metadata algorithms

v1 repository metadata signature baseline:

```text
TUF-compatible ECDSA
scheme: ecdsa-sha2-nistp256
hash: SHA-256
```

Artifact digest baseline:

```text
SHA-256 mandatory
```

The algorithm suite is versioned in root metadata so a future migration can be authorized through root rotation rather than an application code change that disables validation.

---

## 7. Release Envelope

The SPEC-11 `SplitOS Release Envelope` remains the semantic release descriptor.

In SPEC-12 its authenticity is established by TUF delegated target metadata:

```text
splitos-release delegated role
→ authorizes exact Release Envelope path
→ binds size + SHA-256 digest
→ Release Envelope binds all release artifact identities/digests/policies
```

Therefore:

```text
Release Envelope downloaded over HTTPS
!= trusted release
```

A trusted envelope requires a valid TUF metadata chain and all local rollback/freshness checks.

---

## 8. Executable publisher signing

Windows executable artifacts MUST be Authenticode signed using a SplitOS-approved code-signing certificate/key.

The private key MUST be non-exportable and hardware-backed or held by an equivalent managed signing service.

Signing requirements:

```text
file digest       = SHA-256
publisher signing = Authenticode
timestamp         = RFC 3161 + SHA-256
```

Release metadata additionally binds an expected publisher identity/policy so a random otherwise trusted Windows code-signing certificate cannot become an accepted SplitOS publisher.

Authenticode alone does not authorize a release.

---

## 9. Role separation

Private key purposes MUST remain distinct:

```text
ROOT
TARGETS
RELEASE_METADATA
KNOWLEDGE_METADATA
RECOVERY_AUTHORIZATION
SNAPSHOT
TIMESTAMP
AUTHENTICODE_CODE_SIGNING
ENTITLEMENT_ASSERTION
```

One private key MUST NOT be reused across these roles.

Compromise of one role therefore has a bounded blast radius.

---

## 10. Online vs offline roles

### Offline / ceremony-controlled

```text
ROOT
TARGETS
RECOVERY_AUTHORIZATION
```

### Protected online signing service

```text
RELEASE_METADATA
KNOWLEDGE_METADATA
SNAPSHOT
TIMESTAMP
AUTHENTICODE_CODE_SIGNING
```

An online key MUST remain non-exportable where the signing technology permits it.

An ordinary CI runner MUST submit a signing request; it MUST NOT receive raw private key material.

---

## 11. Trusted repository refresh

Normal update discovery follows TUF client ordering and rollback checks:

```text
trusted root
→ sequential root rotation if newer roots exist
→ timestamp
→ snapshot
→ targets/delegations
→ release envelope target
→ artifacts
```

Metadata is validated before becoming trusted/persisted.

A failed rollback/version/signature/expiry check MUST NOT overwrite the previously trusted metadata state.

---

## 12. Freshness / freeze handling

Timestamp and other TUF metadata have explicit expiry.

SplitOS also persists a monotonic trusted-time floor:

```text
trustedTimeFloorUtc = max(previousTrustedTimeFloor, trusted successful refresh time)
```

If the local system clock moves materially behind the trusted floor, update activation becomes:

```text
CLOCK_INDETERMINATE
```

until time can be re-established safely.

The product MUST NOT bypass metadata expiry because the update server/CDN is reachable.

---

## 13. Anti-rollback

Normal update/install paths require both:

```text
TUF metadata versions not rolled back
+
SplitOS releaseSequence/securityEpoch allowed
```

A historically valid signature on an old release does not authorize installing it through the normal updater.

Persistent floors include at least:

```text
highestTrustedRootVersion
highestTrustedSnapshotVersion
highestTrustedReleaseSequence
highestTrustedSecurityEpoch
```

Resetting caches MUST NOT reset these floors.

---

## 14. Recovery downgrade

Recovery is a separate authority path.

A previous release may be restored only when the Recovery Capsule contains a valid `RecoveryAuthorization` authenticated by the dedicated `splitos-recovery` delegated role.

The authorization binds at least:

```text
authorizationId
sourceReleaseId/sourceSequence
exactTargetReleaseId/targetSequence
minimumSecurityEpoch
artifact/release-envelope identity
allowedContext = RECOVERY_ONLY
policyVersion
```

Normal updater code MUST reject a `RECOVERY_ONLY` authorization as permission for an ordinary downgrade.

---

## 15. Recovery Capsule trust

A locally created Recovery Capsule does not receive a production release private key.

Its publisher trust comes from the already authenticated release material captured inside it:

```text
trusted TUF metadata chain / receipt
+
Release Envelope
+
artifact digests
+
Authenticode signatures for executable payloads
+
RecoveryAuthorization
```

The local capsule container adds corruption detection and protected-store immutability, but local machine state inside the capsule never becomes a signing authority.

---

## 16. Root rotation

Root rotation follows TUF root-chain rules.

A new root version MUST be:

1. signed by the threshold defined by the currently trusted root;
2. signed by the threshold defined by the new root;
3. exactly the next trusted root version in the sequential chain;
4. persisted before the client proceeds to later metadata.

There is no supported path:

```text
root verification failed
→ download random replacement trust bundle
```

---

## 17. Key compromise principle

Compromise response depends on role.

Examples:

```text
TIMESTAMP compromised
→ rotate timestamp key; cannot authorize new targets alone

RELEASE_METADATA compromised
→ revoke delegation key; executable injection still blocked by independent Authenticode policy

AUTHENTICODE compromised
→ revoke publisher certificate/key; repository metadata still blocks unauthorized target publication

RECOVERY key compromised
→ revoke recovery role/key and raise recovery security floor

ROOT threshold compromised
→ catastrophic root-trust incident; ordinary online recovery cannot be assumed safe
```

---

## 18. Installed client behavior during security incident

If trusted metadata indicates a key/release is revoked:

```text
new activation denied
staged affected payload quarantined
current installed release evaluated separately
Manager surfaces required security update/recovery state
Windows Desktop remains usable whenever possible
```

A revoked signing key does not automatically mean every historically timestamped binary must be deleted immediately. Treatment is policy/release-specific and must be encoded in signed security metadata.

---

## 19. Offline limitation

An offline machine cannot learn a newly published key revocation.

Therefore SplitOS MUST state explicitly:

```text
offline verification
→ proves against the last trusted local security metadata
→ cannot prove knowledge of revocations published after last trusted refresh
```

This is not solved by extending token/key lifetimes indefinitely.

---

## 20. Signing service boundary

Production CI/build workers do not own production signing keys.

Conceptual chain:

```text
build
→ immutable artifact digest
→ release approval
→ authenticated signing request
→ policy engine
→ non-exportable signing key/HSM
→ signed artifact/metadata
→ verification
→ immutable promotion
```

The signer MUST verify role/path/release constraints before using a key.

---

## 21. Audit requirements

Every production signing operation records at least:

```text
signingEventId
keyId / role
artifact or metadata digest
releaseId
requesting workload identity
approval identity/reference
timestamp
result
signer/HSM reference
```

Audit records are evidence, not release authority.

---

## 22. Forbidden designs

Production SplitOS MUST NOT use:

```text
one universal private key for all purposes
exportable private keys stored as CI secrets
trust-on-first-use from CDN content
signature-validation disable switches in normal recovery
downgrade because old binary is still Authenticode-valid
unsigned emergency updates
client-generated production release signatures
root-key material in installed runtime
```

---

## 23. Source standards

SPEC-12 is intentionally aligned with:

- The Update Framework role/metadata/client model;
- Windows Authenticode / WinVerifyTrust;
- RFC 3161 timestamping through SignTool-compatible Windows tooling;
- general cryptographic key-lifecycle guidance from NIST SP 800-57.

The exact HSM/vendor/CA/CDN product remains an implementation procurement decision and is not a semantic dependency.

---

## 24. Decisions

```text
SPEC-DEC-129  Production update metadata uses TUF role semantics/client verification.
SPEC-DEC-130  Production root uses 3 independent keys with 2-of-3 threshold.
SPEC-DEC-131  Production root private keys remain offline.
SPEC-DEC-132  Top-level targets and recovery authorization are ceremony-controlled/offline roles.
SPEC-DEC-133  Frequent release/knowledge/snapshot/timestamp signing uses separated protected online keys.
SPEC-DEC-134  v1 metadata algorithm baseline is ECDSA P-256 + SHA-256.
SPEC-DEC-135  SHA-256 is mandatory artifact digest baseline.
SPEC-DEC-136  PE executable activation requires independent Authenticode publisher validation.
SPEC-DEC-137  Authenticode signatures use SHA-256 and RFC 3161 SHA-256 timestamping.
SPEC-DEC-138  Metadata-signing and Authenticode private keys are never the same key.
SPEC-DEC-139  Normal downgrade requires no implicit permission from an old valid signature.
SPEC-DEC-140  Recovery downgrade requires dedicated recovery authorization bound to an exact edge/context.
SPEC-DEC-141  Recovery Capsules carry previously authenticated release trust; clients never production-sign capsules.
SPEC-DEC-142  TUF/client rollback floors persist outside rebuildable update cache.
SPEC-DEC-143  Root rotation requires old-root and new-root threshold authorization.
SPEC-DEC-144  Production private keys must not be exportable CI secrets.
SPEC-DEC-145  Security revocation policy is signed/versioned; offline clients only know the last trusted revocation state.
```

---

## 25. Next

SPEC-13 defines Observability & Diagnostics, including the security/audit event taxonomy consumed by this specification.
