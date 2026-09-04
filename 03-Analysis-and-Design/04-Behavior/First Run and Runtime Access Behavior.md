# SplitOS — First Run and Runtime Access Behavior

## 1. Purpose

Документ описывает поведение SplitOS при первом пользовательском запуске, account association, entitlement resolution, FREE/PRO branching и последующем upgrade/downgrade.

Он уточняет `Startup Behavior.md`: mode selection больше не является безусловным результатом каждого Windows sign-in.

---

## 2. First-run entry point

### Trigger

```text
First supported Windows sign-in after SplitOS installation
```

### Preconditions

- Windows OOBE завершён;
- Windows user создан;
- Windows user session доступна;
- SplitOS runtime baseline установлен.

---

## 3. Primary first-run flow

1. Windows выполняет normal sign-in.
2. SplitOS обнаруживает, что для текущего Windows user context нет завершённого product onboarding.
3. Запускается SplitOS First Run Experience.
4. Пользователю предлагается:
   - sign in to SplitOS Account;
   - create SplitOS Account.
5. После успешной identity resolution SplitOS связывает текущий Windows user context с SplitOS Account.
6. SplitOS разрешает entitlement.
7. Flow ветвится по effective access.

---

## 4. FREE branch

### Condition

```text
Entitlement = FREE
ManagedRuntime = DISABLED
```

### Behavior

1. SplitOS завершает базовый onboarding.
2. Mode selection не показывается как обязательный gate.
3. Пользователь получает Windows Desktop.
4. SplitOS Manager остаётся доступен для account/status/upgrade/settings, разрешённых FREE policy.
5. Обычные Windows applications и external Game Clients работают как normal desktop applications.
6. Game launch может происходить обычным client/Windows path без Work→Game orchestration.

### Result

```text
Windows session usable
OperationalMode = NONE
ManagedRuntime = DISABLED
```

---

## 5. PRO branch

### Condition

```text
Entitlement permits managed runtime
```

### Behavior

1. SplitOS определяет, завершён ли initial managed-runtime setup.
2. Если нет — предлагает настроить необходимые Work/Game preferences.
3. После достаточной configuration readiness пользователь получает mode selection.
4. Пользователь выбирает `WORK` или `GAME`.
5. Existing controlled activation/state rules применяются без изменений.

### Result

```text
ManagedRuntime = ENABLED
OperationalMode = WORK xor GAME
```

---

## 6. Normal subsequent startup

После onboarding:

```text
Windows sign-in
→ resolve current SplitOS Account association
→ resolve effective entitlement/access
```

Then:

```text
FREE
→ Windows Desktop

PRO
→ managed runtime startup policy / mode selection
```

Точная remember-last-mode policy остаётся OPEN.

---

## 7. Upgrade FREE → PRO

### Trigger

Пользователь инициирует upgrade через SplitOS Manager или связанный account surface.

### Main flow

1. SplitOS создаёт upgrade/account flow.
2. Payment execution передаётся внешнему payment provider.
3. Payment provider возвращает payment evidence через поддерживаемый backend flow.
4. Product Identity & Entitlement обновляет canonical entitlement.
5. Installed SplitOS refreshes effective runtime access.
6. Если entitlement разрешает Pro Runtime:
   - `ManagedRuntime` становится `ENABLED`;
   - пользователю предлагается initial managed-runtime setup, если он ещё не выполнен;
   - затем mode selection становится доступен.

### Rule

Upgrade не требует clean reinstall, если required Pro components уже входят в installed baseline.

---

## 8. Pro game launch behavior

При активном Pro managed runtime existing canonical behavior сохраняется:

```text
WORK
→ user launches supported GAME
→ Work→Game transition
→ Game Mode commit
→ managed Game Launch
```

При FREE experience:

```text
Windows Desktop
→ user launches game/client normally
→ no SplitOS mode transition required
```

Это два разных supported behaviors одного установленного baseline.

---

## 9. Subscription downgrade / expiry

### Trigger

Effective entitlement больше не разрешает Pro managed runtime.

### Behavior principle

SplitOS должен безопасно прийти к base Windows experience.

Недопустимо:

- повредить Windows baseline;
- удалить user profiles только из-за entitlement change;
- заблокировать Windows desktop.

Exact timing rules для downgrade во время active game/transition определяются в Failures/Flows.

Normal stable result:

```text
ManagedRuntime = DISABLED
OperationalMode = NONE
Windows Desktop usable
```

---

## 10. Backend unavailable

### Situation

SplitOS account/entitlement backend недоступен.

### Behavior

1. SplitOS не блокирует Windows sign-in.
2. Проверяется допустимый cached/offline context.
3. Если offline policy подтверждает временный access — соответствующие capabilities могут продолжить работу согласно policy.
4. Если premium entitlement невозможно подтвердить и policy не разрешает continuation — premium capabilities ограничиваются безопасно.
5. Windows Desktop остаётся доступен.

---

## 11. SplitOS Manager behavior

Manager должен быть доступной точкой управления product identity и entitlement.

At minimum:

```text
Account identity
Plan / entitlement status
Upgrade / manage subscription
Sign-in / re-authentication actions
Mode/profile controls when entitled
Updates / recovery information
```

Manager отображает entitlement, но не является его owner.

---

## 12. Behavioral invariants

### BR-RA-001

Windows authentication завершается независимо от SplitOS product authentication.

### BR-RA-002

FREE user не обязан проходить mode selection.

### BR-RA-003

FREE experience не должен silently запускать Work→Game orchestration при обычном game launch.

### BR-RA-004

PRO upgrade не требует reinstall при наличии необходимых installed capabilities.

### BR-RA-005

Entitlement failure не должен превращать PC в unusable state.

### BR-RA-006

Payment success не равен entitlement success до reconciliation Product Identity & Entitlement.

---

## 13. Result

Canonical user-entry flow:

```text
Windows OOBE
→ Windows User
→ Windows sign-in
→ SplitOS Account
→ Entitlement
   ├── FREE → Windows Desktop
   └── PRO  → Managed Runtime → WORK xor GAME
```
