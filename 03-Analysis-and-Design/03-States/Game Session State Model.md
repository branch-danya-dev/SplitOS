# SplitOS — Game Session State Model

## 1. Purpose

Документ определяет state machine игрового runtime внутри уже активного Game Mode.

Game Session state не заменяет Operational Mode state.

```text
Operational Mode = GAME
+
Game Session = LAUNCHER | PREPARING | RUNNING | RETURNING | FAILED
```

---

# 2. Ownership

Primary owner:

```text
Game Launch Orchestration
```

Game Mode UX consumes the state for presentation.

External Game Client and Game processes provide evidence but do not own SplitOS game-session truth.

---

# 3. States

```text
INACTIVE
LAUNCHER
PREPARING
CLIENT_HANDOFF
GAME_STARTING
GAME_RUNNING
GAME_EXIT_DETECTED
RETURNING_TO_LAUNCHER
FAILED
```

---

# 4. INACTIVE

Game Session lifecycle is inactive when:

```text
Operational Mode != GAME
```

Transition to `LAUNCHER` requires Game Mode commit.

---

# 5. LAUNCHER

Normal idle state inside Game Mode.

Expected user surface:

```text
SplitOS Game Launcher
```

Possible actions:

- select game;
- select/change Game Profile;
- choose display/input profile;
- manage Shared Apps;
- open Game Mode settings;
- switch to Work Mode;
- initiate game launch.

Launch request:

```text
→ PREPARING
```

---

# 6. PREPARING

SplitOS resolves all prerequisites for selected game.

Consumes:

- current hardware snapshot;
- target display;
- selected/resolved Game Profile;
- input profile;
- optimization policy;
- external Game Client availability;
- supported game configuration knowledge.

Possible result:

```text
ready
→ CLIENT_HANDOFF

validation/configuration failure
→ FAILED
```

No actual game running state is assumed yet.

---

# 7. CLIENT_HANDOFF

SplitOS requests the authoritative external Game Client to launch the game when required.

Examples:

```text
Steam
Epic Games
Battle.net
Xbox
```

This state explicitly models ownership boundary crossing.

Success evidence:

```text
client accepted/invoked launch
→ GAME_STARTING
```

Failure:

```text
→ FAILED
```

---

# 8. GAME_STARTING

Launch has been handed off and SplitOS waits for evidence that the expected game process/session actually started.

Possible evidence:

- expected process appears;
- launcher/client reports running state;
- supported game-specific evidence.

Guard:

Evidence must correspond to the selected game, not merely any child process.

Success:

```text
→ GAME_RUNNING
```

Timeout/failure:

```text
→ FAILED
```

---

# 9. GAME_RUNNING

Selected game is considered actively running within Game Mode.

Operational Mode remains:

```text
GAME
```

Game Session owns only the session interpretation around the launched title.

Possible allowed SplitOS behavior:

- performance overlay;
- Shared Apps;
- observation of game lifecycle;
- supported runtime controls that do not violate game/anti-cheat boundaries.

Game process termination evidence:

```text
→ GAME_EXIT_DETECTED
```

---

# 10. GAME_EXIT_DETECTED

SplitOS has evidence the managed game session ended.

Important invariant:

```text
Game exit
≠
Game Mode exit
```

Next:

```text
RETURNING_TO_LAUNCHER
```

---

# 11. RETURNING_TO_LAUNCHER

SplitOS restores Game Mode launcher context after game exit.

Possible actions:

- close transient game-specific overlays;
- restore launcher focus/presentation;
- refresh external library/client evidence if needed;
- restore Game Mode shared-app presentation;
- preserve Game Mode display/input context unless profile policy says otherwise.

Success:

```text
→ LAUNCHER
```

Failure:

```text
→ FAILED
```

---

# 12. FAILED

Managed game launch/session flow could not complete normally.

Examples:

- Game Client unavailable;
- launch rejected;
- expected process never started;
- profile preparation failed;
- launcher return failed.

Expected default:

```text
Operational Mode remains GAME
```

unless failure escalates to broader Recovery state.

Possible next:

```text
user acknowledges / retry
→ LAUNCHER

system-level inconsistency
→ Recovery lifecycle
```

---

# 13. Direct launch from Work Mode

Direct game launch from Work is not represented as immediate Game Session transition.

Correct ordering:

```text
Operational Mode = WORK
↓
managed game launch detected
↓
WORK → GAME Mode Transition
↓
Game Mode committed
↓
Game Session = LAUNCHER/PREPARING
↓
normal launch lifecycle
```

The game must not become canonical `GAME_RUNNING` while committed operational mode is still `WORK`.

---

# 14. External client remains separate

Game Client may be running while Game Session = `LAUNCHER`.

Example:

```text
Steam running
Operational Mode = GAME
Game Session = LAUNCHER
```

This is valid.

Also:

```text
Steam running
Operational Mode = WORK
Game Session = INACTIVE
```

is valid because `GAME_CLIENT != GAME`.

---

# 15. Multiple games

v1 canonical assumption:

```text
one managed foreground Game Session at a time
```

Launching another managed game while `GAME_RUNNING` requires explicit policy later.

Potential options:

- block second launch;
- ask to terminate first;
- allow only verified multi-game scenario later.

No parallel multi-game canonical session is assumed now.

---

# 16. Game Session invariants

### GS-INV-001
`GAME_RUNNING` requires `Operational Mode = GAME`.

### GS-INV-002
Game Client running does not imply Game Session is running.

### GS-INV-003
Game process exit does not trigger Game→Work.

### GS-INV-004
After normal game exit, target state is `LAUNCHER`.

### GS-INV-005
Only one managed foreground Game Session exists in v1.

### GS-INV-006
External client/game evidence must be reconciled before changing canonical session interpretation.

---

# 17. Result

Game Mode has its own internal lifecycle independent from mode lifecycle:

```text
LAUNCHER
→ PREPARING
→ CLIENT_HANDOFF
→ GAME_STARTING
→ GAME_RUNNING
→ GAME_EXIT_DETECTED
→ RETURNING_TO_LAUNCHER
→ LAUNCHER
```

This preserves the product rule that Game Mode is a persistent environment, not a synonym for one running game process.
