# SplitOS — System State Model

## 1. Purpose

Документ определяет канонические state dimensions SplitOS после фиксации boundaries, responsibilities и ownership.

Цель — описать устойчивые состояния и допустимые переходы, не сводя всю систему к одному глобальному `status`.

SplitOS имеет несколько ортогональных state machines, каждая из которых отвечает на свой вопрос.

---

# 2. State-model rule

Состояние моделируется по формуле:

```text
state
+
transition
+
trigger
+
authority
+
guard
+
consequence
```

Само наличие enum или поля `status` не является state model.

---

# 3. Orthogonal state dimensions

Для SplitOS выделяются как минимум следующие dimensions:

```text
1. System Session Lifecycle
2. Operational Mode
3. Mode Transition Lifecycle
4. Game Session Lifecycle
5. Game Launch Preparation
6. Recovery Lifecycle
```

Они не должны быть объединены в одно поле.

Пример:

```text
Operational Mode = GAME
Game Session = LAUNCHER
Recovery = INACTIVE
```

— валидная комбинация.

---

# 4. System Session Lifecycle

## States

```text
BOOTING
WINDOWS_SIGNED_IN
SPLITOS_CONTEXT_RESOLVING
MODE_SELECTION
OPERATIONAL
RECOVERY
SHUTTING_DOWN
```

## BOOTING

Meaning:

Windows ещё не предоставил пользовательскую operational session, в которой SplitOS может разрешить собственный context.

Owner:

```text
Windows = actual boot/session evidence
SplitOS = interpretation of readiness for own lifecycle
```

Exit trigger:

```text
Windows sign-in complete
```

---

## WINDOWS_SIGNED_IN

Windows user session доступна, но SplitOS context ещё не разрешён.

Allowed next:

```text
SPLITOS_CONTEXT_RESOLVING
RECOVERY
SHUTTING_DOWN
```

---

## SPLITOS_CONTEXT_RESOLVING

SplitOS определяет:

- local product context;
- user/account context;
- entitlement state required for current UX;
- baseline/runtime validity required to continue.

Success:

```text
→ MODE_SELECTION
```

Failure:

```text
→ RECOVERY
```

или ограниченный safe path, если такой будет подтверждён позже.

---

## MODE_SELECTION

Operational mode ещё не committed.

Canonical operational mode state:

```text
NONE
```

Allowed user intent:

```text
WORK
GAME
```

После успешного initial transition:

```text
→ OPERATIONAL
```

---

## OPERATIONAL

SplitOS имеет committed operational mode:

```text
WORK xor GAME
```

Система находится в нормальном пользовательском runtime.

Внутри этого state параллельно работают отдельные operational/game state machines.

---

## RECOVERY

Нормальная operational assumption нарушена или восстановление требуется до продолжения обычного UX.

Recovery state не означает автоматически поломку Windows boot.

Possible reasons:

- inconsistent mode state;
- failed transition without immediate rollback;
- invalid runtime/baseline condition;
- failed update;
- partial managed-state application.

Exit зависит от Recovery Model.

---

## SHUTTING_DOWN

Windows session/OS завершается.

Новые mode/game transitions не должны начинаться.

---

# 5. Operational Mode state

Operational Mode — отдельная canonical state dimension.

Owner:

```text
Mode Intent & Active Mode State
```

States:

```text
NONE
WORK
GAME
```

## Invariant

```text
WORK xor GAME
```

Если committed state существует, он должен быть ровно одним из:

```text
WORK
GAME
```

`NONE` допустим только там, где operational mode ещё не committed либо intentionally cleared during recovery/startup handling.

---

# 6. Operational Mode transitions

## NONE → WORK

Trigger:

```text
user selects Work
```

Guard:

- SplitOS context resolved;
- required Work baseline is applicable;
- no blocking recovery state.

Consequence:

- Work desired state applied/verified;
- canonical active mode committed to `WORK`;
- session becomes operational.

---

## NONE → GAME

Trigger:

```text
user selects Game
```

Guard:

- SplitOS context resolved;
- Game Mode entitlement/policy permits entry;
- required Game baseline is applicable;
- no blocking recovery state.

Consequence:

- Game desired state applied/verified;
- canonical active mode committed to `GAME`;
- Game Launcher becomes normal Game Mode surface.

---

## WORK → GAME

Not a direct assignment.

Must go through:

```text
Mode Transition Lifecycle
```

Canonical `WORK` remains committed until transition commit criteria are satisfied.

---

## GAME → WORK

Also goes through Mode Transition Lifecycle.

Canonical `GAME` remains committed until Work state is successfully prepared and commit occurs.

---

# 7. Critical invariant during transition

SplitOS must not represent transition as:

```text
WORK = true
GAME = true
```

и не должен преждевременно считать target mode committed.

Recommended semantic model:

```text
committedMode = WORK
transitionTarget = GAME
transitionState = APPLYING
```

После commit:

```text
committedMode = GAME
transitionTarget = null
transitionState = IDLE
```

Это сохраняет `WORK xor GAME` даже во время перехода.

---

# 8. Derived UI state

UI может показывать:

```text
Switching to Game Mode…
```

но это derived presentation state.

UI не становится владельцем operational truth.

---

# 9. Cross-state constraints

## Constraint 1

Game Session может быть активной только если:

```text
Operational Mode = GAME
```

Исключение — временное launch interception/preparation до commit, но фактический игровой runtime не должен считаться нормальной `GAME_RUNNING` session до успешного Game Mode commit.

---

## Constraint 2

Normal Work application lifecycle policy применяется только если:

```text
Operational Mode = WORK
```

или как rollback target в transition/recovery flow.

---

## Constraint 3

Если Session Lifecycle = RECOVERY, normal mode transition commands могут быть ограничены до восстановления coherent state.

---

# 10. State evidence vs canonical state

SplitOS canonical state не должен строиться из одного случайного Windows signal.

Пример:

```text
Some Game services are running
```

не означает автоматически:

```text
Operational Mode = GAME
```

Actual Windows/process/service state является evidence.

Canonical operational mode принадлежит SplitOS и подтверждается через согласованную проверку required target state.

---

# 11. Persistence requirement

Physical persistence mechanism пока не определяется.

Но ownership требует, чтобы система могла различать минимум:

```text
last committed mode
in-progress transition metadata
last known safe operational state
recovery-required marker where needed
```

Нужно избежать ситуации, когда после crash/reboot UI угадывает mode только по запущенным процессам.

---

# 12. Boot after interrupted transition

Если предыдущая session завершилась во время transition:

```text
WORK → GAME
```

при следующем запуске SplitOS не должен автоматически предполагать, что target `GAME` был committed.

Нужно использовать persisted transition/recovery evidence.

Base rule:

```text
no confirmed commit
→ target mode is not canonical
```

Recovery/rollback policy уточняется отдельно.

---

# 13. Forbidden assumptions

Не допускается моделировать:

```text
mode = SWITCHING
```

как замену Operational Mode.

Почему:

`SWITCHING` отвечает на вопрос о transition lifecycle, а не о committed operational context.

Также нельзя смешивать:

```text
GAME
GAME_RUNNING
```

`GAME` — operational mode.

`GAME_RUNNING` — game-session state.

---

# 14. Result

Канонический SplitOS runtime state должен мыслиться как комбинация независимых dimensions:

```text
Session Lifecycle
+
Committed Operational Mode
+
Transition Lifecycle
+
Game Session Lifecycle
+
Game Launch Preparation
+
Recovery Lifecycle
```

Это позволяет точно моделировать normal, transitional и failure состояния без conflicting truth.
