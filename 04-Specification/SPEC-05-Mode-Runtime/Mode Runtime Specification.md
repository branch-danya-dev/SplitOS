# SPEC-05 — Mode Runtime Specification

## 1. Purpose

This specification defines the implementable v1 runtime contract for SplitOS managed operational modes.

It refines, but does not replace, the canonical A&D semantics from:

- `03-States/System State Model.md`;
- `03-States/Mode Transition Model.md`;
- `04-Behavior/Startup Behavior.md`;
- `04-Behavior/First Run and Runtime Access Behavior.md`;
- `04-Behavior/Work to Game Behavior.md`;
- `04-Behavior/Game to Work Behavior.md`;
- `08-Flows/Work to Game Flow.md`;
- `08-Flows/Game to Work Flow.md`;
- `09-Failures/*`;
- `10-Trust/*`;
- SPEC-01..04.

Core invariant:

```text
FREE
→ ManagedRuntime = DISABLED
→ OperationalMode = NONE

PRO normal operational state
→ ManagedRuntime = ENABLED
→ WORK xor GAME
```

`NONE` is also a valid transient managed-runtime startup/deactivation state before a mode is committed.

---

## 2. Physical ownership

Runtime modules inside `SplitOS.RuntimeHost.exe` remain separate semantic owners:

```text
RuntimeAccess
ModeState
ModeTransition
ModePolicy
ApplicationLifecycle
SystemContext modules
Recovery / Update coordination
```

Physical co-location in Runtime Host MUST NOT collapse ownership.

### 2.1 Canonical writers

| Meaning | Authorizing semantic owner | Physical persistence path |
|---|---|---|
| managed runtime access | RuntimeAccess / entitlement interpretation | user/runtime state + backend evidence |
| committed operational mode | ModeState | Broker-mediated `machine.db` |
| mode operation lifecycle | ModeTransition | Broker-mediated `machine.db` |
| effective mode policy | ModePolicy | runtime-derived from trusted release policy + allowed user settings |
| blockers / decisions | ModeTransition + provider owner | Broker-mediated transition journal |
| actual Windows/device state | external Windows/driver/device evidence | not canonical mode truth |

Storage and Broker are execution/persistence boundaries, not semantic owners.

---

## 3. Operational modes and mode operations

User-visible operational modes remain:

```text
WORK
GAME
```

`NONE` means that no managed Work/Game operational mode is currently committed.

To make startup, normal switching and entitlement-loss convergence use one durable execution model, v1 defines three **mode operation kinds**:

```text
ACTIVATE
SWITCH
DEACTIVATE
```

Allowed transitions:

```text
ACTIVATE
NONE → WORK
NONE → GAME

SWITCH
WORK → GAME
GAME → WORK

DEACTIVATE
WORK → NONE
GAME → NONE
```

Forbidden:

```text
NONE → NONE
WORK → WORK
GAME → GAME
WORK → GAME as ACTIVATE
GAME → NONE as SWITCH
```

A same-target request against an already committed mode is a semantic `NO_OP`, not a new transition row.

### 3.1 Why DEACTIVATE exists

`DEACTIVATE` is required for controlled convergence when managed runtime is no longer authorized or when a fresh Windows control session must return to base Windows state before new mode selection.

It does **not** introduce a third user mode.

---

## 4. Canonical transition lifecycle

All three mode operation kinds reuse the canonical lifecycle:

```text
REQUESTED
INSPECTING
BLOCKED
AWAITING_USER
RESOLVING
APPLYING
VERIFYING
COMMITTING
ROLLING_BACK
COMPLETED
CANCELLED
FAILED_WITH_SAFE_FALLBACK
```

`IDLE` is represented by absence of an active transition rather than an active transition row in `IDLE`.

Terminal results:

```text
COMPLETED
CANCELLED
FAILED_WITH_SAFE_FALLBACK
```

`ROLLED_BACK` MUST NOT be introduced as a competing terminal state. Rollback is a mechanism expressed by `ROLLING_BACK`; the terminal result remains `CANCELLED` or `FAILED_WITH_SAFE_FALLBACK`.

---

## 5. Core mode operation contract

Conceptual API exposed to Runtime consumers:

