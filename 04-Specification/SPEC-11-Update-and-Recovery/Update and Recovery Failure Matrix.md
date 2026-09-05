# Update and Recovery Failure Matrix

## 1. Purpose

This document maps update/recovery failures to containment and recovery behavior.

A failure code describes evidence/outcome. It does not itself mutate canonical installed-release state.

---

## 2. Preparation failures

| Failure | Meaning | Canonical release | Default action |
|---|---|---|---|
| `UPDATE_CHANNEL_UNAVAILABLE` | release service cannot be reached | current | retry later |
| `RELEASE_ENVELOPE_INVALID` | release metadata failed trust/schema validation | current | reject release |
| `ARTIFACT_DIGEST_MISMATCH` | downloaded artifact does not match signed metadata | current | delete/re-download; reject if repeated |
| `WINDOWS_BASE_NOT_SUPPORTED` | target release does not support current Windows base | current | do not activate |
| `WINDOWS_SERVICING_CONFLICT` | Windows servicing/update currently conflicts | current | defer |
| `REBOOT_REQUIRED_BEFORE_SPLITOS_UPDATE` | Windows requires prior reboot | current | request/await reboot |
| `STAGING_SPACE_INSUFFICIENT` | target cannot be staged safely | current | user frees space |
| `RECOVERY_RESERVE_INSUFFICIENT` | mandatory previous capsule cannot be stored | current | block activation |
| `ROLLBACK_COMPATIBILITY_NOT_PROVEN` | target migration breaks mandatory previous release | current | block activation |

---

## 3. Recovery-capsule failures

| Failure | Meaning | Action |
|---|---|---|
| `CAPSULE_CREATE_FAILED` | previous release backup could not be assembled | abort update |
| `CAPSULE_VERIFY_FAILED` | capsule cannot prove integrity/readability | abort update |
| `CAPSULE_CORRUPTED` | sealed capsule changed or became unreadable | do not use; rebuild when source release available |
| `CAPSULE_WINDOWS_INCOMPATIBLE` | previous release not compatible with current Windows base | reject automatic rollback target |
| `CAPSULE_SIGNATURE_REJECTED` | release/capsule trust validation fails | do not use |
| `CAPSULE_SCHEMA_UNSUPPORTED` | recovery tooling cannot safely interpret capsule | require compatible recovery tool/manual support |

A target update must not consume the source release if there is no other valid rollback path after capsule failure.

---

## 4. Activation failures before commit

| Failure | Meaning | Canonical release | Response |
|---|---|---|---|
| `TARGET_ACTIVATION_FAILED` | service/task/release activation did not reach planned target | source | rollback source activation |
| `TARGET_BROKER_UNHEALTHY` | target Broker cannot start/handshake | source | rollback |
| `TARGET_RUNTIME_UNHEALTHY` | target Runtime cannot reach mandatory health | source | rollback |
| `MIGRATION_FAILED` | machine migration failed | source | rollback/recovery based on reversibility |
| `TARGET_HEALTH_NOT_REACHED` | mandatory target predicates fail | source | rollback |
| `UPDATE_BOOTSTRAP_FAILED` | privileged bootstrap stopped unexpectedly | source until commit | resume checkpoint or recovery |

---

## 5. Commit ambiguity

| Evidence | Rule |
|---|---|
| `commitDurable=false` | source remains canonical |
| `commitDurable=true` | target is canonical |
| commit storage unreadable/ambiguous | Recovery; never guess |

---

## 6. Failures after target commit

After target commit, problems are recovery incidents against the target release.

Examples:

```text
TARGET_POST_COMMIT_CRASH_LOOP
TARGET_RUNTIME_DEGRADED
TARGET_MACHINE_STATE_CORRUPT
```

Recovery may select the previous capsule, but this is a new Recovery operation with its own journal and final commit.

---

## 7. Rollback failures

| Failure | Meaning | Escalation |
|---|---|---|
| `ROLLBACK_ACTIVATION_FAILED` | previous service/task mapping cannot be restored | Recovery |
| `ROLLBACK_RELEASE_PAYLOAD_INVALID` | previous release payload unavailable/corrupt | Recovery / WinRE |
| `ROLLBACK_MACHINE_STATE_FAILED` | required machine snapshot cannot be restored | Recovery |
| `ROLLBACK_TARGET_HEALTH_NOT_REACHED` | previous release activated but cannot verify | WinRE / safe runtime disabled |

Rollback failure must not loop forever.

---

## 8. Safe fallback

When SplitOS cannot restore a valid managed runtime but Windows remains healthy:

```text
Windows Desktop usable
ManagedRuntime = DISABLED / unavailable
OperationalMode = NONE
Manager/recovery status available where possible
```

This is preferable to destructive Windows mutation.

---

## 9. User-data safety failures

If update detects that rollback would require destructive user-data downgrade:

```text
USER_DATA_ROLLBACK_INCOMPATIBLE
```

The target update is blocked before activation.

If recovery later encounters unsupported user-data semantics, it must preserve the data and degrade the older runtime rather than erase unknown fields.

---

## 10. Storage-device failure

`RECOVERY_STORE_UNAVAILABLE` may mean the same physical disk can no longer provide the capsule.

SplitOS cannot satisfy local previous-release rollback in that case.

The product should guide to external/Windows recovery and must not imply that same-device recovery equals a full backup.
