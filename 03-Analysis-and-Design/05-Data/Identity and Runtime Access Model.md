# SplitOS — Identity and Runtime Access Data Model

## 1. Purpose

Документ уточняет Data layer после разделения Windows identity, SplitOS Account, entitlement и managed runtime access.

Он дополняет `Domain Model.md` и является canonical refinement для identity/runtime-access concepts.

---

## 2. Core concepts

```text
WindowsUserContext
SplitOSAccount
WindowsUserAccountAssociation
Entitlement
ManagedRuntimeAccessDecision
SubscriptionPlanReference
PaymentEvidenceProjection
```

---

## 3. WindowsUserContext

### Meaning

SplitOS representation текущего Windows user/session context, необходимая для product association.

### Authority

Windows остаётся authority для Windows user identity/session.

SplitOS не должен создавать competing Windows identity truth.

### Data category

```text
external/runtime evidence + stable local reference where required
```

---

## 4. SplitOSAccount

Canonical product identity внутри SplitOS.

Owner:

```text
Product Identity & Entitlement
```

SplitOSAccount не является Windows user и не заменяет его.

---

## 5. WindowsUserAccountAssociation

### Meaning

Каноническое SplitOS знание о том, какой SplitOS Account связан с данным Windows user context на конкретной установке.

Conceptual relation:

```text
SplitOSInstallation
+ WindowsUserContext
→ WindowsUserAccountAssociation
→ SplitOSAccount
```

### Why it exists

Без отдельного association object возникает ошибочная модель:

```text
Windows user == SplitOS Account
```

Association позволяет независимо изменять product identity, не меняя Windows security identity.

### Cardinality

Точная cardinality/account-switching policy остаётся OPEN.

Модель не должна запрещать нескольким Windows users иметь разные SplitOS Accounts.

---

## 6. Entitlement

Existing `Entitlement` concept сохраняется.

Conceptual effective states relevant to runtime access:

```text
FREE
PRO
UNKNOWN
OFFLINE_VALID
EXPIRED_OR_UNAVAILABLE
```

Commercial product catalog может использовать другие plan names; Data model должна сохранять semantic capability access отдельно от marketing label.

---

## 7. SubscriptionPlanReference

### Meaning

Reference к commercial/product plan definition, например FREE/PRO или будущим пакетам.

Это не замена Entitlement.

```text
Plan definition
→ describes commercial bundle

Entitlement
→ states what this account may currently use
```

---

## 8. ManagedRuntimeAccessDecision

### Meaning

Каноническое derived product decision о том, разрешён ли полноценный managed Work/Game runtime для текущего account/context.

Conceptual values:

```text
DISABLED
ENABLED
DEGRADED
```

Inputs may include:

```text
SplitOSAccount
Entitlement
Offline policy
Compatibility/runtime safety constraints
```

Important:

```text
Entitlement
≠
ManagedRuntimeAccessDecision
```

Например entitlement может быть временно cached/offline и приводить к `DEGRADED`, а не к обычному `ENABLED`.

---

## 9. PaymentEvidenceProjection

### Meaning

SplitOS representation результата, полученного от внешнего payment provider/backend integration.

Authority:

```text
external payment provider for payment transaction result
```

Data category:

```text
external evidence/projection
```

Critical rule:

```text
PaymentEvidenceProjection
≠
Entitlement
```

Product Identity & Entitlement reconciles evidence into canonical product entitlement.

---

## 10. Account and installation relation

Conceptual model:

```text
SplitOSInstallation
├── WindowsUserContext A
│     └── association → SplitOSAccount A
└── WindowsUserContext B
      └── association → SplitOSAccount B
```

A SplitOS Account may also be associated with more than one installation if future device policy permits it.

Exact device limits are not fixed here.

---

## 11. Runtime relation

```text
WindowsUserAccountAssociation
        ↓
SplitOSAccount
        ↓
Entitlement
        ↓
ManagedRuntimeAccessDecision
        ↓
┌──────────────────┬──────────────────────┐
│ DISABLED         │ ENABLED              │
│                  │                      │
│ OperationalMode │ OperationalMode      │
│ = NONE          │ = NONE→WORK|GAME     │
└──────────────────┴──────────────────────┘
```

---

## 12. Data persistence implications

### REQUIRED_CANONICAL

Likely includes:

```text
WindowsUserAccountAssociation
SplitOSAccount reference/context
ManagedRuntimeAccessDecision where needed for safe recovery
```

Exact local/cloud split remains undecided.

### PROJECTION_CACHE

May include:

```text
PaymentEvidenceProjection
cached remote account metadata
```

### TIME-BOUND / OFFLINE EVIDENCE

May include:

```text
cached entitlement proof
last successful account resolution
```

Freshness/expiry rules must be explicit in Trust/Interfaces.

---

## 13. Profile retention vs access

Game Profiles and user settings are user/product data.

Access change:

```text
PRO → FREE
```

must not be represented as:

```text
delete GameProfile
```

Instead:

```text
profile remains stored
premium runtime capability becomes unavailable
```

unless a separately defined retention policy requires something else.

---

## 14. Manager relation

SplitOS Manager consumes:

```text
SplitOSAccount
Entitlement
ManagedRuntimeAccessDecision
SubscriptionPlanReference
```

It may initiate commands such as sign-in, sign-out, refresh entitlement or upgrade, but UI state is not canonical identity/entitlement truth.

---

## 15. Open data questions

- Где физически хранится `WindowsUserAccountAssociation`?
- Какой stable Windows user identifier безопасно использовать как reference?
- Может ли один Windows user переключать несколько SplitOS Accounts?
- Может ли один SplitOS Account одновременно использовать несколько devices?
- Как хранится offline entitlement proof и срок его валидности?
- Какие account/profile данные cloud-synced, а какие local-only?
- Как долго хранится payment evidence projection после entitlement reconciliation?

---

## 16. Result

Identity chain now becomes explicit:

```text
Windows identity
→ local user context
→ SplitOS account association
→ SplitOS Account
→ Entitlement
→ Managed Runtime Access
→ optional WORK xor GAME
```

Это предотвращает смешение OS authentication, product identity, billing и runtime state.
