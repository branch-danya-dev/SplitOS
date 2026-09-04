# SplitOS — External Boundary Contracts

## 1. Purpose

Документ определяет semantic contracts между SplitOS и внешними системами/authorities.

Он не фиксирует конкретные SDK/API/registry paths/process names. Эти детали принадлежат следующему Integration layer.

External boundary contract отвечает на вопрос:

```text
Что SplitOS ожидает получить или попросить у внешней системы,
какой смысл имеет ответ,
кто остаётся authority,
и как SplitOS должен интерпретировать failure/staleness?
```

---

# 2. External authorities

Ключевые внешние authority classes:

```text
Windows / Windows APIs
GPU / Audio / Device Drivers
Physical Devices
External Game Clients / Platforms
SplitOS Account Backend
Payment Provider
Microsoft Windows source/update ecosystem
```

SplitOS не должен превращать adapter response в competing truth там, где ownership остаётся внешним.

---

# 3. Windows / platform boundary

## EXT-WIN-001 — Windows User Context Evidence

**Direction:** Windows → SplitOS

**Purpose:** получить текущий Windows user/session context, необходимый для association с SplitOS Account.

### Semantic evidence

```text
current Windows user/session identifier
session availability
relevant local user context
```

### Authority

Windows остаётся authority OS user/session identity.

SplitOS хранит только собственную association:

```text
WindowsUserContext
↔ SplitOSAccount
```

---

## EXT-WIN-010 — Process / Application State Evidence

**Direction:** Windows → SplitOS

**Purpose:** наблюдать process/window/service state для transition/application lifecycle.

### Evidence examples

```text
process running
window present
service state
process exit observed
```

### Critical limit

Это не гарантирует application-internal state.

```text
WINWORD.EXE exists
!= document is safely saved
```

Если для safe-close нужна app-specific integration, она должна иметь отдельный integration contract.

---

## EXT-WIN-DISPLAY-001 — Display Capability / Actual State Evidence

**Direction:** Windows/driver → SplitOS

### Provides

```text
connected displays
current topology
resolution/refresh
HDR/VRR evidence where exposed
capability information
```

### Authority

Actual platform/display evidence belongs to Windows/driver/device.

SplitOS owns desired display context and verification interpretation.

---

## EXT-WIN-DISPLAY-002 — Apply Display Operation

**Direction:** SplitOS → Windows/driver boundary

### Input semantics

Desired platform operation derived from SplitOS display intent.

### Immediate outcome

```text
SUBMITTED
UNSUPPORTED
FAILED
DEPENDENCY_UNAVAILABLE
```

Final success must be verified through `EXT-WIN-DISPLAY-001` evidence.

---

## EXT-WIN-AUDIO-001/002

Audio follows same pattern:

```text
Read capability/actual evidence
Apply desired routing/context
Read back actual evidence
Verify
```

---

## EXT-WIN-INPUT-001

Provides device/input capability evidence relevant to system navigation/profile selection.

Gameplay input remains owned by game/device stack; SplitOS must not use this boundary to create unfair gameplay automation.

---

## EXT-WIN-POWER-001/002

Provides/apply platform power/resource policy evidence and operations.

SplitOS owns desired policy; Windows/platform owns actual applied state evidence.

---

# 4. Game Client / Platform boundary

## EXT-GC-001 — Game Client Availability

**Direction:** Game Client environment → SplitOS

### Purpose

Определить, доступен ли supported client для reconciliation/launch.

### Result semantics

```text
AVAILABLE
NOT_INSTALLED
NOT_RUNNING
UNAVAILABLE
UNSUPPORTED_VERSION
```

Конкретные technical checks определяются integration adapter.

---

## EXT-GC-002 — Library / Installation Evidence

**Direction:** Game Client → SplitOS

### Provides

```text
external game/client identity
installation availability
installation location evidence where allowed
client-specific launch identity
last observed/reconciled time
```

### Authority

External Game Client/platform остаётся authority installation truth.

SplitOS обновляет:

```text
GameInstallationProjection
```

---

## EXT-GC-003 — License / Launch Eligibility Evidence

**Direction:** Game Client/platform → SplitOS

### Purpose

Получить доступное внешнее evidence, необходимое для managed launch.

### Important

SplitOS не должен создавать собственную platform-license truth.

Если platform не предоставляет отдельную license query, фактическая launch response может быть единственным доступным evidence.

---

## EXT-GC-004 — Launch Game Through Client

**Direction:** SplitOS → Game Client

### Input semantics

```text
external game identity
supported launch context
optional client-supported launch options
```

### Immediate outcome

```text
HANDOFF_ACCEPTED
GAME_NOT_AVAILABLE
AUTH_REQUIRED
CLIENT_UNAVAILABLE
UNSUPPORTED
FAILED
```

### Critical rule

```text
HANDOFF_ACCEPTED
!= GAME_RUNNING
```

Game-running state определяется отдельным process/integration evidence path.

---

## EXT-GC-005 — Game Start / Exit Evidence

**Direction:** Game/Windows/client evidence → SplitOS

### Purpose

Подтвердить переход:

```text
GAME_STARTING
→ GAME_RUNNING
→ GAME_EXIT_DETECTED
```

Evidence strategy может отличаться по client/game integration и будет описана позже.

---

# 5. SplitOS Account Backend boundary

