# Engineering Decisions and Research Queue

## 1. Purpose

Makes implementation unknowns explicit so they do not become hidden assumptions inside code or tickets.

Each item is classified as:

```text
BLOCKING_DECISION
ENGINEERING_RESEARCH
OPERATIONAL_SELECTION
LATER_SCOPE
```

A decision is closed only when there is enough evidence to unblock the affected slice.

---

## EDR-001 — Desktop/runtime implementation language and runtime

Type: `BLOCKING_DECISION`

Blocks:

- SLICE-00 process skeleton;
- project/build setup;
- most Windows interop implementation;
- test framework choice.

Question:

> Which implementation stack best fits Runtime Host, Broker Service, Update Bootstrap, Recovery tooling and shared contracts while preserving Windows-native integration and secure service boundaries?

Candidates may include a .NET-centric stack, native C/C++, Rust, or a justified combination.

Required evidence:

- Windows Service implementation;
- Named Pipe server/client implementation;
- WTS/session APIs;
- CCD/Core Audio/GameInput/SCM/PowrProf interop;
- SQLite and DPAPI support;
- Authenticode packaging;
- crash dump/ETW integration;
- testability;
- build/release tooling maturity;
- deployment size/runtime dependency implications.

Exit criterion:

```text
one primary stack selected
+ prototype proves RuntimeHost ↔ Broker hello
+ native API call
+ service install/run
+ packaging path
```

---

## EDR-002 — Manager / Game Launcher UI framework

Type: `BLOCKING_DECISION`

Blocks:

- Manager implementation;
- Launcher implementation;
- controller-focus UI test approach.

Required evidence:

- desktop window lifecycle;
- fullscreen/borderless presentation;
- multi-monitor/high-DPI;
- controller-first focus control;
- keyboard/mouse fallback;
- accessibility;
- rendering overhead in background Game Mode;
- predictable foreground/focus interaction;
- integration with selected runtime stack.

Do not choose only from visual preference.

---

## EDR-003 — Source/build/package orchestration

Type: `BLOCKING_DECISION`

Blocks:

- common repository build;
- packaging;
- test artifact identity;
- update package assembly.

Need to decide:

- project/solution build orchestration;
- version stamping;
- contract/schema generation;
- local developer bootstrap;
- deterministic artifact naming;
- service/desktop packaging;
- installer/setup provisioning development path.

---

## EDR-004 — Disposable Windows integration-test environment

Type: `BLOCKING_DECISION`

Blocks:

- automated destructive Windows integration tests;
- reliable IPC/service/session integration tests;
- component servicing tests;
- fault-injection automation.

Required evidence:

- supported Windows build reproducibility;
- snapshot/reset speed;
- console-session behavior;
- GPU/display limitations documented;
- ability to test Broker/service/install/reboot;
- CI or lab orchestration path.

Likely split:

```text
VM lane
→ service/persistence/update/reboot tests

physical hardware lane
→ display/controller/GPU/game/performance tests
```

---

## EDR-005 — Recovery Capsule physical container

Type: `BLOCKING_DECISION`

Blocks:

- production-shaped SLICE-09 recovery.

Semantic requirements already fixed:

```text
immutable/sealed payload
manifest/digest verification
previous release completeness
hidden Recovery Store
readable from normal maintenance and WinRE recovery context
no user-data rollback
```

Candidates may include:

- VHDX;
- WIM-like package;
- content-addressed directory/package;
- custom sealed archive/container.

Evaluation criteria:

- atomic creation/replacement;
- verification speed;
- disk overhead;
- WinRE access;
- partial corruption behavior;
- update simplicity;
- ACL/isolation;
- ability to preserve exact previous release artifacts.

---

## EDR-006 — WinRE custom recovery packaging

Type: `ENGINEERING_RESEARCH`

Blocks:

- offline Recovery Tool path.

Need to prove:

- how SplitOS recovery tool is provisioned/discovered;
- how it reads Recovery Store;
- what Windows updates do to WinRE customizations;
- how recovery tool version compatibility is maintained;
- how diagnostics can be exported safely from WinRE.

---

## EDR-007 — Supported default audio endpoint switching

Type: `ENGINEERING_RESEARCH`

Blocks only:

```text
automatic default-audio switching capability
```

Does not block:

- audio enumeration;
- default observation;
- Game Mode;
- user-mediated Sound Settings flow.

Exit criterion:

- supported public mechanism verified and maintainable, or;
- capability remains user-mediated for v1.

Undocumented PolicyConfig/registry/UI-automation hacks are not accepted as canonical by default.

---

## EDR-008 — PresentMon packaging model

Type: `ENGINEERING_RESEARCH`

Blocks:

- measured optimization telemetry;
- final performance measurement implementation details.

Need to choose/prove:

- embedded/library approach if supported;
- launched collector process;
- service/agent model;
- lifecycle under Game Session;
- permissions;
- overhead;
- data correlation with specific game process;
- redistribution/license obligations.

Static recommendation work remains unblocked.

---

## EDR-009 — First supported game/config-adapter set

Type: `BLOCKING_DECISION`

Blocks:

- concrete optimizer/config writer vertical.

Select a deliberately small initial set that provides useful variation, for example:

- one Steam game with straightforward config;
- one game with upscaler/frame-generation options if relevant;
- one game suitable for Desktop/TV profile comparison.

Selection criteria:

- stable config location/format;
- legal setting values identifiable;
- safe read/write/read-back;
- no anti-cheat-sensitive modification;
- realistic performance testing.

The selected titles become test fixtures/support scope, not a claim of broad game support.

---

## EDR-010 — Battle.net integration mechanism

Type: `ENGINEERING_RESEARCH`

Blocks only Battle.net support.

Need evidence for:

- client discovery;
- local install evidence;
- launch handoff;
- product identity;
- version stability;
- auth-required behavior.

Until then:

```text
Battle.net = EXPERIMENTAL
```

---

## EDR-011 — Microsoft Gaming capability matrix

Type: `ENGINEERING_RESEARCH`

Blocks broader Microsoft Gaming support.

Need to validate on real installed titles:

- package/PFN/AUMID discovery;
- app activation;
- Gaming Services dependencies;
- process correlation;
- game exit behavior;
- update/install races.

Do not expand this into a claim of full Game Pass cloud library ownership.

---

## EDR-012 — Shared App overlay feasibility

Type: `ENGINEERING_RESEARCH`

Blocks only `OVERLAY` support and possibly in-game panel presentation.

Need matrix across:

- borderless/windowed games;
- exclusive fullscreen where applicable;
- DWM behavior;
- anti-cheat-sensitive games;
- multi-monitor;
- focus behavior.

If ordinary HWND overlay cannot work safely/reliably:

```text
OVERLAY unsupported in that context
```

No injection fallback.

---

## EDR-013 — Global controller chord for in-game SplitOS panel

Type: `ENGINEERING_RESEARCH`

Blocks only in-game panel invocation.

Need to test conflicts with:

- Xbox Game Bar;
- Steam overlay;
- controller Guide/system buttons;
- DualSense/Xbox mappings;
- games that consume the same inputs.

Hidden Launcher must not consume ordinary gameplay input while unresolved.

---

## EDR-014 — Windows source automatic acquisition

Type: `ENGINEERING_RESEARCH`

Blocks only automatic source acquisition.

User-provided authorized Windows source remains the v1 supported fallback/baseline.

Need legal/licensing + technical evidence before adding automatic Microsoft source acquisition.

---

## EDR-015 — Windows Component Matrix risky removals

Type: `ENGINEERING_RESEARCH`

Independent subtracks:

```text
Defender REMOVE candidate
Edge browser REMOVE candidate
Xbox/Game Bar UX classifications
Search/Print mode-management rules
Phone Link mode-management rules
```

Each needs:

```text
mechanism
boot
OOBE
servicing
Windows Update
recovery
application/game compatibility
```

Do not block conservative Builder MVP while these run.

---

## EDR-016 — Performance budgets

Type: `BLOCKING_DECISION_BEFORE_PRODUCTION`

Does not block early prototypes.

Blocks SPEC-14 `GATE-09` production readiness.

Need measured thresholds for:

- Runtime Host idle CPU/RAM;
- Manager idle footprint where relevant;
- Launcher active/background CPU/GPU/RAM;
- WORK→GAME p95;
- GAME→WORK p95;
- managed launch overhead;
- gaming FPS/frame-time regression;
- diagnostics overhead;
- update/recovery time budgets if product requires them.

Budgets should be selected from representative lab evidence, not invented before implementation exists.

---

## EDR-017 — Production signing/HSM/CA/CDN providers

Type: `OPERATIONAL_SELECTION`

Blocks production release operations, not implementation of trust logic with test keys.

Selection must preserve SPEC-12 role separation:

- Root/Targets/Recovery ceremony boundaries;
- protected online delegated keys;
- non-exportable publisher signing key;
- scoped signing APIs;
- audit;
- rotation/revocation.

---

## EDR-018 — Backend hosting / account provider operational selection

Type: `OPERATIONAL_SELECTION`

Core contracts can be implemented against a chosen backend stack before final infrastructure vendor selection if deployment characteristics remain compatible.

Need to decide/prove:

- OAuth/OIDC provider model;
- account database;
- entitlement service;
- hosted checkout integration;
- release feed hosting boundary;
- privacy/retention requirements.

---

## Decision priority

### Close before implementation starts

```text
EDR-001 runtime stack
EDR-002 UI framework
EDR-003 build/package orchestration
EDR-004 test environment baseline
```

### Close before Gaming/Profile alpha

```text
EDR-009 first game set
EDR-008 PresentMon packaging if measured optimizer enters scope
```

### Close before resilient alpha

```text
EDR-005 Recovery Capsule container
EDR-006 WinRE packaging
```

### Close before production release

```text
EDR-016 performance budgets
EDR-017 signing/HSM/CA/CDN operations
required EDR-015 Component Matrix decisions for supported baseline
```

---

## Research artifact rule

Every research item should produce a short evidence record:

```text
Question
Environment/version
Prototype/mechanism
Observed behavior
Failure cases
Maintenance risk
Decision
Affected SPEC/backlog
```

If evidence contradicts an existing specification, update the SSAD knowledge chain before treating the new behavior as canonical.