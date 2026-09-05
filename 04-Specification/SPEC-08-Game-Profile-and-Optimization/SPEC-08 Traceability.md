# SPEC-08 — Traceability

## 1. Source mapping

| SPEC-08 decision | Primary source |
|---|---|
| one Game may have multiple scenario profiles | A&D Domain Model / Configuration Model |
| Desktop/TV profiles remain separate canonical configurations | Concept / Game Profile product behavior |
| desired/recommended/effective/actual are separate | A&D Configuration Model / Ownership / SPEC-06 evidence model |
| device preference persists independently of temporary device absence | Configuration Model + SPEC-06 persistent selectors |
| explicit user override outranks optimizer recommendation when technically valid | Configuration Model user override semantics |
| hard constraints still outrank impossible user intent | Configuration Model precedence |
| profile re-evaluated against fresh hardware/display/input context before launch | Game Profile concept + SPEC-06 generation model + Game Launch flow |
| target quality subject to stable performance | product concept / Game Optimization Policy responsibility |
| game setting writes require game-specific typed mechanism | Interfaces/Integrations + Trust + SPEC-07 adapter isolation pattern |
| performance result requires actual evidence, not assumed hardware capability | Ownership/Interfaces/Flows/Failures |
| process binding for telemetry uses SPEC-07 proof set | SPEC-07 launch/session correlation |
| recommendation may be invalidated by hardware/game/rules change without deleting profile | Data lifecycle/config versioning |
| external game config drift is evidence, not automatically user intent | Data ownership + external evidence trust |

---

## 2. Specification decisions

```text
SPEC-DEC-071
GameProfile v1 is a canonical per-user scenario object with DESKTOP, LIVING_ROOM or CUSTOM scenario hint; scenario type is not hardware identity.

SPEC-DEC-072
profile device bindings use persistent SPEC-06 selectors; transient CCD target IDs, USB order and similar runtime handles are never canonical profile identity.

SPEC-DEC-073
v1 profile graphics management mode is AUTO, SUGGEST_ONLY or USER_MANAGED.

SPEC-DEC-074
performance intent supports QUALITY_PRIORITY, BALANCED, PERFORMANCE_PRIORITY and CUSTOM plus explicit DISPLAY_REFRESH_BOUND, FIXED_TARGET or MAX_STABLE frame-rate policy.

SPEC-DEC-075
field-level LOCKED user overrides are canonical user intent. Optimizer may change other AUTO fields but cannot silently remove a valid lock to satisfy a target.

SPEC-DEC-076
unsupported/impossible locked value is preserved as SUSPENDED_CONFLICT rather than deleted or fabricated into effective configuration.

SPEC-DEC-077
optimization precedence is hard constraints > valid user locks > explicit profile intent > current optimizer recommendation > release safe/default knowledge > unmanaged game default.

SPEC-DEC-078
profile auto-selection uses deterministic eligibility/match ordering rather than opaque weighted scoring; material ambiguity requires user choice.

SPEC-DEC-079
resolved profile context is immutable for one launch and binds profile revision plus relevant hardware/display/input/audio snapshot generations. Generation invalidation requires re-resolution.

SPEC-DEC-080
OptimizationRecommendation is immutable derived product data bound to profile revision, game knowledge version, hardware fingerprint and context. It never replaces GameProfile intent.

SPEC-DEC-081
v1 optimizer objective is lexicographic: compatibility/safety, preserve valid locks, satisfy scenario, meet stable target if feasible, maximize quality, respect latency/pacing policy, minimize churn.

SPEC-DEC-082
v1 game optimization is driven by release-owned per-game setting definitions/dependencies/degradation and upgrade ladders; no arbitrary setting/file mapping is accepted from user/profile data.

SPEC-DEC-083
normal AUTO game-setting mutation occurs while game is not running and requires typed validation, safe write and read-back verification. Runtime setting writes are per-game opt-in capability only.

SPEC-DEC-084
normal gameplay is not continuously auto-tuned. Measured telemetry refines future recommendation or explicit calibration; mid-session mutation is disabled unless a game-setting adapter explicitly supports it.

SPEC-DEC-085
performance target evaluation uses frame-time distribution/stability evidence, not arithmetic average FPS alone.

SPEC-DEC-086
PresentMon-compatible telemetry is the primary v1 implementation candidate for frame/present measurement; exact service vs embedded integration remains an engineering validation gate.

SPEC-DEC-087
external supported game-setting changes are preserved for the immediate launch and recorded as drift; they do not automatically become canonical user overrides and are reconciled explicitly.

SPEC-DEC-088
vendor driver profile/tuning APIs are optional future capabilities. Core v1 optimization does not require overclocking, undervolting, fan control or implicit NVIDIA/AMD driver-profile mutation.
```

---

## 3. Verification backlog

### Profile

