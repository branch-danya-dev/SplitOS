# SplitOS — Windows Component Matrix v1 Baseline

## 1. Purpose

The Windows Component Matrix is versioned SplitOS release knowledge connecting product intent to exact Windows component identities and validated servicing/runtime mechanisms.

A row is not accepted because its desired class looks reasonable.

## 2. Required row fields

Every managed component row MUST define:

```text
componentId
humanName
componentType
technicalIdentityByWindowsBase
SplitOSClass
WorkTarget
GameTarget
buildMechanism
runtimeMechanism where relevant
dependencies
consumers
compatibilityRisk
servicingRisk
recoveryRisk
reversibility
validationStatus
evidenceRefs
notes
```

## 3. Component type

Allowed conceptual types include:

```text
SERVICE
DRIVER
APPX
SYSTEM_APP
OPTIONAL_FEATURE
OS_PACKAGE
CAPABILITY
SCHEDULED_TASK
POLICY
RUNTIME
SUBSYSTEM
OTHER
```

One logical capability may map to multiple technical components. A parent capability row MAY group children, but build operations target exact release-specific children.

## 4. Classification states

```text
REMOVE
DISABLE
MODE_MANAGED
KEEP
TBD
```

Validation state is independent:

```text
HYPOTHESIS
MECHANISM_VERIFIED
BOOT_VERIFIED
SERVICING_VERIFIED
COMPATIBILITY_VERIFIED
ACCEPTED
REJECTED
```

A destructive `REMOVE` row cannot enter production manifest while validation remains only `HYPOTHESIS`.

## 5. Initial v1 matrix posture

The following table defines the current product/engineering baseline. Exact package/service names remain Windows-base-specific mappings.

| componentId | Capability | Type | Desired class | Work target | Game target | Current validation | Main rationale / constraint |
|---|---|---|---|---|---|---|---|
| `WIN.CORE.SERVICING` | CBS / servicing / Windows Update substrate | SUBSYSTEM | KEEP | available | available | ACCEPTED semantic | Required to preserve supported servicing/update/recovery responsibility |
| `WIN.CORE.BOOT` | Boot manager / boot-critical platform | SUBSYSTEM | KEEP | required | required | ACCEPTED semantic | Never a debloat target |
| `WIN.RECOVERY.WINRE` | Windows Recovery Environment / recovery substrate | SUBSYSTEM | KEEP | available | available | ACCEPTED semantic | SplitOS recovery depends on a bootable recovery path; exact recovery customizations in SPEC-11 |
| `WIN.CORE.NETWORK` | Core Windows networking stack | SUBSYSTEM | KEEP | required | required | ACCEPTED semantic | Required by apps, clients, auth, updates |
| `WIN.CORE.DISPLAY` | Core display / graphics OS stack | SUBSYSTEM | KEEP | required | required | ACCEPTED semantic | SPEC-06 manages configuration, not removal |
| `WIN.CORE.AUDIO` | Core Windows audio stack | SUBSYSTEM | KEEP | required | required | ACCEPTED semantic | SPEC-06 consumes Core Audio evidence |
| `WIN.CORE.INPUT` | HID / input platform | SUBSYSTEM | KEEP | required | required | ACCEPTED semantic | Required for KBM/controller and recovery UX |
| `WIN.APPDEPLOY.STORE` | Microsoft Store application/deployment substrate | APPX/SUBSYSTEM | KEEP | available | available | ACCEPTED semantic | Microsoft documents Store removal as unsupported; gaming/app dependencies may consume it |
| `WIN.RUNTIME.WEBVIEW2` | WebView2 runtime capability | RUNTIME | KEEP candidate | available | available | HYPOTHESIS | Likely dependency for Windows/third-party apps; browser shell and WebView runtime are separate decisions |
| `WIN.CROSSDEVICE.PHONELINK` | Phone Link / Cross-Device | APPX/SERVICE group | MODE_MANAGED candidate | available/active when used | inactive where safely enforceable | HYPOTHESIS | Useful in WORK, background/noise candidate in GAME; exact child inventory required |
| `WIN.SEARCH.INDEXING` | Windows Search/indexing | SERVICE/SUBSYSTEM | MODE_MANAGED candidate | available | reduced/inactive where safe | HYPOTHESIS | Work value vs background I/O tradeoff; must test Start/Search dependency and restart cost |
| `WIN.PRINT.SUBSYSTEM` | Print Spooler and print capability | SERVICE/SUBSYSTEM | MODE_MANAGED candidate | available | inactive if no active print dependency | HYPOTHESIS | Work-oriented capability; service/dependency verification required |
| `WIN.ONEDRIVE.BASELINE` | OneDrive preinstallation/provisioning | APP/PROVISIONING | TBD / REMOVE candidate | user-installable | user-installable | HYPOTHESIS | Product wants lean baseline but user must remain able to install/use later; servicing/setup behavior must be verified |
| `WIN.DEFENDER.AV` | Microsoft Defender Antivirus | SECURITY SUBSYSTEM | TBD / desired REMOVE candidate | n/a | n/a | HYPOTHESIS | Product direction is physical removal if supportable, but Windows client security/servicing/dependency implications are unresolved; not allowed in production manifest yet |
| `WIN.SECURITY.WINDOWSSECURITYUI` | Windows Security UI/provider surface | SYSTEM_APP/SUBSYSTEM | TBD | available as required by chosen security baseline | same | HYPOTHESIS | Depends on final minimum security baseline; must not be removed merely because Defender AV is a candidate |
| `WIN.EDGE.BROWSER` | Microsoft Edge browser shell | APP/SYSTEM integration | TBD / REMOVE candidate | user choice | user choice | HYPOTHESIS | Browser shell removal support varies by Windows release/region/policy; WebView2 dependency must remain separate |
| `WIN.CONSUMER.PROMO_APPS` | Consumer/promotional provisioned apps | APPX group | REMOVE candidate | absent | absent | HYPOTHESIS group | Individual removable AppX packages can be deprovisioned; each concrete child requires exact identity/non-removable validation |
| `WIN.FEEDBACK.HUB` | Feedback Hub | APPX | REMOVE candidate | absent | absent | HYPOTHESIS | Candidate if provisioned/removable on target base; not a platform authority |
| `WIN.MAPS.CONSUMER` | Consumer maps UI/offline maps surfaces | APPX/CAPABILITY group | REMOVE candidate | absent unless dependency found | absent | HYPOTHESIS | Must distinguish app UI from location APIs consumed by other apps |
| `WIN.LOCATION.PLATFORM` | Core location platform/API | SUBSYSTEM | TBD | policy dependent | policy dependent | HYPOTHESIS | Do not conflate with Maps app; compatibility/privacy decision requires testing |
| `WIN.XBOX.GAMING_SERVICES` | Microsoft Gaming Services/runtime dependencies | SERVICE/PACKAGE/SUBSYSTEM | KEEP / version-specific | available | required when Microsoft Gaming title uses it | HYPOTHESIS-to-KEEP | SPEC-07 Microsoft Gaming support requires package/app activation dependencies; blanket Xbox removal is forbidden |
| `WIN.XBOX.APP` | Xbox PC application UX | APPX | TBD | user choice | optional GAME_CLIENT experience | HYPOTHESIS | Not equal to Gaming Services; can potentially be removable/reinstallable if validated |
| `WIN.GAMEBAR` | Xbox Game Bar / overlay UX | APPX/SUBSYSTEM | TBD | optional | optional/conflict-tested | HYPOTHESIS | SplitOS has own panel; removal/disable must be tested against controller shortcuts, capture, titles and Gaming Services |
| `WIN.NOTIFICATIONS.CONSUMER_PROMO` | Consumer/promotional notification mechanisms | POLICY/TASK group | DISABLE/REMOVE candidates | normal application notifications preserved | reduced distractions per GAME policy | HYPOTHESIS | Must distinguish useful notification infrastructure from promotional content |
| `WIN.TELEMETRY` | Windows diagnostic/telemetry components | SERVICE/TASK/POLICY group | TBD / minimize candidate | release policy | release policy | HYPOTHESIS | Exact removable/disableable parts and servicing/security implications require component-level evidence; no blanket folklore list |

