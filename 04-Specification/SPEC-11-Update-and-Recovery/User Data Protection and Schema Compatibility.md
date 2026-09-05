# User Data Protection and Schema Compatibility

## 1. Purpose

This document prevents a software rollback from becoming a rollback of the user's canonical data.

---

## 2. Core rule

```text
SplitOS release rollback
!= user-data restore point
```

The user may change profiles, preferences or other canonical user data while the new release is active. Returning software from `N+1` to `N` must not silently erase those later user changes.

---

## 3. Data categories

### User canonical data

Examples:

```text
GameProfile
field-level UserOverride
Shared App assignment/preferences
user UI/preferences
Windows-user ↔ SplitOS-account association metadata
```

Location remains the live user store defined by SPEC-03.

### Machine canonical/transaction data

Examples:

```text
InstalledSplitOSRelease
UpdateTransaction
RecoveryContext
machine mutation lease
machine operational baseline state
```

May use transaction/capsule snapshots when recovery semantics require it.

### Projection/cache data

May be discarded and rebuilt.

### Secrets

Reusable account credentials remain protected by SPEC-04 secret-store rules and are not restored from an old recovery capsule by default.

---

## 4. Rollback compatibility window

Every production release MUST declare whether the immediately previous release can operate safely against the post-update canonical user data.

Conceptual release metadata:

```text
userDataSchemaVersion
rollbackReadableBy[]
secretFormatVersion
rollbackSecretReadableBy[]
migrationClass
```

v1 mandatory invariant:

```text
Target N+1
→ previous mandatory rollback release N
→ N can preserve/read current canonical user data
```

or a tested rollback bridge MUST exist.

---

## 5. Migration classes

### BACKWARD_COMPATIBLE

Preferred v1 path.

Examples:

```text
add nullable field
add new table ignored by old release
add enum value with old-reader fallback
create derived/index data
```

### ROLLBACK_BRIDGE_REQUIRED

Target migration is not directly readable by previous release, but a deterministic bridge can present/preserve data without loss.

The bridge must be created and tested before update activation.

### DESTRUCTIVE_INCOMPATIBLE

Examples:

```text
dropping user fields required by old release
one-way rewrite losing old semantic values
changing IDs without reverse mapping
```

A release requiring this class MUST NOT activate while the previous release is the mandatory rollback target unless product policy introduces a separately verified migration/recovery design.

v1 default: reject activation.

---

## 6. User DB handling

The update does not copy an old `user.db` into the recovery capsule as the rollback authority.

Before schema migration:

```text
validate schema contract
→ validate rollback readability/bridge
→ durable migration journal
→ apply migration
→ verify canonical row counts/invariants
```

If target later rolls back:

```text
live user.db remains
→ previous release opens using declared compatibility contract
```

Unknown/new data must be preserved even if the older release cannot expose it in UI.

---

## 7. Example

R10 adds:

```text
GameProfile.performanceIntent = QUALITY_PRIORITY
```

If R9 does not know this field, R9 may:

```text
ignore the field for runtime behavior
preserve the stored value unchanged
avoid rewriting the whole profile row destructively
```

When R10 is later reinstalled, the value still exists.

---

## 8. User changes after update

Example:

```text
R10 active
user creates TV Profile
user changes RT lock
R10 fails later
rollback to R9
```

Recovery MUST NOT restore an R9-era user database snapshot that deletes those changes.

R9 either:

- preserves and understands the data via backward-compatible schema;
- preserves unsupported fields while exposing a degraded UI/runtime interpretation;
- uses a tested rollback bridge.

---

## 9. Machine DB difference

Machine operational data is different.

A capsule MAY restore a compatible machine-state snapshot because values such as:

```text
active release mapping
update journal
recovery transaction state
release-owned machine policy version
```

belong to system convergence, not personal history.

Even then, the recovery transaction must reconcile fresh actual Windows state after restore.

---

## 10. Secrets

Secret format evolution follows the same previous-release safety requirement.

Preferred pattern:

```text
new release can read old secret format
new writes remain readable by previous rollback release
```

If format rotation is needed, use dual-slot/versioned secret material until the rollback window closes.

An old recovery capsule MUST NOT contain stale copies of access/refresh tokens simply to make rollback easy.

---

## 11. Personal Windows data

SplitOS Update/Recovery MUST NOT modify or restore ordinary personal file trees as part of release rollback.

No normal SplitOS rollback performs:

```text
format C:
restore old Users directory
replace Documents/Desktop
restore old browser/app profiles
```

If Windows itself requires destructive reset/reinstall, that is a separate Windows recovery decision and must be explicitly disclosed to the user.

---

## 12. Data-safety verification

Before release acceptance, SPEC-14 must verify at minimum:

```text
N → N+1 migration
user changes under N+1
forced rollback N+1 → N
no canonical user rows lost
unsupported/new fields preserved
secrets remain usable or require explicit re-auth without data corruption
re-update N → N+1 recovers preserved new-version fields
```

This is a release gate, not an optional QA scenario.
