# SplitOS — Local Privilege and IPC Trust

## 1. Purpose

Документ определяет trust boundary между interactive SplitOS runtime и machine-level privileged operations.

Canonical topology:

```text
Interactive Windows user session
├── SplitOS Manager
├── Game Launcher
└── SplitOS Runtime Host
        │
        │ secured, authenticated, authorized local IPC
        ▼
SplitOS Privileged Broker
Windows Service / Session 0
```

Главный принцип:

> Runtime Host — orchestration authority на уровне SplitOS semantics, но не получает unrestricted Windows administrator authority.

---

## 2. Why separate Broker exists

Без отдельного Broker есть два плохих варианта:

### Anti-pattern A

```text
GameLauncher.exe
→ Run as Administrator
→ меняет services/policies/update state
```

Это смешивает:

- UX;
- user input;
- network content;
- game/client metadata;
- privilege.

### Anti-pattern B

```text
SplitOS Runtime Host = LocalSystem
```

Тогда compromise runtime/process surface автоматически становится machine compromise.

Правильная модель:

```text
high-surface interactive runtime
→ low-privilege

small privileged mutation surface
→ Broker
```

---

## 3. Broker trust responsibilities

Broker отвечает только за:

1. проверку локального caller;
2. authorization requested capability;
3. validation параметров;
4. выполнение allowlisted machine-level operation;
5. возврат technical result/evidence;
6. security audit correlation.

Broker **не владеет**:

- OperationalMode;
- entitlement;
- Game Profile;
- game library;
- UI navigation;
- mode transition semantic commit.

---

## 4. IPC baseline

Current integration candidate:

```text
Named Pipe
+
explicit security descriptor / DACL
+
Windows caller token/session validation
+
versioned message contract
```

Windows Named Pipe security позволяет задавать security descriptor; доступ клиента проверяется системой против DACL. Default descriptor не считается достаточным design choice и не должен использоваться вслепую.

### Required principle

```text
CreateNamedPipe
→ explicit intended access policy
```

а не:

```text
CreateNamedPipe(..., NULL security descriptor)
```

---

## 5. Caller authorization dimensions

Broker должен различать минимум:

### Windows security identity

- SID/token caller;
- logon session;
- integrity/elevation context where relevant.

### Session binding

Запрос user-specific operation должен относиться к ожидаемой interactive session.

Named Pipe DACL может ограничиваться соответствующим logon SID для предотвращения доступа из другой Terminal Services/logon session.

### Protocol identity

Caller должен говорить по ожидаемому SplitOS Broker Protocol version.

### Capability authorization

Даже допустимый Runtime Host не получает unrestricted permission.

Пример:

```text
APPLY_MODE_SERVICE_POLICY
```

может быть разрешён, а:

```text
DELETE_ARBITRARY_FILE
```

не существует как contract.

---

## 6. Impersonation and access-token validation

Windows позволяет Named Pipe server impersonate client для получения/использования client security context.

Broker может использовать platform-supported token inspection/impersonation как часть identity/authorization validation.

Critical rule:

> failure caller impersonation/identity validation must fail closed.

Недопустимо:

```text
ImpersonateNamedPipeClient failed
→ continue request as LocalSystem
```

Потому что это превращает validation failure в privilege escalation.

---

## 7. Broker operation shape

Broker API должен быть semantic and bounded.

### Good

```text
ApplyServicePolicy(
  transitionId,
  policyId,
  expectedSourceState
)
```

```text
ApplyMachinePolicy(
  operationId,
  policySetId
)
```

```text
StageVerifiedUpdate(
  updateTransactionId,
  verifiedPackageRef
)
```

### Bad

```text
RunCommand(string commandLine)
```

```text
RunPowerShell(string script)
```

```text
WriteRegistry(string path, string name, object value)
```

```text
StopService(string arbitraryServiceName)
```

Raw primitives dramatically expand privilege abuse surface.

---

## 8. Allowlisting

Privileged operation should resolve through SplitOS-owned product knowledge:

