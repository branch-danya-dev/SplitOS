# SPEC-14 — Fault Injection and Recovery Verification

## 1. Purpose

Defines mandatory fault-injection verification for state-mutating SplitOS flows.

Fault verification proves that interruption, ambiguity and partial mutation converge to a safe state rather than producing guessed canonical truth.

Core invariant:

```text
fault
→ durable evidence + actual-state read
→ decision owner
→ resume / rollback / recovery / safe fallback
→ verification
→ coherent result
```

---

# 2. Fault classes

```text
PROCESS_CRASH
SERVICE_CRASH
POWER_LOSS_EQUIVALENT
REBOOT
STORAGE_FAILURE
CORRUPTION
DEVICE_LOSS
NETWORK_LOSS
EXTERNAL_CLIENT_FAILURE
STALE_OWNER
CLOCK_ANOMALY
ARTIFACT_TAMPER
RECOVERY_FAILURE
```

A fault test asserts convergence, not merely that an exception was thrown.

---

# 3. Mandatory mode-transition kill points

For `WORK → GAME` and `GAME → WORK`, inject Runtime Host termination at least after:

```text
REQUESTED persisted
INSPECTING
blocker resolution
resolved target created
first machine mutation applied
multiple mutations applied
VERIFYING started
mandatory verification completed
COMMITTING before durable commit
immediately after durable commit
before terminal COMPLETED marker
```

Expected rules:

### Before durable commit

```text
source committed mode remains canonical
```

After restart:

- read durable transition record;
- read actual Windows state;
- do not infer target from partial mutations;
- rollback/reconcile to source or safe fallback;
- escalate to Recovery if rollback cannot verify.

### After durable commit

```text
target committed mode is canonical
```

After restart:

- reconcile actual state to committed target or recover;
- do not revert canonical mode merely because terminal `COMPLETED` event was not written.

---

# 4. Broker crash verification

## FI-BROKER-001 — Broker unavailable before mutation

Expected:

```text
privileged operation unavailable
→ operation fails/degrades
→ no false target commit
→ Windows remains usable
```

## FI-BROKER-002 — Broker crashes after one privileged mutation

Expected:

- Runtime observes loss/time-out;
- current transition does not continue as success;
- actual Windows state is re-read after Broker recovery;
- rollback/recovery follows transition policy.

## FI-BROKER-003 — Broker restart invalidates stale caller state

A restarted/superseded mutation owner must use current lease/fence generation.

Old process/request replay is denied.

---

# 5. Mutation lease fault verification

## FI-LEASE-001 — Lease expiration with dead owner

Kill owner without release.

A new owner may acquire only after defined stale-owner handling/reconciliation.

## FI-LEASE-002 — Zombie owner resumes

Old process with fence N resumes after new owner has N+1.

Expected:

```text
Broker/StateStore rejects N
no machine mutation from stale owner
```

## FI-LEASE-003 — Update and Mode contention

Hold MODE lease and request Update; then reverse.

Expected no blind interleaving of major mutations.

---

# 6. Device loss

## FI-DEVICE-001 — Target display disappears before apply

Resolve target at generation N, remove display, then apply.

Expected stale generation rejection/re-resolution.

## FI-DEVICE-002 — Display disappears during transition

After one or more mutations, disconnect target display.

Expected:

```text
refresh evidence
→ if allowed fallback exists: resolve + verify fallback
→ otherwise abort target and rollback/safe fallback
```

No GAME commit based on stale target.

## FI-DEVICE-003 — Controller disconnect before game launch

If profile requires exact controller and no allowed fallback exists, launch preparation blocks/fails according to profile policy; it must not silently bind an arbitrary controller.

## FI-DEVICE-004 — Secondary display disappears with Shared App

Assignment intent remains; current presentation degrades/unavailable without rewriting canonical preference.

---

# 7. Game-client faults

## FI-GAME-001 — Handoff accepted, game never starts

Expected timeout/absence of strong evidence leads to launch failure and return to Launcher; committed GAME mode remains.

## FI-GAME-002 — Auth required

Client presents login requirement.

Expected controlled `AUTH_REQUIRED`; no corruption of Game Session or mode.

## FI-GAME-003 — Client crashes after handoff

Expected no `GAME_RUNNING` unless independent game evidence exists.

## FI-GAME-004 — Runtime Host crashes while game already running

After Runtime restart:

```text
read existing session context
→ fresh process evidence
→ reattach/reconcile
```

Do not relaunch game just because Runtime lost in-memory state.

## FI-GAME-005 — Game process crashes

Expected Game Session exits/returns Launcher while committed GAME remains if system context is coherent.

---

# 8. Persistence faults

