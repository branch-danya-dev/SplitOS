# Key Hierarchy and Custody

## 1. Purpose

This document defines the production SplitOS cryptographic key roles, custody boundaries, usage restrictions and lifecycle expectations.

The design follows the rule:

```text
key possession
!= universal authorization
```

A key is trusted only for the role/path/capability assigned by trusted metadata.

---

## 2. Production key inventory

| Key role | Typical custody | Threshold | Network posture | Purpose |
|---|---|---:|---|---|
| `ROOT` | independent offline hardware devices | 2-of-3 | offline | authorize root/role/key changes |
| `TARGETS` | independent offline hardware devices | 2-of-3 | offline | delegate target namespaces/roles |
| `RELEASE_METADATA` | protected signing service/HSM | 1 | online controlled | authorize stable SplitOS release envelopes |
| `KNOWLEDGE_METADATA` | protected signing service/HSM | 1 | online controlled | authorize knowledge/catalog releases |
| `RECOVERY_AUTHORIZATION` | independent controlled/offline custody | 2-of-3 | offline/ceremony | authorize exact recovery downgrade edges |
| `SNAPSHOT` | protected signing service/HSM | 1 | online controlled | bind consistent repository metadata view |
| `TIMESTAMP` | separate protected signing service/HSM | 1 | online automated | freshness/freeze detection |
| `AUTHENTICODE_CODE_SIGNING` | CA-backed non-exportable signing key/HSM | 1 | signing service | sign Windows PE/MSI/package artifacts |
| `ENTITLEMENT_ASSERTION` | backend security signing service | separate | online | SPEC-04 offline entitlement assertions |

The same private key MUST NOT occupy more than one row.

---

## 3. Root keys

Production root consists of three independent private keys:

```text
RootKey-A
RootKey-B
RootKey-C

threshold = 2
```

Required properties:

- generated in approved cryptographic hardware or equivalent isolated cryptographic environment;
- private material non-exportable where supported;
- no ordinary online access;
- separate custody records;
- separate recovery/backup process;
- use only during root ceremonies;
- every use explicitly recorded.

Root operators SHOULD be separated so one compromised workstation/account cannot silently satisfy threshold.

---

## 4. Top-level Targets keys

Top-level Targets keys exist primarily to authorize/delegate target path namespaces.

They SHOULD be used infrequently.

Example delegations:

```text
releases/stable/*
→ RELEASE_METADATA

knowledge/*
→ KNOWLEDGE_METADATA

recovery/*
→ RECOVERY_AUTHORIZATION
```

A delegated role cannot exceed its authorized path set.

Compromise of `RELEASE_METADATA` therefore MUST NOT grant authority over `recovery/*` or root metadata.

---

## 5. Release metadata key

`RELEASE_METADATA` is the frequent production release-signing role.

It MAY be online only through a constrained signing service.

The key MUST be non-exportable where supported.

The signing service accepts only structured requests such as:

```text
SignDelegatedTargetsMetadata(
  role = splitos-release,
  version,
  expires,
  targetPaths,
  targetHashes,
  targetSizes
)
```

It MUST NOT expose:

```text
SignArbitraryBytes(privateKeyId, bytes)
```

to ordinary CI workloads.

---

## 6. Knowledge metadata key

Knowledge updates can alter:

- compatibility decisions;
- client adapter capability data;
- optimization knowledge;
- policy catalogs.

They can therefore influence sensitive runtime behavior even when no executable changes.

For this reason `KNOWLEDGE_METADATA` is separate from `RELEASE_METADATA`.

A compromise of the knowledge role cannot authorize executable release paths.

Typed schema and capability restrictions from earlier SPECs remain mandatory defense in depth.

---

## 7. Recovery authorization keys

Recovery is deliberately separated from normal release publication.

Production recovery role:

```text
RecoveryKey-A
RecoveryKey-B
RecoveryKey-C
threshold = 2
```

Its only authority is to sign delegated metadata containing exact `RecoveryAuthorization` targets.

It cannot:

- authorize arbitrary new executables;
- sign stable release metadata;
- modify root roles;
- authorize a target outside recovery namespace.

This role exists because:

```text
valid old release
!= permitted rollback
```

---

## 8. Snapshot and Timestamp keys

These roles are separate even though both can be online.

### Snapshot

Binds versions/hashes of target metadata into a consistent repository view.

### Timestamp

Frequently re-signed, short-lived freshness metadata.

Compromise of Timestamp alone MUST NOT let an attacker authorize a new target.

The key may be rotated frequently without touching artifact signing keys.

---

## 9. Authenticode key

The Windows publisher key is not part of the TUF role keyset.

It uses a code-signing certificate accepted by the Windows publisher policy chosen for SplitOS.

Required operational baseline:

```text
private key = non-exportable
signing      = controlled signing service/HSM
file digest  = SHA-256
timestamp    = RFC 3161 SHA-256
```

The update metadata includes expected publisher identity information so clients validate both:

```text
Windows Authenticode policy
+
SplitOS release publisher policy
```

---

## 10. Key IDs

Every security key has stable key identity metadata including:

```text
keyId
role
algorithm/scheme
public key or certificate identity
createdAt
activationState
retirementState
compromiseState
custody reference
```

Key IDs MUST be derived/managed consistently with the adopted metadata framework and MUST NOT use display names as identity.

---

## 11. Key lifecycle states

Conceptual lifecycle:

```text
GENERATED
→ PROVISIONED
→ ACTIVE
→ ROTATING
→ RETIRED
→ DESTROYED
```

Compromise may transition from any active/retired state to:

```text
COMPROMISED
```

`RETIRED` means no new signatures are produced.

It does not automatically mean every historic signature is invalid.

---

## 12. Key generation

Production keys MUST be generated using cryptographically secure key generation within approved cryptographic tooling/hardware.

No production key is generated from:

- developer passwords;
- deterministic repository seeds;
- CI environment variables;
- source-controlled fixtures;
- user-chosen entropy strings.

Development/test fixture keys MUST be clearly segregated and rejected by production roots.

---

## 13. Private key export

Production private-key export is forbidden by default.

If a technology requires backup/export for an offline role, backup must preserve equivalent protection and multi-person custody.

An encrypted PFX in a normal source repository, artifact store or generic cloud secret is not an acceptable production private-key custody mechanism.

---

## 14. Operator authentication

Human access to offline/root/recovery ceremonies requires strong operator authentication and explicit role assignment.

The exact enterprise identity provider/MFA technology is implementation-specific, but one ordinary GitHub/CI account MUST NOT alone satisfy root/recovery threshold.

---

## 15. Development profile

Local development may use:

```text
DevRoot
DevReleaseKey
DevCodeSigningCertificate
```

with simpler custody.

However:

```text
production client
MUST NOT trust DevRoot
```

Production and test channels are cryptographically separate, not distinguished only by a URL/string flag.

---

## 16. Key backup and disaster recovery

For every offline role, operational policy must define:

- whether backup exists;
- how many backup shares/devices exist;
- required custodians;
- test procedure for restore;
- destruction process after rotation;
- what happens if threshold can no longer be met.

Loss of enough root keys to fall below threshold is a release-authority disaster and MUST have a documented offline business recovery process.

---

## 17. Forbidden key reuse examples

Forbidden:

```text
one PFX
→ Authenticode
→ release JSON
→ recovery authorization
→ entitlement assertion
```

Forbidden:

```text
GitHub Actions secret
→ contains root private key
```

Forbidden:

```text
same release key
→ stable channel
→ dev/test channel
```

---

## 18. Security result

The hierarchy is intentionally designed so common single-key compromises remain bounded:

```text
release metadata key compromised
→ cannot replace root
→ cannot authorize recovery downgrade
→ cannot produce valid SplitOS Authenticode publisher signature

code-signing key compromised
→ cannot add artifact to trusted release repository alone

Timestamp key compromised
→ cannot create trusted target metadata
```

Only compromise of sufficient root threshold represents loss of the repository trust anchor itself.
