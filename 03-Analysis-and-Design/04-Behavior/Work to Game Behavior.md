# SplitOS — Work to Game Behavior

## 1. Purpose

Документ описывает поведение SplitOS при переходе пользователя из активного Work Mode в Game Mode.

Это один из центральных сценариев продукта, потому что именно здесь SplitOS должен доказать ценность разделения режимов.

---

## 2. Scope

Сценарий покрывает:

```text
Committed mode = WORK
        ↓
User or managed request for GAME
        ↓
Pre-flight / blocker handling
        ↓
Work context deactivation
        ↓
Game context activation
        ↓
Game Launcher available
```

---

## 3. Trigger types

Переход может быть инициирован:

1. пользователем вручную из SplitOS UI;
2. управляемым direct-game-launch сценарием;
3. другим разрешённым SplitOS action, который semantically означает `request GAME`.

---

## 4. Behavior goal

Work → Game должен:

- безопасно остановить или деактивировать ненужный Work context;
- не потерять пользовательские данные;
- подготовить Game Mode environment;
- активировать display/audio/input policies для gaming scenario;
- открыть пользователя в Game Mode / Game Launcher;
- не допускать ложного коммита `GAME`, если переход фактически не завершился.

---

## 5. Preconditions

- committed operational mode = `WORK`;
- transition lifecycle = `IDLE`;
- runtime ready;
- requested target mode = `GAME`.

---

## 6. Main scenario

### WG-01 — User switches from Work to Game

#### Trigger

```text
User selects "Switch to Game Mode"
```

#### Main flow

1. SplitOS принимает `request GAME`.
2. Создаётся новый transition context.
3. Transition lifecycle переходит в `REQUESTED`.
4. SplitOS начинает `INSPECTING` current Work context.
5. Проверяются потенциальные blockers:
   - unsaved documents;
   - active local servers;
   - long-running work tasks;
   - protected or confirmation-required applications;
   - other work-critical states.
6. Если blockers отсутствуют или могут быть safely resolved, transition продолжается.
7. Если требуется решение пользователя, transition входит в `AWAITING_USER`.
8. После подтверждения или разрешения blockers transition входит в `RESOLVING`.
9. Work-related managed components и процессы подготавливаются к деактивации.
10. Transition входит в `APPLYING`.
11. SplitOS применяет Game Mode policy:
    - Game display context;
    - Game audio context;
    - Game input context;
    - mode-managed service/process changes;
    - game-oriented runtime environment.
12. SplitOS выполняет `VERIFYING`.
13. Проверяется, что целевой Game context реально достигнут в достаточной степени.
14. Если verification успешен, выполняется `COMMITTING`.
15. Committed operational mode меняется с `WORK` на `GAME`.
16. Transition завершается как `COMPLETED`.
17. Пользователю становится доступен Game Mode, базовая точка входа — Game Launcher.

#### Result

```text
Committed mode = GAME
Transition = IDLE
Game session = LAUNCHER
```

---

## 7. Pre-flight behavior

Pre-flight — не косметическая проверка, а обязательный semantic gate.

### Goals of pre-flight

- предотвратить потерю пользовательских данных;
- обнаружить процессы, несовместимые с immediate mode switch;
- различить auto-resolvable и user-resolvable blockers;
- не коммитить Game Mode преждевременно.

### Pre-flight classes

#### Non-blocking

Состояние не мешает переходу.

#### Auto-resolvable

SplitOS может обработать состояние сам без высокого риска.

#### User-decision-required

Нужно согласие пользователя.

#### Blocking

Без разрешения состояния переход не должен быть продолжен.

---

## 8. Alternative flows

### WG-02 — User confirmation required

1. Во время `INSPECTING` обнаружен blocker, требующий выбора пользователя.
2. Transition переходит в `AWAITING_USER`.
3. Пользователю показывается информация:
   - какой процесс/состояние мешает переходу;
   - какие действия предлагаются;
   - можно ли продолжить, отменить или отложить.
4. Пользователь выбирает допустимое действие.
5. Если состояние разрешено — переход продолжается.
6. Если пользователь отменяет переход — переход завершается как `CANCELLED`.

---

### WG-03 — Transition cancelled

1. Пользователь отменяет переход.
2. Никакой commit в `GAME` не выполняется.
3. Если были частично применены preparatory actions, выполняется возврат к safe Work state.
4. Итоговое состояние:
   - committed mode = `WORK`;
   - transition outcome = `CANCELLED`.

---

### WG-04 — Verification failed

1. После `APPLYING` verification показывает, что целевой Game context не достигнут.
2. Transition не может завершиться как `COMPLETED`.
3. Выполняется:
   - `ROLLING_BACK`, если rollback доступен;
   - либо safe fallback.
4. После завершения rollback/fallback committed mode не должен ложно остаться `GAME`, если semantic activation не удалась.

---

### WG-05 — Direct game launch escalates to transition

1. Пользователь запускает поддерживаемую игру из Work Mode.
2. SplitOS интерпретирует событие как `request GAME + launch managed game`.
3. Поведение перехода идентично WG-01, но после успешного commit запускается flow Game Launch Behavior.

---

## 9. Work context deactivation

Work Mode не обязан уничтожать все приложения без разбора.

Поведение зависит от classification/policy:

- `WORK-only managed` — останавливаются/деактивируются;
- `SHARED` — переводятся в допустимое Game representation или закрываются по policy;
- `SYSTEM/KEEP` — остаются согласно platform need;
- `explicit blocker` — требуют решения.

---

## 10. Game context activation

При переходе в Game Mode SplitOS должен подготовить игровой пользовательский контекст до того, как считать переход завершённым.

### Minimal semantic target

- Game mode policy active;
- target Game display resolved;
- target audio resolved;
- input context resolved;
- managed Game runtime ready;
- user can enter Game Launcher.

---

## 11. Behavioral rules

### BR-WG-001

`WORK` остаётся committed mode до успешного semantic commit в `GAME`.

### BR-WG-002

Нельзя считать переход успешным только потому, что пользователь нажал кнопку `Switch to Game`.

### BR-WG-003

Lossy закрытие пользовательских приложений без соответствующей policy или подтверждения запрещено.

### BR-WG-004

При cancel/rollback Work Mode должен остаться или быть восстановлен как safe operational state.

### BR-WG-005

Успешный переход должен завершаться пользовательским доступом к Game Mode UX, а не только изменением нескольких service states.

---

## 12. Observable outcomes

Допустимые terminal outcomes:

```text
COMPLETED
CANCELLED
ROLLED_BACK
FAILED_WITH_SAFE_FALLBACK
```

Для каждого outcome должен быть понятен итоговый committed mode.

---

## 13. Open behavior questions

- Какие blockers считаются auto-resolvable в v1?
- Нужно ли поддерживать background continuation отдельных work tasks?
- Как классифицировать OBS/streaming в Work→Game для streamer scenario?
- Нужен ли отдельный fast-path для полностью чистого Work context?
- Когда exactly Game Launcher считается “available enough” для commit?

---

## 14. Result

Work → Game Behavior определяет SplitOS как систему с транзакционным переключением контекста:

```text
Request GAME
        ↓
Inspect Work context
        ↓
Resolve blockers
        ↓
Apply Game policy
        ↓
Verify semantic target
        ↓
Commit GAME
        ↓
Enter Game Mode / Game Launcher
```
