# SplitOS — Artifact, Build and Update Trust

## 1. Purpose

Документ определяет trust chain для:

- SplitOS binaries;
- Build Manifest;
- SplitOS packages;
- prepared installation baseline;
- runtime updates;
- recovery artifacts;
- Windows source inputs.

Главная цель:

> Transport/download success не должен быть достаточным доказательством того, что artifact является подлинным SplitOS release.

---

## 2. Trust chain

Canonical chain:

```text
Release authority
→ signed release metadata / manifest
→ artifact identity + cryptographic digest
→ artifact signature/integrity validation
→ compatibility validation
→ staging
→ privileged apply
→ actual-state verification
→ canonical installed baseline commit
```

---

## 3. Release authority

SplitOS release domain owns:

- release identifier/version;
- supported Windows base compatibility;
- Build Manifest version;
- package set;
- package hashes/signatures;
- update applicability metadata;
- signing key identity/key id.

Runtime/backend cannot invent a release merely by returning a URL.

---

## 4. Signing key boundary

Release-signing private key is a high-value asset.

It must not exist in:

- installed SplitOS runtime;
- Media Builder client distribution;
- user profile;
- update cache;
- normal account backend application process unless release design explicitly requires and protects it.

Preferred organizational boundary:

```text
Release/build system
→ private signing authority

Installed clients
→ public verification material / trusted certificate chain
```

---

## 5. Code signing

For Windows executable artifacts, Authenticode is the primary Windows-native provenance/integrity candidate.

Examples:

```text
SplitOSRuntimeHost.exe
SplitOSManager.exe
SplitOSBroker.exe
SplitOSUpdater.exe
DLLs/installers where supported
```

Windows `WinVerifyTrust`/Authenticode policy can verify that PE/software publisher signature is valid and file was not modified after signing.

Code signing alone does not replace release-manifest binding.

---

## 6. Manifest signing

Structured release metadata such as:

```text
BuildManifest
UpdateManifest
Component Matrix snapshot
package index
compatibility metadata
```

may require detached/structured digital signature because not all metadata is PE executable content.

Exact signature envelope/algorithm is OPEN, but semantic requirements are fixed:

- authenticated issuer;
- integrity;
- schema/version binding;
- release identifier;
- key identifier;
- anti-downgrade/version policy;
- artifact digest binding.

---

## 7. Artifact binding

Manifest should bind every trusted artifact by stable identity and digest.

Conceptual:

```text
ArtifactEntry
- artifactId
- version
- type
- expectedDigest
- size
- signature/provenance requirement
- mandatory/optional
```

Download URL itself is not artifact identity.

---

## 8. TLS vs artifact authenticity

HTTPS/TLS protects network transport and authenticates server endpoint.

It does not by itself provide durable release artifact trust after:

- CDN/cache storage;
- local staging;
- mirror/retry;
- offline Builder input;
- later reboot/recovery.

Therefore:

```text
HTTPS download
+
signed manifest/artifact verification
```

are separate layers.

---

## 9. Timestamping

Authenticode timestamping allows proving an artifact was signed while signing certificate was valid, subject to trust policy.

Release process should support timestamped code signatures where appropriate.

Exact timestamp authority/policy remains release engineering specification.

---

## 10. Key hierarchy

Recommended conceptual separation:

```text
Root/release trust authority
        ↓
release/update signing key(s)
        ↓
artifacts/manifests
```

Potential separation:

- application code-signing key;
- update manifest/package signing key;
- offline entitlement assertion signing key.

Using one private key for every trust domain increases blast radius.

Exact hierarchy is OPEN but key-purpose separation is preferred.

---

## 11. Key rotation

Installed clients must support trusted key rotation without insecure `disable signature validation` windows.

Conceptual approaches:

- new key authorized by currently trusted signed metadata;
- trust bundle update through already trusted release;
- certificate chain transition according to OS trust model.

Rotation must be versioned and auditable.

---

## 12. Key revocation

If signing key is compromised, SplitOS needs policy for:

- rejecting future artifacts from revoked key;
- release metadata indicating revocation;
- emergency trusted-key update;
- previously installed artifact treatment;
- offline clients.

Exact mechanism remains OPEN but revocation is mandatory lifecycle concern.

---

## 13. Anti-downgrade

A valid signature on an old release is not necessarily enough to allow installation.

Update trust must consider:

```text
signature valid
+
release allowed by compatibility/security policy
+
version transition authorized
```

Potential downgrade may only be allowed for explicit recovery target.

Normal attacker-controlled rollback to known-vulnerable release must not be silently accepted just because signature remains valid.

---

## 14. Builder Windows source trust

Windows source remains external Microsoft authority input.

Builder should validate at least conceptually:

- supported Windows edition/version/build;
- image structure/readability;
- source identity expected by SplitOS release;
- Microsoft/platform signature/provenance where applicable;
- absence of unexpected modification detectable by supported validation.

Exact source acquisition/authenticity validation remains OPEN and depends on Microsoft distribution/licensing mechanics.

Important:

```text
filename = Win11.iso
```

is not source trust.

---

## 15. Build Manifest trust

Builder may execute powerful offline operations.

Therefore Build Manifest is privileged code-like input.

Before execution:

```text
parse bounded schema
→ validate signature
→ validate release/key
→ validate schema version
→ validate target Windows compatibility
→ validate operation allowlist
```

Unsigned local manifest must not be treated as official supported SplitOS baseline definition.

---

## 16. Manifest operation allowlist

Even signed manifest parser/executor should only support typed operations:

```text
RemoveProvisionedApp
DisableFeature
RemovePackage
ApplyOfflinePolicy
InstallSplitOSPackage
ValidateComponentState
```

No generic:

```text
ExecuteArbitraryPowerShell
RunArbitraryCommand
```

unless an explicitly isolated, signed, narrowly specified migration mechanism is ever introduced.

---

## 17. Build output identity

Prepared baseline gets identity only after build verification.

Conceptually:

```text
source identity
+
manifest id/version
+
package set
+
transformation result
+
verification result
→ InstalledBaselineIdentity candidate
```

If mandatory transformation failed:

```text
supportedBaseline = false
```

No trusted label is applied.

---

## 18. Runtime package trust

At install/startup/update, SplitOS should be able to verify critical installed binaries/config where appropriate.

Potential controls:

- Authenticode verification;
- protected installation directory ACL;
- version/manifest digest checks;
- Windows service binary path protection;
- repair/recovery against known release metadata.

Exact startup integrity verification frequency is Specification/performance tradeoff.

---

## 19. Update discovery trust

Backend may tell client:

```text
release X available
```

but availability response is not sufficient to trust package bytes.

Update Orchestration separately validates:

- release metadata authenticity;
- entitlement/access;
- compatibility;
- package integrity/signature.

---

## 20. Staging trust

Downloaded/staged package remains untrusted until verification completes.

```text
DOWNLOADED
!= VERIFIED
!= STAGED_TRUSTED
```

Staging directory must not permit ordinary user to replace verified artifact between verification and privileged apply.

This introduces TOCTOU concern.

Preferred approaches include:

- protected staging directory;
- open file handle/reference preserved through apply;
- revalidation immediately before privileged use;
- immutable content-addressed storage semantics where practical.

Exact implementation OPEN.

---

## 21. Privileged apply trust

Broker/updater should receive:

```text
verified artifact identity/reference
```

not arbitrary user-controlled path.

Before destructive operation, privileged side may revalidate critical signature/digest binding.

---

## 22. Post-apply verification

Cryptographically authentic package can still fail operationally.

Therefore:

```text
trusted artifact
!= successful target system state
```

After apply/reboot:

- read actual runtime/version/component state;
- validate baseline invariants;
- verify critical binaries/services;
- only then commit target `InstalledBaselineIdentity`.

---

## 23. Recovery artifact trust

Recovery may apply older/alternate artifacts but cannot bypass trust.

Recovery target must be:

