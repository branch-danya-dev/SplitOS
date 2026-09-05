# SPEC-11 Traceability

## 1. Requirement → Specification

| Requirement family | SPEC-11 contract |
|---|---|
| `FR-UPDATE-001..004` | `Windows Update Coordination.md` — unvalidated Windows changes are not automatically accepted; Microsoft remains payload source |
| `FR-UPDATE-005..009` | `SplitOS Update Channel Contract.md`, `Update Transaction and Version Switch.md` |
| `FR-UPDATE-010..013` | independent SplitOS wrapper channel + signed compatibility knowledge + separate Microsoft servicing lane |
| `FR-UPDATE-014..017` | Windows-servicing conflict gate, target verification/commit, mandatory recovery capacity/capsule |
| `FR-RECOVERY-006..007` | verified rollback/recovery and preservation of personal settings |
| `FR-RECOVERY-008..010` | automatic previous-release capsule in isolated local recovery store |
| `FR-RECOVERY-011..012` | software rollback does not erase live user data; one-release schema rollback compatibility |
| `FR-RECOVERY-013..016` | WinRE offline recovery, data/boot priority, no automatic destructive reset, honest same-device limitation |

---

## 2. A&D → Specification

### Ownership

```text
Update entitlement
→ Product Identity & Entitlement

Update compatibility
→ Compatibility Management

Update transaction/apply/result
→ Update Orchestration

Recovery target/result
→ Recovery Coordination
```

SPEC-11 preserves those owners. Transport/bootstrap/storage writers do not become semantic owners.

### Data

```text
UpdateTransactionRecord
→ detailed durable lifecycle / checkpoint / source-target release identity

RecoveryContext
→ recovery operation / previous capsule / final verified result

InstalledBaselineIdentity
→ current installed Windows/SplitOS baseline evidence
```

### Interfaces / Integrations

```text
IF-UPDATE
→ discovery / eligibility / prepare / apply / verify / commit

IF-RECOVERY
→ recovery candidate / restore / verify / final result

Broker
→ Maintenance.Update.ApplyVerified / bounded recovery capabilities
```

### Flow

`FL-05 Update and Recovery` becomes the executable sequence defined in:

- `Update Transaction and Version Switch.md`;
- `Reboot Resume and Crash Recovery.md`;
- `Recovery Capsule and Local Rollback.md`;
- `Recovery Environment and Escalation.md`.

### Failure

Existing rules preserved:

```text
installer technical success != update committed
source baseline remains canonical before target commit
rollback failure escalates to Recovery
Recovery must verify actual state
user data integrity > premium UX
```

### Trust

SPEC-11 requires:

```text
signed release envelope
artifact digests
trusted fixed-purpose Update Bootstrap
no arbitrary privileged executable path
capsule revalidation before rollback
```

Key hierarchy/revocation details remain SPEC-12.

---

## 3. New product clarification chain

```text
Independent SplitOS wrapper update channel
→ FR-UPDATE-010..017
→ SplitOS Update Channel Contract
→ Windows Update Coordination
→ Update Transaction

Automatic previous-version backup on current device
→ FR-RECOVERY-008..016
→ SplitOS Recovery Store
→ Recovery Capsule
→ WinRE Recovery Tool
→ software rollback without user-data rollback
```

---

## 4. Cross-SPEC dependencies

| SPEC | Dependency |
|---|---|
| SPEC-02 | typed Broker maintenance capabilities / caller authorization |
| SPEC-03 | machine/user stores, durability, migrations |
| SPEC-04 | update entitlement/account access where required |
| SPEC-05 | major machine mutation lease + fencing |
| SPEC-06 | Windows/actual-state read-back patterns |
| SPEC-10 | InstalledBaseline / BuildReceipt / recovery assets provisioned at installation |
| SPEC-12 | release/capsule signing, key rotation and revocation |
| SPEC-13 | update/recovery event/correlation diagnostics |
| SPEC-14 | retry thresholds, power-loss tests, rollback/data-loss acceptance tests |

---

## 5. Verification handoff

SPEC-14 must prove at minimum:

```text
wrapper update while Windows servicing idle
wrapper update deferred during Windows servicing
power loss in every durable update phase
Broker/Runtime crash during target activation
reboot resume before and after target commit
capsule corruption detection
previous-release live rollback
offline WinRE rollback
Windows-build incompatibility blocks unsafe capsule
N→N+1 user-data migration + user edits + rollback to N with zero canonical data loss
same-device recovery-store unavailable path
no automatic destructive Windows reset
```

---

## 6. Open items delegated forward

- exact release signing/key hierarchy — SPEC-12;
- capsule manifest signing/revocation semantics — SPEC-12;
- exact automatic rollback retry counts/time windows — SPEC-14;
- exact recovery partition capacity policy by release — implementation/release packaging + SPEC-14;
- exact Windows edition-specific update policy mechanism — engineering validation against supported Windows baseline;
- external/cloud user backup — future scope, not SPEC-11.
