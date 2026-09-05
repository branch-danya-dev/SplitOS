# SplitOS — SPEC-09 Traceability

## 1. Purpose

This document maps `SPEC-09 Game Launcher & Shared Apps UX` decisions to the canonical A&D and earlier Detailed Specifications.

```text
A&D ownership/state/behavior
→ SPEC-01 process boundary
→ SPEC-05 Mode Runtime
→ SPEC-06 Windows/Input context
→ SPEC-07 Game Session/client evidence
→ SPEC-08 GameProfile/optimization
→ SPEC-09 Game UX/presentation
```

---

## 2. Source mapping

| SPEC-09 area | Canonical source |
|---|---|
| Launcher is presentation, not truth owner | Ownership Model; Interface Model; Synthesis Logical Components |
| Launcher readiness before GAME commit | SPEC-05 Mode Runtime / Game Launcher readiness predicate |
| GAME remains committed after game exit | Game Session State Model; Game Launch Behavior |
| Game Launcher != GAME_RUNNING | State/Behavior models; SPEC-07 |
| controller-first UX | Game Experience responsibility / Concept |
| Windows remains underlying shell | Boundary/Concept/Synthesis |
| Shared Apps max 3 active | Concept / Configuration Model / Shared App ownership |
| Shared App modes | Data Configuration Model: Overlay / Locked Window / Secondary Display / Background |
| Shared App app/process remains external | Ownership/Data/Trust |
| display selectors | SPEC-06 |
| game launch phases | SPEC-07 + SPEC-08 |
| profile availability/selection | SPEC-08 |
| Game-to-Work decision | SPEC-05 |
| UI does not call Broker | SPEC-01/02 Trust boundary |
| no injection/anti-cheat bypass | Integration/Trust + SPEC-07/08 |

---

## 3. Specification decisions

```text
SPEC-DEC-089
`SplitOS.GameLauncher.exe` is an unelevated presentation process and never owns canonical mode, game session, entitlement, installation or profile truth.

SPEC-DEC-090
Launcher v1 remains resident during a managed game by default, stops competing for foreground/input in `GAME_RUNNING`, and restores quickly after confirmed game exit.

SPEC-DEC-091
Launcher has a `READY_PRECOMMIT` state: IPC + initial presentation + input/focus readiness can satisfy the Game Mode readiness predicate before canonical `GAME` commit, but full active Game UX appears only after committed GAME evidence.

SPEC-DEC-092
Controller navigation uses semantic input actions and exactly-one logical focus target per interactive focus scope; keyboard/mouse remain recovery-compatible inputs.

SPEC-DEC-093
While a game is running, hidden Launcher does not consume ordinary gameplay controller input. Exact global controller chord for a SplitOS in-game panel remains OPEN pending compatibility testing.

SPEC-DEC-094
Launcher v1 logical routes are HOME, LIBRARY, GAME_DETAILS, PROFILE_PICKER, LAUNCH_PROGRESS, SHARED_APPS, SYSTEM_MENU and blocking MODAL surfaces.

SPEC-DEC-095
Launch UX binds to one runtime launch operation/correlation identity and must preserve `CLIENT_HANDOFF != GAME_RUNNING`.

SPEC-DEC-096
After confirmed game exit, Launcher restores the pre-launch semantic route/focus bookmark where valid; committed mode remains GAME.

SPEC-DEC-097
v1 allows at most three active Shared App assignments. Limit is enforced before canonical assignment commit.

SPEC-DEC-098
Shared App presentation modes are OVERLAY, LOCKED_WINDOW, SECONDARY_DISPLAY and BACKGROUND; presentation mode is separate from application lifecycle intent.

SPEC-DEC-099
Shared App presentation uses ordinary user-session Windows window orchestration and must not require DLL injection, game hooks, rendering capture injection or anti-cheat bypass.

SPEC-DEC-100
v1 window orchestration baseline uses public Win32/DWM primitives for top-level window discovery, identity correlation, placement/read-back and event observation. HWND is ephemeral evidence and is never persistent Shared App identity.

SPEC-DEC-101
Overlay is capability-gated. SplitOS does not guarantee arbitrary Shared App visibility over exclusive fullscreen/protected game presentation and must expose `OVERLAY_UNAVAILABLE` rather than invasive fallback.

SPEC-DEC-102
Shared App window/display resolution is generation-bound; stale HWND/display targets are re-resolved before mutation.

SPEC-DEC-103
Normal background Shared App events never steal game foreground automatically. Shared App foreground activation is user-initiated and may fail as a typed Windows outcome.

SPEC-DEC-104
External Shared App window movement/closure/recreation is treated as evidence; repeated lock/placement failure degrades/unmanages instead of entering an unbounded z-order/position fight.

SPEC-DEC-105
Launcher reconnect always rebuilds authoritative presentation from a fresh Runtime snapshot when sequence/revision continuity is uncertain.

SPEC-DEC-106
Launcher failure/degraded state cannot directly change operational mode or terminate a running game; Runtime Host owns restart/reconciliation and safe fallback.

SPEC-DEC-107
The in-game SplitOS panel is an optional capability-gated control surface. Managed game correctness does not depend on overlay panel availability.

SPEC-DEC-108
Full Launcher takeover while a game is still running is not a guaranteed v1 behavior. The guaranteed return path is confirmed game exit → Launcher restoration.
```