```text
semantic operation ID
→ Broker-known/validated operation definition
→ bounded OS mutation
```

Например Runtime Host не должен присылать:

```text
serviceName = user-controlled string
```

если он может вместо этого прислать:

```text
policySetId = GAME_BASELINE_V3
```

и Broker сам определит разрешённый набор изменений из trusted product configuration.

---

## 9. Input validation

Broker treats all payload fields as untrusted.

Проверки минимум:

- schema version;
- maximum message size;
- enum range;
- required IDs;
- transaction correlation;
- path normalization when paths unavoidable;
- no path traversal;
- no raw shell interpretation;
- no arbitrary environment expansion;
- no executable path from Game Client metadata used as privileged command;
- no unbounded list/recursive action input;
- no silent unknown-field privilege behavior.

---

## 10. Replay / duplicate request handling

Privileged mutations должны быть correlated:

```text
operationId / transactionId
```

Broker должен иметь idempotency/dedup semantics там, где повтор опасен.

Пример после timeout:

```text
Runtime Host unsure whether operation applied
→ retry same operationId
```

не должен приводить к двум логически отдельным destructive actions.

Exact persistence scope idempotency keys определяется Specification.

---

## 11. Broker does not trust UI

Manager или Game Launcher не должны напрямую подключаться к Broker для business operation path.

Preferred path:

```text
UI
→ Runtime Host semantic request
→ owner/orchestrator validation
→ Broker if privilege required
```

Это уменьшает количество privileged callers.

---

## 12. Entitlement at Broker boundary

Broker не должен доверять полю:

```text
isPro = true
```

пришедшему из UI.

При этом Broker не обязан становиться новым Product Identity owner.

Preferred semantic design:

```text
Runtime Access owner
→ authorizes semantic operation
→ Runtime Host creates bounded privileged request
→ Broker validates local caller + operation legitimacy
```

Для особо entitlement-sensitive privileged capability может понадобиться locally verifiable capability grant, но точный формат остаётся OPEN.

Важно избежать двух крайностей:

```text
Broker blindly trusts UI premium flag
```

и

```text
Broker reimplements entire entitlement domain independently
```

---

## 13. Runtime Host binary provenance

Caller Windows SID/session — primary OS identity evidence, но не обязательно единственный возможный signal.

Дополнительная defense-in-depth проверка может включать:

- expected executable location with protected ACL;
- Authenticode publisher/signature verification;
- expected process ancestry/session context;
- installed runtime version compatibility.

Однако:

```text
signed file exists
```

не доказывает автоматически:

```text
this request is authorized
```

Binary provenance — дополнительная assurance, не замена capability authorization.

Exact Broker-side process provenance verification remains CANDIDATE/OPEN until implementation testing.

---

## 14. Service account and privileges

Broker должен работать с минимально достаточным privilege set.

Нельзя автоматически предполагать:

```text
Broker = LocalSystem because convenient
```

Нужно на implementation design проверить:

- нужен ли LocalSystem;
- подходит ли virtual service account;
- какие service privileges реально нужны;
- можно ли разделить update/recovery privilege from normal mode operations;
- какие filesystem/registry/service ACLs можно дать точечно.

Principle:

```text
least privilege
+ narrow service ACL
+ narrow IPC ACL
+ narrow operations
```

---

## 15. Windows Service security

Service installation/configuration itself является sensitive asset.

Нужно защитить:

- service executable path;
- service binary ACL;
- service configuration ACL;
- recovery configuration;
- writable DLL/search paths;
- configuration consumed by privileged service.

Ordinary user must not be able to replace Broker binary/configuration and then obtain SYSTEM-equivalent execution.

---

## 16. Privileged package/update path

Broker принимает update/recovery artifact только после upstream trust validation.

Preferred chain:

```text
Update Orchestration
→ verify manifest/package trust
→ create verified artifact reference
→ Broker revalidates critical binding as needed
→ apply bounded operation
```

Нельзя:

```text
Runtime Host
→ arbitrary local path
→ Broker executes installer from that path
```

---

## 17. User-session Windows integrations

