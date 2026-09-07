# Grooming Readiness and Completion Check

## 1. Purpose

Evaluates whether the implementation program and individual delivery slices are sufficiently understood to begin delivery without hiding critical ambiguity.

This follows the SSAD Grooming readiness meaning:

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

## 2. Program-level readiness

### Purpose understood

Status: `PASS`

The program objective is explicit:

```text
prepared SplitOS baseline
→ FREE usable product
→ PRO managed WORK/GAME runtime
→ gaming vertical
→ safe update/recovery
→ production acceptance
```

### Scope defined

Status: `PASS`

`Implementation Scope and End-to-End Walkthrough.md` defines v1 scope and explicit non-goals.

### Affected owners known

Status: `PASS`

SPEC-01..13 define semantic owners/process boundaries.

### Behavior understood

Status: `PASS`

States, flows, failures, trust and detailed contracts are specified.

### Contracts sufficiently stable

Status: `PASS_FOR_SLICE-00`

Protocol semantics were already stable and the implementation bindings are now selected by EDR-001..004.

### Dependencies known

Status: `PASS`

See `Dependency and Critical Path.md`.

### Acceptance criteria testable

Status: `PASS`

SPEC-14 plus `QA and Acceptance Handoff.md` define acceptance semantics.

### Critical OPEN questions absent

Status: `PASS_FOR_SLICE-00`

The implementation stack, UI framework, build/package orchestration and disposable Windows integration environment are closed decisions.

Open research still exists for later capabilities, but none of it blocks the first code slice.

---

## 3. Slice readiness table

| Slice | Status | Main reason |
|---|---|---|
| SLICE-00 Engineering Foundation | `READY_FOR_DELIVERY` | EDR-001..004 closed; code handoff exists |
| SLICE-01 Local State / FREE Skeleton | `CONDITIONALLY_READY` | starts after Slice-0 process/build skeleton |
| SLICE-02 Account / Entitlement | `CONDITIONALLY_READY` | contracts stable; provider/backend implementation choices can be refined in slice |
| SLICE-03 Mode Transaction | `CONDITIONALLY_READY` | depends on persistence + Broker skeleton |
| SLICE-04 Windows Context | `CONDITIONALLY_READY` | depends on mode core; mechanisms specified |
| SLICE-05 Launcher + Steam MVP | `CONDITIONALLY_READY` | UI stack closed; still depends on mode and Steam vertical |
| SLICE-06 Profiles / Optimization | `CONDITIONALLY_READY` | first game/config-adapter set must be selected |
| SLICE-07 Shared Apps | `LATER_SCOPE` | core modes/gaming should stabilize first |
| SLICE-08 Builder | `CONDITIONALLY_READY` | can run parallel after Slice-0 build/publish skeleton |
| SLICE-09 Update / Recovery | `BLOCKED_BY_DECISION` | Recovery Capsule container + WinRE prototype required |
| SLICE-10 Release Trust | `CONDITIONALLY_READY` | trust code can use fixtures before production HSM selection |
| SLICE-11 Observability / RC | `CONDITIONALLY_READY` | base observability starts in Slice 0; full gate automation later |

---

## 4. Closed Slice-0 decisions

```text
EDR-001  C# / .NET 10 LTS, x64-first, self-contained Windows executables
EDR-002  WinUI 3 / Windows App SDK stable 2.4.x, unpackaged+self-contained UI
EDR-003  monorepo + dotnet/MSBuild + pinned dependencies + GitHub Actions lanes
EDR-004  Hyper-V Gen2 disposable VMs + differencing disks + PowerShell Direct + physical lab
```

Canonical decision records:

```text
Engineering-Decisions/
├── EDR-001 Implementation Stack.md
├── EDR-002 UI Framework.md
├── EDR-003 Source Build Packaging Orchestration.md
├── EDR-004 Windows Integration Test Environment.md
└── SLICE-00 Delivery Handoff.md
```

---

## 5. Immediate next action — start implementation

The next branch should be code-oriented:

```text
delivery/slice-00-engineering-foundation
```

First implementation batch:

```text
IMP-003   repository/source build skeleton
IMP-004   component version metadata
IMP-005   structured event envelope
IMP-020   Runtime Host skeleton
IMP-021   Broker Service skeleton
IMP-022   Broker Named Pipe
IMP-023   UI Runtime pipe
IMP-024   protocol hello/versioning
IMP-025   caller/session validation
IMP-027   capability dispatch
IMP-028   generic privileged-surface rejection tests
IMP-029   correlation/idempotency
IMP-160   verification result schema
```

The first desired demo remains intentionally boring:

```text
RuntimeHost.exe running as user
Broker Service running separately
Manager connects only to Runtime
Launcher connects only to Runtime
Runtime securely connects to Broker
Broker knows actual caller/session
unsupported capability denied
one read-only health capability succeeds
all events share correlation/version metadata
```

This proves the architecture in running code before product breadth.

---

## 6. Questions explicitly not required before Slice 0

The following do **not** block first implementation:

- default audio setter;
- Battle.net support;
- full Microsoft Gaming matrix;
- Defender/Edge final removal classification;
- PresentMon packaging;
- overlay support;
- in-game global controller chord;
- automatic Windows source acquisition;
- production HSM/CDN provider;
- final numeric performance budgets;
- Recovery Capsule physical container;
- final WinRE recovery runtime packaging.

They remain visible research/decision items and block only their relevant later capabilities/gates.

---

## 7. Review checklist

### System context

- [x] product implementation objective is explicit;
- [x] v1 scope is explicit;
- [x] non-goals are explicit;
- [x] major system walkthrough is documented;
- [x] affected process/component boundaries are explicit.

### Dependencies

- [x] critical paths are documented;
- [x] parallel tracks are identified;
- [x] hard vs contract vs evidence dependencies are distinguished;
- [x] Slice-0 blocking engineering decisions are closed;
- [x] optional research does not unnecessarily block the entire product.

### Implementation split

- [x] delivery slices are defined;
- [x] milestones are evidence-based rather than date-based;
- [x] implementation backlog exists;
- [x] first code batch is identified;
- [x] implementation stack is selected;
- [x] UI framework is selected;
- [x] build/package baseline is selected;
- [x] Windows integration lab baseline is selected.

### Acceptance / QA

- [x] each slice has minimum acceptance expectations;
- [x] happy/control/failure scenarios are distinguished;
- [x] Windows/hardware/fault/security test lanes are identified;
- [x] SPEC-14 remains production authority;
- [x] Slice-0 positive and negative IPC/security acceptance is explicit.

### Knowledge discipline

- [x] Grooming does not redefine architecture;
- [x] OPEN later-scope items remain explicit work;
- [x] newly discovered contradictions must return to SSAD source layers;
- [x] implementation tickets/code are not allowed to become hidden source of truth.

---

## 8. Completion state

Program-level Grooming status:

```text
READY
```

Slice-0 status:

```text
READY_FOR_DELIVERY
```

Meaning:

- the delivery team has a coherent first implementation scope;
- implementation technologies needed by Slice 0 are selected;
- dependencies and acceptance are explicit;
- no remaining planning document is required before the first code PR.

---

## 9. Lifecycle transition

```text
05 Grooming
↓
Slice 0 READY
↓
06 Delivery Support
↓
IMPLEMENTATION CODE
```

Delivery Support now works alongside concrete source PRs/issues. If implementation evidence contradicts a semantic decision, the change returns to Decision/Requirement/A&D/Specification instead of silently diverging in code.
