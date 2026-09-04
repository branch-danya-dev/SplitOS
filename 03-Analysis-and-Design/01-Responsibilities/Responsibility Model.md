# SplitOS — Responsibility Model

## 1. Purpose

Документ определяет устойчивые зоны ответственности SplitOS после фиксации системных границ.

Цель — ответить на вопрос:

> **За какие решения, состояния и результаты SplitOS должен отвечать как система?**

Этот документ не определяет:

- конкретные процессы;
- Windows services;
- классы;
- модули;
- API;
- базы данных;
- deployment units.

Responsibility area не должна автоматически превращаться в отдельный software component.

---

# 2. Responsibility design rules

## RSP-RULE-001 — Responsibility before component

Сначала определяется ответственность, затем ownership, и только после этого допустимо принимать component boundaries.

```text
Responsibility
    ↓
Ownership
    ↓
Behavior / State / Data
    ↓
Interfaces
    ↓
Component architecture
```

---

## RSP-RULE-002 — One responsibility must have one primary meaning

Responsibility area должна иметь понятный предмет решения.

Плохой пример:

```text
SystemManager
→ users + modes + updates + games + recovery
```

Такое название скрывает несколько разных responsibilities.

---

## RSP-RULE-003 — Responsibility is not necessarily exclusive implementation

Несколько будущих компонентов могут реализовывать одну responsibility area.

Один компонент также может реализовывать несколько близких responsibilities, если это будет подтверждено позже.

---

## RSP-RULE-004 — External execution does not transfer product responsibility

Если Windows физически применяет настройку, SplitOS всё равно может отвечать за решение, **какое состояние требуется**.

```text
SplitOS
→ decides desired state

Windows
→ executes supported platform operation
```

---

# 3. High-level responsibility domains

После boundary analysis SplitOS разделяется на пять крупных responsibility domains:

```text
1. Distribution Engineering
2. Product Identity & Entitlement
3. Runtime Context Management
4. Game Experience Management
5. Lifecycle, Compatibility & Recovery
```

Дополнительно есть cross-cutting responsibility:

```text
Observability & Diagnostics
```

Это не component architecture, а аналитическая карта ответственности.

---

# 4. Distribution Engineering

## Responsibility statement

SplitOS отвечает за формирование воспроизводимого и поддерживаемого Windows baseline до установки продукта.

## Owns decisions about

- какая Windows base version поддерживается;
- какие Windows components классифицируются как `REMOVE / DISABLE / MODE_MANAGED / KEEP`;
- какие SplitOS packages входят в release;
- какие baseline policies применяются до первого boot;
- какие provisioning rules обязательны;
- является ли подготовленный image/build результатом поддерживаемой конфигурации;
- какие build failures делают результат недействительным.

## Does not own

- Microsoft Windows implementation;
- Windows licensing;
- Windows binary semantics;
- upstream Microsoft patch creation;
- hardware vendor drivers.

## Inputs

```text
Microsoft-authorized Windows source
SplitOS Build Manifest
Windows Component Matrix
SplitOS Package Set
Compatibility Matrix
```

## Outputs

```text
Validated SplitOS Windows Baseline
Build result
Build evidence / diagnostics
```

---

# 5. Product Identity & Entitlement

## Responsibility statement

SplitOS отвечает за собственный пользовательский продуктовый контекст и право доступа к SplitOS capabilities.

## Owns decisions about

- существует ли корректный SplitOS user context;
- какие entitlement активны;
- к каким premium capabilities пользователь имеет доступ;
- имеет ли пользователь право на определённый SplitOS update/support channel;
- какое поведение доступно offline;
- как user context связан с SplitOS profiles/settings.

## Does not own

- Windows activation/license;
- Steam/Epic/Battle.net/Xbox license ownership;
- game ownership;
- Microsoft account semantics.

## Key distinction

```text
Windows License
≠
SplitOS Account
≠
SplitOS Entitlement
≠
External Game License
```

Точная physical identity architecture остаётся на Ownership/Data/Interfaces stages.

---

# 6. Runtime Context Management

Runtime Context Management отвечает за превращение установленного baseline в активный Work или Game context.

Эта область делится на несколько responsibility areas.

---

## 6.1 Mode Intent & Active Mode State

### Responsibility statement

SplitOS отвечает за определение и поддержание текущего операционного контекста пользователя.

### Owns

- `WORK xor GAME` invariant;
- user mode selection flow;
- current desired mode;
- принятие решения о начале transition;
- запрет одновременного committed Work и Game state;
- переход к safe/recovery behavior при нарушении invariant.

### Does not own

- Windows session implementation;
- Windows boot internals;
- user intent itself.

