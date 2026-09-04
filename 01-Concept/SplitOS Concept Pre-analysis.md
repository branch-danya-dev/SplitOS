# SplitOS — Concept / Pre-analysis

## 1. Исходная идея

**SplitOS** — отдельный управляемый программный дистрибутив на базе Windows 11, предназначенный для использования одного персонального компьютера в двух специализированных эксплуатационных режимах:

- **Work Mode** — очищенная и агрессивно оптимизированная рабочая среда Windows 11, сфокусированная на рабочих процессах и минимизации игровой фоновой активности;
- **Game Mode** — игровая и развлекательная среда той же установленной Windows 11, сфокусированная на играх, медиасценариях, стриминге, управлении дисплеями и устройствами ввода.

SplitOS не предполагает создание собственного ядра, замену Windows kernel или запуск двух независимых экземпляров Windows.

Work Mode и Game Mode существуют в рамках одной установленной Windows 11, но являются **взаимоисключающими активными состояниями**: в каждый момент после выбора режима активен только один из них.

SplitOS распространяется как отдельный контролируемый дистрибутив, а не как универсальный установщик поверх произвольной существующей Windows.

После стандартного Windows sign-in SplitOS должен определить пользовательский и ключевой контекст, после чего предоставить выбор активного режима.

Концептуально:

```text
Windows boot
    ↓
Windows sign-in
    ↓
SplitOS user / key context
    ↓
Mode selection
    ↓
┌───────────────┬───────────────┐
│               │               │
Work Mode       Game Mode
```

---

# 2. Problem

Современный игровой ПК часто одновременно используется как рабочая станция и игровое устройство.

Windows 11 предоставляет универсальное окружение, однако рабочие и игровые процессы обычно сосуществуют в одном системном состоянии. Игровые клиенты, сервисы, фоновые задачи, уведомления и связанные процессы могут оставаться активными во время работы; рабочие процессы, серверы, коммуникационные приложения и несвязанные фоновые задачи могут оставаться активными во время игры.

При переходе от работы к игре пользователю могут потребоваться:

- переключение активного дисплея;
- выбор телевизора или игрового монитора;
- изменение разрешения и refresh rate;
- изменение аудиовыхода;
- изменение режима питания;
- остановка или ограничение рабочих процессов;
- запуск игровых сервисов и клиентов;
- применение профиля конкретной игры;
- выбор Keyboard & Mouse или Controller profile;
- подготовка геймпада;
- изменение игровых настроек под текущее железо и дисплей;
- управление shared-приложениями;
- подготовка стримингового или entertainment-сценария.

При возвращении к рабочему сценарию требуется обратная перестройка среды.

Стандартная Windows не рассматривает работу и игру как два полноценных, изолированно управляемых пользовательских контекста.

### Формулировка проблемы

Пользователь не имеет единой управляемой среды, которая позволяет использовать одну Windows 11 в строго разделённых Work и Game контекстах, автоматически освобождать ресурсы от нерелевантных процессов и подготавливать систему, приложения, дисплеи, устройства ввода и игровые параметры под выбранный сценарий.

---

# 3. Context

## 3.1 Текущая модель

Без SplitOS типичный компьютер работает следующим образом:

```text
User
  ↓
Windows 11
  ↓
Desktop environment
  ↓
Applications / Game Clients / Games / Services
  ↓
Hardware
```

Рабочее и игровое использование происходят внутри одного общего системного состояния:

```text
Windows
│
├── Work applications
├── Background applications
├── Game clients
├── Gaming services
├── Streaming / communication apps
├── Displays
├── Audio devices
└── Input devices
```

Существующий Windows Game Mode решает только часть задачи и не управляет всей пользовательской средой, разделением процессов, игровым UX, профилями игр и переходом между рабочим и игровым контекстом.

---

# 4. Expected outcome

После внедрения SplitOS пользователь должен получить один управляемый дистрибутив Windows 11 с двумя взаимоисключающими эксплуатационными состояниями.