```text
V-PROFILE-001 multiple profiles per game persist independently
V-PROFILE-002 Desktop and living-room profile select different device/performance intent
V-PROFILE-003 profile survives preferred display disconnect/reconnect
V-PROFILE-004 transient display target ID never becomes persistent selector
V-PROFILE-005 default profile still undergoes eligibility check
V-PROFILE-006 stale profile revision blocks stale recommendation/apply
V-PROFILE-007 clone creates new profile identity without copying runtime evidence as truth
```

### Selection / hardware

```text
V-SEL-001 explicit eligible user profile wins
V-SEL-002 required missing display makes profile ineligible
V-SEL-003 two ambiguous matching displays require user resolution
V-SEL-004 approved 120→60 fallback becomes immutable resolved launch context
V-SEL-005 controller disconnect invalidates dependent resolved context
V-SEL-006 material generation change before apply causes re-resolution
V-SEL-007 last-used history does not overwrite user default
```

### Overrides

```text
V-OVR-001 valid LOCKED setting survives recommendation refresh
V-OVR-002 optimizer adjusts other AUTO fields around lock
V-OVR-003 unmet target with locks is reported, not silently fixed by deleting lock
V-OVR-004 unsupported lock becomes SUSPENDED_CONFLICT and remains visible
V-OVR-005 reset-to-auto clears lock explicitly
V-OVR-006 external config change does not silently become canonical lock
V-OVR-007 game safe-mode reset does not permanently overwrite profile intent
```

### Recommendation

```text
V-OPT-001 identical static inputs produce identical recommendation
V-OPT-002 recommendation carries profile revision/knowledge version/hardware fingerprint
V-OPT-003 recommendation becomes stale after relevant GPU/display/game/rules change
V-OPT-004 irrelevant controller reconnect does not invalidate graphics recommendation by default
V-OPT-005 degradation ladder skips locked settings
V-OPT-006 no feasible target returns explicit unmet result
V-OPT-007 high-refresh desktop and 4K TV produce materially different recommendation where expected
V-OPT-008 no implicit overclock/vendor driver mutation is required
```

### Game config adapter

```text
V-GCFG-001 unsupported game/config version prevents AUTO write
V-GCFG-002 arbitrary profile-supplied config path rejected
V-GCFG-003 stale source digest returns SOURCE_CHANGED
V-GCFG-004 supported write preserves unknown/unmanaged fields
V-GCFG-005 running-game write denied without explicit capability
V-GCFG-006 read-back mismatch prevents APPLIED_VERIFIED
V-GCFG-007 game update may downgrade AUTO write to SUGGEST_ONLY without breaking launch
V-GCFG-008 accessibility/control settings outside managed set remain unchanged
```

### Telemetry

```text
V-PERF-001 telemetry attaches only to SPEC-07 correlated game process/session
V-PERF-002 too-short/menu/idle sample cannot become representative automatically
V-PERF-003 material display/config/context change invalidates or segments sample
V-PERF-004 average FPS alone cannot satisfy stability policy
V-PERF-005 frame cap matching profile target is not misclassified as refresh failure
V-PERF-006 missing telemetry does not block ordinary launch
V-PERF-007 provider missing metric remains unavailable, not zero
V-PERF-008 performance measurement overhead stays within SPEC-14 budget
V-PERF-009 anti-cheat compatibility matrix validates non-injection measurement path
```

### Drift

```text
V-DRIFT-001 external managed-field change is preserved for immediate launch and flagged
V-DRIFT-002 KEEP_AS_OVERRIDE creates explicit LOCKED intent
V-DRIFT-003 RESTORE recommendation reapplies only against current source digest
V-DRIFT-004 repeated game self-reset can downgrade AUTO capability
V-DRIFT-005 temporary display fallback does not rewrite canonical TV profile
```

---

## 4. Research basis

Current external engineering evidence used for SPEC-08:

```text
Intel PresentMon
→ multi-vendor Windows frame/present metrics
→ frame time, displayed/presented FPS, CPU/GPU busy, dropped/display evidence
→ open-source MIT project

NVIDIA NVAPI DRS
→ documented Windows driver profile/settings API exists

AMD ADLX
→ documented 3D graphics/performance/tuning interfaces exist
```

SPEC-08 deliberately keeps NVAPI/ADLX driver mutation outside mandatory core v1 because the product objective can be implemented through game settings + Windows context + measured evidence without silently taking ownership of vendor driver state.

---

## 5. OPEN items intentionally deferred

```text
exact PresentMon packaging topology (service vs embedded/analysis) → engineering prototype + SPEC-14
numeric performance stability thresholds / sample durations → SPEC-14 measured verification
per-game supported setting catalogs → implementation knowledge packs governed by SPEC-08
account/cloud synchronization of GameProfiles → future product/backend specification
vendor driver optimization adapters → optional future extension after compatibility/safety validation
live runtime game-setting mutation → only per-game future capability
```

---

## 6. Next target

```text
SPEC-09 Game Launcher & Shared Apps UX
```

SPEC-09 consumes the profile-selection/recommendation states defined here and presents them through controller-first Game Mode UX without becoming the canonical owner of profile/runtime truth.
