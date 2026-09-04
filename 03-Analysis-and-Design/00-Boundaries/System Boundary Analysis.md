# SplitOS — System Boundary Analysis

## 1. Purpose

Документ определяет аналитические границы SplitOS перед декомпозицией ответственности и ownership.

Цель — отделить:

- то, чем SplitOS владеет как продукт;
- базовую Windows-платформу, которую SplitOS поставляет и конфигурирует, но не разрабатывает;
- внешние системы и устройства;
- области, которые входят в текущий change surface;
- области, которые участвуют во flow, но остаются внешними ownership boundaries.

Документ не фиксирует внутреннюю компонентную архитектуру SplitOS.

---

# 2. Object of analysis

Объект анализа:

> **SplitOS как управляемый дистрибутив и продуктовая платформа поверх Windows 11, предоставляющая два взаимоисключающих пользовательских контекста — Work Mode и Game Mode — и управляющая системным состоянием, игровым UX, профилями, устройствами и совместимым lifecycle базовой Windows.**

---

# 3. Boundary model

Для SplitOS необходимо различать три разные границы.

## 3.1 Product boundary

Показывает, за какое поведение отвечает продукт SplitOS.

Внутри product boundary находятся:

- SplitOS user/key context;
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
- SplitOS distribution update policy;
- recovery coordination;
- diagnostics and observability SplitOS.

## 3.2 Distribution boundary

Показывает, что физически входит в поставляемый SplitOS distribution.

Внутри distribution boundary находятся:

- Windows 11 base image;
- выбранный набор Windows components;
- SplitOS-owned software;
- SplitOS configuration and policies;
- preconfigured system settings;
- supported integration adapters;
- recovery/update assets SplitOS.

Важно:

> Windows 11 находится внутри **distribution boundary**, но не внутри **SplitOS implementation ownership**.

SplitOS поставляет и контролирует совместимую Windows-базу, однако Microsoft остаётся владельцем Windows kernel, системных компонентов, API semantics и исходных Windows patches.

## 3.3 Runtime ownership boundary

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
├── User Context
├── Mode Intent
├── Mode Policy
├── Mode Transition
├── Work Context Policy
├── Game Context Policy
├── Game Launcher UX
├── Game Library View
├── Game Profile Policy
├── Application Policy
├── Shared App Policy
├── Display Preference Policy
├── Audio Preference Policy
├── Input Preference Policy
├── Game Optimization Policy
├── Distribution Compatibility Policy
├── Update Orchestration
├── Recovery Coordination
└── Diagnostics
```

Это responsibility areas, а не названия будущих сервисов или процессов.

---

# 5. Windows boundary

Windows 11 является базовой платформой SplitOS.

SplitOS использует Windows для фактического выполнения системных операций, включая:

- process execution;
- service control;
- display configuration;
- audio routing;
- input/device management;
- file system operations;
- user session behavior;
- security enforcement;
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

# 6. Windows Shell boundary

Windows Shell остаётся внешней платформенной зоной относительно SplitOS implementation ownership.

SplitOS:

- не заменяет Explorer как обязательную system shell;
- не создаёт новую login shell;
- не требует собственного desktop environment;
- может отображать собственный fullscreen Game Mode UX поверх Windows;
- может позднее расширять Work Mode UX без изменения базовой shell ownership.

SplitOS Game Launcher является продуктовым UX-компонентом, но не Windows Shell replacement.

---

# 7. External Game Clients boundary

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

# 8. Game boundary

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

# 9. Hardware and driver boundary

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

# 10. Shared Applications boundary

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

# 11. Update boundary

Microsoft является источником Windows patches.

SplitOS является authority для решения:

> какая Windows base version считается совместимой с конкретным SplitOS release.

Таким образом:

```text
Microsoft
→ patch source

SplitOS
→ compatibility / release decision

