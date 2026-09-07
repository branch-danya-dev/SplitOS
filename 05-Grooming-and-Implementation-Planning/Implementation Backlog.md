# Implementation Backlog

## 1. Purpose

Provides the first delivery-oriented backlog derived from the stable SplitOS specifications.

This is not a sprint plan and contains no story-point estimates.

Backlog states:

```text
READY
CONDITIONALLY_READY
BLOCKED_BY_DECISION
BLOCKED_BY_RESEARCH
LATER_SCOPE
```

Each item must keep traceability to the owning SPEC when converted into issue-tracker work.

---

# EPIC-IMP-01 — Engineering Foundation

## Objective

Create the real process/build/test skeleton.

### IMP-001 — Select desktop/runtime implementation stack

Status: `BLOCKED_BY_DECISION`

Output:

- decision record;
- native Windows interop proof;
- service process proof;
- UI feasibility proof;
- packaging/signing/test fit.

### IMP-002 — Select Manager/Game Launcher UI framework

Status: `BLOCKED_BY_DECISION`

Must support:

- desktop Manager;
- fullscreen controller-first Launcher;
- deterministic focus;
- high-DPI/multi-display behavior;
- Windows accessibility/input integration.

### IMP-003 — Create product source/build skeleton

Status: `CONDITIONALLY_READY`

Depends on: `IMP-001`, `IMP-002`.

### IMP-004 — Add component version metadata contract

Status: `READY`

### IMP-005 — Add structured event envelope library

Status: `READY`

### IMP-006 — Establish unit/component test baseline

Status: `BLOCKED_BY_DECISION`

Depends on implementation stack.

---

# EPIC-IMP-02 — Runtime Host / Broker / IPC

## Objective

Prove process and privilege boundaries before real machine mutation.

### IMP-020 — Runtime Host per-session startup skeleton

Status: `CONDITIONALLY_READY`

### IMP-021 — Broker Windows Service installation/startup skeleton

Status: `CONDITIONALLY_READY`

### IMP-022 — Implement Broker per-session Named Pipe endpoint

Status: `READY_AFTER_IMP-020/021`

### IMP-023 — Implement Runtime UI Named Pipe endpoint

Status: `READY_AFTER_IMP-020`

### IMP-024 — Implement protocol hello/version negotiation

Status: `READY`

### IMP-025 — Broker caller PID/session validation

Status: `CONDITIONALLY_READY`

### IMP-026 — Physical console control-owner validation

Status: `CONDITIONALLY_READY`

### IMP-027 — Capability catalog dispatch skeleton

Status: `READY`

### IMP-028 — Reject generic command/path/service primitives

Status: `READY`

### IMP-029 — IPC idempotency/correlation baseline

Status: `READY`

---

# EPIC-IMP-03 — Persistence and Durable Transactions

## Objective

Provide canonical stores before significant orchestration.

### IMP-030 — Implement machine SQLite store

Status: `CONDITIONALLY_READY`

### IMP-031 — Implement user SQLite store

Status: `CONDITIONALLY_READY`

### IMP-032 — Implement projection cache store

Status: `CONDITIONALLY_READY`

### IMP-033 — Implement schema migration runner

Status: `READY_AFTER_IMP-030/031`

### IMP-034 — Implement SQLite backup/corruption fixture harness

Status: `READY_AFTER_IMP-030/031`

### IMP-035 — Implement ModeTransition durable schema

Status: `READY_AFTER_IMP-030`

### IMP-036 — Implement mutation lease/fencing schema

Status: `READY_AFTER_IMP-030`

### IMP-037 — Implement user profile/preferences repository

Status: `READY_AFTER_IMP-031`

---

# EPIC-IMP-04 — Account / Auth / Entitlement

## Objective

Deliver the real FREE/PRO product identity vertical.

### IMP-040 — Implement SplitOS Account backend skeleton

Status: `CONDITIONALLY_READY`

### IMP-041 — Implement native external-browser auth flow

Status: `READY_AFTER_STACK_DECISION`

### IMP-042 — Implement loopback callback + PKCE

Status: `READY_AFTER_IMP-041`

### IMP-043 — Implement Windows-user ↔ SplitOS-account association

Status: `READY_AFTER_IMP-031/041`

### IMP-044 — Implement protected refresh-token store

