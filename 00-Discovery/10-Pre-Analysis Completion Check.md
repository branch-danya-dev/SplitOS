# SplitOS — Pre-analysis Completion Check

## Problem

- [x] Исходный запрос сохранён.
- [x] Проблема отделена от предлагаемого решения.
- [x] Product goal сформулирован.
- [x] Work и Game goals разделены.

## Context

- [x] As-Is модель зафиксирована.
- [x] Основные pain points описаны.
- [x] Внешние системы определены.
- [x] Initial change surface определён.

## Stakeholders

- [x] Primary user определён.
- [x] Product authority определён.
- [x] External authorities определены.
- [x] Engineering knowledge gaps определены.

## Requirements discovery

- [x] Ключевые продуктовые вопросы заданы.
- [x] Ответы сохранены в Elicitation Log.
- [x] Решения отделены от assumptions.
- [x] Superseded assumptions помечены.
- [x] Distribution/Builder decisions перенесены в requirements baseline.
- [x] Account/Entitlement/FREE-PRO decisions перенесены в requirements baseline.

## Scope

- [x] Initial in-scope определён.
- [x] Deferred scope определён.
- [x] Explicit out-of-scope определён.
- [x] MVP priority Game Mode определён.

---

## Analysis & Design completeness

```text
Boundaries        ✅
Responsibilities  ✅
Ownership         ✅
States            ✅
Behavior          ✅
Data              ✅
Interfaces        ✅
Integrations      ✅
Flows             ✅
Failures          ✅
Trust             ✅
Synthesis         ✅
```

```text
Analysis & Design baseline = COMPLETE
Next phase = Detailed Specification
```

---

## Canonical architecture now synthesized

### Build

```text
Authorized Windows source
→ Source validation
→ Signed/versioned Build Manifest
→ Typed supported transformations
→ Baseline verification
→ Prepared SplitOS baseline
```

### Runtime

```text
Windows User Session
├── SplitOS Manager / First Run
├── Game Launcher
└── Runtime Host
        ├── runtime semantic coordinators
        ├── Windows adapters
        ├── Game Client adapters
        ├── local state/transaction stores
        ├── HTTPS → Account Backend
        └── authenticated/authorized IPC
                 ↓
         Privileged Broker
                 ↓
         bounded privileged Windows operations
```

### Product access

```text
FREE
→ ManagedRuntime=DISABLED
→ OperationalMode=NONE
→ normal Windows Desktop

PRO
→ ManagedRuntime=ENABLED
→ WORK xor GAME
```

---

## Canonical component synthesis

- [x] Build Plane defined.
- [x] Manager / First Run UX defined.
- [x] Game Launcher defined.
- [x] Runtime Host orchestration responsibilities defined.
- [x] Mode/access/game/update/recovery logical modules mapped.
- [x] Windows adapter families mapped.
- [x] Game Client adapter layer mapped.
- [x] Privileged Broker boundary mapped.
- [x] Local persistence classes mapped.
- [x] Account/Auth/Entitlement backend mapped.
- [x] Release/Build trust domain mapped.
- [x] External authorities remain explicitly external.

---

## State / flow / failure invariants

- [x] `WORK xor GAME` applies only when managed runtime is enabled.
- [x] FREE stable state may have `OperationalMode=NONE`.
- [x] User intent, committed mode, transition lifecycle and game-session lifecycle are distinct.
- [x] Source mode remains canonical before verified target commit.
- [x] `HANDOFF_ACCEPTED != GAME_RUNNING`.
- [x] Game exit does not imply Game→Work.
- [x] Direct managed launch from Work composes Work→Game + Managed Launch.
- [x] Mode Transition / Update / Recovery conflicting mutations require coordination.
- [x] Technical operation success never replaces semantic verification.
- [x] Rollback/recovery require verification.
- [x] Mixed partial actual state is not a new `HYBRID` mode.

---

## Trust/security baseline

