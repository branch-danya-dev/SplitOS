# SPEC-08 — Optimization Recommendation Engine

## 1. Purpose

Defines how SplitOS computes an explainable game-setting recommendation from:

```text
GameProfile intent
+ current hardware/display context
+ release-owned game knowledge
+ explicit user locks
+ optional measured performance evidence
```

The engine produces a recommendation. It does not own game configuration truth, hardware truth or user intent.

---

## 2. Optimization objective

Canonical product objective:

> maximize visual quality subject to stable performance appropriate to the selected hardware/display scenario and explicit user intent.

This is implemented as a lexicographic objective rather than one opaque score:

```text
1. satisfy hard compatibility/safety constraints
2. preserve valid explicit user locks
3. satisfy required profile/device scenario
4. meet stable performance target when feasible
5. maximize visual-quality preference within the remaining feasible set
6. prefer lower latency / better pacing according to profile intent
7. minimize unnecessary setting churn
```

If step 4 is impossible without violating steps 1–3, engine returns an explicit unmet-target result.

---

## 3. Inputs

```text
profileId + profileRevision
ResolvedProfileContext
GameKnowledgeVersion
GameConfigAdapter capability state
HardwareOptimizationFingerprint
current game version / config schema evidence where available
current managed settings read-back
UserOverrideSet
previous valid recommendation?
PerformanceObservationSet?
```

All inputs used by the result are recorded by identity/version in the recommendation.

---

## 4. Recommendation object

```text
OptimizationRecommendation
├── recommendationId
├── gameId
├── profileId
├── profileRevision
├── createdUtc
├── status
├── knowledgeVersion
├── hardwareFingerprint
├── resolvedDisplayContext
├── targetPerformanceContract
├── assignments[]
├── preservedUserLocks[]
├── unresolvedConflicts[]
├── expectedTradeoffs[]
├── evidenceReferences[]
└── rationale[]
```

Status:

```text
RECOMMENDED_STATIC
RECOMMENDED_MEASURED
TARGET_UNMET_WITH_USER_LOCKS
TARGET_UNMET_HARDWARE_LIMIT
INSUFFICIENT_GAME_KNOWLEDGE
INCOMPATIBLE_GAME_VERSION
STALE
```

Recommendation is immutable after creation.

A new evaluation creates a new `recommendationId`.

---

## 5. Game optimization knowledge

Optimization does not infer arbitrary game settings from names in config files.

Each supported game/version family has release-owned `GameOptimizationKnowledge`:

```text
GameOptimizationKnowledge
├── knowledgeVersion
├── supported game/config versions
├── setting definitions
├── legal values
├── dependencies
├── incompatibilities
├── quality ordering
├── performance-cost ordering/rules
├── feature prerequisites
├── restart/apply requirements
├── degradation ladder
├── upgrade ladder
└── telemetry interpretation rules where needed
```

This knowledge is signed/release-owned product data and MUST be versioned.

---

## 6. Setting definition

For each managed semantic `GameSettingKey`:

```text
GameSettingDefinition
├── key
├── data type
├── legal values/range
├── default management mode
├── quality rank/ordering
├── estimated performance-cost class
├── dependencies
├── incompatible combinations
├── hardware/feature requirements
├── apply phase
├── requires game restart?
├── read-back capability
└── adapter mapping reference
```

Estimated cost classes are ordinal/explainable, for example:

```text
VERY_HIGH
HIGH
MEDIUM
LOW
NEGLIGIBLE
CONTEXT_DEPENDENT
```

They are not claimed as universal FPS percentages.

---

## 7. Resolution and render target first

Recommendation MUST resolve the rendering target before most quality settings because resolution materially changes performance budget.

Canonical order:

```text
resolved display
→ effective display mode
→ render-resolution intent
→ render scale / upscaling capability
→ frame-rate target contract
→ quality settings
```

Example:

```text
Desktop profile
2560x1440 @ 280 Hz

TV profile
3840x2160 @ 120 Hz
```

may produce very different recommendations for the same GPU and game.

---

## 8. Target performance contract

The optimizer creates an explicit per-evaluation contract:

