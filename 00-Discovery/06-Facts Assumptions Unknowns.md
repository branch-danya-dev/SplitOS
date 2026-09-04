# SplitOS — Facts, Assumptions and Unknowns

## 1. VERIFIED

### Product

- **V-001** SplitOS является отдельным управляемым дистрибутивом на базе Windows 11, формируемым из поддерживаемого Windows source и SplitOS-owned build inputs.
- **V-002** SplitOS не создаёт собственное ядро.
- **V-003** Windows Shell не заменяется как базовая desktop shell.
- **V-004** Work Mode и Game Mode используют одну Windows installation.
- **V-005** Активным может быть только один режим: Work XOR Game.
- **V-006** После sign-in пользователь проходит SplitOS user/key context и выбор режима.
- **V-007** Game Mode является основным UX-приоритетом первой версии.
- **V-008** Расширенная кастомизация Work Mode не является блокером v1.

### Gaming

- **V-100** SplitOS имеет собственный Game Launcher.
- **V-101** Game Launcher агрегирует внешние Game Clients и игры, но не владеет лицензиями/магазинами.
- **V-102** GAME и GAME_CLIENT — разные категории.
- **V-103** Запуск GAME из Work Mode должен инициировать Work→Game orchestration.
- **V-104** Запуск Game Client сам по себе не обязан включать Game Mode.
- **V-105** После закрытия игры пользователь остаётся в Game Mode/Game Launcher.
- **V-106** Одна игра может иметь несколько SplitOS profiles.
- **V-107** Game input profiles минимум: Keyboard & Mouse / Controller.
- **V-108** На v1 обязательны официально установленные игры из поддерживаемых Game Clients.
- **V-109** Manual/unofficial game support может появиться позже.

### Optimization

- **V-200** SplitOS может автоматически применять поддерживаемые user-facing game settings.
- **V-201** Оптимизация ориентируется на hardware + target display.
- **V-202** Цель — максимально возможное качество при стабильной целевой производительности.
- **V-203** Пользователь может вручную изменить настройки.
- **V-204** SplitOS не вмешивается в anti-cheat, DRM, network code, matchmaking или server logic.

### Transition

- **V-300** Work→Game должен иметь pre-flight проверку.
- **V-301** Несохранённые/критичные состояния нельзя просто уничтожать ради перехода.
- **V-302** Пользователь может отменить переход.
- **V-303** При отмене активным остаётся Work Mode.

### Shared Apps

- **V-400** Shared Apps получают отдельное состояние в Game Mode.
- **V-401** Возможные представления: overlay / locked window / secondary display / background.
- **V-402** Текущая концепция ограничивает активные Shared Apps числом 3.

### Distribution

- **V-500** Windows feature/system updates не должны бесконтрольно применяться напрямую.
- **V-501** SplitOS определяет совместимый Windows base для своего release.
- **V-502** Security fixes не должны игнорироваться; требуется compatibility lifecycle.
- **V-503** Базовая модель распространения SplitOS не предполагает публичную раздачу готового modified Windows ISO самим SplitOS.
- **V-504** SplitOS Media Builder использует Microsoft-authorized Windows source как внешний build input и применяет SplitOS Build Manifest / packages для подготовки clean-install baseline.
- **V-505** Подготовка baseline включает не только установку SplitOS, но и удаление/deprovision, disablement и конфигурацию Windows components согласно проверенной component classification.
- **V-506** Каждый Windows component классифицируется как `REMOVE`, `DISABLE`, `MODE_MANAGED`, `KEEP` либо временно `TBD`.
- **V-507** Компоненты `MODE_MANAGED` могут иметь разное runtime state в Work Mode и Game Mode.
- **V-508** Build-time optimization и runtime optimization являются разными responsibility layers.
- **V-509** После clean installation обычный runtime не должен повторно выполнять полный distribution debloat как часть каждого mode transition.

### Account / Monetization

- **V-600** SplitOS Account является отдельным продуктовым identity/entitlement context и не равен Windows license или внешнему Game Platform account.
- **V-601** Distribution/build tooling может распространяться бесплатно, а paid entitlement может открывать полные/premium SplitOS capabilities, updates/support согласно product policy.
- **V-602** Существенная информация о paid entitlement должна быть доступна пользователю до destructive installation step.