- [x] Interactive UX is separated from privileged machine mutation.
- [x] Privileged Broker exposes bounded capabilities, not arbitrary admin command execution.
- [x] Runtime→Broker caller/context authorization is required.
- [x] SplitOS Account is distinct from Windows identity.
- [x] Entitlement is distinct from payment evidence and local settings.
- [x] Premium access cannot be granted by an editable local flag.
- [x] Native auth external-browser + PKCE candidate recorded.
- [x] Protected reusable token storage / DPAPI candidate recorded.
- [x] Offline PRO requires bounded verifiable evidence if supported.
- [x] Build/update artifacts/manifests require provenance/integrity validation.
- [x] Valid old signature alone does not authorize downgrade.
- [x] External Game Client/browser/device metadata cannot directly authorize privileged actions.
- [x] Hostile unrestricted local Administrator/kernel/firmware compromise is outside v1 guarantee.

---

## Data/persistence synthesis

- [x] BACKEND_CANONICAL data separated from local data.
- [x] User-scoped and machine-scoped canonical state separated.
- [x] Durable transition/update/recovery journals identified.
- [x] External projection cache separated from source authority.
- [x] Ephemeral hardware/device evidence separated from user preferences.
- [x] Protected secrets separated from normal configuration/logs.
- [x] Diagnostics remain evidence rather than canonical state.
- [x] InstalledBaselineIdentity changes only after verification.

---

## Traceability

- [x] Elicitation → Decision.
- [x] Decision → Concept/Requirements.
- [x] Requirement → Boundaries / Responsibilities / Ownership / States / Behavior.
- [x] Requirement → Data.
- [x] Requirement → Interface contracts.
- [x] Requirement → Integration mechanisms.
- [x] Requirement → end-to-end Flows.
- [x] Requirement / Flow → Failure behavior.
- [x] Requirement / Flow / Failure → Trust controls.
- [x] Requirement → Synthesis component mapping.
- [ ] Synthesis component → Detailed Specification ID — next phase.
- [ ] Specification → Verification/Acceptance case — next phase.

---

## Remaining OPEN engineering decisions

These are explicit Specification/research backlog, not hidden architecture assumptions:

- [ ] local persistence technology/schema/migrations;
- [ ] exact Runtime Host physical module/package structure;
- [ ] IPC protocol/serialization/SDDL/caller validation/service account;
- [ ] OAuth/OIDC provider and redirect mechanism;
- [ ] offline entitlement assertion format/TTL/device binding/clock rollback;
- [ ] release manifest envelope/key hierarchy/rotation/revocation;
- [ ] update package/snapshot/rollback technology;
- [ ] supported system-wide default audio endpoint switching mechanism;
- [ ] exact Epic/Xbox/Battle.net integration mechanisms;
- [ ] stable Steam local metadata strategy;
- [ ] Windows Component Matrix empirical `REMOVE/DISABLE/MODE_MANAGED/KEEP` classification;
- [ ] Microsoft-authorized Windows source acquisition/provenance model;
- [ ] performance thresholds and observability retention;
- [ ] exact major-mutation coordination primitive;
- [ ] multi-user/Fast User Switching support level for v1.

---

## Specification handoff

Canonical handoff:

```text
03-Analysis-and-Design/11-Synthesis/Specification Handoff.md
```

Recommended next specifications:

```text
SPEC-01 Runtime Process & Module
SPEC-02 Local IPC & Privileged Broker
SPEC-03 Local Data & Persistence
SPEC-04 Account/Auth/Entitlement
SPEC-05 Mode Runtime
SPEC-06 Windows Context Integrations
SPEC-07 Game Client Adapters
SPEC-08 Game Profile & Optimization
SPEC-09 Game Launcher & Shared Apps UX
SPEC-10 Builder & Component Matrix
SPEC-11 Update & Recovery
SPEC-12 Release Security & Key Management
SPEC-13 Observability & Diagnostics
SPEC-14 Verification & Acceptance
```

---

## Conclusion

Pre-analysis и текущий Analysis & Design cycle завершены.

```text
Discovery / Concept / Requirements
        ↓
Analysis & Design
        ↓
Synthesis COMPLETE
        ↓
Detailed Specification
        ↓
Grooming / Delivery
        ↓
Verification
```

Если detailed engineering обнаруживает новое evidence, которое противоречит текущей модели, необходимо обновить owning A&D layer и затем Synthesis. Implementation не должен становиться скрытым источником архитектурной истины.