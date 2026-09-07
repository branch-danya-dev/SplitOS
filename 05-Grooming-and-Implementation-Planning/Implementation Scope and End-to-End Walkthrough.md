# Implementation Scope and End-to-End Walkthrough

## 1. Purpose

Defines what the first implementation program must actually deliver, what is intentionally outside the first supported scope, which components are touched, and how the main product path crosses those components.

This document is a Grooming artifact. Normative behavior remains in Requirements / A&D / SPEC-01..14.

---

## 2. Implementation problem statement

SplitOS is not a standalone launcher and not a replacement kernel.

The delivery problem is to build a Windows 11 based product that has:

```text
prepared SplitOS baseline
+
mandatory SplitOS product identity
+
FREE stable Windows experience
+
optional PRO managed runtime
+
WORK xor GAME orchestration
+
controller-first Game Launcher
+
validated game/client/profile integration
+
safe update and recovery
```

The implementation must preserve Windows usability even when SplitOS premium/runtime dependencies fail.

---

## 3. v1 product implementation scope

### 3.1 Distribution

In scope:

- user-provided Microsoft-authorized Windows source;
- source identity validation;
- Build Manifest execution;
- versioned Windows Component Matrix subset;
- typed offline servicing;
- staging SplitOS packages;
- clean installation media generation;
- setup-time SplitOS provisioning;
- BuildReceipt and baseline identity;
- lab validation of accepted component lifecycle decisions.

Not automatically in scope for first prototype:

- automatic Microsoft source download;
- every desired REMOVE candidate;
- unvalidated Defender removal;
- unvalidated Edge browser removal;
- mutation of arbitrary existing Windows installs as a supported product path.

### 3.2 Installed runtime

In scope:

- Runtime Host;
- Privileged Broker;
- Manager;
- Game Launcher;
- Update Bootstrap;
- Recovery Tool;
- machine/user persistence;
- local structured diagnostics;
- protected account secret storage;
- active-console ownership model.

### 3.3 Identity / entitlement

In scope:

- SplitOS Account after Windows user exists;
- external-browser auth flow;
- local Windows-user association;
- FREE stable state;
- PRO capability activation;
- bounded offline entitlement assertion;
- hosted checkout callback as refresh trigger only;
- no coupling between account backend failure and Windows sign-in.

### 3.4 Mode runtime

In scope:

```text
ACTIVATE   NONE → WORK|GAME
SWITCH     WORK ↔ GAME
DEACTIVATE WORK|GAME → NONE
```

including:

- mutation lease/fencing;
- blocker inspection;
- desired policy resolution;
- Windows operation invocation;
- read-back verification;
- atomic mode commit;
- rollback / safe fallback / Recovery escalation;
- Runtime Host restart reconciliation.

### 3.5 Windows context

Required first implementation subset:

```text
Display
Power
Processes / blocker evidence
Managed Services subset
Hardware / device generation
Input/controller discovery
```

Audio read/observe is in scope, but automatic system-default audio switching remains capability-gated until a supported setter exists.

### 3.6 Gaming

Initial supported vertical:

```text
Game Launcher
→ Steam adapter
→ one or more explicitly verified Steam games
→ process/game correlation
→ GAME_RUNNING
→ exit detection
→ return to Launcher
```

Epic follows after the shared adapter/session model is proven.

Microsoft Gaming remains partial until the supported local capability matrix is proven.

Battle.net remains experimental until its mechanism is validated.

### 3.7 Game profiles and optimization

In scope for first alpha after gaming vertical:

- multiple profiles per game;
- Desktop and TV/living-room scenarios;
- deterministic hardware/display/input matching;
- field-level user locks;
- per-game configuration adapters for a small supported game set;
- explainable recommendation engine;
- optional measured performance evidence;
- no generic live gameplay setting thrashing.

### 3.8 Shared Apps

In scope after core gaming MVP:

```text
BACKGROUND
SECONDARY_DISPLAY
LOCKED_WINDOW
```