```text
                     SplitOS Distribution
                              │
                         Windows 11 Base
                              │
                         SplitOS Layer
                              │
                 ┌────────────┴────────────┐
                 │                         │
             Work Mode                Game Mode
                 │                         │
       Productivity context      Gaming / entertainment
```

После входа пользователь выбирает режим, после чего SplitOS приводит систему к соответствующему состоянию.

Режим должен определять не только внешний интерфейс, но и набор активных процессов, сервисов, приложений, системных профилей и разрешённых фоновых сценариев.

---

# 5. Work Mode

Work Mode должен оставаться максимально совместимым со стандартным Windows desktop UX, но использовать контролируемое и очищенное состояние дистрибутива.

Основной принцип:

> Компьютер сосредоточен на работе; игровые службы, игровые клиенты и нерелевантные игровые процессы не потребляют ресурсы и не создают фоновую активность без необходимости.

Предполагаемые свойства:

- стандартная Windows Shell;
- стандартный Windows Desktop как базовый UX;
- обычное управление клавиатурой и мышью;
- рабочий набор приложений;
- Work display profile;
- Work audio profile;
- Work power profile;
- игровые сервисы и игровые фоновые процессы отключены либо не активируются без необходимости;
- игровые клиенты не должны автоматически выполнять фоновую игровую активность, если она не требуется пользователю;
- минимизация нерелевантных уведомлений и фоновой нагрузки;
- сохранение совместимости с обычным desktop-ПО;
- обновление базовой Windows происходит через контролируемый SplitOS update lifecycle, а не через обычное автоматическое применение Windows feature/system updates.

В дальнейшем Work Mode может получить собственные productivity-функции, например:

- workspace layouts;
- desktop customization;
- Notes;
- закрепление приложений и рабочих объектов;
- дополнительные сценарии организации рабочего пространства.

Глубокая кастомизация Work Mode UI не является главным приоритетом первой версии.

---

# 6. Game Mode

Game Mode представляет собой специализированное игровое и развлекательное состояние той же Windows 11.

Основной принцип:

> Компьютер сосредоточен на игре и entertainment-сценарии; рабочая фоновая активность ограничивается, а игровая среда подготавливается автоматически.

Game Mode должен быть одним из ключевых продуктовых отличий SplitOS.

Предполагаемые свойства:

- собственный SplitOS Game Launcher;
- controller-friendly Game Mode UI;
- применение игрового системного профиля;
- поддержка всех обнаруженных подключённых дисплеев;
- выбор целевого игрового дисплея;
- применение resolution / refresh rate / HDR / VRR там, где это поддерживается;
- выбор игрового аудиовыхода;
- игровой power profile;
- управление устройствами ввода;
- Keyboard & Mouse и Controller game profiles;
- управление жизненным циклом рабочих и игровых процессов;
- объединённая библиотека поддерживаемых игр;
- применение Game Profile перед запуском игры;
- hardware-aware game optimization;
- собственный overlay / shared-app experience как целевое направление;
- возможность entertainment-сценариев с браузером, Discord, музыкальным клиентом и другими поддерживаемыми shared-приложениями;
- возможность дальнейшей интеграции с OBS, Twitch, трансляцией на соседний дисплей или TV и другими streaming-сценариями.

После завершения игры пользователь остаётся в Game Mode и возвращается в SplitOS Game Launcher. Возврат в Work Mode происходит отдельным действием пользователя.

Game Mode не заменяет Windows kernel или Windows Shell как базовую системную платформу.

---

# 7. Mode selection and switching

## 7.1 Initial selection

После Windows sign-in и определения SplitOS user/key context система должна запросить выбор режима:

```text
Windows sign-in
    ↓
SplitOS context resolved
    ↓
Select mode
    ↓
WORK xor GAME
```

Одновременно активным может быть только один режим.

## 7.2 Manual switching

Пользователь должен иметь отдельное действие для перехода:

```text
Work Mode → Game Mode
Game Mode → Work Mode
```

