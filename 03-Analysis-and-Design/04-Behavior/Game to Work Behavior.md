# SplitOS — Game to Work Behavior

## 1. Purpose

Документ описывает поведение SplitOS при переходе из активного Game Mode обратно в Work Mode.

Сценарий не должен путаться с обычным завершением игры.

---

## 2. Scope

Покрывается путь:

```text
Committed mode = GAME
        ↓
User requests WORK
        ↓
Game-session safety checks
        ↓
Game context deactivation
        ↓
Work context activation
        ↓
Work Mode available
```

---

## 3. Trigger

Базовый trigger:

```text
User explicitly selects "Switch to Work Mode"
```

Для текущей концепции это основной и preferred path.

---

## 4. Behavior goal

Game → Work должен:

- завершить или безопасно остановить active gaming session, если она ещё продолжается;
- закрыть либо деактивировать Game Mode environment;
- восстановить Work-oriented context;
- не оставлять пользователя в ложном hybrid state;
- вернуть пользователя в стабильную рабочую среду.

---

## 5. Preconditions

- committed operational mode = `GAME`;
- transition lifecycle = `IDLE`;
- пользователь запросил `WORK`.

---

## 6. Main scenario

### GW-01 — User switches from Game to Work

#### Main flow

1. SplitOS принимает `request WORK`.
2. Создаётся transition context.
3. Transition lifecycle переходит в `REQUESTED`.
4. SplitOS начинает `INSPECTING` current Game context.
5. Проверяется, есть ли активная managed game session.
6. Если игра всё ещё находится в `GAME_RUNNING` или стартующем состоянии, определяется policy разрешения:
   - предупредить пользователя;
   - предложить закрытие игры;
   - позволить отменить переход.
7. Если gaming blockers разрешены, transition продолжается.
8. Transition входит в `RESOLVING`.
9. SplitOS деактивирует или завершает Game-specific managed context:
   - Game Launcher foreground state;
   - gaming display/audio/input policies;
   - gaming helpers/overlays;
   - game-oriented mode-managed components.
10. Transition входит в `APPLYING`.
11. SplitOS применяет Work Mode policy:
   - Work display context;
   - Work audio context;
   - Work input context;
   - Work-oriented managed components;
   - allowed shared/work applications according to policy.
12. Выполняется `VERIFYING`.
13. Если verification successful, выполняется `COMMITTING`.
14. Committed mode меняется с `GAME` на `WORK`.
15. Transition завершается как `COMPLETED`.
16. Пользователь получает доступ к Work Mode environment.

#### Result

```text
Committed mode = WORK
Transition = IDLE
```

---

## 7. Relationship with game exit

### Important rule

Следующее неверно:

```text
Game exits → switch to Work
```

Правильная модель:

```text
Game exits → return to Game Launcher
```

И только отдельный пользовательский запрос создаёт `request WORK`.

---

## 8. Alternative flows

### GW-02 — Active game still running

1. Пользователь пытается перейти в Work Mode, пока игра ещё идёт.
2. Transition detects active game session.
3. Пользователю показывается понятный выбор:
   - close/leave game and continue;
   - cancel switch.
4. Если пользователь отменяет — committed mode остаётся `GAME`.
5. Если пользователь подтверждает закрытие игры — переход продолжается.

---

### GW-03 — Shared Apps active in Game Mode

1. Во время Game Mode у пользователя могут быть открыты Shared Apps.
2. При переходе в Work Mode их состояние обрабатывается согласно application lifecycle policy:
   - сохранить открытыми как обычные Windows apps;
   - закрыть;
   - свернуть;
   - вернуть на другой display/workspace.
3. Итоговое поведение должно быть deterministic и policy-driven.

---

### GW-04 — Verification failed

1. Work policy частично применена, но verification не подтверждает успешную активацию Work context.
2. Transition не считается completed.
3. Выполняется rollback/fallback.
4. Итоговый committed mode должен соответствовать фактически safe reachable state.

---

## 9. Game context deactivation

При выходе из Game Mode SplitOS должен убрать gaming-specific activation, которая больше не нужна в рабочем контексте.

Examples:

- Game Launcher больше не является основным foreground UX;
- gaming-specific device/display routing должно быть пересмотрено;
- game-oriented overlays и helpers не должны продолжать навязываться в Work Mode;
- gaming-only mode-managed components должны быть остановлены.

---

## 10. Work context restoration

Work Mode после transition не обязан быть «точно тем же пикселем, где пользователь был до игры», если такая функция не поддерживается.

Но он обязан быть:

```text
safe
predictable
usable
```

Минимальный semantic target:

- active Work policy;
- Work display context available;
- Work input usable;
- Work-oriented managed components restored where required;
- user may continue normal Work Mode usage.

---

## 11. Behavioral rules

### BR-GW-001

`GAME` остаётся committed mode до успешного semantic commit в `WORK`.

### BR-GW-002

Active running game должна считаться blocker or confirmation-required state, а не silently ignored fact.

### BR-GW-003

Обычное завершение игры не должно автоматически инициировать Game→Work.

### BR-GW-004

Успешный переход обязан завершиться доступностью usable Work context.

### BR-GW-005

Если Work activation не удалась, SplitOS должен прийти к ясному rollback/fallback result, а не к неопределённому hybrid state.

---

## 12. Open behavior questions

- Нужно ли помнить и восстанавливать pre-game window/workspace state?
- Какие Shared Apps должны survive into Work by default?
- Как обрабатывать streaming session, если пользователь выходит из Game Mode во время stream scenario?
- Нужен ли force-work administrative path?

---

## 13. Result

Game → Work Behavior завершает двунаправленную модель SplitOS:

```text
Request WORK
        ↓
Inspect Game context
        ↓
Resolve active game / blockers
        ↓
Deactivate Game context
        ↓
Apply Work policy
        ↓
Verify Work target
        ↓
Commit WORK
        ↓
Enter Work Mode
```

Этот сценарий закрепляет Work и Game как два полноценных операционных контекста, а не просто разные визуальные оболочки.
