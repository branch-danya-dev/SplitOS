# SplitOS — Stakeholders and Knowledge Sources

## 1. Purpose

Документ определяет участников, влияющих на требования SplitOS, и источники знания, необходимые для подтверждения решений.

---

## 2. Primary stakeholder

### End User

Роль:

```text
Primary product user
```

Интересы:

- простое переключение режимов;
- высокая игровая производительность;
- отсутствие потери данных при переходе;
- удобный controller-first Game Mode;
- предсказуемое обновление;
- совместимость с играми и периферией;
- возможность ручного переопределения автоматических настроек.

Authority:

- личные предпочтения;
- выбор режима;
- выбор дисплея;
- выбор input profile;
- ручное изменение игровых настроек;
- решение по блокирующим Work-процессам во время перехода.

---

## 3. Product owner / SplitOS owner

В текущем проекте продуктовые решения задаются инициатором SplitOS.

Authority:

- product vision;
- scope;
- поведение Work/Game;
- UX priority;
- допустимая глубина модификации Windows;
- distribution model;
- update policy;
- future ecosystem direction.

---

## 4. Microsoft / Windows

Роль:

```text
Base platform owner
```

Authority:

- Windows kernel;
- Windows system behavior;
- supported OS interfaces;
- Windows security model;
- базовые Windows patches;
- licensing terms Windows.

Не является authority для:

- того, какая Windows build считается совместимой с конкретной версией SplitOS.

Это решение принадлежит SplitOS distribution compatibility process.

---

## 5. Game Client owners

Примеры:

```text
Steam
Epic Games
Battle.net
Xbox
```

Authority:

- пользовательский аккаунт платформы;
- лицензии;
- магазин;
- установка игр;
- обновление игр;
- состояние клиента;
- собственные cloud-функции;
- собственные integration constraints.

SplitOS является authority для объединённого Game Launcher UX, но не заменяет platform ownership.

---

## 6. Game developers / publishers

Authority:

- игровые конфигурации;
- поддерживаемые параметры;
- игровой input;
- game binaries;
- anti-cheat;
- DRM;
- network behavior;
- matchmaking;
- server logic.

Источник знания для:

- поддерживаемых config-файлов;
- command-line parameters;
- safe game configuration;
- performance characteristics.

---

## 7. GPU / device vendors

Примеры:

```text
NVIDIA
AMD
Intel
display manufacturers
controller vendors
audio vendors
```

Authority:

- driver behavior;
- hardware capabilities;
- vendor-specific features;
- reported display/device capabilities.

---

## 8. Future OEM / partners

Потенциальные участники:

- OEM-производитель SplitOS controller;
- Discord / communication partners;
- streaming/OBS integrations;
- специализированные hardware vendors.

На текущем этапе не являются обязательными участниками MVP, но архитектура не должна блокировать будущие partner integrations.

---

## 9. Engineering knowledge sources

Для будущей реализации потребуются:

| Область | Источник знания |
|---|---|
| Windows mode transition capabilities | Microsoft documentation + prototype |
| Display management | Windows APIs + GPU vendor docs + prototype |
| Audio switching | Windows Audio APIs |
| Controller detection/navigation | Windows input APIs / XInput / HID |
| Game Client discovery | official client behavior/docs + experiments |
| Game configuration | official game docs / verified profiles |
| Update control | Windows servicing documentation + licensing review |
| Distribution customization | supported Windows deployment/customization tooling |
| Recovery | Windows recovery/deployment capabilities |
| Anti-cheat compatibility | game/anti-cheat vendor documentation + testing |

---

## 10. Stakeholder gaps

На текущем аналитическом этапе часть знаний подтверждена продуктовым владельцем, но технические assumptions требуют дальнейшего подтверждения разработкой/prototyping.

Особенно:

- допустимые способы перехвата запуска игры;
- безопасный suspend/stop процессов;
- автоматическое сохранение документов;
- полный control над Windows Update;
- launcher discovery;
- изменение game config;
- применение HDR/VRR/display mode;
- controller navigation вне game process.
