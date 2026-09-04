# SplitOS — Current Concept

Этот файл фиксирует **текущее каноническое концептуальное понимание SplitOS**.

`SplitOS Concept Pre-analysis.md` сохраняет более ранний подробный pre-analysis контекст и историю формирования идеи. Если его ранняя формулировка расходится с этим README или более поздним Decision Log / Analysis & Design, приоритет имеет более новое каноническое знание.

---

## 1. Product definition

SplitOS — управляемый Windows 11-based product, который формирует известный clean-install baseline и после установки предоставляет два взаимоисключающих пользовательских контекста:

```text
WORK xor GAME
```

SplitOS:

- не создаёт собственный kernel;
- не запускает вторую Windows;
- не использует dual boot для Work/Game;
- не является обычной `.exe`-прослойкой поверх произвольной пользовательской Windows;
- сохраняет Windows Shell как базовую desktop shell для Work Mode;
- предоставляет собственный Game Mode UX / Game Launcher.

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

`MODE_MANAGED` позволяет компоненту иметь разное состояние в Work и Game.

Пример:

```text
Phone Link / Cross-Device

WORK → available
GAME → inactive
```

Цель — не минимальный размер Windows любой ценой, а **минимальный релевантный active runtime footprint текущего режима** при сохранении требуемой совместимости.

---

## 4. Startup concept

```text
Windows boot
    ↓
Windows sign-in
    ↓
SplitOS account / entitlement context
    ↓
Mode selection
    ↓
WORK xor GAME
```

Пользователь выражает session intent, а SplitOS управляет semantic activation выбранного режима.

---

## 5. Work Mode

Work Mode использует знакомый Windows desktop UX и предназначен для продуктивной работы.

В Work Mode:

- Windows Shell остаётся основной оболочкой;
- Work-oriented capabilities доступны согласно policy;
- gaming-only background activity ограничивается;
- mode-managed Work capabilities могут быть активны;
- глубокий собственный Work UI не является главным v1 priority.

---

## 6. Game Mode

Game Mode является основным UX differentiator первой версии.

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

## 7. Game / Game Client boundary

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
- managed launch orchestration;
- Game Mode preparation;
- SplitOS UX.

---

## 8. Mode transition concept

Work → Game является управляемой транзакцией:

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

## 9. Account / monetization concept

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

Текущая product direction:

```text
Distribution / build tooling → free
SplitOS paid entitlement → premium/full capabilities, updates/support according to product policy
```

Существенная информация о paid entitlement должна быть показана до destructive installation step.

Точный Free/Paid split остаётся отдельным requirement/product decision.

---

## 10. Canonical downstream ownership

Concept отвечает на вопрос **что такое SplitOS как продукт**.

Более точные downstream-модели принадлежат:

- Requirements → что система обязана делать;
- `03-Analysis-and-Design/00-Boundaries` → системные границы;
- `01-Responsibilities` → зоны ответственности;
- `02-Ownership` → authority/canonical truth;
- `03-States` → state semantics;
- `04-Behavior` → сценарное поведение.
