# SplitOS — Failure Model

## 1. Purpose

Документ определяет каноническую модель ошибок и отказов SplitOS.

Цель слоя `09-Failures` — ответить не только на вопрос:

```text
Что может пойти не так?
```

но и на более важные вопросы:

```text
Кто обнаруживает проблему?
Кто решает, что делать дальше?
Какой state остаётся canonical?
Можно ли retry?
Нужен ли rollback?
Когда требуется Recovery?
Что считается безопасным пользовательским результатом?
```

Failure layer не заменяет State, Flow, Integration или Trust models. Он определяет общую семантику деградации и восстановления поверх них.

---

## 2. Core rule

Ошибка сама по себе не имеет права переписывать canonical state.

```text
Failure evidence
→ owning responsibility
→ classify failure
→ choose response
→ execute response
→ verify resulting actual state
→ commit recovery/fallback result where required
```

Неверно:

```text
SetDisplayConfig failed
→ mode = WORK
```

или:

```text
Steam launch failed
→ game session = INACTIVE
```

Правильно:

```text
technical failure evidence
→ semantic owner evaluates consequences
→ resulting state is changed only through canonical state/transition rules
```

---

## 3. Failure is not equal to every non-success outcome

Не каждый отрицательный или прерванный результат является системным failure.

### Normal controlled outcomes

```text
USER_CANCELLED
AUTH_REQUIRED
BLOCKED_BY_USER_DATA_RISK
UNSUPPORTED_CAPABILITY
DEPENDENCY_NOT_CONFIGURED
NO_UPDATE_AVAILABLE
FREE_ENTITLEMENT
```

Такие outcomes могут быть ожидаемыми и корректно обработанными.

### Failure outcomes

Failure начинается там, где система:

- не смогла выполнить required operation;
- получила непредусмотренное или противоречивое evidence;
- потеряла required component/dependency;
- не смогла подтвердить target state;
- не может доказать coherent safe state;
- потеряла durable transaction/recovery semantics.

---

# 4. Failure taxonomy

## F-01 — Request / Precondition Failure

Запрос невозможно начать из-за текущего semantic state.

Examples:

```text
Work→Game requested while Recovery active
second major mutation requested while Update applying
managed launch requested while runtime access disabled
```

Typical response:

```text
REJECT / DEFER
```

Canonical state обычно не меняется.

---

## F-02 — Dependency Unavailable

Required dependency недоступна.

Examples:

```text
Privileged Broker unavailable
Game Client missing
Account backend unavailable
required display disconnected
required SplitOS component missing
```

Possible response:

```text
retry
fallback
cancel target operation
degraded continuation
recovery escalation
```

Решение зависит от обязательности dependency.

---

## F-03 — Unsupported Capability

Integration существует, но required capability не поддерживается данным platform/client/device combination.

Examples:

```text
requested refresh rate unsupported
client adapter cannot provide reliable library discovery
system-wide default audio switching mechanism unavailable
```

Это не обязательно авария.

Possible result:

```text
UNSUPPORTED
→ choose supported fallback
or
→ stop operation before commit
```

---

## F-04 — External Evidence Missing / Stale / Contradictory

SplitOS не может получить достаточно свежую или непротиворечивую информацию от external authority.

Examples:

```text
GameInstallationProjection says installed, client no longer confirms it
cached entitlement expired and backend unavailable
hardware snapshot stale after device topology change
process evidence conflicts with client launch evidence
```

Rule:

```text
stale projection
!= canonical external truth
```

Response должен зависеть от risk/freshness policy.

---

## F-05 — Operation Rejected / Technical Failure

Integration mechanism отклонил операцию или завершился технической ошибкой.

Examples:

```text
SetDisplayConfig failed
SCM service operation rejected
Named Pipe privileged request denied
Game Client launch invocation failed
update package apply failed
```

Immediate technical failure ещё не говорит, был ли system state частично изменён.

Поэтому после state-changing operation failure может потребоваться actual-state read-back.

---

## F-06 — Partial Application

Часть target policy применена, часть — нет.

