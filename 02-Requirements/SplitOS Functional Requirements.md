# SplitOS — Functional Requirements

## 1. Purpose

Документ определяет функциональные требования к SplitOS на основании актуального Concept / Pre-analysis и System Context.

Требования фиксируют **что должна делать система**, но не определяют конкретные Windows API, внутреннюю компонентную архитектуру, формат хранения данных или реализацию алгоритмов.

Все требования имеют статус `DRAFT`, пока не пройдут отдельную review / confirmation процедуру.

---

# 2. Requirement notation

```text
FR-DIST-XXX      Distribution
FR-USER-XXX      User / key context
FR-MODE-XXX      Mode management
FR-TRANS-XXX     Mode transition
FR-WORK-XXX      Work Mode
FR-GAME-XXX      Game Mode / games
FR-DISPLAY-XXX   Display
FR-AUDIO-XXX     Audio
FR-INPUT-XXX     Input
FR-APP-XXX       Applications
FR-SHARED-XXX    Shared Apps
FR-LAUNCHER-XXX  SplitOS Game Launcher
FR-HW-XXX        Hardware detection
FR-OPT-XXX       Game optimization
FR-STORAGE-XXX   Storage
FR-UPDATE-XXX    Updates
FR-SETUP-XXX     Initial setup
FR-MANAGER-XXX   SplitOS Manager
FR-RECOVERY-XXX  Recovery
FR-FUTURE-XXX    Future extensibility
```

---

# 3. Distribution Requirements

## FR-DIST-001

SplitOS должен распространяться как отдельный управляемый дистрибутив на базе Windows 11.

## FR-DIST-002

Полноценная SplitOS-среда не должна зависеть от установки SplitOS Manager поверх произвольной существующей пользовательской Windows.

## FR-DIST-003

SplitOS distribution должен включать контролируемую Windows base version и совместимые SplitOS-компоненты.

## FR-DIST-004

SplitOS distribution должен позволять заранее удалять, отключать или конфигурировать Windows-компоненты, если это необходимо для согласованного состояния продукта.

## FR-DIST-005

SplitOS distribution должен позволять предустанавливать собственные SplitOS-компоненты и необходимые поддерживаемые зависимости.

## FR-DIST-006

Windows kernel не должен заменяться собственным kernel SplitOS.

## FR-DIST-007

Windows Shell должна оставаться базовой системной оболочкой Windows.

---

# 4. User / Key Context

## FR-USER-001

После стандартного Windows sign-in SplitOS должен определить собственный пользовательский контекст до выбора режима.

## FR-USER-002

SplitOS должен иметь собственную систему пользовательских и ключевых данных.

## FR-USER-003

User/key context должен иметь возможность связываться с персональными SplitOS-настройками и профилями.

## FR-USER-004

User/key context должен иметь возможность использоваться для определения update entitlement.

## FR-USER-005

Mode selection не должен происходить до разрешения необходимого SplitOS user/key context.

> Точный способ хранения пользователей, ключей, entitlement и проверки лицензии определяется отдельно.

---

# 5. Mode Management

## FR-MODE-001

SplitOS должен предоставлять два основных режима:

```text
Work Mode
Game Mode
```

## FR-MODE-002

Work Mode и Game Mode должны использовать одну установленную Windows 11.

## FR-MODE-003

Work Mode и Game Mode должны быть взаимоисключающими активными состояниями.

```text
WORK xor GAME
```

## FR-MODE-004

SplitOS не должен допускать состояние, в котором Work Mode и Game Mode одновременно считаются активными.

## FR-MODE-005

Переключение режимов не должно запускать вторую Windows OS или отдельную Windows session.

## FR-MODE-006

После Windows sign-in и разрешения SplitOS user/key context пользователь должен выбрать активный режим.

## FR-MODE-007

SplitOS должен отображать текущий активный режим.

## FR-MODE-008

Пользователь должен иметь возможность вручную выполнить:

```text
Work Mode → Game Mode
Game Mode → Work Mode
```

## FR-MODE-009

Закрытие игры не должно автоматически переключать систему в Work Mode.

## FR-MODE-010

