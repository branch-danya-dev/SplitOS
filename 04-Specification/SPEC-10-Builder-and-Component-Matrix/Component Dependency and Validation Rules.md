# SplitOS — Component Dependency and Validation Rules

## 1. Purpose

This document defines the evidence required before a Windows component classification may become executable release behavior.

## 2. Dependency model

For every component SplitOS records:

```text
providers it requires
consumers that require it
setup dependencies
servicing dependencies
recovery dependencies
runtime dependencies
application compatibility dependencies
```

Dependency evidence may be:

```text
Microsoft documentation
Windows package/feature metadata
service dependency metadata
runtime observation
controlled removal experiment
regression test result
external product integration evidence
```

No single evidence source is sufficient for every component type.

## 3. Validation ladder

A classification moves through:

```text
HYPOTHESIS
↓
MECHANISM_VERIFIED
↓
BOOT_VERIFIED
↓
SERVICING_VERIFIED
↓
RECOVERY_VERIFIED
↓
COMPATIBILITY_VERIFIED
↓
ACCEPTED
```

A release may require additional gates for high-risk components.

## 4. Mechanism validation

Proves:

```text
exact technical identity is known
operation is supported/implementable on target Windows base
operation reaches expected immediate state
postcondition can be read back
```

It does not prove that the resulting Windows installation remains supportable.

## 5. Boot validation

Required for all destructive baseline changes.

Minimum:

```text
Windows Setup completes
OOBE completes
user account can be created
first user sign-in succeeds
SplitOS runtime provisioning completes
Windows shell remains usable in FREE/base state
```

## 6. Servicing validation

At least the release's supported Windows servicing path must be tested after the candidate mutation.

Examples:

```text
latest supported cumulative update
reboot completion
component-store health/servicing checks
SplitOS runtime update interaction where relevant
```

The exact acceptance suite is owned finally by SPEC-14, but `REMOVE` cannot be accepted without servicing evidence.

## 7. Recovery validation

High-risk classifications must test:

```text
WinRE/repair boot availability
SplitOS recovery path
failed-update recovery where relevant
reset/repair consequences documented
```

If removal intentionally prevents Windows built-in reset from restoring a capability, that consequence must be explicit and compatible with the SplitOS recovery contract.

## 8. Compatibility validation

Component acceptance must cover product-critical scenarios, including where relevant:

```text
Windows sign-in
networking
browser/web runtime dependencies
Microsoft Store/app deployment
Steam/Epic/Microsoft Gaming launch
controller/input
multi-display/audio
Work applications
printing/indexing for MODE_MANAGED rows
SplitOS Manager/Launcher/RuntimeHost/Broker
```

## 9. Risk classes

```text
LOW
MEDIUM
HIGH
CRITICAL
```

Risk is multidimensional:

```text
compatibilityRisk
servicingRisk
recoveryRisk
securityRisk
```

A component may be LOW performance/UX risk but HIGH servicing risk.

## 10. REMOVE acceptance

`REMOVE` is the strongest mutation and requires:

```text
mechanism verified
boot verified
servicing verified
recovery consequence verified
critical app/integration compatibility verified
no unresolved required consumer
post-removal footprint/benefit evidence
```

If any required consumer remains unresolved:

```text
classification = TBD
```

not `REMOVE`.

## 11. DISABLE acceptance

Requires:

```text
disable mechanism
startup/baseline read-back
boot verification
re-enable/recovery behavior documented
servicing test
```

If a globally disabled component is useful in WORK, classification should be reconsidered as `MODE_MANAGED`.

## 12. MODE_MANAGED acceptance

Requires both build-time and runtime evidence:

```text
component retained in baseline
WORK target reachable
GAME target reachable
transitions bounded in time
read-back evidence reliable
failure criticality defined
rollback/reconciliation defined
meaningful footprint/UX benefit demonstrated
```

A component with negligible benefit from mode switching SHOULD remain `KEEP` rather than add transition complexity.

## 13. KEEP acceptance

`KEEP` may be selected proactively when:

```text
component is platform-critical
or
removal/disable benefit is unproven
or
risk exceeds expected gain
```

KEEP is not failure to optimize. It is an explicit compatibility decision.

## 14. Benefit evidence

Aggressive removal/disable must have a stated purpose.

Possible benefits:

```text
background CPU reduction
RAM reduction
I/O reduction
network reduction
notification/distraction reduction
privacy improvement
image/provisioning simplification
attack-surface reduction
UX cleanup
```

Pure disk-size reduction alone does not automatically justify high servicing/recovery risk.

## 15. Windows build change

A classification accepted for Windows Base A is not automatically accepted for Base B.

When source build changes:

```text
re-inventory technical identities
re-evaluate package/component dependencies
rerun release-defined validation gates
publish new matrix version
```

A technical identity disappearing or changing does not authorize the Builder to search by fuzzy package name and remove the closest match.

## 16. Rejected candidates

A `REJECTED` decision is preserved as knowledge with reason/evidence.

Example:

```text
candidate REMOVE
→ servicing regression
→ REJECTED
→ class becomes KEEP or MODE_MANAGED
```

Future releases may reopen the hypothesis only with new evidence.

## 17. Security-sensitive candidates

Security platform changes, especially Microsoft Defender/Windows Security dependencies, require an explicit minimum supported security baseline before production acceptance.

Product desire to remove a security component does not satisfy this requirement.

SPEC-12 and SPEC-14 provide the final release-security and acceptance gates.