Пользователь является authority для явного выбора режима, а SplitOS — authority для канонического operational mode state после принятого решения.

---

## 6.2 Mode Transition Coordination

### Responsibility statement

SplitOS отвечает за управляемый переход между Work и Game context как за целостную транзакцию.

### Owns

- transition request lifecycle;
- pre-flight;
- blocker classification;
- порядок mode-dependent actions;
- confirmation points;
- transition cancellation;
- commit criteria;
- rollback initiation;
- final transition outcome.

### Expected outcomes

```text
SUCCESS
CANCELLED
ROLLED_BACK
FAILED_WITH_SAFE_FALLBACK
```

### Does not own

- внутреннюю save semantics произвольного приложения;
- гарантированное graceful close там, где приложение не предоставляет такой возможности.

---

## 6.3 Mode Policy

### Responsibility statement

SplitOS отвечает за определение желаемого системного и application state для каждого режима.

### Owns

- какие `MODE_MANAGED` capabilities должны быть активны;
- какие application categories допустимы/ожидаемы;
- Work/Game service/process policy;
- notification/background policy;
- mode-specific power/display/audio/input intentions;
- difference between Work and Game active footprint.

### Example

```text
Phone Link / Cross-Device

WORK → available
GAME → inactive
```

или:

```text
Game Client background activity

WORK → restricted unless explicitly used
GAME → available as required
```

---

## 6.4 Application Lifecycle Policy

### Responsibility statement

SplitOS отвечает за системное значение приложения относительно Work/Game lifecycle.

### Owns

- classification:
  - `WORK`
  - `GAME`
  - `GAME_CLIENT`
  - `SHARED`
  - `SYSTEM`;
- desired start/stop/background behavior;
- transition participation;
- excluded-from-management rules;
- direct supported game launch semantics.

### Does not own

- application internal data;
- arbitrary app save semantics;
- third-party app business logic.

---

# 7. System Context Policy

System Context Policy отвечает за целевое состояние платформенных ресурсов, используемых режимом.

Её не следует пока разносить на отдельные implementation components.

---

## 7.1 Display Context

SplitOS отвечает за:

- preferred Work display context;
- preferred Game display context;
- target display selection;
- resolution/refresh/HDR/VRR desired state where supported;
- fallback when saved display is unavailable;
- использование display capabilities как input в Game Profile.

Windows/driver остаются authority для actual state/capability.

---

## 7.2 Audio Context

SplitOS отвечает за:

- Work/Game audio target;
- desired output/input selection;
- fallback behavior;
- restoration policy.

Windows/audio driver применяют физическое состояние.

---

## 7.3 Input Context

SplitOS отвечает за:

- user/system navigation context;
- selected SplitOS input profile;
- controller-friendly Game Mode navigation;
- distinction between system navigation and actual game input;
- mode-dependent device/application policy.

Игра остаётся authority для собственного gameplay input.

---

## 7.4 Power / Resource Context

SplitOS отвечает за desired power/resource policy режима.

Потенциально сюда относятся:

- power profile;
- background activity expectations;
- mode-managed system capabilities;
- policies that reduce irrelevant active footprint.

Конкретные registry/power APIs не входят в responsibility definition.

---

# 8. Game Experience Management

Эта domain area является главным product differentiator Game Mode.

---

## 8.1 Game Library Representation

### Responsibility statement

SplitOS отвечает за собственное объединённое представление поддерживаемых официально установленных игр.

### Owns

- unified library view;
- relation `Game ↔ Game Client` в собственной модели;
- SplitOS metadata/cache required for UX;
- eligibility of game for SplitOS-managed launch flow.

### Does not own

- installation truth when external Game Client is authoritative;
- game license;
- store catalog;
- external client account state.

---

## 8.2 Game Launch Orchestration

### Responsibility statement

SplitOS отвечает за end-to-end gaming preparation вокруг запуска поддерживаемой игры.

### Owns

```text
launch request recognition
↓
mode readiness
↓
hardware/display refresh
↓
Game Profile resolution
↓
system/input/display preparation
↓
supported game configuration application
↓
external client invocation
↓
launch result observation
```

### Does not own

- external platform authentication;
- DRM/anti-cheat behavior;
- game process internal startup semantics.

---

## 8.3 Game Profiles

### Responsibility statement

SplitOS отвечает за собственную модель пользовательского игрового сценария.

### Owns

- multiple profiles per game;
- relation between game, display, input and performance expectations;
- stored SplitOS recommended/user-selected context;
- profile validity relative to hardware snapshot;
- profile selection/re-evaluation policy.

Possible conceptual information:

```text
Game
Target display
Input profile
Resolution
Refresh rate
Target FPS
Graphics configuration
Hardware context
External client relation
```

Physical storage определяется позже.