Status: `CONDITIONALLY_READY`

### IMP-045 — Implement entitlement endpoint/projection

Status: `READY_AFTER_IMP-040`

### IMP-046 — Implement FREE/PRO RuntimeAccess evaluator

Status: `READY_AFTER_IMP-045`

### IMP-047 — Implement bounded offline entitlement assertion validation

Status: `CONDITIONALLY_READY`

### IMP-048 — Implement hosted checkout refresh-trigger flow

Status: `LATER_WITHIN_EPIC`

---

# EPIC-IMP-05 — Mode Runtime

## Objective

Implement the canonical mode transaction semantics.

### IMP-050 — Implement OperationalModeState owner

Status: `READY_AFTER_IMP-030`

### IMP-051 — Implement ModeTransition state machine

Status: `READY_AFTER_IMP-035`

### IMP-052 — Implement ACTIVATE / SWITCH / DEACTIVATE commands

Status: `READY_AFTER_IMP-050/051`

### IMP-053 — Implement mutation lease acquisition/renew/release

Status: `READY_AFTER_IMP-036`

### IMP-054 — Implement fencing validation at Broker boundary

Status: `READY_AFTER_IMP-053`

### IMP-055 — Implement blocker provider contract

Status: `READY`

### IMP-056 — Implement user-decision blocker flow

Status: `READY_AFTER_MANAGER_SHELL`

### IMP-057 — Implement BASE / WORK / GAME policy representation

Status: `READY`

### IMP-058 — Implement commit/rollback/reconciliation logic

Status: `READY_AFTER_IMP-051/053`

### IMP-059 — Implement Runtime Host crash recovery tests

Status: `READY_AFTER_IMP-058`

---

# EPIC-IMP-06 — Windows Context Integrations

## Objective

Make mode targets observable and verifiable on real Windows.

### IMP-060 — Display snapshot/query adapter

Status: `READY_AFTER_STACK_DECISION`

### IMP-061 — Display target apply + read-back

Status: `READY_AFTER_IMP-060`

### IMP-062 — Persistent display selector resolver

Status: `CONDITIONALLY_READY`

### IMP-063 — Power read/apply/read-back adapter

Status: `READY_AFTER_STACK_DECISION`

### IMP-064 — Process evidence adapter

Status: `READY_AFTER_STACK_DECISION`

### IMP-065 — PnP hardware generation adapter

Status: `READY_AFTER_STACK_DECISION`

### IMP-066 — GameInput controller adapter

Status: `READY_AFTER_STACK_DECISION`

### IMP-067 — ManagedServiceId catalog + Broker SCM capability

Status: `CONDITIONALLY_READY`

### IMP-068 — Core Audio enumeration/default observation

Status: `CONDITIONALLY_READY`

### IMP-069 — Supported automatic default-audio mechanism research

Status: `BLOCKED_BY_RESEARCH`

---

# EPIC-IMP-07 — Game Launcher / Game Runtime

## Objective

Deliver the first controller-first GAME vertical.

### IMP-070 — Launcher process lifecycle and Runtime binding

Status: `READY_AFTER_UI_DECISION`

### IMP-071 — Controller semantic action/focus model

Status: `READY_AFTER_IMP-066/070`

### IMP-072 — Home/Library/Game Details navigation skeleton

Status: `READY_AFTER_IMP-070`

### IMP-073 — Game Library normalized model

Status: `READY`

### IMP-074 — Game Session state machine implementation

Status: `READY`

### IMP-075 — Process proof-set correlation engine

Status: `READY_AFTER_IMP-064`

### IMP-076 — Launcher background/restore behavior

Status: `READY_AFTER_IMP-074`

### IMP-077 — Launch/error/auth-required UX

Status: `READY_AFTER_IMP-070/074`

---

# EPIC-IMP-08 — Game Client Adapters

## Objective

Integrate external clients through the common adapter contract.

### IMP-080 — Adapter registry/common contract

Status: `READY`

### IMP-081 — Steam client discovery

Status: `CONDITIONALLY_READY`

### IMP-082 — Steam local library projection parser

Status: `CONDITIONALLY_READY`

### IMP-083 — Steam protocol launch handoff

Status: `CONDITIONALLY_READY`

### IMP-084 — Steam running/exit correlation fixtures

