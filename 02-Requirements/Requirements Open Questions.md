# SplitOS — Requirements Open Questions

Документ содержит только вопросы, которые остаются открытыми после актуализации Functional Requirements.

## 1. Transition safety

### REQ-OPEN-001

Какие типы приложений поддерживают гарантированное auto-save / graceful close, а для каких требуется обязательное подтверждение пользователя?

### REQ-OPEN-002

Как формализовать правила для local servers / long-running tasks при Work → Game transition?

Нужно определить как минимум:

- критерий значимого процесса;
- когда процесс можно закрыть автоматически;
- когда требуется confirmation;
- когда transition должен быть blocked.

---

# 2. Supported Game Clients and reference games

### REQ-OPEN-003

Какие Game Clients входят в обязательный compatibility set v1?

### REQ-OPEN-004

Какой набор игр используется как reference set для разработки и проверки:

- game discovery;
- Game Profiles;
- graphics configuration;
- optimization engine;
- direct launch interception;
- Game Launcher orchestration.

---

# 3. Shared Apps / Overlay

### REQ-OPEN-005

Какие presentation states обязательны в первой версии:

```text
Overlay
Locked window
Secondary display
Background
```

### REQ-OPEN-006

Что происходит при попытке открыть четвёртое Shared App при лимите в три активных приложения?

Возможные модели должны быть проанализированы отдельно, без предварительного выбора решения.

---

# 4. Game Optimization

### REQ-OPEN-007

Как учитывать VRR при выборе target FPS и frame cap?

### REQ-OPEN-008

Как учитывать:

- DLSS;
- FSR;
- XeSS;
- frame generation;
- dynamic resolution;
- нестабильный frame pacing?

### REQ-OPEN-009

Как сохранять manual user overrides при последующем automatic recalculation после изменения hardware / display?

Нужно определить ownership между:

```text
SplitOS Recommended Profile
User Override
Current Game Configuration
```

---

# 5. User / Key / Entitlement

### REQ-OPEN-010

Как реализуется SplitOS user/key storage и entitlement validation?

Необходимо определить:

- authority пользовательского состояния;
- локальное / удалённое хранение;
- offline behavior;
- связь пользователя с профилями;
- update entitlement.

---

# 6. Update / Recovery

### REQ-OPEN-011

Какой update rollback mechanism является обязательным для SplitOS distribution?

### REQ-OPEN-012

Какой target SLA требуется для прохождения critical Microsoft security patches через SplitOS validation и distribution cycle?

---

# 7. Deferred questions

Следующие темы не блокируют v1 Requirements, но должны быть возвращены в анализ при расширении scope:

- manual / unofficial game library;
- SplitOS Overlay advanced UX;
- OBS / Twitch deep integration;
- Discord partner build;
- SplitOS communication client;
- OEM SplitOS Controller;
- глубокий Work Mode UI / workspace layer.
