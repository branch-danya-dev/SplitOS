# SplitOS — Mode Transition Model

## 1. Purpose

Документ определяет отдельную state machine для переходов между `WORK` и `GAME`.

Переход рассматривается как управляемая транзакция, а не как мгновенная смена enum.

---

# 2. Ownership

Primary owner:

```text
Mode Transition Coordination
```

Consumes:

- current committed mode;
- target mode intent;
- Mode Policy;
- Application Lifecycle Policy;
- platform actual-state evidence;
- user confirmations;
- recovery policy.

Does not own:

- committed operational mode truth;
- actual Windows service/device truth;
- application-internal save semantics.

---

# 3. States

```text
IDLE
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

`COMPLETED`, `CANCELLED`, `FAILED_WITH_SAFE_FALLBACK` are terminal result states for a transition attempt.

---

# 4. IDLE

No active transition exists.

Allowed trigger:

```text
RequestModeTransition(source, target)
```

Guard:

```text
source == committedMode
source != target
no blocking recovery state
```

Next:

```text
REQUESTED
```

---

# 5. REQUESTED

Transition request accepted for evaluation.

Stores conceptually:

```text
sourceMode
targetMode
initiator
requestTime
```

Next:

```text
INSPECTING
```

---

# 6. INSPECTING

SplitOS evaluates whether transition can proceed.

For `WORK → GAME`, inspection includes potential:

- unsaved work;
- long-running processes;
- local servers;
- applications requiring confirmation;
- incompatible target-mode conditions;
- required device/display availability;
- entitlement if target capability requires it.

Possible result:

```text
no blockers
→ RESOLVING

blocking issue requiring user decision
→ AWAITING_USER

hard blocker
→ BLOCKED
```

---

# 7. BLOCKED

A transition cannot safely continue under current conditions.

Examples:

- mandatory target dependency unavailable;
- transition policy forbids automatic continuation;
- recovery-required state detected;
- required context cannot be established.

Possible next:

```text
condition resolved externally
→ INSPECTING

user/system abort
→ CANCELLED
```

`BLOCKED` does not mean source mode is lost.

Canonical committed mode remains source mode.

---

# 8. AWAITING_USER

SplitOS needs an explicit user decision.

Examples:

- unsaved document cannot be safely autosaved;
- active server/process should be stopped only with confirmation;
- user must approve a destructive/interrupting action.

Possible user decisions:

```text
continue / resolve
→ RESOLVING

cancel
→ CANCELLED
```

---

# 9. RESOLVING

SplitOS performs pre-application resolution actions.

Examples:

- graceful close request;
- safe save where supported;
- stopping approved processes;
- preparing target-mode dependencies;
- resolving selected display/input/profile context.

If resolution succeeds:

```text
→ APPLYING
```

If source mode remains coherent but transition cannot continue:

```text
→ CANCELLED
```

If partial changes create inconsistency:

```text
→ ROLLING_BACK
```

---

# 10. APPLYING

Target Mode Policy is being applied.

Possible actions include:

```text
MODE_MANAGED capability changes
application lifecycle changes
display/audio/input/power changes
Game Launcher readiness
Game-specific runtime preparation where applicable
```

Important:

```text
committedMode still == sourceMode
```

Target mode is not yet canonical.

Next:

```text
VERIFYING
```

or on failure:

```text
ROLLING_BACK
```

---

# 11. VERIFYING

SplitOS checks actual-state evidence against required target conditions.

Verification must distinguish:

```text
command accepted
≠
state actually applied
```

Possible result:

```text
all mandatory conditions satisfied
→ COMMITTING

recoverable mismatch
→ ROLLING_BACK