После закрытия игры Game Mode должен оставаться активным до отдельной команды пользователя на возврат в Work Mode.

---

# 6. Work → Game Transition

## FR-TRANS-001

Переход Work → Game должен выполняться как управляемый transition flow.

## FR-TRANS-002

Перед деактивацией Work Mode SplitOS должен выполнить pre-flight проверку активного рабочего состояния.

## FR-TRANS-003

Pre-flight должен иметь возможность обнаруживать как минимум потенциально значимые категории процессов:

- приложения с несохранённым пользовательским состоянием;
- документы;
- локальные серверы;
- long-running tasks;
- процессы, которые нельзя безопасно завершить автоматически.

## FR-TRANS-004

Если приложение предоставляет безопасный поддерживаемый механизм сохранения, SplitOS должен иметь возможность сохранить пользовательское состояние перед закрытием.

## FR-TRANS-005

Если безопасное автоматическое сохранение невозможно гарантировать, SplitOS должен уведомить пользователя до завершения соответствующего приложения.

## FR-TRANS-006

Для активных серверов и других значимых фоновых процессов SplitOS должен предоставить пользователю решение о закрытии или сохранении процесса.

## FR-TRANS-007

Пользователь должен иметь возможность отменить Work → Game transition при наличии блокирующих процессов.

## FR-TRANS-008

Если transition отменён, Work Mode должен оставаться активным.

## FR-TRANS-009

SplitOS не должен считать Game Mode активным до завершения обязательных transition actions.

## FR-TRANS-010

Если поддерживаемая игра уже была запущена до завершения Work → Game transition, SplitOS должен иметь возможность завершить этот игровой процесс и связанные с неуспешным игровым стартом процессы.

## FR-TRANS-011

После неуспешного или отменённого перехода SplitOS должен оставить систему в согласованном Work Mode state.

---

# 7. Work Mode

## FR-WORK-001

Work Mode должен сохранять стандартную Windows Shell и базовый Windows desktop UX.

## FR-WORK-002

Work Mode должен быть ориентирован на рабочие процессы.

## FR-WORK-003

В Work Mode игровые службы и игровые фоновые процессы, не требуемые текущим рабочим сценарием, не должны создавать ненужную системную нагрузку.

## FR-WORK-004

Игровые клиенты не должны автоматически создавать ненужную фоновую активность в Work Mode.

## FR-WORK-005

Work Mode должен иметь собственные системные профили, включая как минимум:

- display profile;
- audio profile;
- power policy;
- application lifecycle policy.

## FR-WORK-006

Work Mode должен поддерживать стандартное использование keyboard & mouse.

## FR-WORK-007

Глубокий кастомный Work Mode UI не является обязательным требованием первой версии.

## FR-WORK-008

Архитектура SplitOS не должна запрещать дальнейшее появление Work Mode workspace customization, Notes, layouts и других productivity-функций.

---

# 8. Game Mode

## FR-GAME-001

Game Mode должен быть отдельным взаимоисключающим активным состоянием SplitOS.

## FR-GAME-002

Game Mode должен быть ориентирован на gaming / entertainment UX.

## FR-GAME-003

Game Mode должен предоставлять собственный SplitOS Game Launcher как основную пользовательскую поверхность игрового сценария.

## FR-GAME-004

Game Mode должен иметь собственные:

- display policy;
- audio policy;
- power policy;
- application policy;
- input policy;
- game profile policy;
- shared-app policy.

## FR-GAME-005

Game Mode должен поддерживать controller-friendly navigation.

## FR-GAME-006

Game Mode не должен требовать обязательного использования controller для всех игр.

## FR-GAME-007

Game Mode должен поддерживать Keyboard & Mouse game profiles.

## FR-GAME-008

Game Mode должен поддерживать Controller game profiles.

## FR-GAME-009

После завершения игры Game Mode должен возвращать пользователя в SplitOS Game Launcher.

## FR-GAME-010

Из Game Launcher пользователь должен иметь возможность запустить другую игру или вручную переключиться в Work Mode.

---

# 9. Game / Game Client Classification

## FR-APP-001

SplitOS должен различать как минимум следующие application classes:

```text
WORK
GAME
GAME_CLIENT
SHARED
SYSTEM
```

