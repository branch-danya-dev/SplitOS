# Dependency and Critical Path

## 1. Purpose

Shows which implementation work can begin independently, which work is blocked, and what minimum sequence leads to the first meaningful SplitOS product prototypes.

This document exists to prevent false parallelism such as implementing a polished Launcher before the Runtime/Session/IPC contract exists, or building update rollback before durable release identity and persistence are stable.

---

## 2. Primary dependency chain

The shortest critical path to a real managed-mode prototype is:

```text
Implementation stack decision
↓
Build/source skeleton
↓
Runtime Host + Broker process skeleton
↓
Named Pipe hello / caller validation
↓
Machine + user persistence
↓
Mode state / transition journal
↓
Mutation lease + fencing
↓
Power / display adapters
↓
ACTIVATE WORK / SWITCH GAME
↓
actual-state verification
↓
GAME commit
↓
Game Launcher readiness
```

The shortest critical path to the first gaming MVP continues:

```text
GAME committed
↓
Steam adapter
↓
Game Library projection
↓
launch handoff
↓
process proof correlation
↓
GAME_RUNNING
↓
exit detection
↓
Launcher restore
```

---

## 3. Critical path to a prepared SplitOS installation

A separate but converging distribution path is:

```text
Build tooling/runtime packaging decision
↓
Windows source identity
↓
BuildManifest schema
↓
typed offline servicing executor
↓
minimal accepted Component Matrix
↓
SplitOS runtime package staging
↓
Setup provisioning
↓
clean install
↓
Runtime/Broker first boot
↓
BuildReceipt ↔ InstalledBaselineIdentity verification
```

This path can execute in parallel with mode/game development once the first runtime artifacts can be packaged.

---

## 4. Critical path to resilient alpha

```text
stable installed runtime
+
stable persistence/migrations
+
release artifact identity
↓
UpdateTransaction
↓
Recovery Capsule technology decision
↓
create/seal/verify previous release
↓
Update Bootstrap
↓
reboot/resume
↓
target health verification
↓
release commit
↓
software rollback preserving current user data
↓
WinRE recovery path
```

Release-security work overlays this path:

```text
TUF metadata model
+
Authenticode publisher validation
+
security floors
+
RecoveryAuthorization
↓
secure update / secure rollback
```

---

## 5. Dependency categories

### HARD_DEPENDENCY

Consumer implementation cannot be meaningfully completed without the producer.

Example:

```text
Game launch correlation
HARD_DEPENDS_ON
process evidence adapter
```

### CONTRACT_DEPENDENCY

Implementation may begin using fixtures/mocks once the contract is stable.

Example:

```text
Manager entitlement UX
CONTRACT_DEPENDS_ON
Entitlement contract
```

The real backend can arrive later if the contract remains stable.

### EVIDENCE_DEPENDENCY

Code can exist, but production support cannot be claimed until empirical evidence exists.

Example:

```text
Defender REMOVE
EVIDENCE_DEPENDS_ON
component validation lab
```

### OPERATIONAL_DEPENDENCY

Core code can be built with test fixtures, while production deployment requires external infrastructure.

Example:

```text
TUF client
can use test repository

production release signing
OPERATIONAL_DEPENDS_ON
HSM/CA/signing service
```

---

## 6. Parallelizable tracks

### Track A — Runtime Foundation

Can start immediately after stack decision:

- process skeleton;
- IPC contracts;
- caller/session checks;
- persistence;
- mode state machine;
- diagnostics envelope;
- verification result schema.

### Track B — Backend / Identity

Can run in parallel after auth contract confirmation:

- account backend;
- entitlement backend;
- external-browser auth;
- offline assertion fixture model;
- checkout integration later.

Runtime can use test entitlement fixtures until real backend lands.

### Track C — Windows Integrations

Can run in parallel with mode core:

- display query/apply prototype;
- power adapter;
- process evidence;
- PnP generation;
- GameInput controller evidence;
- SCM Broker capability.

These should expose the final typed adapter contracts even before Mode Runtime consumes them.

### Track D — Builder / Distribution

Can start once build/runtime packaging form is known:

- source identity;
- BuildManifest;
- WIM servicing lifecycle;
- Component Matrix lab;
- setup provisioning.

### Track E — Verification

Starts on day one:

- protocol negative tests;
- schema fixtures;
- migration fixtures;
- disposable Windows integration environment;
- hardware inventory model;
- later fault injection/performance.

