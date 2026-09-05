# Release Metadata and Repository Trust

## 1. Purpose

This document defines how SplitOS publishes, discovers and authenticates update metadata using TUF role semantics.

The CDN/repository is treated as untrusted transport/storage.

```text
CDN says file exists
!= file is trusted
```

---

## 2. Repository layout

Conceptual production layout:

```text
metadata/
├── N.root.json
├── timestamp.json
├── N.snapshot.json
├── N.targets.json
└── delegated/
    ├── N.splitos-release.json
    ├── N.splitos-knowledge.json
    └── N.splitos-recovery.json

targets/
├── releases/stable/<releaseId>/release-envelope.json
├── releases/stable/<releaseId>/artifacts/...
├── knowledge/<knowledgeId>/...
└── recovery/<authorizationId>/recovery-authorization.json
```

Exact physical CDN paths MAY differ, but logical role/path separation is normative.

---

## 3. Required top-level roles

SplitOS uses the standard TUF role responsibilities:

### Root

Defines trusted keys, thresholds and role assignments.

### Targets

Defines target namespace delegations and top-level target authority.

### Snapshot

Binds metadata versions/hashes into a consistent repository view.

### Timestamp

Provides freshness and snapshot binding with short-lived metadata.

---

## 4. Delegated roles

### `splitos-release`

Allowed namespace:

```text
releases/stable/*
```

Owns release envelopes and release-owned artifact target entries.

### `splitos-knowledge`

Allowed namespace:

```text
knowledge/*
```

Owns signed knowledge/catalog targets.

### `splitos-recovery`

Allowed namespace:

```text
recovery/*
```

Owns exact recovery authorization targets only.

Delegation path matching MUST be enforced by the client library/verifier.

---

## 5. Release Envelope binding

The release envelope is a target object, not a trust anchor.

Delegated targets metadata binds:

```text
release envelope path
size
SHA-256 digest
custom target metadata as needed
```

The envelope then binds all product semantics needed by SPEC-11:

```text
releaseId
releaseSequence
securityEpoch
channel
Windows compatibility constraints
required source release constraints
artifact entries
knowledge set
schema compatibility
reboot behavior
publisher identity requirements
recovery relationship
```

A modified envelope fails target digest verification before its semantic content is trusted.

---

## 6. Artifact target entries

Every artifact required for a release is either directly represented as a TUF target or transitively bound by the authenticated Release Envelope.

For every privileged/executable artifact, the release envelope records at least:

```text
artifactId
relative path
version
size
SHA-256 digest
artifact type
required publisher policy
mandatory/optional
```

Download URL is not security identity.

---

## 7. Consistent snapshot

Production repositories SHOULD enable TUF consistent-snapshot behavior.

Clients MUST avoid mixing metadata/artifacts from incompatible repository generations.

A target URL constructed from attacker-controlled metadata that does not match trusted path/hash/version constraints is rejected.

---

## 8. Client refresh algorithm

The updater uses a maintained TUF implementation/client workflow rather than a hand-written approximation.

Conceptual sequence:

```text
1. load durable trusted root + metadata floors
2. sequentially update root metadata
3. fetch/verify timestamp
4. fetch/verify snapshot
5. fetch/verify top-level targets
6. resolve/verify delegated role metadata
7. fetch authenticated Release Envelope target
8. evaluate SplitOS release policy
9. fetch exact authenticated artifacts
10. verify artifact hashes/publisher signatures
```

Each verification step completes before the next trust state is committed.

---

## 9. Trusted metadata persistence

Previously trusted metadata is security state.

The updater MUST persist enough information to enforce rollback protection across:

- restart;
- reboot;
- update-cache cleanup;
- failed refresh;
- CDN outage.

At minimum:

```text
trusted root version
trusted timestamp metadata version
trusted snapshot metadata version
trusted targets/delegated metadata versions
highest releaseSequence/securityEpoch
trusted time floor
```

Rebuildable target files may be deleted; security floors may not.

---

## 10. Metadata failure outcomes

Normalized outcomes include:

