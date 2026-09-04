# SplitOS — Runtime Failure Scenarios

## 1. Purpose

Документ применяет каноническую Failure Model к interactive runtime SplitOS:

```text
Account / Entitlement
Runtime Host / Privileged Broker
Mode Transition
Display / Audio / Input / Power
Game Client adapters
Managed Game Launch
Game Session
```

Каждый сценарий описывает:

```text
Trigger / evidence
Canonical state before failure
Expected handling owner
Response
Safe result
Escalation condition
```

---

# 2. Account and runtime access failures

## RF-01 — Account backend unavailable at Windows sign-in

### Preconditions

```text
Windows sign-in succeeded
SplitOS Account association exists
```

### Failure

```text
Account Backend unavailable
```

### Owner

Product Identity & Entitlement.

### Response

1. Do not affect Windows authentication/session.
2. Evaluate cached/offline entitlement according to offline policy.
3. If cached PRO access is still valid, continue within allowed degraded/offline policy.
4. If premium access cannot be safely established, disable premium runtime access without blocking Windows desktop.

### Safe result

At minimum:

```text
Windows Desktop usable
```

### Important

```text
backend unavailable
!= Windows login failure
```

---

## RF-02 — Entitlement expires while managed runtime is active

### Situation

A PRO user is currently in `WORK` or `GAME`, but effective entitlement no longer permits managed runtime.

### Rule

Entitlement change must not asynchronously destroy active user work/game state.

### Response direction

1. Mark access reconciliation required.
2. Do not start new premium-only operations that policy forbids.
3. If an active game/transition exists, finish or safely resolve it according to downgrade policy.
4. Transition toward base Windows experience at a safe boundary.

### Stable target

```text
ManagedRuntime = DISABLED
OperationalMode = NONE
Windows Desktop usable
```

Exact grace timing remains policy-level OPEN until Trust/Specification.

---

# 3. Runtime Host / Privileged Broker failures

## RF-10 — Runtime Host crash in stable WORK

### Before failure

```text
CommittedMode = WORK
Transition = IDLE
```

### Failure

Interactive Runtime Host terminates unexpectedly.

### Interpretation

Runtime Host process presence is not canonical operational mode truth.

### Response

1. Windows session remains alive.
2. On restart, Runtime Host reloads durable canonical state.
3. Re-read relevant actual Windows/device evidence.
4. Reconcile whether current actual policy still satisfies committed WORK.
5. If coherent, resume WORK.
6. If not coherent and normal reconciliation cannot prove safety, escalate to Recovery.

### Forbidden

```text
Runtime Host crashed
→ CommittedMode = NONE
```

without canonical reconciliation.

---

## RF-11 — Runtime Host crash during Work→Game before commit

### Before failure

```text
CommittedMode = WORK
Transition target = GAME
Transition state = APPLYING / VERIFYING
```

### Failure

Runtime Host crashes.

### Required semantics

After restart:

```text
CommittedMode remains WORK
```

unless a durable commit record proves GAME was committed.

### Response

1. Load durable `ModeTransitionRecord`.
2. Read actual Windows/device state.
3. Determine whether source WORK can be safely restored/reconciled.
4. Roll back or recover to coherent WORK where feasible.
5. If source state cannot be verified, Recovery Coordination takes control.

### Rule

Target intent alone is insufficient to resume as GAME.

---

## RF-12 — Privileged Broker unavailable

### Failure

Runtime Host cannot reach the SplitOS Privileged Broker.

### Impact

Operations requiring elevated machine mutation are unavailable.

### Response

- queries and user-session-only operations may continue if safe;
- privileged requests fail explicitly as dependency unavailable;
- major transition/update requiring broker must not commit;
- Windows desktop remains usable.

### Severity

Typically S3 degraded system state.

### Escalation

If broker is required to restore a partially modified transition, Recovery may be required.

---

## RF-13 — Privileged request rejected by local IPC security

### Failure

Broker rejects caller/request due authorization/validation.

### Response

Treat as trust/integrity failure, not as a retry loop.

```text
DENIED
→ do not bypass security boundary
→ log correlated evidence
→ fail owning operation safely
```

