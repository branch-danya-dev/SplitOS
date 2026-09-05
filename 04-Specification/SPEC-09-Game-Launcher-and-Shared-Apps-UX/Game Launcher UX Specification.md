# SplitOS — Game Launcher UX Specification

## 1. Purpose

This document defines the v1 behavioral and presentation contract for `SplitOS.GameLauncher.exe`.

The Game Launcher is the primary visible UX while SplitOS has committed `GAME`, but it is **not** the owner of canonical runtime truth.

```text
Game Launcher
→ presents
→ requests
→ observes

Runtime Host
→ owns/coordinates semantic state
```

The Launcher MUST NOT invent or directly persist:

```text
OperationalModeState
GameSession state
Game installation truth
GameProfile truth
Entitlement
ModeTransition outcome
```

---

## 2. Product role

The Launcher provides a controller-first fullscreen experience for:

```text
Game Mode entry
Game library browsing
Game details
GameProfile selection
Managed launch
Launch progress / client interaction status
Return after game exit
Shared Apps management
Game-to-Work request
Runtime/degraded status
```

Windows remains the underlying shell/runtime. The Launcher is not a Windows Shell replacement.

---

## 3. Process contract

Physical process:

```text
SplitOS.GameLauncher.exe
```

Cardinality:

```text
0..1 per eligible Windows interactive session
```

The active physical-console Runtime Host is responsible for launching/restarting it when required.

The Launcher:

- runs unelevated;
- connects only to the session-scoped Runtime Host IPC endpoint;
- MUST NOT call Privileged Broker directly;
- MUST NOT open canonical SQLite stores directly;
- MAY maintain rebuildable in-memory presentation state;
- MUST treat Runtime Host snapshots/events as source for system truth.

---

## 4. Launcher lifecycle

Canonical presentation lifecycle:

```text
STOPPED
→ STARTING
→ CONNECTING
→ PREPARING
→ READY_PRECOMMIT
→ ACTIVE
↔ BACKGROUND_GAME_RUNNING
→ RESTORING
→ ACTIVE
→ STOPPING
→ STOPPED
```

Additional failure state:

```text
DEGRADED_DISCONNECTED
```

### 4.1 `READY_PRECOMMIT`

`READY_PRECOMMIT` means:

```text
process alive
+ Runtime IPC established
+ initial view model loaded enough to present Game UX
+ controller/focus subsystem ready
```

It does **not** mean `OperationalMode = GAME` yet.

This state satisfies the SPEC-05 launcher-readiness predicate required before GAME commit.

The Launcher MUST NOT render a false fully-active GAME Home before it receives committed GAME state.

Recommended precommit presentation:

```text
Preparing Game Mode…
```

with no action that assumes the transition has committed.

### 4.2 `ACTIVE`

Allowed only when Runtime Host reports:

```text
CommittedMode = GAME
```

The Launcher may then present the full Game Mode navigation surface.

### 4.3 `BACKGROUND_GAME_RUNNING`

When a managed game is confirmed running:

- Launcher process SHOULD remain resident in v1;
- its fullscreen surface MUST no longer compete with the game foreground;
- ordinary controller-navigation input MUST NOT be consumed by Launcher;
- Launcher retains only minimal presentation/session state needed for fast restoration;
- Runtime Host remains owner of GameSession truth.

### 4.4 `RESTORING`

After `GAME_EXITED_CONFIRMED`:

```text
Runtime Host
→ tells Launcher game session ended
→ Launcher restores foreground presentation
→ restores pre-launch navigation context when still valid
→ ACTIVE
```

Committed mode remains `GAME`.

---

## 5. Launcher crash semantics

```text
Launcher crash
!= Game Mode failure
!= game termination
!= GameSession termination
```

If Launcher crashes while no game is running:

```text
Runtime Host
→ restart Launcher
→ resupply current GAME snapshot
→ restore navigation from last recoverable presentation bookmark
```

If Launcher crashes while game is running:

- game remains untouched;
- Runtime Host may restart Launcher in background or defer until game exit;
- after game exit, Launcher MUST be available or Runtime Host MUST surface a safe fallback path to leave GAME.

Repeated Launcher crash MUST produce a typed degraded condition rather than restart forever without backoff.

---

## 6. Navigation surfaces

Normative v1 logical routes:

```text
HOME
LIBRARY
GAME_DETAILS(gameId)
PROFILE_PICKER(gameId)
LAUNCH_PROGRESS(launchOperationId)
SHARED_APPS
SYSTEM_MENU
MODAL
```

Implementation may render these as pages/panels/layers, but semantic routes are stable.

### 6.1 Home

Home SHOULD prioritize:

```text
Continue / Recently Played
Recent games
Pinned/Favorite games [if product feature enabled]
Shared Apps quick access
Current Game context status
```

Home MUST NOT infer installed/launchable state locally. Game cards consume Game Library projection from Runtime Host.

### 6.2 Library

Library displays the normalized SplitOS game library.

A card MUST be able to represent at least:

```text
READY
CLIENT_ACTION_REQUIRED
NOT_INSTALLED_VERIFIED
STALE_OR_UNKNOWN
UNSUPPORTED_CLIENT_CAPABILITY
```

UI labels may be friendlier, but must not collapse uncertainty into "Installed".

### 6.3 Game Details

`GAME_DETAILS` is the canonical pre-launch decision screen.

Minimum information:

```text
Game identity/art
selected GameProfile
current profile availability
client/platform
launch readiness
optimization summary
important blockers/warnings
Launch action
Profile action
```

### 6.4 Profile Picker

Profile Picker consumes SPEC-08 profile and current eligibility information.

