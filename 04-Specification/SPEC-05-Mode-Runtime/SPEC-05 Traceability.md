# SPEC-05 — Traceability

## 1. Source mapping

| SPEC-05 decision | Primary source |
|---|---|
| ACTIVATE/SWITCH/DEACTIVATE share one durable mode-operation engine | Runtime Access + Mode Transition + First Run behavior + SPEC-03 physical schema |
| `NONE` is base/no-managed-mode state, not a third user mode | Runtime Access State Model / First Run behavior |
| source mode stays canonical until atomic commit | Mode Transition Model / Work→Game / Game→Work / Failures |
| target commit atomically updates mode + durable transition marker | SPEC-03 repository model + Failure crash semantics |
| `ROLLED_BACK` is not a terminal lifecycle state | canonical Mode Transition Model |
| BASE/WORK/GAME declarative policy targets | Mode Policy responsibility + FREE/PRO product model |
| blocker provider evidence is bounded by what source proves | Ownership + External Evidence Trust |
| active game blocks/asks on GAME→WORK | Game Session State / Game→Work Behavior |
| Game Launcher readiness is required for normal GAME commit | Work→Game Behavior / Synthesis |
| one active mode operation | Mode Transition invariant |
| Mode/Update/Recovery share major mutation exclusivity | Flows / Failures / Synthesis |
| mutation lease uses fencing | SPEC-level closure of crash-safe major mutation coordination OPEN |
| fresh Windows logon does not auto-restore prior mode in v1 | Startup remember-last-mode OPEN closed by SPEC-05 |
| same-logon Runtime Host restart preserves committed mode | Runtime Failure Scenarios / SPEC-01 lifecycle |
| entitlement loss safely converges to NONE | Runtime Access Behavior / Failures / SPEC-04 |
| premium target cannot commit after entitlement loss | Runtime Access + Trust |
| actual state must be read/verified after mutation | Interfaces / Integrations / Flows |
| machine state writes remain Broker-mediated | SPEC-02 / SPEC-03 |

---

## 2. Specification decisions

```text
SPEC-DEC-026
v1 uses one durable Mode Operation pipeline with operation kinds ACTIVATE, SWITCH and DEACTIVATE.

SPEC-DEC-027
DEACTIVATE WORK|GAME→NONE is the normal safe convergence mechanism when managed runtime is disabled; NONE is not a third user-selectable mode.

SPEC-DEC-028
normal mode target commit is one atomic machine persistence boundary that updates OperationalModeState and transition commit durability together.

SPEC-DEC-029
COMMITTING + commit_durable=1 means target mode is already canonical even if Runtime crashes before COMPLETED finalization.

SPEC-DEC-030
v1 mode policy uses release-owned declarative BASE/WORK/GAME targets and forbids arbitrary shell/registry/service commands in policy.

SPEC-DEC-031
mode pre-flight is provider-based; evidence is interpreted only within what each provider can prove. Generic process presence cannot prove unsaved document state.

SPEC-DEC-032
Mode/Update/Recovery share one machine-wide major mutation lease; lease acquisition increments a fencing token used to reject stale mutation owners.

SPEC-DEC-033
v1 does not queue conflicting mode intents. Same-target requests return in-progress/no-op semantics and conflicting active requests are BUSY.

SPEC-DEC-034
fresh physical-console Windows logon starts a new activation epoch: prior WORK/GAME is recovery evidence only, Runtime converges BASE/NONE, then PRO user performs explicit mode selection.

SPEC-DEC-035
Runtime Host restart within the same Windows logon preserves the committed mode and reconciles actual state instead of returning to mode selection.

SPEC-DEC-036
Game Launcher must complete a Runtime readiness handshake before normal GAME commit; process existence is insufficient.

SPEC-DEC-037
cancellation after target mutation requires verified rollback/reconciliation; rollback remains a mechanism and terminal outcome is CANCELLED or FAILED_WITH_SAFE_FALLBACK.

SPEC-DEC-038
entitlement loss before premium target commit blocks that commit; stable WORK/GAME is converged through DEACTIVATE to BASE/NONE without making Windows unusable.
```

---

