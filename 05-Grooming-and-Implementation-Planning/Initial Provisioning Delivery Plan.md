# Initial Provisioning Delivery Plan

## 1. Purpose

This Grooming addendum converts the accepted Initial Provisioning model into implementation work.

It is downstream from:

```text
DEC-048..051
→ FR-PROV-*
→ FL-00 Initial Provisioning
→ SPEC-10 Initial Provisioning and Package Delivery
```

This file clarifies and supersedes only the older Grooming interpretations that previously jumped directly from:

```text
Windows first sign-in
→ SplitOS First Run / Account
```

The current delivery sequence is:

```text
Windows Setup / OOBE
↓
first Windows sign-in
↓
Initial Provisioning
↓
READY_FOR_FIRST_RUN or READY_WITH_DEFERRED_OPTIONALS
↓
SplitOS First Run / Account / personalization
```

All unrelated content in the existing Grooming artifacts remains valid.

---

## 2. What we are implementing

The product must deliver a prepared user environment rather than a bare Windows installation plus manual post-install work.

Initial Provisioning has four logical stages:

```text
1 REQUIRED_PLATFORM
2 FIRST_PARTY_BUNDLED
3 THIRD_PARTY_PROVISIONED
4 FINALIZATION
```

### REQUIRED_PLATFORM

Examples:

```text
RuntimeHost
Broker Service
Manager / First Run substrate
persistence/bootstrap
update/recovery bootstrap
```

### FIRST_PARTY_BUNDLED

SplitOS-owned feature packages belonging to the release.

Current/future examples:

```text
Game Launcher
Audio / Equalizer
Hotkeys
Widgets
Pins
Controller Tools
Performance Tools
Display Tools
```

These packages are part of the release even if they are physically installed after Windows Setup rather than injected into offline `install.wim`.

### THIRD_PARTY_PROVISIONED

External software that SplitOS intentionally prepares for the user, for example supported game clients/companion apps selected by release/product policy.

External vendor ownership remains unchanged.

---

## 3. Offline-first requirement

For the installed media release:

```text
REQUIRED_PLATFORM
+
required FIRST_PARTY_BUNDLED
```

must be installable and verifiable from local installation-media/staged release payload.

The following MUST NOT be required to acquire those first-party binaries:

```text
SplitOS CDN
SplitOS Account Backend
GitHub
community mirror
```

A network-less supported installation may still complete core provisioning and produce:

```text
READY_WITH_DEFERRED_OPTIONALS
```

if online-only third-party packages are deferred.

---

## 4. Delivery ownership

### Builder / Distribution track

Owns:

```text
package catalog schema
ProvisioningPlan schema
media payload staging
media completeness verification
third-party provisioning descriptor staging
bootstrap registration
```

### Runtime / Provisioning track

Owns:

```text
Provisioning Coordinator
transaction/item persistence
package handlers
actual-state verification
resume/reconciliation
readiness result
```

### Manager track

Owns:

```text
provisioning progress/status surface where applicable
deferred optional item projection
retry / decline / vendor-action intents
```

### Release / Security track

Owns:

```text
first-party artifact trust
ProvisioningPlan/catalog trust
approved third-party adapter/catalog trust
publisher/integrity constraints
```

---

## 5. Backlog extension

New implementation items use `IMP-170..179` and do not renumber the existing backlog.

### IMP-170 — ProvisioningPlan and package-catalog schema

Status: `READY_AFTER_IMP-111`

Output:

- schema/version identity;
- release binding;
- package class;
- requiredForFirstUse;
- sourceKind;
- handler IDs;
- readiness predicates;
- third-party catalog reference.

### IMP-171 — Durable provisioning transaction/item store

Status: `READY_AFTER_IMP-030`

Must support:

```text
transaction state
per-item state
attempt/revision
reboot resume marker
deferred reason
actual verification result
```

### IMP-172 — Local release package store

Status: `READY_AFTER_RUNTIME_PACKAGING`

Prove:

```text
media/staged package lookup
release binding
digest/provenance verification
no network dependency for required first-party artifacts
```

### IMP-173 — Typed first-party install/verify handlers

Status: `READY_AFTER_IMP-170/172`

Initial handlers should cover the package shapes actually used by the SplitOS executables before expanding to future feature packages.

No generic arbitrary command handler.

### IMP-174 — Initial Provisioning Coordinator

Status: `READY_AFTER_IMP-171/173`

Implement:

```text
PLATFORM_PREPARING
FIRST_PARTY_INSTALLING
THIRD_PARTY_PROVISIONING
FINALIZING
READY_*
BLOCKED_REQUIRED_FAILURE
```

with crash/reboot reconciliation.

### IMP-175 — Third-party provisioning adapter registry

Status: `CONDITIONALLY_READY`

Defines bounded adapter contract; does not yet promise any particular vendor application.