---

## 2. INFERRED

- **I-001** Для части системных операций понадобится privileged/elevated component.
- **I-002** Для mode transition потребуется persistent coordinator, способный пережить сбой UI.
- **I-003** Для game launch orchestration нужен механизм определения/удержания запуска до готовности Game Mode.
- **I-004** Полная поддержка controller-first desktop navigation потребует input abstraction layer.
- **I-005** Применение игровых настроек потребует game-specific adapters/profiles.
- **I-006** Для update safety может понадобиться системный rollback/snapshot strategy глубже обычного config rollback.
- **I-007** Game Client integrations должны быть модульными, иначе обновление одного клиента будет ломать весь Launcher.
- **I-008** Для automatic document save невозможно использовать единый универсальный механизм для любого Windows-приложения.
- **I-009** Некоторые display capabilities потребуют vendor-specific behavior помимо стандартных Windows interfaces.
- **I-010** Для воспроизводимого image servicing понадобится versioned Build Manifest и технический механизм применения offline changes.
- **I-011** Для проверки `MODE_MANAGED` state потребуется runtime verification actual state, а не только отправка start/stop commands.
- **I-012** После удаления части Windows security components SplitOS потребуется явно определить собственную минимальную security baseline.

Эти утверждения не должны считаться архитектурными решениями до Analysis & Design / prototype validation.

---

## 3. OPEN

### Performance

- **O-001** CPU overhead budget.
- **O-002** RAM budget.
- **O-003** Work→Game / Game→Work target transition time.
- **O-004** Метрика стабильного Target FPS.

### Game optimization

- **O-100** Точная логика VRR.
- **O-101** Frame generation.
- **O-102** Upscaling.
- **O-103** Поведение при недостижимом refresh-rate target.
- **O-104** Определение значимого изменения hardware context.

### Updates / recovery

- **O-200** Конкретный distribution rollback mechanism.
- **O-201** SLA интеграции critical security patches.
- **O-202** Driver update policy.
- **O-203** Baseline drift detection и repair policy.

### Integrations

- **O-300** Точный Game Client baseline v1.
- **O-301** Контракты обнаружения игр.
- **O-302** Поведение при обновлении/поломке клиента.

### Identity / licensing

- **O-400** Модель SplitOS user database.
- **O-401** Key lifecycle.
- **O-402** Online/offline entitlement.
- **O-403** Recovery access при недоступности SplitOS identity layer.
- **O-404** Точный Free/Paid capability split.

### Distribution build

- **O-500** Конкретный Microsoft-authorized Windows source для Media Builder.
- **O-501** Допустимый механизм автоматического source acquisition.
- **O-502** Поддерживаемые Windows editions / languages / architectures.
- **O-503** Формат и versioning SplitOS Build Manifest.
- **O-504** Полный Windows Component Matrix.
- **O-505** Какие component changes выполняются offline, during setup или first boot.
- **O-506** Точная removal/security baseline для Microsoft Defender Antivirus и связанных security components.

---

## 4. SUPERSEDED

### S-001

Раннее предположение:

> Work Mode и Game Mode могут частично сосуществовать.

Заменено решением:

```text
WORK xor GAME
```

Причина: параллельная активность нарушает цель deep mode isolation.

### S-002

Раннее предположение:

> Steam может классифицироваться как GAME.

Заменено:

```text
Steam → GAME_CLIENT
```

`GAME_CLIENT` не является `GAME`.

### S-003

Раннее предположение:

> SplitOS Manager можно рассматривать как отдельный продукт поверх произвольной Windows.

Заменено:

> Основной поддерживаемый продукт — отдельный SplitOS distribution.

### S-004

Раннее предположение:

> v1 может поддерживать manually added/unofficial games.

Уточнено:

> v1 baseline — официально установленные игры из поддерживаемых Game Clients. Manual library deferred.

### S-005

Ранняя трактовка:

> SplitOS распространяет готовый modified Windows image как собственный download artifact.

Заменено:

> SplitOS распространяет собственный Media Builder / manifests / packages; Windows source является внешним Microsoft-authorized build input, из которого формируется поддерживаемый clean-install baseline.
