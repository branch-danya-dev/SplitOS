
# SplitOS — System Context

## 1. Purpose

Документ определяет системный контекст SplitOS:

- что входит в границу SplitOS;
- что находится за её пределами;
- с какими внешними системами и устройствами SplitOS взаимодействует;
- за какое поведение SplitOS отвечает самостоятельно;
- какое поведение остаётся ответственностью Windows, внешних приложений, драйверов и оборудования;
- какие системные зависимости необходимо учитывать при дальнейшем анализе и проектировании.

Документ не определяет внутреннюю архитектуру SplitOS и не фиксирует конкретные технологии реализации.

# 2. System definition

**SplitOS** — отдельный управляемый программный дистрибутив и продуктовая платформа-обёртка над Windows 11, которая разделяет рабочий и игровой пользовательские контексты, управляет переходами между ними и предоставляет собственный UX-слой для игрового сценария.

SplitOS предоставляет два основных режима:

```text
Work Mode
Game Mode
```

Work Mode и Game Mode используют одну установленную Windows 11, но являются **взаимоисключающими активными состояниями**. После выбора режима одновременно активен только один из них.

SplitOS не является самостоятельной операционной системой с собственным kernel. Windows 11 остаётся базовой операционной системой и владельцем низкоуровневого системного поведения.

SplitOS распространяется как отдельный контролируемый дистрибутив, а не как универсальный установщик поверх произвольной пользовательской Windows.

После стандартного Windows sign-in SplitOS должен определить собственный пользовательский и ключевой контекст, после чего предоставить выбор режима.

```text
Windows boot
    ↓
Windows sign-in
    ↓
SplitOS user / key context
    ↓
Mode selection
    ↓
WORK xor GAME
```

SplitOS определяет желаемый пользовательский и системный контекст, координирует его применение и предоставляет собственные продуктовые слои там, где это необходимо для целостного UX.

В Game Mode ключевым продуктовым компонентом является собственный SplitOS Game Launcher, который выступает единым UX-слоем и фильтром поверх внешних игровых клиентов, официально установленных игр, профилей и связанных настроек.

Концептуально:

```text
                    SplitOS Distribution
                            │
                       Windows 11 Base
                            │
                       SplitOS Layer
                            │
                 ┌──────────┴──────────┐
                 │                     │
             Work Mode             Game Mode
                 │                     │
                 │            SplitOS Game Launcher
                 │                     │
                 └──────────┬──────────┘
                            ▼
                         Windows
                            │
              ┌─────────────┼─────────────┐
              ▼             ▼             ▼
           Drivers      Applications   Game Clients
              │                            │
              ▼                            ▼
           Hardware                       Games
```

# 3. System boundary

## 3.1 Inside SplitOS boundary

На текущем уровне анализа в границу SplitOS входят следующие ответственности:

```text
SplitOS Distribution
│
├── Base image preparation
├── SplitOS user / key context
├── Mode selection after sign-in
├── Mode configuration
├── Mode transition coordination
├── Work → Game pre-flight validation
├── User preferences
├── Profile configuration
├── Application classification
├── Gaming input profiles
├── Display profiles
├── Audio profiles
├── Power profiles
├── SplitOS Game Launcher
├── Official game discovery through supported clients
├── Game profiles
├── Hardware / display detection
├── Game experience optimization policy
├── Shared Apps policy / Game Mode presentation
├── Configuration persistence
├── SplitOS distribution update policy
├── State validation
├── Recovery coordination
└── User interaction through SplitOS Manager / Game Mode UI
```

SplitOS является владельцем логики:

> какой режим должен быть активен, какое целевое состояние соответствует этому режиму и какой UX должен быть предоставлен пользователю.

SplitOS также является владельцем orchestration-сценария запуска поддерживаемой игры: перед запуском должны быть разрешены mode transition, game profile, target display, input profile и поддерживаемые игровые настройки.

SplitOS использует Windows и внешние системы для фактического применения системных параметров, но сохраняет ownership собственной продуктовой логики, профилей, Game Launcher, update lifecycle и переходов между режимами.

# 4. Outside SplitOS boundary

SplitOS не владеет внутренней реализацией следующих областей:

```
Windows kernel
Windows Shell
Windows drivers
GPU driver implementation
Audio driver implementation
Game execution
Game rendering
Game installation internals
External game launcher business logic
Third-party application internals
Hardware firmware
Anti-cheat systems
DRM systems
Windows Update infrastructure
```

SplitOS может взаимодействовать с этими областями, но не становится владельцем их внутреннего поведения.

# 5. Primary actor

## User

Основным актором SplitOS является пользователь SplitOS-PC.

Пользователь:

