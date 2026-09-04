# SPEC-06 — Power, Process and Service Integration

## 1. Purpose

Defines Windows mechanisms for power-plan state, process evidence and allowlisted Windows service lifecycle used by SplitOS Mode Runtime.

These are intentionally separate concerns:

```text
Power scheme
!= process lifecycle
!= Windows service lifecycle
```

---

# Part A — Power

## 2. Power API baseline

v1 uses PowrProf APIs:

```text
PowerGetActiveScheme
PowerSetActiveScheme
PowerEnumerate / related read functions where required
PowerSettingRegisterNotification / effective-mode notifications where required
```

Normal Work/Game power policy executes in the interactive Runtime Host because `PowerSetActiveScheme` changes the active scheme for the current user.

---

## 3. SplitOS power target

Mode Policy references a release-defined `PowerPolicyId`, not an arbitrary GUID supplied by UI.

Example mapping:

```text
BASE_DEFAULT
WORK_BALANCED
GAME_PERFORMANCE
```

Release configuration maps these semantic IDs to supported Windows scheme GUIDs or to a no-change policy.

The exact user-facing optimization policy belongs to SPEC-08.

---

## 4. Power apply

Flow:

```text
PowerGetActiveScheme
→ if already expected: ALREADY_SATISFIED
→ PowerSetActiveScheme(expectedGuid)
→ PowerGetActiveScheme
→ compare GUID
→ APPLIED_VERIFIED or VERIFICATION_FAILED
```

The pre-operation scheme GUID is stored in operation source evidence for rollback.

---

## 5. Missing/unsupported schemes

Windows hardware/OEM configuration may not expose every classic power plan expected by a generic desktop assumption.

Therefore release compatibility must validate required schemes or provide a fallback.

Result classes:

```text
TARGET_NOT_FOUND
UNSUPPORTED_CAPABILITY
APPLIED_VERIFIED
VERIFICATION_FAILED
```

SplitOS MUST NOT generate/modify arbitrary power settings from profile JSON without an explicit versioned policy contract.

---

## 6. Out of scope power controls

SPEC-06 v1 does not define:

```text
GPU overclock/undervolt
CPU voltage/clock manipulation
vendor control-panel hacks
firmware power limits
fan curves
thermal-limit override
```

Such integrations require separate vendor-specific validation and safety contracts.

---

# Part B — Process evidence

## 7. Process API baseline

For transition inspection and general process evidence, Runtime Host uses supported Win32 process APIs:

```text
EnumProcesses or equivalent supported snapshot enumeration
OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION where sufficient)
QueryFullProcessImageName
ProcessIdToSessionId
GetProcessTimes when identity needs protection against PID reuse
```

The adapter minimizes requested process rights.

---

## 8. Process identity

PID alone is not a durable process identity.

For one observation SplitOS records:

```text
pid
sessionId
imagePath when readable
processCreationTime when readable
observedUtc
```

Where correctness depends on distinguishing PID reuse:

```text
pid + creationTime
```

is used as the observation identity.

Image path is normalized before policy matching.

---

## 9. Process classification

Process evidence is matched against release/user configuration through typed `ApplicationId` / application classification.

Policy MUST NOT accept a free-form process path from a remote/backend/UI source and automatically perform destructive operations on it.

A process observation may prove:

```text
application process is currently observed
process belongs to current/other Windows session
image identity matches configured application mapping
```

It cannot prove:

```text
user document is saved
server job is semantically complete
application can be killed without data loss
```

---

## 10. Generic process close semantics

`TerminateProcess` is **not** the normal Work→Game application lifecycle mechanism.

Generic supported behavior is conservative:

```text
observe process
→ classify blocker/policy
→ request app-specific/user-mediated resolution where available
→ wait for actual process exit
```

A future release may define an allowlisted graceful window close mechanism for specific applications, but:

```text
close request sent
!= process exited
!= user data safely saved
```

Force termination requires an explicit destructive policy/recovery context and is not part of normal v1 mode switching.

---

## 11. Known launched process tracking

When SplitOS itself obtains/learns a reliable process handle for a managed launched process, it may wait on that process for exit evidence.

For external Game Client/game process semantics, exact discovery/correlation belongs to SPEC-07.

SPEC-06 provides only generic Windows process primitives.

---

# Part C — Windows services

## 12. Service execution boundary

Managed service mutation executes in the Privileged Broker.

