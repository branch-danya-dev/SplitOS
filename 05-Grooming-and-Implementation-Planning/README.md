# SplitOS — Grooming & Implementation Planning

## 1. Purpose

This stage converts the reviewed product/architecture/specification baseline into delivery work that developers, QA and analysts can understand in the same way.

It follows the SSAD Grooming question:

> Do analyst, developers, QA and other delivery participants share the same understanding of scope, behavior, dependencies and readiness criteria?

Grooming does **not** create a new architecture layer.

```text
Discovery / Decisions / Requirements
→ Analysis & Design
→ Detailed Specification
→ Grooming & Implementation Planning
→ Delivery Support
→ Verification Execution
```

If implementation planning exposes a missing product/system answer, that answer must be returned to the owning Decision / Requirement / A&D / Specification artifact instead of being hidden inside a task.

---

## 2. Source of truth

This package is downstream from:

```text
00-Discovery
01-Concept
02-Requirements
03-Analysis-and-Design
04-Specification / SPEC-01..14
```

Normative system semantics continue to live in those layers.

Grooming owns:

- implementation scope decomposition;
- delivery slices;
- dependency ordering;
- project/repository topology proposal;
- first implementation backlog;
- engineering decision/research queue;
- QA/acceptance handoff;
- readiness status.

It does not own:

- new product requirements;
- alternative state semantics;
- new trust rules;
- silent changes to contracts;
- support claims that conflict with SPEC-14.

---

## 3. Grooming outputs

```text
05-Grooming-and-Implementation-Planning/
├── README.md
├── Implementation Scope and End-to-End Walkthrough.md
├── Repository and Project Topology.md
├── Delivery Slices and Milestones.md
├── Implementation Backlog.md
├── Dependency and Critical Path.md
├── Engineering Decisions and Research Queue.md
├── QA and Acceptance Handoff.md
├── Grooming Readiness and Completion Check.md
├── solution-topology.mmd
├── delivery-roadmap.mmd
└── dependency-map.mmd
```

---

## 4. Program-level implementation objective

The implementation program is not considered successful merely when isolated executables exist.

The first meaningful product path is:

```text
prepared Windows baseline
→ clean install / Windows OOBE
→ Windows user sign-in
→ SplitOS Runtime Host starts
→ SplitOS Account flow
→ FREE Windows desktop remains usable
→ PRO entitlement can activate managed runtime
→ WORK / GAME transition is verified
→ Game Launcher becomes usable
→ one supported game launches through a supported client
→ game exit returns to Launcher
→ machine can update and recover without losing user data
```

Delivery slices intentionally reach this path incrementally.

---

## 5. Current stable implementation boundaries

Physical process topology is already specified:

```text
Windows machine
│
├── SplitOS.Broker.Service.exe          one machine service
│
└── eligible Windows user session
    ├── SplitOS.RuntimeHost.exe         one per session
    ├── SplitOS.Manager.exe             0..1
    └── SplitOS.GameLauncher.exe        0..1
```

Additional product artifacts include:

```text
SplitOS.UpdateBootstrap.exe
SplitOS Recovery Tool
SplitOS Media Builder
SplitOS Account / Entitlement backend
SplitOS Release / Update repository metadata
versioned release knowledge and adapters
```

Grooming may propose source-project boundaries for these artifacts, but cannot merge semantic owners just because they are physically hosted in the same process/project.

---

## 6. Delivery principles

### 6.1 Vertical evidence before breadth

Prefer one working end-to-end path over many disconnected stubs.

Example:

```text
Runtime Host
→ Broker secure hello
→ one typed capability
→ actual Windows read-back
→ semantic verification
```

is more useful than implementing twenty unverified Windows wrappers.

### 6.2 Safety semantics travel with implementation

Transaction durability, verification, rollback, trust and diagnostics are not postponed as a final hardening phase when they are necessary to prove the slice.

A slice may intentionally use a reduced capability set, but it must preserve the final architecture's safety direction.

### 6.3 Unsupported is better than fake support

If a client/device/feature is not ready:

```text
mark unsupported / experimental
```

