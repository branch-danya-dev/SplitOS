# SplitOS — Update and Recovery Flow

## 1. Purpose

Документ связывает Compatibility Management, Update Orchestration, Runtime Host, Privileged Broker и Recovery Coordination в end-to-end update/recovery flow.

Exact package format, Windows update technology и retry policy остаются OPEN; flow фиксирует semantic order и ownership.

---

# 2. Participants

```text
User / SplitOS Manager
SplitOS Runtime Host
Product Identity & Entitlement
Compatibility Management
Update Orchestration
Mode Transition Coordination
Game Session State
SplitOS Privileged Broker
Windows / Servicing Mechanism
Recovery Coordination
Installed Baseline Identity
Observability & Diagnostics
Microsoft Update / Release Evidence
```

---

# 3. Global mutation coordination

Update, recovery and mode transition all can mutate machine/runtime state.

Therefore v1 requires a semantic exclusivity rule:

```text
At most one major state-mutating orchestration owns the machine transition window:

Mode Transition
or
Update
or
Recovery
```

This does not mean one global process mutex is already chosen. It means competing workflows must be coordinated and rejected/deferred rather than interleaved blindly.

Examples:

```text
Update APPLYING
→ new Work→Game transition must not start
```

```text
Recovery active
→ game launch/mode transition blocked
```

---

# 4. FL-05A — Update discovery and eligibility

## Trigger

- manual user check;
- scheduled product check;
- release notification;
- startup reconciliation.

## Sequence

1. Update Orchestration obtains available SplitOS release/update metadata.
2. Product Identity & Entitlement resolves whether the account/install is entitled to the update where entitlement applies.
3. Compatibility Management evaluates the target release against:
   - current installed baseline;
   - supported Windows base;
   - known client/platform compatibility constraints;
   - rollout/compatibility policy.
4. Result is separated into:

```text
ENTITLED?
COMPATIBLE?
AVAILABLE/APPLICABLE?
```

5. Update Orchestration decides whether the update can be offered/applied now.

Critical rule:

```text
Update exists
!= update compatible
!= update entitled
!= update safe to apply now
```

---

# 5. FL-05B — Prepare update

## Preconditions

- no active Recovery;
- no active Mode Transition;
- update compatibility decision permits target;
- required update artifacts are available and validated according to later Trust/Specification rules.

## Sequence

1. Update Orchestration creates `UpdateTransactionId`.
2. Current installed release/baseline identity is recorded as source.
3. Target release is recorded.
4. Runtime inspects whether an active game/workload or current mode context requires user deferral/normalization.
5. User is prompted where policy requires.
6. Required local artifacts are staged.
7. Pre-update recovery data / last-known-safe references are prepared where supported.
8. Update enters prepared state.

No target baseline identity is committed yet.

---

# 6. FL-05C — Apply update

1. Update Orchestration acquires the major mutation window.
2. Normal new mode transitions / managed launches are blocked or deferred.
3. If privileged servicing is required:

```text
Runtime / Update Orchestration
→ secured IPC
→ Privileged Broker
→ Windows/servicing mechanism
```

4. Update mechanism applies target artifacts/actions.
5. Immediate technical execution results are recorded.
6. If reboot is required, update transaction remains durable across reboot.
7. After reboot/runtime return, Update Orchestration resumes from durable transaction state.

---

# 7. FL-05D — Verify target release

After application/reboot:

1. Runtime resolves installed version/baseline evidence.
2. Mandatory runtime components are checked for readiness.
3. Baseline/compatibility health checks are executed according to target release verification policy.
4. Actual state is compared with expected target.
5. Only after mandatory verification passes does Installed Baseline Identity move to target release.

Critical rule:

```text
files copied / installer exited 0
!= update committed
```

---

# 8. Successful update result

```text
source release = old
expected target = new
verification = passed
↓
InstalledBaselineIdentity = new release
UpdateTransaction = COMPLETED
```

