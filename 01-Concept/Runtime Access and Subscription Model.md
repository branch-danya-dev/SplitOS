# SplitOS — Runtime Access and Subscription Model

## 1. Purpose

Документ фиксирует текущее продуктовое понимание того, что получает пользователь после установки SplitOS baseline и как SplitOS Account / Entitlement влияют на runtime experience.

Это концептуальная модель продукта, а не реализация authentication/payment/backend.

---

## 2. Core product split

SplitOS состоит из двух разных смысловых слоёв:

```text
SplitOS Base
→ модернизированный Windows 11 baseline

SplitOS Pro Runtime
→ управляемый Work/Game experience
```

Установка SplitOS Base не требует платной подписки.

---

## 3. Identity model

В системе существуют независимые identity domains:

```text
Windows User / Windows Identity
≠
SplitOS Account
≠
SplitOS Entitlement
```

Windows User:

- создаётся стандартным Windows OOBE;
- используется Windows для OS sign-in/security context;
- может быть local Windows account или Microsoft Account согласно поддерживаемому Windows flow.

SplitOS Account:

- является product identity SplitOS;
- разрешается уже внутри созданной Windows user session;
- не является вторым Windows login principal;
- используется для entitlement, пользовательских SplitOS settings/profiles и product lifecycle.

Entitlement:

- является отдельным фактом о доступе к SplitOS capabilities;
- не равен самому аккаунту;
- не равен Windows license;
- не равен payment transaction.

---

## 4. First-run concept

Канонический first-run порядок:

```text
Prepared SplitOS baseline
        ↓
Windows OOBE
        ↓
Windows user created
        ↓
First Windows sign-in
        ↓
SplitOS First Run Experience
        ↓
Sign in / Create SplitOS Account
        ↓
associate SplitOS Account with current Windows user context
        ↓
resolve entitlement
```

SplitOS Account не должен требоваться до того, как существует Windows user context, к которому его можно осмысленно привязать.

---

## 5. FREE experience

Если account имеет FREE entitlement:

```text
Windows sign-in
→ SplitOS Account resolved
→ entitlement = FREE
→ normal Windows desktop
```

Пользователь получает обычный desktop/application UX Windows на подготовленном SplitOS baseline.

Build-time преимущества остаются, например:

- очищенный baseline;
- validated component removal/disablement;
- SplitOS packages;
- privacy/consumer-noise changes согласно release policy;
- известная supported composition.

Но полноценный managed mode runtime не активируется.

FREE experience не должен требовать:

```text
Work Mode selection
Game Mode selection
Game Launcher
managed Work→Game transition
Game Profiles
automatic game optimization
premium Shared App Game UX
```

Игра в FREE experience может запускаться обычным Windows/client способом:

```text
Windows Desktop
→ Steam/Epic/etc.
→ Game
```

---

## 6. PRO experience

Если account имеет entitlement, разрешающий full managed runtime:

```text
Windows sign-in
→ SplitOS Account resolved
→ entitlement permits Pro Runtime
→ mode selection
→ WORK xor GAME
```

В этом состоянии активируются product capabilities согласно policy, включая концептуально:

- Work/Game managed runtime;
- transactional mode transitions;
- SplitOS Game Launcher;
- managed game launch;
- Game Profiles;
- display/audio/input orchestration;
- game optimization;
- premium Shared App/Game Mode capabilities.

`WORK xor GAME` является invariant именно managed runtime.

---

## 7. Preinstalled but entitlement-gated capabilities

SplitOS Pro components могут быть частью установленного baseline заранее.

```text
installed capability
≠
entitled capability
≠
active capability
```

FREE user не получает новую Windows installation после upgrade.

После получения соответствующего entitlement SplitOS может активировать уже установленный managed runtime.

Это позволяет flow:

```text
FREE
→ purchase/upgrade
→ entitlement refresh
→ PRO capabilities available
```

без переустановки ОС.

---

## 8. Subscription downgrade / expiry

Истечение paid entitlement не должно повреждать установленную систему.

Conceptual behavior:

```text
PRO entitlement unavailable/expired
→ managed premium capability access disabled according to policy
→ base Windows experience remains usable
```

User-owned profiles/settings не должны уничтожаться только из-за downgrade, если отдельная retention policy не определяет иное.

---

## 9. Offline / backend unavailable

SplitOS backend availability не является Windows boot authority.

Недопустимо:

```text
SplitOS backend unavailable
→ Windows desktop inaccessible
```

Вместо этого используется заранее определённая offline/degraded entitlement policy.

Minimum safety invariant:

```text
Windows sign-in remains usable
SplitOS Base remains usable
```

Точная длительность cached/offline entitlement и premium offline behavior остаются requirement/open policy questions.

---

## 10. SplitOS Manager role

SplitOS Manager является основным desktop control center для установленного продукта.

Conceptual areas:

```text
Account
Subscription / Plan
Upgrade
Work Mode
Game Mode
Games / Profiles
Devices
Updates
Recovery
```

Account area должна позволять как минимум:

- увидеть текущий SplitOS Account;
- увидеть plan/entitlement state;
- sign in / sign out where allowed;
- инициировать upgrade/manage-subscription flow;
- получить понятный status premium capabilities.

---

## 11. Payment boundary

SplitOS не должен владеть payment-card truth.

Conceptual chain:

```text
SplitOS Manager / account surface
→ upgrade request
→ SplitOS account/backend flow
→ external payment provider
→ payment evidence
→ SplitOS entitlement decision
```

Payment provider owns transaction/payment evidence.

SplitOS owns resulting product entitlement semantics.

---

## 12. Multi-user implication

Поскольку SplitOS Account разрешается после Windows sign-in, модель не должна запрещать:

```text
Windows User A ↔ SplitOS Account A
Windows User B ↔ SplitOS Account B
```

на одном физическом PC.

Точные cardinality, device limits и subscription sharing policy определяются отдельно.

---

## 13. Canonical product model

```text
Microsoft-authorized Windows source
        +
SplitOS build inputs
        ↓
SplitOS Base installation
        ↓
Windows User
        ↓
SplitOS Account
        ↓
Entitlement
   ┌────┴────┐
   │         │
 FREE       PRO
   │         │
Windows     Mode Selection
Desktop     ↓
            WORK xor GAME
```

Это разделяет право пользоваться установленным PC и право использовать premium SplitOS runtime capabilities.
