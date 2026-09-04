# SplitOS — System Boundary Analysis

## 1. Purpose

Документ определяет аналитические границы SplitOS перед декомпозицией ответственности и ownership.

Цель — отделить:

- то, чем SplitOS владеет как продукт;
- build-time responsibility SplitOS;
- базовую Windows-платформу, которую SplitOS использует и конфигурирует, но не разрабатывает;
- installed runtime responsibility;
- внешние системы и устройства;
- области, которые входят в текущий change surface;
- области, которые участвуют во flow, но остаются внешними ownership boundaries.

Документ не фиксирует внутреннюю компонентную архитектуру SplitOS.

---

# 2. Object of analysis

Объект анализа:

> **SplitOS как управляемый Windows 11-based product, который формирует известный clean-install baseline из поддерживаемого Windows source, а после установки предоставляет два взаимоисключающих пользовательских контекста — Work Mode и Game Mode — и управляет live system state, игровым UX, профилями, устройствами и совместимым lifecycle базовой Windows.**

---

# 3. Boundary model

Для SplitOS необходимо различать четыре разные границы.

## 3.1 Product boundary

Показывает, за какое поведение отвечает продукт SplitOS в целом.

Внутри product boundary находятся:

- SplitOS Media Builder;
- SplitOS Build Manifest;
- Windows Component Classification / Matrix;
- SplitOS packages;
- SplitOS user/account/key context;
- entitlement consumption;
- mode selection;
- Work/Game state policy;
- mode transition orchestration;
- SplitOS Manager;
- SplitOS Game Launcher;
- game discovery abstraction;
- game profiles;
- display/audio/input preferences;
- application classification and policies;
- Shared App behavior in Game Mode;
- game experience optimization policy;
- compatibility policy;
- SplitOS update policy;
- recovery coordination;
- diagnostics and observability SplitOS.

## 3.2 Build boundary

Показывает, что SplitOS контролирует при подготовке installable baseline.

Build boundary включает SplitOS-owned inputs:

- Media Builder;
- Build Manifest;
- SplitOS packages;
- component classification;
- baseline policies;
- provisioning rules;
- compatibility metadata;
- recovery/update assets SplitOS.

Windows 11 source является **внешним build input**, а не SplitOS-owned artifact.

Boundary rule:

```text
Microsoft-authorized Windows source
→ external build input

SplitOS Build Manifest / Packages
→ SplitOS-owned transformation inputs

Prepared SplitOS baseline
→ output of supported build/deployment flow
```

SplitOS не должен проектироваться в расчёте на публичное распространение готового modified Windows ISO как собственного базового download artifact без отдельного подтверждённого права на такую модель.

## 3.3 Installed runtime boundary

Показывает, какие live-system решения принадлежат SplitOS после clean installation.

В runtime boundary находятся:

- account/entitlement context consumption;
- active mode state;
- Work/Game desired state;
- mode transition;
- `MODE_MANAGED` Windows component lifecycle;
- application lifecycle policy;
- Game Launcher;
- profiles;
- display/audio/input policy;
- game optimization policy;
- Shared Apps policy;
- runtime update orchestration;
- recovery coordination;
- actual-state verification;
- baseline-drift awareness.

Build-time component removal не является обычной runtime responsibility.

## 3.4 Runtime ownership boundary

Показывает, какие runtime-решения являются authority SplitOS, а какие принадлежат внешним системам.

Пример:

```text
Desired Game Display
→ SplitOS authority

Actual display configuration
→ Windows / driver authority

Physical display capability
→ hardware / driver-reported authority
```

---

# 4. Inside SplitOS product boundary

На текущем уровне анализа SplitOS владеет следующими responsibility areas:

```text
SplitOS Product
│
├── Distribution Build
│   ├── Source Validation
│   ├── Build Manifest
│   ├── Component Classification
│   ├── SplitOS Package Set
│   └── Baseline Compatibility
│
├── Installed Runtime
│   ├── User / Account Context
│   ├── Entitlement Context
│   ├── Mode Intent
│   ├── Mode Policy
│   ├── Mode Transition
│   ├── Work Context Policy
│   ├── Game Context Policy
│   ├── MODE_MANAGED Component Policy
│   ├── Game Launcher UX
│   ├── Game Library View
│   ├── Game Profile Policy
│   ├── Application Policy
│   ├── Shared App Policy
│   ├── Display Preference Policy
│   ├── Audio Preference Policy
│   ├── Input Preference Policy
│   ├── Game Optimization Policy
│   ├── Update Orchestration
│   ├── Recovery Coordination
│   └── Diagnostics / State Verification
│
└── Distribution Compatibility Policy
```

