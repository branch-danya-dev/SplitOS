# SplitOS — Flow Model

## 1. Purpose

Документ определяет правила end-to-end flow analysis для SplitOS.

Предыдущие слои уже отвечают на разные вопросы:

```text
Ownership     → кто владеет истиной?
State         → в каком состоянии может находиться система?
Behavior      → как должна вести себя ответственность?
Data          → какие сущности и знания существуют?
Interface     → какие semantic contracts допустимы?
Integration   → какими mechanism families contracts реализуются?
Flow          → как всё это связывается во времени для конкретного сценария?
```

Flow не создаёт новых владельцев истины и не должен переопределять state/behavior semantics.

---

## 2. Canonical flow rule

Каждый flow должен быть читаем как причинно-временная последовательность:

```text
Trigger
→ Preconditions
→ Request / Evidence
→ Owner Decision
→ Integration Operation
→ Actual-State Evidence
→ Verification
→ Commit / Result
→ Observable User Outcome
```

Если сценарий меняет canonical state, flow обязан явно показывать semantic commit boundary.

---

## 3. Participant classes

В end-to-end flows используются следующие classes участников.

### 3.1 User-facing participants

```text
User
SplitOS First Run Experience
SplitOS Manager
SplitOS Game Launcher
Windows Desktop / Windows Shell
```

UI не является владельцем canonical state.

---

### 3.2 Runtime orchestration participants

```text
SplitOS Runtime Host
Product Identity & Entitlement
Mode Intent & Active Mode State
Mode Transition Coordination
Mode Policy
Application Lifecycle Policy
Game Library Representation
Game Profiles
Hardware Context Evaluation
Game Optimization Policy
Game Launch Orchestration
Shared App Experience
Compatibility Management
Update Orchestration
Recovery Coordination
Observability & Diagnostics
```

Эти имена описывают responsibility/owner roles, а не обязательно отдельные процессы.

---

### 3.3 Local integration participants

```text
Windows User Session APIs
Windows Process / Service APIs
Display Integration
Audio Integration
Input Integration
Power Integration
SplitOS Privileged Broker
Game Client Adapter
```

---

### 3.4 External participants

```text
SplitOS Account Backend
Payment Provider
External Game Client / Platform
Microsoft Windows Source / Update Ecosystem
Physical Device / Driver evidence
```

---

## 4. Flow identity and correlation

Долгоживущие или многокомпонентные flows должны иметь correlation identity.

Conceptual examples:

```text
OnboardingFlowId
ModeTransitionId
GameLaunchId
UpdateTransactionId
RecoveryId
Checkout/EntitlementRefresh correlation
```

Точное physical representation определяется позднее.

### Rule

Нельзя склеивать независимые операции только потому, что они произошли рядом по времени.

```text
Request GAME #A
!=
Request GAME #B
```

---

## 5. Command, acceptance, evidence and commit

Критическое правило всех flows:

```text
Command sent
!= command accepted
!= external operation submitted
!= target state observed
!= semantic verification passed
!= canonical commit completed
```

Пример display flow:

```text
Apply 4K@120
→ Windows accepted SetDisplayConfig
→ QueryDisplayConfig reports 4K@60
→ target not reached
→ transition verification fails
```

Пример game launch:

```text
Steam launch handoff accepted
!= GAME_RUNNING
```

---

## 6. Flow kinds

### 6.1 Activation flow

Переводит capability/context из доступного, но неактивного состояния в активное.

Examples:

- FREE → PRO managed runtime activation;
- Work → Game;
- Game → Work.

### 6.2 Transaction flow

Имеет source state, target state, commit boundary и rollback/fallback semantics.

Examples:

- mode transition;
- update;
- recovery.

### 6.3 Projection/reconciliation flow

Обновляет SplitOS projection на основании external authority/evidence.

Examples:

- game library reconciliation;
- entitlement refresh;
- hardware refresh.

### 6.4 Handoff flow

SplitOS инициирует действие у external authority, после чего обязан отдельно наблюдать результат.

Example:

```text
Game launch → External Game Client
```

---

## 7. User-visible vs internal completion

Flow может иметь несколько значимых milestones.

Example Work → Game:

```text
request accepted
↓
pre-flight complete
↓
platform changes applied
↓
verification passed
↓
GAME committed
↓
Game Launcher ready
```

В зависимости от contract user-visible success может требовать последнего milestone, а не только commit.

---

## 8. Normal, alternate and failure paths

Каждый canonical flow должен содержать:

1. normal path;
2. user-cancel path where applicable;
3. external dependency unavailable path;
4. verification failure path;
5. rollback/fallback path if state mutation уже началась;
6. final observable outcome.

Failure details later получают отдельную нормализацию в `09-Failures`, но Flow layer обязан показать **где failure может возникнуть и куда управление идёт дальше**.

---

## 9. State mutation rule

Consumers не должны напрямую менять canonical state другого owner.

Неверно:

```text
Game Launcher
→ currentMode = GAME
```

Правильно:

```text
Game Launcher
→ RequestOperationalMode(GAME)
→ Mode Transition Coordination
→ apply/verify
→ Mode State commit
→ OperationalModeCommitted(GAME)
```

---

## 10. Privilege rule

User-facing surfaces не выполняют privileged machine mutation напрямую.

Flow pattern:

```text
UI
→ Runtime Host
→ validated semantic operation
→ secured local IPC
→ Privileged Broker
→ Windows privileged mechanism
→ actual-state evidence
→ Runtime verification
```

Не каждая platform operation требует broker; user-session APIs должны оставаться в user session там, где это корректно.

---

## 11. External authority rule

Flow обязан сохранять ownership external systems.

Examples:

```text
Payment Provider
→ payment evidence
→ SplitOS Backend
→ Entitlement decision
```

не:

```text
Payment Provider
→ local Pro = true
```

и:

```text
Game Client
→ installation/license evidence
→ SplitOS projection/launch decision
```

не:

```text
SplitOS cache
→ platform license truth
```

---

## 12. Offline/degraded rule

Account/backend dependency не является Windows authentication dependency.

Flow must preserve:

```text
Windows sign-in success
↓
SplitOS backend unavailable
↓
apply offline/degraded runtime-access policy
↓
Windows Desktop remains usable
```

---

## 13. Canonical v1 flows

Текущий Flow layer фиксирует пять основных end-to-end families:

```text
FL-01 First Run / FREE-PRO / Upgrade
FL-02 Work → Game
FL-03 Managed Game Launch and Exit
FL-04 Game → Work
FL-05 Update and Recovery
```

Direct game launch from Work является composition:

```text
FL-02 Work → Game
+
FL-03 Managed Game Launch
```

а не отдельной competing flow model.

---

## 14. Flow composition principle

Повторяемые subflows должны переиспользоваться semanticly.

Example:

```text
Refresh entitlement
```

используется при:

- first run;
- normal startup;
- checkout completion;
- manual refresh;
- downgrade detection.

Но exact transport/trigger может различаться.

---

## 15. Deferred to later layers

Flow layer пока не фиксирует:

- exact timeout numbers;
- retry counts;
- circuit-breaker values;
- security token format;
- physical DB transactions;
- exact thread/process implementation;
- exact UI wording;
- SLA/SLO values.

Это будет уточняться в Failures, Trust, Specification и implementation design.

---

## 16. Result

После Flow layer SplitOS можно читать как целостную систему:

```text
User intent
→ product/runtime gate
→ semantic owners
→ interfaces
→ integration mechanisms
→ external/platform effects
→ observed evidence
→ verification
→ canonical state/result
→ user-visible outcome
```
