# SplitOS — Detailed Specification

## Purpose

Этот каталог переводит завершённый Analysis & Design baseline в implementable specifications.

Specification не переопределяет уже зафиксированные product/ownership/state/trust semantics. Если implementation evidence конфликтует с A&D, изменение должно пройти обратно через Decision/Requirement/A&D/Synthesis chain.

## Status

| Package | Status | Scope |
|---|---|---|
| `SPEC-01` Runtime Process & Module | READY FOR REVIEW | physical processes, Runtime Host module boundaries, startup/lifecycle, session cardinality, version compatibility |
| `SPEC-02` Local IPC & Privileged Broker | READY FOR REVIEW | Named Pipe transport, protocol, caller validation, broker capabilities, service hardening; machine-state persistence extension from SPEC-03 |
| `SPEC-03` Local Data & Persistence | READY FOR REVIEW | SQLite stores, machine/user/cache separation, schemas, durability, migrations, corruption recovery |
| `SPEC-04` Account/Auth/Entitlement | READY FOR REVIEW | native auth, backend contracts, token protection, account association, FREE/PRO, offline entitlement |
| `SPEC-05` Mode Runtime | READY FOR REVIEW | ACTIVATE/SWITCH/DEACTIVATE transaction model, blocker engine, mode policy, major mutation lease, rollback/reconciliation |
| `SPEC-06` Windows Context Integrations | READY FOR REVIEW | display/audio/input/power/process/services/hardware, supported Windows APIs, device identity, hot-plug and read-back verification |
| `SPEC-07` Game Client Adapters | READY FOR REVIEW | shared adapter contract, Steam/Epic/Microsoft Gaming/Battle.net capability model, library/install evidence, launch handoff and process/session correlation |
| `SPEC-08` Game Profile & Optimization | READY FOR REVIEW | multi-profile scenarios, deterministic hardware matching, field-level overrides, game-config adapters, recommendation engine, performance telemetry/drift reconciliation |
| `SPEC-09` Game Launcher & Shared Apps UX | READY FOR REVIEW | controller-first Launcher lifecycle/navigation, runtime binding, launch/return/error UX, Shared App assignments/window orchestration and capability-gated in-game panel |
| `SPEC-10` Builder & Component Matrix | READY FOR REVIEW | Windows source contract, strict Build Manifest, typed offline servicing, versioned component matrix, validation ladder, BuildReceipt and clean-install provisioning |
| `SPEC-11` Update & Recovery | READY FOR REVIEW | independent SplitOS update channel, validated Windows servicing coexistence, durable update/reboot transaction, previous-release recovery capsule, user-data-preserving rollback, WinRE recovery |
| `SPEC-12` Release Security & Key Management | READY FOR REVIEW | TUF repository trust, offline threshold root/targets/recovery roles, Authenticode publisher trust, key custody/rotation/revocation, anti-rollback and signing pipeline |
| `SPEC-13` Observability & Diagnostics | NEXT | events/correlation/privacy/retention |
| `SPEC-14` Verification & Acceptance | NOT STARTED | executable acceptance/test cases |

## Specification rules

```text
A&D semantic owner
→ Specification contract
→ implementation
→ verification
```

Normative keywords:

- **MUST / MUST NOT** — required for conformance;
- **SHOULD / SHOULD NOT** — strong default; deviation requires documented evidence;
- **MAY** — optional compatible behavior;
- **OPEN** — unresolved and must not be silently guessed in implementation.

## Current physical baseline

```text
Windows machine
│
├── SplitOSBroker Windows Service                exactly 1 / machine
│
└── Windows interactive sessions
    └── per eligible session
        ├── SplitOS.RuntimeHost.exe              exactly 1
        ├── SplitOS.Manager.exe                  0..1
        └── SplitOS.GameLauncher.exe             0..1
```

Only the active physical console session may own machine-wide managed Runtime control in v1.

## Current IPC baseline

