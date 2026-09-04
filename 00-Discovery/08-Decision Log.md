# SplitOS — Decision Log

## Purpose

Decision Log фиксирует продуктовые и системные решения, которые уже влияют на дальнейшие требования.

Это не ADR технической архитектуры. Архитектурные решения появятся позднее в Analysis & Design.

---

| ID | Decision | Rationale | Status |
|---|---|---|---|
| DEC-001 | SplitOS является отдельным управляемым Windows 11-based distribution, формируемым из известного Windows source и SplitOS-owned build inputs | Предсказуемое базовое состояние, debloat, testing, updates, recovery | ACCEPTED |
| DEC-002 | Work Mode и Game Mode строго взаимоисключающие | Иначе теряется isolation и optimization value | ACCEPTED |
| DEC-003 | После Windows sign-in пользователь проходит SplitOS user/key context и mode selection | Режим является частью session intent | ACCEPTED |
| DEC-004 | Game Mode остаётся активным после закрытия игры | Пользователь может сменить игру/отойти/использовать Game Launcher | ACCEPTED |
| DEC-005 | Возврат Game→Work выполняется явным действием пользователя | Не предполагать завершение gaming session по закрытию одной игры | ACCEPTED |
| DEC-006 | GAME и GAME_CLIENT являются разными сущностями | Клиент может использоваться без игрового контекста | ACCEPTED |
| DEC-007 | Запуск GAME из Work должен пройти через SplitOS orchestration | Иначе display/input/profile могут быть не подготовлены | ACCEPTED |
| DEC-008 | Собственный SplitOS Game Launcher — core component | Единый console-like UX поверх внешних клиентов | ACCEPTED |
| DEC-009 | v1 поддерживает официально установленные игры из поддерживаемых Game Clients | Сужение MVP и повышение предсказуемости | ACCEPTED |
| DEC-010 | Manual/unofficial games deferred | Не блокировать v1 сложной discovery model | ACCEPTED |
| DEC-011 | Одна игра может иметь несколько SplitOS profiles | Разные display/input scenarios требуют разных конфигураций | ACCEPTED |
| DEC-012 | Hardware context перечитывается при запуске Game Launcher | Профили должны соответствовать реальному железу | ACCEPTED |
| DEC-013 | Перед game launch профиль пересчитывается только при значимых изменениях | Не выполнять лишнюю работу | ACCEPTED |
| DEC-014 | Game optimization = max quality при стабильном target performance | Основная ценность — комфортный опыт под текущий display | ACCEPTED |
| DEC-015 | Пользователь может вручную менять настройки | SplitOS рекомендует/применяет, но не лишает пользователя контроля | ACCEPTED |
| DEC-016 | Work→Game использует pre-flight | Защита user data и running workloads | ACCEPTED |
| DEC-017 | Переход можно отменить при unresolved blockers | Safety > автоматизация | ACCEPTED |
| DEC-018 | Shared Apps имеют gaming-specific representation | Discord/browser/music должны интегрироваться в Game UX | ACCEPTED |
| DEC-019 | До 3 Shared Apps active одновременно | Ограничение UX/resource complexity | ACCEPTED |
| DEC-020 | Work Mode advanced UX — низкий приоритет v1 | Сначала ценность Game Mode | ACCEPTED |
| DEC-021 | Game Mode UI — высокий приоритет v1 | Это основная дифференциация продукта | ACCEPTED |
| DEC-022 | Стандартные Windows feature/system updates не применяются бесконтрольно | Защита совместимости модифицированного distribution | ACCEPTED |
| DEC-023 | Windows patches интегрируются через SplitOS release lifecycle | SplitOS владеет compatibility decision | ACCEPTED |
| DEC-024 | SplitOS не модифицирует anti-cheat/DRM/network/matchmaking | Жёсткая продуктовая граница | ACCEPTED |
| DEC-025 | Один физический накопитель должен поддерживаться | Не навязывать искусственное физическое разделение | ACCEPTED |
| DEC-026 | Все корректно обнаруживаемые подключённые дисплеи должны быть доступны SplitOS | Display selection — core capability | ACCEPTED |
| DEC-027 | Future ecosystem может включать OEM/partner hardware/software | Архитектура должна оставаться расширяемой | ACCEPTED |
| DEC-028 | SplitOS не проектируется как публично распространяемый modified Windows ISO; пользовательский Builder использует Microsoft-authorized Windows source как внешний build input | Разделить Windows redistribution и собственные SplitOS assets; сохранить clean-install distribution model | ACCEPTED |
| DEC-029 | SplitOS Media Builder формирует поддерживаемый baseline до установки, применяя SplitOS Build Manifest и packages к совместимому Windows source | Агрессивная интеграция должна происходить на build-time, а не быть набором runtime tweaks поверх произвольной Windows | ACCEPTED |
| DEC-030 | Windows components классифицируются как REMOVE / DISABLE / MODE_MANAGED / KEEP | Отделить физическое удаление, baseline-disablement, mode-dependent lifecycle и обязательные dependencies | ACCEPTED |
| DEC-031 | MODE_MANAGED components могут иметь разное состояние в Work и Game | Work/Game дают уникальную возможность уменьшать active runtime footprint без необратимого удаления полезных Work-функций | ACCEPTED |
| DEC-032 | После установки Build Pipeline больше не управляет обычным runtime; Installed SplitOS Runtime управляет только live state, mode transitions и MODE_MANAGED lifecycle | Не смешивать image servicing и runtime orchestration | ACCEPTED |
| DEC-033 | SplitOS distribution/build tooling предполагается бесплатным, а SplitOS Account/entitlement может предоставлять платные product capabilities, updates и support | Монетизация относится к SplitOS product/services, а не к продаже Windows binaries | ACCEPTED |
| DEC-034 | Существенная информация о paid entitlement должна быть показана до destructive installation step | Пользователь не должен узнать о существенных ограничениях после форматирования накопителя | ACCEPTED |

---

## Superseded clarification

Ранняя трактовка DEC-001 могла читаться как:

> SplitOS самостоятельно распространяет готовый modified Windows image.

Эта трактовка **SUPERSEDED** решениями DEC-028/029.

Текущая модель:

```text
Microsoft-authorized Windows source
+
SplitOS Media Builder
+
SplitOS Build Manifest / packages
↓
locally prepared SplitOS installation baseline
```

Отдельный distribution сохраняется как product/deployment model, но Windows source является внешним build input.

---

## Decision lifecycle

При изменении решения:

```text
ACCEPTED
  ↓
SUPERSEDED
  ↓
new DEC-XXX
```

История не удаляется.
