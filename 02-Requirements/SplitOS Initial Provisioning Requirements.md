# SplitOS — Initial Provisioning Requirements

## 1. Purpose

Документ определяет requirements-level модель первоначальной подготовки SplitOS после clean installation и до обычного пользовательского First Run onboarding.

Он дополняет:

```text
SplitOS Functional Requirements.md
SplitOS Distribution and Entitlement Requirements.md
SplitOS Runtime Access and Account Requirements.md
```

и фиксирует продуктовую границу:

```text
Windows deployment complete
!=
SplitOS ready for first use
```

Все требования являются частью текущего `ANALYSIS_BASELINE`.

---

## 2. Requirement notation

```text
FR-PROV-XXX   Initial Provisioning / package preparation / readiness
```

---

# 3. Provisioning lifecycle

## FR-PROV-001

После clean installation, завершения Windows OOBE и создания Windows user SplitOS должен выполнить управляемый Initial Provisioning до начала обычного SplitOS First Run onboarding.

Канонический порядок:

```text
Windows Setup
↓
Windows OOBE / user creation
↓
first Windows sign-in
↓
SplitOS Initial Provisioning
↓
READY_FOR_FIRST_RUN or READY_WITH_DEFERRED_OPTIONALS
↓
SplitOS First Run / Account / personalization
```

## FR-PROV-002

Initial Provisioning должен иметь как минимум следующие логические стадии:

```text
REQUIRED_PLATFORM
FIRST_PARTY_BUNDLED
THIRD_PARTY_PROVISIONED
FINALIZATION
```

Точное физическое количество installer processes не определяется requirement-слоем.

## FR-PROV-003

SplitOS должен иметь durable provisioning state, достаточный для безопасного resume/reconciliation после process crash или reboot.

Повторное выполнение не должно слепо дублировать уже verified package installation.

---

# 4. Package classes

## FR-PROV-010

Provisioning catalog должен различать минимум три package classes:

```text
REQUIRED_PLATFORM
FIRST_PARTY_BUNDLED
THIRD_PARTY_PROVISIONED
```

## FR-PROV-011

`REQUIRED_PLATFORM` должен использоваться для SplitOS-owned components, без которых supported product runtime/readiness не может быть подтверждён.

Примеры могут включать Runtime Host, Broker, required persistence/bootstrap/recovery components.

## FR-PROV-012

`FIRST_PARTY_BUNDLED` должен использоваться для SplitOS-owned feature packages, являющихся частью конкретного release package set, включая будущие first-party capabilities such as audio/equalizer, hotkeys, widgets, pins, performance tools и другие release-owned features.

## FR-PROV-013

`THIRD_PARTY_PROVISIONED` должен использоваться для внешнего ПО, которое SplitOS считает частью recommended/conceptual user environment, но ownership/auth/licensing/update authority которого остаётся у внешнего vendor/platform.

```text
SplitOS provisions third-party software
!=
SplitOS owns third-party software
```

---

# 5. Offline first-party readiness

## FR-PROV-020

Installation media конкретного SplitOS release должно содержать все release-owned package artifacts, необходимые для установки `REQUIRED_PLATFORM` и всех `FIRST_PARTY_BUNDLED` packages, помеченных release policy как required for first-use readiness.

## FR-PROV-021

Первоначальная установка release-owned first-party package set не должна требовать доступа к SplitOS CDN/backend.

```text
network unavailable
!=
first-party provisioning impossible
```

## FR-PROV-022

First-party artifacts должны проходить release-defined integrity/provenance verification до установки или активации.

## FR-PROV-023

First-party package может быть staged на installation media и физически установлен во время Initial Provisioning; requirement не требует, чтобы каждый first-party binary был заранее installed inside offline `install.wim`.

---

# 6. Readiness and failure semantics

## FR-PROV-030

SplitOS не должен публиковать `READY_FOR_FIRST_RUN`, пока mandatory `REQUIRED_PLATFORM` и required `FIRST_PARTY_BUNDLED` postconditions не verified.