```text
TargetPerformanceContract
├── targetType
├── preferredFps
├── acceptableFloor
├── displayRefreshBound
├── framePacingPolicy
├── latencyPolicy
├── samplePolicyId
└── qualityFloorRules[]
```

Exact numeric defaults belong to release/game/profile policy and later verification evidence; they MUST NOT be hidden constants scattered through implementation.

---

## 9. Static recommendation pass

Static recommendation is always the first supported path.

```text
validate game knowledge
→ validate current profile/hardware context
→ apply hard constraints
→ apply user locks
→ choose base render target
→ choose initial legal AUTO values
→ resolve setting dependencies
→ produce deterministic recommendation
```

The same inputs and same rule-set version MUST produce the same static recommendation.

---

## 10. Degradation ladder

When expected or measured performance is below target, settings are degraded only according to game-specific release-owned order.

Example conceptual ladder:

```text
1. reduce expensive ray-tracing tier
2. reduce reflection/shadow tier
3. adjust crowd/geometry cost
4. change upscaling quality/render scale within allowed quality floor
5. lower resolution only if profile policy permits
6. lower effective frame-rate target when remaining quality floor/user locks prohibit further degradation
```

This is an example only. Exact order is per game knowledge.

A locked field is skipped.

If every remaining legal degradation is blocked:

```text
TARGET_UNMET_WITH_USER_LOCKS
or
TARGET_UNMET_HARDWARE_LIMIT
```

---

## 11. Upgrade ladder

When measured/known headroom is sufficient, quality may increase according to the inverse/explicit game-specific upgrade ladder.

Rules:

- no locked value is changed;
- no setting exceeds hardware/feature constraint;
- upgrade must preserve required performance margin policy;
- recommendation avoids oscillating adjacent settings from small sample noise.

---

## 12. Stability, not average FPS only

Optimization MUST NOT classify performance solely from arithmetic average FPS.

Performance evidence SHOULD represent at least:

```text
frame-time distribution
low-percentile / tail behavior
presented/displayed FPS where available
dropped/discarded frame evidence where available
GPU busy / CPU busy context where available
sample duration and validity
```

The exact threshold policy is versioned and must later be verified in SPEC-14.

---

## 13. Measured refinement

Measured refinement is optional capability on top of static recommendation.

Canonical v1 behavior:

```text
static recommendation
→ apply before launch
→ game session runs
→ collect valid telemetry where enabled
→ produce PerformanceObservation
→ after enough trusted evidence, calculate refined recommendation
→ apply on next launch / explicit Optimize action
```

Default v1 MUST NOT repeatedly mutate graphics settings in the middle of normal gameplay.

Reason:

- many settings require restart;
- menus/cutscenes are not representative;
- unexpected visual changes are poor UX;
- config writes while game runs may race the game;
- game-specific runtime-setting safety differs.

Future per-game live tuning requires explicit adapter capability.

---

## 14. Calibration mode

Manager/Game Launcher MAY expose a deliberate `Optimize`/calibration workflow.

Concept:

```text
user requests Optimize
→ known safe candidate applied
→ launch game
→ collect representative sample(s)
→ evaluate target satisfaction
→ if needed propose/apply next candidate on subsequent controlled run
→ converge recommendation
```

The system MUST tell the user when representative gameplay input is required.

SplitOS MUST NOT pretend that a main-menu sample proves real gameplay performance.

---

## 15. Measurement confidence

Performance observations have:

```text
VALID_REPRESENTATIVE
VALID_LOW_CONFIDENCE
INVALID_MENU_OR_IDLE
INVALID_TOO_SHORT
INVALID_PROCESS_MISMATCH
INVALID_CONTEXT_CHANGED
INVALID_TELEMETRY_ERROR
```

Low-confidence/invalid evidence cannot automatically authorize major quality degradation as though it were strong proof.

---

## 16. Bottleneck interpretation

Where telemetry exposes CPU/GPU busy timing, optimizer MAY classify:

```text
GPU_BOUND_LIKELY
CPU_BOUND_LIKELY
MIXED
UNKNOWN
```