- проходит стандартный Windows sign-in;
- после определения SplitOS user/key context выбирает Work Mode или Game Mode;
- может вручную переключаться между режимами;
- задаёт предпочтительные display / audio / input profiles;
- выбирает и изменяет игровые профили;
- может вручную переопределять автоматически подобранные игровые настройки;
- управляет поведением Shared Apps;
- подтверждает или отменяет Work → Game transition при наличии блокирующих рабочих процессов;
- может инициировать recovery;
- получает совместимые обновления SplitOS-дистрибутива в соответствии со своим update entitlement.

Пользователь остаётся владельцем конечного выбора режима и ручного изменения доступных пользовательских настроек.

SplitOS должен минимизировать количество вопросов в штатном игровом сценарии, но не должен скрывать от пользователя ситуации, где автоматическое действие может привести к потере рабочего состояния.

## SplitOS user / key context

После Windows sign-in SplitOS должен определить собственный пользовательский и ключевой контекст до выбора режима.

Этот контекст потенциально используется для:

- идентификации пользователя внутри SplitOS;
- хранения персональных SplitOS-профилей;
- определения entitlement на обновления дистрибутива;
- дальнейших персонализированных функций.

Точная модель хранения пользователей, ключей, лицензий и entitlement не определяется данным документом.

# 6. Windows 11

## Role

Windows 11 является базовой платформой SplitOS.

Windows отвечает за:

```
process management
service management
display configuration
audio subsystem
power management
device management
user sessions
file system
security model
application execution
driver model
base operating system update mechanisms
```

SplitOS не реализует повторно эти функции.

SplitOS использует Windows как исполнителя системных изменений.

## Responsibility boundary

Пример:

```
User selects Game Mode
        ↓
SplitOS determines target display configuration
        ↓
Windows applies display configuration
        ↓
Display driver performs hardware-level configuration
        ↓
Monitor / TV changes active mode
```

Ownership:

```
Target configuration
→ SplitOS

System application of configuration
→ Windows

Hardware implementation
→ Driver / device
```

# 7. Windows Shell

Стандартная Windows Shell остаётся частью Windows и не входит в SplitOS.

SplitOS:

- не заменяет Explorer;
- не создаёт собственный desktop environment;
- не создаёт собственную login shell;
- не скрывает стандартный Windows Desktop как обязательное поведение;
- не изменяет основной принцип работы desktop-приложений.

Game Mode должен работать поверх стандартной Windows-среды.

При этом Game Mode может предоставлять собственный полноэкранный пользовательский слой SplitOS Game Launcher, ориентированный на игровой сценарий и управление контроллером.

SplitOS Game Launcher не заменяет Windows Shell и не становится login shell или desktop environment.

Управление геймпадом является дополнительным способом взаимодействия с игровым пользовательским контекстом, а не заменой Windows Shell.

# 8. Hardware

SplitOS взаимодействует с физическим оборудованием преимущественно через Windows и драйверы.

Потенциально затрагиваемое оборудование:

```
Displays
├── monitors
└── televisions

Input devices
├── keyboard
├── mouse
├── Xbox-compatible controller
├── DualSense
└── other supported controllers

Audio devices
├── HDMI audio
├── USB audio
├── Bluetooth audio
└── onboard audio

Storage
├── system drive
├── game drives
└── external storage

GPU
├── NVIDIA
├── AMD
└── Intel
```

SplitOS не должен напрямую зависеть от конкретной физической модели устройства, если необходимое поведение может быть получено через Windows abstraction layer.

Vendor-specific интеграции рассматриваются только там, где стандартных механизмов Windows недостаточно.

# 9. Display subsystem

Windows и display driver являются владельцами фактической конфигурации дисплеев.

SplitOS отвечает за:

- хранение предпочтительного Work-дисплея;
- хранение предпочтительного Game-дисплея;
- выбор целевой конфигурации при смене режима;
- предоставление пользователю возможности временно выбрать другой экран;
- обработку отсутствия предпочтительного дисплея;
- хранение связанных предпочтений режима.

SplitOS не отвечает за:

- HDMI/DisplayPort protocol implementation;
- EDID generation;
- работу GPU-драйвера;
- физическую поддержку HDR/VRR/refresh rate устройством.

# 10. Audio subsystem

Windows Audio и драйверы устройств являются владельцами фактического вывода и ввода звука.

SplitOS отвечает за:

- выбор предпочтительного аудиовыхода для режима;
- сохранение пользовательской конфигурации;
- запрос переключения устройства;
- обработку отсутствия сохранённого устройства;
- возможное восстановление предыдущего аудиопрофиля.

SplitOS не отвечает за:

- обработку звукового сигнала драйвером;
- поддержку аудиокодеков устройством;
- работу Bluetooth-аудиостека;
- внутреннюю реализацию HDMI audio.

# 11. Input devices