Это responsibility areas, а не названия будущих сервисов или процессов.

---

# 5. Windows source boundary

Windows 11 source является внешним входом SplitOS Build Pipeline.

SplitOS определяет:

- какая Windows version/build считается поддерживаемой;
- какие source characteristics обязательны;
- можно ли продолжать build;
- какие SplitOS transformations применяются к image.

Microsoft остаётся owner/source для:

- Windows binaries;
- Windows implementation;
- licensing terms;
- upstream Windows patches.

SplitOS не становится владельцем Windows implementation только потому, что выполняет offline servicing или формирует clean-install baseline.

---

# 6. Windows runtime boundary

Windows 11 является базовой runtime-платформой SplitOS.

SplitOS использует Windows для фактического выполнения системных операций, включая:

- process execution;
- service control;
- display configuration;
- audio routing;
- input/device management;
- file system operations;
- user session behavior;
- security enforcement тех компонентов, которые остаются в baseline;
- power management;
- driver interaction.

SplitOS не владеет внутренней реализацией этих механизмов.

Boundary rule:

```text
SplitOS
→ decides desired product/system state

Windows
→ applies supported OS-level state

Driver / Hardware
→ implements physical capability
```

---

# 7. Windows component boundary

SplitOS не рассматривает Windows-компоненты как единый неделимый блок.

Каждый значимый компонент классифицируется:

```text
REMOVE
DISABLE
MODE_MANAGED
KEEP
```

### REMOVE

Компонент отсутствует/deprovisioned в поддерживаемом baseline после validation.

### DISABLE

Компонент остаётся установленным, но не активен в normal baseline.

### MODE_MANAGED

Компонент остаётся установленным и его live state зависит от активного режима.

Пример:

```text
Phone Link / Cross-Device

Work Mode → available / active when used
Game Mode → inactive
```

### KEEP

Компонент сохраняет обязательную platform responsibility.

Classification принадлежит SplitOS compatibility knowledge, но фактические dependency semantics Windows должны подтверждаться testing/evidence.

---

# 8. Windows Shell boundary

Windows Shell остаётся внешней платформенной зоной относительно SplitOS implementation ownership.

SplitOS:

- не заменяет Explorer как обязательную system shell;
- не создаёт новую login shell;
- не требует собственного desktop environment;
- может отображать собственный fullscreen Game Mode UX поверх Windows;
- может позднее расширять Work Mode UX без изменения базовой shell ownership.

SplitOS Game Launcher является продуктовым UX-компонентом, но не Windows Shell replacement.

---

# 9. External Game Clients boundary

Game Clients являются внешними ownership boundaries.

Примеры:

- Steam;
- Epic Games;
- Battle.net;
- Xbox;
- другие поддерживаемые клиенты в будущих версиях.

Game Client владеет:

- account state;
- authentication;
- licenses;
- store behavior;
- game installation;
- game updates;
- platform-specific cloud behavior;
- внутренним состоянием клиента.

SplitOS владеет:

- unified Game Launcher UX;
- представлением обнаруженных игр;
- связью `Game ↔ Game Client` в собственной модели;
- выбором SplitOS Game Profile;
- подготовкой Game Mode перед launch;
- пользовательским gaming flow вокруг внешнего клиента.

Boundary rule:

```text
External Game Client
→ platform authority

SplitOS
→ gaming UX / orchestration authority
```

---

# 10. Game boundary

Игра является внешним executable/product boundary.

SplitOS может:

- определить игру;
- связать её с Game Client;
- подобрать Game Profile;
- подготовить display/input/system context;
- безопасно изменить поддерживаемые user-facing configuration settings;
- инициировать launch;
- определить завершение процесса для возврата в Game Launcher.

SplitOS не владеет:

- gameplay logic;
- save semantics игры;
- network code;
- matchmaking;
- anti-cheat;
- DRM;
- server logic;
- internal input handling.