```text
RequestModeOperation(request)
GetModeRuntimeSnapshot()
GetActiveModeOperation()
SubmitBlockerDecision(...)
CancelModeOperation(...)
```

Request shape:

```text
operationKind
sourceMode
requestedTargetMode
initiatorType
initiatorId
correlationId
originalIntentRef?    // e.g. direct game launch intent
```

The caller cannot directly select lifecycle state or committed mode.

---

## 6. Preconditions

Before a mode operation is accepted, Runtime MUST validate:

```text
physical-console control ownership
compatible Runtime/Broker/schema versions
no Recovery-owned blocking state
no conflicting Update transaction
no conflicting active mode operation
sourceMode == current committed mode
runtime access allows requested operation
requested operationKind/source/target tuple is legal
```

Additional target-specific checks happen during `INSPECTING`.

### 6.1 Access rules

`ACTIVATE WORK/GAME` and `SWITCH WORK↔GAME` require effective authorization for:

```text
runtime.managed_modes
```

`DEACTIVATE ...→NONE` MUST remain permitted even when PRO authorization has been lost, because it is the safe convergence path back to base Windows.

---

## 7. Request collision semantics

Only one active machine mode operation is permitted.

When another request arrives:

```text
same operation identity
→ return existing operation

same target but different request
→ ALREADY_IN_PROGRESS

conflicting target
→ BUSY_MODE_OPERATION

Update/Recovery owns major mutation lease
→ BUSY_MAJOR_MUTATION or DEFERRED
```

v1 MUST NOT maintain an implicit queue of future mode intents.

A caller may retry after observing a terminal result.

---

## 8. Commit boundary

Target mode is canonical only after all mandatory target conditions have been verified and the atomic machine persistence boundary succeeds.

Required final commit operation:

```text
CommitTransitionAndMode(...)
```

implemented as one `machine.db` SQLite transaction through SPEC-03/SPEC-02 typed persistence.

The transaction MUST atomically:

1. update `operational_mode_state.committed_mode` to target;
2. increment its revision;
3. bind the commit to `operationId`, `correlationId`, and current control session identity;
4. set transition `commit_durable = 1`;
5. preserve enough transition state to distinguish a durable commit from later UI/finalization work.

Recommended durable sequence:

```text
VERIFYING passed
→ persist transition = COMMITTING
→ atomic CommitTransitionAndMode
→ target mode canonical
→ post-commit UX/finalization
→ transition = COMPLETED
```

Therefore:

```text
transitionState = COMMITTING
+ commit_durable = 1
```

means the target mode is already canonical even if Runtime crashed before writing `COMPLETED`.

---

## 9. Source-mode rule

Until the atomic commit succeeds:

```text
committedMode = sourceMode
```

This remains true even if some target actions have already been applied successfully.

A partially changed Windows environment is actual-state evidence requiring rollback/reconciliation; it MUST NOT create a new operational mode.

---

## 10. Mode policy targets

Mode Runtime executes toward a resolved target policy:

```text
BASE
WORK
GAME
```

Mapping:

```text
ACTIVATE NONE→WORK   → WORK policy
ACTIVATE NONE→GAME   → GAME policy
SWITCH WORK→GAME     → GAME policy
SWITCH GAME→WORK     → WORK policy
DEACTIVATE *→NONE    → BASE policy
```

`BASE` is a neutral SplitOS-managed policy target, not a user-selectable operational mode.

BASE removes or neutralizes SplitOS mode-specific deltas sufficiently to return to normal Windows desktop behavior.

Exact Windows mechanisms are supplied by SPEC-06 and later integration specs.

---

## 11. Target policy snapshot

Before entering `APPLYING`, the operation MUST persist a target-policy identity sufficient for crash-safe reconstruction:

```text
policyId
policyVersion
policyDigest
resolvedTargetMode
resolutionTimestamp
```

An Update operation cannot replace the active release policy while a Mode operation holds the major mutation lease.

The transition engine MUST NOT silently re-resolve against a different release policy mid-operation.

---

## 12. Execution phases

Canonical implementation phases:

```text
1 ACCEPT
2 INSPECT
3 RESOLVE
4 BUILD_ACTION_PLAN
5 APPLY
6 VERIFY
7 COMMIT
8 FINALIZE
```