Example:

```text
Game display applied
power plan applied
one mode-managed service failed to change state
```

Это критический класс для transactional mode/update behavior.

Rule:

```text
partial target
!= successful target
```

Response:

```text
continue only if policy explicitly marks failed item non-mandatory
otherwise rollback / safe fallback
```

---

## F-07 — Verification Failure

Команды технически выполнились, но actual state не соответствует semantic target.

Example:

```text
requested 4K@120
API returned success
actual state = 4K@60
```

или:

```text
service stop accepted
actual service still running
```

Rule:

```text
verification failure
→ target state must not be committed
```

---

## F-08 — Component Crash / Runtime Loss

Один из SplitOS components неожиданно завершился или потерял communication channel.

Examples:

```text
Runtime Host crashed
Game Launcher crashed
Privileged Broker stopped
adapter process failed
```

Failure consequence зависит от ownership:

- UI crash не должен автоматически менять canonical mode;
- owner/orchestrator crash может потребовать durable reconciliation;
- privileged broker loss блокирует privileged mutation, но не должен сам ломать Windows session.

---

## F-09 — Persistence / Durability Failure

SplitOS не может надёжно сохранить canonical transaction/state data.

Examples:

```text
ModeTransitionRecord cannot be persisted
UpdateTransactionRecord durability failed
InstalledBaselineIdentity cannot be safely updated
RecoveryContext cannot be written
```

Для операций, требующих crash/reboot recovery, durability failure является blocking.

Rule:

```text
cannot persist recovery-critical state
→ do not enter irreversible mutation
```

---

## F-10 — Interruption / Reboot / Power Loss

Operation была прервана внешним завершением процесса, reboot, crash или power loss.

Important distinction:

```text
intent existed
!= target committed
```

После restart SplitOS должен опираться на:

- durable committed state;
- durable transaction record;
- actual Windows/device evidence;
- last-known-safe evidence;

а не на предположение "мы, наверное, почти переключились".

---

## F-11 — Recovery Failure

Recovery action itself failed or resulting state cannot be verified.

Это отдельный higher-severity failure class.

```text
primary operation failed
→ recovery started
→ recovery failed
```

Response priority:

```text
User data integrity
↓
Windows bootability/usability
↓
known coherent state
↓
base Windows experience where possible
↓
manual recovery / support path
```

---

## F-12 — Integrity / Trust Validation Failure

Evidence, package, caller, token or artifact не проходит required trust/integrity validation.

Examples may include:

```text
invalid update signature
unauthorized privileged IPC caller
invalid account token
unexpected build artifact integrity
```

Detailed rules принадлежат `10-Trust`.

На Failure layer фиксируется только принцип:

```text
trust validation failed
→ do not silently continue as trusted
```

---

# 5. Failure response classes

## R-01 — Reject

Operation не начинается.

Canonical state unchanged.

---

## R-02 — Defer

Operation допустима позже, но сейчас конфликтует с другой major mutation или temporary blocker.

---

## R-03 — Retry

Повторить idempotent/retry-safe operation после transient failure.

Retry policy must be bounded.

```text
retry forever
```

недопустим как универсальная стратегия.

---

## R-04 — Controlled fallback

Использовать заранее допустимую альтернативу.

Example:

```text
preferred Game display unavailable
→ use validated fallback display
```

Только если policy разрешает такой fallback.

---

## R-05 — Degraded continuation

Система продолжает работу без части возможностей.

Examples:

```text
Account backend unavailable
→ Windows desktop remains usable

optional game metadata stale
→ launcher may show limited information
```

Degraded state должен быть observable и не должен маскироваться как full success.

---

## R-06 — Cancel to source state

Target operation прекращается до semantic commit.

Example:

```text
Work→Game verification failed
→ source committed mode remains WORK
```

Если фактические changes уже применялись, может потребоваться rollback прежде чем source state снова будет verified.

---

## R-07 — Rollback

Вернуть систему к previous known coherent state.

Rollback itself:

```text
is an operation
→ produces evidence
→ requires verification
```

