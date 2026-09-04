# SplitOS — Concept Open Questions

Документ фиксирует статус вопросов, возникших на Concept / Pre-analysis этапе.

## 1. Resolved concept decisions

### CONCEPT-RES-001 — Distribution model

**RESOLVED**

SplitOS распространяется как отдельный управляемый дистрибутив на базе Windows 11, а не как универсальный installer поверх произвольной существующей Windows.

### CONCEPT-RES-002 — Mode concurrency

**RESOLVED**

Work Mode и Game Mode являются взаимоисключающими активными состояниями.

```text
WORK xor GAME
```

### CONCEPT-RES-003 — Mode entry

**RESOLVED**

После Windows sign-in SplitOS определяет user/key context и предоставляет выбор режима.

### CONCEPT-RES-004 — Game completion

**RESOLVED**

После закрытия игры пользователь остаётся в Game Mode и возвращается в SplitOS Game Launcher. Возврат в Work Mode выполняется отдельно.

### CONCEPT-RES-005 — Game vs Game Client

**RESOLVED**

`GAME` и `GAME_CLIENT` являются разными категориями.

Запуск Game Client сам по себе не инициирует обязательный переход в Game Mode.

Запуск поддерживаемой Game из Work Mode инициирует Work → Game transition.

### CONCEPT-RES-006 — Game Launcher role

**RESOLVED**

SplitOS имеет собственный Game Launcher как центральный Game Mode UX и orchestration point перед запуском игры.

### CONCEPT-RES-007 — v1 game source

**RESOLVED**

В v1 поддерживаются официально установленные игры из поддерживаемых игровых клиентов.

Manual / standalone / unofficial game addition может появиться позже.

### CONCEPT-RES-008 — Multiple Game Profiles

**RESOLVED**

Одна игра может иметь несколько SplitOS Game Profiles для разных display / input / performance сценариев.

### CONCEPT-RES-009 — Hardware refresh

**RESOLVED**

Hardware/display state перечитывается при запуске Game Launcher и сравнивается с Game Profile перед запуском игры. При значимых изменениях профиль корректируется.

### CONCEPT-RES-010 — Optimization objective

**RESOLVED**

SplitOS стремится обеспечить максимально возможное качество при стабильной производительности, соответствующей возможностям текущего display и PC. Если refresh rate недостижим, настройки снижаются для максимизации устойчивого FPS.

### CONCEPT-RES-011 — Automatic game configuration

**RESOLVED**

Поддерживаемые игровые настройки применяются автоматически с минимальным количеством вопросов пользователю. Пользователь сохраняет возможность ручного изменения.

### CONCEPT-RES-012 — Work → Game safety

**RESOLVED at concept level**

Переход включает pre-flight рабочего контекста. Несохранённые данные должны быть безопасно сохранены там, где это возможно; серверы и значимые процессы требуют решения пользователя; transition можно отменить.

### CONCEPT-RES-013 — Shared Apps

**RESOLVED at concept level**

В Game Mode Shared Apps могут использовать Overlay / Locked window / Secondary display / Background. Одновременно допускается не более трёх активных Shared Apps.

### CONCEPT-RES-014 — Windows Update

**RESOLVED**

Стандартный Windows feature/system auto-update отключается. Windows patches проходят SplitOS validation и доставляются через совместимый SplitOS distribution release.

### CONCEPT-RES-015 — Product priority

**RESOLVED**

Game Mode UI и Game Launcher являются core product scope первой версии. Глубокая кастомизация Work Mode UI имеет более низкий приоритет.

---

# 2. Remaining questions moved to Requirements / Analysis

Эти вопросы больше не блокируют Concept, но должны быть закрыты на следующих этапах.

## Transition safety

- Какие приложения поддерживают гарантированное auto-save / graceful close?
- Как формализовать critical / non-critical long-running processes?

## Supported game integrations

- Какие Game Clients входят в обязательный v1 compatibility set?
- Какой reference game set используется для validation optimization pipeline?

## Shared Apps / Overlay

- Какие presentation states обязательны для v1?
- Что происходит при попытке открыть четвёртое Shared App?

## Optimization

- Как учитывать VRR, upscaling, frame generation и frame pacing?
- Как совместить автоматический recalculation с ручными overrides пользователя?

## User / key / entitlement

- Где хранится SplitOS user/key state?
- Как подтверждается entitlement на distribution updates?

## Update / Recovery

- Какой rollback mechanism обязателен?
- Каков ускоренный процесс доставки critical Microsoft security patches?

## Future ecosystem

- Какие extension interfaces нужны для OEM SplitOS devices?
- Когда OBS/Twitch/Discord partner integrations становятся поддерживаемым product scope?

---

# 3. Concept completion status

На текущем уровне Concept / Pre-analysis не содержит блокирующих продуктовых вопросов, препятствующих переходу к детальному Requirements и Analysis & Design.

Оставшиеся вопросы относятся к поведению, контрактам, состояниям, интеграциям и техническим ограничениям и должны закрываться на соответствующих следующих этапах SSAD.
