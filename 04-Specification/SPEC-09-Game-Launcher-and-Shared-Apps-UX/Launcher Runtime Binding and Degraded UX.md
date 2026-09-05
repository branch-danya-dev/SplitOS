# SplitOS — Launcher Runtime Binding and Degraded UX

## 1. Purpose

This document defines how Game Launcher presentation state binds to Runtime Host truth and how the UX behaves when data is stale, capabilities are unavailable, or operations fail.

Main rule:

```text
UI state may lag
but MUST NOT contradict canonical runtime truth knowingly
```

---

## 2. Runtime snapshot

After IPC connection/reconnection, Launcher requests one coherent `LauncherRuntimeSnapshot`.

Conceptual content:

```text
runtimeAccess
committedMode
activeTransition summary
GameSession summary
library projection revision
selected/available GameProfiles summary
Shared App assignments/effective states
input/controller status
runtime/broker degraded flags relevant to UX
snapshotVersion / observedAt
```

The exact wire DTO belongs UI IPC implementation, but semantic fields MUST retain ownership boundaries.

---

## 3. Incremental events

After initial snapshot, Launcher consumes typed Runtime events.

Examples:

```text
ModeCommitted
ModeTransitionChanged
GameLibraryProjectionChanged
GameProfileChanged
LaunchOperationChanged
GameSessionChanged
SharedAppStateChanged
InputContextChanged
RuntimeAccessChanged
DegradedConditionChanged
```

Events are hints to update/requery state; they do not grant Launcher ownership.

If sequence/revision continuity cannot be proven:

```text
request fresh snapshot
```

---

## 4. UI optimistic behavior

The Launcher MAY use small optimistic presentation affordances only when they cannot be confused with semantic success.

Allowed example:

```text
user presses Launch
→ button immediately becomes busy
→ request sent
```

Not allowed:

```text
request sent
→ UI says "Game running"
```

Semantic success labels require owner-confirmed state.

---

## 5. Stale data markers

When Runtime identifies projection/evidence as stale, Launcher must surface uncertainty where it affects user action.

Examples:

```text
Installation status needs refresh
Game client unavailable — showing last known library
TV profile currently unresolved
Controller disconnected
```

Do not render stale external evidence with the same confidence as fresh verified state.

---

## 6. Degraded categories

Launcher-facing degraded categories:

```text
RUNTIME_DISCONNECTED
BROKER_UNAVAILABLE
LIBRARY_DEGRADED
GAME_CLIENT_CAPABILITY_DEGRADED
PROFILE_CONTEXT_STALE
WINDOW_PRESENTATION_DEGRADED
INPUT_DEGRADED
ENTITLEMENT_REAUTH_REQUIRED
RECOVERY_REQUIRED
```

These categories can coexist.

---

## 7. Broker unavailable

The Launcher does not talk to Broker directly.

If Runtime reports Broker unavailable:

```text
Game Mode already committed
→ Launcher may remain usable for read-only/non-privileged functions
```

Actions requiring privileged machine mutation are disabled/blocked through Runtime.

UI SHOULD say what cannot be done rather than simply showing `Error 500`.

---

## 8. Library degraded

If one Game Client adapter loses library-discovery capability:

- other clients remain usable;
- launch may still be available if fresh enough launch identity/install evidence exists;
- cards from affected client carry stale/unknown state;
- user can request refresh/reconcile where Runtime offers it.

Do not blank the whole Launcher because one client parser broke.

---

## 9. Launch failure UX

Launch failures are typed and action-oriented.

Minimum normalized classes:

```text
PROFILE_UNAVAILABLE
GAME_NOT_INSTALLED
CLIENT_NOT_AVAILABLE
AUTH_REQUIRED
CLIENT_UPDATE_REQUIRED
GAME_CONFIG_APPLY_FAILED
WINDOWS_CONTEXT_FAILED
HANDOFF_FAILED
GAME_START_NOT_CONFIRMED
COMPATIBILITY_BLOCKED
CANCELLED
UNKNOWN_FAILURE
```

