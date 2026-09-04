# SPEC-05 — Mode Policy Contract

## 1. Purpose

Defines how SplitOS represents the desired managed runtime target for BASE, WORK and GAME without embedding raw Windows commands into product policy.

Mode Policy answers:

```text
what SplitOS wants the managed environment to mean
```

It does not directly own:

```text
how Windows APIs achieve it
```

Mechanism-level details belong to SPEC-06 and other integration specifications.

---

## 2. Policy classes

v1 has three runtime policy targets:

```text
BASE
WORK
GAME
```

`WORK` and `GAME` correspond to committed user operational modes.

`BASE` is a neutral managed-runtime target used when:

- FREE runtime is active;
- managed runtime is being deactivated;
- a fresh control session must return to neutral state before mode selection;
- safe fallback needs normal Windows usability without pretending WORK/GAME is active.

BASE is not shown as a third user-selectable mode.

---

## 3. Release-owned policy catalog

Mode policy definitions are release-owned versioned product configuration.

Conceptual installation location:

```text
%ProgramFiles%\SplitOS\Policy\mode-policy.v1.json
```

Exact packaging may change in SPEC-12/implementation, but policy MUST be protected as release content and bound to release provenance.

Policy identity contains:

```text
schemaVersion
policyCatalogId
policyVersion
releaseId
contentDigest
```

Runtime MUST validate policy compatibility before use.

---

## 4. Policy must be declarative

Allowed semantics include references such as:

```text
managed component desired lifecycle
application class lifecycle intent
display context intent
audio context intent
input context intent
power context intent
launcher/runtime readiness requirements
shared-app rules
verification requirements
fallback policy
```

Forbidden policy payloads:

```text
arbitrary PowerShell
arbitrary command line
arbitrary registry path/value
arbitrary Windows service name
arbitrary file delete/write path
arbitrary process kill target
```

Machine references use release-owned typed IDs understood by bounded adapters/Broker catalogs.

---

## 5. Conceptual policy shape

Example semantic shape, not a literal final JSON schema:

```json
{
  "schemaVersion": 1,
  "policyCatalogId": "...",
  "policyVersion": 1,
  "releaseId": "...",
  "targets": {
    "BASE": {},
    "WORK": {},
    "GAME": {}
  }
}
```

Each target may define sections:

```text
applicationLifecycle
managedComponents
display
audio
input
power
runtimeServices
launcher
sharedApps
verification
fallbacks
```

Later specs may normalize these sections into typed code/config objects while preserving the semantic contract.

---

## 6. Effective policy composition

Runtime resolves one immutable effective policy snapshot for each mode operation.

Composition order:

```text
1 mandatory security/platform constraints
2 compatibility constraints
3 release target defaults
4 allowed explicit user preferences/overrides
5 runtime evidence-based target resolution/fallback
```

A lower-priority input cannot override a higher-priority mandatory constraint.

This precedence applies to general mode policy only. Game-profile-specific quality/optimization precedence is finalized in SPEC-08.

---

## 7. User preferences

User preferences may customize only release-defined fields marked user-configurable.

Examples:

- preferred Work display selector;
- preferred Game display selector;
- allowed Shared App behavior;
- selected audio/input preference where supported.

User settings MUST NOT create new machine capability IDs or bypass required verification.

Invalid/stale preference handling:

```text
invalid value
→ reject preference or ignore with explicit diagnostic

stale device selector
→ resolve through approved fallback policy
→ never silently treat stale selector as current device truth
```

---

## 8. Managed component policy

Policy references typed `managedComponentId` values.

Desired lifecycle semantics may include:

```text
ACTIVE
INACTIVE
AVAILABLE
UNCHANGED
```

The mapping from a component ID to concrete service/package/process operations is release-owned by the appropriate Windows integration/Broker catalog.

Mode Policy never emits direct SCM/registry commands.

---

## 9. Application lifecycle policy

Applications are addressed by stable SplitOS application identity/classification, not raw PID.

Possible target semantics:

```text
ALLOW
KEEP_RUNNING
REQUEST_CLOSE
REQUIRE_CLOSED
MOVE_TO_SHARED_PRESENTATION
IGNORE_UNMANAGED
```

Lossy/forced behavior requires a separately authorized policy and cannot be inferred from classification alone.