Lifecycle mapping:

```text
ACCEPT            → REQUESTED
INSPECT           → INSPECTING / BLOCKED / AWAITING_USER
RESOLVE           → RESOLVING
BUILD_ACTION_PLAN → RESOLVING
APPLY             → APPLYING
VERIFY            → VERIFYING
COMMIT            → COMMITTING
FINALIZE          → COMPLETED
```

Rollback uses `ROLLING_BACK` regardless of the phase that caused it after mutations began.

---

## 13. Action plan

Before first target mutation, Runtime MUST create a deterministic ordered action plan.

Each action contains at least:

```text
actionId
sequence
owningModule
actionType
targetRef
desiredStateVersion
desiredStatePayload / digest
mandatory: true|false
rollbackClass
verificationClass
status
```

Allowed rollback classes:

```text
REVERSIBLE
RECONCILABLE
NON_REVERSIBLE
```

`NON_REVERSIBLE` mode actions require explicit design review and SHOULD be absent from ordinary Work/Game switching.

Raw command lines, raw registry paths or arbitrary service names MUST NOT be generated as mode actions.

Machine mutations reference SPEC-02 allowlisted target IDs/capabilities.

---

## 14. Verification model

Every mandatory action belongs to one verification class:

```text
READ_BACK_REQUIRED
OWNER_SEMANTIC_CHECK
PROCESS_OR_SESSION_EVIDENCE
UX_READINESS_HANDSHAKE
NO_SEPARATE_CHECK
```

`NO_SEPARATE_CHECK` may be used only for non-critical technical actions whose completion result is itself sufficient and documented.

A target mode may commit only if all mandatory target invariants pass.

Examples:

```text
GAME target
→ required display context resolved and verified
→ required input usable
→ required mode-managed machine policy verified
→ Game Launcher readiness handshake available

WORK target
→ Work display/input usable
→ required Work managed components restored
→ Windows desktop/work context usable

BASE target
→ SplitOS mode-specific deltas neutralized sufficiently for ordinary Windows use
```

Detailed device-specific criteria belong to SPEC-06.

---

## 15. Game Launcher readiness

For a transition whose target is `GAME`, Game Launcher readiness is a mandatory semantic precondition for normal commit.

Implementation baseline:

1. Runtime starts or attaches to `SplitOS.GameLauncher.exe` before target commit.
2. Launcher establishes Runtime IPC.
3. Launcher reports `READY_FOR_GAME_MODE_PRESENTATION` for the current operation/correlation.
4. Runtime may then include launcher readiness in target verification.
5. After target commit, Runtime instructs Launcher to become the primary Game Mode presentation.

Process existence alone is insufficient.

---

## 16. Work/Game blocker behavior

`INSPECTING` aggregates blocker evidence from registered providers.

The transition engine does not infer application-internal facts it cannot prove.

In particular:

```text
process exists
!= document has unsaved changes
```

Unsaved-document knowledge requires an app-specific integration/provider or explicit user declaration.

Generic process rules may still classify known applications as confirmation-required based on policy.

Detailed provider contract is defined in `Blocker and Decision Engine.md`.

---

## 17. Cancellation

Cancellation before the first mutation:

```text
→ CANCELLED
→ source mode remains canonical
→ no rollback required
```

Cancellation after any target mutation:

```text
→ ROLLING_BACK
→ reconcile/restore source policy
→ verify result
→ CANCELLED if normal source operation restored
→ FAILED_WITH_SAFE_FALLBACK if only a degraded safe state is proven
```

After target commit:

```text
user cancel
!= undo committed operation magically
```

A new opposite Mode operation is required.

---

## 18. Entitlement loss convergence

When `runtime.managed_modes` is no longer authorized:

### 18.1 No active mode operation

If committed mode is `WORK` or `GAME`, Runtime creates:

```text
DEACTIVATE currentMode → NONE
```

using BASE target policy.

### 18.2 Active operation before target commit

If authorization disappears before a premium target commit:

```text
new premium commit prohibited
→ cancel/rollback operation
→ converge through DEACTIVATE if source remains managed mode
```

### 18.3 Active game

Entitlement loss MUST NOT silently hard-kill the running game solely because subscription state changed.

Runtime enters restricted convergence behavior:

- deny new premium mode/game operations;
- notify the user;
- preserve current application safety;
- after game exit or explicit user-approved close, execute `DEACTIVATE GAME→NONE`;
- emergency security/recovery policy may override only under separately defined conditions.

Final stable result:

```text
ManagedRuntime = DISABLED
OperationalMode = NONE
Windows Desktop usable
```

---

## 19. Fresh Windows control-session startup

v1 does **not** automatically restore the previous session's Work/Game selection.

This closes the earlier remember-last-mode OPEN for v1:

```text
new physical-console Windows logon/control session
→ previous committed mode is recovery evidence only
→ reconcile incomplete operations
→ establish BASE / NONE
→ resolve PRO access
→ if PRO: mode selection
→ ACTIVATE NONE→WORK or NONE→GAME
```

A future remember-last-mode feature requires a new explicit decision and user preference; it is not implicit in persisted `committed_mode`.

### 19.1 Runtime Host restart within the same Windows logon

This is different from a fresh Windows control session.

If Runtime Host restarts while the same OS-derived control-session identity remains active:

```text
preserve committed WORK/GAME
→ reconcile persisted operation + actual Windows state
→ restore Runtime orchestration around the existing mode
```

The Host MUST NOT force mode selection merely because its process restarted.

---

## 20. Runtime control-session identity

Runtime derives a control-session identity from OS-owned facts, at minimum:

```text
Windows SessionId
Windows User SID
logon authentication identity / equivalent OS-derived logon instance
```

The value persisted with mode state is an opaque `controlSessionKey`/digest generated from trusted OS observations.

Payload-supplied identity is not authority.

A changed control-session identity means a fresh managed-runtime activation epoch.

---

## 21. Crash / reboot interpretation

At Runtime startup, persisted state is interpreted in this order:

```text
1 machine schema readable?
2 Recovery active?
3 Update incomplete?
4 Mode operation incomplete?
5 commit_durable?
6 current control-session identity same or new?
7 current runtime access FREE/PRO?
8 actual Windows/device state reconciliation
```

Rules:

```text
incomplete transition + commit_durable=0
→ source remains canonical
→ rollback/reconcile source or BASE

incomplete transition + commit_durable=1
→ target is canonical
→ verify/reconcile target
→ finish terminal record or enter Recovery

fresh Windows control session
→ previous WORK/GAME not automatically reactivated
→ converge BASE/NONE before new mode selection
```

Detailed algorithm is in `Transition Execution and Reconciliation.md`.

---

## 22. Failure escalation

Mode Runtime may produce:

```text
CANCELLED
FAILED_WITH_SAFE_FALLBACK
RECOVERY_REQUIRED
```

It MUST escalate to Recovery when:

- rollback cannot establish a verified coherent source/BASE state;
- durable state is contradictory/corrupt;
- mandatory machine mutation result is unknown and cannot be reconciled;
- actual state cannot be brought to a known safe target through bounded mode actions;
- machine-state persistence required for safe continuation is unavailable.

Recovery remains the semantic owner of recovery strategy selection.

---

## 23. Observability

Every operation MUST carry:

```text
correlationId
operationId
transitionId
sourceMode
targetMode
operationKind
policyId/version/digest
controlSessionKey reference
```

Every blocker/action/verification record MUST be correlatable to the transition.

Logs MUST NOT contain account tokens, arbitrary document contents, or sensitive app payloads.

---

## 24. Acceptance criteria

SPEC-05 is conformant only if:

- `WORK xor GAME` remains true for normal enabled managed-runtime operation;
- FREE/base convergence ends at `NONE` without making Windows unusable;
- one durable pipeline supports activation, switching and deactivation;
- only one active mode operation exists;
- major mutation exclusivity is enforced;
- source mode remains canonical before commit;
- target commit is atomic with transition durability;
- `COMMITTING + commit_durable=1` survives Runtime crash as target canonical truth;
- new Windows logon does not silently reactivate previous mode in v1;
- same-session Runtime restart preserves committed mode and reconciles;
- blockers never claim semantic knowledge unsupported by evidence;
- cancellation after mutation performs verified rollback;
- entitlement loss uses safe DEACTIVATE convergence;
- technical Broker success never directly commits mode.