```text
Manager / Game Launcher
        ↓ user-session Named Pipe
Runtime Host
        ↓ authenticated + authorized per-session Named Pipe
Privileged Broker
        ↓ bounded privileged operations
Windows machine state
```

No UI process may call the Privileged Broker directly.

## Current persistence baseline

```text
%ProgramData%\SplitOS\Data\machine.db
→ SQLite WAL + synchronous=FULL
→ machine canonical + durable transactions
→ Broker-mediated write boundary

%LocalAppData%\SplitOS\Data\user.db
→ SQLite WAL + synchronous=FULL
→ per-user canonical profiles/preferences/association metadata
→ Runtime Host write boundary

%LocalAppData%\SplitOS\Cache\projection.db
→ SQLite WAL + synchronous=NORMAL
→ rebuildable external projections
→ Runtime Host cache boundary
```

Reusable account credentials are not stored as ordinary SQLite plaintext fields.

Canonical persistence rules:

```text
storage writer != semantic owner
persistence commit != external target verification
projection cache != authoritative external truth
machine DB != directly writable by ordinary user processes
```

## Current account/auth baseline

```text
Windows user
→ SplitOS Runtime Host
→ external system browser
→ OAuth/OIDC Authorization Code + PKCE S256
→ loopback 127.0.0.1 callback
→ SplitOS Account
→ Entitlement capabilities
```

Reusable credentials:

```text
refresh token
→ user-scoped DPAPI
→ %LocalAppData%\SplitOS\Secrets\account.v1.dat
```

Runtime access:

```text
FREE
→ ManagedRuntime = DISABLED
→ base Windows desktop remains usable

PRO
→ server entitlement or valid bounded offline assertion
→ required capability present
→ ManagedRuntime = ENABLED
```

Offline premium baseline:

```text
JWS OfflineEntitlementAssertion v1
→ accountId + installationId + associationId bound
→ max 7 days
→ clock rollback check
→ never cachedPro=true
```

Hosted checkout/browser return never grants PRO directly; Runtime Host refreshes backend entitlement before changing access.

## Current Mode Runtime baseline

User-visible modes remain only:

```text
WORK
GAME
```

Physical durable mode operations are:

```text
ACTIVATE   NONE → WORK|GAME
SWITCH     WORK ↔ GAME
DEACTIVATE WORK|GAME → NONE
```

`NONE` is base/no-managed-mode state, not a third user mode.

Mode / Update / Recovery share one machine mutation lease with fencing. Target mode changes only after mandatory target verification plus atomic `CommitTransitionAndMode` persistence.

v1 startup policy:

```text
fresh physical-console Windows logon
→ reconcile incomplete work
→ BASE / NONE
→ FREE: Windows Desktop
→ PRO: mode selection
→ ACTIVATE WORK or GAME
```

Runtime Host restart inside the same logon preserves committed mode and reconciles actual state.

## Current Windows Context integration baseline

```text
Mode/Policy owner
→ typed Windows target
→ supported/version-gated adapter
→ apply operation
→ fresh Windows/device read-back
→ typed verification predicates
→ Mode owner decides commit
```

Current mechanism families:

```text
Display
→ QueryDisplayConfig / DisplayConfigGetDeviceInfo / SetDisplayConfig
→ temporary Work/Game display configuration by default
→ no SDC_SAVE_TO_DATABASE during ordinary mode switching

Audio
→ MMDevice/Core Audio enumeration/default observation/notifications
→ PKEY_AudioEndpoint_StableId preferred on Windows 11 24H2+ when present
→ automatic system-default setter remains OPEN
→ documented Windows Sound Settings user-mediated fallback

Input
→ Microsoft GameInput + supported redistributable
→ stable opaque device IDs for controller selectors
→ hot-plug callbacks invalidate input snapshot

Generic Hardware
→ Configuration Manager / SetupAPI
→ CM_Register_Notification + typed PnP evidence

Power
→ PowerGetActiveScheme / PowerSetActiveScheme
→ release-owned PowerPolicyId mapping
→ read-back verification

Processes
→ bounded Win32 process evidence
→ no generic unsaved-document inference
→ no generic TerminateProcess in normal mode switching

Managed Services
→ SCM APIs inside Privileged Broker
→ release-owned ManagedServiceId allowlist
→ QueryServiceStatusEx final-state verification
```

