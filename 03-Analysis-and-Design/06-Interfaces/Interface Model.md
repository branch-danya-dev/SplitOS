# SplitOS — Interface Model

## 1. Purpose

Документ определяет каноническую модель интерфейсов SplitOS между responsibility/ownership zones и внешними authority boundaries.

Interface отвечает на вопрос:

```text
Как одна зона запрашивает действие, получает факт,
публикует результат или предоставляет evidence другой зоне,
не присваивая себе ownership чужого знания?
```

На этом этапе **не фиксируются** конкретные технологии транспорта.

Интерфейс может впоследствии быть реализован как:

- in-process contract;
- IPC;
- local RPC;
- event/message boundary;
- Windows API invocation boundary;
- file/config contract;
- HTTP/API contract;
- adapter к внешнему client/platform API;
- другой подходящий mechanism.

Главное правило:

> Interface != Integration != Flow.

Interface определяет контракт взаимодействия. Integration позже определит конкретное соединение с системой/технологией. Flow свяжет несколько интерфейсов в end-to-end сценарий.

---

# 2. Interface design principles

## IF-RULE-001 — Contract follows ownership

Consumer не должен напрямую изменять canonical state другого owner.

Правильно:

```text
Game Mode UX
→ RequestModeChange(GAME)
→ Mode Intent & Active Mode State
```

Неправильно:

```text
Game Mode UX
→ write currentMode = GAME
```

---

## IF-RULE-002 — Request does not imply success

Любой command/request должен отделяться от подтверждённого результата.

```text
Request
→ Accepted | Rejected
→ Execution
→ Verification
→ Result
```

Особенно это важно для:

- mode transition;
- Windows/display/audio/input operations;
- game launch;
- update;
- recovery.

---

## IF-RULE-003 — Desired state is not actual state

Контракт, который просит применить состояние, не должен автоматически возвращать success только потому, что operation была отправлена.

Минимальная семантика:

```text
Desired state
→ operation requested
→ actual evidence collected
→ verification result
```

---

## IF-RULE-004 — Evidence contract does not transfer authority

Если Windows или внешний Game Client сообщает состояние, интерфейс переносит evidence, но не ownership.

```text
Steam reports installed
→ GameInstallationProjection updated
```

Steam остаётся authority installation truth.

---

## IF-RULE-005 — Entitlement gates capabilities, not Windows login

Account/entitlement interfaces не входят в Windows authentication authority.

Backend failure не может означать:

```text
Windows session denied
```

Допустимый результат:

```text
ManagedRuntimeAccessDecision = DISABLED | DEGRADED | ENABLED
```

в соответствии с offline policy.

---

## IF-RULE-006 — Interface owns meaning, not transport

Идентификатор интерфейса закрепляет semantic contract.

Например:

```text
IF-MODE-001 Request Operational Mode
```

не означает автоматически:

```text
POST /api/mode
```

Транспорт выбирается позже.

---

## IF-RULE-007 — Contract versioning follows semantic compatibility

Изменение внутренней реализации не требует новой semantic version интерфейса, если входы/результаты/гарантии остаются совместимыми.

Breaking semantic change должен быть явно версионирован или согласован через compatibility policy.

---

# 3. Interface categories

## 3.1 Command interface

Consumer просит owner выполнить действие.

```text
RequestModeChange
ApplyDisplayIntent
LaunchManagedGame
StartUpdate
```

Command не даёт consumer права напрямую писать canonical state.

---

## 3.2 Query interface

Consumer читает canonical state или projection через owner.

```text
GetCurrentMode
GetManagedRuntimeAccess
GetEffectiveGameProfile
GetLibraryProjection
```

---

## 3.3 Evidence interface

Поставляет фактические observations из Windows/device/external system.

```text
DisplayActualStateEvidence
ProcessStateEvidence
GameClientLibraryEvidence
```

---

## 3.4 Event interface

Owner публикует значимое изменение состояния для consumers.

```text
OperationalModeCommitted
EntitlementChanged
GameSessionChanged
HardwareContextInvalidated
```

Event должен описывать уже произошедший fact, а не скрытый command.

---

## 3.5 Transaction coordination interface

Используется там, где один scenario объединяет несколько owners и требует commit/rollback semantics.

```text
Mode Transition
Update
Recovery
```

Coordinator не становится owner всех участвующих данных.

---

# 4. Canonical contract template

Каждый значимый интерфейс должен иметь:

```text
Interface ID
Name
Category
Provider / semantic owner
Consumers
Purpose
Preconditions
Input
Output
Errors / rejection reasons
Temporal semantics
Idempotency expectations
Verification semantics
Data ownership notes
Security/trust notes where relevant
Versioning notes
```

Не все поля должны быть технически детализированы сейчас, но смысл должен быть определён.

---

# 5. Interface families

## 5.1 Identity & Runtime Access

```text
IF-ID-001   Resolve SplitOS Account Context
IF-ID-002   Resolve Entitlement
IF-ACCESS-001 Evaluate Managed Runtime Access
IF-ACCESS-002 Runtime Access Changed
```

---

## 5.2 Mode & Transition

```text
IF-MODE-001 Request Operational Mode
IF-MODE-002 Read Committed Operational Mode
IF-MODE-003 Operational Mode Committed
IF-TRANS-001 Start Mode Transition
IF-TRANS-002 Resolve Transition Blocker
IF-TRANS-003 Read Transition Status
IF-TRANS-004 Transition Completed
```

