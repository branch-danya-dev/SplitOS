# SplitOS — Shared App Window Orchestration

## 1. Purpose

This document defines the v1 Windows window-management mechanism behind Shared App presentation modes.

It extends SPEC-06 with one additional user-session integration area:

```text
Windows top-level window discovery
→ identity correlation
→ placement / z-order request
→ read-back verification
→ change observation
```

The implementation remains unelevated and outside protected game processes.

---

## 2. Supported Windows mechanism family

v1 baseline uses public Win32/DWM mechanisms for ordinary desktop windows:

```text
EnumWindows
GetWindowThreadProcessId
IsWindow / IsWindowVisible
GetWindowLongPtr [bounded metadata where needed]
DwmGetWindowAttribute(DWMWA_EXTENDED_FRAME_BOUNDS)
GetWindowRect [fallback/diagnostic]
MonitorFromWindow / GetMonitorInfo
SetWindowPos
SetWinEventHook
```

These mechanisms do not guarantee that every application/window can be repositioned or remain visible over every game presentation mode.

---

## 3. Window identity

An `HWND` is ephemeral and MUST NOT be persisted as Shared App identity.

Session-local `WindowEvidence` should include:

```text
hwnd
owningPid
processCreationTime where available
windowsSessionId
windowClass
visible
cloaked state where relevant
extendedFrameBounds
observedAt
windowGeneration
```

Correlation rule:

```text
HWND alone
!= durable window identity
```

For supported Shared Apps, application-specific window selectors may add known stable/validated signals.

---

## 4. Discovery

Initial discovery:

```text
EnumWindows
→ top-level desktop windows
→ GetWindowThreadProcessId
→ correlate to expected Application/process evidence
→ filter eligible windows
```

SPEC-09 MUST NOT accept an arbitrary `HWND` from Launcher UI as a trusted mutation target.

The Runtime Host resolves the window from an `applicationId` / assignment and current OS evidence.

---

## 5. Change observation

Shared App window state is event-driven where practical.

Use `SetWinEventHook` with out-of-context observation for relevant accessibility/window events such as:

```text
window shown
window hidden
window destroyed
location changed
foreground changed [when useful]
```

Event callbacks do not directly mutate canonical assignment state.

They:

```text
invalidate window snapshot/generation
→ enqueue refresh
→ rebuild evidence
→ Shared App owner interprets result
```

This follows the same stale-evidence model as SPEC-06 devices.

---

## 6. Window generation

Each resolved Shared App presentation holds a `windowGeneration`.

Example:

```text
resolve Discord HWND against generation 14
↓
Discord updates/recreates window
↓
EVENT_OBJECT_DESTROY / SHOW
↓
generation = 15
↓
old HWND target is stale
```

A stale target MUST NOT be repositioned without re-resolution.

---

## 7. Placement geometry

Target geometry is calculated in physical screen coordinates using the currently resolved display/work area and DPI-aware window bounds.

For visible bounds, `DwmGetWindowAttribute(DWMWA_EXTENDED_FRAME_BOUNDS)` is preferred where available because ordinary `GetWindowRect` can include invisible resize borders.

The placement model should use normalized presets where possible:

```text
TOP_LEFT
TOP_RIGHT
BOTTOM_LEFT
BOTTOM_RIGHT
SIDE_LEFT
SIDE_RIGHT
CUSTOM_NORMALIZED_RECT
```

`CUSTOM_NORMALIZED_RECT` is stored relative to the target display work area rather than raw absolute pixels when persistence across resolution changes is intended.

---

## 8. Placement apply

Typical apply:

```text
resolved current HWND
+ target display/work area
+ target rect
+ presentation z-order intent
↓
SetWindowPos
↓
fresh window bounds read-back
↓
verify tolerance
```

The immediate `SetWindowPos` return value is only an operation result.

```text
SetWindowPos success
!= PLACEMENT_VERIFIED
```

The actual window bounds/z-order behavior must be observed after application.

---

## 9. Topmost / Overlay behavior

`OVERLAY` may request topmost z-order using documented top-level window ordering behavior when the target window/application permits it.

However:

```text
HWND_TOPMOST requested
!= guaranteed visible over every game mode
```

Exclusive fullscreen, protected surfaces, game display mode changes, application-owned z-order changes, or system restrictions may make overlay ineffective.

If effective visibility cannot be verified:

```text
OVERLAY_UNAVAILABLE
```

The system MUST NOT repeatedly force z-order in a tight loop.