## FR-APP-002

`GAME` и `GAME_CLIENT` должны иметь разное системное поведение.

## FR-APP-003

Запуск `GAME_CLIENT` из Work Mode сам по себе не должен принудительно активировать Game Mode.

## FR-APP-004

Запуск поддерживаемого `GAME` из Work Mode должен инициировать Work → Game transition.

## FR-APP-005

SplitOS должен иметь возможность определять application class на основании собственных поддерживаемых metadata и integration data.

## FR-APP-006

Пользовательская классификация неизвестных приложений может быть добавлена позднее и не является обязательным v1 requirement.

---

# 10. Direct Game Launch from Work Mode

## FR-GAME-020

Если пользователь инициирует запуск поддерживаемой игры непосредственно из Work Mode, SplitOS должен по возможности перехватить или приостановить игровой сценарий до полноценного запуска игры.

## FR-GAME-021

Direct launch должен быть преобразован в следующий логический flow:

```text
Game launch request
      ↓
Detect supported GAME
      ↓
Work → Game pre-flight
      ↓
Activate Game Mode
      ↓
Game Launcher orchestration
      ↓
Resolve Game Profile
      ↓
Apply environment
      ↓
Launch game
```

## FR-GAME-022

Если безопасный переход невозможен, SplitOS не должен продолжать нормальный игровой запуск из Work Mode.

## FR-GAME-023

При отменённом переходе система должна сохранить Work Mode.

---

# 11. Game Detection and Library

## FR-GAME-100

В первой версии SplitOS должен поддерживать игры, официально установленные через поддерживаемые игровые клиенты.

## FR-GAME-101

SplitOS должен иметь возможность обнаруживать официально установленные игры из поддерживаемых игровых библиотек.

## FR-GAME-102

SplitOS должен связывать игру с соответствующим внешним игровым клиентом.

## FR-GAME-103

SplitOS должен представлять поддерживаемые игры в собственной объединённой библиотеке Game Launcher.

## FR-GAME-104

SplitOS не должен брать на себя ownership лицензии игры.

## FR-GAME-105

External Game Client остаётся authority для установки, обновления и лицензии игры.

## FR-GAME-106

Ручное добавление standalone / unofficial / arbitrary executable игр не является обязательным v1 requirement.

## FR-GAME-107

Системная модель не должна запрещать ручное добавление игр в будущих версиях.

---

# 12. SplitOS Game Launcher

## FR-LAUNCHER-001

SplitOS должен иметь собственный внутренний Game Launcher.

## FR-LAUNCHER-002

Game Launcher должен быть частью SplitOS и ключевым UX-компонентом Game Mode.

## FR-LAUNCHER-003

Game Launcher должен предоставлять единый интерфейс поверх поддерживаемых внешних игровых клиентов.

## FR-LAUNCHER-004

Game Launcher должен отображать объединённую библиотеку поддерживаемых официально установленных игр.

## FR-LAUNCHER-005

Game Launcher должен поддерживать controller-oriented navigation.

## FR-LAUNCHER-006

Game Launcher должен предоставлять доступ к Game Profiles.

## FR-LAUNCHER-007

Game Launcher должен предоставлять доступ к выбору target display.

## FR-LAUNCHER-008

Game Launcher должен предоставлять доступ к выбору input profile.

## FR-LAUNCHER-009

Game Launcher должен предоставлять доступ к поддерживаемым игровым настройкам SplitOS.

## FR-LAUNCHER-010

Перед запуском игры Game Launcher должен разрешить необходимые элементы игрового контекста:

- current hardware state;
- target display;
- selected Game Profile;
- input profile;
- supported game configuration;
- required external Game Client.

## FR-LAUNCHER-011

Game Launcher должен запускать игру через соответствующий внешний Game Client, если этого требует платформа игры.

## FR-LAUNCHER-012

Game Launcher должен скрывать от пользователя лишние промежуточные действия внешнего клиента там, где это возможно без нарушения его работы.

## FR-LAUNCHER-013

Game Launcher не должен становиться владельцем аккаунтов, магазинов, лицензий и внутренних update-механизмов внешних игровых платформ.

---