## 7.3 Work → Game pre-flight

Переход Work → Game должен быть управляемым сценарием, а не простым включением overlay.

Перед завершением Work Mode SplitOS должен проверить активный рабочий контекст, включая потенциально:

- несохранённые документы;
- приложения с пользовательскими данными;
- локальные серверы;
- длительные задачи;
- процессы, которые нельзя безопасно завершить автоматически.

Если приложение поддерживает безопасное сохранение, SplitOS может попытаться сохранить данные и закрыть его. Если безопасное автоматическое действие невозможно, пользователь должен быть уведомлён.

Для активных серверов и других значимых процессов SplitOS должен запросить решение пользователя.

Пользователь должен иметь возможность отменить переход.

Если игра была запущена раньше, чем Game Mode подготовил окружение, SplitOS должен отменить некорректный игровой сценарий, завершить целевой игровой процесс при необходимости, очистить связанные игровые процессы и сохранить Work Mode до разрешения блокирующих условий.

## 7.4 Game → Work

Возврат из Game Mode инициируется пользователем отдельной кнопкой или командой SplitOS.

Закрытие одной игры само по себе не должно автоматически возвращать систему в Work Mode.

---

# 8. Display concept

SplitOS должен поддерживать все дисплеи, которые Windows и драйверы корректно обнаруживают как подключённые устройства.

Для режимов и игровых профилей пользователь должен иметь возможность определять целевой дисплей.

Пример:

```text
Work Mode
→ Primary desktop monitor

Game Mode profile A
→ Gaming Monitor / 2560×1440 / 280 Hz

Game Mode profile B
→ TV / 3840×2160 / 60 Hz
```

При входе в Game Mode SplitOS должен применять соответствующий display profile.

Пользователь должен иметь возможность быстро переключить целевой дисплей, например временно выбрать TV вместо обычного игрового монитора.

Характеристики активного игрового дисплея становятся входными данными для Game Profile и hardware-aware optimization.

---

# 9. Application concept

Приложения физически не разделяются по разным накопителям или Windows-установкам. Разделяется их поведение относительно активного режима.

Предварительная классификация:

```text
WORK
GAME
GAME_CLIENT
SHARED
SYSTEM
```

Пример:

```text
Visual Studio Code → WORK
Cyberpunk 2077     → GAME
Steam              → GAME_CLIENT
Discord            → SHARED
Windows Explorer   → SYSTEM
```

`GAME` и `GAME_CLIENT` являются разными категориями.

Запуск `GAME` из Work Mode должен инициировать подготовку перехода в Game Mode.

Запуск `GAME_CLIENT` сам по себе не должен принудительно переключать режим: пользователь может открыть Steam, Battle.net или другое ПО без запуска игры.

SplitOS может определять для приложения:

- разрешённый режим;
- autostart policy;
- background policy;
- start / stop behavior;
- transition behavior;
- input behavior;
- presentation behavior.

### Shared applications

В Work Mode shared-приложение работает как обычное Windows-приложение.

В Game Mode оно может получить отдельное представление:

```text
Overlay
Locked window
Secondary display
Background
```

В Game Mode одновременно должно поддерживаться не более **трёх активных Shared Apps**.

Пример:

```text
Browser + Discord + Spotify
```

Точная модель overlay и управления shared-приложениями определяется позднее.

---

# 10. Storage concept

SplitOS не должен требовать физического разделения одного накопителя на рабочую и игровую части.

При одном SSD:

```text
C:\
├── Windows
├── Program Files
├── Users
├── Games
└── SplitOS
```

При нескольких накопителях игровые библиотеки могут находиться отдельно:

```text
C:
→ Windows + applications

D:
→ Games
```

Разделение Work/Game является логическим и системным, а не дисковым.

Игровые клиенты остаются владельцами собственных библиотек и физической установки игр.

---

# 11. Controller / Input concept

Game Mode должен поддерживать как минимум два игровых input-профиля:

```text
GAME INPUT PROFILE
├── Keyboard & Mouse
└── Controller
```

Одна игра должна иметь возможность иметь **несколько SplitOS-профилей** для разных пользовательских сценариев.

Пример:

```text
Cyberpunk 2077
├── Desktop / 1440p / 280 Hz / Keyboard & Mouse
└── TV / 4K / 60 Hz / Controller
```

SplitOS должен обеспечивать удобное управление устройствами ввода при смене режима и игрового профиля.

### Keyboard & Mouse profile

Предназначен для сценариев, где основным способом управления являются клавиатура и мышь.

SplitOS не должен вмешиваться в игровой input без необходимости.

### Controller profile

Предназначен для controller-first сценариев и должен учитывать:

- выбранный контроллер;
- controller-first navigation до запуска игры;
- передачу управления игре;
- возврат в Game Launcher после закрытия игры;
- fallback-навигацию для Windows-приложений, если она необходима.

Для desktop-приложений сохраняются потенциальные уровни поддержки:

```text
1. Native controller support
2. SplitOS application-specific navigation
3. Generic controller → keyboard/mouse fallback
```

Точная модель input routing определяется позднее.

---

# 12. SplitOS Game Launcher concept

SplitOS должен иметь собственный Game Launcher как центральную пользовательскую точку Game Mode.

Он является UX-обёрткой и фильтром поверх внешних игровых клиентов и игр.

```text
User
  ↓
SplitOS Game Launcher
  ↓
Unified game library / profiles / settings
  ↓
External Game Client
  ↓
Game
```

Game Launcher должен использоваться для корректной подготовки игры:

```text
Game launch request
      ↓
Resolve active Game Mode
      ↓
Read hardware / display state
      ↓
Resolve Game Profile
      ↓
Apply display / input / game settings
      ↓
Launch through external client
```

Если пользователь пытается запустить поддерживаемую игру непосредственно из Work Mode, SplitOS должен по возможности перехватить сценарий до полноценного запуска игры и провести его через Work → Game transition и Game Launcher orchestration.

Запуск внешнего игрового клиента не считается запуском игры и сам по себе не должен инициировать принудительный переход.

После закрытия игры Game Launcher снова становится основной точкой Game Mode.

---

# 13. Games and Game Experience Optimization

## 13.1 v1 game source

В первой версии SplitOS поддерживает игры, официально установленные через поддерживаемые игровые клиенты.

Ручное добавление произвольных executable, standalone-игр и других источников может быть добавлено позже, но не является обязательным scope первой версии.

SplitOS не проверяет право собственности на игру как часть игрового UX: authority лицензии остаётся у внешней игровой платформы.

## 13.2 Game profiles

Одна игра может иметь несколько SplitOS Game Profiles, зависящих от:

- целевого дисплея;
- input profile;
- resolution;
- refresh rate;
- target FPS;
- hardware state;
- пользовательского сценария.

## 13.3 Hardware refresh

При запуске Game Launcher SplitOS должен перечитать доступное игровое железо и дисплеи.

Перед запуском игры SplitOS должен сравнить актуальное hardware/display state с состоянием, на основании которого был подготовлен Game Profile.

Если значимых изменений нет, существующий профиль может быть использован без перерасчёта.

Если оборудование или целевой дисплей изменились, SplitOS должен скорректировать профиль перед запуском игры.

## 13.4 Optimization objective

Основная цель автоматической оптимизации:

> предоставить максимально возможное качество изображения при достижении максимально стабильной производительности, соответствующей возможностям активного игрового дисплея и компьютера.

Если компьютер способен стабильно обеспечить refresh rate активного дисплея, SplitOS повышает качество до максимально возможного уровня при сохранении целевого FPS.

Пример:

```text
Powerful PC
4K / 60 Hz TV
→ Target: stable 60 FPS
→ Highest quality that sustains 60 FPS
→ FPS cap around target where appropriate
```

Если целевой refresh rate требует снижения качества:

```text
Mid-range PC
1440p / 144 Hz
→ Target: stable 144 FPS where achievable
→ Reduce quality until target becomes sustainable
```

Если refresh rate физически недостижим на текущем железе:

```text
Weak PC
1080p / 300 Hz
→ Reduce settings aggressively
→ Maximize sustainable FPS
```

Поддерживаемые настройки применяются автоматически с минимальным количеством вопросов пользователю.

Пользователь сохраняет право вручную изменить настройки непосредственно в игре.

SplitOS не должен изменять anti-cheat, DRM, matchmaking, сетевой код, серверную игровую логику или подменять игровой input для получения преимущества.

---

# 14. Distribution and Update concept

SplitOS является отдельным управляемым дистрибутивом.

Базовый образ должен позволять:

- заранее удалить или отключить ненужные компоненты;
- предустановить необходимые SplitOS-компоненты;
- контролировать системные политики;
- тестировать известное базовое состояние;
- гарантировать совместимость между SplitOS-компонентами и конкретной Windows base version.

Стандартное автоматическое применение Windows feature/system updates в SplitOS должно быть отключено.

Microsoft остаётся источником Windows patches, но они должны попадать к пользователю через проверенный SplitOS distribution release.

Концептуально:

```text
Microsoft patch
     ↓
SplitOS validation
     ↓
Distribution adaptation
     ↓
Testing
     ↓
SplitOS release
     ↓
Eligible SplitOS user
```

SplitOS user/key context может определять право пользователя на получение соответствующих обновлений. Точная модель лицензирования и update entitlement определяется отдельно.

---

# 15. Initial scope

В приоритетный scope первой реализации входят:

```text
Controlled Windows 11 distribution
SplitOS user/key context
Mode selection after sign-in
Strict Work / Game separation
Work → Game pre-flight transition
Game Mode UI
SplitOS Game Launcher
Official games from supported game clients
Game profiles
Multiple profiles per game
Hardware detection
Hardware-aware game optimization
All connected display support
Display profile management
Audio profile management
Keyboard & Mouse / Controller profiles
Input device management between modes
Application lifecycle management
GAME / GAME_CLIENT distinction
Shared Apps policy
Windows optimization
Controlled SplitOS update lifecycle
Recovery / rollback
```

Глубокая кастомизация Work Mode UI имеет более низкий приоритет и может развиваться после стабилизации основной игровой платформы.

---

# 16. Out of scope for the first implementation

На текущем этапе не входят в обязательную первую реализацию:

- разработка собственного ядра ОС;
- модификация Windows kernel;
- полная замена Windows Shell;
- собственная реализация DirectX;
- разработка GPU-драйверов;
- обход DRM;
- обход anti-cheat;
- изменение сетевого кода или matchmaking игр;
- подмена игрового input для получения преимущества;
- обязательное физическое разделение SSD;
- две независимые установки Windows;
- виртуализация Work/Game режимов;
- ручное добавление произвольных standalone / unofficial игр в v1;
- глубокий кастомный Work Mode UI в v1;
- собственный OEM-геймпад в v1;
- специализированные partner-версии Discord и других приложений в v1;
- собственный коммуникационный сервис в v1.

Эти пункты не запрещают последующее расширение экосистемы SplitOS.

---

# 17. Potentially affected areas

Первичная поверхность системы:

```text
SplitOS Distribution
│
├── Image preparation
├── User / key context
├── Mode selection
├── Mode transition
├── Transition pre-flight
├── SplitOS Manager
├── Work Mode policy
├── Game Mode policy
├── SplitOS Game Launcher
├── Game detection
├── Game profiles
├── Hardware detection
├── Game optimization
├── Display subsystem
├── Audio subsystem
├── Power subsystem
├── Input subsystem
├── Application lifecycle
├── Shared Apps / Overlay
├── External game-client integrations
├── Storage / game libraries
├── Configuration persistence
├── SplitOS Update
└── Recovery / rollback
        │
        ▼
     Windows 11
```