Runtime then returns to a safe normal access/mode startup path.

If previous mode restoration is supported, restoration remains subject to runtime-access and normal mode activation verification; it is not automatic truth copied from pre-update state.

---

# 9. Update failure before target commit

If apply or verification fails before target release is committed:

1. Update cannot be marked successful.
2. Update Orchestration requests Recovery Coordination.
3. Recovery reads:
   - source InstalledBaselineIdentity;
   - failed UpdateTransaction;
   - last-known-safe evidence;
   - current observed baseline/runtime state.
4. Recovery selects best safe target.

Preferred priority remains:

```text
User data integrity
→ system bootability
→ known safe baseline/runtime state
→ mode/profile restoration
→ UX restoration
```

---

# 10. FL-05E — Recovery

## Trigger classes

```text
failed update
failed mode transition with no clean rollback
startup detects incomplete durable transaction
critical baseline/runtime mismatch
manual supported recovery request
```

## Sequence

1. Recovery Coordination creates/loads `RecoveryContext` and `RecoveryId`.
2. Normal mode/game/update mutation commands are blocked.
3. Recovery gathers canonical and evidence inputs:
   - committed operational mode if trustworthy;
   - incomplete transition/update records;
   - Installed Baseline Identity;
   - actual Windows/runtime evidence;
   - last-known-safe references.
4. Recovery chooses a target state.
5. Required privileged/local recovery operations are executed.
6. Actual system/runtime state is re-read.
7. Recovery verifies target coherence.
8. Only after successful verification is recovery marked complete.
9. Runtime re-enters an appropriate stable access path:

```text
FREE → Windows Desktop / OperationalMode NONE
PRO  → safe managed-runtime startup / mode selection
```

---

# 11. Incomplete transaction after reboot/crash

Startup must distinguish:

```text
previous transaction completed
```

from:

```text
previous transaction started but commit not reached
```

Examples:

### Mode transition

```text
source WORK
transition target GAME
crash before commit
→ GAME must not be assumed canonical
```

### Update

```text
source release R1
target R2
reboot during apply
→ InstalledBaselineIdentity must not be blindly changed to R2
```

Durable records guide reconciliation/recovery.

---

# 12. User-visible outcomes

Update/recovery surfaces should produce semantic outcomes such as:

```text
UPDATE_AVAILABLE
UPDATE_NOT_ENTITLED
UPDATE_INCOMPATIBLE
UPDATE_DEFERRED
UPDATE_COMPLETED
UPDATE_FAILED_RECOVERY_STARTED
RECOVERY_COMPLETED
RECOVERY_REQUIRES_USER_ACTION
SAFE_FALLBACK_ACTIVE
```

Exact UI wording is deferred.

---

# 13. Invariants

### FL-UR-001

Compatibility decision and update execution remain separate owners.

### FL-UR-002

Update target release is not canonical until verification/commit succeeds.

### FL-UR-003

Mode transition, update and recovery must not mutate the same machine state concurrently without coordination.

### FL-UR-004

Recovery prioritizes user data and bootability over cosmetic profile restoration.

### FL-UR-005

Failure/recovery must not make FREE/expired entitlement equivalent to unbootable Windows.

---

# 14. OPEN items

This flow intentionally does not yet decide:

- exact update package/signature format;
- exact Windows patch integration mechanism;
- exact reboot coordination implementation;
- automatic retry counts/timeouts;
- exact last-known-good snapshot mechanism;
- exact rollback mechanism for every component class;
- whether every update is in-place or some require a rebuilt baseline/clean-install path.

These remain explicit inputs to Failures/Trust/Specification.

---

# 15. Sequence summary

```text
Discover target
→ entitlement check
→ compatibility decision
→ prepare transaction
→ acquire mutation window
→ apply
→ reboot/resume if needed
→ read actual target state
→ verify
→ commit target release

failure before commit
→ Recovery Coordination
→ select safe target
→ recover
→ verify
→ stable FREE/PRO startup path
```
