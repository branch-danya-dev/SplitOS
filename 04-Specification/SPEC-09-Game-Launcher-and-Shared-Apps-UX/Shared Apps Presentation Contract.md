# SplitOS — Shared Apps Presentation Contract

## 1. Purpose

This document defines the v1 semantic contract for applications classified as `SHARED` when used in Game Mode.

Shared Apps allow selected ordinary Windows applications such as browser/Discord/Spotify-class tools to remain accessible around gaming without becoming part of the game process.

Core rule:

```text
Shared App presentation
!= application ownership
!= application data ownership
!= process injection
```

---

## 2. Ownership

Canonical configuration owner:

```text
Shared App Experience
```

Presentation surface:

```text
Game Launcher / SplitOS Game panel
```

Application process/window truth:

```text
Windows + application itself
```

The Launcher MUST NOT directly create canonical Shared App assignment state.

---

## 3. Active assignment limit

v1 product constraint:

```text
maximum active Shared App assignments = 3
```

This means three active Game Mode presentation/lifecycle assignments, not three windows discovered on the system.

Examples:

```text
Discord     → OVERLAY
Spotify     → BACKGROUND
Browser     → SECONDARY_DISPLAY

active assignments = 3
```

A fourth assignment MUST require removing/replacing/deactivating one of the existing three.

The limit is enforced before canonical assignment commit.

---

## 4. SharedAppAssignment

Conceptual v1 object:

```text
SharedAppAssignment
├── assignmentId
├── applicationId
├── presentationMode
├── targetDisplaySelector? 
├── placementPreset?
├── lifecycleIntent
├── activationPolicy
├── revision
└── enabled
```

`applicationId` refers to SplitOS-known Application identity. It is not an arbitrary executable path supplied by UI.

---

## 5. Presentation modes

Canonical v1 modes:

```text
OVERLAY
LOCKED_WINDOW
SECONDARY_DISPLAY
BACKGROUND
```

These represent presentation intent, not implementation commands.

---

## 6. `OVERLAY`

Meaning:

> Present an ordinary Shared App top-level window over/alongside the game surface on the game display when the current game/windowing environment safely supports it.

v1 constraints:

- no DLL injection;
- no game-process hooks;
- no copying application pixels into the game process;
- no anti-cheat bypass;
- no assumption that arbitrary windows can appear over exclusive fullscreen;
- no silent switch of game presentation mode solely to force overlay without explicit supported policy.

Typical compatible case:

```text
borderless/windowed game
+ ordinary desktop Shared App window
+ window is movable/resizable
+ supported z-order behavior
→ OVERLAY_AVAILABLE
```

Potential incompatible case:

```text
exclusive fullscreen
or protected presentation
or application window rejects required placement
→ OVERLAY_UNAVAILABLE
```

If unavailable, SplitOS MUST NOT silently claim overlay is active.

User-visible alternatives may include:

```text
SECONDARY_DISPLAY
LOCKED_WINDOW if context supports it
BACKGROUND
Cancel
```

---

## 7. `LOCKED_WINDOW`

Meaning:

> Keep a normal Shared App window assigned to a SplitOS-managed screen region while the assignment is active.

This is still an ordinary Windows top-level window.

Typical behavior:

```text
resolve window
→ resolve target bounds
→ position/size window
→ verify actual bounds
→ monitor relevant window changes
```

`LOCKED_WINDOW` does not imply embedding inside Launcher.

The user MUST have an explicit way to unlock/deactivate the assignment.

If external user/application behavior moves/resizes the window, policy may:

```text
RESTORE_LOCK
PROMPT
MARK_DRIFT
UNMANAGE
```

but MUST NOT enter a high-frequency tug-of-war loop.

---

## 8. `SECONDARY_DISPLAY`

Meaning:

> Place/present the Shared App on a selected non-game display.

Display identity reuses SPEC-06 persistent display selectors.

Flow:

```text
SharedAppAssignment
→ target display selector
→ fresh display snapshot
→ exact compatible display resolution
→ window placement
→ read-back verification
```

If selected display disappears:

```text
assignment preference remains stored
current presentation becomes unresolved
```

The system MUST NOT silently rewrite the canonical preferred display to another monitor.

A user-approved temporary fallback may be used for the current session.

---

## 9. `BACKGROUND`

Meaning:

> Application is allowed/desired to remain active for its background function, but SplitOS does not require a visible managed Shared App window.

Examples may include:

```text
music playback
voice/chat client background operation
sync/helper behavior explicitly allowed by policy
```

`BACKGROUND` MUST NOT fabricate a visible surface.

An application may be running before the assignment starts; SplitOS may adopt that existing lifecycle state rather than restart it.

---

