# SPEC-06 — Traceability

## 1. Source mapping

| SPEC-06 decision | Primary source |
|---|---|
| supported Windows mechanism + read-back verification | A&D Interfaces/Integrations/Flows/Trust + SPEC-05 Mode action verification |
| CCD display APIs are v1 topology/mode mechanism | `07-Integrations/Windows Runtime Integration.md` + current Windows API validation |
| persistent display selector is not raw targetId/friendly name | Data/External Evidence Trust + device identity requirement |
| normal Work/Game display mutation is temporary | Mode semantics + safe rollback + SetDisplayConfig persistence distinction |
| display hot-plug invalidates resolved target | Hardware refresh + failure model |
| Core Audio/MMDevice is canonical audio read/notification layer | A&D Windows integration baseline |
| audio StableId preferred on Windows 11 24H2+ | current Windows endpoint identity capability |
| system-wide default audio setter remains OPEN | A&D explicit OPEN + absence of adopted documented setter |
| documented Windows Settings is audio user fallback | First-class user-decision behavior + supported Windows Settings URI |
| GameInput is primary game-controller API | controller-first Game UX + current Microsoft GameInput runtime |
| GameInput device ID drives persistent controller selectors | hardware/profile identity requirement |
| CM/SetupAPI handles generic PnP evidence | Windows hardware integration boundary |
| PowrProf controls active power scheme | A&D integration baseline |
| process adapter provides bounded evidence, no generic force kill | Work blocker semantics + data safety priority |
| SCM service mutation is Broker-mediated + allowlisted | SPEC-02 Broker capability model + Mode Policy no raw service names |
| adapter snapshot generations invalidate stale resolution | hardware refresh + flow/failure actual-state model |
| desired/resolved/actual are separate | Data + Interfaces + SPEC-05 resolved policy model |
| current Windows build/capability is release-gated | Compatibility responsibility + release coherence |

---

## 2. Specification decisions

```text
SPEC-DEC-039
v1 display topology/mode orchestration uses Windows CCD APIs (`QueryDisplayConfig` / `DisplayConfigGetDeviceInfo` / `SetDisplayConfig`) with mandatory read-back verification.

SPEC-DEC-040
persistent display association uses a layered selector based on monitor/device identity evidence; raw CCD targetId and friendly monitor name are not persistent unique keys.

SPEC-DEC-041
normal Work/Game display changes are temporary Windows display configuration changes and do not use `SDC_SAVE_TO_DATABASE` unless an explicit future feature intentionally changes durable Windows display preferences.

SPEC-DEC-042
advanced color/HDR read/write remains release/build capability-gated; an unvalidated HDR setter cannot be a mandatory mode/profile target.

SPEC-DEC-043
v1 audio observation uses MMDevice/Core Audio endpoint enumeration, default-endpoint reads and `IMMNotificationClient` invalidation.

SPEC-DEC-044
on Windows 11 24H2+ where available, `PKEY_AudioEndpoint_StableId` is the preferred persisted audio endpoint identity; legacy endpoint ID is a weaker fallback across OS/driver changes.

SPEC-DEC-045
v1 does not adopt undocumented `IPolicyConfig`/registry automation as canonical system-default audio switching. Automatic default-audio mutation remains OPEN; documented Windows Sound Settings is the user-mediated fallback.

SPEC-DEC-046
Microsoft GameInput is the primary v1 game-controller/input device discovery API and SplitOS provisions a supported GameInput redistributable in the release baseline.

SPEC-DEC-047
specific controller association uses opaque GameInput device ID as primary persistent selector; absence/replacement follows explicit fallback policy rather than first-device selection.

SPEC-DEC-048
generic PnP discovery/notifications use Configuration Manager/SetupAPI and invalidate hardware snapshots; device notifications do not directly mutate Mode state.

SPEC-DEC-049
v1 power-mode Windows integration is bounded to release-owned active power scheme targets applied/read back through PowrProf; GPU/CPU overclock/voltage controls are outside the baseline.

SPEC-DEC-050
generic process integration is evidence-first and requests minimum practical process rights; PID alone is not durable identity and normal mode switching has no generic `TerminateProcess` behavior.

SPEC-DEC-051
managed Windows service state is applied/read through SCM inside Privileged Broker using release-owned ManagedServiceIds; Runtime Host never supplies arbitrary service names.

SPEC-DEC-052
all Windows context adapters separate desired target, resolved target and fresh actual evidence, using generation invalidation and independent read-back before a mandatory Mode action may verify.

SPEC-DEC-053
Windows integration capability availability is version/release-gated; unsupported APIs/properties fail to explicit fallback/unsupported semantics instead of being assumed present on every Windows 11 build.
```

---

## 3. Verification backlog

### Display