Common invariant:

```text
desired state
!= resolved Windows target
!= actual Windows evidence
```

Relevant device/OS notifications invalidate an adapter generation. A resolved target from a stale generation cannot silently continue.

## Current Game Client adapter baseline

```text
Game Library / Game Launch
→ GameClientAdapterRegistry
→ client-specific adapter
→ normalized installation/launch/process evidence
→ Game Library / Game Launch / Game Session owners
```

Client-level v1 posture:

```text
Steam              TARGET_SUPPORTED_V1
Epic               TARGET_SUPPORTED_V1
Microsoft Gaming   PARTIAL_SUPPORTED_V1
Battle.net         EXPERIMENTAL
```

Capability support is independent. A client can have a supported public launch mechanism while local library parsing remains version-sensitive.

Current launch mechanisms:

```text
Steam
→ Valve-documented steam:// protocol using Steam App ID
→ local library/install VDF/ACF = version-sensitive evidence

Epic
→ documented com.epicgames.launcher://apps/... protocol
→ Sandbox/Catalog/Artifact preferred identity
→ validated install-path protocol identity fallback
→ local launcher metadata = version-sensitive evidence

Microsoft Gaming
→ Windows package/app registration
→ PFN + AUMID identity
→ Windows application activation
→ local registered titles only; no claim of full Xbox/Game Pass cloud library

Battle.net
→ adapter boundary exists
→ discovery/launch/product metadata remain experimental until validated
```

Common launch invariant:

```text
HANDOFF_ACCEPTED
!= GAME_STARTING
!= GAME_RUNNING
```

Process/session correlation uses named proof sets and `PID + creationTime` where Win32 PID identity is involved. Weak foreground/timing evidence alone cannot establish a managed running game.

Adapters are unelevated/read-only integrations. They never collect external client passwords/tokens, modify client databases, inject into games, or convert local metadata into privileged command input.

## Current Game Profile & Optimization baseline

One game may own multiple independent profiles:

```text
Game
├── Desktop profile
└── Living-room / TV profile
```

A profile carries desired scenario/device/performance intent and field-level user locks. It does not store actual hardware/game state as intent.

Profile resolution:

```text
fresh hardware/display/input evidence
→ profile eligibility
→ deterministic selection order
→ immutable ResolvedProfileContext
→ generation validation before apply
```

No opaque weighted profile score is used. Material ambiguity/fallback requires explicit deterministic handling or user choice.

Optimization precedence:

```text
hard compatibility/platform constraints
> valid field-level user locks
> explicit profile intent
> current optimizer recommendation for AUTO fields
> release safe/default game knowledge
> unmanaged game defaults
```

Optimization objective:

```text
stable useful performance target
subject to user locks / scenario constraints
then maximize visual quality
```

Recommendation uses release-owned per-game setting definitions, legal values, dependencies and degradation/upgrade ladders. Game configuration writes are performed only through a typed per-game configuration adapter with source-digest conflict detection and read-back verification.

Normal v1 gameplay is not continuously reconfigured. Static recommendation applies before launch; optional measured evidence refines a future recommendation or explicit calibration run.

Performance telemetry:

```text
PerformanceTelemetryAdapter
→ PresentMon-compatible provider is primary v1 candidate
→ exact service vs embedded packaging remains engineering validation gate
```

Average FPS alone is not sufficient; frame-time distribution/stability is part of target evaluation.

External game-setting drift is preserved for the immediate launch and surfaced for reconciliation. It does not silently become a permanent SplitOS user override.