SplitOS должен учитывать два различных класса input-взаимодействия:

```
System navigation
Game input
```

Они не являются одним и тем же.

## 11.1 System navigation

SplitOS может предоставлять дополнительное управление Windows-средой через контроллер.

Это может включать:

- навигацию;
- эмуляцию клавиатурных действий;
- эмуляцию мыши;
- специализированные профили приложений.

Точный механизм определяется позднее.

## 11.2 Game input

Для игр SplitOS должен поддерживать как минимум два пользовательских профиля:

```
Keyboard & Mouse
Controller
```

Input profile относится к игровому контексту и может задаваться индивидуально для игры.

Пример:

```
Counter-Strike 2
→ Keyboard & Mouse

The Witcher 3
→ Controller
```

SplitOS отвечает за хранение выбранного профиля и подготовку соответствующего окружения.

Сама игра остаётся владельцем обработки игрового ввода.

SplitOS не должен вмешиваться в игровой input без необходимости.

## 12. SplitOS Game Launcher

SplitOS должен иметь собственный внутренний **Game Launcher**, являющийся частью продуктовой оболочки SplitOS поверх Windows 11 и ключевым UX-компонентом Game Mode.

Game Launcher не заменяет внешние игровые платформы и не становится владельцем их бизнес-логики. Его задача — предоставить единый пользовательский слой поверх:

```
Steam
Battle.net
Epic Games Launcher
Xbox App
Other supported launchers
Installed games
Game-specific SplitOS profiles
Gaming settings
```

Концептуально:

```
User
  ↓
SplitOS Game Launcher
  ↓
Game / Launcher abstraction
  ↓
┌────────────┬────────────┬────────────┬────────────┐
│            │            │            │            │
Steam    Battle.net      Epic       Xbox App      Other
│            │            │            │
└────────────┴────────────┴────────────┘
                     ↓
                   Games
```

SplitOS Game Launcher является фильтром и единым UX-слоем для внешних игровых систем.

Пользователь не должен быть обязан вручную взаимодействовать с каждым установленным клиентом для выполнения типовых игровых действий, если SplitOS способен выполнить их через собственный интерфейс или интеграционный слой.

---

## 12.1 UX role

Game Launcher должен предоставлять интерфейс, ориентированный на использование с игрового дисплея и контроллера.

По пользовательской модели он может быть близок к интерфейсам игровых консолей:

```
PlayStation
Xbox
Steam Big Picture
other console-oriented launchers
```

При этом SplitOS не должен копировать конкретный внешний интерфейс.

Game Launcher должен предоставлять собственный единый UX для:

- выбора игры;
- запуска игры;
- выбора игрового клиента;
- просмотра установленных игр;
- управления Game Mode;
- выбора input profile;
- выбора игрового дисплея;
- доступа к игровым параметрам SplitOS;
- отображения состояния устройства;
- быстрого доступа к системным игровым функциям.

---

## 12.2 External launchers

Внешние игровые клиенты остаются владельцами:

- пользовательских аккаунтов;
- авторизации;
- магазинов;
- лицензий;
- установки игр;
- обновления игр;
- cloud-функций;
- собственных внутренних данных;
- внутренних механизмов запуска.

SplitOS Game Launcher может:

- обнаруживать установленный клиент;
- обнаруживать доступные игры;
- хранить связь игры и игрового клиента;
- инициировать запуск клиента;
- инициировать запуск игры через клиент;
- показывать объединённую игровую библиотеку;
- применять SplitOS-профиль перед запуском;
- скрывать от пользователя лишние промежуточные действия там, где это технически возможно.

Таким образом:

```
External launcher
→ platform authority

SplitOS Game Launcher
→ user experience authority
```

---

## 12.3 Automatic Game Mode activation

Work Mode и Game Mode являются взаимоисключающими активными состояниями. Одновременное состояние `Work + Game active` не допускается.

Автоматический переход из Work Mode в Game Mode инициируется **запуском поддерживаемой игры**, а не просто запуском игрового клиента.

Разделяются как минимум два типа объектов:

```text
GAME
GAME_CLIENT
```

Пример:

```text
Cyberpunk 2077 → GAME
Steam          → GAME_CLIENT
```

Запуск `GAME_CLIENT` из Work Mode сам по себе не должен принудительно активировать Game Mode.

Запуск `GAME` из Work Mode должен инициировать управляемый transition flow.

```text
Work Mode
   ↓
Supported GAME launch requested / detected
   ↓
Hold or interrupt premature game start where possible
   ↓
Work → Game pre-flight
   ↓
Resolve blockers
   ↓
Activate Game Mode
   ↓
Open / use SplitOS Game Launcher orchestration
   ↓
Resolve display / input / game profile
   ↓
Apply supported settings
   ↓
Launch game through external client
```

