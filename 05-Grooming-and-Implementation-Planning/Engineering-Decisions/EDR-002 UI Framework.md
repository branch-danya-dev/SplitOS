# EDR-002 — Manager / Game Launcher UI Framework

Status: `CLOSED`

## Decision

Selected UI baseline:

```text
Framework       WinUI 3
Platform        Windows App SDK stable 2.4.x generation
Language        C# / .NET 10
Packaging       unpackaged
Deployment      self-contained Windows App SDK for initial SplitOS releases
Processes       Manager and Game Launcher remain separate executables
```

The implementation repository MUST pin an exact stable Windows App SDK package version. The initial coding baseline should use the current stable `2.4.x` release line; upgrading it is an explicit dependency change.

## Why WinUI 3

SplitOS is a new Windows-only desktop product and requires:

- modern Windows desktop/windowing integration;
- custom full-screen Game Launcher UI;
- high-DPI and multi-monitor behavior;
- accessibility primitives;
- composition/animation without introducing a browser runtime as the primary shell;
- first-class C# integration with the selected implementation stack.

Microsoft currently recommends WinUI 3 / Windows App SDK for new native Windows desktop applications.

## Manager and Launcher remain separate applications

Using the same UI framework MUST NOT collapse their process roles.

```text
SplitOS.Manager.exe
→ account / subscription / mode / profiles / devices / update / recovery control center

SplitOS.GameLauncher.exe
→ controller-first GAME presentation
```

Both remain:

- unelevated;
- presentation-only;
- clients of Runtime Host;
- unable to call Privileged Broker directly.

## Unpackaged deployment

SplitOS does not use Microsoft Store/App Installer/MSIX update semantics as its product update authority.

The product already owns:

```text
prepared Windows baseline
+ SplitOS release directories
+ SplitOS signed update channel
+ previous-release recovery capsule
```

Therefore initial Manager/Launcher deployment is unpackaged.

This avoids making package identity or App Installer a hidden dependency of runtime/update semantics.

## Self-contained Windows App SDK

Initial releases use self-contained Windows App SDK deployment for the UI applications.

Benefits for SplitOS:

- release directory contains the UI runtime it was tested with;
- no dependency on an independently serviced shared Windows App SDK runtime for basic UI startup;
- previous SplitOS release can retain its matching UI dependencies for rollback;
- prepared baseline and update package behavior remain deterministic.

Cost:

- larger release artifacts;
- duplicated framework payload between UI applications unless later optimized.

The size cost is accepted for early implementation. A future shared/framework-dependent model requires measured size/serviceability evidence and must preserve rollback compatibility.

## Controller-first rule

WinUI input events do not become the canonical game-controller abstraction.

The Launcher consumes semantic navigation actions from the SplitOS input layer:

```text
GameInput / InputContext
↓
NAV_UP / NAV_DOWN / NAV_LEFT / NAV_RIGHT
ACTIVATE / BACK / OPEN_CONTEXT / OPEN_SYSTEM_MENU
↓
Launcher focus model
```

The hidden Launcher does not consume ordinary gameplay controls while `GAME_RUNNING`.

## Fullscreen/windowing rule

The Launcher implements a borderless full-screen desktop presentation but is **not** a Windows shell replacement.

It must tolerate:

- display topology change;
- DPI change;
- foreground loss;
- game launch foreground handoff;
- Runtime disconnect/reconnect;
- transition rollback before GAME commit.

## Performance rule

The Launcher stays resident by default while a managed game is running, so background overhead is a release criterion.

Slice tests must record at least:

```text
background CPU
background GPU
working set / private memory
input/focus non-interference
restore latency after game exit
```

Final numeric budgets are bound by SPEC-14 release acceptance rather than invented in this decision.

## Rejected primary alternatives

### WPF

WPF is mature and viable, but for a brand-new Windows-only product we prefer Microsoft's current native Windows application direction and the Windows App SDK composition/windowing stack.

### Electron / browser shell

Rejected as primary Manager/Launcher technology because it adds a browser/runtime footprint and does not provide a requirement advantage for this Windows-native system product.

### C++ WinUI as default

Rejected because EDR-001 selects C# as the primary implementation language and no UI requirement justifies duplicating the product stack in C++.

## Acceptance evidence for Slice 0

Before UI breadth begins, prove:

```text
Manager WinUI process starts unelevated
Launcher WinUI process starts unelevated
both connect only to Runtime UI pipe
basic focus navigation works
Launcher can enter/leave borderless full-screen presentation
Runtime disconnect produces degraded/non-mutating UI
```

## Evidence sources

- Microsoft Windows development path: https://learn.microsoft.com/en-us/windows/apps/get-started/
- Windows App SDK downloads: https://learn.microsoft.com/en-us/windows/apps/windows-app-sdk/downloads
- Windows App SDK deployment overview: https://learn.microsoft.com/en-us/windows/apps/windows-app-sdk/deployment-architecture
- Unpackaged WinUI deployment: https://learn.microsoft.com/en-us/windows/apps/package-and-deploy/unpackage-winui-app

At decision time, Windows App SDK `2.4.0` is the current stable release shown by Microsoft. The repository must pin the exact version actually implemented/tested.