Он не считается успешным только потому, что rollback commands были отправлены.

---

## R-08 — Recovery

Recovery Coordination получает control, когда обычный flow/rollback недостаточен или state после interruption не доказан.

---

## R-09 — Manual recovery / support required

Автоматическая система не может доказать безопасный coherent result.

Windows usability и user-data access должны быть сохранены максимально возможным образом.

---

# 6. Severity model

Severity отражает impact, а не "насколько страшно выглядит exception".

## S0 — Informational / controlled negative outcome

Examples:

```text
user cancelled
client login required
feature unsupported
```

No system integrity loss.

## S1 — Local feature failure

Одна capability недоступна, основной current mode/session остаётся coherent.

Example:

```text
game artwork refresh failed
```

## S2 — Operation failure with known safe state

Target operation failed, но system safely remains or returns to known state.

Example:

```text
Work→Game failed and verified WORK restored
```

## S3 — Degraded system state

System usable, но часть expected SplitOS semantics недоступна или state needs attention.

Example:

```text
Privileged Broker unavailable
→ Windows desktop usable, managed mutations blocked
```

## S4 — Recovery required

SplitOS cannot safely continue normal managed operation until recovery resolves state.

## S5 — Manual recovery / bootability or data risk

Автоматическое recovery не может доказать coherent usable system.

Этот класс должен быть редким и явно observable.

---

# 7. Commit safety rules

## FR-FAIL-001 — No failure-driven implicit commit

Никакой error handler не может напрямую объявить target canonical state успешным.

## FR-FAIL-002 — No target commit after mandatory verification failure

```text
mandatory verification failed
→ target commit prohibited
```

## FR-FAIL-003 — Source state remains canonical before commit

Для transactional mode transition:

```text
source = WORK
apply GAME changes
failure before COMMITTING
→ canonical mode still WORK
```

Даже если actual platform temporarily находится в mixed state.

## FR-FAIL-004 — Mixed actual state is evidence, not new mode

```text
some WORK policy
+
some GAME policy
```

не создаёт новый operational mode `HYBRID`.

Это degraded actual state, который должен быть resolved.

## FR-FAIL-005 — Recovery result also requires verification

Recovery success is semantic, not merely technical.

---

# 8. Major mutation exclusivity

Current v1 semantic rule:

```text
Mode Transition
Update
Recovery
```

не должны независимо выполнять конфликтующие machine mutations одновременно.

Если один major mutation owner активен:

```text
second conflicting mutation
→ reject or defer
```

Exact locking/lease implementation is not canonical yet.

---

# 9. User experience principles

Failure UX должен отвечать пользователю минимум на четыре вопроса:

```text
Что не получилось?
В каком состоянии компьютер сейчас?
Что SplitOS сделал для безопасности?
Что пользователь может сделать дальше?
```

Нельзя показывать только:

```text
Error 0x80070005
```

без semantic context.

Также нельзя сообщать:

```text
Switched to Game Mode successfully
```

если verification не прошёл.

---

# 10. Diagnostic principle

Diagnostics capture evidence, but do not become canonical truth.

Для важного failure полезен correlation context:

```text
operation / transaction id
source state
target state
integration mechanism
technical result
actual-state evidence
verification result
chosen response
final semantic outcome
```

Detailed observability/storage belongs to specification/implementation layers.

---

# 11. Canonical safety priority

Across SplitOS failures:

```text
1. User data integrity
2. Windows bootability and base usability
3. Coherent known system state
4. Correct SplitOS canonical state
5. Restoration of managed runtime capabilities
6. UX convenience
```

This means premium runtime failure must never be allowed to intentionally make the underlying Windows installation unusable merely because SplitOS cannot complete its own operation.

---

# 12. Relationship to next layers

```text
09-Failures
→ what can fail and how semantic recovery is selected

10-Trust
→ which callers/evidence/artifacts are trusted and how validation works

11-Synthesis
→ final system architecture assembled from all previous layers
```

Failure model intentionally leaves detailed authentication, authorization, package-signature and secret-storage decisions to `10-Trust`.