# 13. Multiple Game Profiles

## FR-GAME-200

SplitOS должен поддерживать несколько Game Profiles для одной игры.

## FR-GAME-201

Game Profile должен иметь возможность различаться по пользовательскому сценарию.

Пример:

```text
Game
├── Desktop / 1440p / 280 Hz / Keyboard & Mouse
└── TV / 4K / 60 Hz / Controller
```

## FR-GAME-202

Game Profile может включать как минимум:

- target display;
- input profile;
- resolution;
- refresh rate;
- target FPS;
- graphics configuration;
- hardware snapshot;
- external launcher relation.

## FR-GAME-203

Пользователь должен иметь возможность выбирать доступный Game Profile.

## FR-GAME-204

SplitOS должен иметь возможность автоматически выбирать профиль на основании целевого display / input scenario, если такое правило определено.

---

# 14. Display Management

## FR-DISPLAY-001

SplitOS должен использовать все подключённые дисплеи, которые Windows и поддерживаемые драйверы корректно обнаруживают.

## FR-DISPLAY-002

SplitOS не должен ограничивать Game Mode единственным фиксированным монитором или TV.

## FR-DISPLAY-003

Пользователь должен иметь возможность выбрать целевой display для Work Mode.

## FR-DISPLAY-004

Пользователь должен иметь возможность выбрать целевой display для Game Mode / Game Profile.

## FR-DISPLAY-005

Пользователь должен иметь возможность временно переключить Game Mode на другой доступный display.

## FR-DISPLAY-006

Display profile должен иметь возможность учитывать:

- resolution;
- refresh rate;
- HDR capability/state;
- VRR capability/state;
- scaling;
- primary display;
- enabled displays.

## FR-DISPLAY-007

При входе в Game Mode SplitOS должен применить выбранный Game display profile.

## FR-DISPLAY-008

При возврате в Work Mode SplitOS должен применить Work display profile.

## FR-DISPLAY-009

SplitOS должен обрабатывать отсутствие ранее выбранного display через fallback policy.

## FR-DISPLAY-010

Характеристики текущего Game display должны использоваться как входные данные для Game Profile / optimization.

---

# 15. Audio Management

## FR-AUDIO-001

SplitOS должен обнаруживать доступные аудиоустройства через возможности базовой платформы.

## FR-AUDIO-002

Пользователь должен иметь возможность задать Work audio profile.

## FR-AUDIO-003

Пользователь должен иметь возможность задать Game audio profile.

## FR-AUDIO-004

SplitOS должен применять соответствующий audio profile при mode transition.

## FR-AUDIO-005

SplitOS должен иметь fallback behavior при отсутствии сохранённого устройства.

---

# 16. Input Management

## FR-INPUT-001

SplitOS должен поддерживать как минимум два игровых input profile:

```text
Keyboard & Mouse
Controller
```

## FR-INPUT-002

Input profile должен назначаться Game Profile, а не только игре целиком.

## FR-INPUT-003

Одна игра должна иметь возможность использовать разные input profiles в разных Game Profiles.

## FR-INPUT-004

SplitOS должен обеспечивать удобное переключение устройств ввода при mode transition и выборе Game Profile.

## FR-INPUT-005

SplitOS должен различать:

```text
System navigation
Game input
```

## FR-INPUT-006

При передаче input самой игре SplitOS не должен вмешиваться в игровой input без необходимости.

## FR-INPUT-007

Для controller-first Game Mode SplitOS должен поддерживать navigation вне игры.

## FR-INPUT-008

Для приложений без controller support SplitOS должен иметь возможность использовать fallback navigation.

## FR-INPUT-009

Fallback navigation потенциально может включать:

- keyboard emulation;
- mouse emulation;
- application-specific mappings.

## FR-INPUT-010

Точная input routing architecture определяется позднее.

---

# 17. Shared Applications

## FR-SHARED-001

SplitOS должен поддерживать class `SHARED` для приложений, используемых в обоих режимах.

## FR-SHARED-002

В Work Mode Shared App должен иметь возможность работать как обычное Windows-приложение.

## FR-SHARED-003

В Game Mode Shared App должен иметь возможность использовать отдельное presentation state.