Installed SplitOS Distribution
→ consumes approved release
```

Windows Update infrastructure не становится внутренним компонентом SplitOS только потому, что SplitOS ограничивает его стандартное поведение.

---

# 12. User boundary

Пользователь находится вне системы как primary actor.

Пользователь владеет конечным намерением:

- выбором Work или Game;
- решением отменить mode transition;
- разрешением спорных Work blockers;
- ручным выбором display/input profile;
- ручным изменением игровых настроек;
- явным переходом Game → Work.

SplitOS может автоматизировать действия, но не должен превращать продуктовую рекомендацию в необратимое пользовательское решение без необходимости.

---

# 13. Current change boundary

Для первой проектируемой версии основной change surface:

```text
SplitOS Distribution
│
├── Startup / user context
├── Mode selection
├── Mode isolation
├── Work → Game transition
├── Game → Work transition
├── Game Launcher
├── supported Game Client integrations
├── official game discovery
├── Game Profiles
├── display management
├── audio management
├── input management
├── game experience optimization
├── Shared Apps
├── update compatibility lifecycle
├── recovery
└── diagnostics
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

# 14. Boundary-crossing interactions

Основные внешние переходы, которые позднее потребуют interface/integration analysis:

| Boundary | Direction | Meaning |
|---|---|---|
| SplitOS ↔ Windows | both | apply/read system state |
| SplitOS ↔ Game Client | both | discover library, launch game/client, read platform state |
| SplitOS ↔ Game | both/limited | launch, observe lifecycle, read/write supported configuration |
| SplitOS ↔ Drivers | indirect | capabilities and supported configuration through Windows/vendor interfaces |
| SplitOS ↔ Hardware | indirect | actual device state/capability |
| SplitOS ↔ Shared Apps | both/limited | lifecycle and gaming representation |
| SplitOS ↔ Microsoft update source | inbound | base patches used by SplitOS release lifecycle |
| User ↔ SplitOS | both | intent, decisions, observable product state |

Этот документ пока не определяет конкретный protocol/API для этих границ.

---

# 15. Boundary invariants

## BND-INV-001

Windows 11 является частью поставляемого SplitOS distribution, но Microsoft-owned implementation не становится SplitOS-owned implementation.

## BND-INV-002

Work Mode и Game Mode находятся внутри SplitOS product boundary и являются взаимоисключающими runtime contexts.

## BND-INV-003

External Game Client может участвовать в Game Mode flow, не становясь внутренней частью SplitOS.

## BND-INV-004

SplitOS Game Launcher находится внутри SplitOS product boundary.

## BND-INV-005

Game process остаётся внешним product/runtime boundary даже когда SplitOS изменяет поддерживаемые настройки игры.

## BND-INV-006

Hardware capability не определяется исключительно сохранённым SplitOS profile.

## BND-INV-007

SplitOS-controlled Windows update policy не делает SplitOS владельцем Windows patches.

## BND-INV-008

Участие внешней системы в end-to-end flow не означает её включение в SplitOS ownership boundary.

---

# 16. Open boundary questions

Следующие вопросы сознательно не закрываются на boundary-этапе:

- какие именно runtime responsibility zones станут отдельными процессами/службами;
- где физически будет находиться canonical active-mode state;
- какой механизм удерживает direct game launch до завершения transition;
- какие Game Client interfaces доступны и стабильны;
- какие game configuration mechanisms допустимы для каждой игры;
- какой update/recovery mechanism будет использоваться физически;
- где проходит trust boundary SplitOS identity/licensing subsystem.

Они относятся к Responsibilities, Ownership, Interfaces, Integrations, States и Failures.

---

# 17. Result

Boundary analysis фиксирует следующую модель:

```text
User
  ↓
┌──────────────── SplitOS Product Boundary ────────────────┐
│                                                         │
│  Mode policy     Game UX       Profiles      Recovery   │
│       │              │             │             │      │
│       └──────────────┴──────┬──────┴─────────────┘      │
│                             │                           │
└─────────────────────────────┼───────────────────────────┘
                              │
                     Windows Platform
                              │
                ┌─────────────┼─────────────┐
                │             │             │
             Drivers      Applications   Game Clients
                │                           │
             Hardware                     Games
```

При этом distribution boundary шире product implementation boundary, потому что включает совместимую Windows 11 base image.

Следующий шаг анализа:

```text
BOUNDARIES
    ↓
RESPONSIBILITIES
```

То есть теперь можно разделять SplitOS на реальные зоны ответственности, не придумывая компоненты раньше времени.
