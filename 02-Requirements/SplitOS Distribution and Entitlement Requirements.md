# SplitOS — Distribution and Entitlement Requirements

## 1. Purpose

Документ дополняет текущий Functional Requirements baseline требованиями, появившимися после уточнения distribution/build и monetization model.

Он не заменяет `SplitOS Functional Requirements.md`, а расширяет его новыми canonical requirement families.

Все требования имеют maturity status `DRAFT` и используются как часть текущего `ANALYSIS_BASELINE`.

---

# 2. Requirement notation extension

```text
FR-BUILD-XXX   Media Builder / Windows source / baseline preparation
FR-ENT-XXX     SplitOS Account / Entitlement
```

Existing setup requirements продолжают использовать:

```text
FR-SETUP-XXX
```

---

# 3. Media Builder / Windows Source

## FR-BUILD-001

SplitOS должен формировать поддерживаемый installation baseline из совместимого Windows 11 source и SplitOS-owned build inputs.

## FR-BUILD-002

Windows source должен рассматриваться как внешний Microsoft-owned build input и не должен считаться SplitOS-owned binary artifact.

## FR-BUILD-003

Поддерживаемый production build flow не должен зависеть от публичного распространения самим SplitOS готового modified Windows ISO без отдельно подтверждённого права на такую модель.

## FR-BUILD-004

SplitOS Media Builder должен проверять совместимость предоставленного/полученного Windows source до применения обязательных SplitOS transformations.

Проверка должна иметь возможность учитывать как минимум:

```text
edition
architecture
build/version
language where relevant
image identity/integrity evidence
```

## FR-BUILD-005

Если Windows source не соответствует поддерживаемому compatibility baseline, Builder не должен молча продолжать normal supported build.

## FR-BUILD-006

Способ получения Windows source должен использовать Microsoft-authorized source или другой юридически допустимый механизм.

Конкретный acquisition mechanism остаётся предметом отдельной licensing/technical validation.

---

# 4. Build Manifest

## FR-BUILD-010

SplitOS должен иметь versioned Build Manifest или эквивалентное canonical build definition, определяющее поддерживаемую composition конкретного release.

## FR-BUILD-011

Build definition должен иметь возможность определять:

- supported Windows base;
- component actions;
- SplitOS packages;
- baseline policies;
- provisioning rules;
- recovery/update assets;
- build version.

## FR-BUILD-012

Две установки одной версии SplitOS при одинаковом поддерживаемом source/configuration должны формироваться из одного и того же canonical build definition.

## FR-BUILD-013

Изменение Windows base version не должно автоматически означать, что старый Build Manifest остаётся совместимым.

---

# 5. Windows Component Classification

## FR-BUILD-020

Каждый Windows component, которым SplitOS целенаправленно управляет на distribution/runtime уровне, должен иметь одну primary classification:

```text
REMOVE
DISABLE
MODE_MANAGED
KEEP
```

До принятия решения допускается:

```text
TBD
```

## FR-BUILD-021

`REMOVE` должен использоваться только для компонентов, физическое removal/deprovision которых подтверждено dependency/compatibility validation.

## FR-BUILD-022

`DISABLE` должен использоваться для компонентов, которые сохраняются в baseline, но не должны быть активны в normal supported configuration.

## FR-BUILD-023

`MODE_MANAGED` должен использоваться для capabilities, runtime state которых зависит от `WORK | GAME`.

## FR-BUILD-024

`KEEP` должен использоваться для компонентов, сохраняющих обязательную platform/compatibility responsibility.

## FR-BUILD-025

Component classification должна быть versioned knowledge и может изменяться по результатам тестов.

## FR-BUILD-026

SplitOS не должен считать внешний community debloat list достаточным основанием для component removal без собственной validation.

---

# 6. Build-time vs Runtime

## FR-BUILD-030

Build-time baseline preparation и installed runtime orchestration должны быть разными responsibility phases.

## FR-BUILD-031

Обычный Work/Game transition не должен повторно выполнять полный distribution-level debloat/removal.

## FR-BUILD-032

Installed Runtime должен управлять live state, включая `MODE_MANAGED` lifecycle, но не должен становиться владельцем Windows source или build-time image composition.

## FR-BUILD-033

Обязательная build transformation, завершившаяся ошибкой, должна делать build result failed/unsupported, если невозможно подтвердить требуемый baseline.

---

# 7. Installed Baseline Integrity

## FR-BUILD-040

SplitOS должен иметь возможность определить, относится ли установленная конфигурация к известному SplitOS release/baseline.

## FR-BUILD-041

SplitOS должен иметь возможность обнаруживать значимый baseline drift, если изменение нарушает required SplitOS composition или runtime assumptions.

## FR-BUILD-042

Для повреждённых обязательных SplitOS packages или `KEEP` dependencies должен существовать repair/recovery path.

Точная реализация определяется на Recovery/Specification stages.

---

# 8. SplitOS Account

## FR-ENT-001

SplitOS должен иметь собственный product account/identity context, отличный от Windows account/license и внешних Game Platform accounts.

## FR-ENT-002

SplitOS Account должен иметь возможность связываться с:

- entitlement;
- пользовательскими SplitOS settings/profiles;
- update/support eligibility where applicable.

## FR-ENT-003

Наличие или отсутствие SplitOS entitlement не должно изменять authority Windows activation/license или game-platform license ownership.

---

# 9. Entitlement

## FR-ENT-010

SplitOS должен иметь canonical entitlement state для определения доступа к entitlement-dependent product capabilities.

## FR-ENT-011

Entitlement model должен поддерживать различие между:

```text
account identity
feature entitlement
update entitlement
support entitlement
```

если product policy использует эти типы отдельно.

## FR-ENT-012

Точный Free/Paid capability split должен определяться продуктовой policy и не должен выводиться из Windows licensing state.

## FR-ENT-013

При временной недоступности account backend SplitOS должен применять заранее определённую offline entitlement policy, а не случайно менять доступность функций.

## FR-ENT-014

Истечение или отсутствие paid entitlement не должно само по себе приводить к повреждению установленного Windows/SplitOS baseline.

---

# 10. Installation Disclosure

## FR-SETUP-008

До destructive operation над выбранным накопителем пользователь должен получить явное предупреждение о том, что операция может удалить существующие данные/систему.

## FR-SETUP-009

До destructive installation step пользователь должен иметь доступ к существенной информации о SplitOS Account и entitlement-dependent capabilities.

## FR-SETUP-010

Пользователь не должен впервые узнавать о существенном обязательном paid limitation только после форматирования/удаления предыдущей системы.

---

# 11. Security Baseline Constraint

## FR-BUILD-050

Если SplitOS удаляет или существенно изменяет Windows security components, SplitOS должен определить поддерживаемый minimum security baseline для итогового distribution.

## FR-BUILD-051

Planned removal Microsoft Defender Antivirus не считается полностью подтверждённой технической реализацией до dependency, servicing, security и game/application compatibility validation.

## FR-BUILD-052

Security-related component changes должны быть отражены в Windows Component Matrix и иметь validation evidence.

---

# 12. Traceability

Основные источники требований:

```text
DEC-028 → FR-BUILD-001..006
DEC-029 → FR-BUILD-010..013, 030..033
DEC-030 → FR-BUILD-020..026
DEC-031 → FR-BUILD-023
DEC-032 → FR-BUILD-030..032
DEC-033 → FR-ENT-001..014
DEC-034 → FR-SETUP-008..010
```

Open implementation/product questions остаются в:

```text
Requirements Open Questions.md
```