```text
ROOT_SIGNATURE_INVALID
ROOT_VERSION_ROLLBACK
TIMESTAMP_EXPIRED
TIMESTAMP_SIGNATURE_INVALID
SNAPSHOT_ROLLBACK
SNAPSHOT_MISMATCH
TARGETS_SIGNATURE_INVALID
DELEGATION_PATH_VIOLATION
TARGET_HASH_MISMATCH
TARGET_SIZE_MISMATCH
METADATA_EXPIRED
CLOCK_INDETERMINATE
```

Any of these prevents new target activation.

Existing Windows desktop usability remains independent.

---

## 11. Expiration policy

Every TUF metadata role uses explicit expiry.

Exact operational durations are configured by release-security policy and validated in SPEC-14.

Semantic requirements:

```text
Timestamp expiration
< Snapshot expiration
<= Targets/delegation expiration
< Root operational lifetime
```

Timestamp is short-lived enough to detect repository freeze in useful time.

Offline Root expiry must still be renewed before it prevents supported clients from advancing trust.

---

## 12. Clock behavior

TUF expiration needs a usable notion of current time.

SplitOS persists:

```text
trustedTimeFloorUtc
```

After a successful trusted refresh:

```text
trustedTimeFloorUtc = max(oldFloor, effectiveTrustedCurrentTime)
```

If Windows clock is earlier than the trusted floor beyond allowed skew:

```text
CLOCK_INDETERMINATE
```

New update activation is denied until time is reconciled.

The updater MUST NOT set its clock from unsigned repository metadata.

---

## 13. Freeze behavior

If the repository/CDN serves only old but still cryptographically authentic metadata:

- metadata expiry eventually makes freshness failure explicit;
- local previously trusted version floors detect metadata rollback;
- the client does not silently continue treating update discovery as current forever.

UI semantics distinguish:

```text
NO_UPDATE_AVAILABLE
```

from:

```text
UPDATE_METADATA_STALE_OR_UNAVAILABLE
```

---

## 14. Repository mirror/CDN behavior

Multiple mirrors/CDNs MAY serve targets.

They do not need private release keys.

Trust remains:

```text
mirror bytes
→ local TUF/hash verification
→ trusted target
```

A compromised mirror can cause availability/freeze pressure but cannot create a new trusted release without relevant signing authority.

---

## 15. Release policy beyond TUF

TUF authenticates repository metadata and prevents repository-level attacks, but SplitOS adds product policy checks:

```text
release channel allowed?
Windows baseline compatible?
source version edge allowed?
entitlement/update capability allowed?
releaseSequence >= normal floor?
securityEpoch >= required floor?
recovery-only target being misused?
artifact publisher policy satisfied?
```

Therefore:

```text
TUF target authentic
!= SplitOS update applicable
```

---

## 16. Security epoch

`securityEpoch` is a monotonic security-policy generation independent of semantic product version.

Example:

```text
1.8.5  securityEpoch 12
2.0.0  securityEpoch 12
1.8.6  securityEpoch 13   security emergency backport
```

Normal update cannot activate a release below the locally required security epoch.

A recovery authorization can permit only the exact lower product sequence while still satisfying its declared minimum security epoch rules.

---

## 17. Repository bootstrap

The initial production root is embedded in the verified SplitOS baseline/Builder release trust.

There is no supported normal flow:

```text
first network connection
→ download root.json
→ trust it because HTTPS worked
```

Production trust begins from the preinstalled root anchor.

---

## 18. Metadata implementation dependency

The project SHOULD consume a maintained implementation conforming to the TUF specification rather than reimplementing the detailed client workflow from memory.

Any selected library version becomes a security dependency and must be tracked for advisories/updates.

A known-vulnerable TUF client implementation cannot remain pinned merely to preserve binary compatibility.

---

## 19. Repository publication order

Publication is transactional from the repository-consumer perspective.

Conceptually:

```text
upload immutable target artifacts
→ publish delegated targets metadata
→ publish snapshot metadata
→ publish timestamp metadata last
```

Timestamp publication exposes the new repository state to clients.

Partially uploaded releases are not discoverable as trusted complete releases.

---

## 20. Result

Repository security becomes:

```text
preinstalled Root
→ TUF role chain
→ delegated SplitOS release authority
→ exact Release Envelope
→ exact artifact hashes
→ SplitOS applicability policy
→ independent publisher verification
→ staged release
```

The CDN itself never owns release truth.