- explicitly allowed;
- signature/integrity validated;
- known-compatible recovery path;
- transaction-correlated.

Emergency situation does not permit `run unsigned repair.exe` automatically.

---

## 24. Update transaction durability

Trust-sensitive metadata persisted across reboot:

```text
updateTransactionId
sourceRelease
expectedTargetRelease
manifestId
verifiedArtifactIds
phase
```

must be validated for schema/integrity/context on resume.

Corrupt transaction record should trigger reconciliation/recovery, not arbitrary continuation.

---

## 25. Split-brain update

Example:

```text
Runtime Host = v2
Broker = v1
Manager = v2
```

All binaries may be individually signed but release composition is incoherent.

Therefore release trust includes **set consistency**, not only per-file signatures.

Version compatibility matrix/protocol handshake must detect mismatched critical components.

---

## 26. Protocol version trust

Updated components must negotiate only supported interface/protocol versions.

If signed but incompatible component connects:

```text
AUTHENTIC
but
INCOMPATIBLE
```

Result must be controlled degraded/recovery behavior, not blind trust.

---

## 27. External dependency packages

Any third-party runtime/library packaged by SplitOS requires provenance/license/update policy.

Third-party signature may be evidence, but SplitOS release still owns decision to include exact version.

```text
vendor signed
!= SplitOS approved
```

---

## 28. Supply-chain metadata

Release engineering should eventually record:

- source revision;
- build pipeline/version;
- artifact hashes;
- signer/key id;
- timestamp;
- dependency versions;
- Windows base compatibility;
- Build Manifest version.

Reproducibility/SBOM details belong later specification/engineering but trust model anticipates them.

---

## 29. Trust failure responses

### Invalid signature

Reject artifact; do not apply.

### Digest mismatch

Reject and redownload/recover.

### Unknown signing key

Reject or require trusted trust-bundle update path.

### Expired/revoked key

Evaluate timestamp/revocation policy; do not silently ignore.

### Manifest schema unsupported

Reject as incompatible.

### Signed but wrong target release/platform

Reject compatibility/context mismatch.

### Staged artifact modified after verification

Reject and recreate stage.

---

## 30. Security invariants

### ART-INV-001

Release private signing key never ships to clients.

### ART-INV-002

Official Build Manifest requires authenticated integrity/provenance.

### ART-INV-003

HTTPS alone does not make artifact trusted.

### ART-INV-004

Update target baseline is not committed until post-apply verification.

### ART-INV-005

Broker does not execute arbitrary unverified local package path.

### ART-INV-006

Valid old signature does not automatically authorize downgrade.

### ART-INV-007

Critical release composition must be coherent, not merely individually signed.

### ART-INV-008

Recovery cannot bypass artifact trust requirements.

### ART-INV-009

Unsigned locally modified Builder output cannot claim supported SplitOS baseline identity.

### ART-INV-010

Trust-key rotation/revocation must have explicit lifecycle.

---

## 31. Research basis

Windows Authenticode/`WinVerifyTrust` provides a native mechanism to verify signed executable publisher provenance/integrity, and Authenticode supports timestamping signed executables.

This is a suitable mechanism family for Windows binary artifacts, while structured manifests still require an explicitly designed signed metadata format.

---

## 32. Open questions

- exact code-signing certificate/provider;
- exact manifest signature algorithm/envelope;
- root/update/application key separation;
- key storage/HSM/CI signing workflow;
- key rotation/revocation format;
- artifact digest algorithm/versioning;
- timestamp authority/policy;
- exact Windows source authenticity validation;
- protected staging implementation;
- rollback/downgrade authorization format;
- release composition manifest;
- startup integrity check frequency;
- SBOM/reproducible-build requirements.

---

## 33. Result

Canonical artifact trust chain:

```text
Trusted release authority
→ signed manifest
→ exact artifact digest/provenance
→ protected staging
→ privileged apply
→ actual state verification
→ supported baseline commit
```

This prevents `downloaded successfully` or `installer exited 0` from becoming false release trust.