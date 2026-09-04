# SplitOS — Trust Model

## 1. Purpose

Этот документ определяет каноническую trust-модель SplitOS: какие субъекты, процессы, данные и внешние источники считаются доверенными для конкретных решений, где проходят trust boundaries и какие доказательства нужны до выполнения sensitive actions.

Trust layer не заменяет Ownership, Interfaces, Integrations или Failures. Он отвечает на другой вопрос:

```text
Кто утверждает факт
+
почему принимающая сторона вправе этому утверждению доверять
+
какой ущерб возможен, если утверждение подделано или устарело
+
какая проверка обязательна перед sensitive action
```

---

## 2. Core rule

SplitOS не использует глобальную модель:

```text
component = trusted
```

Вместо этого доверие scoped по claim/capability:

```text
Subject
→ presents evidence / request
→ verifier establishes identity + integrity + freshness + authorization
→ owner accepts or rejects claim
→ sensitive action only after authorization
```

Доверие к одному факту не переносится автоматически на другой.

Пример:

```text
Windows confirms process X is running
```

не означает:

```text
process X may command the Privileged Broker
```

И:

```text
Payment Provider confirms a transaction
```

не означает:

```text
Payment Provider owns SplitOS entitlement
```

---

## 3. Trust objectives

SplitOS должен защищать как минимум:

1. целостность Windows baseline;
2. bootability/base Windows usability;
3. privileged machine mutation boundary;
4. canonical SplitOS runtime state;
5. SplitOS Account session/tokens;
6. entitlement decisions;
7. update/build artifact integrity;
8. recovery transaction integrity;
9. user Game Profiles/configuration from unauthorized mutation;
10. diagnostic data from becoming an authority source accidentally.

Приоритет безопасности согласован с Failure Model:

```text
User data integrity
→ Windows bootability/base usability
→ known coherent state
→ correct SplitOS canonical state
→ managed runtime restoration
→ UX convenience
```

---

## 4. Threat model scope

### In scope

Trust model должен учитывать:

- другой обычный desktop process в той же Windows installation;
- другой Windows user/session;
- compromised non-admin user process;
- spoofed local IPC client;
- malformed IPC messages;
- stale/replayed product evidence;
- modified local configuration/cache files;
- tampered SplitOS package/update artifact;
- malicious or stale external Game Client metadata;
- forged browser/custom-URI callback;
- network attacker against backend traffic;
- compromised/malformed external dependency response;
- accidental privilege escalation through Broker command design;
- partial update/recovery state after reboot/crash.

### Out of security guarantee

v1 не должен обещать защиту от:

```text
hostile local Administrator
kernel compromise
malicious/compromised Windows kernel
malicious firmware/hypervisor
physical attacker with unrestricted offline disk access
compromised SplitOS release-signing root private key
```

Эти сценарии могут частично смягчаться Windows/BitLocker/Secure Boot/code-signing инфраструктурой, но не должны скрытно считаться решёнными SplitOS runtime.

Особенно важно:

> Если пользователь или malware уже обладает unrestricted local Administrator/kernel authority, SplitOS не может честно гарантировать premium entitlement secrecy или абсолютную целостность локального runtime только собственными user-space механизмами.

---

## 5. Trust zones

### TZ-01 — Windows Platform Authority

Содержит:

- Windows kernel/security subsystem;
- access tokens/SIDs/logon sessions;
- Service Control Manager;
- supported Windows API evidence;
- OS trust store / signature verification primitives.

SplitOS опирается на Windows как platform security authority при условии отсутствия platform compromise.

### TZ-02 — Interactive User Session

Содержит:

- SplitOS Manager;
- Game Launcher;
- SplitOS Runtime Host;
- Game Client adapters;
- user-session Windows integrations.

Эта зона **не считается privileged автоматически**.

Любой process внутри interactive session потенциально может быть скомпрометирован и не должен получать machine-level authority только потому, что находится рядом с пользователем.

### TZ-03 — Privileged Broker

Windows Service / Session 0, выполняющий строго ограниченные machine-level operations.

Это отдельная privileged trust zone.

```text
Interactive User Session
       |
       | authenticated + authorized IPC
       v
Privileged Broker
```

Broker должен считать входящие requests недоверенными до проверки.

### TZ-04 — Local Persistent SplitOS State

Содержит:

- canonical runtime persistence;
- account association;
- entitlement cache/offline assertion;
- Game Profiles;
- update/recovery transaction records;
- installed baseline identity;
- local diagnostics.

Не все данные внутри этой зоны одинаково чувствительны.

### TZ-05 — SplitOS Backend

Содержит server-side product authority:

- SplitOS Account identity;
- canonical server entitlement;
- subscription/product policy;
- release metadata distribution where applicable;
- payment evidence processing boundary.

### TZ-06 — Release / Build Trust Domain

Содержит:

- SplitOS release signing keys;
- Build Manifest publication;
- update/package signing;
- release metadata;
- trusted build pipeline.

Release-signing authority должна быть логически отделена от обычного runtime/backend request handling.

### TZ-07 — External Authorities

Содержит:

- Microsoft Windows source/update authority;
- Payment Provider;
- Steam/Epic/Xbox/Battle.net;
- hardware/drivers/devices.

External authority trusted only for facts it actually owns.

---

## 6. Trust boundary catalogue

| Boundary | From | To | Sensitive claim/action |
|---|---|---|---|
| `TB-LOCAL-IPC` | Runtime Host | Privileged Broker | privileged machine mutation |
| `TB-USER-SESSION` | arbitrary user process | Runtime Host/Manager | user/runtime commands |
| `TB-ACCOUNT` | Manager/Runtime | SplitOS Backend | identity/auth/token exchange |
| `TB-ENTITLEMENT` | Backend/cache | Runtime Access owner | FREE/PRO capabilities |
| `TB-PAYMENT` | Payment Provider | SplitOS Backend | payment transaction evidence |
| `TB-UPDATE` | release source | Update Orchestration | package/manifest authenticity |
| `TB-BUILD` | Windows source + SplitOS release artifacts | Media Builder | baseline inputs |
| `TB-GAMECLIENT` | external Game Client/local metadata | adapters | install/license/launch evidence |
| `TB-WINDOWS-EVIDENCE` | Windows/drivers | SplitOS context owners | actual platform/device state |
| `TB-CALLBACK` | browser/custom URI | Manager/Runtime | auth/payment flow continuation |
| `TB-PERSISTENCE` | local disk | runtime owners | restored canonical/transaction state |

---

## 7. Trust properties

Для sensitive trust decision нужно явно определить набор свойств.

### Identity

Кто отправитель?

### Authenticity

Доказано ли, что claim действительно создан ожидаемым issuer?

### Integrity

Не изменилось ли содержимое после создания?

### Authorization

Имеет ли подтверждённый subject право выполнить именно эту capability?

### Freshness

Не является ли evidence устаревшим или replayed?

### Context binding

Относится ли evidence именно к:

- этому Windows user;
- этой SplitOS installation;
- этому account;
- этому transaction;
- этому release;
- этому request?

### Verification

Можно ли независимо прочитать фактическое состояние после mutation?

---

## 8. Trust is not transitive

Запрещённые implicit assumptions:

```text
Manager trusted UI
→ therefore Manager may perform admin operation
```

```text
Runtime Host signed executable
→ therefore every IPC request is authorized
```

```text
Payment callback received
→ therefore entitlement = PRO
```

```text
Steam manifest says installed
→ therefore launch/license truth confirmed
```

```text
update file arrived over HTTPS
→ therefore package is an authentic SplitOS release
```

```text
local persisted state exists
→ therefore it is current and internally consistent
```

Каждый переход требует собственного trust decision.

---

## 9. User intent vs authorization

Пользовательский intent:

```text
Switch to Game
Launch Game
Apply Update
Upgrade Subscription
```

не является authority proof.

Например:

```text
User presses Switch to Game
→ request accepted by UI
→ runtime access must still permit managed mode
→ transition owner must validate preconditions
→ privileged actions separately authorized
```

UX event не должен обходить Product Identity, Mode Transition или Broker authorization.

---

## 10. Canonical state trust

Canonical SplitOS state должен обновляться только своим owner после достаточного evidence.

Пример:

```text
Broker operation succeeded
```

является technical evidence, но не owner of:

```text
OperationalMode = GAME
```

Правильная цепочка:

```text
Broker result
+
Windows/device actual-state evidence
→ Mode Transition verification
→ Mode owner commit
```

Trust layer сохраняет правило:

```text
technical success
!= semantic success
!= canonical commit
```

---

## 11. Sensitive operations

К sensitive относятся минимум:

- service lifecycle mutation;
- machine-level policy changes;
- protected configuration writes;
- update staging/apply/rollback;
- recovery mutations;
- installed baseline identity commit;
- creation/replacement of trusted product packages;
- entitlement issuance;
- account token issuance/refresh;
- release signing.

Sensitive operation должен иметь:

```text
identified caller
+ authorized capability
+ validated input
+ bounded operation shape
+ audit correlation
+ actual-state verification where applicable
```

---

## 12. Capability-oriented authorization

Предпочтительная модель:

```text
CanPerform(OperationClass, Context)
```

а не:

```text
isTrusted = true
```

Пример Broker capabilities:

```text
APPLY_MODE_SERVICE_POLICY
APPLY_MACHINE_POLICY
STAGE_UPDATE
APPLY_UPDATE
ROLLBACK_UPDATE
APPLY_RECOVERY_ACTION
```

Не должно существовать общей capability:

```text
EXECUTE_ARBITRARY_ADMIN_COMMAND
```

---

## 13. Trust decision result

Trust validation не должна возвращать только `true/false` без причины.

Conceptual result:

```text
TRUSTED_FOR_CAPABILITY
DENIED_IDENTITY
DENIED_AUTHORIZATION
INVALID_INTEGRITY
STALE
CONTEXT_MISMATCH
UNSUPPORTED_ISSUER
REAUTH_REQUIRED
INDETERMINATE
```

`INDETERMINATE` не должен автоматически превращаться в trusted.

Для premium/security-sensitive действий принцип:

```text
cannot prove
→ do not grant
```

при сохранении base Windows usability.

---

## 14. Offline trust

Offline mode не означает отключение trust checks.

Если online backend unavailable, SplitOS может использовать только заранее определённое offline evidence:

```text
server-issued entitlement assertion
+ integrity/authenticity validation
+ validity window
+ context binding
```

Точный формат, TTL, clock-rollback handling и device binding остаются отдельным specification/security decision.

Недопустимо:

```text
backend unavailable
→ trust last boolean forever
```

---

## 15. Trust and diagnostics

Diagnostics могут фиксировать:

- issuer;
- subject;
- operation class;
- validation result;
- rejection reason;
- correlation/transaction ID;
- release/manifest identifier;
- evidence freshness metadata.

Но diagnostics **не являются authority**.

Нельзя восстанавливать entitlement или mode только потому, что в логе написано `PRO` или `GAME`.

---

## 16. Privacy minimization

Trust verification не даёт права собирать лишние данные.

SplitOS должен хранить только данные, необходимые для:

- account association;
- capability enforcement;
- transaction recovery;
- security/audit diagnosis;
- product behavior.

Game Client tokens/passwords не должны копироваться в SplitOS, если интеграция может работать без этого.

Payment card data не входит в SplitOS trust/storage boundary.

---

## 17. Security invariants

### TR-INV-001

Обычный user-session process не получает privileged machine mutation только через доступ к UI/runtime process.

### TR-INV-002

Privileged Broker не выполняет arbitrary command execution API.

### TR-INV-003

FREE/PRO определяется Product Identity & Entitlement, а не UI flag/local editable setting.

### TR-INV-004

Browser/payment callback не может сам предоставить entitlement.

### TR-INV-005

Update/build artifact не считается trusted только из-за transport security.

### TR-INV-006

External Game Client metadata никогда не становится privileged command input без validation/normalization.

### TR-INV-007

Local persisted canonical state должен иметь ownership/schema/integrity rules и не приниматься вслепую после crash/reboot.

### TR-INV-008

Failure trust validation не может привести к ложному successful commit.

### TR-INV-009

Premium trust failure не должен делать Windows desktop неиспользуемым.

### TR-INV-010

Release-signing private keys не должны присутствовать в installed SplitOS runtime.

---

## 18. Relationship to Failures

Trust failure — отдельный failure class:

```text
invalid signature
wrong caller SID/session
expired entitlement assertion
callback state mismatch
package hash mismatch
unexpected issuer
malformed external metadata
```

Response:

```text
deny sensitive action
→ preserve/restore safe state
→ record evidence
→ request reauthentication/update/recovery if appropriate
```

Не допускается fallback в `trust anyway` ради UX.

---

## 19. Open trust questions

- точный SplitOS authentication protocol/backend identity provider;
- точный OAuth/native redirect mechanism;
- token lifetimes/refresh policy;
- offline entitlement assertion format and TTL;
- device/installation binding strategy;
- clock rollback resistance;
- exact local canonical-state integrity representation;
- exact release manifest signature format;
- release key hierarchy/rotation/revocation;
- update signing key separation from application code-signing key;
- whether Privileged Broker requires caller binary provenance verification in addition to OS token/ACL;
- service account/privilege minimization details;
- telemetry/security event retention policy;
- handling of local Administrator threat explicitly in product documentation.

---

## 20. Result

Trust model вводит сквозную цепочку:

```text
Claim / Request
→ Identity
→ Integrity
→ Freshness
→ Context binding
→ Authorization
→ Semantic owner decision
→ Sensitive operation
→ Actual-state verification
```

Это позволяет следующему `Synthesis` собрать архитектуру, не оставляя неявных переходов вида “этому процессу просто доверяем”.