Потенциальные состояния:

```text
Overlay
Locked window
Secondary display
Background
```

## FR-SHARED-004

В Game Mode одновременно допускается не более трёх активных Shared Apps.

## FR-SHARED-005

SplitOS должен иметь возможность размещать Shared App на другом доступном display, если выбран соответствующий presentation state.

## FR-SHARED-006

SplitOS должен иметь возможность применять controller navigation policy к Shared App.

## FR-SHARED-007

Приложение остаётся владельцем собственных данных и бизнес-логики.

## FR-SHARED-008

Точная модель SplitOS Overlay определяется отдельно на этапе Analysis & Design.

---

# 18. Hardware Detection

## FR-HW-001

При запуске SplitOS Game Launcher система должна перечитать актуальные характеристики доступного игрового окружения.

## FR-HW-002

Hardware detection должен иметь возможность учитывать как минимум:

- CPU;
- GPU;
- GPU memory;
- RAM;
- storage context;
- connected displays;
- display resolution;
- display refresh rate;
- HDR capability;
- VRR capability;
- available input devices.

## FR-HW-003

Перед запуском игры SplitOS должен сравнить актуальное hardware/display state с состоянием, связанным с выбранным Game Profile.

## FR-HW-004

Если значимых изменений не обнаружено, SplitOS должен иметь возможность использовать существующий профиль без полного перерасчёта.

## FR-HW-005

Если hardware/display state изменился, SplitOS должен скорректировать или перерассчитать Game Profile перед запуском игры.

---

# 19. Game Experience Optimization

## FR-OPT-001

SplitOS должен иметь возможность формировать рекомендуемую игровую конфигурацию на основании текущего железа, активного display и поддерживаемых параметров игры.

## FR-OPT-002

Главной целью optimization должна быть максимально высокая визуальная quality при достижении максимально стабильной производительности, соответствующей возможностям активного display и PC.

## FR-OPT-003

Если hardware способен стабильно обеспечить FPS, соответствующий refresh rate display, SplitOS должен стремиться сохранить этот FPS и максимизировать visual quality.

## FR-OPT-004

Для display с ограниченной частотой обновления и избыточной мощностью PC SplitOS должен иметь возможность использовать максимальное поддерживаемое качество при сохранении стабильного целевого FPS.

Пример:

```text
4K / 60 Hz + powerful PC
→ stable 60 FPS
→ highest sustainable quality
```

## FR-OPT-005

Если достижение refresh rate требует снижения graphics quality, SplitOS должен иметь возможность снижать настройки до достижения стабильного target FPS.

## FR-OPT-006

Если refresh rate display физически недостижим на текущем hardware, SplitOS должен иметь возможность снижать graphics quality и максимизировать устойчивый FPS.

## FR-OPT-007

При наличии безопасного поддерживаемого интерфейса SplitOS должен автоматически применять рекомендуемые настройки без обязательного дополнительного подтверждения пользователя.

## FR-OPT-008

Автоматически применённые параметры должны оставаться доступны пользователю для ручного изменения.

## FR-OPT-009

SplitOS не должен блокировать ручное изменение graphics settings пользователем в самой игре.

## FR-OPT-010

SplitOS не должен изменять игровые executable с целью обхода ограничений.

## FR-OPT-011

SplitOS не должен изменять:

- anti-cheat;
- DRM;
- matchmaking;
- network code;
- server-side logic.

## FR-OPT-012

SplitOS не должен подменять игровой input с целью получения преимущества.

---

# 20. Application Lifecycle

## FR-APP-100

SplitOS должен иметь отдельные lifecycle policies для Work Mode и Game Mode.

## FR-APP-101

SplitOS должен иметь возможность определять, должен ли процесс:

- запускаться;
- оставаться запущенным;
- быть остановлен;
- быть исключён из автоматического управления;
- иметь отдельное presentation state.

## FR-APP-102

SplitOS не должен без безопасного механизма молча уничтожать пользовательские данные при mode transition.

## FR-APP-103

Игровые процессы, не соответствующие активному Work Mode, должны иметь возможность очищаться в рамках согласованной Work policy.

## FR-APP-104