## 10. Lifecycle intent

Assignment lifecycle intent is separate from presentation mode.

Possible semantic values:

```text
USE_EXISTING_OR_START
USE_EXISTING_ONLY
START_IF_NEEDED
KEEP_RUNNING_AFTER_GAME
STOP_WHEN_LEAVING_GAME
DO_NOT_MANAGE_PROCESS
```

Exact combinations must be validated by Shared App policy.

Normal default SHOULD favor preserving user-owned applications rather than killing them on Game Mode exit.

---

## 11. App discovery / adoption

When activating a Shared App:

```text
Application definition
↓
find existing eligible process/window evidence
↓
if valid, adopt
else if policy allows, request normal application launch
↓
observe actual process/window
↓
present
```

A launch request success is not window readiness.

```text
process started
!= target Shared App window available
```

---

## 12. Window cardinality

Some applications expose multiple top-level windows.

Shared App adapters/definitions MUST define a deterministic window-selection rule when needed.

Possible evidence:

```text
process identity
window class
known title pattern [weak/version-sensitive]
window visibility
owner relationship
application-specific adapter identity
```

Weak title matching alone SHOULD NOT be considered a strong stable identity for official support.

If more than one eligible window remains ambiguous:

```text
WINDOW_AMBIGUOUS
```

and user selection may be required.

---

## 13. Window ownership and restore snapshot

Before SplitOS changes an adopted window placement, it SHOULD capture a session-local restore snapshot:

```text
window identity
bounds
monitor
z-order/topmost intent where observable
show state
observedAt
```

This snapshot is evidence for best-effort restoration, not canonical application configuration.

When assignment ends:

```text
if window still same identity
→ best-effort restore original placement
```

If application already moved/recreated the window, do not restore stale coordinates onto an unrelated new window.

---

## 14. During game launch / game running

Shared App assignments belong to Game Mode, not one GameSession unless explicitly game-specific later.

Therefore:

```text
GAME Launcher
→ Shared Apps active
→ launch game
→ assignments continue according to presentation capability
→ game exits
→ assignments remain Game Mode assignments
```

For `OVERLAY` or `LOCKED_WINDOW`, actual visibility may change when the game takes foreground or changes display mode.

SplitOS must report the effective presentation state:

```text
ACTIVE_VISIBLE
ACTIVE_BACKGROUND
UNAVAILABLE_CURRENT_CONTEXT
REQUIRES_USER_ACTION
DRIFTED
```

---

## 15. Game Mode exit

On `GAME → WORK`:

Shared App Experience evaluates each assignment lifecycle policy.

Canonical assignment preference may remain configured for the next Game Mode session even when no longer actively presented.

Example:

```text
Discord assignment configured = true
Game Mode ends
→ active presentation deactivated
→ Discord process preserved by default
→ assignment remains for next GAME
```

Do not equate:

```text
assignment configured
```

with:

```text
window currently topmost/positioned
```

---

## 16. External application changes

Application/user remains able to:

```text
close window
open new window
resize/move window
restart app
show modal/login/update window
```

SplitOS treats these as external evidence.

Example:

```text
managed Spotify window closed by user
↓
Shared App state refresh
↓
WINDOW_NOT_AVAILABLE
```

Do not immediately relaunch against explicit user action unless assignment policy explicitly requires it and UX makes that behavior clear.

---

## 17. Security / anti-cheat boundary

Shared Apps MUST NOT require:

```text
DLL injection into game
capture/injection into protected rendering path
memory reading from game
input spoofing into game
anti-cheat bypass
DRM modification
privileged arbitrary window/process commands from UI
```

Window management targets are resolved from typed Application/SharedApp definitions and verified OS evidence.

---

## 18. Failure outcomes

Expected typed outcomes include:

```text
APP_NOT_INSTALLED
APP_NOT_RUNNING
APP_START_FAILED
WINDOW_NOT_FOUND
WINDOW_AMBIGUOUS
WINDOW_REPLACED
TARGET_DISPLAY_NOT_FOUND
OVERLAY_UNAVAILABLE
PLACEMENT_NOT_REACHED
WINDOW_ACCESS_DENIED
EXTERNAL_CHANGE_DETECTED
ACTIVE_ASSIGNMENT_LIMIT_REACHED
```

These are presentation/lifecycle outcomes; they do not by themselves change `OperationalMode`.

---

## 19. Result

```text
Shared App canonical assignment
        ↓
Runtime Host Shared App Experience
        ↓
Application lifecycle + window evidence
        ↓
Presentation adapter
        ↓
ordinary Windows app/window
        ↓
read-back / observation
```

The feature is useful precisely because it orchestrates existing Windows applications without pretending SplitOS owns or embeds them.