---

## 7. False parallelism to avoid

### Launcher before Runtime truth

Bad:

```text
build complete visual Game Launcher
while GameSession/Profile/Runtime IPC are undefined in code
```

Better:

```text
Launcher shell
→ bind to Runtime snapshot
→ one route
→ one deterministic focus path
→ add surfaces with real data
```

### Optimizer before profile/config adapter

Bad:

```text
complex optimization algorithm
without real supported game-setting writer/read-back
```

Better:

```text
one game config adapter
→ legal settings
→ read/write/read-back
→ simple deterministic ladder
→ expand knowledge
```

### Aggressive Component Matrix before conservative baseline

Bad:

```text
spend weeks removing Defender/Edge
before Builder can produce a boring bootable image
```

Better:

```text
minimal KEEP baseline
→ boot/install pipeline
→ validate individual removals independently
```

### Production signing infrastructure before trust code works with fixtures

Bad:

```text
procure HSM/CDN/CA first
```

Better:

```text
implement role-separated test-key trust flow
→ negative tests
→ then bind to production custody infrastructure
```

---

## 8. Blocking decision map

| Decision / research | Blocks |
|---|---|
| implementation language/runtime | almost all executable projects |
| Manager/Launcher UI framework | Manager, Launcher, UI test approach |
| source/build system | CI/package/project skeleton |
| Windows disposable test environment | Windows integration automation |
| Recovery Capsule container | resilient update/rollback slice |
| first supported game/config adapter | optimizer vertical |
| PresentMon packaging | measured optimization/performance evidence |
| default audio setter | automatic audio-switch capability only |
| Windows source acquisition | automatic acquisition only; user-provided source remains unblocked |
| Defender/Edge removal validation | those component classifications only |
| Battle.net mechanism validation | Battle.net support only |
| in-game global controller chord | in-game panel invocation only |
| production HSM/CA/CDN selection | production release operations, not trust code fixtures |
| numeric performance budgets | production GATE-09, not early functional prototypes |

This table is important because many OPEN items do **not** block the entire product.

---

## 9. Prototype critical paths

### P0 — Secure Process Skeleton

```text
stack decision
→ RuntimeHost
→ BrokerService
→ Named Pipe
→ caller/session verification
→ health capability
→ structured event
```

### P1 — FREE Product

```text
P0
→ user store
→ protected secret store
→ Account flow
→ entitlement
→ FREE stable desktop
```

### P2 — Managed Mode

```text
P0
→ machine store
→ ModeTransition
→ lease/fence
→ power/display
→ verification
→ WORK/GAME commit
```

### P3 — Gaming MVP

```text
P2
→ Launcher
→ Steam adapter
→ process correlation
→ game running/exit
```

### P4 — Prepared Image

```text
runtime packaging
→ Builder
→ conservative Component Matrix
→ clean install
→ P1/P2/P3 runtime inside prepared baseline
```

### P5 — Resilient Alpha

```text
P4
→ UpdateTransaction
→ Recovery Capsule
→ Update Bootstrap
→ rollback
→ TUF/Authenticode
→ fault injection
```

---

## 10. Critical sequencing decisions

### Account is not required to prove local mode mechanics, but required before product PRO acceptance

Development fixtures may allow:

```text
DevEntitlement = PRO
```

for engineering mode tests.

But they must be compile/config test infrastructure and never a production trust path.

### Builder is not required to prove Windows adapters, but required before calling the result a SplitOS distribution prototype

This allows runtime engineering on normal development Windows while distribution engineering proceeds in parallel.

### Full release security is not required for first functional updater prototype, but the updater design must already preserve its final trust insertion points

Avoid a prototype that assumes:

```text
HTTPS == trusted release
```

because that architecture would need replacement.

---

## 11. Dependency exit criteria

A dependency is considered sufficiently stable when the consumer has:

```text
versioned contract
representative fixture
failure semantics
compatibility expectations
owner
```

The producer implementation does not always need to be complete.

This enables parallel delivery without mocking away semantics.

---

## 12. Grooming conclusion

The program's actual critical path is not “implement every module”.

It is:

```text
prove boundaries
→ prove durable ownership
→ prove actual Windows mutation and verification
→ prove one gaming vertical
→ prove prepared distribution
→ prove recovery/security
→ broaden matrix
```

Breadth should be added only after these verticals are stable.