## FI-STORAGE-001 — Disk full during canonical commit

Force durable write failure.

Expected:

```text
required commit not durable
→ semantic success cannot be reported
```

No cleanup deletes canonical DB or Recovery Capsule to preserve diagnostics.

## FI-STORAGE-002 — Projection DB corruption

Expected projection store is quarantined/rebuilt; canonical mode/profile/account state preserved.

## FI-STORAGE-003 — User DB corruption

Verify documented corruption handling/backups/manual recovery path. Product must not silently replace corrupted canonical user data with empty defaults while claiming success.

## FI-STORAGE-004 — Machine DB corruption

Expected machine canonical state enters recovery/repair handling. Diagnostic logs cannot be used as automatic canonical replacement.

---

# 9. Update fault matrix

Inject faults at least at:

```text
release metadata refresh
artifact download
artifact verification
staging
Recovery Capsule creation
Recovery Capsule verification
READY_TO_APPLY
first target activation mutation
Broker replacement
Runtime replacement
before reboot
expected reboot
post-reboot resume
VERIFYING_TARGET
COMMITTING before durable release commit
after durable release commit
cleanup
```

---

## 9.1 Before target release commit

Expected:

```text
source release remains canonical
```

Recovery engine may resume target only if transaction semantics permit and evidence is unambiguous; otherwise restore source capsule/safe source state.

---

## 9.2 After target release commit

Expected:

```text
target release remains canonical
```

A subsequent fault is handled as target repair/recovery transaction, not by pretending update never committed.

---

# 10. Power-loss-equivalent verification

At required mutation checkpoints, test using an environment capable of removing process/VM power without graceful SplitOS shutdown.

A normal app close is not sufficient to simulate power loss.

Minimum assertions after restart:

```text
Windows bootability
canonical DB integrity or explicit recovery detection
transaction identity retained where durable
commit state correctly interpreted
actual machine state re-read
no guessed mode/release
user data retained
recovery path available
```

Physical-hardware power interruption may be required for release qualification of storage/recovery assumptions that cannot be proven in VM-only testing.

---

# 11. Recovery Capsule faults

## FI-REC-001 — Capsule missing

Update activation must not start when previous-release capsule is mandatory and unavailable.

## FI-REC-002 — Capsule hash corruption before update

Activation blocked.

## FI-REC-003 — Capsule corruption discovered during recovery

Recovery must not apply known-invalid capsule. It escalates to available Windows/native/manual recovery path while preserving user data priority.

## FI-REC-004 — Recovery operation interrupted

Recovery itself is journaled/verified. Interruption triggers another reconciliation; it does not assume recovery target became valid.

## FI-REC-005 — Recovery verification fails

System must not report recovered. If Windows is usable, managed SplitOS may remain disabled/degraded and manual repair is surfaced.

---

# 12. Network/backend faults

## FI-NET-001 — Account backend loss during normal Windows use

Windows sign-in/Desktop remain usable.

## FI-NET-002 — Backend loss during checkout return

Callback triggers refresh, refresh fails, no false PRO.

## FI-NET-003 — Update CDN unavailable

No staged/verified target means no mutation. Current release remains usable.

## FI-NET-004 — Network drops after verified target staging

If update no longer needs network and all required signed artifacts are local, execution may continue according to SPEC-11; otherwise fail before unsafe mutation.

---

# 13. Clock faults

Test:

```text
clock moved backward
clock moved forward materially
RTC reset / invalid time where reproducible
```

Expected:

- offline entitlement does not gain extra validity;
- TUF expiration/trusted time logic does not accept stale metadata due rollback;
- local diagnostic ordering records both occurrence/observation context without becoming authority.

---

# 14. Fault-injection evidence

Each fault execution records:

```text
faultCaseId
faultInjectionPoint
candidateId
transaction/correlation IDs
pre-fault canonical state
pre-fault actual evidence
injected mechanism
post-restart durable state
post-restart actual evidence
recovery path selected
final canonical state
final actual state
user-data integrity checks
result
```

---

# 15. Non-waivable fault outcomes

Release is blocked if an in-scope mandatory fault case demonstrates any of:

```text
Windows unbootable due SplitOS operation with no supported recovery
user canonical data lost/rolled back unexpectedly
FREE escalated to PRO
old release accepted through unauthorized downgrade
stale mutation owner changes machine
source/target canonical state guessed inconsistently
rollback/recovery reports success without verification
recovery destroys user documents/profile data
privileged arbitrary command surface reachable
```

---

# 16. Result

SplitOS is not considered reliable because happy paths work. It is release-qualified only when interruption points demonstrate deterministic, evidence-driven convergence to a coherent state.
