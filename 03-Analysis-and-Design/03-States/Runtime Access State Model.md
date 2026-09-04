# SplitOS — Runtime Access State Model

## 1. Purpose

Документ уточняет state semantics после разделения FREE baseline experience и PRO managed runtime.

Он является каноническим refinement к `System State Model.md` для account/entitlement/runtime-access dimension.

---

## 2. Why a separate state dimension is required

Следующие facts нельзя смешивать:

```text
SplitOS Account state
Entitlement state
Managed Runtime access
Operational Mode
```

Например валидно:

```text
Account = AVAILABLE
Entitlement = FREE
ManagedRuntime = DISABLED
OperationalMode = NONE
Windows desktop = usable
```

Это не ошибка и не recovery state.

---

## 3. Account Context State

Conceptual states:

```text
UNRESOLVED
RESOLVING
AVAILABLE
OFFLINE_DEGRADED
ACTION_REQUIRED
```

### UNRESOLVED

SplitOS product identity ещё не разрешена для текущего Windows user context.

### RESOLVING

Выполняется sign-in/create-account/cache resolution.

### AVAILABLE

SplitOS Account context доступен и может использоваться для entitlement resolution.

### OFFLINE_DEGRADED

Backend недоступен, но существует допустимый cached/offline context согласно policy.

### ACTION_REQUIRED

Для нормального supported product flow требуется пользовательское действие, например sign-in/re-authentication.

Важно: `ACTION_REQUIRED` не является Windows login failure.

---

## 4. Entitlement State

Conceptual product-level state:

```text
UNKNOWN
FREE
PRO
OFFLINE_VALID
EXPIRED_OR_UNAVAILABLE
```

Точные commercial plan names могут измениться; state model фиксирует смысл, а не pricing nomenclature.

`OFFLINE_VALID` означает, что policy временно принимает cached entitlement evidence.

---

## 5. Managed Runtime Access State

Canonical decision:

```text
DISABLED
ENABLED
DEGRADED
```

Owner:

```text
Product Identity & Entitlement
```

в части access decision, при этом runtime consumers исполняют разрешённые capabilities.

### DISABLED

Full Work/Game managed runtime не разрешён.

Normal FREE combination:

```text
Entitlement = FREE
ManagedRuntime = DISABLED
OperationalMode = NONE
```

### ENABLED

Entitlement/policy разрешает полноценный managed runtime.

Только при этом состоянии mode lifecycle может требовать:

```text
WORK xor GAME
```

### DEGRADED

Некоторые ранее разрешённые capabilities могут временно продолжать/ограничиваться по offline/recovery policy, но нормальный entitlement verification недоступен.

Exact premium offline semantics остаются OPEN.

---

## 6. Refinement of Operational Mode

`OperationalModeState` сохраняет значения:

```text
NONE
WORK
GAME
```

Но значение `NONE` теперь имеет два легитимных значения:

### Case A — managed runtime not yet committed

```text
ManagedRuntime = ENABLED
OperationalMode = NONE
```

например до initial mode selection.

### Case B — FREE/base experience

```text
ManagedRuntime = DISABLED
OperationalMode = NONE
```

Это устойчивое нормальное состояние бесплатного пользователя.

Следовательно прежнее правило «`NONE` допустим только до mode commit или recovery» уточняется этим документом.

---

## 7. Main state combinations

### FREE

```text
Account = AVAILABLE
Entitlement = FREE
ManagedRuntime = DISABLED
OperationalMode = NONE
Session = OPERATIONAL
```

User surface:

```text
Windows Desktop
```

### PRO before mode selection

```text
Account = AVAILABLE
Entitlement = PRO
ManagedRuntime = ENABLED
OperationalMode = NONE
Session = MODE_SELECTION
```

### PRO Work

```text
ManagedRuntime = ENABLED
OperationalMode = WORK
```

### PRO Game

```text
ManagedRuntime = ENABLED
OperationalMode = GAME
```

---

## 8. Upgrade transition

Conceptual sequence:

```text
Entitlement FREE
→ payment/account evidence reconciled
→ Entitlement PRO
→ ManagedRuntime ENABLED
→ managed runtime setup if required
→ Mode Selection
→ WORK | GAME
```

Upgrade does not imply immediate automatic commit to `GAME` or `WORK` without the required setup/intent flow.

---

## 9. Downgrade / expiry

Conceptually:

```text
PRO
→ entitlement no longer permits managed runtime
→ safe managed-runtime deactivation policy
→ ManagedRuntime DISABLED
→ OperationalMode NONE
→ Windows Desktop remains usable
```

If a game/transition/update is active, exact teardown timing belongs Behavior/Failure analysis.

Data/profile retention is separate from active capability access.

---

## 10. Backend unavailable

Invalid model:

```text
backend unavailable
→ Windows session blocked
```

Allowed conceptual outcomes:

```text
cached entitlement accepted
→ OFFLINE_VALID / DEGRADED

or

premium access cannot be proven
→ safe limitation
→ Windows Desktop remains usable
```

---

## 11. Invariants

### RA-INV-001

`WORK` or `GAME` requires managed runtime access that permits mode operation.

### RA-INV-002

`ManagedRuntime = DISABLED` requires `OperationalMode = NONE`.

### RA-INV-003

`OperationalMode = NONE` is a valid normal FREE state.

### RA-INV-004

Entitlement state does not replace account identity state.

### RA-INV-005

Payment evidence does not directly set operational mode.

### RA-INV-006

Failure to resolve SplitOS backend must not redefine Windows user authentication state.

---

## 12. Result

The runtime model is now:

```text
Windows Session
+
SplitOS Account Context
+
Entitlement State
+
Managed Runtime Access
+
Operational Mode
+
Transition/Game/Recovery lifecycles
```

`WORK xor GAME` remains a strong invariant, but only inside enabled managed SplitOS runtime.