---

## 8.4 Hardware Context Evaluation

### Responsibility statement

SplitOS отвечает за формирование собственной подтверждённой snapshot-модели relevant hardware context.

### Owns

- какие hardware facts нужны для Game Profile;
- когда требуется refresh;
- что считается meaningful change;
- re-evaluation trigger.

### Does not own

- физические возможности оборудования;
- driver implementation.

Authority actual capabilities:

```text
Windows / Driver / Device
```

---

## 8.5 Game Optimization Policy

### Responsibility statement

SplitOS отвечает за формирование и применение поддерживаемой рекомендуемой конфигурации игры.

### Owns

- optimization objective;
- target performance interpretation;
- quality/performance trade-off policy;
- supported configuration knowledge;
- automatic recommendation/application policy;
- handling of user override at product level.

### Core objective

```text
maximize visual quality
subject to stable performance appropriate to active display/hardware
```

### Does not own

- renderer implementation;
- anti-cheat;
- DRM;
- matchmaking;
- network/server logic;
- game internal unsupported settings.

---

## 8.6 Game Mode UX

### Responsibility statement

SplitOS отвечает за целостный controller-friendly пользовательский Game Mode experience.

### Owns

- Game Launcher UX;
- game selection;
- profile selection;
- device/profile controls exposed in Game Mode;
- return-to-launcher behavior after game exit;
- Game→Work entry point;
- exposure of Shared Apps.

Game Launcher является продуктовой поверхностью этой responsibility, но document intentionally не утверждает, что вся responsibility реализуется одним процессом.

---

## 8.7 Shared App Experience

### Responsibility statement

SplitOS отвечает за то, как разрешённое shared-приложение участвует в Game Mode.

### Owns

- Shared App eligibility/classification;
- presentation policy;
- active-limit policy;
- placement/context decisions;
- Game Mode lifecycle around Shared App.

Potential representations:

```text
Overlay
Locked window
Secondary display
Background
```

Third-party app остаётся владельцем своих данных и внутренней логики.

---

# 9. Lifecycle, Compatibility & Recovery

---

## 9.1 Compatibility Management

### Responsibility statement

SplitOS отвечает за решение, какие внешние версии и комбинации считаются поддерживаемыми.

### Owns

- supported Windows base;
- compatibility status SplitOS release;
- supported Game Client versions/ranges where needed;
- tested driver constraints/recommendations;
- component classification validity;
- known compatibility issues.

### Does not own

- release cadence Microsoft/vendor/client owners.

---

## 9.2 Update Orchestration

### Responsibility statement

SplitOS отвечает за безопасный lifecycle собственных release changes и одобренных изменений Windows base.

### Owns

- whether change is compatible;
- SplitOS release packaging/manifest;
- update eligibility according to product policy;
- migration expectations;
- update preconditions;
- update result validation.

Microsoft остаётся source authority для Windows patches.

---

## 9.3 Recovery Coordination

### Responsibility statement

SplitOS отвечает за возврат системы к согласованному поддерживаемому состоянию после partial failure.

### Owns

- recovery trigger;
- last-known-good SplitOS state concept;
- rollback decision;
- safe fallback target;
- user-visible recovery result;
- preservation priority for SplitOS-managed user data.

### Recovery priority

```text
User data integrity
        ↓
System bootability
        ↓
Known safe mode/state
        ↓
Profile restoration
        ↓
UX restoration
```

---

# 10. Observability & Diagnostics

## Responsibility statement

SplitOS отвечает за возможность объяснить собственные решения и подтвердить фактический результат управляемых операций.

### Owns

- logging SplitOS decisions;
- transition evidence;
- build evidence;
- component-policy evidence;
- desired vs actual state verification;
- failure classification;
- diagnostics useful for support/recovery.

### Important rule

```text
command sent
≠
state applied
```

SplitOS должен различать desired state и verified actual state.

---

# 11. Responsibility interaction map

```text
Distribution Engineering
        │
        ▼
Installed Baseline
        │
        ├──────────────► Compatibility Management
        │
        ▼
Product Identity & Entitlement
        │
        ▼
Mode Intent & Active Mode State
        │
        ▼
Mode Transition Coordination
        │
        ├──────────────► Mode Policy
        │                  │
        │                  ├── Application Lifecycle
        │                  ├── Display Context
        │                  ├── Audio Context
        │                  ├── Input Context
        │                  └── Power/Resource Context
        │
        ▼
Game Experience Management
        ├── Game Library
        ├── Game Launch Orchestration
        ├── Game Profiles
        ├── Hardware Context
        ├── Optimization Policy
        ├── Game Mode UX
        └── Shared Apps

Compatibility / Update / Recovery
        ↕
all managed areas

Observability & Diagnostics
        ↕
all managed areas
```

