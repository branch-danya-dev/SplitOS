# SplitOS — Security Control Matrix

## 1. Purpose

Этот документ сводит основные угрозы и trust controls в компактную review-матрицу.

Он не заменяет подробные Trust документы и не является penetration-test plan.

---

## 2. Matrix

| ID | Threat / misuse | Boundary | Required control | Safe failure result |
|---|---|---|---|---|
| `SEC-001` | другой user process пытается вызвать Broker | Local IPC | explicit pipe ACL + caller token/session validation + capability auth | request denied; runtime remains usable |
| `SEC-002` | другой Windows user/session вызывает Broker | Local IPC | logon/session scoped authorization | denied |
| `SEC-003` | valid caller requests arbitrary admin command | Broker API | no generic shell/script API; operation allowlist | unsupported/denied |
| `SEC-004` | malformed IPC payload | Local IPC | bounded/versioned schema validation | reject message, no mutation |
| `SEC-005` | replayed privileged request | Local IPC | transaction/operation IDs, idempotency/dedup where needed | return prior result/reject duplicate |
| `SEC-006` | Broker caller validation fails | Local IPC | fail closed | no privileged action |
| `SEC-007` | Runtime Host binary replaced by normal user | Local install | protected install ACL + signed artifact provenance/repair | runtime rejected/repaired/degraded |
| `SEC-008` | token stolen from plaintext config | Identity | DPAPI/Windows protected secret storage candidate | reauth if decrypt/validation fails |
| `SEC-009` | forged auth callback | Auth callback | state + transaction correlation + PKCE/token endpoint validation | reject callback |
| `SEC-010` | embedded fake login captures password | Identity | external browser/native-app auth pattern | no password collected by runtime |
| `SEC-011` | local config flips `pro=true` | Entitlement | entitlement authority server/offline signed evidence only | premium remains disabled |
| `SEC-012` | stale offline entitlement used forever | Entitlement | validity window + issuer/signature/context checks | premium disabled/reauth required |
| `SEC-013` | clock rollback extends offline PRO | Entitlement | anti-clock rollback strategy required; suspicious state forces refresh | degrade/require online validation |
| `SEC-014` | fake checkout callback grants PRO | Payment | client callback only triggers backend entitlement refresh | no entitlement change |
| `SEC-015` | replayed payment event | Payment backend | provider auth + transaction ID + idempotency | duplicate ignored |
| `SEC-016` | update package modified after download | Update | signed manifest + digest/signature validation | package rejected |
| `SEC-017` | artifact swapped after verification | Update staging | protected staging + revalidation/handle binding | apply blocked |
| `SEC-018` | signed but old vulnerable release forced | Update | anti-downgrade/version transition policy | downgrade denied unless explicit recovery |
| `SEC-019` | signed incompatible component set | Update | release composition/protocol compatibility verification | degraded/recovery, no false commit |
| `SEC-020` | unsigned local Build Manifest | Builder | manifest signature/provenance required for supported build | unsupported build/reject |
| `SEC-021` | manifest contains arbitrary script | Builder | typed operation allowlist | manifest rejected |
| `SEC-022` | modified Windows ISO renamed as official | Builder | supported source identity/provenance validation | build rejected/unsupported |
| `SEC-023` | Steam manifest contains malicious path | Game Client adapter | normalize/validate paths; no privileged execution | evidence rejected/launch unavailable |
| `SEC-024` | external client metadata parser crashes | Game Client adapter | parser containment + bounded input | adapter unavailable, core remains coherent |
| `SEC-025` | client handoff accepted but game absent | Game launch | independent process/game evidence + timeout | failed launch; remain GAME/Launcher |
| `SEC-026` | device label/string injected into command/UI | External evidence | treat as data, escape UI, never shell-expand | input rejected/escaped |
| `SEC-027` | Microsoft update offered but incompatible with SplitOS | Platform update | Compatibility Management gate | defer/deny update |
| `SEC-028` | corrupted transaction record after reboot | Persistence | schema/context/integrity validation + reconciliation | enter recovery, no guessed commit |
| `SEC-029` | recovery artifact tampered | Recovery | same artifact trust chain as update | recovery denied/manual path |
| `SEC-030` | recovery operation reports success but target not reached | Recovery | actual-state readback + verification | remain recovery/degraded |
| `SEC-031` | diagnostics contains secrets | Observability | redact tokens/secrets, structured security events | diagnostic omission preferred to secret leak |
| `SEC-032` | log entry is used as state authority | Observability | diagnostics never canonical source | reconcile from owners/evidence |
| `SEC-033` | TLS endpoint response contains malicious fields | Network/API | TLS + application schema/authorization validation | reject payload/request |
| `SEC-034` | local admin deliberately patches SplitOS | Host threat | explicitly outside v1 guarantee; rely on Windows platform controls where possible | no false security claim |
| `SEC-035` | release signing private key leaked into client | Supply chain | private keys only in protected release-signing domain | release process invariant violation |

---

## 3. Control families

### Identity controls

```text
Windows SID / logon session
external-browser auth
PKCE candidate
server-issued tokens
protected local secrets
```

### Authorization controls

```text
capability-based authorization
no global trusted flag
Broker operation allowlist
entitlement capability gate
```

### Integrity controls

```text
DPAPI integrity for protected user secrets
Authenticode for Windows binaries
signed manifests
artifact digests
post-apply verification
```

### Freshness controls

```text
transaction correlation
assertion expiry
snapshot timestamps
reconciliation after reboot
anti-replay/idempotency
```

### Containment controls

```text
interactive runtime separated from Broker
client adapters separated from core owners
external parsers outside privileged zone
base Windows usable on premium trust failure
```

---

## 4. High-risk controls requiring implementation validation

The following are conceptually required but not fully specified:

```text
Broker exact ACL/SDDL/token validation
Broker service privilege minimization
DPAPI/Credential storage abstraction
OAuth/OIDC provider + redirect strategy
offline entitlement format + TTL
clock rollback handling
manifest signature envelope/key hierarchy
artifact staging TOCTOU protection
Windows source provenance validation
release key rotation/revocation
```

These must remain tracked as OPEN rather than being assumed solved.

---

## 5. Result

Matrix establishes a direct review chain:

```text
Threat
→ trust boundary
→ preventive/detective control
→ safe failure behavior
```

The next Synthesis layer can use this matrix to derive component responsibilities and deployment/security boundaries.