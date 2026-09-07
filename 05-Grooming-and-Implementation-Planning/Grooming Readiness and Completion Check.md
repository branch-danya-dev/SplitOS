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

SPEC-01..13 already define semantic owners/process boundaries.

### Behavior understood

Status: `PASS`

States, flows, failures, trust and detailed contracts are already specified.

### Contracts sufficiently stable

Status: `PASS_WITH_IMPLEMENTATION_DECISIONS_PENDING`

Protocol semantics are stable; language/framework-specific bindings are not yet selected.

### Dependencies known

Status: `PASS`

See `Dependency and Critical Path.md`.

### Acceptance criteria testable

Status: `PASS`

SPEC-14 plus `QA and Acceptance Handoff.md` define acceptance semantics.

### Critical OPEN questions absent

Status: `PASS_AT_PROGRAM_LEVEL / FAIL_FOR_SLICE-00_UNTIL_EDR-001..004`

The unknowns are visible and assigned, not hidden.

---

## 3. Slice readiness table

| Slice | Status | Main reason |
|---|---|---|
| SLICE-00 Engineering Foundation | `BLOCKED_BY_DECISION` | EDR-001 runtime stack, EDR-002 UI, EDR-003 build, EDR-004 test environment |
| SLICE-01 Local State / FREE Skeleton | `CONDITIONALLY_READY` | can start immediately after stack/build baseline |
| SLICE-02 Account / Entitlement | `CONDITIONALLY_READY` | backend/provider implementation choices needed, contracts stable |
| SLICE-03 Mode Transaction | `CONDITIONALLY_READY` | depends on persistence + Broker skeleton |
| SLICE-04 Windows Context | `CONDITIONALLY_READY` | depends on stack + mode core; mechanisms specified |
| SLICE-05 Launcher + Steam MVP | `CONDITIONALLY_READY` | depends on UI, GameInput, mode and Steam vertical |
| SLICE-06 Profiles / Optimization | `CONDITIONALLY_READY` | first game/config-adapter set must be selected |
| SLICE-07 Shared Apps | `LATER_SCOPE` | core modes/gaming should stabilize first |
| SLICE-08 Builder | `CONDITIONALLY_READY` | can run parallel after packaging/build baseline |
| SLICE-09 Update / Recovery | `BLOCKED_BY_DECISION` | Recovery Capsule container + WinRE prototype required |
| SLICE-10 Release Trust | `CONDITIONALLY_READY` | trust code can use fixtures before production HSM selection |
| SLICE-11 Observability / RC | `CONDITIONALLY_READY` | base observability begins earlier; full gate automation later |

---

## 4. Immediate next actions

The first Grooming-to-Delivery handoff should close these four items:

```text
EDR-001  runtime implementation stack
EDR-002  Manager/Launcher UI framework
EDR-003  source/build/package orchestration
EDR-004  disposable Windows test environment
```

Once closed, issue the first delivery batch:

```text
IMP-003
IMP-004
IMP-005
IMP-020
IMP-021
IMP-022
IMP-023
IMP-024
IMP-025
IMP-027
IMP-028
IMP-029
IMP-160
```

The first desired demo is intentionally boring:

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

This proves the architecture before product breadth.

---

## 5. Questions explicitly not required before Slice 0

The following do **not** need to block first implementation:

- default audio setter;
- Battle.net support;
- full Microsoft Gaming matrix;
- Defender/Edge final removal classification;
- PresentMon packaging;
- overlay support;
- in-game global controller chord;
- automatic Windows source acquisition;
- production HSM/CDN provider;
- final numeric performance budgets.

They remain visible research/decision items and block only their relevant capabilities/gates.

---

## 6. Review checklist for the Grooming package

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
- [x] blocking engineering decisions are explicit;
- [x] optional research does not unnecessarily block the entire product.

### Implementation split

- [x] delivery slices are defined;
- [x] milestones are evidence-based rather than date-based;
- [x] first implementation backlog exists;
- [x] first issue batch is identified;
- [x] no story-point estimation was invented before team refinement.

### Acceptance / QA

- [x] each slice has minimum acceptance expectations;
- [x] happy/control/failure scenarios are distinguished;
- [x] Windows/hardware/fault/security test lanes are identified;
- [x] SPEC-14 remains production authority;
- [x] unsupported scope handling is explicit.

### Knowledge discipline

- [x] Grooming does not redefine architecture;
- [x] OPEN items are explicit work;
- [x] newly discovered contradictions must return to SSAD source layers;
- [x] implementation tickets are not allowed to become hidden source of truth.

---

## 7. Completion state of this Grooming pass

Program-level Grooming status:

```text
READY FOR REVIEW
```

Meaning:

- the team can review a coherent implementation split;
- critical dependencies and OPEN decisions are visible;
- Slice 0 is not yet `READY` until its four blocking decisions are closed;
- no missing decision is disguised as an implementation detail.

This is the intended outcome of Grooming.

---

## 8. Next SSAD lifecycle step

After the team accepts this decomposition and closes Slice 0 blockers:

```text
05 Grooming
↓
06 Delivery Support
```

Delivery Support should then operate against concrete implementation PRs/issues, helping resolve new evidence without allowing code to silently diverge from the documented system model.