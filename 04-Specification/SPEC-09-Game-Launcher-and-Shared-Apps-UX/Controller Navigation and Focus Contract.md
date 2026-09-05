# SplitOS — Controller Navigation and Focus Contract

## 1. Purpose

This document defines the v1 controller-first interaction model for `SplitOS.GameLauncher.exe` and SplitOS Game Mode panels.

The goal is deterministic navigation that remains usable with:

```text
gamepad
keyboard
mouse
```

without binding product semantics to one specific controller model.

---

## 2. Input ownership

When Launcher is foreground and interactive:

```text
Launcher
→ owns UI navigation interpretation
```

When a managed game is confirmed running:

```text
Game
→ owns ordinary gameplay input
Launcher
→ MUST stop consuming ordinary navigation input
```

A future/optional SplitOS global panel invocation action may be observed separately, but the exact controller chord is **OPEN** until compatibility testing proves it does not create unacceptable game-side input effects.

The Launcher MUST NOT install input injection, remapping, aim-assist, macro, or anti-cheat-sensitive hooks to obtain controller navigation.

---

## 3. Semantic input actions

UI logic consumes semantic actions:

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
FOCUS_HOME
```

Physical mappings may include:

```text
GameInput controller buttons
keyboard keys
mouse pointer/click
```

but route/view models MUST NOT depend directly on device-specific button numbers.

---

## 4. Focus invariant

At every interactive Launcher route:

```text
exactly one logical focus target
```

except when:

```text
no interactive content exists
or
blocking loading/degraded surface owns focus
```

Focus MUST be visible enough for controller use.

The current focus target is presentation state, not product authority.

---

## 5. Focus identity

Focus bookmarks SHOULD use stable semantic IDs rather than UI-tree indices.

Example:

```text
route = GAME_DETAILS(gameId)
focusId = action.launch
```

not:

```text
focusIndex = 6
```

because layout/order may change after refresh.

If bookmarked focus no longer exists:

```text
nearest valid semantic fallback
→ route default focus
```

---

## 6. Directional navigation

Directional movement MUST be deterministic.

Priority:

```text
explicit navigation edge
→ geometric nearest valid target in requested direction
→ route-level fallback
```

Views with nontrivial grids/carousels SHOULD declare explicit edges for predictable behavior.

A single input event MUST cause at most one logical navigation action before repeat behavior begins.

---

## 7. Input repeat

Controller/keyboard repeat MAY accelerate navigation through long lists, but MUST preserve deterministic selection.

v1 recommended baseline:

```text
initial repeat delay ≈ 350 ms
repeat interval ≈ 90 ms
```

These are UX defaults, not hard protocol constants, and may become accessibility settings.

Analog stick navigation SHOULD use:

```text
dead zone
+ direction latch
+ repeat timing
```

to avoid diagonal oscillation.

Exact dead-zone values belong implementation calibration.

---

## 8. Activate / Back semantics

`ACTIVATE`:

```text
performs the primary action of focused target
```

`BACK`:

```text
blocking modal → dismiss/cancel if permitted
nested route   → previous meaningful route
HOME           → no implicit exit from GAME
```

The Back action MUST NOT silently request `WORK` from Home.

Leaving Game Mode requires an explicit user action such as:

```text
System Menu → Return to Work
```

---

## 9. Modal focus

When a blocking modal appears:

```text
background route loses interactive focus
modal gets exclusive logical focus scope
```

The focus MUST NOT escape behind the modal through controller navigation.

Examples:

```text
profile unavailable
user decision required
external client action required
Return to Work while game is active
```

Default destructive action MUST NOT receive focus solely because it is visually first.

---

## 10. Async refresh behavior

Library/profile/runtime updates may arrive while user navigates.

Rules:

1. Existing focused semantic item remains focused if still valid.
2. Reordering MUST NOT unexpectedly activate another item.
3. If focused item disappears, choose deterministic neighbor/fallback.
4. Incoming data MUST NOT steal focus merely because it is new.
5. Errors/modals steal focus only when the user must respond before continuing.

---

## 11. Route transitions

Route transition SHOULD preserve an explicit focus return target.

Example:

```text
LIBRARY
focus game:cyberpunk
↓ ACTIVATE
GAME_DETAILS(cyberpunk)
focus action.launch
↓ BACK
LIBRARY
focus game:cyberpunk
```

Profile picker:

```text
GAME_DETAILS
focus profile selector
↓
PROFILE_PICKER
focus current profile
↓ choose
GAME_DETAILS
focus profile selector
```

---

## 12. Mouse coexistence

Mouse movement MAY temporarily switch visible affordance from controller focus to pointer hover, but MUST NOT destroy the logical controller focus bookmark.

Recommended behavior:

```text
mouse click
→ clicked control receives logical focus

next controller NAV action
→ resumes from current logical focus
```

---

## 13. Keyboard recovery path

Keyboard navigation MUST remain available even if controller enumeration fails.

Minimum:

```text
Arrow keys / WASD candidate → directional nav
Enter / Space             → activate
Escape / Backspace        → back
Tab                        → optional focus traversal
```

Exact key mapping may be adjusted, but there MUST be a path to:

```text
open System Menu
Return to Work
```

without a controller.

---

## 14. Controller disconnect

If the currently used controller disconnects while Launcher is active:

```text
Input generation changes
→ Launcher receives refreshed input context
```

The current route/focus remains.

If another eligible controller exists, navigation may continue.

If none exists:

```text
show non-blocking input status
keyboard/mouse recovery remains available
```

Do not auto-switch operational mode.

---

## 15. During game execution

After `GAME_RUNNING_CONFIRMED`:

```text
Launcher ordinary navigation subscription = inactive
```

The Launcher MUST NOT:

```text
react to A/B/X/Y as UI actions
move hidden focus in response to gameplay
open random panels because the user played the game
```

If a SplitOS in-game panel invocation contract is implemented later, it MUST be isolated from normal navigation actions and compatibility-tested.

---

## 16. Shared App panel navigation

Shared App management uses the same focus contract.

Conceptual surface:

```text
Shared Apps
├── Slot 1
├── Slot 2
├── Slot 3
└── Add / Manage
```

When three active assignments already exist:

```text
Add
→ disabled or opens replace/manage flow
```

It MUST NOT create a fourth active assignment and rely on later cleanup.

---

## 17. Accessibility / legibility baseline

Controller-first UX requires at minimum:

- visible focus indication;
- no essential action dependent only on hover;
- readable state labels in addition to color;
- deterministic Back path;
- modal focus trapping;
- keyboard recovery;
- no time-critical dismissal for ordinary errors unless required by platform behavior.

Exact WCAG/UI-framework implementation belongs UI engineering, but these semantic requirements are normative.

---

## 18. Result

```text
Physical input
→ Input normalization
→ semantic Launcher action
→ deterministic logical focus/navigation
→ semantic Runtime request where needed
```

The focus system is local presentation behavior; runtime/system state remains outside it.