---

# 12. Responsibility boundaries that must not collapse

Следующие ответственности должны оставаться аналитически различимыми, даже если позднее часть из них окажется в одном implementation unit.

## Distribution Engineering vs Runtime Context Management

```text
Build-time baseline preparation
≠
live mode switching
```

---

## Mode State vs Mode Policy

```text
Which mode is active?
≠
What should be active in that mode?
```

---

## Mode Transition vs Application Lifecycle

```text
End-to-end transaction
≠
policy for one application/process
```

---

## Game Library vs External Client Authority

```text
SplitOS unified representation
≠
external installation/license truth
```

---

## Game Profile vs Hardware Actual State

```text
stored desired scenario
≠
actual current hardware capability
```

---

## Optimization Policy vs Game Ownership

```text
SplitOS recommendation/configuration
≠
game renderer/business logic
```

---

## Entitlement vs Windows Licensing

```text
SplitOS feature access
≠
Windows license validity
```

---

# 13. Preliminary responsibility matrix

| ID | Responsibility | Primary decision/result |
|---|---|---|
| RSP-DIST-001 | Distribution Engineering | What constitutes a valid SplitOS baseline |
| RSP-ID-001 | Product Identity & Entitlement | Who the SplitOS user is and what SplitOS capabilities are available |
| RSP-MODE-001 | Mode Intent & Active State | Which operational mode is canonically active |
| RSP-TRANS-001 | Mode Transition Coordination | Whether/how a mode transition commits, cancels or rolls back |
| RSP-POLICY-001 | Mode Policy | Desired runtime footprint for Work/Game |
| RSP-APP-001 | Application Lifecycle Policy | How an app/process participates in active mode |
| RSP-DISP-001 | Display Context | Desired display state for current scenario |
| RSP-AUDIO-001 | Audio Context | Desired audio state |
| RSP-INPUT-001 | Input Context | Desired navigation/input context |
| RSP-POWER-001 | Power / Resource Context | Desired mode resource policy |
| RSP-LIB-001 | Game Library Representation | Unified supported game view |
| RSP-LAUNCH-001 | Game Launch Orchestration | Prepared and validated managed game launch |
| RSP-PROFILE-001 | Game Profiles | Canonical SplitOS scenario configuration for a game |
| RSP-HW-001 | Hardware Context Evaluation | Relevant verified hardware snapshot and change detection |
| RSP-OPT-001 | Game Optimization Policy | Recommended supported game configuration |
| RSP-GUX-001 | Game Mode UX | Controller-friendly integrated gaming interaction |
| RSP-SHARED-001 | Shared App Experience | How shared apps participate in Game Mode |
| RSP-COMPAT-001 | Compatibility Management | What combinations are supported |
| RSP-UPD-001 | Update Orchestration | How approved product/base changes reach installed SplitOS |
| RSP-REC-001 | Recovery Coordination | How failed/partial state returns to safety |
| RSP-OBS-001 | Observability & Diagnostics | Evidence of decisions, state and failures |

IDs являются аналитическими identifiers и не подразумевают будущие service names.

---

# 14. Open responsibility questions

Следующие вопросы требуют Ownership / Behavior / Data analysis:

1. Кто является canonical owner persisted active-mode state?
2. Кто принимает final transition commit decision?
3. Как разделяется ownership между local SplitOS Account context и remote entitlement authority?
4. Кто владеет canonical `Game` identity внутри SplitOS при наличии нескольких external clients?
5. Кто владеет user override vs recommended profile precedence?
6. Кто является authority для lifecycle состояния `MODE_MANAGED` component после команды start/stop?
7. Кто хранит last-known-good baseline/runtime state для recovery?
8. Где проходит ownership между Compatibility Matrix и Build Manifest?
9. Кто определяет whether a compatibility failure blocks update/launch/transition?
10. Какой responsibility area подтверждает actual applied state после Windows operation?

Эти вопросы не следует закрывать здесь преждевременно.

---

# 15. Result

После Boundary и Responsibility analysis SplitOS выглядит не как набор предполагаемых сервисов, а как система решений:

```text
Build a known baseline
        ↓
Resolve product identity/entitlement
        ↓
Maintain one active operational mode
        ↓
Apply mode-specific runtime policy
        ↓
Orchestrate gaming context and launches
        ↓
Validate compatibility
        ↓
Update / recover safely
        ↓
Verify and explain actual state
```

Следующий SSAD step:

```text
RESPONSIBILITIES
        ↓
OWNERSHIP
```

На Ownership stage для каждой существенной responsibility будет определено:

```text
who decides
who stores truth
who may change it
who consumes it
who verifies it
```