## 6. Matrix interpretation rules

### 6.1 `KEEP` does not mean always active

`KEEP` means SplitOS preserves the component's platform responsibility.

A KEEP component may still have supported configuration/policy tuned by runtime or baseline logic.

### 6.2 `MODE_MANAGED` requires runtime contract

Before a row becomes `ACCEPTED MODE_MANAGED`, SPEC-05/SPEC-06 must be able to define:

```text
runtime target
apply mechanism
verification predicate
transition criticality
rollback behavior
startup reconciliation
```

### 6.3 `REMOVE` requires stronger evidence

Production acceptance requires at least:

```text
exact target identity mapped
supported/validated removal mechanism
post-removal absence verified
boot validation
Windows servicing/update validation
repair/recovery validation
required application compatibility validation
reinstall/repair consequences documented
```

### 6.4 Group rows are not execution targets

Example:

```text
WIN.CONSUMER.PROMO_APPS
```

is a decision family, not a DISM package name.

The release-specific matrix expands it to concrete children such as:

```text
component child A → package identity X on Windows Base R
component child B → package identity Y on Windows Base R
```

Only exact child mappings appear in Build Manifest operations.

## 7. Dependency direction

The matrix records both:

```text
requires[]
consumedBy[]
```

because a component that looks independently removable may be a hidden provider for:

- setup/OOBE;
- Microsoft Store/package deployment;
- Xbox/Gaming Services;
- authentication;
- repair/reset;
- Windows servicing;
- shell/system settings;
- third-party applications.

Unknown dependency is not evidence of no dependency.

## 8. Release-specific matrix

This document is the semantic v1 baseline.

Actual shipping release must materialize a machine-readable matrix version scoped to an exact supported Windows base, for example:

```text
component-matrix/windows-<baseId>/matrix-v1.json
```

SPEC-12 later defines release signing/trust for this artifact.

## 9. Change control

Classification change:

```text
KEEP → MODE_MANAGED
MODE_MANAGED → DISABLE
DISABLE → REMOVE
REMOVE → KEEP
```

is a product compatibility change and MUST create a new matrix version and associated verification evidence.

Existing installed baselines are not silently reinterpreted as though they had been built with the new classification.