This is recommendation evidence, not eternal hardware truth.

Example:

```text
GPU-bound
→ reducing GPU-heavy effects/render scale may help

CPU-bound
→ dropping texture quality may be pointless
→ game-specific CPU-heavy settings become preferred candidates
```

No universal setting-to-bottleneck mapping is assumed without game knowledge.

---

## 17. Frame generation/upscaling

Features such as DLSS/FSR/XeSS/frame generation are represented as game setting capabilities, not vendor marketing strings embedded in the generic engine.

The game knowledge declares:

```text
feature supported?
hardware/API requirements
legal modes
quality implications
latency implications
incompatible combinations
read/write mechanism
```

Frame generation MUST NOT be treated as identical to native rendered FPS when evaluating latency/frame-generation-specific targets.

Telemetry/UX must distinguish where evidence allows.

---

## 18. Driver-level tuning

Vendor driver settings are NOT required for core v1 optimization.

Potential future adapters include documented vendor mechanisms such as:

```text
NVIDIA NVAPI DRS
AMD ADLX 3D Settings
```

Such adapters MUST be capability-gated, version-tested and independently reversible.

Core optimization MUST work without overclocking, undervolting, fan tuning or arbitrary vendor-profile mutation.

Forbidden implicit behavior:

```text
"Performance Priority"
→ silently overclock GPU
```

---

## 19. Recommendation explanation

Every managed changed field SHOULD have a rationale code, e.g.:

```text
TARGET_GPU_COST_REDUCTION
DISPLAY_RESOLUTION_CONSTRAINT
USER_LOCK_PRESERVED
VRAM_CONSTRAINT
FEATURE_UNSUPPORTED
QUALITY_HEADROOM_UPGRADE
FRAME_PACING_STABILITY
GAME_VERSION_COMPATIBILITY
```

Manager can translate these into user-facing explanations.

The optimizer must not be an uninspectable black box.

---

## 20. Recommendation persistence

Recommendation may be cached with:

```text
recommendationId
profileId/profileRevision
knowledgeVersion
hardwareFingerprint
createdUtc
status
assignment digest
observation references
```

It is derived product data, not canonical user intent.

Deleting recommendation cache MUST NOT delete GameProfile/user locks.

---

## 21. Invalidation

Recommendation becomes `STALE` on relevant change:

```text
profile revision
user lock set
selected display/resolution/refresh class
GPU/VRAM/CPU capability fingerprint
supported game version/config schema
optimization knowledge version
material driver compatibility bucket
```

A stale recommendation cannot be automatically applied as if current.

---

## 22. Determinism and reproducibility

Diagnostic reproduction requires:

```text
profile revision
resolved context identities
game knowledge version
recommendation algorithm version
input hardware fingerprint
observation IDs used
output assignment digest
```

Given identical inputs and no measured stochastic step, output must be deterministic.

---

## 23. Failure results

```text
GAME_KNOWLEDGE_MISSING
GAME_VERSION_UNSUPPORTED
CONFIG_ADAPTER_READ_UNAVAILABLE
INVALID_PROFILE_INTENT
HARDWARE_CONTEXT_STALE
USER_OVERRIDE_CONFLICT
NO_FEASIBLE_CONFIGURATION
TELEMETRY_UNAVAILABLE
TELEMETRY_INVALID
TARGET_UNMET_WITH_USER_LOCKS
TARGET_UNMET_HARDWARE_LIMIT
```

Failure to optimize does not imply failure to launch when profile policy allows `USER_MANAGED`/safe game defaults.

---

## 24. Acceptance criteria

- recommendation is deterministic from versioned inputs;
- explicit valid user locks are never silently changed;
- impossible locks are surfaced as conflict;
- average FPS alone is not the stability criterion;
- 1440p/high-refresh and 4K living-room profiles can resolve differently on same hardware;
- optimizer can report target unmet without destroying quality floor/user locks;
- measured evidence refines later recommendations rather than mutating normal gameplay by default;
- recommendation cache can be rebuilt without losing canonical profile;
- vendor driver tuning/overclock is not required for core optimization.