### Pre-flight behavior

Перед завершением Work Mode SplitOS должен проверить рабочие процессы, которые могут быть повреждены или потеряны при переходе.

К ним относятся потенциально:

- несохранённые документы;
- приложения с пользовательским состоянием;
- локальные серверы;
- длительные фоновые задачи;
- процессы, которые нельзя безопасно завершить автоматически.

Если приложение поддерживает безопасное автоматическое сохранение, SplitOS может выполнить сохранение и закрытие. Если это невозможно гарантировать, пользователь должен быть уведомлён.

Для серверов и других значимых процессов пользователь должен получить возможность выбрать, закрывать их или отменить переход.

Если transition отменён, SplitOS остаётся в Work Mode.

Если игра уже была запущена до завершения подготовки, SplitOS должен завершить некорректный игровой процесс при необходимости, очистить связанные игровые процессы и сохранить Work Mode до разрешения блокирующих условий.

### Game completion

Закрытие игры не деактивирует Game Mode.

После завершения игры пользователь возвращается в SplitOS Game Launcher и может:

- запустить другую игру;
- изменить Game Profile;
- использовать Game Mode shared-приложения;
- вручную вернуться в Work Mode.

## 12.4 Automatic detection

В первой версии SplitOS должен обнаруживать игры, официально установленные через поддерживаемые игровые клиенты.

Источниками определения могут быть:

- библиотеки поддерживаемых игровых клиентов;
- documented launcher metadata;
- известные executable / install manifests;
- verified SplitOS integration metadata.

Игровые клиенты обнаруживаются отдельно от игр и классифицируются как `GAME_CLIENT`.

Ручное добавление произвольных executable, standalone-игр и других источников не входит в обязательный v1 scope, но архитектура не должна запрещать такое расширение позже.

Точный механизм detection определяется на этапе Analysis & Design.

## 12.5 Future extension of gaming ecosystem

Граница SplitOS не запрещает дальнейшее расширение собственных компонентов.

В будущем игровые зоны ответственности SplitOS могут получать более глубокие реализации.

Например:

```
SplitOS Controller
SplitOS-certified OEM Controller
SplitOS-specific Discord integration
Special partner application builds
SplitOS communication client
SplitOS overlay
SplitOS social services
SplitOS gaming accessories
```

Возможна разработка специализированного контроллера совместно с OEM-производителем, рассчитанного на системные функции SplitOS.

Также допускаются специализированные версии или интеграции сторонних приложений по договорённости с их разработчиками.

Например:

```
Discord
→ standard desktop integration

future:
→ SplitOS-specific Discord experience
```

Таким образом, текущие внешние зависимости не рассматриваются как неизменная конечная архитектура продукта.

SplitOS может постепенно принимать на себя дополнительные зоны ответственности, если это улучшает пользовательский опыт и не нарушает внешние контракты.

# 13. Games and Game Experience Management

Игра является внешним относительно SplitOS программным продуктом, однако SplitOS управляет окружающим игровым контекстом и, при наличии безопасного поддерживаемого интерфейса, пользовательскими параметрами игры.

SplitOS может знать о:

```text
Game identity
External launcher
Installation location
Available SplitOS profiles
Selected display
Input profile
Resolution
Refresh rate
Graphics configuration
Target frame rate
Device capabilities
Hardware snapshot
```

## 13.1 Multiple Game Profiles

Одна игра должна иметь возможность иметь несколько SplitOS Game Profiles.

Пример:

```text
Cyberpunk 2077
├── Desktop / 1440p / 280 Hz / Keyboard & Mouse
└── TV / 4K / 60 Hz / Controller
```

Game Profile может зависеть от:

- target display;
- input profile;
- resolution;
- refresh rate;
- target FPS;
- hardware state;
- пользовательского сценария.

## 13.2 Hardware refresh

При запуске SplitOS Game Launcher система должна перечитать доступное игровое оборудование и дисплеи.

Перед запуском игры SplitOS должен сравнить актуальное hardware/display state с состоянием, на основании которого был рассчитан выбранный Game Profile.

```text
Game Launcher start
      ↓
Read current hardware / displays
      ↓
Game selected
      ↓
Compare current state with profile snapshot
      ↓
No relevant changes → reuse profile
Changes detected    → recalculate / adjust profile
```

## 13.3 Optimization objective

Основная цель автоматической оптимизации:

> максимизировать качество изображения при достижении максимально стабильной производительности, соответствующей возможностям активного игрового дисплея и текущего компьютера.

Если компьютер способен обеспечить FPS, соответствующий refresh rate активного дисплея, SplitOS должен повышать качество до максимально возможного уровня при сохранении целевой производительности.

Пример:

```text
4K / 60 Hz TV + powerful PC
→ target stable 60 FPS
→ maximum sustainable visual quality
→ FPS cap around target where appropriate
```

Если для достижения refresh rate требуется снижение качества:

```text
1440p / 144 Hz + mid-range PC
→ target stable 144 FPS where achievable
→ reduce graphics until target becomes sustainable
```

Если refresh rate дисплея недостижим на текущем железе:

```text
1080p / 300 Hz + weak PC
→ reduce settings aggressively
→ maximize sustainable FPS
```

## 13.4 Automatic game configuration

Если для конкретной игры существует безопасный поддерживаемый способ изменения пользовательских параметров, SplitOS может автоматически применять Game Profile без дополнительного подтверждения пользователя.

Источниками могут выступать:

- configuration files;
- documented command-line parameters;
- official APIs;
- launcher configuration;
- documented game configuration interfaces;
- verified SplitOS game integration profiles.

Автоматическое изменение не должно предполагать модификацию игрового исполняемого кода.

## 13.5 User control

Все автоматически применённые пользовательские игровые настройки должны оставаться доступными для ручного изменения в самой игре или через поддерживаемый интерфейс.

SplitOS не должен блокировать пользователя в автоматически выбранной конфигурации.

## 13.6 Responsibility boundary

SplitOS может влиять только на параметры, относящиеся к пользовательскому игровому UX и производительности.

SplitOS не должен влиять на:

- anti-cheat;
- DRM;
- игровой сетевой код;
- matchmaking;
- server-side logic;
- соревновательную инфраструктуру;
- подмену игрового input с целью получения преимущества;
- игровые бинарные файлы с целью обхода ограничений.

Игра остаётся владельцем игрового процесса, сетевого поведения, anti-cheat, DRM и фактической обработки игрового input.

# 14. Desktop applications

Обычные Windows-приложения остаются внешними относительно SplitOS продуктами, но SplitOS может управлять их жизненным циклом и представлением относительно активного режима.

Предварительная классификация:

```text
WORK
GAME
GAME_CLIENT
SHARED
SYSTEM
```

`GAME` и `GAME_CLIENT` имеют разные transition semantics.

## 14.1 Work Mode behavior

В Work Mode приоритет отдан рабочим процессам.

Игровые службы, нерелевантные игровые процессы и игровые клиенты не должны создавать фоновую нагрузку без необходимости.

Work-приложения могут включать пользовательские данные и процессы, которые необходимо безопасно обработать перед Work → Game transition.

## 14.2 Shared applications

Примеры Shared Apps:

```text
Discord
Browser
Spotify
Media applications
```

В Work Mode shared-приложение работает как обычное Windows-приложение.

В Game Mode оно может получить отдельное состояние представления:

```text
Overlay
Locked window over game
Secondary display
Background
```

В Game Mode одновременно допускается не более **трёх активных Shared Apps**.

SplitOS может управлять:

- запуском;
- остановкой;
- autostart;
- presentation mode;
- placement on available displays;
- controller navigation policy.

Приложение остаётся владельцем собственных данных, аккаунта, интерфейса, сетевого взаимодействия и внутренней бизнес-логики.

Точная модель собственного SplitOS Overlay определяется позднее.

# 15. Storage

Файловая система остаётся ответственностью Windows.

Игровые launchers остаются владельцами собственных игровых библиотек.

SplitOS может хранить:

- известные пути игровых библиотек;
- связь игры и launcher'а;
- пользовательские storage preferences.

SplitOS не должен требовать отдельного физического накопителя или раздела для Game Mode.

Поддерживаемая базовая конфигурация:

```
Single drive

C:
├── Windows
├── Applications
├── Games
└── SplitOS
```

Дополнительно могут поддерживаться:

```
C:
→ Windows / applications

D:
→ Games
```

Физическое размещение файлов не определяет режим работы приложения.

# 16. GPU drivers

GPU driver является внешней зависимостью.

SplitOS должен учитывать возможность работы с:

```
NVIDIA
AMD
Intel
```

GPU driver отвечает за:

- управление GPU;
- display output;
- hardware acceleration;
- vendor-specific graphics features;
- фактическое применение части графических параметров.

SplitOS может взаимодействовать с параметрами GPU только через поддерживаемые интерфейсы.

Использование undocumented driver modifications не предполагается как базовая часть концепции.

# 17. Windows Update and SplitOS Distribution Updates

SplitOS распространяется как отдельный управляемый дистрибутив на базе Windows 11.

Такой delivery model позволяет контролировать:

- состав базового образа;
- удалённые и отключённые компоненты;
- предустановленные SplitOS-компоненты;
- системные политики;
- совместимую Windows base version;
- тестирование mode transition и игрового UX;
- rollback и recovery.

Стандартное автоматическое применение Windows feature/system updates в поддерживаемом SplitOS-дистрибутиве должно быть принудительно отключено.

