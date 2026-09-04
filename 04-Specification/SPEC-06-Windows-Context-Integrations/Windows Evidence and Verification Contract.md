# SPEC-06 — Windows Evidence and Verification Contract

## 1. Purpose

Defines the common evidence, invalidation, apply/read-back and verification semantics shared by all Windows context adapters.

The goal is to make the following impossible by design:

```text
API returned success
→ assume SplitOS target is true
```

Instead:

```text
desired target
→ supported mechanism
→ operation result
→ fresh evidence
→ verification
→ semantic owner decision
```

---

## 2. Evidence categories

Windows integration returns evidence in categories:

```text
CURRENT_STATE
CAPABILITY
IDENTITY
AVAILABILITY
OPERATION_RESULT
NOTIFICATION
```

Each category proves only its bounded meaning.

---

## 3. Common evidence metadata

All evidence records include when applicable:

```text
evidenceId
sourceAdapter
sourceApiFamily
observedUtc
snapshotGeneration
Windows build/release context
availability
provider/native status code for diagnostics
```

Persistent projections may additionally carry:

```text
expiresUtc
sourceVersion
confidenceCode
```

but projection metadata cannot upgrade stale evidence into current truth.

---

## 4. Snapshot generation

Each adapter family maintains a monotonic in-memory generation.

Relevant notification/event:

```text
→ generation++
→ currently resolved target marked stale
→ consumers notified
```

Generation is process-local evidence identity, not a durable global sequence across reboots.

After Runtime Host restart, all live Windows contexts are freshly enumerated.

---

## 5. Required refresh points

A fresh read is mandatory:

```text
before resolving a MANDATORY target
before applying if target generation was invalidated
immediately after apply for verification
before final mode verification if a relevant device changed
before managed game launch hardware/profile finalization
```

A projection cache may accelerate discovery but cannot replace these reads.

---

## 6. Desired state vs resolved target vs actual state

These are separate data objects:

```text
Desired Policy Target
        ↓ resolution
Resolved Windows Target
        ↓ apply
Actual Windows Evidence
```

Example:

```text
Desired:
TV, 4K, >=60Hz

Resolved:
monitor selector X → current adapterLuid/targetId Y, 3840x2160, 120000/1000Hz

Actual after apply:
monitor Y active, 3840x2160, 60000/1000Hz
```

Whether the last result passes depends on the policy requirement/fallback that was resolved for the operation.

---

## 7. Resolved-target immutability

Once SPEC-05 action execution begins, the resolved target/fallback set and its digest are immutable for that action attempt.

If hardware changes invalidate it:

```text
old target is not silently rewritten
→ action returns STALE_SNAPSHOT/TARGET_UNAVAILABLE
→ Mode owner may return to policy resolution and create an explicit new resolved action/fallback
```

This preserves auditability and crash reconciliation.

---

## 8. Apply semantics

Adapters classify apply return as:

```text
NOT_SUBMITTED
SUBMITTED
REJECTED
TECHNICAL_SUCCESS
TECHNICAL_FAILURE
```

These are not the final context result.

The final adapter result is produced only after read-back where the capability is verifiable.

---

## 9. Verification predicates

Verification uses typed predicates, for example:

```text
DISPLAY_TARGET_PRESENT(selector)
DISPLAY_RESOLUTION_EQUALS(w,h)
DISPLAY_REFRESH_SATISFIES(requirement)
DISPLAY_TOPOLOGY_SATISFIES(intent)

AUDIO_DEFAULT_EQUALS(selector, flow, role)
AUDIO_ENDPOINT_AVAILABLE(selector)

INPUT_DEVICE_PRESENT(selector)
INPUT_KIND_AVAILABLE(kind)

POWER_SCHEME_EQUALS(policyTarget)

PROCESS_PRESENT(applicationId)
PROCESS_ABSENT(applicationId)

SERVICE_STATE_EQUALS(managedServiceId, RUNNING|STOPPED)
```

Predicates are data/contract identifiers, not executable expressions.

---

## 10. Mandatory vs preferred verification

SPEC-05 policy item severity controls consequence:

```text
MANDATORY predicate fails
→ target mode cannot commit

PREFERRED predicate fails
→ approved fallback may be resolved/applied
→ otherwise operation may continue only if policy explicitly permits degraded result

OPTIONAL predicate fails
→ diagnostic/degraded feature result
→ does not alone block mode commit
```

Adapter does not decide item severity.

---

## 11. Verification race rule

If a relevant generation changes while verification is running:

```text
verification result = STALE
```

unless the adapter's fresh read itself already represents the new generation and the full predicate set is re-evaluated against it.

SplitOS MUST NOT combine fields from mutually inconsistent snapshots into one synthetic success.

---

## 12. Idempotent target semantics

Where Windows mechanism permits it, adapters SHOULD make reapplying an already-satisfied target a no-op:

```text
fresh read proves target
→ ALREADY_SATISFIED
→ no unnecessary mutation
```

This reduces disruption during crash reconciliation.

However `ALREADY_SATISFIED` still requires a sufficiently fresh read.

---

## 13. Deadlines and cancellation

All mutating/verification operations run under the SPEC-05 operation deadline/cancellation context.

Rules:

- no unbounded service wait loop;
- no unbounded device wait inside adapter;
- cancellation before technical submission may stop the action;
- cancellation after Windows accepted a mutation requires read-back/reconciliation;
- caller cancellation never fabricates rollback success.

---

## 14. Error classification

Native Windows error codes are retained for diagnostics but normalized upward into:

```text
UNSUPPORTED_CAPABILITY
VERSION_UNSUPPORTED
TARGET_NOT_FOUND
TARGET_AMBIGUOUS
TARGET_UNAVAILABLE
PERMISSION_DENIED
OPERATION_REJECTED
STALE_SNAPSHOT
TECHNICAL_FAILURE
VERIFICATION_FAILED
USER_ACTION_REQUIRED
CANCELLED
```

User UX SHOULD receive semantic error codes/messages rather than raw HRESULT/Win32 values.

---

## 15. Trust handling

Windows APIs are platform authority for the specific platform facts they return, within documented semantics.

They are not semantic owner for SplitOS state.

For example:

```text
QueryServiceStatusEx: SERVICE_RUNNING
→ service state evidence
→ not proof that Work Mode may commit

QueryDisplayConfig: TV targetAvailable=false
→ target availability evidence
→ not instruction to switch to another monitor
```

---

## 16. Diagnostic correlation

Every apply/read-back/verify chain emits operation-scoped diagnostic events carrying:

```text
correlationId
operationId
modeTransitionId where applicable
actionId
adapter
resolvedTargetId
snapshot generation before/after
normalized result
native status code when safe
elapsed duration
```

Secrets/user document titles/raw sensitive content MUST NOT be added merely for debugging.

Exact retention/export belongs to SPEC-13.

---

## 17. Acceptance criteria

- desired/resolved/actual states are distinct objects;
- adapter generations invalidate stale targets;
- every mandatory mutation has fresh read-back;
- verification uses typed predicates rather than arbitrary expressions;
- severity/fallback remains Mode Policy-owned;
- native errors are normalized without losing diagnostic correlation;
- cancellation after mutation triggers reconciliation rather than assumed rollback;
- adapters avoid unnecessary mutation when fresh actual state already satisfies target;
- Windows evidence never directly writes `OperationalModeState`.
