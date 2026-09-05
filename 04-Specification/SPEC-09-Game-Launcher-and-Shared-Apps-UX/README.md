# SPEC-09 — Game Launcher & Shared Apps UX

## Status

`READY FOR REVIEW`

## Scope

```text
Game Launcher process/presentation lifecycle
controller-first navigation + focus
Runtime snapshot/event binding
launch / failure / return UX
in-game SplitOS panel semantics
Shared App assignments
Overlay / Locked Window / Secondary Display / Background
Windows top-level window orchestration
presentation degradation / recovery
```

## Core invariants

```text
Launcher presentation != canonical runtime truth
Launcher ready != GAME committed
Game running != Launcher foreground
Game exit != Game Mode exit
Shared App assignment != app/process ownership
HWND != persistent app identity
SetWindowPos success != presentation verified
Overlay requested != overlay guaranteed
```

## Artifacts

```text
Game Launcher UX Specification.md
Controller Navigation and Focus Contract.md
Launcher Runtime Binding and Degraded UX.md
In-Game Panel and Shared App Interaction.md
Shared Apps Presentation Contract.md
Shared App Window Orchestration.md
SPEC-09 Traceability.md
launcher-runtime-state.mmd
shared-app-presentation.mmd
```

## Upstream dependencies

```text
SPEC-01 process/session topology
SPEC-02 UI↔Runtime IPC trust boundary
SPEC-05 Mode Runtime
SPEC-06 Windows display/input/process evidence
SPEC-07 Game Client/GameSession evidence
SPEC-08 GameProfile/optimization
```

## Next package

```text
SPEC-10 Builder & Component Matrix
```