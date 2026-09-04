# SplitOS — Game Launch Behavior

## 1. Purpose

Документ описывает поведение SplitOS при управляемом запуске игры.

Этот сценарий начинается тогда, когда пользователь уже находится в Game Mode либо когда direct launch из Work Mode был доведён до committed `GAME`.

---

## 2. Scope

Сценарий покрывает:

```text
Game selected or requested
        ↓
Profile resolution
        ↓
Preparation
        ↓
External client handoff
        ↓
Game start
        ↓
Game running
        ↓
Game exit detection
        ↓
Return to Game Launcher
```

---

## 3. Trigger types

1. пользователь выбирает игру в SplitOS Game Launcher;
2. direct game launch был перехвачен и доведён до Game Mode;
3. SplitOS выполняет supported relaunch/retry from Game Launcher.

---

## 4. Behavior goal

Game Launch Behavior должен:

- запустить именно нужную игру, а не только открыть внешний клиент;
- определить подходящий Game Profile;
- учесть текущий hardware/display/input context;
- применить нужные SplitOS preparations до старта игры;
- корректно отработать handoff во внешний Game Client;
- удержать canonical SplitOS game session state;
- вернуть пользователя в Game Launcher после выхода из игры.

---

## 5. Preconditions

- committed operational mode = `GAME`;
- Game Launcher или иная SplitOS entry point active;
- game entity известна SplitOS;
- игра находится в supported launch scenario.

---

## 6. Main scenario

### GL-01 — Launch game from Game Launcher

#### Trigger

```text
User selects a game and chooses Launch
```

#### Main flow

1. SplitOS получает `GameLaunchRequest`.
2. Game session lifecycle переходит из `LAUNCHER` в `PREPARING`.
3. SplitOS определяет игру, связанный внешний Game Client и допустимый launch path.
4. SplitOS определяет подходящий Game Profile.
5. Проверяется актуальность hardware/display/input context.
6. Если context изменился значимо, recommendations/profile resolution обновляются.
7. SplitOS применяет подготовительные действия:
   - target display;
   - target audio;
   - target input profile;
   - mode-managed gaming helpers;
   - shared app preparation if relevant;
   - supported game settings, если policy и integration допускают это.
8. После завершения preparation game session переходит в `CLIENT_HANDOFF`.
9. SplitOS инициирует запуск через соответствующий внешний Game Client.
10. После передачи управления session переходит в `GAME_STARTING`.
11. SplitOS ожидает достаточных evidence того, что игра действительно запущена.
12. После подтверждения session переходит в `GAME_RUNNING`.
13. Во время игровой сессии SplitOS поддерживает Game Mode context, не вмешиваясь в gameplay beyond declared boundaries.
14. Когда игра завершает работу, SplitOS определяет exit.
15. Session переходит в `GAME_EXIT_DETECTED`.
16. Выполняется controlled return в `RETURNING_TO_LAUNCHER`.
17. Пользователь снова попадает в `LAUNCHER`.

#### Result

```text
Game Mode remains active.
User returns to Game Launcher.
```

---

## 7. Profile resolution behavior

Перед запуском игры SplitOS должен решить не только “какую игру запускать”, но и “в каком SplitOS scenario её запускать”.

### Inputs

- selected game;
- selected/default game profile;
- active Game display;
- active input preference;
- current hardware snapshot;
- user overrides;
- compatibility knowledge.

### Expected result

Определяется effective launch context:

```text
Game
+ Game Profile
+ Display Context
+ Input Context
+ Optimization Context
```

---

## 8. External client handoff

SplitOS Game Launcher не владеет лицензиями и installation truth внешнего клиента.

Поэтому при запуске всегда существует controlled handoff.

### Rule

SplitOS должен сохранять собственное понимание lifecycle, но не подменять ownership внешнего Game Client там, где тот остаётся обязательным звеном запуска.

---

## 9. Alternative flows

### GL-02 — Required external client unavailable

1. Во время preparation или handoff выясняется, что required Game Client недоступен.
2. Запуск игры не должен маркироваться как успешный.
3. Пользователю возвращается понятный результат ошибки.
4. Session должна вернуться в `LAUNCHER` или в другой safe pre-launch state.

---

### GL-03 — Game profile missing or invalid

1. Для игры не найден допустимый effective profile.
2. SplitOS пытается:
   - использовать default compatible profile;
   - либо предложить controlled fallback.
3. Если safe launch context не может быть собран, запуск не продолжается.

---

### GL-04 — Game start not confirmed

1. Handoff произошёл, но evidence запущенной игры не получены.
2. Session не должна бесконечно считаться `GAME_STARTING` без policy.
3. SplitOS применяет timeout/error handling.
4. Итог — возврат в safe launcher state с понятным результатом.

---

### GL-05 — Direct launch from Work Mode

1. Пользователь инициирует запуск игры вне Game Launcher.
2. SplitOS перехватывает это как managed gaming scenario.
3. Сначала выполняется Work → Game Behavior.
4. После успешного commit `GAME` SplitOS продолжает текущий Game Launch Behavior как обычный managed launch.

---

## 10. Running-game behavior boundary

Во время `GAME_RUNNING` SplitOS не должен:

- менять anti-cheat behavior;
- модифицировать DRM;
- вмешиваться в network code;
- менять matchmaking/server logic;
- искусственно подменять input ради игрового преимущества.

SplitOS может продолжать:

- поддерживать mode context;
- обслуживать допустимые Shared Apps / overlay;
- сохранять diagnostic/observability state;
- реагировать на окончание игры.

---

## 11. Exit behavior

### Rule

Закрытие игры не равнозначно `Game → Work`.

После определения завершения игры SplitOS должен вернуть пользователя в Game Launcher / Game Mode context.

Это правило сохраняет логику gaming session как более широкую сущность, чем lifecycle одной игры.

---

## 12. Behavioral rules

### BR-GL-001

Game session считается запущенной только после достаточного подтверждения, что игра реально вошла в running state.

### BR-GL-002

Если игра не стартовала, canonical game session не должна оставаться в `GAME_RUNNING`.

### BR-GL-003

Успешный внешний client handoff не равен успешному запуску игры.

### BR-GL-004

Direct launch должен сводиться к тому же canonical managed flow, что и запуск из Game Launcher.

### BR-GL-005

После выхода из игры пользователь должен вернуться в Game Launcher, сохраняя committed mode = `GAME`.

---

## 13. Open behavior questions

- Как precisely подтверждать `GAME_RUNNING` для разных клиентов/игр?
- Нужен ли промежуточный post-exit cleanup before launcher return?
- Разрешаем ли retry launch из launcher error screen?
- Как обрабатывать launcher-client update race during handoff?
- Что является default profile selection rule, если у игры несколько профилей?

---

## 14. Result

Game Launch Behavior закрепляет SplitOS Game Launcher как orchestration point:

```text
Game selected
        ↓
Resolve profile and context
        ↓
Prepare environment
        ↓
Handoff to external client
        ↓
Confirm running game
        ↓
Maintain Game Mode
        ↓
Detect game exit
        ↓
Return to Game Launcher
```