---

## 5.3 Policy & Application Lifecycle

```text
IF-POLICY-001 Resolve Effective Mode Policy
IF-APP-001 Resolve Application Classification
IF-APP-002 Resolve Application Lifecycle Intent
IF-APP-003 Report Application Actual State
```

---

## 5.4 System Context

```text
IF-DISPLAY-001 Resolve Desired Display Context
IF-DISPLAY-002 Apply Display Intent
IF-DISPLAY-003 Read Display Actual State

IF-AUDIO-001 Resolve Desired Audio Context
IF-AUDIO-002 Apply Audio Intent
IF-AUDIO-003 Read Audio Actual State

IF-INPUT-001 Resolve Input Context
IF-INPUT-002 Apply System Input Context

IF-POWER-001 Resolve Power/Resource Intent
IF-POWER-002 Apply Power/Resource Intent
IF-POWER-003 Read Actual Power/Resource Evidence
```

---

## 5.5 Gaming

```text
IF-LIB-001 Reconcile Game Library
IF-LIB-002 Read Unified Game Library
IF-PROFILE-001 Resolve Effective Game Profile
IF-PROFILE-002 Change Game Profile / Override
IF-HW-001 Refresh Hardware Context
IF-HW-002 Hardware Context Invalidated
IF-OPT-001 Resolve Optimization Recommendation
IF-LAUNCH-001 Launch Managed Game
IF-LAUNCH-002 Read Game Launch / Session Status
IF-SHARED-001 Resolve Shared App Game Presentation
```

---

## 5.6 Update / Recovery / Diagnostics

```text
IF-COMPAT-001 Resolve Compatibility Decision
IF-UPDATE-001 Evaluate Update Eligibility
IF-UPDATE-002 Start Update
IF-UPDATE-003 Read Update Transaction
IF-RECOVERY-001 Request Recovery
IF-RECOVERY-002 Resolve Recovery Target
IF-OBS-001 Publish Diagnostic Evidence
```

---

# 6. Internal vs external boundary

Важно различать два уровня.

## Internal semantic interface

Пример:

```text
Game Launch Orchestration
→ IF-PROFILE-001
→ Game Profiles
```

Это контракт внутри product responsibility model.

## External adapter/interface

Пример:

```text
Game Library Representation
→ EXT-GAMECLIENT-LIBRARY
→ Steam/Epic/Xbox/etc.
```

Внутренний consumer не должен знать детали конкретного Steam API/process/file layout, если это может быть скрыто adapter boundary.

---

# 7. Command/result separation examples

## Mode switch

```text
RequestModeChange(GAME)
        ↓
Accepted
        ↓
Mode Transition executes
        ↓
OperationalModeCommitted(GAME)
```

`Accepted` не равно `GAME active`.

## Game launch

```text
LaunchManagedGame(Game, Profile)
        ↓
Launch accepted
        ↓
client handoff
        ↓
game-start evidence
        ↓
GameSession = RUNNING
```

Client handoff success не равно game running.

## Display

```text
ApplyDisplayIntent(4K@120)
        ↓
operation submitted
        ↓
Windows/driver evidence: 4K@60
        ↓
verification = TARGET_NOT_REACHED
```

---

# 8. Error taxonomy at interface level

Точный error catalog будет расширяться, но interface layer различает минимум:

```text
REJECTED_BY_POLICY
NOT_ENTITLED
INVALID_STATE
PRECONDITION_FAILED
DEPENDENCY_UNAVAILABLE
UNSUPPORTED
CONFLICT
TIMEOUT
TARGET_NOT_REACHED
STALE_EVIDENCE
CANCELLED
RECOVERY_REQUIRED
INTERNAL_FAILURE
```

Ошибка должна быть semantic, а не просто transport exception.

Например:

```text
Steam process unavailable
```

может быть технической причиной, но semantic result интерфейса:

```text
DEPENDENCY_UNAVAILABLE
```

---

# 9. Temporal semantics

Интерфейсы SplitOS должны явно различать:

```text
snapshot
current canonical state
long-running transaction
one-shot command
subscription/event stream
```

Особенно для hardware/game-client evidence нужно понятие freshness.

```text
Evidence at T1
!= guaranteed actual state at T2
```

---

# 10. Idempotency semantics

Повтор command из-за retry не должен непредсказуемо создавать вторую независимую операцию там, где сценарий логически один.

Candidates requiring explicit correlation/idempotency semantics:

- mode transition;
- game launch;
- update;
- recovery;
- entitlement reconciliation.

Точный технический correlation identifier будет определён позже.

---

# 11. Trust boundary preview

Полная Trust Model будет отдельным слоем, но interface analysis уже фиксирует:

```text
UI request
!= trusted canonical write

External evidence
!= trusted SplitOS decision

Payment success page
!= entitlement

Game process found
!= license ownership
```

Каждый interface должен иметь owner, который валидирует вход и принимает semantic decision.

---

# 12. Result

Interface layer продолжает цепочку SSAD:

```text
Responsibility
→ Ownership
→ State / Behavior
→ Data
→ Interface
```

Следующий `07-Integrations` будет отвечать уже на другой вопрос:

```text
Через какие конкретные платформенные/внешние механизмы
эти interface contracts реализуются?
```