Detailed authorization belongs to `10-Trust`.

---

# 4. Work→Game transition failures

## RF-20 — User cancels blocker resolution

### Classification

Controlled outcome, not system failure.

### Result

```text
CommittedMode = WORK
Transition = CANCELLED
Game launch continuation = STOPPED
```

Any preparatory mutation already performed must be reconciled to safe WORK before the flow is considered cleanly closed.

---

## RF-21 — Required Work application cannot be safely resolved

Example:

```text
unsaved application state detected
no safe automation available
user refuses/does not confirm close
```

### Response

Block transition.

```text
WORK remains canonical
```

No forced lossy close is allowed merely to satisfy Game Mode target.

---

## RF-22 — Game display disappears during transition

### Before failure

```text
CommittedMode = WORK
TransitionTarget = GAME
```

### Failure

Preferred Game display is disconnected or becomes unavailable during APPLYING/VERIFYING.

### Response

1. Refresh hardware/display evidence.
2. Re-evaluate target Game policy.
3. If a policy-approved fallback display exists, resolve a new effective target and re-verify.
4. Otherwise abort target transition and rollback to WORK.

### Critical rule

Do not commit GAME against stale display target evidence.

---

## RF-23 — Display operation returns success but target not reached

Example:

```text
Desired: TV 4K@120
SetDisplayConfig: success
Actual: TV 4K@60
```

### Classification

Verification failure.

### Response

If 4K@60 is not an explicit acceptable fallback:

```text
GAME commit prohibited
→ rollback / resolve another supported target
```

If policy says max sustainable supported mode is acceptable, the owner must explicitly resolve that as a new target before commit.

---

## RF-24 — Optional Game policy action fails

Example:

A non-mandatory helper cannot start.

### Response

If policy classifies capability as optional:

```text
record degraded capability
continue transition
```

But completion must expose degraded result where relevant.

Mandatory vs optional must be explicit in policy/verification criteria.

---

## RF-25 — Mandatory mode-managed service fails to change state

### Response

```text
partial application
→ target verification fails
→ GAME commit prohibited
→ rollback WORK or Recovery
```

A single failed mandatory component cannot be hidden by success of display/power operations.

---

## RF-26 — Rollback to WORK partially fails

### Situation

Game transition failed and rollback commands were executed, but actual WORK target cannot be fully verified.

### Result

Do not report:

```text
Transition cancelled successfully
```

### Response

```text
FAILED_WITH_SAFE_FALLBACK
or
RECOVERY_REQUIRED
```

depending on actual state/usability.

Canonical WORK may remain source truth, but managed runtime must not proceed as if WORK policy is fully healthy until reconciliation succeeds.

---

# 5. Game launch failures

## RF-30 — Game Client not installed

### Classification

Dependency unavailable / controlled launch failure.

### Result

```text
CommittedMode = GAME
GameSession returns/stays LAUNCHER
```

User receives actionable client/install information.

No Game→Work transition is implied.

---

## RF-31 — Game Client authentication required

### External result

```text
AUTH_REQUIRED
```

### Classification

Controlled outcome, not SplitOS runtime failure.

### Response

- keep Game Mode active;
- surface client authentication requirement;
- allow retry after client auth;
- do not mark `GAME_RUNNING`.

---

## RF-32 — Game installation projection stale

### Situation

SplitOS projection says game is installed, external client no longer confirms it.

### Rule

External client/platform authority wins.

### Response

1. Mark projection stale/reconciled.
2. Do not attempt to promote stale local data as installation truth.
3. Return launch failure or client install flow as supported.

---

## RF-33 — Client handoff rejected

### Example results

```text
GAME_NOT_AVAILABLE
CLIENT_UNAVAILABLE
UNSUPPORTED
FAILED
```

### Result

```text
GameSession != GAME_RUNNING
CommittedMode = GAME
```

Return to launcher error/retry state.

---

## RF-34 — HANDOFF_ACCEPTED but game never starts

### Situation

Client accepted launch invocation, but no sufficient game-running evidence arrives before bounded timeout/policy condition.

### Classification

Launch verification failure.