### IMP-176 — First approved third-party acquisition adapter

Status: `BLOCKED_BY_VENDOR_PACKAGE_SELECTION`

Selection requires:

```text
legal/distribution check
stable official acquisition mechanism
publisher verification
silent/install UX constraints
uninstall/update ownership understanding
```

A Steam provisioning path is a reasonable future candidate but is not automatically declared supported merely because Steam is our first game-client runtime adapter.

### IMP-177 — Initial Provisioning progress UI

Status: `READY_AFTER_IMP-174`

Semantic stages, not raw installer consoles.

### IMP-178 — Manager deferred optional setup surface

Status: `READY_AFTER_IMP-174/175`

Supports:

```text
view deferred items
retry
explicit decline where allowed
open required vendor/user action
```

### IMP-179 — Provisioning end-to-end verification

Status: `READY_AFTER_IMP-118/174`

Required test matrix:

```text
offline first-party install
corrupt required package
first-party read-back mismatch
network unavailable for third party
vendor endpoint unavailable
publisher mismatch
crash during package installation
reboot/resume
duplicate/retry idempotency
First Run blocked before mandatory readiness
Manager deferred retry
```

---

## 6. Existing backlog relationship

Existing Builder item:

```text
IMP-118 — SplitOS package staging/setup provisioning
```

now means the **Builder/media side** only:

```text
stage package payload
stage plan/catalog
register setup/bootstrap
verify media completeness
```

It does NOT mean "the Builder itself completes all live user-session provisioning".

Actual installed-machine execution is owned by `IMP-170..179`.

---

## 7. SLICE-08 clarification

Existing `SLICE-08 — Builder and Prepared Baseline` is extended with Initial Provisioning evidence.

Its supported end state is no longer merely:

```text
Windows OOBE works
RuntimeHost exists
```

The meaningful prepared-image milestone is:

```text
supported source
↓
prepared installation media
↓
clean install
↓
Windows OOBE / first user
↓
Initial Provisioning
↓
required platform verified
↓
required first-party packages verified
↓
optional third-party attempt/defer
↓
READY_FOR_FIRST_RUN or READY_WITH_DEFERRED_OPTIONALS
```

### Additional SLICE-08 acceptance

- required first-party payload is present on media;
- no SplitOS CDN/backend is needed to install required first-party packages;
- provisioning plan/catalog match installed release;
- missing/corrupt required package blocks readiness;
- optional online third-party item can defer without trapping the user;
- crash/reboot resumes through actual-state reconciliation;
- normal First Run cannot begin before mandatory readiness;
- one approved third-party adapter may be proven later without blocking the first Builder prototype if no vendor package is yet declared in the release support set.

### Milestone interpretation

`M1 — Prepared Baseline Boots` is strengthened to mean:

```text
Prepared baseline boots
+
Initial Provisioning path reaches first-use readiness
```

for the supported test release.

---

## 8. Main implementation walkthrough correction

Where `Implementation Scope and End-to-End Walkthrough.md` currently describes:

```text
Windows user created
→ first sign-in
→ Runtime Host
→ SplitOS First Run
→ Account
```

read the current canonical sequence as:

```text
Windows user created
→ first sign-in
→ Runtime/Provisioning bootstrap
→ Initial Provisioning Coordinator
→ required platform verification
→ first-party install/verification
→ third-party preparation/defer
→ provisioning readiness
→ SplitOS First Run
→ Account
→ Entitlement
```

This addendum exists specifically to prevent implementation tasks from following the older direct-to-account shorthand.

---

## 9. Dependency path

Provisioning engineering can begin incrementally before the full Builder exists.

```text
SLICE-00 executable packaging
↓
package identity/manifest fixture
↓
ProvisioningPlan fixture
↓
local package-store fixture
↓
Coordinator + durable journal
↓
install/verify current SplitOS executables in disposable VM
↓
Builder later supplies the same contracts from prepared media
```

This allows us to return to code now without waiting for full WIM/ISO servicing implementation.

---

## 10. Recommended near-term implementation order

After the current documentation clarification is merged:

```text
continue SLICE-00 real Windows service/lab proof
↓
SLICE-01 persistence
↓
in parallel begin provisioning schemas/fixtures (IMP-170/171)
↓
when packaging is stable prove local package store + handlers
↓
connect to Builder track later
```

Do not jump immediately into third-party auto-installers before the first-party transaction/read-back model is proven.

---

## 11. Definition of Ready for Initial Provisioning implementation

The model is ready for delivery when developers/QA agree on:

```text
package classes
release media ownership
ProvisioningPlan identity
required vs optional semantics
offline behavior
third-party authority boundary
readiness states
crash/reboot reconciliation
First Run handoff
```

Those semantics are now owned upstream by DEC-048..051 / FR-PROV / FL-00 / SPEC-10 and must not be redefined in implementation tickets.
