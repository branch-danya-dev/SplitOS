# SplitOS — Startup Behavior

## 1. Purpose

Документ описывает поведение SplitOS от момента запуска компьютера до входа пользователя в выбранный операционный режим.

Документ отвечает на вопрос:

```text
Как система должна вести себя при старте,
до начала обычной работы пользователя?
```

Документ не определяет низкоуровневую реализацию logon hooks, boot components, Windows APIs или внутренние имена процессов.

---

## 2. Scope

Сценарий покрывает путь:

```text
Power on / reboot
        ↓
Windows boot
        ↓
Windows sign-in
        ↓
SplitOS context restore
        ↓
Mode selection
        ↓
Work Mode | Game Mode
```

---

## 3. Behavior goal

Startup Behavior должен обеспечить:

- запуск системы из поддерживаемого baseline;
- восстановление SplitOS user / license / entitlement context;
- проверку пригодности runtime к работе;
- безопасный выбор режима до активации рабочего или игрового контекста;
- невозможность неявного параллельного включения Work и Game.

---

## 4. Preconditions

Для нормального supported path предполагается:

- система установлена из поддерживаемого SplitOS baseline;
- Windows base загрузилась успешно;
- доступен пользовательский Windows sign-in;
- SplitOS runtime components присутствуют в ожидаемой версии или в recoverable state.

---

## 5. Primary scenario

### SB-01 — Normal startup

#### Trigger

```text
System boot or restart
```

#### Main flow

1. Windows выполняет штатную загрузку.
2. Пользователь проходит стандартный Windows sign-in.
3. После успешного sign-in SplitOS инициирует восстановление собственного runtime context.
4. SplitOS восстанавливает или проверяет:
   - identity context;
   - entitlement context;
   - runtime readiness;
   - last known supported local configuration;
   - наличие незавершённого recovery/transition state.
5. Если критических блокеров не обнаружено, SplitOS переводит сессию в состояние `MODE_SELECTION_AVAILABLE`.
6. Пользователю показывается экран выбора режима.
7. Пользователь выбирает один из режимов:
   - `WORK`;
   - `GAME`.
8. SplitOS запускает controlled activation выбранного режима.
9. После успешной активации:
   - пользователь попадает в Work Mode;
   - или пользователь попадает в Game Mode / Game Launcher.
10. Startup scenario завершается, система переходит в обычный operational state.

#### Result

```text
Exactly one committed mode is active.
```

---

## 6. Mode selection behavior

### 6.1 Rule

После startup system session не должна автоматически считаться `WORK` или `GAME`, если startup policy явно этого не требует.

Базовая политика SplitOS:

```text
Windows sign-in
        ↓
SplitOS context restore
        ↓
Mode selection
        ↓
activate chosen mode
```

### 6.2 Why this exists

Mode selection является продуктовой частью SplitOS, а не косметическим экраном.

Именно здесь пользователь выражает намерение:

```text
Current session intent = WORK | GAME
```

Это намерение далее определяет:

- active operational mode;
- mode policy activation;
- service/process lifecycle;
- display/audio/input context;
- expected UX.

---

## 7. Runtime readiness checks

До показа mode selection либо до коммита режима SplitOS должен оценить достаточность своего runtime state.

### Potential checks

- required SplitOS runtime available;
- baseline version known;
- entitlement state readable;
- required local configuration readable;
- no unrecovered critical transition residue;
- no mandatory recovery gate.

### Rule

Неподтверждённая готовность runtime не должна игнорироваться как полностью успешный startup.

---

## 8. Alternative flows

### SB-02 — Previous incomplete transition detected

#### Situation

При startup обнаружено, что предыдущая сессия завершилась в частично применённом переходе режима.

#### Behavior

1. SplitOS фиксирует, что normal startup path прерван.
2. Определяется, доступно ли безопасное автоматическое восстановление.
3. Если возможно — запускается recovery path.
4. Если recovery завершён успешно, пользователь возвращается к mode selection либо в safe known state.
5. Если recovery не может быть выполнен автоматически, пользователю показывается безопасный recovery-oriented сценарий.

#### Expected result

Startup не должен silently продолжать normal operation в неизвестном состоянии.

---

### SB-03 — Entitlement unavailable

#### Situation

Не удалось восстановить или проверить SplitOS entitlement context.

#### Behavior

1. SplitOS определяет, является ли entitlement check blocking для базового запуска.
2. Если policy допускает ограниченный запуск:
   - пользователь может быть допущен в ограниченный режим;
   - premium capabilities остаются недоступны.
3. Если policy не допускает normal activation выбранной capability path:
   - показывается соответствующее сообщение;
   - пользователь получает доступ к допустимым recovery / sign-in действиям.

#### Rule

Недоступность entitlement не должна автоматически приводить к повреждению system state.

---

### SB-04 — Runtime component mismatch

#### Situation

Обнаружено, что локальная configuration/runtime version не соответствует ожидаемому supported state.

#### Behavior

1. SplitOS определяет severity mismatch.
2. При незначительном совместимом расхождении допускается controlled degraded startup.
3. При критическом несовместимом состоянии normal startup должен быть остановлен.
4. Пользователь переводится в update/recovery path.

---

## 9. Failure behavior

Startup должен завершиться одним из состояний:

```text
WORK_ACTIVE
GAME_ACTIVE
MODE_SELECTION_AVAILABLE
RECOVERY_REQUIRED
SAFE_FALLBACK
STARTUP_BLOCKED
```

Недопустимо состояние:

```text
Unknown startup result
```

---

## 10. Behavioral rules

### BR-SB-001

Пока committed operational mode не активирован, система не должна считаться находящейся в `WORK` или `GAME`.

### BR-SB-002

Screen выбора режима должен появляться после восстановления SplitOS context, а не до него.

### BR-SB-003

Одновременно активировать `WORK` и `GAME` на этапе startup запрещено.

### BR-SB-004

Если выбран `GAME`, пользователь после успешной активации должен попасть в Game Mode context, а не на обычный Windows desktop как финальный expected UX.

### BR-SB-005

Если выбран `WORK`, пользователь должен попасть в Work Mode context с активной Work policy.

---

## 11. Open behavior questions

- Допускается ли remember-last-mode startup policy как optional feature?
- Нужен ли auto-timeout с default mode selection?
- Какие именно entitlement failures блокируют Game Mode полностью?
- Должна ли ограниченная free-конфигурация позволять вход в Work Mode при недоступности сети?
- Где заканчивается startup behavior и начинается recovery behavior в UI-терминах?

---

## 12. Result

Startup Behavior формирует первую управляемую точку SplitOS user experience:

```text
Windows sign-in
        ↓
SplitOS context restore
        ↓
Mode selection
        ↓
controlled activation of one committed mode
```

Это поведение закрепляет SplitOS не как обычную Windows-утилиту, а как систему, которая управляет пользовательским сеансовым намерением ещё до начала обычной работы.