unrecoverable but safe state established
→ FAILED_WITH_SAFE_FALLBACK
```

---

# 12. COMMITTING

Atomic semantic boundary of transition.

At this point SplitOS changes canonical operational truth:

```text
committedMode = targetMode
```

and clears active transition target metadata as appropriate.

Then:

```text
→ COMPLETED
```

Rule:

> No UI, launcher, service state, or process presence may independently commit operational mode.

---

# 13. ROLLING_BACK

SplitOS attempts to restore a coherent source-mode or known-safe state.

Priority:

```text
User data integrity
→ coherent operational state
→ source mode where feasible
→ safe fallback
```

Possible result:

```text
source mode restored
→ CANCELLED or FAILED_WITH_SAFE_FALLBACK depending reason

safe non-source state established
→ FAILED_WITH_SAFE_FALLBACK
```

`ROLLING_BACK` must not silently commit target mode merely because some target actions succeeded.

---

# 14. COMPLETED

Transition succeeded.

Required conditions:

- target mode canonical commit completed;
- required target state verified;
- transition result recorded;
- user-visible runtime reflects committed mode.

Result:

```text
SUCCESS
```

---

# 15. CANCELLED

Transition did not complete and normal operation remains in source mode.

Expected result:

```text
committedMode = sourceMode
```

No target-mode canonical commit occurred.

Reasons may include:

- user cancellation;
- safe resolution failure before destructive application;
- blocker remains unresolved.

---

# 16. FAILED_WITH_SAFE_FALLBACK

Transition failed, but SplitOS established a coherent safe state that may require recovery/attention.

This differs from `CANCELLED`:

```text
CANCELLED
→ normal source-mode operation continues

FAILED_WITH_SAFE_FALLBACK
→ transition failed and system is safe, but degraded/recovery state may exist
```

---

# 17. Work → Game specifics

Canonical flow:

```text
IDLE
→ REQUESTED
→ INSPECTING
→ [AWAITING_USER / BLOCKED]*
→ RESOLVING
→ APPLYING
→ VERIFYING
→ COMMITTING
→ COMPLETED
```

Important Work→Game checks:

- work blockers;
- source data safety;
- Game Mode entitlement where required;
- target display/input availability;
- Game Launcher readiness;
- required game-client/runtime context if transition was game-launch initiated.

---

# 18. Game → Work specifics

Game→Work normally has fewer data-loss blockers but still requires managed teardown.

Possible checks:

- active game process state;
- Shared Apps state;
- gaming client background activity;
- restoration of Work display/audio/input/power;
- restoration/activation of Work-specific MODE_MANAGED capabilities.

If game is still running, policy must determine whether:

```text
block
ask user to close
request graceful termination
```

Exact rule belongs Behavior analysis.

---

# 19. Direct game launch interaction

If a supported `GAME` is launched from Work Mode:

```text
GameLaunchRequest
↓
ModeTransition WORK→GAME
↓
only after successful mode commit
↓
continue managed game launch
```

If transition ends as:

```text
CANCELLED
FAILED_WITH_SAFE_FALLBACK
```

normal managed game launch must not proceed.

---

# 20. Idempotency / repeated requests

Repeated request to same target while transition active must not create competing transitions.

Conceptual rule:

```text
one active mode transition at a time
```

Potential behaviors:

- deduplicate same request;
- reject conflicting target;
- queue later intent only if explicitly designed.

Exact contract later.

---

# 21. Crash/restart semantics

Transition state must be recoverable enough to answer:

```text
Was there an active transition?
What was source mode?
What was target mode?
Was canonical commit reached?
What actions may have been partially applied?
```

Rule:

```text
no evidence of COMMITTING completion
→ do not assume target mode became canonical
```

---

# 22. Transition invariants

### TR-INV-001
Only one active transition exists at a time.

### TR-INV-002
Committed mode remains source mode until `COMMITTING` succeeds.

### TR-INV-003
User cancellation before commit cannot result in target mode being canonical.

### TR-INV-004
A failed mandatory target-state verification cannot result in normal `SUCCESS`.

### TR-INV-005
Rollback outcome must be explicit.

### TR-INV-006
Transition lifecycle state is not the same as operational mode state.

---

# 23. Result

Mode transition is modeled as a transaction with explicit inspection, resolution, application, verification, commit and rollback phases.

This creates the basis for later Behavior, Interfaces, Failures and Recovery models.