Рабочие процессы, не соответствующие Game Mode, должны обрабатываться Work → Game pre-flight policy.

---

# 21. Storage

## FR-STORAGE-001

SplitOS должен поддерживать конфигурацию с одним физическим SSD.

## FR-STORAGE-002

SplitOS не должен требовать отдельного дискового раздела для Game Mode.

## FR-STORAGE-003

SplitOS должен поддерживать игровые библиотеки на дополнительных накопителях.

## FR-STORAGE-004

External Game Client остаётся владельцем собственной физической game library.

## FR-STORAGE-005

SplitOS должен иметь возможность хранить relation между Game, Game Client и installation location.

---

# 22. Initial Setup

## FR-SETUP-001

Первоначальная настройка SplitOS должна выполняться в контролируемом SplitOS distribution environment.

## FR-SETUP-002

Initial Setup должен иметь возможность определить доступные:

- displays;
- audio devices;
- input devices;
- supported game clients;
- official game libraries;
- основные hardware characteristics.

## FR-SETUP-003

Пользователь должен иметь возможность задать начальные Work / Game display profiles.

## FR-SETUP-004

Пользователь должен иметь возможность задать Work / Game audio profiles.

## FR-SETUP-005

Пользователь должен иметь возможность настроить базовые input preferences.

## FR-SETUP-006

Initial Setup должен создать исходную конфигурацию двух взаимоисключающих режимов.

## FR-SETUP-007

Все initial settings должны иметь возможность быть изменены позднее через SplitOS.

---

# 23. SplitOS Manager

## FR-MANAGER-001

SplitOS Manager должен предоставлять ручное управление основными SplitOS settings.

## FR-MANAGER-002

Manager должен отображать текущий active mode.

## FR-MANAGER-003

Manager должен позволять вручную инициировать mode transition.

## FR-MANAGER-004

Manager должен предоставлять управление:

- Work Mode settings;
- Game Mode settings;
- display profiles;
- audio profiles;
- input profiles;
- Game Profiles;
- application classification;
- Shared Apps policy;
- optimization preferences;
- update / recovery information.

## FR-MANAGER-005

Manager не обязан дублировать весь Windows Settings UX.

## FR-MANAGER-006

Game Mode primary UX должен оставаться ответственностью Game Launcher, а не Manager.

---

# 24. Windows / SplitOS Update Policy

## FR-UPDATE-001

Стандартное автоматическое применение Windows feature/system updates в SplitOS distribution должно быть отключено.

## FR-UPDATE-002

Microsoft Windows patches должны рассматриваться как внешний источник изменений базовой платформы.

## FR-UPDATE-003

Patch не должен считаться поддерживаемым SplitOS до завершения compatibility validation.

## FR-UPDATE-004

Совместимые Windows patches должны включаться в отдельный SplitOS distribution release.

## FR-UPDATE-005

SplitOS должен иметь собственный update delivery lifecycle.

## FR-UPDATE-006

Update entitlement должен иметь возможность определяться через SplitOS user/key context.

## FR-UPDATE-007

SplitOS update process должен сохранять пользовательские профили, если migration не требует иного явно определённого поведения.

## FR-UPDATE-008

Security patches должны проходить контролируемую validation / distribution процедуру.

## FR-UPDATE-009

GPU / peripheral driver updates должны иметь отдельную compatibility policy.

---

# 25. Recovery

## FR-RECOVERY-001

SplitOS должен иметь recovery behavior для неуспешного mode transition.

## FR-RECOVERY-002

SplitOS должен уметь определить частично применённый mode state.

## FR-RECOVERY-003

При ошибке Work → Game transition SplitOS должен иметь возможность вернуть согласованный Work state.

## FR-RECOVERY-004

При ошибке Game → Work transition SplitOS должен иметь fallback / rollback behavior.

## FR-RECOVERY-005

SplitOS должен фиксировать неуспешно применённые операции и сообщать пользователю результат.

## FR-RECOVERY-006

Distribution update process должен предусматривать rollback или другой безопасный recovery mechanism.

## FR-RECOVERY-007

Recovery не должен без явной необходимости удалять пользовательские Game Profiles и персональные SplitOS settings.

