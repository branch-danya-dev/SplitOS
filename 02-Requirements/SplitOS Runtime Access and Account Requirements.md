# SplitOS — Runtime Access and Account Requirements

## 1. Purpose

Документ дополняет requirements baseline требованиями к SplitOS Account, first-run onboarding, FREE/PRO runtime access и account/subscription management.

Он фиксирует продуктовые решения DEC-035..043.

Все требования имеют maturity status `DRAFT` и используются как часть текущего `ANALYSIS_BASELINE`.

---

## 2. Requirement notation extension

```text
FR-ACCOUNT-XXX   SplitOS Account / Windows user association
FR-ACCESS-XXX    FREE / PRO runtime access
FR-FIRST-XXX     first-run onboarding
```

Existing families `FR-ENT-*`, `FR-MANAGER-*` и `FR-SETUP-*` продолжают действовать.

---

# 3. SplitOS Account

## FR-ACCOUNT-001

SplitOS должен иметь собственный product account, отличный от Windows account, Microsoft Account и external Game Platform accounts.

## FR-ACCOUNT-002

SplitOS Account не должен являться Windows login principal и не должен заменять Windows authentication.

## FR-ACCOUNT-003

Normal supported SplitOS onboarding должен требовать создания или входа в SplitOS Account после появления Windows user context.

## FR-ACCOUNT-004

SplitOS должен иметь возможность связать текущий Windows user context с SplitOS Account.

## FR-ACCOUNT-005

Windows user identity и SplitOS Account identity должны храниться/обрабатываться как разные system facts.

## FR-ACCOUNT-006

System model не должна запрещать разным Windows users на одном PC использовать разные SplitOS Accounts.

Точные cardinality/device-sharing rules определяются отдельно.

---

# 4. First Run Experience

## FR-FIRST-001

SplitOS Account sign-in/create-account не должен требоваться до создания Windows user через поддерживаемый Windows OOBE flow.

## FR-FIRST-002

После первого Windows sign-in SplitOS должен иметь возможность запустить SplitOS First Run Experience.

## FR-FIRST-003

First Run Experience должен позволять пользователю:

- войти в существующий SplitOS Account;
- создать SplitOS Account;
- получить plan/entitlement result;
- завершить базовую initialization текущего Windows user context.

## FR-FIRST-004

First Run Experience не должен делать Windows user недоступным из-за временной недоступности SplitOS backend без предусмотренного safe/degraded path.

## FR-FIRST-005

После разрешения account/entitlement First Run Experience должен направлять пользователя в experience, соответствующий entitlement.

---

# 5. FREE Runtime Access

## FR-ACCESS-001

SplitOS должен поддерживать FREE entitlement, не требующий paid subscription для базового использования установленного SplitOS PC.

## FR-ACCESS-002

При FREE entitlement пользователь должен иметь доступ к обычному Windows desktop UX на SplitOS-prepared baseline.

## FR-ACCESS-003

FREE entitlement не должен требовать выбора Work Mode или Game Mode для нормального использования Windows desktop.

## FR-ACCESS-004

При FREE entitlement canonical managed operational mode может оставаться `NONE`, пока full managed runtime не разрешён entitlement.

## FR-ACCESS-005

При FREE entitlement обычный запуск игр через Windows/external Game Client должен оставаться допустимым supported user behavior.

## FR-ACCESS-006

Отсутствие paid entitlement не должно само по себе блокировать:

- Windows sign-in;
- Windows desktop;
- обычные Windows applications;
- обычный external Game Client launch path;
- базовую SplitOS Manager account/subscription surface.

---

# 6. PRO / Managed Runtime Access

## FR-ACCESS-010

При наличии entitlement, разрешающего full SplitOS runtime, SplitOS должен иметь возможность активировать managed Work/Game experience без переустановки Windows.

## FR-ACCESS-011

Full managed runtime должен использовать mode selection и invariant:

```text
WORK xor GAME
```

## FR-ACCESS-012

При активном managed runtime supported game launch из Work Mode должен проходить через SplitOS Work→Game orchestration согласно существующим transition requirements.

## FR-ACCESS-013

Entitlement-dependent capabilities могут быть физически предустановлены в SplitOS baseline, оставаясь неактивными/недоступными до соответствующего entitlement.

## FR-ACCESS-014

Наличие binary/package capability в системе не должно само по себе означать, что пользователь имеет entitlement на её product behavior.

---

# 7. Upgrade and Entitlement Change

## FR-ACCESS-020

Переход FREE → PRO должен иметь возможность выполняться через entitlement refresh без clean reinstall SplitOS.

## FR-ACCESS-021

После успешного upgrade SplitOS должен позволять пользователю пройти необходимую managed-runtime configuration/setup перед нормальным использованием Work/Game modes.

## FR-ACCESS-022

Истечение, отмена или отсутствие paid entitlement не должно повреждать Windows/SplitOS baseline.

## FR-ACCESS-023

Downgrade PRO → FREE не должен автоматически уничтожать пользовательские Game Profiles/settings только потому, что premium access временно отсутствует.

Точная retention policy определяется отдельно.

---

# 8. Offline / Backend Failure

## FR-ACCESS-030

Недоступность SplitOS account/entitlement backend не должна блокировать базовый Windows desktop.

## FR-ACCESS-031

SplitOS должен иметь определённую offline/degraded policy для account и entitlement resolution.

## FR-ACCESS-032

Offline policy должна отдельно определять:

- identity cache validity;
- entitlement cache validity;
- premium capability behavior offline;
- recovery/sign-in path.

## FR-ACCESS-033

Неизвестное entitlement state не должно интерпретироваться как разрешение destructive или privileged premium operation без явной policy.

---

# 9. SplitOS Manager Account / Subscription Surface

## FR-MANAGER-007

SplitOS Manager должен иметь Account area.

## FR-MANAGER-008

Account area должна отображать как минимум:

- current SplitOS Account identity;
- current plan/entitlement status;
- account action availability;
- upgrade/manage-subscription entry point.

## FR-MANAGER-009

SplitOS Manager должен предоставлять upgrade path из FREE в paid plan.

## FR-MANAGER-010

SplitOS Manager не должен хранить или становиться canonical owner payment-card data.

## FR-MANAGER-011

Payment execution может выполняться через внешний payment provider/web checkout, а SplitOS должен потреблять результат как evidence для entitlement processing.

---

# 10. Payment / Entitlement Boundary

## FR-ENT-015

Payment transaction evidence и SplitOS Entitlement должны оставаться разными facts.

## FR-ENT-016

External payment provider должен оставаться authority для payment transaction result.

## FR-ENT-017

SplitOS должен оставаться canonical owner того, какое product entitlement следует из подтверждённого payment/account evidence.

---

# 11. Traceability

```text
DEC-035 → FR-ACCOUNT-001..006
DEC-036 → FR-ACCESS-001..006
DEC-037 → FR-ACCESS-010..012
DEC-038 → FR-ACCESS-013..014, 020..021
DEC-039 → FR-ACCOUNT-001..006
DEC-040 → FR-FIRST-004, FR-ACCESS-030..033
DEC-041 → FR-FIRST-001..005
DEC-042 → FR-MANAGER-007..011
DEC-043 → FR-MANAGER-010..011, FR-ENT-015..017
```