`OVERLAY` and in-game SplitOS panel remain capability-gated and must not require injection/hooking.

### 3.9 Lifecycle

In scope:

- independent SplitOS update channel;
- coexistence with validated Microsoft servicing;
- previous-release Recovery Capsule required before activation;
- reboot/resume transaction;
- user-data-preserving software rollback;
- WinRE bounded recovery path;
- TUF repository trust;
- Authenticode publisher validation;
- anti-rollback/security floors.

---

## 4. Explicit v1 non-goals / later scope

The first supported product does not require:

- custom kernel;
- custom Windows bootloader;
- dual boot Work/Game;
- Work/Game virtual machines;
- replacement of Windows Shell in WORK;
- injection into games;
- DRM/anti-cheat bypass;
- network/matchmaking/input-cheating modifications;
- generic arbitrary Broker admin shell;
- arbitrary service/registry/path mutation interfaces;
- automatic unsupported default audio endpoint hacks;
- GPU overclock, voltage, fan, undervolt or vendor profile mutation as core behavior;
- mandatory cloud telemetry;
- full Battle.net support before evidence;
- full Xbox/Game Pass cloud-library ownership model;
- same-device Recovery Capsule as disk-loss backup;
- destructive automatic Windows reset/format recovery.

---

## 5. Main end-to-end system walkthrough

### 5.1 Build and install

```text
User provides supported Windows source
↓
Media Builder identifies source
↓
Build Manifest + Component Matrix resolve allowed operations
↓
Builder applies typed servicing
↓
Builder verifies postconditions
↓
BuildReceipt emitted
↓
installation media created
↓
clean installation
↓
Windows OOBE
↓
Windows user created
```

Touched implementation areas:

```text
Builder
Component Matrix / Release Knowledge
Package/Staging tooling
Setup provisioning
Build verification harness
```

### 5.2 First Windows sign-in

```text
Windows logon
↓
Runtime Host starts in user session
↓
Broker service already machine-available
↓
Runtime validates local stores / component compatibility
↓
SplitOS First Run
↓
external browser auth
↓
SplitOS Account associated with current Windows user
↓
Entitlement resolved
```

If entitlement is FREE:

```text
ManagedRuntime = DISABLED
OperationalMode = NONE
Windows Desktop usable
```

Touched implementation areas:

```text
Runtime Host
Persistence
Manager / First Run UI
Account backend
Protected Secret Store
Entitlement evaluator
Diagnostics
```

### 5.3 PRO activation

```text
valid PRO entitlement
↓
Runtime access enabled
↓
physical console ownership confirmed
↓
mode selection
↓
ACTIVATE WORK or GAME
```

No mode commit occurs before actual target verification.

### 5.4 WORK → GAME

```text
User requests GAME
↓
RuntimeAccess check
↓
mutation lease acquired
↓
transition journal REQUESTED
↓
Work context inspected
↓
blockers classified
↓
user decision if required
↓
GAME target policy resolved
↓
Windows targets applied
↓
actual display/power/service/input state read back
↓
Game Launcher READY_PRECOMMIT
↓
mandatory verification passes
↓
atomic COMMIT GAME
↓
Launcher ACTIVE
```

Touched implementation areas:

```text
Mode State
Mode Transition
Mode Policy
Blocker providers
Windows adapters
Broker
Persistence
Launcher
Diagnostics
```

### 5.5 Managed game launch

```text
User selects game
↓
Game Library projection
↓
profile selection
↓
fresh hardware context
↓
optimization recommendation
↓
per-game config apply + verify
↓
Steam adapter handoff
↓
HANDOFF_ACCEPTED
↓
process evidence appears
↓
GAME_RUNNING_CONFIRMED
↓
Launcher releases foreground/input
```

Touched implementation areas:

```text
Launcher
Game Library
Game Profile
Optimization
Game Config Adapter
Steam Adapter
Process Evidence
Game Session
```