---

# 26. v1 Product Priority

## FR-V1-001

Game Mode UI и SplitOS Game Launcher являются core scope первой версии.

## FR-V1-002

v1 должна поддерживать официально установленные игры из поддерживаемых Game Clients.

## FR-V1-003

v1 должна поддерживать все корректно обнаруженные подключённые displays в рамках возможностей Windows / drivers.

## FR-V1-004

v1 должна обеспечивать управление input devices между Work / Game scenarios.

## FR-V1-005

v1 должна поддерживать Keyboard & Mouse и Controller profiles.

## FR-V1-006

Глубокий собственный Work Mode UI может быть отложен после стабилизации Game Mode platform.

---

# 27. Future Extensibility

## FR-FUTURE-001

Системная модель не должна запрещать ручное добавление standalone / unofficial games в будущих версиях.

## FR-FUTURE-002

Системная модель не должна запрещать SplitOS Overlay expansion.

## FR-FUTURE-003

Системная модель не должна запрещать интеграции OBS / Twitch и сценарии game streaming / secondary display entertainment.

## FR-FUTURE-004

Системная модель не должна запрещать специализированные partner builds приложений, например Discord integration.

## FR-FUTURE-005

Системная модель не должна запрещать SplitOS-certified OEM controllers и другие устройства.

## FR-FUTURE-006

Системная модель не должна запрещать собственные коммуникационные или social-компоненты SplitOS.

## FR-FUTURE-007

Системная модель не должна запрещать глубокую кастомизацию Work Mode workspace в последующих версиях.

---

# 28. Remaining Open Requirements Questions

Следующие вопросы не противоречат уже подтверждённой концепции, но требуют детализации перед Analysis & Design.

## Transition safety

**REQ-OPEN-001**  
Какие типы приложений поддерживают гарантированное auto-save / graceful close, а какие требуют обязательного подтверждения?

**REQ-OPEN-002**  
Как формализовать правила для local servers / long-running tasks при Work → Game transition?

## Supported clients

**REQ-OPEN-003**  
Какие Game Clients входят в обязательный compatibility set v1?

**REQ-OPEN-004**  
Какой набор игр используется как reference set для разработки Game Profile / optimization engine?

## Shared Apps

**REQ-OPEN-005**  
Какие presentation states обязательны в первой версии Game Mode?

**REQ-OPEN-006**  
Что происходит при попытке открыть четвёртое Shared App?

## Optimization

**REQ-OPEN-007**  
Как учитывать VRR при выборе target FPS?

**REQ-OPEN-008**  
Как учитывать DLSS / FSR / XeSS, frame generation и dynamic resolution?

**REQ-OPEN-009**  
Как сохранять manual user overrides при последующем автоматическом перерасчёте профиля после изменения hardware/display?

## User / Update

**REQ-OPEN-010**  
Как реализуется SplitOS user/key storage и entitlement validation?

**REQ-OPEN-011**  
Какой update rollback mechanism является обязательным?

**REQ-OPEN-012**  
Какой target SLA нужен для критических Microsoft security patches?

---

# 29. Result

Актуальная функциональная модель SplitOS строится вокруг следующего поведения:

```text
Controlled SplitOS Distribution
        ↓
Windows sign-in
        ↓
SplitOS user/key context
        ↓
Mode selection
        ↓
WORK xor GAME
```

При запуске поддерживаемой игры из Work Mode:

```text
GAME detected
      ↓
Transition pre-flight
      ↓
Resolve work blockers
      ↓
Activate Game Mode
      ↓
Game Launcher orchestration
      ↓
Hardware / display refresh
      ↓
Resolve / update Game Profile
      ↓
Apply supported settings
      ↓
Launch through Game Client
```

После завершения игры пользователь остаётся в Game Mode и возвращается в SplitOS Game Launcher.

Game Client сам по себе не является `GAME` и не должен автоматически переключать режим.

Первая версия продукта ориентирована прежде всего на стабильный Game Mode UX, официальный game-client library, все доступные displays, управление input devices и hardware-aware Game Profiles. Глубокая кастомизация Work Mode остаётся расширяемой, но вторичной продуктовой зоной.
