# Reboot Resume and Crash Recovery

## 1. Purpose

This document specifies how SplitOS decides what is true after process crash, power loss or reboot during update/recovery.

---

## 2. Durable evidence first

Startup never infers update success from the fact that Windows booted.

```text
Windows booted
!= SplitOS update completed
```

The coordinator first reads durable transaction state.

---

## 3. Startup decision

```text
UpdateTransaction exists?
├── NO  → normal installed-release verification
└── YES
    ↓
    read commitDurable + checkpoint + mutation owner
```

Decision:

```text
commitDurable = false
→ source release remains canonical
→ verify actual activation
→ resume pre-commit work or rollback

commitDurable = true
→ target release is canonical
→ verify target activation
→ repair/recover target if possible

commit state unreadable/ambiguous
→ Recovery
→ never guess
```

---

## 4. Expected reboot

Before requesting reboot, SplitOS stores:

```text
expectedReboot = true
resumePhase
sourceReleaseId
targetReleaseId
recoveryCapsuleId
activationCheckpoint
fence token / recovery handoff metadata
```

The reboot itself performs no semantic commit.

---

## 5. Unexpected reboot / power loss

If power is lost in `STAGING`:

```text
source release untouched
→ discard/reverify staging
```

If power is lost in `PREPARING_RECOVERY`:

```text
unsealed capsule cannot satisfy rollback gate
→ source remains canonical
→ recreate/verify capsule
```

If power is lost in `APPLYING`:

```text
load checkpoint
→ inspect actual service/task/release roots
→ inspect capsule
→ either resume a proven idempotent activation step
   or rollback source
```

If power is lost in `VERIFYING_TARGET` before commit:

```text
source remains canonical
→ target may be running as provisional actual state
→ verify again
→ commit only if all predicates pass
→ otherwise rollback
```

---

## 6. Update Bootstrap crash

Bootstrap checkpoints every machine-visible activation stage.

A new Bootstrap instance MUST NOT simply restart the full apply plan from step 1.

Each action declares:

```text
idempotency behavior
precondition
postcondition
rollback action
```

Restart flow:

```text
checkpoint says service target switched
↓
fresh SCM read-back
↓
if already correct → continue
if source still active → apply step
if neither coherent → Recovery
```

---

## 7. Runtime Host crash

Runtime Host crash does not transfer update authority to UI.

When Runtime restarts:

```text
read machine UpdateTransaction
→ read mutation lease/fence
→ connect Broker
→ reconcile
```

If a privileged Bootstrap currently owns the handoff, Runtime observes the transaction rather than issuing a competing apply.

---

## 8. Broker crash

If Broker dies before Bootstrap handoff:

```text
no handoff checkpoint
→ source remains canonical
→ Runtime can retry/recover after Broker restart
```

If Broker dies after durable Bootstrap handoff:

```text
Bootstrap/recovery checkpoint owns progress
→ new Broker must not start a competing update
```

---

## 9. Repeated target startup failure

The recovery policy may detect repeated target failures such as:

```text
Broker target cannot start
Runtime target crashes before health
required protocol handshake never succeeds
required migration repeatedly fails
```

When policy threshold is met and a valid previous capsule exists:

```text
Recovery Coordination
→ automatic previous-release recovery candidate
```

Exact thresholds, timing and false-positive controls are SPEC-14 release policy and remain OPEN here.

---

## 10. Reboot requested by Windows Update

If Windows triggers/requires reboot while SplitOS is not yet in a safe activation checkpoint:

SplitOS should defer its own apply.

If reboot occurs unexpectedly anyway, startup treats it like any interrupted transaction and refreshes Windows compatibility before continuing.

No checkpoint is trusted across a Windows build change without revalidation.

---

## 11. Cleanup after crash

Transaction scratch is removed only after canonical outcome is known.

Do not delete:

```text
source release
previous capsule
transaction journal
```

while the system still needs them to disambiguate recovery.

---

## 12. Failure result

A recovered update produces explicit history such as:

```text
UPDATE_FAILED_ROLLBACK_SUCCEEDED
UPDATE_FAILED_RECOVERY_SUCCEEDED
UPDATE_FAILED_SAFE_RUNTIME_DISABLED
UPDATE_STATE_AMBIGUOUS_MANUAL_RECOVERY_REQUIRED
```

Diagnostics describe what happened; they do not rewrite canonical installed-release truth.