Не все platform actions нужно уносить в Broker.

Операции, которые:

- naturally belong to user session;
- supported without elevation;
- require interactive device/session context;

должны оставаться в Runtime Host/user-session integration.

Примеры кандидатов:

- display topology/read/apply where permissions allow;
- user audio endpoint discovery;
- input/device observation;
- process evidence;
- launching normal Game Client process.

Broker — не universal Windows API proxy.

---

## 18. Denial behavior

Если Broker rejects request:

```text
DENIED_CALLER
DENIED_CAPABILITY
INVALID_PAYLOAD
CONTEXT_MISMATCH
STALE_TRANSACTION
UNSUPPORTED_OPERATION
```

Runtime Host должен трактовать это как explicit technical/trust result.

Он не должен обходить Broker другим admin mechanism автоматически.

Пример запрещённого fallback:

```text
Broker denied service change
→ launch powershell.exe -Verb RunAs
→ do it anyway
```

---

## 19. Audit requirements

Security-sensitive Broker operation должна иметь audit correlation минимум:

- operationId;
- transactionId when applicable;
- caller SID/session metadata;
- capability;
- requested semantic policy/version;
- authorization result;
- operation result;
- timestamp;
- resulting evidence summary.

Audit не должен включать secrets/tokens.

---

## 20. Local compromise scenarios

### Another process connects to Broker pipe

```text
connect attempt
→ DACL/token validation
→ denied
```

### Another Windows user/session connects

```text
wrong logon SID/session
→ denied
```

### Valid caller sends unknown privileged operation

```text
protocol parsed
→ capability not allowlisted
→ denied
```

### Runtime Host compromised inside same user

Trust boundary reduces damage but does not claim perfect containment.

Broker still restricts available privileged operations and validates payload/context.

This is why arbitrary command APIs are prohibited.

### Local Administrator attacks Broker

Outside v1 security guarantee. Product should not claim resistance to unrestricted local Administrator/kernel authority.

---

## 21. Security invariants

### IPC-INV-001

Only explicitly authorized Windows security contexts may connect/use privileged protocol.

### IPC-INV-002

Caller validation failure is fail-closed.

### IPC-INV-003

No arbitrary command/script execution contract exists.

### IPC-INV-004

Broker only performs known operation classes with validated parameters.

### IPC-INV-005

UI never becomes direct privileged authority.

### IPC-INV-006

External Game Client/browser content never flows directly into privileged command execution.

### IPC-INV-007

Broker technical success cannot commit OperationalMode/Entitlement itself.

### IPC-INV-008

Privileged request replay must not silently duplicate non-idempotent destructive work.

### IPC-INV-009

Broker binary/configuration must not be modifiable by ordinary user.

### IPC-INV-010

Broker denial cannot be bypassed by automatic alternative elevation path.

---

## 22. Implementation evidence basis

Windows provides the primitives required for this design family:

- Named Pipe objects support explicit security descriptors/DACL access checks;
- logon SID can scope access to a particular logon session;
- Named Pipe server can inspect/impersonate client security context;
- impersonation failure must be handled safely because continuing as privileged server context would be dangerous.

Exact C#/native implementation remains Specification/Engineering work.

---

## 23. Open questions

- final IPC transport: Named Pipe remains current candidate;
- exact SDDL/DACL definition;
- exact caller token checks;
- whether binary signature/provenance verification is mandatory Broker-side;
- service account choice;
- Broker privilege stripping/service SID strategy;
- operation idempotency persistence window;
- whether update/recovery need separate privileged worker/service;
- exact local capability-grant model for entitlement-sensitive operations;
- Broker protocol serialization format;
- protocol compatibility/version negotiation.

---

## 24. Result

Canonical privileged path:

```text
User intent
→ Runtime semantic owner
→ bounded privileged capability request
→ OS identity/session verification
→ capability authorization
→ payload validation
→ Broker operation
→ technical result
→ actual-state verification by semantic owner
→ canonical commit if justified
```

Так SplitOS получает необходимые machine privileges без превращения всего UI/runtime surface в administrator process.