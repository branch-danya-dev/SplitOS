# SplitOS — Windows Component Classification Model

## 1. Purpose

Документ определяет принцип классификации Windows-компонентов внутри SplitOS.

SplitOS не должен рассматривать Windows как набор служб, которые либо всегда остаются включёнными, либо удаляются без разбора.

Для каждого Windows component необходимо определить его роль относительно:

```text
Distribution build-time
Work Mode runtime
Game Mode runtime
Recovery / servicing
```

Цель модели — отделить:

- компоненты, которые не должны присутствовать в SplitOS вообще;
- компоненты, которые нужны системе, но не должны работать по умолчанию;
- компоненты, состояние которых зависит от активного режима;
- компоненты, необходимые для compatibility baseline.

---

## 2. Core principle

SplitOS оптимизирует не только размер образа, но прежде всего **активный runtime footprint текущего пользовательского контекста**.

Это означает:

```text
BUILD-TIME OPTIMIZATION
≠
RUNTIME OPTIMIZATION
```

Часть изменений должна происходить до установки системы.

Часть — только при переключении Work/Game.

---

## 3. Classification

Каждый анализируемый Windows component должен получить ровно один основной lifecycle class:

```text
REMOVE
DISABLE
MODE_MANAGED
KEEP
```

Дополнительно для неподтверждённых решений используется:

```text
TBD
```

---

# 4. REMOVE

## Definition

Компонент физически отсутствует в поддерживаемом SplitOS baseline либо не provisioned в итоговый image.

```text
Windows source
    ↓
offline servicing
    ↓
component removed
    ↓
SplitOS installation
```

## Suitable when

- компонент не участвует ни в Work, ни в Game scenarios;
- отсутствие компонента не нарушает required Windows servicing/recovery;
- dependency analysis не выявил обязательных consumers;
- compatibility testing подтверждает удаление;
- восстановление компонента не является нормальным runtime scenario.

## Preliminary candidates

Требуют отдельной validation, но относятся к этому классу концептуально:

```text
consumer / promotional AppX
Feedback / promotional experiences
unused maps/location UI packages
unused preinstalled applications
telemetry packages where removable
Microsoft Defender Antivirus — target REMOVE, validation required
```

Наличие кандидата в этом списке не означает, что removal уже технически подтверждён.

---

# 5. DISABLE

## Definition

Компонент остаётся частью Windows baseline, но SplitOS не активирует его в нормальной поддерживаемой конфигурации.

Причины оставить компонент:

- removal повышает servicing risk;
- компонент является dependency другого Windows subsystem;
- функция редко нужна для recovery/maintenance;
- физическое удаление не даёт достаточной выгоды.

Пример:

```text
Installed: YES
Startup: DISABLED / MANUAL
Normal Work: inactive
Normal Game: inactive
```

---

# 6. MODE_MANAGED

## Definition

Компонент остаётся в SplitOS и его runtime state определяется текущим активным режимом.

Это один из ключевых отличительных механизмов SplitOS.

```text
Component exists
       ↓
Mode policy
       ↓
┌───────────────┬───────────────┐
│ WORK          │ GAME          │
│ required      │ unnecessary   │
│ ACTIVE        │ INACTIVE      │
└───────────────┴───────────────┘
```

или наоборот.

## Why this class exists

Обычный debloat-проект вынужден выбрать глобальное состояние:

```text
ON
or
OFF
```

SplitOS имеет дополнительное знание:

```text
CURRENT USER INTENT = WORK | GAME
```

Поэтому компонент может быть полезен в Work Mode и считаться лишним background workload в Game Mode.

---

## 6.1 Example — Phone Link / Cross-Device experience

Связь телефона и ПК является хорошим примером mode-dependent capability.

В Work Mode пользователь может использовать:

- notifications from phone;
- messages;
- calls;
- cross-device workflows;
- file/content transfer;
- other supported phone-PC interactions.

В Game Mode такая активность обычно не является частью основного gaming context и может:

- создавать background activity;
- генерировать notifications;
- поддерживать ненужные device connections;
- отвлекать пользователя.

Поэтому предварительная модель:

```text
Phone Link / Cross-Device stack

Distribution: KEEP
Work Mode:    AVAILABLE / ACTIVE WHEN USED
Game Mode:    INACTIVE
Class:        MODE_MANAGED
```

Точный набор AppX/services/processes, составляющих Cross-Device stack, определяется позднее через component inventory и testing.

---

## 6.2 Other likely MODE_MANAGED candidates

### Work-oriented

```text
Phone Link / Cross-Device
Print subsystem
Work indexing
Development-related background tooling
Local development servers
Enterprise/work integrations
Selected sync clients
```

Possible target:

```text
WORK → available/active
GAME → stopped/suspended/not started
```