not:

```text
ship incomplete behavior behind a hidden assumption
```

### 6.4 OPEN means visible work

An OPEN that blocks implementation becomes an explicit Engineering Decision / Research item with:

```text
owner
question
required evidence
blocked slice
exit criterion
```

### 6.5 QA starts with the slice

Each delivery slice names acceptance evidence before development starts.

SPEC-14 remains the production release gate model; Grooming only selects which verification cases become mandatory for each incremental milestone.

---

## 7. Proposed delivery tracks

The program is easier to execute as coordinated tracks rather than one serial list.

```text
TRACK A — Runtime Foundation
Runtime Host / Broker / IPC / persistence / mode orchestration

TRACK B — Windows & Gaming Experience
Windows adapters / Game Launcher / clients / profiles / optimization

TRACK C — Distribution & Lifecycle
Builder / component matrix / install / update / recovery

TRACK D — Product Services & Trust
Account / entitlement / release metadata / signing / compatibility knowledge

TRACK E — Verification Infrastructure
CI fixtures / Windows integration tests / hardware lab / fault injection / diagnostics
```

Tracks can progress in parallel, but the critical-path dependencies are explicit in `Dependency and Critical Path.md`.

---

## 8. Technology choices not yet silently fixed

The current specification fixes Windows mechanisms and contracts, but does not appear to canonically fix all implementation technologies.

Therefore this Grooming package does **not** silently declare:

- desktop implementation language/runtime;
- Manager/Game Launcher UI framework;
- concrete test framework;
- concrete CI provider;
- HSM/CA/CDN vendors;
- Recovery Capsule container technology;
- exact PresentMon packaging model;
- unsupported public default-audio setter.

These are explicit items in `Engineering Decisions and Research Queue.md`.

Where a slice depends on one of them, its readiness is conditional until the decision has sufficient evidence.

---

## 9. Program milestones

```text
M0 — Engineering Skeleton
processes build and communicate on developer Windows

M1 — Prepared Baseline Boots
Builder produces a validated lab baseline that clean-installs and starts SplitOS skeleton

M2 — FREE Product Vertical
Windows user → SplitOS Account → FREE → usable Windows desktop

M3 — Managed Mode Vertical
PRO → ACTIVATE WORK → SWITCH GAME → verified commit → return WORK

M4 — Gaming MVP
GAME → Launcher → Steam-supported game → GAME_RUNNING → exit → Launcher

M5 — Profile/TV Alpha
Desktop/TV profiles + deterministic device resolution + game config recommendation subset

M6 — Resilient Alpha
crash reconciliation + local update + previous-release Recovery Capsule + rollback preserving user data

M7 — Release Candidate Infrastructure
release trust/signing + observability + acceptance automation + support matrix
```

These milestones describe evidence progression, not dates.

---

## 10. Grooming status model

Each slice/task can be:

```text
READY
CONDITIONALLY_READY
BLOCKED_BY_DECISION
BLOCKED_BY_RESEARCH
LATER_SCOPE
```

A task is not READY just because a ticket can be written.

Minimum Ready meaning:

```text
Purpose understood
Scope defined
Affected owners known
Behavior understood
Contracts sufficiently stable
Dependencies known
Acceptance criteria testable
Critical OPEN questions absent
```

---

## 11. Current program readiness

At this stage:

```text
Architecture / Specification baseline     READY FOR DELIVERY PLANNING
Program decomposition                     READY FOR REVIEW
Slice 0 engineering foundation            READY / decision-driven
Later slices                               dependency-gated
Production acceptance                     defined by SPEC-14
```

The first implementation activity should not be a random UI screen or a full Windows image modification.

It should close the minimum engineering decisions required by Slice 0 and prove the process/security skeleton end to end.

---

## 12. Next lifecycle step after Grooming

Once the implementation split is agreed and blocking decisions for a slice are closed:

```text
Groomed slice
→ implementation task set
→ Delivery Support
→ implementation evidence
→ Verification execution
→ knowledge update when evidence changes the model
```

This package should therefore be treated as a live planning boundary, not a replacement for issue tracking.