### 5.6 Game exit

```text
primary game evidence exits
↓
GAME_EXITED_CONFIRMED
↓
Launcher restores foreground
↓
pre-launch route/focus restored
↓
CommittedMode remains GAME
```

Normal game exit does not trigger GAME → WORK.

### 5.7 GAME → WORK

```text
User requests WORK
↓
active game check
↓
user confirmation if close required
↓
actual game exit
↓
WORK target resolved
↓
apply / read-back / verify
↓
atomic COMMIT WORK
↓
Launcher becomes inactive/stopped according to lifecycle
```

### 5.8 SplitOS update

```text
SplitOS release metadata discovered
↓
TUF + compatibility verification
↓
artifacts downloaded and Authenticode-verified
↓
target staged
↓
previous current release Recovery Capsule created
↓
Recovery Capsule sealed + verified
↓
UPDATE mutation lease
↓
Update Bootstrap activates target
↓
reboot if required
↓
transaction resumes
↓
target Runtime/Broker/DB compatibility verified
↓
InstalledSplitOSRelease committed
```

If target verification fails:

```text
Recovery Coordination
↓
authorized previous-release restore
↓
software rollback
↓
current user data preserved
```

---

## 6. Component change surface

| Area | Main delivery responsibility | First meaningful evidence |
|---|---|---|
| Runtime Host | user-session orchestration | starts, owns per-session modules, reconnects/reconciles |
| Broker | bounded privileged mutation | authenticated hello + one allowlisted machine operation |
| Manager | account/settings/control center | First Run + FREE/PRO state projection |
| Game Launcher | Game Mode presentation | READY_PRECOMMIT + controller navigation + launch flow |
| Persistence | durable canonical/transaction state | crash-safe mode journal and user profile writes |
| Windows Adapters | actual Windows integration | apply + read-back for display/power/service subset |
| Game Client adapters | external handoff/evidence | Steam launch + running/exit confirmation |
| Game Profile/Optimization | scenario intent | Desktop/TV selection + one config adapter |
| Builder | prepared baseline | lab image boots and provisioning succeeds |
| Backend | account/entitlement | auth + FREE/PRO entitlement contracts |
| Update/Recovery | lifecycle safety | N→N+1 + Recovery Capsule + N+1→N test |
| Release Security | trusted release | TUF fixture + publisher verification + anti-rollback tests |
| Diagnostics | supportability | correlated transition/game/update bundle |
| Verification | production evidence | automated gate evidence for supported scope |

---

## 7. Cross-cutting edge cases that every delivery team must understand

- backend unavailable during first run;
- FREE must remain valid and Windows usable;
- second Windows session attempts machine mutation;
- Runtime Host crashes before/after mode commit;
- Broker crashes after partial machine mutation;
- monitor/controller disappears after target resolution;
- application/process evidence is ambiguous;
- Steam accepts launch but game never starts;
- game crashes while GAME remains committed;
- user modifies game configuration outside SplitOS;
- disk full during durable state write;
- update interrupted before and after durable release commit;
- Recovery Capsule unreadable/corrupted;
- rollback software version while preserving newer user data;
- old authentic release presented as downgrade attack;
- diagnostics contain synthetic secret material;
- Windows patch/build no longer matches accepted compatibility knowledge.

These are not optional QA polish. They are part of the system behavior that implementation tasks must preserve.

---

## 8. Grooming conclusion

The first implementation program is broad, but the work can be delivered incrementally because the architecture provides stable semantic boundaries.

The decomposition strategy is therefore:

```text
prove process + privilege skeleton
↓
prove persistence + FREE identity vertical
↓
prove managed mode transaction
↓
prove one gaming vertical
↓
add profile/TV behavior
↓
prove prepared distribution
↓
prove update/recovery/security lifecycle
↓
expand supported matrix
```

The exact ordering and parallel tracks are defined in `Delivery Slices and Milestones.md`.