---

## 4. Key invariants

```text
Launcher presentation != canonical truth

Launcher process alive != GAME committed

Launch handoff accepted != game running

Game exited != Game Mode exited

Shared App configured != Shared App currently visible

HWND != persistent application/window identity

SetWindowPos success != placement verified

Overlay requested != overlay guaranteed visible

Shared App process running != correct window presentation
```

---

## 5. Verification backlog — Launcher

```text
V-UX-001 Launcher reaches READY_PRECOMMIT without falsely showing GAME active
V-UX-002 full Home activates only after committed GAME
V-UX-003 exactly one logical focus target per route/modal scope
V-UX-004 keyboard recovery path works with no controller
V-UX-005 controller disconnect preserves route/focus and does not change mode
V-UX-006 hidden Launcher ignores ordinary gameplay input during GAME_RUNNING
V-UX-007 launch button shows busy but not running before runtime confirmation
V-UX-008 HANDOFF_ACCEPTED remains Waiting for game until proof set confirms running
V-UX-009 external AUTH_REQUIRED keeps GAME and exposes correct user action
V-UX-010 game exit restores pre-launch route/focus bookmark
V-UX-011 Launcher crash during game does not terminate game/mode
V-UX-012 Launcher reconnect replaces stale UI with fresh runtime snapshot
V-UX-013 Back from Home does not implicitly leave GAME
V-UX-014 Return to Work uses Mode Runtime user-decision contract
V-UX-015 one client library degradation does not blank unaffected client games
```

---

## 6. Verification backlog — Shared Apps

```text
V-SHARED-001 fourth active assignment is rejected/requires replacement before commit
V-SHARED-002 BACKGROUND assignment does not require visible window
V-SHARED-003 SECONDARY_DISPLAY preserves canonical display selector if display disconnects
V-SHARED-004 overlay unsupported in incompatible context is reported honestly
V-SHARED-005 arbitrary UI HWND cannot become mutation target
V-SHARED-006 stale HWND after app window recreation is not reused
V-SHARED-007 stale display generation blocks old placement target
V-SHARED-008 SetWindowPos result is followed by bounds/read-back verification
V-SHARED-009 repeated locked-window restore failure degrades instead of infinite loop
V-SHARED-010 user-closing app window is not immediately overridden without explicit policy
V-SHARED-011 Shared App notification does not steal game foreground
V-SHARED-012 Game Mode exit deactivates presentation without killing user app by default
V-SHARED-013 restore snapshot is applied only to same current window identity
V-SHARED-014 higher-integrity inaccessible window degrades instead of adding generic Broker control
V-SHARED-015 Shared App failure does not change committed GAME by itself
```

---

## 7. Verification backlog — in-game panel

```text
V-PANEL-001 ordinary gameplay buttons do not move hidden Launcher focus
V-PANEL-002 unsupported overlay context reports panel unavailable
V-PANEL-003 panel close returns focus/input to game where platform permits
V-PANEL-004 panel crash leaves game/session/mode intact
V-PANEL-005 Shared App panel actions route through Runtime semantic contracts
V-PANEL-006 active game Return to Work renders close-game/cancel decision from Mode Runtime
V-PANEL-007 no injection/anti-cheat bypass required for panel capability
```

---

## 8. Engineering gates

Before calling all SPEC-09 presentation capabilities production-supported, prototype:

```text
ENG-UX-01 selected Launcher UI framework + fullscreen/DPI/display behavior
ENG-UX-02 GameInput controller focus/repeat/navigation latency
ENG-UX-03 foreground/background input handoff around real games
ENG-UX-04 global panel invocation mechanism and game-side double-input behavior
ENG-SHARED-01 window discovery/correlation across Discord/Spotify/browser classes
ENG-SHARED-02 SetWindowPos + DWM bounds verification across DPI/display changes
ENG-SHARED-03 overlay z-order behavior with borderless and exclusive fullscreen games
ENG-SHARED-04 secondary-display hot-unplug/replug behavior
ENG-SHARED-05 app self-reposition/recreate/update dialog behavior
ENG-SHARED-06 higher-integrity window access failure behavior
```

---

## 9. Next target

After SPEC-09 review/merge:

```text
SPEC-10 Builder & Component Matrix
```

SPEC-10 will turn the build/distribution model into a concrete source-validation, manifest, offline servicing, component classification and baseline-verification contract.