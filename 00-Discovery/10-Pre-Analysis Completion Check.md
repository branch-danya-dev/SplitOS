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
- [x] Distribution/Builder и Account/Entitlement decisions перенесены в requirements baseline.
- [x] FREE/PRO runtime access и first-run identity model перенесены в requirements baseline.

## Scope

- [x] Initial in-scope определён.
- [x] Deferred scope определён.
- [x] Explicit out-of-scope определён.
- [x] MVP priority Game Mode определён.

## Unknowns

- [x] Technical assumptions не замаскированы под VERIFIED.
- [x] Performance thresholds остаются OPEN/TBD.
- [x] Offline entitlement TTL / device sharing / account cardinality остаются OPEN.
- [x] Windows Component Matrix и Defender/security baseline остаются subject to validation.
- [x] System-wide default audio switching остаётся OPEN до supported mechanism validation.
- [x] Epic/Xbox/Battle.net exact client integration mechanisms остаются OPEN/CANDIDATE.
- [x] Steam local metadata не объявлена stable public contract.
- [x] Exact retry/backoff/timeout numeric values остаются Specification-level unless product semantics require them.
- [x] Exact update package/snapshot/rollback technology остаётся OPEN.
- [x] OAuth/OIDC provider and Windows redirect strategy remain OPEN implementation decisions.
- [x] Offline entitlement assertion format/TTL/device binding/clock rollback remain OPEN but trust requirements are explicit.
- [x] Privileged Broker exact SDDL/token checks/service account remain OPEN but trust boundary is explicit.
- [x] Release manifest signature envelope/key hierarchy/rotation/revocation remain OPEN but lifecycle requirements are explicit.
- [x] Windows source provenance validation remains OPEN pending supported Microsoft acquisition model.
- [x] Hostile unrestricted local Administrator/kernel compromise is explicitly outside v1 SplitOS security guarantee.

## Traceability

- [x] Elicitation → Decision связь присутствует.
- [x] Decision → Concept/Requirements области определены.
- [x] Requirement → Boundaries / Responsibilities / Ownership / States / Behavior mapping зафиксирован.
- [x] Requirement → Data concepts mapping зафиксирован.
- [x] Requirement → Interface contracts mapping зафиксирован.
- [x] Requirement → Integration mechanisms mapping зафиксирован с VERIFIED/CANDIDATE/OPEN статусами.
- [x] Requirement → end-to-end Flows mapping зафиксирован для canonical v1 scenarios.
- [x] Requirement / Flow → Failure behavior mapping зафиксирован для runtime/update/recovery.
- [x] Requirement / Flow / Failure → Trust controls mapping зафиксирован.
- [ ] Requirement → Synthesis component mapping — следующий A&D layer.
- [ ] Requirement → Verification mapping — заполнить после Specification/QA.

---

## Current Analysis & Design status

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
Synthesis         NEXT
```

---

## Canonical trust coverage

```text
Windows platform authority assumptions
Interactive user session vs Privileged Broker boundary
Named Pipe ACL / caller SID-session validation candidate
bounded privileged capability protocol
no arbitrary admin command contract
SplitOS Account native-app authentication model
external browser + PKCE candidate
protected reusable token storage / DPAPI candidate
server-owned entitlement
bounded offline entitlement evidence
payment-provider evidence → backend → entitlement
release/build signing authority
Authenticode binary verification candidate
signed Build/Update Manifest requirement
artifact digest binding / protected staging
anti-downgrade authorization
key rotation / revocation lifecycle
Game Client metadata parsing/normalization
custom URI/browser callback distrust
external evidence authority/freshness rules
explicit local-admin/kernel threat limitation
```

Core trust chain:

```text
Claim / Request
→ Identity / Issuer
→ Integrity
→ Freshness
→ Context binding
→ Capability authorization
→ Semantic owner decision
→ Sensitive action
→ Verification
```

Trust failure for premium capability follows:

```text
cannot prove authorization
→ do not grant premium capability
→ preserve base Windows usability
```

---

## Canonical failure coverage

```text
Account/backend unavailable
Entitlement stale/ambiguous
Runtime Host crash
Privileged Broker unavailable/denied
Work→Game blocker/cancel/partial application
Display target unavailable/not verified
Mandatory mode policy failure
Rollback failure
Game Client missing/auth required
Stale installation projection
Client handoff rejected
Game start not confirmed
Game/game-client crash
Game→Work close/restore failure
Display/controller/audio disappearance
Major mutation conflicts
Update staging/apply/verification failure
Crash/reboot/power loss during update
Recovery verification failure
Manual recovery escalation
```

Safe convergence priority:

```text
User data integrity
→ Windows bootability/base usability
→ known coherent state
→ correct canonical SplitOS state
→ managed runtime restoration
→ UX convenience
```

---

## Conclusion

Pre-analysis завершён; Analysis & Design доведён до Trust layer и достаточен для финальной архитектурной синтезации.

Текущий reasoning path:

```text
Boundaries
    ↓
Responsibilities
    ↓
Ownership
    ↓
States
    ↓
Behavior
    ↓
Data
    ↓
Interfaces
    ↓
Integrations
    ↓
Flows
    ↓
Failures
    ↓
Trust
    ↓
Synthesis
```

Следующий layer должен собрать все предыдущие решения в единую implementable component/deployment architecture без переопределения уже установленных owners, states и trust boundaries.

Оставшиеся `OPEN` должны перейти в explicit Synthesis/Specification decision backlog и не превращаться в неявные implementation assumptions.