Microsoft остаётся владельцем исходных Windows patches, но пользователь SplitOS не должен получать непроверенное изменение базовой Windows напрямую как обычное автоматическое обновление системы.

## 17.1 Distribution update flow

```text
Microsoft releases patch
        ↓
SplitOS compatibility validation
        ↓
SplitOS adaptation
        ↓
Regression testing
        ↓
SplitOS distribution release
        ↓
Eligible SplitOS user
```

## 17.2 SplitOS update authority

```text
Microsoft
→ source of Windows patches

SplitOS
→ compatibility and distribution authority

SplitOS user/key context
→ update entitlement context
```

Точная система подписок, ключей, entitlement и доставки обновлений определяется отдельно.

## 17.3 Validation scope

Перед выпуском обновления необходимо проверить как минимум:

- boot / sign-in;
- SplitOS user/key context;
- mode selection;
- Work Mode;
- Game Mode;
- Work → Game pre-flight;
- Game Launcher;
- supported game detection;
- display profiles;
- audio profiles;
- input profiles;
- game profiles;
- Shared Apps behavior;
- optimization policies;
- recovery;
- supported external game clients;
- supported GPU drivers.

## 17.4 Security updates

Отключение стандартного автоматического Windows Update не означает отказ от security patches.

Критические исправления должны проходить контролируемый цикл проверки и включаться в совместимый SplitOS release.

Требуемые сроки и ускоренный security update process определяются отдельно.

## 17.5 Driver updates

GPU и peripheral driver updates рассматриваются отдельно от базовых Windows updates.

SplitOS может предоставлять проверенные версии, рекомендации или допустимые диапазоны версий драйверов.

## 17.6 Recovery

Обновление SplitOS должно предусматривать pre-update validation, сохранение пользовательских профилей, rollback и восстановление после неуспешного обновления.

# 18. SplitOS Manager and Mode Entry

После Windows sign-in SplitOS должен разрешить user/key context и предоставить mode selection.

SplitOS Manager является основной точкой ручной конфигурации и управления SplitOS, но Game Mode также имеет собственную основную пользовательскую поверхность — SplitOS Game Launcher.

SplitOS Manager отвечает за:

- отображение текущего режима;
- ручное переключение Work / Game;
- конфигурацию Work Mode;
- конфигурацию Game Mode;
- display / audio / input profiles;
- game profiles;
- Work → Game transition policy;
- application classification;
- Shared Apps policy;
- game optimization preferences;
- update / recovery information.

Game Launcher отвечает за controller-friendly игровой UX, библиотеку, игровые профили и orchestration запуска игры.

Глубокая кастомизация Work Mode UI не является обязательным приоритетом первой версии.

# 19. Responsibility model

| Область | Owner |
|---|---|
| Windows sign-in | Windows |
| SplitOS user / key context | SplitOS |
| Выбор режима после входа | User + SplitOS flow |
| Активный режим | SplitOS |
| Work / Game mutual exclusion | SplitOS |
| Work → Game pre-flight | SplitOS |
| Решение по блокирующим рабочим процессам | User where confirmation is required |
| Определение `GAME` / `GAME_CLIENT` | SplitOS |
| Автопереход по запуску поддерживаемой игры | SplitOS |
| Запуск Game Client без обязательного mode transition | User / external client |
| SplitOS Game Launcher UX | SplitOS |
| Объединённое представление официальных игр | SplitOS |
| Установка и обновление игры | External Game Client |
| Лицензия игры | External Game Client / platform |
| Game Profile | SplitOS |
| Multiple profiles per game | SplitOS |
| Hardware / display snapshot | SplitOS, based on Windows / driver authority |
| Рекомендации и применение поддерживаемых игровых настроек | SplitOS |
| Фактический игровой процесс | Game |
| Anti-cheat / DRM / matchmaking / network code | Game / platform |
| Игровой input | Game |
| SplitOS input profile | SplitOS |
| Shared Apps policy | SplitOS |
| Данные Shared App | Corresponding application |
| Применение системных параметров | Windows |
| Hardware implementation | Windows + Drivers + Device |
| Исходные Windows patches | Microsoft |
| SplitOS distribution update policy | SplitOS |
| Update entitlement context | SplitOS user / key context |

# 20. High-level context model

```text
                           User
                            │
                            ▼
                     Windows sign-in
                            │
                            ▼
                  SplitOS user/key context
                            │
                            ▼
                       Mode selection
                            │
                 ┌──────────┴──────────┐
                 │                     │
                 ▼                     ▼
             Work Mode             Game Mode
                 │                     │
                 │            SplitOS Game Launcher
                 │                     │
                 │           ┌─────────┼─────────┐
                 │           ▼         ▼         ▼
                 │        Games    Game Clients Profiles
                 │                     │
                 └──────────┬──────────┘
                            ▼
                        Windows 11
                            │
             ┌──────────────┼──────────────┐
             ▼              ▼              ▼
         Windows APIs     Drivers      Applications
                            │
                            ▼
                         Hardware
```