Runtime Host sends only SplitOS-owned semantic target IDs through the SPEC-02 capability surface.

Example:

```text
ManagedServiceId = SEARCH_INDEXER
DesiredState = STOPPED
```

Runtime Host never sends:

```text
serviceName = "arbitrary string"
```

to a generic stop API.

---

## 13. SCM API baseline

Broker service adapter uses Service Control Manager APIs:

```text
OpenSCManager
OpenService
QueryServiceStatusEx
StartServiceW
ControlService for supported controls such as SERVICE_CONTROL_STOP
CloseServiceHandle
```

Service names and accepted actions come from the trusted release-owned managed-service catalog.

---

## 14. Service target catalog

Each `ManagedServiceId` record includes at least:

```text
managedServiceId
Windows service key name
allowed desired states
required access mask
start/stop timeout policy
required dependencies/fallback metadata
supported Windows release/build constraints
```

The catalog is signed/release-owned through the broader release trust chain.

It is not mutable by ordinary user preferences.

---

## 15. Service read/verify

Broker obtains actual state via `QueryServiceStatusEx`.

Normalized states include:

```text
STOPPED
START_PENDING
STOP_PENDING
RUNNING
PAUSED
OTHER_TRANSITIONAL
NOT_FOUND
ACCESS_DENIED
```

For v1 Mode Policy, normal managed targets are generally `RUNNING` or `STOPPED`.

---

## 16. Start verification

`StartServiceW` returning success does not prove `RUNNING`.

Flow:

```text
StartServiceW
→ QueryServiceStatusEx loop bounded by policy deadline/wait hints
→ SERVICE_RUNNING observed
→ APPLIED_VERIFIED
```

If service remains pending or stops/fails:

```text
→ VERIFICATION_FAILED / TECHNICAL_FAILURE
```

---

## 17. Stop verification

Flow:

```text
ControlService(SERVICE_CONTROL_STOP)
→ QueryServiceStatusEx bounded wait
→ SERVICE_STOPPED observed
→ APPLIED_VERIFIED
```

If SCM rejects the control, dependencies block it, or the service returns to running:

```text
→ OPERATION_REJECTED / VERIFICATION_FAILED
```

No false success is reported because the stop control was merely accepted.

---

## 18. Dependency behavior

SplitOS MUST NOT recursively stop arbitrary dependent services merely to satisfy one target.

Dependency handling is release-defined.

Allowed policy shapes:

```text
service action validated safe as-is
explicit dependent managed services listed in action plan
approved fallback: leave service running
mandatory target fails transition
```

Unplanned service fan-out is prohibited.

---

## 19. Service configuration mutation

Normal Mode switching changes runtime state, not arbitrary Windows service installation/configuration.

SPEC-06 Mode flow does not use:

```text
ChangeServiceConfig
DeleteService
CreateService
```

for ordinary Work/Game transitions.

Build/install lifecycle owns permanent service configuration.

---

## 20. Service result mapping

| Condition | Result |
|---|---|
| managed ID not in current release catalog | `TARGET_NOT_FOUND` / policy invalid |
| service already target state | `ALREADY_SATISFIED` |
| SCM start/stop submitted and read-back reaches target | `APPLIED_VERIFIED` |
| service control rejected | `OPERATION_REJECTED` |
| service missing on incompatible/drifted baseline | `TARGET_NOT_FOUND` + compatibility/recovery evidence |
| read-back never reaches target | `VERIFICATION_FAILED` |
| caller has stale fence | Broker denies via SPEC-05/SPEC-02 |

---

## 21. Acceptance criteria

### Power

- active scheme is read with PowrProf;
- PowerPolicyId resolves through release-owned mapping;
- `PowerSetActiveScheme` always has read-back verification;
- source scheme is available for rollback resolution;
- no vendor overclock/tuning is hidden in the power adapter.

### Processes

- process observation uses minimum practical rights;
- PID reuse is handled where identity matters;
- process presence is not misrepresented as unsaved-work evidence;
- generic force kill is absent from normal mode switching;
- app-specific close semantics remain explicit.

### Services

- service mutation occurs only through Broker;
- only release-owned ManagedServiceIds may be mutated;
- SCM read-back verifies final state;
- StartService/ControlService submission is not treated as completion;
- arbitrary dependent-service recursion is prohibited;
- permanent service configuration belongs to install/build, not Mode Runtime.
