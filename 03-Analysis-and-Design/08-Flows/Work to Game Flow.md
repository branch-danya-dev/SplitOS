# SplitOS — Work to Game Flow

## 1. Purpose

Документ описывает end-to-end flow перехода из committed `WORK` в committed `GAME`, связывая UI, runtime owners, platform integrations, verification и commit.

---

# 2. Participants

```text
User
SplitOS Manager / Game Launch entry point
SplitOS Runtime Host
Mode Intent & Active Mode State
Mode Transition Coordination
Application Lifecycle Policy
Mode Policy
Display / Audio / Input / Power Context
Hardware Context Evaluation
SplitOS Privileged Broker
Windows / Drivers / Devices
Game Launcher
Observability & Diagnostics
```

---

# 3. Preconditions

```text
ManagedRuntimeAccess = ENABLED
Committed OperationalMode = WORK
ModeTransition = IDLE
```

Runtime must be sufficiently healthy to accept a transition request.

---

# 4. FL-02A — Explicit Switch to Game Mode

## Trigger

User chooses:

```text
Switch to Game Mode
```

## Phase 1 — Request

1. UI sends semantic `RequestOperationalMode(GAME)` to Runtime Host.
2. Runtime checks runtime-access gate.
3. Runtime queries committed operational mode.
4. Current mode is confirmed as `WORK`.
5. Mode Transition Coordination creates a new correlated transition attempt:

```text
source = WORK
target = GAME
state = REQUESTED
```

6. `WORK` remains committed.

---

# 5. Phase 2 — Inspect current Work context

1. Transition enters `INSPECTING`.
2. Application Lifecycle Policy resolves managed application classifications/policies.
3. Runtime collects Windows process/window/service evidence.
4. Relevant Work-state evidence is classified into:

```text
NON_BLOCKING
AUTO_RESOLVABLE
USER_DECISION_REQUIRED
BLOCKING
```

5. If app-specific safe-save integration exists, its result may be consumed.
6. Generic process existence must not be interpreted as saved-document evidence.

Example:

```text
WINWORD.EXE running
!= document safely saved
```

---

# 6. Phase 3 — User decision where required

If blockers require user action:

1. Transition enters `AWAITING_USER`.
2. UI receives structured blocker information.
3. User chooses a permitted resolution:
   - continue after safe resolution;
   - close/stop managed workload;
   - cancel switch.
4. If user cancels:
   - transition records `CANCELLED`;
   - committed mode remains `WORK`;
   - flow ends.

No target-mode platform mutation should proceed after cancellation.

---

# 7. Phase 4 — Resolve desired Game target

After blockers are resolved:

1. Transition enters `RESOLVING`.
2. Mode Policy resolves effective Game Mode policy.
3. Hardware Context Evaluation refreshes relevant hardware/device evidence if required.
4. Context owners resolve desired values for:
   - display;
   - audio;
   - input/navigation;
   - power/resource policy;
   - application lifecycle;
   - MODE_MANAGED Windows capabilities;
   - Shared App representation where relevant.
5. The target is frozen sufficiently for the current transition attempt.

A later external change can invalidate/restart relevant resolution according to policy, but must not silently mutate the meaning of the active transition.

---

# 8. Phase 5 — Apply target context

Transition enters `APPLYING`.

## 8.1 User-session operations

Runtime Host applies operations that belong in the interactive user session, e.g.:

```text
display topology/mode request
user-session app lifecycle
Game Launcher preparation
supported user-session device operations
```

## 8.2 Privileged operations

Where a machine-level privileged change is required:

```text
Runtime Host
→ secured semantic IPC
→ SplitOS Privileged Broker
→ Windows service/policy mechanism
```

Broker returns only technical execution outcome, not canonical mode success.

## 8.3 Platform operations

For each mutable context:

```text
Desired state
→ invoke integration
→ immediate technical result
```

Immediate success does not yet complete the transition.

---

# 9. Phase 6 — Verify actual target

Transition enters `VERIFYING`.

1. Runtime re-reads actual platform evidence.
2. Each mandatory target condition is compared with desired context.
3. Examples:

```text
Desired display = TV / 4K / 120
Actual display  = TV / 4K / 60
→ verification failed
```

```text
Desired mode-managed service state = inactive
Actual service state = stopped
→ verification passed for that condition
```

4. Optional/non-critical differences are interpreted according to Mode Policy.
5. Mandatory verification failure prevents commit.

---

# 10. Phase 7 — Commit GAME

If mandatory verification passes:

1. Transition enters `COMMITTING`.
2. Mode Intent & Active Mode State changes canonical committed mode:

```text
WORK → GAME
```

3. Transition records successful commit.
4. Transition reaches `COMPLETED`, then returns to idle lifecycle.
5. Game Session enters/returns to `LAUNCHER` state.
6. Game Launcher becomes the primary Game Mode UX.

## Result

```text
ManagedRuntimeAccess = ENABLED
CommittedMode = GAME
ModeTransition = IDLE
GameSession = LAUNCHER
```

---

# 11. Verification failure / rollback

If target verification fails:

1. No `GAME` commit occurs.
2. Transition enters `ROLLING_BACK` where feasible.
3. Runtime resolves rollback target from source Work context / safe known state.
4. Platform changes are reversed or converged to a coherent state.
5. Rollback itself is verified.
6. Terminal result becomes either:

```text
CANCELLED / ROLLED_BACK_TO_WORK
```

or:

```text
FAILED_WITH_SAFE_FALLBACK
```

7. If coherent Work state cannot be restored, control escalates to Recovery Coordination.

---

# 12. FL-02B — Direct managed game launch from Work

This is not a separate transition implementation.

## Trigger

User launches a supported managed `GAME` while committed mode is `WORK`.

## Composition

```text
GameLaunchRequest(gameId)
↓
FL-02 Work → Game
↓
only if GAME commit succeeds
↓
FL-03 Managed Game Launch(gameId)
```

The original launch intent must carry correlation/context across both flows.

If Work→Game ends as:

```text
CANCELLED
FAILED_WITH_SAFE_FALLBACK
```

then managed game launch does not continue.

---

# 13. Observability requirements

Diagnostics should correlate at least:

```text
ModeTransitionId
initiator
source/target mode
blocker summary
integration operations attempted
verification results
commit reached?
rollback/fallback result
```

Logs do not become canonical transition state.

---

# 14. Invariants

### FL-WG-001

`WORK` remains committed until semantic commit succeeds.

### FL-WG-002

Game Launcher visibility alone does not prove `GAME` committed.

### FL-WG-003

A platform API success code alone cannot complete the transition.

### FL-WG-004

User cancellation before commit cannot result in `GAME` canonical state.

### FL-WG-005

Direct game launch must reuse the same Work→Game transaction semantics.

---

# 15. Sequence summary

```text
User request GAME
→ runtime access check
→ transition REQUESTED
→ inspect Work blockers
→ resolve user decisions
→ resolve Game target policy
→ apply user-session + privileged operations
→ read actual Windows/device state
→ verify
→ commit GAME
→ Game Launcher ready
```
