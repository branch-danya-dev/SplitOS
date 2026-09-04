# SplitOS — Installed Runtime Boundary

## 1. Purpose

Документ определяет, что происходит после установки подготовленного SplitOS Windows Baseline и где заканчивается build-time responsibility.

Цель — не допустить смешивания двух разных задач:

```text
prepare distribution
≠
operate installed system
```

---

# 2. Runtime entry

После clean installation система должна войти в нормальный installed runtime lifecycle.

Концептуально:

```text
Installed SplitOS Windows Baseline
        ↓
Windows boot
        ↓
Windows sign-in
        ↓
SplitOS bootstrap/runtime start
        ↓
SplitOS Account / entitlement context
        ↓
Mode selection
        ↓
WORK xor GAME
```

Build-time component removal к этому моменту уже завершён.

---

# 3. What runtime owns

Installed SplitOS runtime отвечает за динамическое состояние установленной системы.

Внутри runtime boundary находятся responsibility areas:

```text
SplitOS Runtime
│
├── user/account context
├── entitlement state consumption
├── active mode state
├── mode transition orchestration
├── MODE_MANAGED component lifecycle
├── application lifecycle policy
├── display context
├── audio context
├── input context
├── Game Launcher UX
├── game library view
├── Game Profiles
├── hardware-context refresh
├── game optimization policy
├── Shared Apps policy
├── update orchestration
├── recovery coordination
└── diagnostics / state verification
```

Это зоны ответственности, а не утверждение о количестве процессов/служб.

---

# 4. What runtime does not own

Runtime не должен считать своей нормальной задачей:

```text
rebuilding Windows image
repeating full debloat on every boot
randomly deleting system components after installation
mutating unsupported Windows versions into a "close enough" state
```

Если установленный baseline drifted настолько, что его нельзя безопасно привести к поддерживаемому состоянию, это update/recovery problem, а не обычный mode transition.

---

# 5. Baseline vs live state

Необходимо различать:

```text
BASELINE STATE
→ what the installed SplitOS release is expected to contain

LIVE STATE
→ what is currently active in this session
```

Пример:

```text
Phone Link / Cross-Device stack

Baseline:
installed
class = MODE_MANAGED

Work Mode live state:
available / active when used

Game Mode live state:
inactive
```

Runtime не меняет сам факт наличия компонента; он управляет его текущим состоянием.

---

# 6. Mode-managed Windows components

`MODE_MANAGED` является отдельной runtime responsibility.

Для каждого такого компонента future behavior model должен определить:

```text
Work target state
Game target state
activation trigger
deactivation trigger
dependencies
verification method
timeout
failure policy
rollback policy
```

Пример:

```text
Work Mode
Phone integration available
        ↓
User requests Game Mode
        ↓
Transition checks Cross-Device activity
        ↓
Stop/suspend relevant activity
        ↓
Verify target state
        ↓
Continue transition
```

---

# 7. Runtime mode policy

Активный режим является runtime intent SplitOS.

```text
WORK
or
GAME
```

Режим определяет не только UI, но и desired state нескольких областей:

```text
applications
services/components
background activity
display
audio
power
input
Game Clients
Shared Apps
notifications/presentation
```

Это означает, что mode transition является системным state transition, а не сменой темы интерфейса.

---

# 8. Runtime and Windows authority

Runtime хранит/определяет desired product state.

Windows и drivers остаются authority фактического системного state.

```text
SplitOS desired state
        ↓
request change
        ↓
Windows / external subsystem
        ↓
actual state
        ↓
SplitOS verification
```

Runtime не должен считать действие успешным только потому, что команда была отправлена.

---

# 9. Account and entitlement boundary

SplitOS Account является продуктовой identity boundary, отдельной от Windows identity/licensing.

Необходимо различать:

```text
Windows License
SplitOS Account
SplitOS Entitlement
External Game Platform Account
```

Они не являются одним и тем же authority.

Предварительная business model:

```text
SplitOS build/distribution tooling
→ distributed without charge

SplitOS Account
→ created/used for product identity

Paid entitlement
→ provides full/premium SplitOS capabilities according to product policy
```

Точный Free/Paid split остаётся Requirements/Product decision.

Runtime должен уметь потреблять entitlement state, но не должен смешивать его с Windows activation или лицензией игр.

---

# 10. Entitlement and system safety

Истечение/отсутствие paid entitlement не должно автоматически делать установленную машину неработоспособной.

Минимальная безопасная product requirement для дальнейшего уточнения:

```text
no entitlement
≠
unbootable system
```

Какие SplitOS capabilities остаются доступными, определяется отдельно.

Security/recovery-critical behavior не должно зависеть от невозможного пользовательского действия после destructive installation.

---

# 11. Runtime update boundary

После установки SplitOS update flow работает с уже существующим baseline.

Нужно различать:

```text
SplitOS product update
Windows-base compatible update
profile/configuration update
driver compatibility update
```

Runtime может инициировать update/recovery flow, но update package должен соответствовать текущему supported baseline.

Нельзя считать произвольный upstream Windows update автоматически совместимым.

---

# 12. Runtime drift

Installed system может отклониться от baseline из-за:

- user changes;
- third-party software;
- external Game Client updates;
- driver changes;
- failed update;
- manual Windows configuration changes;
- component corruption.

Runtime должен в будущем различать:

```text
EXPECTED VARIATION
SUPPORTED OVERRIDE
BASELINE DRIFT
CRITICAL INCONSISTENCY
```

Конкретная drift model относится к Ownership / States / Recovery analysis.

---

# 13. Runtime recovery principle

Если dynamic transition не удался:

```text
restore last coherent mode state
```

Если baseline itself повреждён:

```text
enter recovery / repair path
```

Это разные классы failure.

Например:

```text
Game display switch failed
→ transition rollback

Required SplitOS package missing
→ baseline/recovery incident
```

---

# 14. Boundary with external applications

Runtime может управлять lifecycle policy приложения, но не становится владельцем его данных или внутреннего состояния.

Например Phone Link может быть `MODE_MANAGED`, однако SplitOS не становится владельцем:

- Microsoft account state;
- phone data;
- messages/calls;
- cross-device application internals.

SplitOS владеет только решением:

```text
should this capability be active in current mode?
```

---

# 15. Boundary with Game Clients

Game Clients могут быть runtime-managed, но остаются external ownership boundaries.

```text
Steam process lifecycle
→ SplitOS may orchestrate

Steam authentication/license/library truth
→ Steam authority
```

То же разделение применяется к Epic, Battle.net, Xbox и другим клиентам.

---

# 16. Runtime invariant

Ключевой runtime invariant:

```text
Installed baseline is known
+
exactly one active mode
+
mode-managed state converges to active-mode policy
```

То есть нормальное состояние системы должно быть объяснимо как:

```text
Baseline R
+
User configuration U
+
Active Mode M
+
Current external evidence E
=
Expected live SplitOS state
```

---

# 17. Open questions

- Какой runtime area хранит authoritative active-mode state?
- Как MODE_MANAGED changes становятся transactional?
- Какие component failures блокируют mode transition?
- Как проверяется actual service/process/device state?
- Как runtime определяет baseline drift?
- Как entitlement кешируется offline?
- Какие функции доступны без paid entitlement?
- Как recovery работает при недоступности account backend?

---

# 18. Result

После установки SplitOS разделяет ответственность следующим образом:

```text
Build Pipeline
→ establishes known baseline

Installed Runtime
→ manages live intent and state

Windows / Drivers / External Apps
→ execute or own their actual behavior
```

Это позволяет далее проектировать Responsibilities и Ownership без смешивания image servicing с runtime mode orchestration.