### Response

1. Reconcile client/process evidence.
2. If auth/update/dialog evidence exists, surface it.
3. If no game starts, end attempt as failed.
4. Return session to `LAUNCHER` or explicit failed launcher state.

### Rule

```text
HANDOFF_ACCEPTED
!= GAME_RUNNING
```

---

## RF-35 — Game process crashes after GAME_RUNNING

### Interpretation

Game crash is not automatically a SplitOS mode failure.

### Response

1. Detect game exit/crash evidence.
2. End game session attempt.
3. Preserve `CommittedMode = GAME` if Game Mode context remains coherent.
4. Return to Game Launcher with crash/exit result.

Escalate only if the crash also destabilized managed system context.

---

## RF-36 — Game Client crashes during active game

### Response depends on actual game ownership/runtime behavior.

If game remains running independently:

```text
continue observing game session
mark client capability degraded
```

If game terminates with client:

```text
process game exit normally
```

Do not infer game state solely from client process state.

---

# 6. Game→Work failures

## RF-40 — User requests WORK while game running, then cancels

Controlled outcome:

```text
CommittedMode = GAME
GameSession = GAME_RUNNING
Transition = CANCELLED
```

---

## RF-41 — User confirms game close but game does not exit

### Response

Do not continue Work transition until policy defines a safe resolved game state.

Possible outcomes:

```text
retry graceful close
allow user to cancel transition
explicit force-close option if product policy permits
```

Force-close semantics must never be implicit.

---

## RF-42 — Work display/context cannot be restored

### Situation

Game→Work policy application succeeds partially, but desired Work display/input/context is unavailable.

### Response

1. Re-read actual device state.
2. Resolve policy-approved Work fallback if available.
3. Verify fallback target.
4. Commit WORK only against the resolved verified target.
5. Otherwise remain GAME or enter Recovery/safe fallback depending current actual state.

---

# 7. Device and evidence failures during stable operation

## RF-50 — Active Game display disconnected while in GAME

This is not necessarily a mode transition.

### Response

1. Hardware context invalidates stale snapshot.
2. Refresh actual display topology.
3. Resolve emergency/safe presentation target.
4. Preserve GAME if a coherent Game Mode can continue on fallback display.
5. If no usable presentation target exists, escalate according to safe fallback/recovery policy.

User must not be left with an intentionally invisible control surface if Windows can provide a usable display.

---

## RF-51 — Controller disconnected in Game Launcher

### Response

If keyboard/mouse or another supported input exists:

```text
continue GAME with degraded input context
```

If no usable input remains, surface reconnect guidance where possible.

Controller loss alone does not imply Game→Work.

---

## RF-52 — Audio target disappears

Because supported default-audio switching mechanism is still OPEN, audio failures must not be allowed to block unrelated platform safety.

Policy may:

- keep current available endpoint;
- choose a supported fallback if integration allows;
- mark audio orchestration degraded.

Game Mode commit criteria must distinguish mandatory from best-effort audio behavior.

---

# 8. Major mutation conflict failures

## RF-60 — User requests mode transition while Update owns mutation control

### Response

```text
DEFER or REJECT
```

No competing machine mutation should begin.

---

## RF-61 — Recovery starts while another mutation is in uncertain state

Recovery becomes coordinating owner for resolution.

Other mutation flows must stop issuing independent conflicting changes.

This is a semantic ownership transfer, not necessarily a particular mutex implementation.

---

# 9. Runtime safe-state principles

For normal runtime failures, preferred results are:

```text
Current known coherent mode
or
Verified source mode after rollback
or
Base Windows usable experience
or
Recovery-controlled state
```

Never invent:

```text
HYBRID mode
UNKNOWN-but-continue-as-success
```

Mixed actual state is a condition to resolve, not a third product mode.

---

# 10. Failure observability minimum

Each significant runtime failure should preserve semantic context conceptually equivalent to:

```text
correlationId
flow / transaction id
committed source state
requested target
current transition/game-session state
failed integration/capability
technical outcome
actual evidence snapshot
verification result
response class
final user-visible outcome
```

Exact log schema belongs to later specification.