```text
V-WIN-DSP-001 QueryDisplayConfig snapshot returns active paths and target metadata
V-WIN-DSP-002 raw targetId is not persisted as sole profile identity
V-WIN-DSP-003 two identical friendly-name displays produce ambiguity unless stronger selector resolves
V-WIN-DSP-004 SDC_VALIDATE failure prevents apply
V-WIN-DSP-005 normal mode apply does not use SDC_SAVE_TO_DATABASE
V-WIN-DSP-006 SetDisplayConfig success + mismatched read-back returns VERIFICATION_FAILED
V-WIN-DSP-007 exact resolution read-back verified
V-WIN-DSP-008 refresh rational is not truncated to integer
V-WIN-DSP-009 target hot-unplug invalidates generation and stale resolved target
V-WIN-DSP-010 targetAvailable=false cannot satisfy mandatory target
V-WIN-DSP-011 approved display fallback is explicit and journaled
V-WIN-DSP-012 HDR mandatory target rejected when current release capability is unvalidated/unsupported
```

### Audio

```text
V-WIN-AUD-001 render/capture endpoints enumerate through MMDevice
V-WIN-AUD-002 default endpoint read is role-aware
V-WIN-AUD-003 endpoint add/remove/default callbacks invalidate audioGeneration
V-WIN-AUD-004 24H2+ StableId persisted when property exists
V-WIN-AUD-005 legacy endpoint ID loss after simulated driver change does not silently bind same-name device
V-WIN-AUD-006 desired non-default endpoint with no supported setter returns USER_ACTION_REQUIRED/UNSUPPORTED
V-WIN-AUD-007 launching Sound Settings alone does not report APPLIED_VERIFIED
V-WIN-AUD-008 user changes default; subsequent GetDefaultAudioEndpoint match verifies success
V-WIN-AUD-009 no undocumented IPolicyConfig dependency in supported build
```

### Input / hardware

```text
V-WIN-INP-001 SplitOS provisions/loads supported GameInput runtime
V-WIN-INP-002 connected gamepad enumerates and gets stable selector
V-WIN-INP-003 device callback invalidates inputGeneration
V-WIN-INP-004 selected controller disconnect returns TARGET_UNAVAILABLE
V-WIN-INP-005 specific selector never silently chooses first same-model controller
V-WIN-INP-006 approved keyboard/mouse fallback works only when policy permits
V-WIN-INP-007 no synthetic aim/macro input capability in supported contract
V-WIN-HW-001 CM_Register_Notification causes lightweight invalidation + deferred refresh
V-WIN-HW-002 cached hardware projection cannot satisfy required fresh launch check after invalidation
```

### Power / process / services

```text
V-WIN-PWR-001 PowerSetActiveScheme target verified by PowerGetActiveScheme
V-WIN-PWR-002 missing configured scheme returns TARGET_NOT_FOUND/fallback
V-WIN-PWR-003 source scheme can be re-resolved for rollback

V-WIN-PROC-001 process enumeration captures PID/session/image evidence
V-WIN-PROC-002 PID reuse distinguished by creation time when required
V-WIN-PROC-003 process presence never reports unsaved-document proof
V-WIN-PROC-004 normal Work→Game contract contains no generic force terminate

V-WIN-SVC-001 unknown ManagedServiceId rejected before SCM mutation
V-WIN-SVC-002 StartService submission requires QueryServiceStatusEx RUNNING verification
V-WIN-SVC-003 stop control requires STOPPED verification
V-WIN-SVC-004 dependency rejection does not trigger arbitrary recursive service stops
V-WIN-SVC-005 stale mutation fence denied by Broker
```

### Common evidence

```text
V-WIN-EV-001 relevant notification increments adapter generation
V-WIN-EV-002 generation change between resolve/apply returns stale/re-resolve path
V-WIN-EV-003 desired/resolved/actual states remain distinct
V-WIN-EV-004 mandatory verification failure prevents SPEC-05 commit
V-WIN-EV-005 already-satisfied fresh state avoids unnecessary mutation
V-WIN-EV-006 cancellation after technical mutation triggers read-back/reconciliation
V-WIN-EV-007 native Win32/HRESULT is diagnostic and normalized semantic result drives owner logic
V-WIN-EV-008 Windows API result never directly writes OperationalModeState
```

---

## 4. OPEN items intentionally deferred

SPEC-06 does not close:

```text
supported system-wide automatic default audio endpoint setter
exact HDR/advanced-color write structures per final supported Windows build matrix
VRR/G-SYNC/FreeSync vendor-specific controls
GPU vendor power/performance controls
per-application audio routing
DDC/CI monitor controls
Game Client discovery/process correlation
Game Profile device/quality precedence
numeric hardware snapshot TTL/SLA where event invalidation is insufficient
```

These require either later specs or explicit engineering validation.

---

## 5. Next target

```text
SPEC-07 Game Client Adapters
```

SPEC-07 should consume the generic process/device evidence from SPEC-06 but define client-specific discovery, library/install evidence, launch handoff and game-process correlation without promoting client metadata to privileged authority.