Exact application detection/control mechanisms belong to SPEC-06 and client/app integrations.

---

## 10. Display/audio/input/power intents

Mode Policy stores desired semantic context, for example:

```text
preferred display selector
required/optional target class
fallback class
preferred audio role/device selector
input requirement/controller-first preference
power profile semantic ID
```

It MUST NOT assume an API call proves application.

Context owner workflow remains:

```text
resolve desired intent
→ apply mechanism
→ read actual state
→ verify
```

Detailed contracts are SPEC-06.

---

## 11. Required vs preferred targets

Every target item that affects commit semantics is classified:

```text
MANDATORY
PREFERRED
OPTIONAL
```

Rules:

```text
MANDATORY unsatisfied
→ target mode cannot normally commit

PREFERRED unsatisfied
→ approved fallback may be used

OPTIONAL failure
→ may continue with diagnostic if safe
```

An approved fallback becomes part of the persisted resolved target policy snapshot for that operation.

---

## 12. Verification requirements

Policy defines semantic verification IDs, not arbitrary scripts.

Examples:

```text
DISPLAY_TARGET_REACHED
INPUT_CONTEXT_USABLE
POWER_POLICY_CONFIRMED
MANAGED_COMPONENT_SET_CONFIRMED
GAME_LAUNCHER_READY
WORK_DESKTOP_USABLE
BASE_MODE_DELTAS_NEUTRALIZED
```

The owning module maps these IDs to typed verification logic.

---

## 13. GAME target minimum

Normal GAME commit requires at least:

```text
managed runtime authorized
Game target policy loaded
required display context verified or approved fallback verified
required input context usable
mandatory managed component policy verified
Game Launcher READY handshake
no unresolved hard blocker
```

Audio may be mandatory or preferred depending on final SPEC-06 supported switching semantics and policy classification.

This prevents the current OPEN for system-wide default-audio switching from blocking the entire policy model prematurely.

---

## 14. WORK target minimum

Normal WORK commit requires at least:

```text
managed runtime authorized
Work target policy loaded
Work display/input usable
mandatory Work managed components verified
required application lifecycle conditions resolved
Windows desktop/work UX reachable
no unresolved hard blocker
```

Pixel-perfect restoration of the previous workspace is not required by v1 unless a later specification adds it.

---

## 15. BASE target minimum

BASE verification focuses on safe normal Windows usability:

```text
SplitOS mode-exclusive machine deltas neutralized/restored
no GAME-only foreground requirement remains
ordinary Windows desktop usable
input usable
no unsafe partial mutation remains
```

BASE does not attempt to recreate a stock Microsoft image. The underlying machine remains the installed SplitOS baseline.

---

## 16. Policy immutability during operation

Once an operation reaches `ACTION_PLAN_READY`, its resolved policy identity and digest are immutable.

If user preferences or hardware evidence changes afterward:

- the operation may re-resolve only through an explicit transition back to `RESOLVING` before target mutation where safe;
- after mutations begin, incompatible target changes trigger rollback/restart of operation rather than silently mutating the plan in place.

Update cannot replace release policy while the mode operation owns the major mutation lease.

---

## 17. Policy compatibility

Runtime rejects policy when:

```text
unknown schema major
policy release incompatible with Runtime
unknown mandatory target ID
unknown mandatory verification ID
invalid signature/digest/provenance
```

Unknown optional fields may be tolerated only through explicit forward-compatible schema rules.

A rejected mandatory policy causes managed target activation to fail closed, while base Windows usability remains preferred.

---

## 18. Policy diagnostics

Resolved policy diagnostics SHOULD expose non-sensitive semantic information:

```text
policyId/version/digest
selected target
user overrides applied
fallbacks selected
mandatory/preferred items
resolution warnings
```

Raw secrets or arbitrary external payloads are excluded.

---

## 19. Acceptance criteria

- BASE/WORK/GAME policy targets are explicit;
- BASE is not a third user mode;
- policy is versioned and release-owned;
- policy contains no arbitrary admin commands;
- machine targets use typed IDs;
- user customization is bounded by release schema;
- mandatory/preferred/optional semantics are explicit;
- fallback choice becomes part of resolved snapshot;
- policy is immutable across apply/verify/commit;
- GAME/WORK/BASE have minimum semantic targets;
- unsupported audio switching can be modeled without inventing a fake supported mechanism.