## EXT-ID-001 — Authenticate / Establish SplitOS Account Session

**Direction:** SplitOS client ↔ SplitOS Account Backend

### Purpose

Создать/восстановить SplitOS product identity session.

### Inputs conceptually

```text
user authentication interaction
client/device/session context as required by security design
```

### Results

```text
AUTHENTICATED
AUTH_REQUIRED
INVALID_CREDENTIALS
MFA_REQUIRED
BACKEND_UNAVAILABLE
ACCOUNT_RESTRICTED
```

### Boundary rule

Failure does not invalidate Windows authentication/session.

---

## EXT-ID-002 — Read Account / Entitlement Evidence

**Direction:** Account Backend → local Product Identity & Entitlement

### Provides

Evidence used to establish local canonical entitlement state.

Potential semantic fields:

```text
account identity reference
plan/subscription evidence
entitlement claims/state
validity/freshness metadata
```

### Important

Local Product Identity & Entitlement remains owner of local runtime-access decision according to offline policy.

---

## EXT-ID-003 — Account / Entitlement Change Notification or Refresh

Used to reconcile upgrades, renewals, downgrades or server-side account changes.

Transport may later be polling, push, explicit refresh or another mechanism.

---

# 6. Payment Provider boundary

## EXT-PAY-001 — Create / Open Checkout

**Direction:** SplitOS Account/Manager flow → Payment Provider through SplitOS backend or hosted checkout architecture

### Purpose

Позволить пользователю приобрести paid plan без передачи card-data ownership SplitOS desktop runtime.

### Result

```text
CHECKOUT_CREATED
UNAVAILABLE
INVALID_PRODUCT
FAILED
```

### Security principle

SplitOS desktop product не должен становиться raw card-data processor без отдельной необходимости/architecture decision.

---

## EXT-PAY-002 — Payment Evidence

**Direction:** Payment Provider → SplitOS backend

### Purpose

Дать evidence о transaction/subscription state.

### Critical rule

```text
Payment provider status
!= local SplitOS runtime entitlement by itself
```

Backend/Product Identity converts validated payment evidence into SplitOS entitlement semantics.

---

# 7. Microsoft Windows source / update boundary

## EXT-MS-SOURCE-001 — Windows Source Input

**Direction:** Microsoft-authorized source → SplitOS Media Builder

### Provides

Windows installation source + identifying/integrity metadata needed for source validation.

### Authority

Windows source/binaries remain Microsoft-owned input.

Builder must not silently treat unknown source as supported.

---

## EXT-MS-UPDATE-001 — Windows Patch/Release Evidence

**Direction:** Microsoft ecosystem → Compatibility Management

### Purpose

Provide candidate Windows update/release information and artifacts for SplitOS compatibility validation.

### Rule

Microsoft publishing a patch does not mean:

```text
SplitOS compatible = true
```

Compatibility Management owns that decision.

---

# 8. Freshness and cache semantics

External evidence can be stale.

Every projection/evidence interface that may persist beyond the observation should support conceptual freshness metadata:

```text
observedAt
sourceVersion where useful
reconciliationStatus
stale/unknown indication
```

Examples:

- GameInstallationProjection;
- HardwareSnapshot;
- entitlement cache;
- device/display evidence.

### Rule

Stale data should not silently masquerade as fresh authoritative data.

---

# 9. Failure isolation

External dependency failure should map to semantic degradation, not uncontrolled system failure.

Examples:

```text
Account backend unavailable
→ Windows remains usable
→ runtime access follows offline policy

Steam unavailable
→ Steam-managed games may be unavailable to managed launch
→ SplitOS Game Mode itself need not crash

Display operation fails
→ transition/launch may fallback or fail verification
→ committed state follows canonical state rules
```

---

# 10. Adapter principle

Future integrations should hide external-specific mechanics behind stable semantic contracts where practical.

For Game Clients:

```text
Steam Adapter
Epic Adapter
Xbox Adapter
Battle.net Adapter
        ↓
common SplitOS semantic boundary
        ↓
Game Library / Launch Orchestration
```

Это не требует одинаковой технической реализации. Оно требует одинакового понятного product-level meaning.

---

# 11. Open interface questions for Integration layer

- Какие официальные/поддерживаемые mechanisms доступны для Steam/Epic/Xbox/Battle.net library discovery и launch?
- Какие Windows APIs достаточны для display/audio/input/power apply + readback?
- Какие app-specific mechanisms нужны для reliable pre-flight unsaved-work detection?
- Какой local IPC boundary подходит для SplitOS Manager ↔ privileged runtime operations?
- Нужен ли отдельный privileged broker/service для Windows operations?
- Как реализовать account auth безопасно: system browser, WebView, device-code, native flow?
- Какой offline entitlement cache/validation mechanism нужен?
- Какие payment provider/backend callbacks формируют entitlement evidence?
- Какие Windows update/source APIs и licensing terms реально допустимы для Builder/update lifecycle?

Эти вопросы **не должны быть решены догадкой в Interface layer**.

---

# 12. Result

External interface model сохраняет границу:

```text
External authority/evidence
        ↓
Adapter contract
        ↓
SplitOS owner interprets/decides
        ↓
Canonical SplitOS state or projection
```

а не:

```text
External response
=
SplitOS canonical truth for everything
```