Presentation must preserve whether GAME remains committed.

Typical result:

```text
launch failed
→ GameSession returns to LAUNCHER/FAILED handling
→ CommittedMode = GAME
→ Launcher remains usable
```

---

## 10. Retry semantics

`Retry` is shown only if Runtime indicates the operation is retryable.

Retry MUST create/use an operation identity consistent with the underlying subsystem's idempotency/reconciliation rules.

Launcher MUST NOT blindly resend the previous request after timeout without asking Runtime for current operation/session state.

---

## 11. Cancel semantics

Cancel may mean different things by phase:

```text
before client handoff
→ cancel preparation where safe

after external client handoff
→ SplitOS may only stop waiting/managed launch flow
→ external client/game action may already be in progress
```

The UI must not promise cancellation beyond Runtime's actual authority.

---

## 12. Game start timeout

When handoff succeeded but game is not confirmed within policy timeout:

```text
HANDOFF_ACCEPTED
→ waiting
→ timeout
→ GAME_START_NOT_CONFIRMED
```

UX options may include:

```text
Keep waiting
Retry if safe
Open client
Cancel managed wait
```

The system MUST reconcile actual process/session evidence before any retry.

---

## 13. Profile unavailable UX

If selected profile cannot currently resolve required hardware:

```text
selected profile remains canonical
current launch context = unavailable
```

Launcher should explain the blocking reason, e.g.:

```text
Living-room TV is disconnected
Required controller is unavailable
Display selector is ambiguous
```

Actions:

```text
Reconnect device
Choose another profile for this launch
Edit profile
Cancel
```

Choosing another profile for one launch MUST distinguish temporary launch choice from persistent profile preference.

---

## 14. External client auth/update UX

The Launcher should not duplicate external credentials or update UI.

Pattern:

```text
Runtime → AUTH_REQUIRED
Launcher → "Steam needs sign-in"
User → Open Steam
External client → auth
Runtime adapter → new evidence
Launcher → Retry / auto-refresh
```

Same for external client update requirements.

---

## 15. Shared App degraded UX

Each assignment shows its effective state independently.

Example:

```text
Discord Overlay          Active
Spotify Background       Active
Browser Secondary        Display disconnected
```

One failed assignment MUST NOT remove the other active assignments.

If active limit is reached:

```text
3 / 3 active
```

`Add` routes into replace/manage flow rather than silently exceeding the limit.

---

## 16. Runtime reconnect

On reconnect:

```text
transport re-established
↓
protocol compatibility handshake
↓
fresh LauncherRuntimeSnapshot
↓
compare route validity
↓
rebuild authoritative presentation
```

If a launch/game started while Launcher was disconnected, current Runtime truth wins.

Example:

```text
Launcher thought WAITING_FOR_GAME
reconnect snapshot says GAME_RUNNING
→ background Launcher presentation immediately
```

---

## 17. Entitlement downgrade/reauth

Launcher may display entitlement/account status, but must not self-authorize features.

If Runtime reports:

```text
REAUTH_REQUIRED
```

while GAME already committed, Runtime owns safe convergence policy from SPEC-04/05.

Launcher only presents permitted actions.

---

## 18. Recovery required

If Runtime reports machine/runtime recovery is required:

Launcher must prioritize:

```text
clear state
safe actions
Return to Windows/Desktop path where possible
```

over decorative Game UX.

The Launcher must not attempt local "reset mode" hacks.

---

## 19. User-facing error structure

Recommended normalized presentation model:

```text
ErrorPresentation
├── category
├── concise title
├── user-impact statement
├── next-action options
├── technical reference/correlationId
└── expandable diagnostics [optional]
```

Technical identifiers should be available for support without forcing normal users to read logs.

---

## 20. Result

```text
Runtime snapshot/events
→ presentation state
→ user intent
→ Runtime request
→ typed outcome
→ presentation
```

A robust Launcher is one that remains honest when only part of the managed gaming stack is available.