---

## 10. Locked-window behavior

`LOCKED_WINDOW` may own a target rect while the assignment is active.

When a location-change event indicates drift:

```text
read fresh window state
↓
classify source/extent if possible
↓
debounce
↓
policy decision
```

Policy options:

```text
RESTORE_LOCK
MARK_DRIFT
ASK_USER
UNMANAGE
```

v1 MUST rate-limit restoration attempts.

Recommended safety invariant:

```text
repeated restore failure
→ stop fighting the app
→ DRIFTED / PLACEMENT_NOT_REACHED
```

---

## 11. Secondary-display behavior

`SECONDARY_DISPLAY` uses the selected SPEC-06 display selector.

Resolution:

```text
Display selector
→ current DisplaySnapshot generation
→ exact display
→ monitor/work area
→ target window rect
```

If the display generation changes before apply/read-back:

```text
STALE_DISPLAY_CONTEXT
```

and placement is re-resolved.

If the display disconnects after placement:

- assignment remains configured;
- active presentation becomes unresolved;
- SplitOS may offer a temporary fallback;
- it MUST not silently rewrite canonical display binding.

---

## 12. Window show/minimize state

Shared App orchestration SHOULD preserve user/app intent where practical.

SplitOS may need to make a selected window visible for `OVERLAY`, `LOCKED_WINDOW`, or `SECONDARY_DISPLAY`, but MUST distinguish:

```text
window unavailable
window minimized
window hidden by application
window cloaked
```

Exact restore/show APIs and per-application behavior require compatibility validation.

A generic "force show every hidden window" rule is prohibited.

---

## 13. Foreground activation

Windows foreground activation has platform restrictions.

Shared App UI should request focus only as a direct user action:

```text
User selects Shared App
→ attempt user-initiated activation
```

Background monitoring MUST NOT continuously steal foreground from the game.

If Windows denies activation:

```text
FOREGROUND_ACTIVATION_DENIED
```

and UI may instruct user or keep the app presented without focus.

---

## 14. App-owned dialogs and child windows

Login/update/file-picker dialogs may be separate top-level/owned windows.

v1 policy:

- do not blindly move every window owned by the same process;
- primary Shared App presentation tracks the resolved primary top-level window;
- app-owned modal dialog may temporarily supersede primary focus/visibility;
- application-specific adapter may define safe owned-window handling.

Generic handling MUST avoid relocating unrelated dialogs across monitors without user intent.

---

## 15. Unsupported window types

Examples requiring capability downgrade or rejection:

```text
exclusive/protected presentation surfaces
windows that immediately undo position/size
apps with no stable top-level window
apps whose visible UI is not represented by ordinary desktop HWND semantics
security-sensitive/admin surfaces outside current user context
```

Result:

```text
PRESENTATION_CAPABILITY_UNSUPPORTED
```

rather than invasive fallback.

---

## 16. Restore snapshot

Before mutation:

```text
WindowRestoreSnapshot
├── resolved window identity
├── original bounds
├── original monitor
├── original show state
├── original topmost observation where available
├── capturedAt
└── generation
```

On deactivation:

```text
same window identity + compatible generation
→ best-effort restore
```

If identity no longer matches:

```text
skip stale restore
```

The restore snapshot is transient evidence and MUST NOT become canonical user app settings.

---

## 17. Verification contract

Presentation verification can include:

```text
WINDOW_EXISTS
WINDOW_IDENTITY_MATCHES
TARGET_MONITOR_REACHED
TARGET_BOUNDS_WITHIN_TOLERANCE
VISIBLE_WHEN_REQUIRED
TOPMOST_REQUEST_EFFECTIVE_WHERE_SUPPORTED
```

A Shared App assignment may remain active even if one optional presentation predicate degrades, but the UI must show the effective state.

---

## 18. Privilege boundary

Window orchestration executes inside the current interactive user session.

It MUST NOT go through Broker merely to gain arbitrary cross-process window control.

If a target window runs at a higher integrity level and normal interaction is denied:

```text
WINDOW_ACCESS_DENIED
```

v1 SHOULD treat that as unsupported/degraded rather than adding a generic privileged window-manipulation capability to Broker.

---

## 19. Result

```text
SharedAppAssignment
→ resolve Application/window evidence
→ generation-bound HWND
→ calculate presentation target
→ documented user-session window operation
→ fresh read-back
→ presentation result
```

This keeps Shared Apps as ordinary Windows-window orchestration rather than an injection/capture subsystem.