---

# 11. Hardware and driver boundary

Внешними являются:

- displays / TVs;
- GPU;
- controllers;
- keyboard/mouse;
- audio devices;
- storage hardware;
- device firmware;
- vendor drivers.

SplitOS использует reported capabilities и supported interfaces, но не считает собственную конфигурацию доказательством физической возможности устройства.

Например:

```text
SplitOS profile says HDR = enabled
≠
proof that HDR is currently available
```

Actual capability должна подтверждаться Windows/driver/hardware evidence.

---

# 12. Shared Applications boundary

Shared Applications остаются внешними desktop applications.

Примеры:

- Discord;
- browser;
- Spotify;
- media applications.

SplitOS может управлять их представлением и lifecycle policy в Game Mode:

```text
Overlay
Locked window
Secondary display
Background
```

Но SplitOS не владеет их:

- account state;
- application data;
- internal UI logic;
- network behavior;
- собственными настройками, кроме явно поддерживаемой интеграции.

---

# 13. Account / entitlement boundary

SplitOS Account является внутренней product identity boundary.

Необходимо различать:

```text
Windows License
SplitOS Account
SplitOS Entitlement
External Game Platform Account
```

SplitOS владеет собственным account/entitlement decision.

Microsoft остаётся authority Windows licensing.

Game Platform остаётся authority игровых лицензий.

Текущая business model допускает:

```text
SplitOS Builder / distribution tooling → free
Paid SplitOS entitlement → full/premium capabilities, updates/support according to product policy
```

Точный feature split остаётся отдельным Requirements/Product решением.

---

# 14. Update boundary

Microsoft является источником Windows patches.

SplitOS является authority для решения:

> какая Windows base version / patch level считается совместимой с конкретным SplitOS release.

Таким образом:

```text
Microsoft
→ patch source

SplitOS
→ compatibility / release decision

Installed SplitOS Runtime
→ consumes approved SplitOS update path
```

Windows Update infrastructure не становится внутренним компонентом SplitOS только потому, что SplitOS ограничивает его стандартное поведение.

---

# 15. User boundary

Пользователь находится вне системы как primary actor.

Пользователь владеет конечным намерением:

- выбором Work или Game;
- решением отменить mode transition;
- разрешением спорных Work blockers;
- ручным выбором display/input profile;
- ручным изменением игровых настроек;
- явным переходом Game → Work;
- решением начать destructive installation после disclosure.

SplitOS может автоматизировать действия, но не должен превращать продуктовую рекомендацию в необратимое пользовательское решение без необходимости.

---

# 16. Current change boundary

Для первой проектируемой версии основной change surface:

```text
Build / Distribution
├── Media Builder
├── Windows source validation
├── Build Manifest
├── Windows Component Matrix
├── offline/setup preparation
├── SplitOS package provisioning
├── clean installation
├── compatibility lifecycle
└── recovery assets

Installed Runtime
├── startup / account context
├── entitlement consumption
├── Mode selection
├── Mode isolation
├── Work → Game transition
├── Game → Work transition
├── MODE_MANAGED components
├── Game Launcher
├── supported Game Client integrations
├── official game discovery
├── Game Profiles
├── display management
├── audio management
├── input management
├── game experience optimization
├── Shared Apps
├── update orchestration
├── recovery
└── diagnostics / state verification
```

Низкоприоритетный / deferred change surface:

- advanced Work Mode desktop UX;
- manually added / unofficial games;
- OEM SplitOS controller;
- proprietary communication/social platform;
- handheld-specific UX;
- laptop-specific battery model;
- partner-specific app builds.

---

# 17. Boundary-crossing interactions

Основные внешние переходы, которые позднее потребуют interface/integration analysis:

| Boundary | Direction | Meaning |
|---|---|---|
| Builder ↔ Windows source | inbound/read | acquire/accept and validate supported Windows source |
| Build Pipeline ↔ Windows image | both | inspect/service image according to Build Manifest |
| SplitOS Runtime ↔ Windows | both | apply/read live system state |
| SplitOS ↔ Game Client | both | discover library, launch game/client, read platform state |
| SplitOS ↔ Game | both/limited | launch, observe lifecycle, read/write supported configuration |
| SplitOS ↔ Drivers | indirect | capabilities and supported configuration through Windows/vendor interfaces |
| SplitOS ↔ Hardware | indirect | actual device state/capability |
| SplitOS ↔ Shared Apps | both/limited | lifecycle and gaming representation |
| SplitOS ↔ Microsoft update source | inbound | base patches used by SplitOS release lifecycle |
| SplitOS ↔ Account backend | both | identity / entitlement evidence |
| User ↔ SplitOS | both | intent, decisions, observable product state |

Этот документ пока не определяет конкретный protocol/API для этих границ.

---

# 18. Boundary invariants

## BND-INV-001

Windows 11 source является внешним Microsoft-owned build input; SplitOS-owned artifacts — Builder, manifests, packages и compatibility knowledge.

## BND-INV-002

Поддерживаемый SplitOS baseline формируется до clean installation; arbitrary existing Windows state не является нормальным supported baseline.

## BND-INV-003

Work Mode и Game Mode находятся внутри SplitOS product/runtime boundary и являются взаимоисключающими runtime contexts.

## BND-INV-004

Build Pipeline и Installed Runtime имеют разные responsibilities: первый формирует baseline, второй управляет live state.

## BND-INV-005

`MODE_MANAGED` component может иметь разное Work/Game state, оставаясь установленным в одном Windows baseline.

## BND-INV-006

External Game Client может участвовать в Game Mode flow, не становясь внутренней частью SplitOS.

## BND-INV-007

SplitOS Game Launcher находится внутри SplitOS product boundary.

## BND-INV-008

Game process остаётся внешним product/runtime boundary даже когда SplitOS изменяет поддерживаемые настройки игры.

## BND-INV-009

Hardware capability не определяется исключительно сохранённым SplitOS profile.

## BND-INV-010

SplitOS-controlled Windows update policy не делает SplitOS владельцем Windows patches.

## BND-INV-011

Windows License, SplitOS Entitlement и Game Platform License являются разными authority domains.

## BND-INV-012

Участие внешней системы в end-to-end flow не означает её включение в SplitOS ownership boundary.

---

# 19. Open boundary questions

Следующие вопросы сознательно не закрываются на boundary-этапе:

- какой Microsoft-authorized source acquisition mechanism будет использован Builder;
- какие именно image servicing operations выполняются offline / setup / first boot;
- какие конкретные Windows components входят в REMOVE/DISABLE/MODE_MANAGED/KEEP для v1;
- какие именно runtime responsibility zones станут отдельными процессами/службами;
- где физически будет находиться canonical active-mode state;
- какой механизм удерживает direct game launch до завершения transition;
- как runtime верифицирует `MODE_MANAGED` state;
- какие Game Client interfaces доступны и стабильны;
- какие game configuration mechanisms допустимы для каждой игры;
- какой update/recovery mechanism будет использоваться физически;
- где проходит trust boundary SplitOS identity/licensing subsystem;
- как обнаруживается baseline drift.

Они относятся к Responsibilities, Ownership, Interfaces, Integrations, States, Data и Failures.

---

# 20. Result

Boundary analysis фиксирует следующую модель:

```text
Microsoft-authorized Windows Source
                │
                ▼
┌──────── SplitOS Build Boundary ────────┐
│ Media Builder                         │
│ Build Manifest                        │
│ Component Matrix                      │
│ SplitOS Packages                      │
└────────────────┬──────────────────────┘
                 │
                 ▼
      Prepared SplitOS Baseline
                 │
            clean install
                 │
                 ▼
User ──→ ┌──── Installed SplitOS Runtime Boundary ────┐
         │ Account / Mode / Transition / Game UX      │
         │ MODE_MANAGED lifecycle / Profiles / Update │
         └────────────────────┬────────────────────────┘
                              │
                         Windows 11
                              │
                ┌─────────────┼─────────────┐
                │             │             │
             Drivers      Applications   Game Clients
                │                           │
             Hardware                     Games
```

Следующий шаг анализа:

```text
BOUNDARIES
    ↓
RESPONSIBILITIES
```

Теперь можно разделять SplitOS на реальные зоны ответственности, не смешивая build-time image engineering с installed runtime orchestration и не придумывая компоненты раньше времени.
