# Update Transaction and Version Switch

## 1. Purpose

This document specifies the durable transaction used to move an installed machine from one SplitOS release to another.

---

## 2. Canonical identities

SplitOS distinguishes:

```text
SourceRelease
TargetRelease
CommittedInstalledRelease
StagedRelease
ActiveProcessVersion
```

These values may temporarily differ during update.

Critical rule:

```text
some target binaries running
!= target release committed
```

---

## 3. UpdateTransactionRecord

Machine canonical/durable record:

```text
updateTransactionId
sourceReleaseId
targetReleaseId
releaseEnvelopeDigest
state
createdAtUtc
lastCheckpointAtUtc
mutationFenceToken
windowsSnapshotId
recoveryCapsuleId
stagingRoot
activationCheckpoint
rebootExpected
commitDurable
failureCode
recoveryId
```

The transaction MUST be persisted before any machine-visible mutation.

---

## 4. States

```text
DISCOVERED
ELIGIBILITY_CHECK
DOWNLOADING
VERIFYING_ARTIFACTS
STAGING
PREPARING_RECOVERY
READY_TO_APPLY
APPLYING
AWAITING_REBOOT
RESUMING
VERIFYING_TARGET
COMMITTING
COMPLETED
ROLLING_BACK
RECOVERING
FAILED_WITH_SAFE_FALLBACK
```

`ROLLING_BACK` and `RECOVERING` are mechanisms/path states, not evidence that rollback/recovery succeeded.

---

## 5. Prepare phase

Preparation is non-authoritative for installed release identity.

```text
verify release envelope
→ download artifacts
→ verify artifacts
→ stage versioned target release
→ validate migration contract
→ validate recovery reserve
→ create previous-release capsule
→ verify capsule
→ READY_TO_APPLY
```

If preparation fails, the current release remains untouched and canonical.

---

## 6. Versioned release roots

Target payload SHOULD be staged under:

```text
C:\Program Files\SplitOS\Releases\<releaseId>\
```

The source release remains available until the target is verified or rollback has completed.

Release roots are immutable after verification except for explicitly declared mutable data directories which must be outside the release root.

User data and machine databases do not live inside a versioned release root.

---

## 7. Apply authority

`Runtime Host / Update Orchestration` owns the semantic update.

Privileged machine activation uses:

```text
Maintenance.Update.ApplyVerified
```

inside the Privileged Broker.

The Broker validates:

```text
transaction identity
fencing token
target release identity
verified staging state
verified recovery capsule
release-owned activation plan
```

It then invokes the fixed one-shot Update Bootstrap implementation.

---

## 8. One-shot bootstrap

The Update Bootstrap exists because the Broker may need to update/repoint itself.

Baseline flow:

```text
Broker
→ launch trusted bootstrap under privileged maintenance context
→ Broker exits/stops when checkpoint says safe
→ bootstrap owns update checkpoint
→ quiesce Runtime/Manager/Launcher
→ update service/task activation metadata
→ run typed machine migrations
→ start target Broker
→ start/allow target Runtime
→ persist activation evidence
```

The bootstrap cannot accept arbitrary command lines or arbitrary executable paths from Runtime Host.

---

## 9. Activation metadata

The exact Windows implementation may use service/task registrations and release-owned path metadata.

Regardless of mechanism, activation data is separated from canonical commit.

Example transitional state:

```text
SCM Broker ImagePath → target release
Runtime logon action → target release
CommittedInstalledRelease → source release
UpdateTransaction → VERIFYING_TARGET
```

This is valid only inside the durable transaction window.

---

## 10. Health verification

Target verification includes at least:

```text
Broker service starts
Broker IPC health succeeds
Runtime Host starts for eligible session
Runtime ↔ Broker protocol compatible
machine DB schema is readable/valid
required target artifacts match expected release
release-owned policy/catalog versions load
Installed Windows base remains compatible
no unresolved required migration
```

Additional release-specific predicates MAY exist.

---

## 11. Commit

Only Update Orchestration may request final installed-release commit.

Atomic semantic commit must persist:

```text
InstalledSplitOSRelease = targetReleaseId
UpdateTransaction.commitDurable = true
```

or equivalent machine-store transaction.

After durable commit:

```text
target release = canonical
```

A crash before the later cosmetic `COMPLETED` marker does not revert canonical identity.

---

## 12. Failure before commit

If target activation/verification fails before commit:

```text
source release remains canonical
↓
ROLLING_BACK
↓
restore source activation metadata
↓
restore required machine state
↓
start source Broker/Runtime
↓
verify
```

Successful rollback ends with an explicit failed-update result; it does not create a canonical update state named `ROLLED_BACK`.

---

## 13. Failure after commit

If target commit is already durable and a later problem appears:

```text
target is canonical
```

Recovery may still choose the previous capsule as a recovery target, but that is a new Recovery operation, not pretending the original update never committed.

---

## 14. Reboot-required update

Before reboot:

```text
UpdateTransaction = AWAITING_REBOOT
rebootExpected = true
required activation checkpoint durable
```

After boot:

```text
startup recovery coordinator
→ load transaction
→ verify Windows base
→ verify active release/processes
→ RESUMING
→ VERIFYING_TARGET
```

`reboot occurred` is not completion evidence.

---

## 15. Stale owner protection

Every privileged update mutation carries the SPEC-05 fencing token.

If an old Runtime/Bootstrap resumes with a stale token after Recovery or another valid owner took the machine mutation lease:

```text
STALE_MUTATION_OWNER
→ reject
```

No old updater may continue changing the machine after ownership transfer.

---

## 16. Cleanup

Cleanup occurs only after target commit and recovery invariants remain satisfied.

Safe cleanup may remove:

- temporary download files;
- superseded staging data;
- source live release root after capsule verification and target stabilization rules allow it.

Cleanup MUST NOT delete the mandatory previous-release capsule.

---

## 17. Disk-space rule

Required disk calculation includes:

```text
target staging
+
current live release
+
mandatory previous-release capsule
+
transaction scratch
+
safety margin
```

If the recovery store cannot hold a verified previous capsule:

```text
RECOVERY_RESERVE_INSUFFICIENT
```

v1 automatic activation is blocked.