Status: `READY_AFTER_IMP-075/083`

### IMP-085 — Epic protocol activation adapter

Status: `LATER_AFTER_STEAM_VERTICAL`

### IMP-086 — Microsoft Gaming package/AUMID adapter

Status: `LATER_AFTER_STEAM_VERTICAL`

### IMP-087 — Battle.net research/prototype

Status: `BLOCKED_BY_RESEARCH`

---

# EPIC-IMP-09 — Game Profiles and Optimization

## Objective

Implement scenario-aware game intent and explainable optimization.

### IMP-090 — GameProfile user-store schema/repository

Status: `READY_AFTER_IMP-031`

### IMP-091 — Hardware/profile eligibility resolver

Status: `READY_AFTER_IMP-060/065/066`

### IMP-092 — Deterministic profile selection

Status: `READY_AFTER_IMP-091`

### IMP-093 — Field-level user override/lock model

Status: `READY`

### IMP-094 — GameOptimizationKnowledge schema

Status: `READY`

### IMP-095 — Recommendation engine baseline

Status: `READY_AFTER_IMP-094`

### IMP-096 — First per-game config adapter

Status: `BLOCKED_BY_GAME_SELECTION`

### IMP-097 — Config source-digest/write/read-back flow

Status: `READY_AFTER_IMP-096`

### IMP-098 — PresentMon packaging prototype

Status: `BLOCKED_BY_RESEARCH`

### IMP-099 — External config drift reconciliation UX

Status: `READY_AFTER_IMP-093/097`

---

# EPIC-IMP-10 — Shared Apps

## Objective

Provide bounded companion app presentation during GAME.

### IMP-100 — SharedAppAssignment model

Status: `READY`

### IMP-101 — top-level window evidence/resolver

Status: `READY_AFTER_STACK_DECISION`

### IMP-102 — BACKGROUND assignment

Status: `READY_AFTER_IMP-100`

### IMP-103 — SECONDARY_DISPLAY placement

Status: `READY_AFTER_IMP-101/062`

### IMP-104 — LOCKED_WINDOW placement

Status: `READY_AFTER_IMP-101`

### IMP-105 — drift/debounce/bounded retry

Status: `READY_AFTER_IMP-103/104`

### IMP-106 — OVERLAY capability prototype

Status: `BLOCKED_BY_RESEARCH`

### IMP-107 — in-game panel invocation prototype

Status: `BLOCKED_BY_RESEARCH`

---

# EPIC-IMP-11 — Builder / Distribution

## Objective

Produce a real prepared SplitOS Windows baseline.

### IMP-110 — SourceIdentity detector

Status: `CONDITIONALLY_READY`

### IMP-111 — BuildManifest JSON schema/validator

Status: `READY`

### IMP-112 — typed manifest executor skeleton

Status: `READY_AFTER_IMP-111`

### IMP-113 — offline WIM mount/commit lifecycle

Status: `CONDITIONALLY_READY`

### IMP-114 — provisioned AppX remove operation

Status: `CONDITIONALLY_READY`

### IMP-115 — optional feature/package operations

Status: `CONDITIONALLY_READY`

### IMP-116 — Component Matrix resolver

Status: `READY`

### IMP-117 — conservative accepted component subset lab

Status: `BLOCKED_BY_RESEARCH`

### IMP-118 — SplitOS package staging/setup provisioning

Status: `READY_AFTER_RUNTIME_PACKAGING`

### IMP-119 — BuildReceipt generation/verification

Status: `READY_AFTER_IMP-112/116`

### IMP-120 — automatic Microsoft source acquisition research

Status: `BLOCKED_BY_RESEARCH`

---

# EPIC-IMP-12 — Update / Recovery

## Objective

Make the installed product safely serviceable.

### IMP-130 — Release discovery/download client

Status: `CONDITIONALLY_READY`

### IMP-131 — UpdateTransaction persistence

Status: `READY_AFTER_IMP-030`

### IMP-132 — target release staging/verification

Status: `READY_AFTER_RELEASE_FORMAT`

### IMP-133 — Recovery Capsule container prototype

Status: `BLOCKED_BY_DECISION`

### IMP-134 — Recovery Capsule create/seal/verify

Status: `READY_AFTER_IMP-133`