`Work Mode xor Game Mode` является системным инвариантом активной пользовательской сессии.

SplitOS расположен над Windows 11 как управляющая и продуктовая обёртка, но распространяется вместе с контролируемой Windows base image как отдельный дистрибутив.

# 21. Trust and authority

При дальнейшем проектировании необходимо различать:

```
Desired state
Actual system state
External application state
Hardware capabilities
```

### Desired state

Владелец:

```
SplitOS
```

Пример:

```
Game display = LG OLED
Input profile = Controller
Power mode = Gaming
```

---

### Actual system state

Основной authority:

```
Windows
```

SplitOS не должен считать настройку успешно применённой только потому, что отправил команду.

Фактическое состояние должно быть подтверждено через доступный системный источник.

---

### Gaming UX state

Authority:

```text
SplitOS
```

SplitOS является каноническим владельцем:

- активного игрового UX-контекста;
- представления объединённой библиотеки;
- SplitOS game profiles;
- выбранного input profile;
- рекомендованных игровых параметров;
- связи между игрой и её внешним launcher'ом.

Внешний launcher остаётся authority для собственных лицензий, установки, обновления и состояния платформы.

---

### Distribution update state

Authority:

```text
SplitOS
```

SplitOS является authority для того, какая версия базовой Windows считается совместимой с конкретной версией SplitOS-дистрибутива.

Microsoft остаётся источником Windows patches, но их наличие само по себе не означает разрешение на применение внутри поддерживаемой SplitOS-среды.

---

### External application state

Authority:

```
corresponding application / launcher
```

Например, SplitOS не должен считать игру установленной исключительно на основании собственного старого конфигурационного файла, если launcher сообщает другое состояние.

---

### Hardware capability

Authority:

```
Windows / driver / hardware-reported capability
```

SplitOS должен опираться на фактически обнаруженные возможности оборудования.

---

# 22. System invariants

### INV-001
SplitOS использует Windows 11 как базовую операционную систему и не заменяет Windows kernel.

### INV-002
Work Mode и Game Mode используют одну установленную Windows.

### INV-003
После выбора режима одновременно активен только один режим: `WORK xor GAME`.

### INV-004
Переключение режима не создаёт вторую Windows-сессию или вторую ОС.

### INV-005
После Windows sign-in режим выбирается через SplitOS flow после разрешения user/key context.

### INV-006
Windows Shell остаётся базовой системной оболочкой; SplitOS Game Launcher является собственным Game Mode UX, а не replacement shell.

### INV-007
Один физический накопитель является поддерживаемой конфигурацией.

### INV-008
Игровой Controller profile не означает обязательное использование контроллера для всех игр.

### INV-009
Одна игра может иметь несколько SplitOS Game Profiles.

### INV-010
`GAME` и `GAME_CLIENT` являются разными категориями.

### INV-011
Запуск `GAME_CLIENT` сам по себе не должен принудительно активировать Game Mode.

### INV-012
Запуск поддерживаемого `GAME` из Work Mode должен инициировать управляемый Work → Game transition.

### INV-013
Work → Game transition может быть отменён при наличии неразрешённых рабочих процессов; в этом случае система остаётся в Work Mode.

### INV-014
Закрытие игры не возвращает систему автоматически в Work Mode; пользователь возвращается в Game Launcher и остаётся в Game Mode.

### INV-015
В Game Mode одновременно допускается не более трёх активных Shared Apps.

### INV-016
В первой версии библиотека SplitOS строится на официально установленных играх поддерживаемых игровых клиентов.

### INV-017
При запуске Game Launcher hardware/display state перечитывается; при изменениях Game Profile должен быть переоценён перед запуском игры.

### INV-018
Автоматически применённые игровые настройки должны оставаться доступными для ручного изменения пользователем.

### INV-019
SplitOS не должен изменять anti-cheat, DRM, matchmaking, сетевой код, server-side logic или подменять игровой input для получения преимущества.

### INV-020
SplitOS распространяется как отдельный контролируемый дистрибутив, а не как универсальный installer поверх произвольной Windows.

### INV-021
Стандартное автоматическое применение Windows feature/system updates отключается; обновления базовой Windows проходят через совместимый SplitOS release.

### INV-022
Game Mode UI и Game Launcher являются приоритетными продуктовыми зонами первой версии; глубокая кастомизация Work Mode UI может быть отложена.

# 23. Known constraints

На текущем этапе известны следующие ограничения и подтверждённые рамки:

- базовая платформа — Windows 11;
- SplitOS — отдельный управляемый дистрибутив;
- Windows kernel не модифицируется;
- Windows Shell сохраняется как базовая системная оболочка;
- активен только один режим;
- после входа требуется SplitOS mode selection;
- SplitOS имеет собственный user/key context;
- необходимо поддерживать один физический накопитель;
- должны поддерживаться все корректно обнаруженные подключённые дисплеи;
- Game Mode поддерживает Keyboard & Mouse и Controller profiles;
- одна игра может иметь несколько Game Profiles;
- v1 поддерживает официальные игры из поддерживаемых игровых клиентов;
- запуск Game Client не равен запуску Game;
- Work → Game transition должен учитывать сохранность рабочего состояния;
- автоматическое сохранение зависит от возможностей конкретного приложения;
- Game Mode допускает максимум три активных Shared Apps;
- hardware-aware optimization зависит от безопасных конфигурационных интерфейсов конкретной игры;
- часть display/input поведения зависит от драйверов;
- часть системных операций требует elevated privileges;
- стандартный Windows feature/system auto-update отключён;
- Windows patches проходят через SplitOS compatibility / distribution lifecycle;
- глубокая кастомизация Work Mode UI не является обязательной для v1.

# 24. Open questions

Ранее открытые вопросы о взаимном состоянии режимов, запуске игровых клиентов, поведении после закрытия игры, distribution model и v1 game source закрыты текущими решениями.

Остаются вопросы следующего уровня детализации.

## Transition safety

**CTX-OPEN-001**  
Какие приложения SplitOS сможет безопасно сохранять и закрывать автоматически, а для каких всегда потребуется участие пользователя?

**CTX-OPEN-002**  
Как определяется критичность локального сервера или long-running process при Work → Game transition?

## Supported clients and games

**CTX-OPEN-003**  
Какие игровые клиенты входят в обязательный первый compatibility set?

**CTX-OPEN-004**  
Какой минимальный набор игр используется для validation первой версии Game Profile / optimization pipeline?

## Game optimization

**CTX-OPEN-005**  
Как учитывать VRR, frame generation, upscaling и нестабильный frame pacing при вычислении рекомендуемого профиля?

**CTX-OPEN-006**  
Как фиксировать ручные изменения пользователя, чтобы автоматический перерасчёт после hardware/display change не создавал нежелательных конфликтов?

## Shared Apps / Overlay

**CTX-OPEN-007**  
Какие presentation states (`Overlay`, `Locked window`, `Secondary display`, `Background`) обязательны для v1?

**CTX-OPEN-008**  
Каким образом выбираются три одновременно активных Shared Apps и что происходит при попытке открыть четвёртое?

## User / key / updates

**CTX-OPEN-009**  
Где и каким образом хранится SplitOS user/key state и как подтверждается update entitlement?

**CTX-OPEN-010**  
Какой rollback-механизм обязателен для обновления дистрибутива?

**CTX-OPEN-011**  
Как быстро критические Microsoft security patches должны проходить ускоренный SplitOS compatibility cycle?

## Future ecosystem

**CTX-OPEN-012**  
Какие interfaces следует предусмотреть для будущих SplitOS-certified OEM controllers и других устройств?

**CTX-OPEN-013**  
Когда partner integrations (Discord, OBS/Twitch и др.) должны перейти из future extension в поддерживаемый product scope?

# 25. Result

На уровне системного контекста SplitOS определяется как отдельный управляемый дистрибутив и продуктовая платформа-обёртка над Windows 11.

Ключевая модель:

```text
Windows sign-in
      ↓
SplitOS user/key context
      ↓
Mode selection
      ↓
WORK xor GAME
```

Work Mode отвечает за рабочий контекст и минимизацию нерелевантной игровой активности.

Game Mode отвечает за gaming / entertainment context и использует собственный SplitOS Game Launcher как основной UX и orchestration point.

Запуск поддерживаемой игры из Work Mode должен пройти через управляемый Work → Game transition, pre-flight рабочей среды и подготовку Game Profile. Запуск внешнего Game Client сам по себе не является причиной принудительного mode transition.

После закрытия игры пользователь остаётся в Game Mode и возвращается в Game Launcher.

SplitOS может автоматически адаптировать поддерживаемые игровые настройки под текущее железо и активный дисплей, сохраняя возможность ручного изменения пользователем и не вмешиваясь в anti-cheat, DRM, matchmaking, network code или игровой input с целью получения преимущества.

SplitOS контролирует собственный distribution/update lifecycle: Microsoft patches являются внешним источником изменений, но применяются к поддерживаемой среде только через проверенный SplitOS release.

Эта модель является основой для актуализированных Functional Requirements и последующего Analysis & Design.