## FR-PROV-031

Failure обязательного platform/first-party package должен переводить provisioning в typed blocked/recovery state, а не маскироваться как успешная инициализация.

## FR-PROV-032

Provisioning failure не должен уничтожать Windows user data или превращать доступную Windows recovery/base usability в зависимость от внешнего backend.

## FR-PROV-033

Невозможность установить optional/recommended `THIRD_PARTY_PROVISIONED` item не должна сама по себе запрещать завершение core SplitOS initialization.

Допустимый стабильный outcome:

```text
READY_WITH_DEFERRED_OPTIONALS
```

## FR-PROV-034

Deferred third-party items должны сохранять typed reason, например:

```text
NETWORK_UNAVAILABLE
VENDOR_SERVICE_UNAVAILABLE
USER_DECLINED
INSTALL_FAILED_RETRYABLE
UNSUPPORTED_CURRENT_CONTEXT
```

а не один generic `FAILED`.

---

# 7. Third-party acquisition boundary

## FR-PROV-040

SplitOS должен использовать только release-approved third-party acquisition/distribution mechanisms, соответствующие vendor/licensing constraints.

## FR-PROV-041

Если SplitOS не имеет права bundle/redistribute third-party installer, provisioning должен получать его через approved official vendor/store mechanism, а не через неавторизованное SplitOS mirror.

## FR-PROV-042

Third-party acquisition metadata не должно превращаться в generic arbitrary execution surface.

Provisioning contract не должен принимать с backend/UI произвольные значения вида:

```text
url + commandLine + runAsAdmin
```

как достаточную authority для выполнения.

## FR-PROV-043

Перед запуском third-party installer/provisioning adapter SplitOS должен проверять release-defined identity/integrity/publisher constraints в пределах доступного supported mechanism.

## FR-PROV-044

External application account, license, update и cloud state остаются authority соответствующего external vendor/platform.

---

# 8. User experience

## FR-PROV-050

Initial Provisioning должен предоставлять пользователю понятный progress/status для основных стадий подготовки.

Пример семантики:

```text
Core Platform          VERIFIED
SplitOS Features       INSTALLING
Gaming Clients         WAITING_FOR_NETWORK
Finalization           PENDING
```

Точное визуальное оформление определяется UX specification/delivery.

## FR-PROV-051

Пользователь не должен быть вынужден вручную искать и устанавливать release-required SplitOS first-party components после supported installation.

## FR-PROV-052

Release policy может определять recommended third-party package set и в будущем custom/minimal selection, но third-party package не должен становиться скрытым prerequisite для базовой Windows/SplitOS recoverability.

## FR-PROV-053

После `READY_WITH_DEFERRED_OPTIONALS` SplitOS Manager должен иметь возможность показать незавершённые optional items и предложить retry/complete setup позднее.

---

# 9. First Run ordering

## FR-PROV-060

Обычный SplitOS Account / entitlement / personalization First Run должен начинаться только после достижения:

```text
READY_FOR_FIRST_RUN
or
READY_WITH_DEFERRED_OPTIONALS
```

## FR-PROV-061

SplitOS backend/account availability не должна быть prerequisite для установки bundled first-party release packages.

## FR-PROV-062

После provisioning readiness дальнейшая ветка остаётся канонической:

```text
SplitOS First Run
↓
Account association
↓
Entitlement
↓
FREE: Windows Desktop
or
PRO: setup / WORK xor GAME
```

---

# 10. Traceability

```text
DEC-048 → FR-PROV-020..023
DEC-049 → FR-PROV-001..003, 060..062
DEC-050 → FR-PROV-010..013, 030..034, 050..053
DEC-051 → FR-PROV-040..044, 053
```

Downstream ownership:

```text
03-Analysis-and-Design/08-Flows/Initial Provisioning Flow.md
04-Specification/SPEC-10-Builder-and-Component-Matrix/Initial Provisioning and Package Delivery.md
05-Grooming-and-Implementation-Planning/
```
