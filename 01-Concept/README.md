# SplitOS — Current Concept

Этот файл фиксирует **текущее каноническое концептуальное понимание SplitOS**.

`SplitOS Concept Pre-analysis.md` сохраняет более ранний подробный pre-analysis контекст и историю формирования идеи. Если его ранняя формулировка расходится с этим README или более поздним Decision Log / Analysis & Design, приоритет имеет более новое каноническое знание.

---

## 1. Product definition

SplitOS — управляемый Windows 11-based product, который формирует известный clean-install baseline и после установки предоставляет два уровня пользовательского experience:

```text
SplitOS Base
→ модернизированный Windows desktop baseline

SplitOS Pro Runtime
→ managed WORK xor GAME experience
```

SplitOS:

- не создаёт собственный kernel;
- не запускает вторую Windows;
- не использует dual boot для Work/Game;
- не является обычной `.exe`-прослойкой поверх произвольной пользовательской Windows;
- сохраняет Windows Shell как базовую desktop shell;
- предоставляет собственный Game Mode UX / Game Launcher при соответствующем entitlement.

`WORK xor GAME` является invariant полноценного managed SplitOS runtime, а не обязательным состоянием каждого FREE пользователя.

---

## 2. Distribution / build model

Каноническая модель не предполагает, что SplitOS публично распространяет готовый modified Windows ISO как собственный download artifact.

Текущая модель:

```text
Microsoft-authorized Windows source
        +
SplitOS Media Builder
        +
SplitOS Build Manifest / Packages
        +
Windows Component Classification
        ↓
locally prepared supported baseline
        ↓
clean installation
        ↓
Installed SplitOS Runtime
```

Windows source является внешним Microsoft-owned build input.

SplitOS владеет:

- Media Builder;
- Build Manifest;
- SplitOS packages;
- component classification;
- compatibility knowledge;
- runtime product logic.

---

## 3. Windows component strategy

SplitOS baseline не сводится к runtime-tweaks.

Для Windows components используется классификация:

```text
REMOVE
DISABLE
MODE_MANAGED
KEEP
```

`MODE_MANAGED` позволяет компоненту иметь разное состояние в Work и Game, когда managed runtime разрешён.

Пример:

```text
Phone Link / Cross-Device

WORK → available
GAME → inactive
```

Цель — не минимальный размер Windows любой ценой, а **минимальный релевантный active runtime footprint текущего experience/mode** при сохранении требуемой совместимости.

---

## 4. Identity and startup concept

Windows identity и SplitOS identity разделены:

```text
Windows User / Windows Identity
≠
SplitOS Account
≠
SplitOS Entitlement
```

Канонический first-run flow:

```text
Windows OOBE
    ↓
Windows user created
    ↓
First Windows sign-in
    ↓
SplitOS First Run Experience
    ↓
Sign in / Create SplitOS Account
    ↓
Entitlement resolution
```

После этого:

```text
FREE
→ normal Windows Desktop on SplitOS baseline

PRO
→ managed runtime
→ Mode selection
→ WORK xor GAME
```

SplitOS Account является product identity и не заменяет Windows authentication principal.

Подробная модель находится в:

```text
Runtime Access and Subscription Model.md
```

---

## 5. FREE / SplitOS Base experience

FREE пользователь получает модернизированный SplitOS Windows baseline без обязательной платной подписки.

В FREE experience:

- Windows desktop доступен обычным образом;
- Windows Shell остаётся базовым UX;
- обычные приложения работают как Windows applications;
- Game Clients могут запускать игры обычным Windows/client path;
- mode selection не является обязательным gate;
- Work/Game managed runtime не активируется;
- SplitOS Manager остаётся доступен как account/subscription/product control surface.

Build-time изменения baseline сохраняются независимо от подписки.

---

## 6. Work Mode

Work Mode относится к managed SplitOS runtime и доступен согласно entitlement/product policy.

Work Mode использует знакомый Windows desktop UX и предназначен для продуктивной работы.

В Work Mode:

- Windows Shell остаётся основной оболочкой;
- Work-oriented capabilities доступны согласно policy;
- gaming-only background activity ограничивается;
- mode-managed Work capabilities могут быть активны;
- глубокий собственный Work UI не является главным v1 priority.

---

## 7. Game Mode

Game Mode является основным UX differentiator первой версии managed runtime.

В Game Mode:

- активен SplitOS Game Launcher;
- UX controller-friendly;
- доступна unified library поддерживаемых игр;
- применяются Game Profiles;
- учитываются display/audio/input/hardware context;
- Work-oriented background activity ограничивается;
- Shared Apps получают Game Mode-specific representation;
- после завершения игры пользователь возвращается в Game Launcher, а не автоматически в Work Mode.

---

## 8. Game / Game Client boundary

```text
GAME != GAME_CLIENT
```

External Game Client владеет:

- account/auth;
- license truth;
- store;
- installation/update internals;
- platform-specific cloud data.

SplitOS владеет:

- unified library representation;
- SplitOS game/profile relation;
- managed launch orchestration при active Pro Runtime;
- Game Mode preparation;
- SplitOS UX.

В FREE experience обычный game launch может идти напрямую через external Game Client без managed mode transition.

---

## 9. Mode transition concept

При активном managed runtime Work → Game является управляемой транзакцией:

```text
Request GAME
    ↓
Inspect Work context
    ↓
Resolve blockers
    ↓
Apply Game target state
    ↓
Verify
    ↓
Commit GAME
```

Committed mode не меняется преждевременно.

Пользователь может отменить переход, если blocking state не разрешён.

---

## 10. Account / monetization concept

SplitOS Account и SplitOS Entitlement являются отдельной продуктовой областью:

```text
Windows License
≠
SplitOS Account
≠
SplitOS Entitlement
≠
External Game License
```

Текущая модель:

```text
SplitOS Base / build tooling → free
SplitOS Account → required product identity for supported onboarding
FREE entitlement → Windows desktop on SplitOS baseline
PRO entitlement → managed Work/Game runtime and premium capabilities
```

Pro capabilities могут быть предустановлены, но entitlement определяет право на их активное product behavior.

Upgrade FREE → PRO не должен требовать reinstall при наличии required installed components.

Существенная информация о paid entitlement должна быть показана до destructive installation step.

---

## 11. SplitOS Manager

SplitOS Manager является основным desktop control center SplitOS и должен включать product surfaces для:

```text
Account
Subscription / Plan
Upgrade
Modes
Game Profiles
Devices
Updates
Recovery
```

Payment execution остаётся внешней responsibility; SplitOS владеет resulting entitlement semantics.

---

## 12. Canonical downstream ownership

Concept отвечает на вопрос **что такое SplitOS как продукт**.

Более точные downstream-модели принадлежат:

- Requirements → что система обязана делать;
- `03-Analysis-and-Design/00-Boundaries` → системные границы;
- `01-Responsibilities` → зоны ответственности;
- `02-Ownership` → authority/canonical truth;
- `03-States` → state semantics;
- `04-Behavior` → сценарное поведение;
- `05-Data` → meaning/ownership/lifecycle данных.