It MUST distinguish:

```text
eligible
currently unavailable
ambiguous device binding
stale context
unsupported configuration capability
```

Selecting a profile changes user intent through Runtime Host; the Launcher MUST NOT mutate `user.db` directly.

---

## 7. Launch UX contract

User action:

```text
Launch
```

becomes a semantic request:

```text
RequestManagedGameLaunch(gameId, optional explicitProfileId)
```

Launcher MUST bind launch progress to one `launchOperationId` / `correlationId`.

Presentation phases:

```text
REQUESTED
RESOLVING_PROFILE
PREPARING_WINDOWS_CONTEXT
PREPARING_GAME_CONFIGURATION
CLIENT_HANDOFF
WAITING_FOR_GAME
GAME_RUNNING_CONFIRMED
```

Not every backend implementation must emit every micro-step, but if a phase is shown it must come from Runtime Host truth.

### 7.1 Handoff rule

```text
CLIENT_HANDOFF accepted
!= Game running
```

The UI MUST NOT replace `Waiting for game…` with `Running` until SPEC-07 GameSession evidence confirms it.

### 7.2 External client interaction

Normal client-owned outcomes include:

```text
AUTH_REQUIRED
UPDATE_REQUIRED
CLIENT_UI_REQUIRED
LICENSE_OR_PLATFORM_ACTION_REQUIRED
```

When one occurs:

- Launcher explains that the external client requires action;
- external client remains responsible for its own auth/update/license UX;
- Game Mode remains committed unless another failure requires recovery;
- Launcher can offer `Retry`, `Cancel`, or `Open client` only when Runtime exposes that action.

---

## 8. Game-running presentation behavior

After `GAME_RUNNING_CONFIRMED`:

```text
GameSession = GAME_RUNNING
CommittedMode = GAME
Launcher = BACKGROUND_GAME_RUNNING
```

The Launcher MUST NOT:

- continuously steal foreground;
- continuously consume normal controller buttons;
- display launch progress over the game unless an explicitly supported SplitOS panel is invoked;
- interpret client process lifetime as game lifetime.

---

## 9. Return after game exit

Canonical rule:

```text
Game exits
→ GAME_EXITED_CONFIRMED
→ Launcher foreground restored
→ GAME remains committed
```

Navigation restoration SHOULD use a presentation bookmark captured before the launch.

Example:

```text
GAME_DETAILS(Cyberpunk)
  focus = Launch button
→ launch
→ game exits
→ GAME_DETAILS(Cyberpunk)
  focus = Launch / last meaningful action
```

If the bookmarked route is no longer valid because the game disappeared from current projection, fallback:

```text
LIBRARY
```

If Library is unavailable, fallback:

```text
HOME
```

---

## 10. Game-to-Work request

The System Menu exposes:

```text
Return to Work
```

The Launcher only requests:

```text
RequestOperationalMode(WORK)
```

If a game is active, Runtime Host owns the user-decision flow described by SPEC-05.

Launcher may render the decision modal, but the options and outcome come from the transition contract.

---

## 11. Runtime disconnect behavior

If Launcher loses Runtime Host IPC:

```text
ACTIVE
or
READY_PRECOMMIT
→ DEGRADED_DISCONNECTED
```

The Launcher MUST:

- stop issuing state-mutating requests;
- not claim mode/game state changes succeeded;
- preserve only presentation cache;
- show reconnecting/degraded status;
- reconnect using SPEC-01/02 compatible protocol behavior.

If reconnection succeeds:

```text
fetch fresh runtime snapshot
→ discard stale authoritative-looking UI state
→ rebuild route state
```

---

## 12. Presentation cache vs truth

Launcher MAY cache:

```text
artwork already rendered
scroll position
route history
focus bookmark
animation state
```

Launcher MUST NOT treat cached values as authority for:

```text
entitlement
committed mode
installed state
launchability
GameSession
GameProfile revision
Shared App assignment truth
```

---

## 13. Input parity

Gamepad is primary v1 navigation input, but keyboard/mouse MUST remain usable for accessibility/recovery.

Input surfaces should map to semantic actions rather than device-specific button names:

```text
NAV_UP
NAV_DOWN
NAV_LEFT
NAV_RIGHT
ACTIVATE
BACK
OPEN_CONTEXT
OPEN_SYSTEM_MENU
PAGE_NEXT
PAGE_PREVIOUS
```

Device mapping belongs the input/navigation implementation and MAY vary by controller family.

---

## 14. Visual implementation boundary

This SPEC defines behavior, information architecture, focus, and state binding.

It does **not** yet canonically require:

```text
WPF
WinUI 3
DirectComposition
custom DirectX renderer
webview UI
```

The selected framework MUST be able to satisfy:

- fullscreen/borderless Game Mode surface;
- low-latency controller navigation;
- reliable focus model;
- accessibility/recovery keyboard path;
- fast suspend/restore around game launch;
- correct multi-DPI/display behavior;
- no dependency on replacing Windows Shell.

---

## 15. Security / trust

Launcher is unprivileged presentation code.

It MUST NOT accept arbitrary values from artwork/library metadata as:

```text
file paths to execute
URIs to invoke
Broker commands
registry paths
window handles to mutate
```

All semantic actions are resolved by Runtime Host/adapters through typed contracts.

---

## 16. Result

Canonical Launcher relationship:

```text
Runtime truth
     ↓ snapshot/events
Game Launcher presentation
     ↓ semantic user intent
Runtime Host owners
     ↓
Mode / Game Profile / Game Launch / Shared App flows
```

The Launcher should feel like the Game Mode shell while remaining architecturally replaceable and non-authoritative.