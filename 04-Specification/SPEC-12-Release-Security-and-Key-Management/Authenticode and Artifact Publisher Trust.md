# Authenticode and Artifact Publisher Trust

## 1. Purpose

This document defines the Windows-native publisher trust requirements for SplitOS executable artifacts.

Repository authorization and code publisher trust are deliberately independent.

```text
TUF-authorized artifact hash
+
valid SplitOS Authenticode publisher signature
→ candidate trusted executable
```

Neither side alone is sufficient for privileged activation.

---

## 2. Covered artifact classes

Mandatory Authenticode validation applies to supported Windows executable/package forms where Windows Authenticode signing is applicable, including at least:

```text
.exe
.dll
.msi / installer payload where applicable
bootstrap/update/recovery executables
Broker service binary
Runtime Host
Manager
Game Launcher
native helper libraries
```

Non-executable structured metadata is authenticated through TUF/release metadata and its own schema/digest rules.

---

## 3. Signing baseline

Production signing baseline:

```text
file digest algorithm  = SHA-256
signature system       = Authenticode
publisher certificate  = SplitOS-approved code-signing identity
timestamp protocol     = RFC 3161
timestamp digest       = SHA-256
```

SHA-1-only signatures are forbidden for new SplitOS production artifacts.

---

## 4. Private key custody

The Authenticode private key MUST be non-exportable where supported and held in:

- hardware security module;
- hardware-backed managed code-signing service;
- equivalent controlled signing environment.

The raw private key MUST NOT appear in:

```text
CI environment variables
source repository
artifact ZIP
PFX stored as ordinary cloud secret
developer workstation profile
installed SplitOS runtime
```

---

## 5. Signing request model

Build worker produces immutable unsigned artifact digest:

```text
artifact
→ SHA-256
→ build provenance/identity
→ signing request
```

The signer independently validates:

```text
allowed repository/project
artifact type
releaseId
build provenance reference
approval state
requested publisher policy
```

before producing the signature.

---

## 6. Timestamping

Production PE signatures MUST be RFC 3161 timestamped when the artifact format supports it.

Timestamping provides evidence that the publisher signature existed while the signing certificate was valid, subject to certificate/revocation policy.

A timestamp server is not a SplitOS release authority.

```text
valid timestamp
!= trusted SplitOS release
```

---

## 7. Runtime verification

Before privileged activation of a staged executable, SplitOS performs both:

### Repository binding

```text
actual file SHA-256
== authenticated expected digest
```

### Publisher verification

Windows Authenticode trust validation using supported Windows verification APIs/policy.

For PE files, `WinVerifyTrust`/equivalent Windows Authenticode verification is the canonical OS-level mechanism candidate.

---

## 8. SplitOS publisher policy

A signature accepted by generic Windows trust is not automatically an accepted SplitOS publisher.

The authenticated Release Envelope includes a publisher policy reference such as:

```text
publisherPolicyId
allowed signer identity/certificate lineage
required EKU/purpose
required timestamp policy
```

The exact signer identity representation MAY use certificate/public-key identity suitable for secure rotation, but it MUST be versioned and authenticated by release metadata.

---

## 9. Certificate renewal

Routine code-signing certificate expiration/renewal is not equivalent to root rotation.

A new publisher certificate may be introduced when:

```text
new certificate/identity
→ authorized by trusted release security metadata
→ release artifacts signed with new identity
→ old identity retained/retired according to policy
```

There is no global `accept any certificate issued to similar display name` fallback.

---

## 10. Publisher compromise

If the Authenticode signing key/certificate is compromised:

1. stop signing immediately;
2. request CA revocation where applicable;
3. remove the publisher identity from future accepted publisher policy;
4. issue updated trusted metadata/security policy through unaffected TUF roles;
5. evaluate already released artifacts separately;
6. rotate to a new non-exportable publisher key/certificate.

A compromised Authenticode key alone does not grant TUF target publication authority.

---

## 11. Release metadata compromise defense

If `RELEASE_METADATA` key is compromised but Authenticode is not:

an attacker may attempt to publish a malicious target entry, but executable activation still requires the expected SplitOS publisher signature.

For non-executable knowledge/configuration artifacts, typed schema/capability boundaries remain the secondary defense.

---

## 12. Verification result model

Publisher verification returns explicit outcomes:

```text
PUBLISHER_VALID
FILE_DIGEST_MISMATCH
AUTHENTICODE_INVALID
SIGNER_NOT_ALLOWED
SIGNING_CERT_EXPIRED_WITHOUT_VALID_TIMESTAMP
TIMESTAMP_INVALID
CERTIFICATE_REVOKED
CERTIFICATE_STATUS_INDETERMINATE
WRONG_ARTIFACT_TYPE
```

Security-sensitive apply treats indeterminate publisher trust as denial unless a separately specified offline verification policy proves sufficient trust.

---

## 13. Offline verification

A recovery environment may not have network access for live certificate revocation checks.

Offline recovery therefore relies on:

- the previously trusted release receipt/TUF metadata;
- exact artifact digests;
- embedded Authenticode signature and timestamp evidence;
- locally persisted signed security/revocation metadata already known before going offline.

Offline recovery cannot know about certificate revocations published after its last trusted security refresh.

---

## 14. Signed executable is not mutable configuration

Executable artifacts MUST be immutable after publisher signing.

Runtime-specific configuration belongs outside the signed executable and is authenticated/validated through appropriate configuration contracts.

The release process MUST NOT patch binary bytes after signing and assume the signature remains meaningful.

---

## 15. Build and release ordering

Normative ordering:

```text
build unsigned artifact
→ test/scan
→ freeze digest
→ Authenticode sign + timestamp
→ verify signature
→ calculate final signed artifact digest
→ place signed artifact in immutable target store
→ publish that final digest in Release Envelope/TUF target metadata
```

The digest bound by the release repository is the digest of the final signed artifact.

---

## 16. Recovery tooling

`SplitOS.UpdateBootstrap.exe` and WinRE-hosted SplitOS recovery executables are high-risk artifacts.

They MUST satisfy the same or stricter publisher policy as Broker/runtime artifacts.

No recovery mode may skip publisher verification merely because normal Runtime Host is unavailable.

---

## 17. Result

Executable trust becomes dual control:

```text
Release repository says:
"this exact hash is SplitOS release artifact X"

Windows publisher policy says:
"this executable was signed by an allowed SplitOS publisher"

both valid
↓
artifact may proceed to compatibility/apply verification
```