## 3. Verification backlog

### Core state / operation

```text
V-MODE-001 NONE→WORK ACTIVATE success
V-MODE-002 NONE→GAME ACTIVATE success
V-MODE-003 WORK→GAME SWITCH success
V-MODE-004 GAME→WORK SWITCH success
V-MODE-005 WORK→NONE DEACTIVATE success
V-MODE-006 GAME→NONE DEACTIVATE success
V-MODE-007 same committed target returns NO_OP without new transition
V-MODE-008 only one active mode operation
V-MODE-009 conflicting target while active returns BUSY
V-MODE-010 source mode remains canonical throughout APPLYING/VERIFYING
V-MODE-011 failed mandatory verification prevents target commit
V-MODE-012 atomic mode+transition commit cannot physically diverge
V-MODE-013 COMMITTING+commit_durable target survives Runtime crash
```

### Major mutation coordination

```text
V-MUT-001 Mode cannot acquire lease while Update owns it
V-MUT-002 Mode cannot acquire lease while Recovery owns it
V-MUT-003 fence token increments on new lease owner
V-MUT-004 stale Runtime Host fence rejected
V-MUT-005 lease expiry alone does not skip reconciliation
V-MUT-006 console-session ownership change invalidates old mutation authority
```

### Blockers / decisions

```text
V-BLOCK-001 hard blocker prevents APPLYING
V-BLOCK-002 user-decision blocker persists and resumes same logon
V-BLOCK-003 user cancel before mutation produces CANCELLED without rollback
V-BLOCK-004 user cancel after mutation enters ROLLING_BACK
V-BLOCK-005 generic process provider never reports fake unsaved-document proof
V-BLOCK-006 stale blocker evidence is rechecked
V-BLOCK-007 active game close decision waits for actual exit evidence
V-BLOCK-008 unapproved device fallback cannot silently continue
```

### Policy

```text
V-POL-001 unsigned/incompatible policy rejected
V-POL-002 arbitrary command/raw registry/service target absent from policy schema
V-POL-003 mandatory target failure blocks commit
V-POL-004 preferred target may use only approved fallback
V-POL-005 resolved policy digest immutable after action plan starts
V-POL-006 GAME requires Launcher READY handshake
V-POL-007 BASE does not pretend system is stock Windows
```

### Crash / reconciliation

```text
V-REC-001 Runtime crash before first mutation resumes/cancels safely
V-REC-002 Runtime crash mid-apply rolls back/reconciles source
V-REC-003 Runtime crash after durable commit preserves target
V-REC-004 Broker response loss reconciles via idempotency + actual evidence
V-REC-005 power loss during APPLYING does not assume target
V-REC-006 power loss during COMMITTING uses commit_durable truth
V-REC-007 fresh Windows logon after prior GAME converges BASE/NONE and asks mode selection for PRO
V-REC-008 same-logon RuntimeHost restart does not force mode selection
V-REC-009 rollback failure escalates Recovery
```

### Entitlement

```text
V-ACCESS-001 FREE cannot ACTIVATE WORK/GAME
V-ACCESS-002 lost capability before commit blocks premium target commit
V-ACCESS-003 entitlement loss from WORK converges NONE
V-ACCESS-004 entitlement loss from GAME with active game does not subscription-kill game silently
V-ACCESS-005 final entitlement-loss stable state is NONE + usable Windows desktop
```

---

## 4. OPEN items intentionally deferred

SPEC-05 does not close:

```text
exact Display/Audio/Input/Power Windows APIs and verification → SPEC-06
exact game-client process/launch mechanisms → SPEC-07
Game Profile optimization/override precedence → SPEC-08
Game Launcher detailed UX/navigation → SPEC-09
Update lease/resume implementation detail → SPEC-11
Recovery strategy selection → SPEC-11
exact observability retention → SPEC-13
numeric performance/transition SLA thresholds → SPEC-14 / measured NFR closure
```

---

## 5. Next target

```text
SPEC-06 Windows Context Integrations
```

SPEC-06 must provide typed apply/read/verify implementations that satisfy the mode action/verification contracts without changing ModeState/ModeTransition ownership.
