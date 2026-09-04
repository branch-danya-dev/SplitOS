# SplitOS — First Run and Subscription Flow

## 1. Purpose

Документ связывает Windows first sign-in, SplitOS account onboarding, entitlement resolution и FREE/PRO activation в один end-to-end flow.

Canonical principle:

```text
Windows identity
!= SplitOS identity
!= entitlement
```

---

# 2. Participants

```text
User
Windows
SplitOS First Run Experience
SplitOS Runtime Host
Product Identity & Entitlement
SplitOS Account Backend
SplitOS Manager
Payment Provider
Mode State / Mode Selection
```

---

# 3. FL-01A — First supported Windows sign-in

## Trigger

```text
First Windows sign-in after SplitOS installation
```

## Preconditions

- Windows OOBE completed;
- Windows user exists;
- SplitOS runtime baseline installed;
- current Windows session is usable.

## Main path

1. Windows completes user sign-in.
2. Runtime Host resolves current Windows user/session evidence.
3. Product Identity & Entitlement checks whether a completed Windows-user ↔ SplitOS-account association exists.
4. No association is found.
5. Runtime Host opens SplitOS First Run Experience.
6. User chooses `Sign in` or `Create account`.
7. First Run Experience initiates account authentication with SplitOS Account Backend.
8. Backend returns authenticated SplitOS account identity.
9. Product Identity & Entitlement creates/updates the local association:

```text
WindowsUserContext
↔ SplitOSAccount
```

10. Runtime requests entitlement evidence from the backend.
11. Product Identity & Entitlement evaluates effective local runtime access.
12. Flow branches to FREE or PRO.

---

# 4. FREE branch

## Condition

```text
effective entitlement does not grant managed runtime
```

## Sequence

1. Product Identity & Entitlement commits:

```text
ManagedRuntimeAccess = DISABLED
```

2. Operational Mode remains:

```text
NONE
```

3. First Run Experience marks base onboarding complete.
4. Windows Desktop remains/returns as primary UX.
5. SplitOS Manager is available for account/subscription management.
6. No mandatory mode selection occurs.

## Result

```text
Windows session = usable
SplitOS account = associated
Managed runtime = DISABLED
Operational mode = NONE
```

---

# 5. PRO branch

## Condition

```text
effective entitlement grants managed runtime
```

## Sequence

1. Product Identity & Entitlement commits:

```text
ManagedRuntimeAccess = ENABLED
```

2. Runtime checks whether initial managed-runtime configuration is ready.
3. If required configuration is incomplete, setup UI gathers the minimum required preferences.
4. Runtime validates configuration readiness.
5. Mode selection becomes available.
6. User chooses `WORK` or `GAME`.
7. Runtime uses normal mode activation semantics.
8. After successful activation, committed operational mode becomes exactly one of:

```text
WORK
GAME
```

## Result

```text
Managed runtime = ENABLED
Operational mode = WORK xor GAME
```

---

# 6. Authentication/backend unavailable during first run

## Situation

Windows sign-in succeeded, but SplitOS backend is unavailable.

## Flow

1. Runtime does not invalidate Windows session.
2. Product Identity & Entitlement checks for existing valid cached association/evidence.
3. Because this is first run, usable cached identity may not exist.
4. SplitOS First Run Experience shows account onboarding unavailable/degraded.
5. Windows Desktop remains usable.
6. Normal account association can be retried later.

## Result

```text
Windows usable
SplitOS onboarding incomplete
No false PRO access granted
```

---

# 7. FL-01B — Normal subsequent sign-in

## Sequence

```text
Windows sign-in
→ resolve current Windows user
→ resolve local SplitOS account association
→ refresh/use valid entitlement evidence
→ evaluate ManagedRuntimeAccess
```

Then:

```text
FREE
→ Windows Desktop

PRO
→ managed startup policy / mode selection
```

Exact remember-last-mode policy remains OPEN.

---

# 8. FL-01C — FREE → PRO upgrade

## Trigger

User selects upgrade/manage subscription in SplitOS Manager.

## Main path

1. Manager requests an upgrade flow from Product Identity & Entitlement.
2. Runtime/backend creates or obtains a hosted checkout session.
3. Manager opens the supported checkout experience.
4. User completes payment with external Payment Provider.
5. Payment Provider sends validated payment/subscription evidence to SplitOS Backend.
6. Backend updates product account/subscription state.
7. Desktop callback, browser return or explicit refresh may notify Manager that checkout may have completed.
8. **The callback itself does not grant PRO.**
9. Product Identity & Entitlement refreshes entitlement from the backend.
10. Backend returns entitlement evidence that grants managed runtime.
11. Local canonical entitlement/runtime-access decision is updated.
12. Runtime transitions:

```text
ManagedRuntimeAccess
DISABLED → ENABLED
```

13. If managed-runtime setup is incomplete, setup is offered.
14. When ready, mode selection becomes available.

## Result

No Windows reinstall is required.

---

# 9. Upgrade failure alternatives

### Checkout cancelled

```text
FREE remains FREE
ManagedRuntimeAccess remains DISABLED
```

### Payment succeeded but entitlement refresh unavailable

1. Local runtime does not infer PRO from browser callback.
2. Manager shows pending/refresh-required status.
3. Refresh is retried according to later failure policy.
4. Windows remains usable.

### Payment evidence rejected by backend

No PRO entitlement is granted locally.

---

# 10. FL-01D — PRO downgrade / expiry

## Trigger

Entitlement refresh shows managed runtime no longer permitted.

## Stable target

```text
ManagedRuntimeAccess = DISABLED
OperationalMode = NONE
Windows Desktop usable
```

## Flow principle

If system is already in a stable Windows/base state, runtime access can be disabled directly.

If downgrade is observed during:

- active game;
- mode transition;
- update/recovery;

then immediate destructive interruption is forbidden. The event is handed to the applicable safety/failure policy and the system converges to base Windows experience at a safe boundary.

Exact timing belongs to `09-Failures`.

---

# 11. Invariants

### FL-ACCOUNT-001

Windows sign-in success is never rolled back because SplitOS account backend failed.

### FL-ACCOUNT-002

Browser/custom-URI callback is not entitlement authority.

### FL-ACCOUNT-003

FREE is a valid stable product state, not a degraded PRO error.

### FL-ACCOUNT-004

PRO activation changes runtime capability, not installed Windows baseline identity.

### FL-ACCOUNT-005

Account association is scoped to current Windows user context.

---

# 12. Canonical sequence summary

```text
Windows sign-in
→ Windows user evidence
→ SplitOS account association
→ entitlement evidence
→ local runtime access decision
→ FREE: Windows Desktop
   or
→ PRO: setup / mode selection / WORK xor GAME
```
