# SplitOS — Managed Game Launch Flow

## 1. Purpose

Документ описывает end-to-end managed game launch после того, как SplitOS уже находится в committed `GAME`.

Он связывает Game Launcher, library/profile/hardware owners, Windows/device integrations и external Game Client.

---

# 2. Participants

```text
User
Game Launcher
SplitOS Runtime Host
Game Launch Orchestration
Game Library Representation
Game Profiles
Hardware Context Evaluation
Game Optimization Policy
Display / Audio / Input / Power Context
Shared App Experience
Game Client Adapter
External Game Client / Platform
Windows Process Evidence
Game Session State
Observability & Diagnostics
```

---

# 3. Preconditions

```text
ManagedRuntimeAccess = ENABLED
CommittedMode = GAME
GameSession = LAUNCHER
selected Game exists in SplitOS representation
```

The game must have a supported or explicitly acceptable launch capability path.

---

# 4. FL-03A — Launch from Game Launcher

## Trigger

User chooses a game and invokes `Launch`.

## Phase 1 — Accept launch request

1. Game Launcher sends `GameLaunchRequest(gameId, optionalProfileIntent)`.
2. Game Launch Orchestration creates a correlated `GameLaunchId`.
3. It validates:
   - managed runtime still enabled;
   - committed mode still `GAME`;
   - no conflicting foreground game session exists;
   - game entity is known.
4. Game Session transitions:

```text
LAUNCHER → PREPARING
```

---

# 5. Phase 2 — Resolve library/client truth

1. Game Launch Orchestration queries Game Library Representation.
2. Game Library returns SplitOS projection:
   - game identity;
   - associated supported Game Client;
   - last known installation projection;
   - client-specific launch identity where known.
3. If projection freshness is insufficient, Game Client Adapter performs reconciliation/evidence refresh where supported.
4. External client/platform remains authority for actual install/license/launch eligibility.
5. If game is unavailable, launch fails before platform preparation is committed unnecessarily.

---

# 6. Phase 3 — Resolve effective Game Profile

1. Game Profiles resolves candidate profiles for the selected game.
2. Hardware Context Evaluation refreshes relevant snapshot when stale or invalidated.
3. Current display/input availability is read from Windows/device evidence.
4. Game Optimization Policy consumes:

```text
game
profile intent
hardware snapshot
display capability
user overrides
compatibility knowledge
```

5. An effective launch context is produced:

```text
Game
+ Game Profile
+ Display Context
+ Input Context
+ Power Context
+ Optimization Context
+ supported Shared App Context
```

6. If no safe/compatible effective profile can be resolved, launch stops and Game Session returns to `LAUNCHER` with explicit error.

---

# 7. Phase 4 — Prepare local runtime context

Game Launch Orchestration coordinates target owners.

## Typical preparation

```text
display target resolution/apply
power policy apply
input/navigation preparation
audio routing where supported
Shared Apps preparation
supported game-setting preparation
client readiness checks
```

For mutable Windows/device state:

```text
resolve desired
→ invoke integration
→ read actual state
→ verify
```

Mandatory preparation failure prevents client handoff.

---

# 8. Phase 5 — Client handoff

1. Game Session enters:

```text
CLIENT_HANDOFF
```

2. Game Launch Orchestration calls the appropriate Game Client Adapter.
3. Adapter uses its supported client-specific mechanism.
4. External client returns/produces an immediate launch outcome.
5. If semantic result is:

```text
HANDOFF_ACCEPTED
```

then Game Session moves to:

```text
GAME_STARTING
```

Critical rule:

```text
HANDOFF_ACCEPTED
!= GAME_RUNNING
```

If result is `AUTH_REQUIRED`, `CLIENT_UNAVAILABLE`, `GAME_NOT_AVAILABLE`, `UNSUPPORTED`, or `FAILED`, launch moves to controlled failure handling.

---

# 9. Phase 6 — Confirm game running

1. Runtime observes client/game/process evidence using the adapter and Windows evidence mechanisms available for that game/client.
2. Evidence must be correlated with the requested game, not just with “some executable appeared”.
3. When sufficient evidence confirms the intended title is running:

```text
GAME_STARTING → GAME_RUNNING
```

4. Game Session records game/profile/client relation for the managed session.
5. Game Launcher may move to background/hidden presentation according to Game Mode UX policy.

---

# 10. Start timeout / unconfirmed launch

If `GAME_RUNNING` is not confirmed within policy:

1. Runtime reconciles client/process evidence one final time.
2. If the game is still unconfirmed, the launch cannot remain indefinitely in `GAME_STARTING`.
3. Game Session moves to `FAILED`.
4. Temporary launch-preparation state is cleaned up where safe.
5. User receives a meaningful launch result.
6. Game Session converges back to `LAUNCHER` unless broader recovery is required.

Exact timeout duration is deferred to Failure/Specification layers.

---

# 11. FL-03B — Normal game exit

## Trigger

Game/process/client evidence confirms that the managed game session ended.

## Sequence

1. Game Session:

```text
GAME_RUNNING → GAME_EXIT_DETECTED
```

2. Runtime records exit evidence/outcome.
3. Game-specific temporary state is cleaned up where applicable.
4. Game Launcher is prepared as foreground Game Mode UX.
5. Game Session:

```text
GAME_EXIT_DETECTED
→ RETURNING_TO_LAUNCHER
→ LAUNCHER
```

6. Committed Operational Mode remains:

```text
GAME
```

## Critical rule

```text
Game exit
!= Game → Work
```

---

# 12. FL-03C — Client unavailable / auth required

If external client cannot launch because authentication/client readiness is missing:

1. SplitOS reports the external dependency state.
2. SplitOS may offer a supported action such as opening/bringing forward the client.
3. It must not invent license/auth success.
4. User may retry launch after the external condition is resolved.
5. Game Session returns to `LAUNCHER` if no active launch remains.

---

# 13. Direct launch from Work composition

When a supported direct launch originates in Work:

```text
original GameLaunchRequest
→ FL-02 Work → Game
→ GAME commit
→ resume same GameLaunchId/intention
→ FL-03 Managed Game Launch
```

No client handoff is allowed before successful Game Mode commit.

---

# 14. Observability

Correlated diagnostic context should include:

```text
GameLaunchId
GameId
GameProfileId/effective profile
GameClient
reconciliation freshness
preparation operations
client handoff result
GAME_RUNNING evidence
exit evidence
terminal result
```

Diagnostics remain evidence, not Game Session owner.

---

# 15. Invariants

### FL-GL-001

Only one managed foreground Game Session exists in v1.

### FL-GL-002

External client handoff success cannot directly set `GAME_RUNNING`.

### FL-GL-003

A stale installation projection cannot override fresher client authority evidence.

### FL-GL-004

Normal game exit returns to Game Launcher and keeps committed `GAME`.

### FL-GL-005

Profile/hardware/context must be resolved before launch handoff when they are required for the launch scenario.

---

# 16. Sequence summary

```text
User Launch
→ validate GAME/runtime/session
→ reconcile game/client projection
→ refresh hardware
→ resolve Game Profile
→ apply + verify local context
→ Game Client Adapter
→ HANDOFF_ACCEPTED
→ observe actual game evidence
→ GAME_RUNNING
→ observe exit
→ cleanup
→ return to Game Launcher
```
