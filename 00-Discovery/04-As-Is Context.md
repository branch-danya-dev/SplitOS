# SplitOS — As-Is Context

## 1. Current user environment

До SplitOS пользователь использует стандартную Windows 11 как единую среду для:

```text
Work
Gaming
Communication
Media
Streaming
Background services
```

Контексты разделяются в основном привычками пользователя, а не системой.

---

## 2. Current high-level flow

```text
User
  ↓
Windows sign-in
  ↓
Windows Desktop
  ↓
Applications / Game Clients / Games
  ↓
Windows / Drivers
  ↓
Hardware
```

После входа система не требует выбрать рабочий или игровой контекст.

---

## 3. Current work-to-game transition

Типичный пользователь выполняет часть действий вручную:

```text
close / minimize work apps
stop local tools if needed
select TV / gaming monitor
change resolution
change refresh rate
change audio output
connect controller
open Steam / another launcher
find game
change game settings
launch
```

Часть процессов при этом продолжает работать.

---

## 4. Current process coexistence

Типичная Windows допускает одновременное существование:

```text
IDE
browser
Docker / local servers
messengers
game launchers
game updaters
RGB utilities
streaming tools
cloud clients
games
```

Система не знает, какие из них относятся к текущей цели пользователя.

---

## 5. Current Game Mode limitations

Windows Game Mode не является эквивалентом SplitOS Game Mode.

В текущем контексте он не решает полностью:

- lifecycle рабочих приложений;
- выбор игрового пользовательского пространства;
- объединение игровых клиентов;
- controller-first system UX;
- game-specific display/input profiles;
- возврат пользователя в единый gaming workspace;
- разделение фоновых Work/Game components;
- SplitOS-controlled update lifecycle.

---

## 6. Current multi-display problem

Пользователь может иметь:

```text
work monitor
gaming monitor
TV
additional display
```

Но Windows не знает продуктового намерения:

```text
"этот TV — целевой Game Display для этого профиля"
```

Переключение и настройка часто остаются ручными.

---

## 7. Current input problem

Windows desktop ориентирован на:

```text
Keyboard + Mouse
```

Игры могут хорошо поддерживать Controller, но путь:

```text
Windows Desktop
→ Game Client
→ Game
```

не гарантирует удобного controller-first опыта.

---

## 8. Current game configuration problem

Настройки игры обычно связаны с самой игрой, но не с пользовательским SplitOS-контекстом:

```text
same game
+
different display
+
different controller
+
different target FPS
```

не обязательно автоматически означает другой performance profile.

---

## 9. Current update problem for modified distributions

Если системная среда глубоко модифицирована, непроверенный Windows feature/system update потенциально может изменить:

- удалённые/отключённые компоненты;
- системные настройки;
- service behavior;
- APIs;
- drivers;
- optimization assumptions.

Поэтому для отдельного SplitOS distribution требуется собственный compatibility/update lifecycle.

---

## 10. Main As-Is gap

Текущая Windows является универсальной платформой.

SplitOS требуется потому, что целевой продукт хочет добавить над ней управляемое знание о пользовательском намерении:

```text
WORK
or
GAME
```

и использовать это намерение для системного управления.