Vendor driver profile/tuning APIs are optional future capabilities; core v1 optimization does not require overclock/undervolt/fan control or implicit NVAPI/ADLX mutation.

## Current Game Launcher & Shared Apps baseline

Launcher role:

```text
Runtime truth
→ Game Launcher presentation
→ semantic user requests
→ Runtime Host owners
```

`SplitOS.GameLauncher.exe` is unelevated, presentation-only and normally resident while GAME is committed. It can reach `READY_PRECOMMIT` to satisfy launcher readiness, but full active Game UX appears only after Runtime reports committed `GAME`.

When a managed game becomes `GAME_RUNNING`:

```text
Launcher foreground/input → released
Launcher process → resident/background by default
Game → owns ordinary gameplay foreground/input
```

After confirmed game exit:

```text
GAME remains committed
→ Launcher foreground restored
→ pre-launch semantic route/focus bookmark restored when valid
```

Controller navigation uses semantic actions and deterministic logical focus. Keyboard/mouse remain recovery-compatible. The exact global controller chord for an in-game SplitOS panel remains an engineering gate; hidden Launcher does not process ordinary gameplay buttons.

Shared Apps:

```text
maximum active assignments = 3

OVERLAY
LOCKED_WINDOW
SECONDARY_DISPLAY
BACKGROUND
```

Shared App assignment/presentation is separate from application/process ownership. v1 window presentation uses ordinary user-session Win32/DWM top-level window orchestration with generation-bound window/display evidence and read-back verification.

Key invariants:

```text
HWND != persistent app identity
SetWindowPos success != placement verified
overlay requested != overlay guaranteed visible
Shared App configured != currently visible
```

`OVERLAY` is capability-gated and is not guaranteed over exclusive fullscreen/protected presentation. SplitOS does not use DLL injection, game hooks or anti-cheat bypass to force presentation.

The in-game SplitOS panel is optional/capability-gated. Game/session/mode correctness does not depend on panel availability.

## Current Builder & Component Matrix baseline

Supported v1 composition path:

```text
Microsoft-authorized Windows source
→ SourceIdentity / approved release source constraint
→ immutable working copy
→ versioned strict BuildManifest
→ versioned Windows Component Matrix
→ typed offline servicing executor
→ offline postcondition verification
→ image commit
→ media/deployment assembly
→ output verification
→ BuildReceipt + baseline descriptor
```

The production Builder supports a user-provided Windows source in v1. Automatic source acquisition remains OPEN pending legal/licensing and technical validation.

`BuildManifest` canonical execution form is strict schema-validated JSON. It contains only typed release-owned operations; arbitrary PowerShell/command/registry/path primitives are forbidden.

Component lifecycle remains:

```text
REMOVE
DISABLE
MODE_MANAGED
KEEP
TBD
```

but classification and validation status are independent. Destructive `REMOVE` requires mechanism, boot, servicing, recovery and compatibility evidence before production acceptance.

Current examples:

```text
core boot/servicing/recovery/network/display/audio/input → KEEP
Microsoft Store/application deployment substrate         → KEEP
Phone Link / Search / Print                              → MODE_MANAGED candidates
consumer/promotional removable AppX                     → REMOVE candidates
Defender Antivirus                                      → desired REMOVE candidate, still TBD/not accepted
Edge browser shell                                      → TBD/remove candidate; separate from WebView2 runtime
Gaming Services                                         → preserve required Microsoft Gaming dependencies
```

Build success is not a command exit code. All mandatory postconditions and final output verification must pass. Successful build emits a `BuildReceipt` bound to source identity, manifest/matrix/package digests, Builder/toolchain identity and verification results.

Clean installation of the verified prepared baseline is the supported v1 product path. Mutation of an arbitrary existing Windows installation is not equivalent.

## Current Update & Recovery baseline

Update authority is split explicitly:

```text
Microsoft Windows servicing
→ Microsoft-signed Windows patch payload source

SplitOS Compatibility
→ decides whether Windows patch/build is supported

SplitOS Update Channel
→ SplitOS-owned wrapper/runtime/knowledge/recovery payload source
```

