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

# 5. User / Account / Entitlement

### REQ-OPEN-010

Как реализуется SplitOS user/account/key storage и entitlement validation?

Необходимо определить:

- authority пользовательского состояния;
- локальное / удалённое хранение;
- offline behavior;
- связь пользователя с профилями;
- update entitlement;
- support entitlement;
- feature entitlement.

### REQ-OPEN-013

Какие SplitOS capabilities входят в Free baseline, а какие требуют paid entitlement?

Нужно отдельно определить minimum-safe behavior при отсутствии или истечении entitlement.

### REQ-OPEN-014

Какие capabilities должны оставаться доступными offline при временной недоступности SplitOS account backend?

---

# 6. Distribution Builder / Windows Source

### REQ-OPEN-015

Какой Microsoft-authorized Windows source является поддерживаемым входом SplitOS Media Builder?

### REQ-OPEN-016

Имеет ли Builder право автоматизировать получение Windows source, либо source должен предоставляться пользователем/официальным Microsoft tooling?

### REQ-OPEN-017

Какие Windows editions, languages и architectures входят в supported SplitOS baseline?

### REQ-OPEN-018

Как Builder проверяет identity/integrity Windows source до применения SplitOS Build Manifest?

### REQ-OPEN-019

Какие действия выполняются:

```text
offline image servicing
during setup
first boot
runtime
```

и какие операции запрещено переносить между этими фазами?

---

# 7. Windows Component Baseline

### REQ-OPEN-020

Какой полный Windows Component Matrix входит в v1?

Для каждого элемента требуется определить:

```text
REMOVE
DISABLE
MODE_MANAGED
KEEP
```

### REQ-OPEN-021

Какие Work-oriented Windows capabilities должны быть `MODE_MANAGED`, например Phone Link / Cross-Device, Print subsystem, indexing или sync clients?

### REQ-OPEN-022

Какие Game-oriented components должны активироваться только в Game Mode?

### REQ-OPEN-023

Какой minimum security baseline должен оставаться после planned removal Microsoft Defender Antivirus и связанных security components?

### REQ-OPEN-024

Какие component removals считаются необратимыми в рамках обычного runtime и требуют repair/rebuild/update path?

---

# 8. Update / Recovery

### REQ-OPEN-011

Какой update rollback mechanism является обязательным для SplitOS distribution?

### REQ-OPEN-012

Какой target SLA требуется для прохождения critical Microsoft security patches через SplitOS validation и distribution cycle?

### REQ-OPEN-025

Как Installed Runtime определяет baseline drift относительно release manifest/component matrix?

### REQ-OPEN-026

Какой repair path используется, если обязательный SplitOS package или `KEEP` dependency повреждён/удалён?

---

# 9. Deferred questions

Следующие темы не блокируют v1 Requirements, но должны быть возвращены в анализ при расширении scope:

- manual / unofficial game library;
- SplitOS Overlay advanced UX;
- OBS / Twitch deep integration;
- Discord partner build;
- SplitOS communication client;
- OEM SplitOS Controller;
- глубокий Work Mode UI / workspace layer.