### Game-oriented

```text
Game Clients
Gaming overlays
Game capture / streaming integrations
Game-specific services
Controller-oriented helpers
```

Possible target:

```text
WORK → inactive unless explicitly opened
GAME → available/active
```

The exact state must be defined per component; category alone is not enough.

---

# 7. KEEP

## Definition

Компонент является частью compatibility baseline и не должен удаляться либо глобально отключаться SplitOS.

Typical areas:

```text
boot
core servicing
recovery
core networking
audio stack
display stack
input/HID dependencies
required identity/credential infrastructure
required application deployment infrastructure
```

`KEEP` не означает, что SplitOS никогда не изменяет configuration компонента.

Это означает, что компонент не должен терять свою базовую system responsibility.

---

# 8. Classification is not based on folklore

SplitOS не должен копировать списки "safe services to disable" из готовых gaming builds без проверки.

External projects may be used as:

```text
research evidence
candidate source
comparison baseline
```

but never as authoritative SplitOS decision.

Для каждого компонента требуется собственная оценка:

```text
Meaning
Dependencies
Work value
Game value
Resource impact
Compatibility impact
Servicing impact
Recovery impact
Reversibility
Test evidence
```

---

# 9. Windows Component Matrix

Следующим аналитическим артефактом должна стать `Windows Component Matrix`.

Recommended fields:

| Field | Meaning |
|---|---|
| Component | Human-readable component name |
| Technical ID | Service / package / feature / AppX / driver identifier |
| Type | SERVICE / DRIVER / APPX / FEATURE / PACKAGE / TASK / POLICY |
| Default Windows state | Baseline source behavior |
| SplitOS class | REMOVE / DISABLE / MODE_MANAGED / KEEP / TBD |
| Work state | Desired Work Mode behavior |
| Game state | Desired Game Mode behavior |
| Dependencies | Known providers/consumers |
| Reason | Why SplitOS changes or keeps it |
| Expected impact | CPU/RAM/I/O/network/UX/privacy impact |
| Compatibility risk | LOW / MEDIUM / HIGH / TBD |
| Servicing risk | LOW / MEDIUM / HIGH / TBD |
| Reversible | YES / NO / PARTIAL / TBD |
| Evidence | docs / experiment / external reference |
| Validation status | UNTESTED / TESTED / REJECTED / ACCEPTED |

---

# 10. Build-time vs runtime ownership

## Build-time

SplitOS Build Manifest owns the desired baseline composition.

It may define:

```text
REMOVE component
DISABLE default state
KEEP dependency
install SplitOS package
apply baseline policy
```

These actions are applied before or during installation preparation.

---

## Runtime

SplitOS Runtime owns dynamic mode-dependent state only.

It should not continuously redo distribution preparation.

Runtime responsibilities include:

```text
MODE_MANAGED lifecycle
process/service activation
mode transition state
Game Client lifecycle
selected display/audio/input context
user-facing SplitOS features
```

---

# 11. Mode transition consequence

For every `MODE_MANAGED` component the future transition model must define:

```text
Current state
Desired target state
Transition trigger
Guard
Shutdown/start behavior
Timeout
Failure handling
Rollback behavior
State verification
```

Example:

```text
WORK
Phone Link available
       ↓
request GAME
       ↓
stop/suspend Cross-Device activity
       ↓
verify inactive
       ↓
continue Game Mode activation
```

If a component cannot reach its required target state, transition policy must determine whether it is:

```text
BLOCKING
NON_BLOCKING
DEGRADED
```

This decision belongs to later Behavior/State/Failure analysis.

---

# 12. Testing feedback loop

Component classification is versioned knowledge, not a one-time list.

```text
Candidate component
        ↓
Classification hypothesis
        ↓
Build / runtime experiment
        ↓
Compatibility testing
        ↓
Performance measurement
        ↓
ACCEPT / CHANGE CLASS / REJECT
```

During testing SplitOS may discover:

- additional removable components;
- components that must be restored;
- globally disabled components that should become `MODE_MANAGED`;
- `MODE_MANAGED` components that create no meaningful benefit and should become `KEEP`;
- hidden dependencies that change the original decision.

This is expected behavior of the analysis process.

---

# 13. Current architectural consequence

The Windows baseline is therefore not simply:

```text
"debloated Windows"
```

It is:

```text
Known Windows source
        +
SplitOS Build Manifest
        +
validated component classification
        +
mode-dependent runtime policy
        =
SplitOS Windows Baseline
```

The distinguishing feature is not only removal.

It is the combination of:

```text
static removal
+
static disablement
+
mode-dependent lifecycle
+
compatibility ownership
```

which allows Work Mode and Game Mode to have materially different active system footprints while sharing one installed Windows base.