Automatic application of unvalidated Windows feature/system changes remains controlled per the existing update requirements, but SplitOS does not remove/replace the Windows servicing infrastructure and does not rehost Microsoft patch binaries inside the wrapper feed.

SplitOS wrapper update flow:

```text
signed release envelope
→ compatibility / Windows-servicing gate
→ download + verify
→ stage target release
→ create + verify previous-release Recovery Capsule
→ UPDATE mutation lease
→ trusted one-shot Update Bootstrap
→ activate target
→ [reboot/resume]
→ verify Broker / Runtime / DB / Windows compatibility
→ atomic InstalledSplitOSRelease commit
```

Mandatory local rollback target:

```text
Current release N
→ create + seal + verify capsule N
→ only then activate N+1
```

The previous-release capsule lives in a hidden SplitOS Recovery Store on the same device, separate from ordinary user data and conceptually separate from Windows RE tools.

Recovery invariant:

```text
software rollback
!= user-data rollback
```

Per-user canonical data remains live. Production releases must preserve at least one previous-release rollback compatibility or provide a tested rollback bridge. Normal rollback never restores an old `%UserProfile%` or old `user.db` snapshot that would erase user changes made after update.

When normal SplitOS runtime cannot recover, a bounded SplitOS Recovery Tool may run from Windows RE and restore validated SplitOS-owned payload/machine state from the local capsule. Windows-level corruption remains a Windows-native recovery responsibility.

A same-device capsule is a fast last-known-good recovery mechanism, not protection against physical disk/device loss.

## Current Release Security & Key Management baseline

Production update trust now has two independent layers:

```text
TUF repository authorization
+
Windows Authenticode publisher authorization
```

Repository trust:

```text
embedded production Root
→ sequential Root rotation
→ Timestamp
→ Snapshot
→ Targets / delegated roles
→ exact Release Envelope target
→ exact artifact digests
```

Production key hierarchy uses role separation. Root and top-level Targets are offline threshold authorities; stable release, knowledge, snapshot and timestamp metadata use separate protected signing keys; recovery downgrade authorization has its own threshold-controlled role. Production root baseline is `2-of-3` across three independent root keys.

SplitOS executable artifacts must additionally satisfy Authenticode publisher validation. Production code-signing keys are non-exportable/hardware-backed where supported, use SHA-256 file digests and RFC 3161 SHA-256 timestamping, and are separate from TUF metadata keys.

Normal downgrade remains forbidden even for historically valid signed artifacts. Recovery is a separate capability:

```text
current release N+1
→ exact signed RecoveryAuthorization(N+1 → N)
→ locally trusted security floor/revocation checks
→ controlled recovery to N
```

Clients persist trusted metadata/version/security floors outside rebuildable update cache so cache deletion, reboot or CDN rollback cannot reset anti-rollback state.

CI/build workers never receive raw production private keys. Signing is performed by capability-scoped signing services/HSM-backed keys after release approval, with immutable final artifact hashes bound into release metadata.

## Current specification artifacts

```text
SPEC-01-Runtime-Process-and-Module/
SPEC-02-Local-IPC-and-Privileged-Broker/
SPEC-03-Local-Data-and-Persistence/
SPEC-04-Account-Auth-and-Entitlement/
SPEC-05-Mode-Runtime/
SPEC-06-Windows-Context-Integrations/
SPEC-07-Game-Client-Adapters/
SPEC-08-Game-Profile-and-Optimization/
SPEC-09-Game-Launcher-and-Shared-Apps-UX/
SPEC-10-Builder-and-Component-Matrix/
SPEC-11-Update-and-Recovery/
SPEC-12-Release-Security-and-Key-Management/
```

## Source architecture

Detailed Specification is based on:

```text
03-Analysis-and-Design/11-Synthesis/
```

and ultimately preserves the canonical models from Boundaries through Trust.