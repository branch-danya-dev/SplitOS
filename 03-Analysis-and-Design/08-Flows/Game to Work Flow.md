# SplitOS — Game to Work Flow

## 1. Purpose

Документ описывает end-to-end flow перехода из committed `GAME` обратно в committed `WORK`.

Обычный выход из игры не является этим flow.

---

# 2. Participants

```text
User
Game Launcher / SplitOS UI
SplitOS Runtime Host
Mode Intent & Active Mode State
Mode Transition Coordination
Game Session State
Application Lifecycle Policy
Mode Policy
Display / Audio / Input / Power Context
Shared App Experience
SplitOS Privileged Broker
Windows / Drivers / Devices
Windows Desktop / Shell
Recovery Coordination
Observability & Diagnostics
```

---

# 3. Preconditions

```text
ManagedRuntimeAccess = ENABLED
CommittedMode = GAME
ModeTransition = IDLE
```

Game Session may be `LAUNCHER` or may still have an active/startup game state.

---

# 4. FL-04A — Explicit Switch to Work

## Trigger

User selects:

```text
Switch to Work Mode
```

## Phase 1 — Request

1. UI sends `RequestOperationalMode(WORK)`.
2. Runtime verifies managed runtime access.
3. Mode State confirms current committed mode is `GAME`.
4. Mode Transition Coordination creates a transition:

```text
source = GAME
target = WORK
state = REQUESTED
```

5. `GAME` remains committed.

---

# 5. Phase 2 — Inspect Game context

1. Transition enters `INSPECTING`.
2. Runtime reads canonical Game Session state.
3. If Game Session is:

```text
GAME_RUNNING
GAME_STARTING
CLIENT_HANDOFF
PREPARING
```

then transition cannot silently ignore it.
4. Application/Game lifecycle policy determines whether the state is:
   - safely closable;
   - requires user confirmation;
   - temporarily blocking.
5. Shared Apps and Game-only helpers are also evaluated for target Work behavior.

---

# 6. Active game branch

If a game is active:

1. Transition enters `AWAITING_USER` where confirmation is required.
2. User is offered supported choices such as:
   - close/leave game and continue;
   - cancel switch.
3. If user cancels:

```text
CommittedMode = GAME
Transition = CANCELLED
```

4. If user confirms close:
   - Game Launch/Game Session logic coordinates graceful termination where supported;
   - actual exit evidence is observed;
   - only after the game is no longer active does mode transition continue.

Forceful termination without explicit policy must not be treated as default safe behavior.

---

# 7. Phase 3 — Resolve Work target

1. Transition enters `RESOLVING`.
2. Mode Policy resolves effective Work target context.
3. Application Lifecycle Policy resolves Work application behavior.
4. Context owners resolve desired:
   - display;
   - audio;
   - input;
   - power/resource policy;
   - MODE_MANAGED component state;
   - Shared App transformation back to normal Windows representation.

---

# 8. Phase 4 — Apply Work context

Transition enters `APPLYING`.

Typical operations:

```text
remove Game Launcher as primary foreground UX
restore Work display intent
restore Work power policy
stop/deactivate gaming-only helpers
restore Work-oriented MODE_MANAGED capabilities
convert Shared Apps to Work behavior
restore normal Windows desktop accessibility
```

Privileged machine changes go through the Privileged Broker; user-session changes stay in the user session where appropriate.

---

# 9. Phase 5 — Verify Work target

1. Transition enters `VERIFYING`.
2. Runtime re-reads actual Windows/device/service evidence.
3. Mandatory Work target conditions are verified.
4. Windows Shell/Desktop usability must be part of semantic success, not only service state.
5. If mandatory target conditions fail, no `WORK` commit occurs.

---

# 10. Phase 6 — Commit WORK

If verification succeeds:

1. Transition enters `COMMITTING`.
2. Mode State changes canonical mode:

```text
GAME → WORK
```

3. Game Session converges to `INACTIVE` for normal Work context.
4. Transition completes and returns to idle lifecycle.
5. Windows Desktop / Shell becomes primary visible Work UX.

## Result

```text
ManagedRuntimeAccess = ENABLED
CommittedMode = WORK
GameSession = INACTIVE
ModeTransition = IDLE
```

---

# 11. Verification failure / rollback

If Work target cannot be verified:

1. `WORK` is not committed.
2. Transition attempts rollback to coherent Game state where feasible.
3. If rollback cannot restore safe coherent state, Recovery Coordination selects a safe fallback.
4. Windows usability remains a higher priority than preserving cosmetic Game UX.

Terminal result must explicitly state where the system ended.

---

# 12. Shared Apps transformation

A Shared App may be active in Game Mode as:

```text
Overlay
Locked Window
Secondary Display
Background
```

During Game→Work:

1. Shared App Experience resolves Work representation.
2. Application lifecycle executes the configured transformation.
3. Work flow must not assume that Game presentation state equals native application state.

---

# 13. Subscription downgrade interaction

If entitlement loss requires managed runtime shutdown while committed mode is `GAME`, this flow can be reused conceptually as the safe path toward Windows base experience.

However the target after entitlement shutdown is ultimately:

```text
ManagedRuntimeAccess = DISABLED
OperationalMode = NONE
```

not persistent `WORK`.

The downgrade coordinator therefore may use Game→Work-like context normalization first, then disable managed runtime after a safe Windows state is reached.

Exact timing belongs to Failures.

---

# 14. Invariants

### FL-GW-001

`GAME` remains committed until Work semantic commit succeeds.

### FL-GW-002

Normal game exit does not automatically start Game→Work.

### FL-GW-003

Active game state cannot be silently discarded during mode switch.

### FL-GW-004

Windows Desktop usability is part of successful Work activation.

### FL-GW-005

Failure to activate Work must converge to explicit rollback/fallback, never ambiguous hybrid state.

---

# 15. Sequence summary

```text
User request WORK
→ inspect Game Session
→ resolve active game/user decision
→ resolve Work policy
→ apply Work context
→ read actual platform state
→ verify
→ commit WORK
→ Game Session INACTIVE
→ Windows Desktop ready
```