Это initial change surface, а не Component Diagram.

---

# 18. External systems and dependencies

Предварительно SplitOS взаимодействует со следующими внешними системами и устройствами:

```text
Windows 11
Windows Services
Windows Registry
Task Scheduler
Windows Display subsystem
Windows Audio subsystem
Windows Power subsystem
Windows Input subsystem
Microsoft Windows patches

GPU Drivers
├── NVIDIA
├── AMD
└── Intel

Supported Game Clients
├── Steam
├── Battle.net
├── Epic Games Launcher
├── Xbox App
└── other supported clients

Input devices
├── Keyboard
├── Mouse
├── Xbox-compatible Controller
├── DualSense
└── other supported controllers

Displays
├── Monitors
└── TVs

Audio devices

Potential future integrations
├── OBS
├── Twitch
├── Discord partner integration
└── OEM SplitOS devices
```

---

# 19. Known constraints

### VERIFIED

- Базовой системой является Windows 11.
- SplitOS распространяется как отдельный управляемый дистрибутив.
- Work Mode и Game Mode существуют внутри одной установленной Windows.
- Одновременно активен только один режим.
- После входа пользователь выбирает режим.
- SplitOS имеет собственный user/key context.
- Windows Shell не заменяется как базовая системная оболочка.
- SplitOS имеет собственный Game Launcher.
- Запуск игры и запуск игрового клиента являются разными системными событиями.
- Только запуск поддерживаемой игры должен инициировать обязательный Work → Game transition.
- После закрытия игры Game Mode сохраняется, пользователь возвращается в Game Launcher.
- Одна игра может иметь несколько SplitOS-профилей.
- Game Mode должен поддерживать Keyboard & Mouse и Controller profiles.
- SplitOS должен поддерживать все корректно обнаруженные подключённые дисплеи.
- В Game Mode одновременно допускается не более трёх активных Shared Apps.
- В v1 поддерживаются официально установленные игры из поддерживаемых игровых клиентов.
- SplitOS не требует нескольких физических накопителей.
- Стандартное автоматическое применение Windows feature/system updates отключается.
- Обновления базовой Windows доставляются через проверенный SplitOS distribution release.
- Game Mode UI является приоритетной частью продукта; глубокая кастомизация Work Mode UI может быть отложена.

### INFERRED

- Для части системных операций потребуются elevated privileges.
- Возможность автоматического сохранения документов зависит от возможностей конкретного приложения.
- Не все процессы можно безопасно остановить без участия пользователя.
- Некоторые display-настройки зависят от GPU driver.
- Поведение и источники данных игровых клиентов придётся интегрировать индивидуально.
- Не каждая игра предоставляет безопасный и стабильный интерфейс для изменения graphics settings.
- Точная модель SplitOS accounts, keys и update entitlement требует отдельного анализа.

---

# 20. Concept statement

**SplitOS — отдельный управляемый дистрибутив на базе Windows 11, который превращает один ПК в строго разделённые Work и Game пользовательские контексты и управляет всей подготовкой среды при переходе между ними.**

Work Mode сосредоточен на рабочих процессах и минимизирует игровую фоновую активность.

Game Mode сосредоточен на играх и развлечениях и предоставляет собственный SplitOS Game Launcher, управление игровыми профилями, дисплеями, устройствами ввода, shared-приложениями и hardware-aware оптимизацией игр.

Одновременно активен только один режим.

Запуск поддерживаемой игры из Work Mode должен проходить через управляемый переход в Game Mode и подготовку профиля; запуск игрового клиента сам по себе не является основанием для принудительного переключения.

SplitOS контролирует собственный distribution/update lifecycle, чтобы базовая Windows оставалась проверенной и совместимой с продуктом.

Основная ценность SplitOS — не набор твиков поверх Windows, а новый управляемый пользовательский опыт, в котором одна Windows-система получает две различающиеся по ответственности, ресурсам и UX среды эксплуатации.
