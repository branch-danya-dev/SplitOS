# SplitOS — In-Game Panel and Shared App Interaction

## 1. Purpose

This document defines the semantic v1 contract for invoking a SplitOS control surface while a managed game is running.

The exact global controller/keyboard invocation mechanism is intentionally not canonized until compatibility validation proves it does not interfere with games or platform shortcuts.

What is defined here is the behavior **after** a valid `SHOW_GAME_PANEL` request reaches Runtime/Launcher.

---

## 2. Invocation boundary

Conceptual action:

```text
SHOW_GAME_PANEL
```

Possible future physical triggers:

```text
controller chord
keyboard shortcut
Launcher/Shared App external control
```

Exact trigger mapping is `OPEN`.

The trigger mechanism MUST NOT require:

```text
input injection into game
DLL hook into game process
anti-cheat bypass
memory patching
```

---

## 3. Panel availability

Panel eligibility requires at minimum:

```text
CommittedMode = GAME
GameSession = GAME_RUNNING
Launcher/Runtime panel capability available
```

If current game/presentation mode cannot safely show an overlay surface:

```text
PANEL_OVERLAY_UNAVAILABLE
```

The system may use another supported presentation path, but MUST NOT claim an in-game overlay is active when Windows/game presentation prevents it.

---

## 4. Panel role

The panel is a limited Game Mode control surface, not a second full Launcher.

Minimum semantic sections may include:

```text
Shared Apps
Current Game/Profile summary
Audio/Input quick status
Return to Launcher [only if semantically possible]
Return to Work request
Close panel
```

It MUST NOT duplicate account checkout, full profile editing, Builder/update administration, or unrelated Manager functionality by default.

---

## 5. Input focus while panel is open

When panel is successfully active:

```text
panel obtains logical SplitOS UI focus
```

However the underlying game may still receive some physical input depending on the trigger/presentation technology.

Therefore the implementation MUST validate whether the chosen invocation/input capture method can prevent harmful double-action for the supported scenario.

If it cannot:

```text
controller interactive panel capability
→ unsupported for that scenario
```

rather than pretending isolation exists.

Keyboard/mouse may be a fallback where platform behavior permits.

---

## 6. Close behavior

`BACK` at panel root:

```text
Close panel
→ release SplitOS panel focus
→ return foreground/focus to game where permitted
```

Panel close MUST NOT:

```text
exit Game Mode
terminate game
return to Launcher fullscreen
```

unless the user explicitly chose such an action.

---

## 7. Shared App quick access

Panel may display up to the three active Shared App assignments.

Example:

```text
Discord      OVERLAY
Spotify      BACKGROUND
Browser      SECONDARY_DISPLAY
```

User actions may include:

```text
Focus/show app
Hide current visible presentation
Toggle assignment active presentation where policy allows
Open Shared Apps management
```

The panel sends semantic requests to Runtime Host; it does not manipulate HWNDs directly.

---

## 8. Shared App focus

When user selects a visible Shared App:

```text
panel
→ RequestSharedAppActivate(assignmentId)
→ Shared App Experience
→ window/app activation attempt
→ effective result
```

Possible outcomes:

```text
ACTIVATED
VISIBLE_NOT_FOCUSED
USER_ACTION_REQUIRED
WINDOW_UNAVAILABLE
PRESENTATION_UNSUPPORTED
```

The panel must preserve the distinction.

---

## 9. Overlay coexistence

A SplitOS panel and one or more Shared App overlays may conflict for limited screen area/z-order.

v1 policy:

- SplitOS panel has temporary UI priority while explicitly open;
- Shared App assignments remain active but may be visually suppressed behind panel;
- closing panel restores their previous effective presentation where still valid;
- no canonical assignment is changed merely because panel temporarily covers it.

---

## 10. Game focus protection

Background Shared App events MUST NOT steal game focus automatically.

Examples:

```text
Discord notification
Spotify track change
browser popup
```

must not cause:

```text
SetForegroundWindow(sharedApp)
```

without explicit user action.

An app may independently steal focus due to its own behavior; SplitOS observes that as external state and may warn/recover presentation policy, but must not claim ownership of the app's internal behavior.

---

## 11. Return to Launcher

While a game is running, "Return to Launcher" is not equivalent to game exit.

v1 default:

```text
GAME_RUNNING
→ full Launcher takeover is not automatic
```

Possible product actions:

```text
Show panel
Minimize/switch away from game if supported and user explicitly requests
Close game then return Launcher
```

The exact "minimize game and show full Launcher" capability is `OPEN/version-tested`, because games differ significantly in focus/minimize behavior.

The canonical guaranteed path remains:

```text
game exits
→ Launcher restored
```

---

## 12. Return to Work

Panel can expose:

```text
Return to Work
```

but this is only:

```text
RequestOperationalMode(WORK)
```

If a game is active:

```text
Runtime Host
→ user-decision-required blocker
→ Close game and continue / Cancel
```

The panel renders the decision; transition semantics remain SPEC-05 owned.

---

## 13. Panel crash / loss

If panel presentation crashes or becomes unavailable:

```text
Game continues
GameSession continues
CommittedMode remains GAME
```

Runtime/Launcher should tear down transient panel focus state and return ordinary input ownership to the game.

---

## 14. Exclusive fullscreen and protected scenarios

v1 must explicitly represent capability limitations.

Examples:

```text
exclusive fullscreen game
protected content surface
anti-cheat-sensitive overlay conflict
window composition behavior incompatible
```

may result in:

```text
IN_GAME_PANEL_OVERLAY = UNSUPPORTED_CURRENT_CONTEXT
```

Fallback UX can tell the user:

```text
Use secondary display
Use keyboard/Windows switch path
Return to Launcher after game
```

No injection-based workaround is allowed.

---

## 15. Result

```text
validated invocation action
→ Runtime/Launcher panel
→ semantic Shared App / mode requests
→ typed result
→ close panel
→ game resumes foreground/input
```

The panel is an optional, capability-gated convenience layer; managed game/session correctness does not depend on it.