### IMP-135 — Update Bootstrap activation flow

Status: `READY_AFTER_PACKAGING_DECISION`

### IMP-136 — reboot/resume reconciliation

Status: `READY_AFTER_IMP-131/135`

### IMP-137 — user-data-preserving rollback compatibility fixtures

Status: `READY_AFTER_MIGRATIONS`

### IMP-138 — WinRE Recovery Tool prototype

Status: `BLOCKED_BY_RESEARCH`

---

# EPIC-IMP-13 — Release Security

## Objective

Implement production-shaped release trust.

### IMP-140 — TUF metadata/client fixture repository

Status: `CONDITIONALLY_READY`

### IMP-141 — embedded Root / sequential root update logic

Status: `READY_AFTER_IMP-140`

### IMP-142 — delegated release/knowledge/recovery verification

Status: `READY_AFTER_IMP-140`

### IMP-143 — artifact digest + Authenticode verification

Status: `CONDITIONALLY_READY`

### IMP-144 — anti-rollback/security floor store

Status: `READY_AFTER_IMP-030`

### IMP-145 — RecoveryAuthorization evaluator

Status: `READY_AFTER_IMP-142/144`

### IMP-146 — key rotation/revocation fixture suite

Status: `READY_AFTER_IMP-140`

### IMP-147 — production HSM/CA/signing service selection

Status: `LATER_OPERATIONAL_DECISION`

---

# EPIC-IMP-14 — Observability / Diagnostics

## Objective

Make all critical flows supportable without making diagnostics authoritative.

### IMP-150 — rotating NDJSON sink

Status: `READY_AFTER_STACK_DECISION`

### IMP-151 — security audit sink/policy

Status: `READY_AFTER_IMP-150`

### IMP-152 — correlation context propagation

Status: `READY`

### IMP-153 — ETW/TraceLogging provider baseline

Status: `CONDITIONALLY_READY`

### IMP-154 — WER LocalDumps setup

Status: `CONDITIONALLY_READY`

### IMP-155 — diagnostic bundle builder

Status: `READY_AFTER_CORE_DIAGNOSTIC_VIEWS`

### IMP-156 — redaction/pseudonymization engine

Status: `READY`

### IMP-157 — synthetic secret leakage fixture suite

Status: `READY_AFTER_IMP-156`

---

# EPIC-IMP-15 — Verification Infrastructure

## Objective

Make acceptance evidence executable from the beginning.

### IMP-160 — VerificationCase/result schema

Status: `READY`

### IMP-161 — ReleaseAcceptanceProfile schema

Status: `READY`

### IMP-162 — Windows disposable integration-test environment

Status: `BLOCKED_BY_TEST_ENV_DECISION`

### IMP-163 — hardware lab inventory/matrix model

Status: `CONDITIONALLY_READY`

### IMP-164 — Runtime/Broker IPC negative test harness

Status: `READY_AFTER_EPIC-IMP-02`

### IMP-165 — mode crash checkpoint harness

Status: `READY_AFTER_EPIC-IMP-05`

### IMP-166 — update power-loss/fault harness

Status: `READY_AFTER_EPIC-IMP-12`

### IMP-167 — performance benchmark harness

Status: `BLOCKED_BY_PERFORMANCE_DECISION`

### IMP-168 — ReleaseReadinessRecord generator

Status: `READY_AFTER_GATE_AUTOMATION`

---

## First backlog cut recommended for implementation

The first issue batch should be intentionally small:

```text
IMP-001   stack decision
IMP-002   UI framework decision
IMP-003   source/build skeleton
IMP-004   version metadata
IMP-005   event envelope
IMP-020   Runtime Host skeleton
IMP-021   Broker Service skeleton
IMP-022   Broker pipe
IMP-023   UI pipe
IMP-024   protocol hello
IMP-025   caller validation
IMP-027   capability dispatch
IMP-028   generic capability rejection tests
IMP-029   correlation/idempotency
IMP-160   verification result schema
```

This batch produces a secure process skeleton without prematurely implementing product breadth.

---

## Backlog change rule

If an implementation item discovers a missing semantic answer:

```text
Implementation evidence/question
→ owning SSAD layer
→ update decision/spec if necessary
→ return to Grooming
→ revise task
```

Do not solve cross-system ambiguity only in ticket comments or code.