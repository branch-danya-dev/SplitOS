# SplitOS — Elicitation Log

## 1. Purpose

Документ хранит вопросы, ответы и влияние ответов на системную модель.

Он не заменяет Requirements. После согласования ответ должен быть отражён в каноническом Concept / Requirements.

---

## 2. Elicitation records

| ID | Topic | Question | Answer / decision | Status | Impact |
|---|---|---|---|---|---|
| EL-001 | Mode state | Могут ли Work и Game быть активны одновременно? | Нет. Активен ровно один выбранный режим. Параллельное состояние разрушает смысл изоляции и оптимизации. | VERIFIED | Mode XOR invariant |
| EL-002 | Startup | Что происходит после Windows sign-in? | SplitOS восстанавливает свой user/key context и предлагает выбор режима. | VERIFIED | Startup/session state model |
| EL-003 | Game exit | Что после закрытия игры? | Пользователь возвращается в Game Launcher/Game Mode. Возврат в Work — только отдельным действием. | VERIFIED | Game lifecycle |
| EL-004 | Game Client | Должен ли запуск Steam автоматически активировать Game Mode? | Нет. Game Client не равен Game. Принудительный переход вызывается запуском объекта типа Game. | VERIFIED | App classification |
| EL-005 | Direct game launch | Что если игра запускается из Work Mode напрямую? | SplitOS должен сначала подготовить Game Mode/Game Launcher и профили, затем продолжить запуск. | VERIFIED | Launch orchestration |
| EL-006 | Game definition | Какие игры считаются поддерживаемыми? | На v1 — официально установленные игры из поддерживаемых игровых клиентов. | VERIFIED | MVP compatibility scope |
| EL-007 | Manual games | Поддерживать ли вручную добавленные/неофициальные игры? | Концептуально возможно в будущем, но не обязательный scope v1. | VERIFIED | Deferred scope |
| EL-008 | Multiple profiles | Может ли одна игра иметь несколько профилей? | Да. Например Desktop/KB&M и TV/Controller. | VERIFIED | Game profile model |
| EL-009 | Hardware refresh | Когда перечитывать железо? | При запуске Game Launcher; перед запуском игры проверять изменения контекста и пересчитывать профиль только при необходимости. | VERIFIED | Profile resolution flow |
| EL-010 | Optimization target | Какова цель автооптимизации? | Максимально возможное качество при стабильной производительности, соотнесённой с текущим display context. | VERIFIED | Optimization policy |
| EL-011 | 60 Hz case | Что при мощном ПК и 60 Hz TV? | Максимально высокое качество, target около 60 FPS, допускается FPS cap. | VERIFIED | Optimization example |
| EL-012 | High refresh case | Что при 144/280/300 Hz display? | Качество снижается настолько, насколько необходимо для максимально устойчивого FPS около возможностей устройства/дисплея. | VERIFIED | Optimization example |
| EL-013 | User override | Может ли пользователь менять настройки игры вручную? | Да. SplitOS не блокирует ручные настройки. | VERIFIED | User authority |
| EL-014 | Work blockers | Что делать с unsaved docs / servers при Work→Game? | Сначала pre-flight. Безопасно сохраняемые документы можно сохранить/закрыть; сомнительные состояния требуют уведомления/подтверждения. Переход можно отменить. | VERIFIED | Transactional transition |
| EL-015 | Early launched game | Что если игра уже начала запуск при незавершённом переходе? | При отмене перехода игра должна быть остановлена/закрыта там, где возможно; система остаётся в Work. | VERIFIED | Failure/rollback flow |
| EL-016 | Shared Apps | Как ведут себя Discord/browser/music в Game Mode? | Получают отдельное gaming representation: overlay, locked window, secondary display или background. | VERIFIED | Shared App model |
| EL-017 | Shared limit | Сколько Shared Apps одновременно? | До 3 активных приложений в текущей концепции. | VERIFIED | Resource/UX policy |
| EL-018 | Product depth | Это мягкая надстройка или глубокая сборка? | Агрессивно оптимизированный отдельный дистрибутив. | VERIFIED | Distribution architecture |
| EL-019 | Distribution | Устанавливать поверх произвольной Windows? | Нет. Основной продукт — отдельный управляемый SplitOS distribution. | VERIFIED | Installation boundary |
| EL-020 | Windows Update | Кто управляет обновлением Windows base? | SplitOS distribution lifecycle. Стандартные feature/system updates не должны применяться бесконтрольно. | VERIFIED | Update ownership |
| EL-021 | Security patches | Отказываемся ли от security patches? | Нет. Патчи должны интегрироваться после проверки совместимости. | VERIFIED | Security/update NFR |
| EL-022 | Game Launcher | Нужен ли свой лаунчер? | Да. Это core Game Mode UX и orchestration point поверх внешних клиентов. | VERIFIED | Product boundary |
| EL-023 | Game config | Может ли SplitOS менять параметры игры? | Да, если существует безопасный поддерживаемый механизм; только UX/performance settings. | VERIFIED | Game experience management |
| EL-024 | Anti-cheat | Вмешивается ли SplitOS в anti-cheat/DRM/network/matchmaking? | Нет. | VERIFIED | Hard boundary |
| EL-025 | Work UI | Насколько важна кастомизация Work Mode в v1? | Низкий приоритет. Сначала чистая Windows; расширенный Work UX позже. | VERIFIED | MVP priority |
| EL-026 | Game UI | Насколько важен Game Mode UI? | Core scope v1; controller-friendly console-like experience. | VERIFIED | MVP priority |
| EL-027 | Displays | Какие дисплеи поддерживать? | Все корректно обнаруживаемые подключённые дисплеи в поддерживаемой Windows/driver environment. | VERIFIED | Compatibility requirement |
| EL-028 | Storage | Нужен ли отдельный диск для игр? | Нет. Разделение логическое; один SSD обязателен как поддерживаемый сценарий. | VERIFIED | Storage invariant |
| EL-029 | Future ecosystem | Может ли SplitOS расшириться своим hardware/partner software? | Да. OEM controller, partner apps, communication/social/streaming layers допустимы в будущем. | VERIFIED | Extensibility |
| EL-030 | Windows source distribution | Должен ли SplitOS публично отдавать готовый modified Windows ISO? | Нет как базовая модель. SplitOS Builder использует Microsoft-authorized Windows source как внешний вход и формирует поддерживаемый baseline локально/в допустимом deployment flow. | VERIFIED | Distribution/legal boundary |
| EL-031 | Build-time preparation | Должна ли подготовка Windows происходить только после установки как набор runtime tweaks? | Нет. Удаление/deprovision компонентов, baseline policies и внедрение SplitOS должны применяться до/во время clean installation через Build Manifest. | VERIFIED | Build pipeline |
| EL-032 | Component lifecycle | Все ненужные Windows components нужно либо удалить, либо оставить включёнными? | Нет. Используются классы `REMOVE / DISABLE / MODE_MANAGED / KEEP`. | VERIFIED | Component classification |
| EL-033 | Mode-managed Windows capability | Может ли компонент быть полезен в Work и отключён в Game? | Да. Например Phone Link / Cross-Device может оставаться доступным в Work и отключаться в Game. | VERIFIED | Runtime mode policy |
| EL-034 | Build vs runtime | Кто управляет Windows после установки? | Builder заканчивает responsibility после создания baseline; Installed Runtime управляет live state, transitions и MODE_MANAGED lifecycle. | VERIFIED | Runtime boundary |
| EL-035 | Monetization | За что платит пользователь? | Distribution/build tooling может быть бесплатным; paid SplitOS Account/entitlement предоставляет product capabilities, updates/support согласно policy. | VERIFIED | Identity/entitlement scope |
| EL-036 | Destructive install disclosure | Когда сообщать о paid limitations? | Существенные ограничения должны быть показаны до форматирования/установки, а не после удаления старой системы. | VERIFIED | Setup UX / consumer transparency |

---

## 3. Open elicitation topics

Следующие вопросы ещё требуют анализа или прототипирования:

- безопасный механизм intercept/hold запуска игры;
- точная модель авто-сохранения документов;
- критерии «significant hardware context change»;
- метрика стабильного FPS;
- точная политика работы с VRR/frame generation/upscaling;
- поддерживаемый baseline Game Clients;
- rollback model distribution updates;
- точные CPU/RAM/transition-time budgets;
- security/licensing model SplitOS user/key context;
- конкретный Microsoft-authorized Windows source для Builder;
- механизм автоматического source acquisition и его licensing validation;
- полный Windows Component Matrix и validation каждой REMOVE/DISABLE/MODE_MANAGED гипотезы;
- точный Free/Paid feature split;
- offline entitlement behavior;
- baseline drift detection после установки.
