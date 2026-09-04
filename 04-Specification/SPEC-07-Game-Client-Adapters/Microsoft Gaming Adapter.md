# SPEC-07 — Microsoft Gaming / Xbox Adapter

## 1. Purpose

Defines the v1 adapter for Windows games installed through Microsoft Store / Xbox app / Gaming Services where the title has usable Windows package/application registration.

This adapter is intentionally named:

```text
MICROSOFT_GAMING
```

rather than treating the Xbox desktop app process itself as the game authority.

Current v1 posture:

```text
PARTIAL_SUPPORTED_V1
```

because Windows exposes strong package/application identity and activation mechanisms, but SplitOS does not assume a public universal API for the user's complete Xbox/Game Pass library or proactive license eligibility.

---

## 2. Authority boundary

Microsoft Store / Xbox / Gaming Services remain authority for:

- acquisition;
- ownership/subscription;
- licensing;
- package registration;
- updates;
- Gaming Runtime context.

SplitOS observes Windows registration and performs supported application activation.

It does not emulate Xbox identity or launch the encrypted/raw game executable directly.

---

## 3. Capability baseline

| Capability | v1 mechanism | Status |
|---|---|---|
| CLIENT_DISCOVERY | Microsoft Gaming/Xbox app presence is auxiliary only | `SUPPORTED_OS_MECHANISM` evidence |
| CLIENT_VERSION_EVIDENCE | package/app version evidence where applicable | `SUPPORTED_OS_MECHANISM` |
| LIBRARY_DISCOVERY | only known/registered package/AUMID-backed titles; not universal account library | `PARTIAL_SUPPORTED_V1` |
| INSTALLATION_EVIDENCE | Windows package/application registration for current user | `SUPPORTED_OS_MECHANISM` |
| LAUNCH_IDENTITY_RESOLUTION | Package Family Name + AUMID/app registration | `SUPPORTED_OS_MECHANISM` |
| GAME_LAUNCH | Windows application activation by AUMID | `SUPPORTED_OS_MECHANISM` / package-model gated |
| LAUNCH_ELIGIBILITY_EVIDENCE | no universal proactive license API assumed | `OPEN` / platform-owned |
| CLIENT_INTERACTION_EVIDENCE | Windows/Xbox/Gaming Services UI as external UX | `PARTIAL` |
| GAME_PROCESS_CORRELATION | activation/process/package identity | `SUPPORTED_VERSION_GATED` |
| GAME_EXIT_CORRELATION | correlated process/application set | `SUPPORTED_OS_MECHANISM` |
| ACCOUNT_CONTEXT_EVIDENCE | Microsoft/Xbox account details not required | `UNSUPPORTED` |
| UPDATE_STATE_EVIDENCE | package status may provide limited evidence; platform owns actual update workflow | `PARTIAL` |

---

## 4. Windows identity basis

Windows uses Application User Model IDs (AUMIDs) to identify installed applications independently from display name or installation path.

Microsoft also exposes package enumeration/registration APIs through Windows package management.

Relevant supported Windows mechanisms include conceptually:

```text
PackageManager / package registration APIs
Package Family Name
AUMID
IApplicationActivationManager::ActivateApplication
```

Microsoft GDK registration additionally requires app-launch semantics so Gaming Runtime/game context is correctly available.

---

## 5. Why raw EXE launch is forbidden

For Microsoft Gaming titles:

```text
find executable
→ CreateProcess
```

is not an accepted generic launch mechanism.

A GDK game may require application registration/context from Gaming Services and the Windows app repository.

Therefore:

```text
registered application identity
→ Windows app activation
```

is the v1 supported direction.

---

## 6. External identity

Preferred binding:

```text
MicrosoftGameIdentityV1
{
  packageFamilyName
  appUserModelId
  packageIdentityName?
  storeProductId?      // metadata only when known
}
```

Canonical external key for a concrete installed application is:

```text
PFN + AUMID
```

not:

- install folder;
- display title;
- Xbox app tile text;
- executable filename.

---

## 7. Discovery strategy

Two discovery paths exist.

### A. Release-known game binding

For officially supported titles SplitOS compatibility knowledge MAY contain known package/application identity selectors.

Runtime queries Windows registration for the current Windows user and determines whether a matching package/AUMID exists.

### B. General registered-app observation

SplitOS MAY enumerate current-user package/application registration to surface candidate Microsoft Gaming titles.

However generic enumeration does not automatically prove:

```text
this package is an Xbox/Game Pass game supported by SplitOS
```

Mapping must use product/package metadata and compatibility rules.

---

## 8. Package enumeration

Candidate supported API family:

```text
Windows.Management.Deployment.PackageManager
FindPackagesForUser(...)
```

or narrower package-family queries when a PFN is already known.

Rules:

- query current user context;
- do not request all-user administrative enumeration for normal Game Launcher behavior;
- preserve package identity/version/status evidence;
- do not infer license ownership from package presence.

---

## 9. Installation evidence

Strong local evidence:

```text
expected package/application registration exists for current Windows user
+
expected PFN/AUMID resolves
+
package is not in an obviously unusable registration/status condition
```

Result:

```text
INSTALLED_VERIFIED_EVIDENCE
```

Again, this means Windows currently has the expected registered application; Microsoft platform remains canonical install/license owner.

---

## 10. Flat File Install nuance

Modern GDK PC games may use Flat File Install so content can exist in user-selectable folders such as Xbox game folders.

File discoverability does not change SplitOS launch semantics:

```text
files visible on disk
!= safe to direct-launch executable
```

Application identity/registration remains the preferred launch anchor.

---

## 11. Launch preparation

Required fresh evidence:

```text
PFN/AUMID registration
+
package/application compatibility status
+
current interactive user session
```

Prepared identity:

```text
MICROSOFT_AUMID
```

No arbitrary app-specific arguments are included by default.

---

## 12. Launch mechanism

Preferred v1 activation mechanism:

```text
IApplicationActivationManager::ActivateApplication(
  appUserModelId,
  no arbitrary args,
  normal options,
  returnedProcessId
)
```

This activates the registered Windows application in the current session.

A documented shell `shell:AppsFolder\<AUMID>` activation MAY be retained as a compatibility fallback only when tested for the applicable game/package class.

SplitOS MUST NOT depend on development-only GDK tooling such as `wdapp.exe` in the retail runtime.

---

## 13. Activation process ID

`ActivateApplication` may return a process ID.

This is useful strong evidence, but it still does not make the API return equivalent to `GAME_RUNNING`.

Possible flow:

```text
ActivateApplication success + PID
→ HANDOFF_ACCEPTED
→ resolve PID + creation time + package/application identity
→ observe bootstrap/replacement
→ GAME_RUNNING_CONFIRMED
```

The returned PID may represent a bootstrap/application process whose lifecycle must still be correlated.

---

## 14. Xbox app process

`XboxPcApp.exe` or other Xbox UI process presence is not required proof that a specific game is available and is never Game Session evidence.

The Xbox app may be useful only as:

```text
client/platform UX availability evidence
```

or user-mediated fallback.

---

## 15. Authentication/license interaction

SplitOS does not proactively determine Game Pass/subscription/license ownership from undocumented account data.

If Windows/Xbox/Gaming Services activation requires:

- account sign-in;
- license resolution;
- purchase/subscription;
- update/repair;

that UX remains external.

Unless a supported signal distinguishes the reason, SplitOS reports:

```text
CLIENT_INTERACTION_REQUIRED
```

or `GAME_PROCESS_NOT_CONFIRMED` after timeout.

---

## 16. Process correlation

Strong evidence may combine:

```text
returned activation PID
+
expected AUMID/PFN
+
current user session
+
Windows package/application identity evidence
```

For games that replace the initial process, adapter follows a release-owned replacement policy and normal SPEC-06 process evidence.

---

## 17. Package identity over path identity

If a process image path is observable it MAY strengthen correlation.

But package/AUMID identity is preferred because install folders can move and modern Xbox game content may use Flat File Install.

Persistent binding MUST NOT depend only on:

```text
C:\XboxGames\GameName\...
```

---

## 18. Exit correlation

Exit confirmation uses the correlated application/game process set.

Xbox app / Gaming Services background process lifetime is excluded.

If application activation spawns/replaces processes, the session remains active while a permitted correlated replacement exists.

---

## 19. Library scope limitation

v1 MUST distinguish:

```text
installed registered Microsoft Gaming titles SplitOS can identify
```

from:

```text
all titles owned/available in user's Xbox/Game Pass cloud library
```

The latter is not provided by this specification.

Game Launcher language must not imply full account-library synchronization if only local registered games are available.

---

## 20. Package update/version changes

Package identity is stable across normal package versions, while package full name/version changes.

Persistent binding therefore prefers:

```text
Package Family Name + AUMID
```

not package full name/version.

Version/status remains freshness/compatibility evidence.

---

## 21. Package registration invalidation

Relevant install/uninstall/package changes invalidate Microsoft Gaming projections.

Implementation may use package deployment notifications or refresh on:

```text
Runtime start
Game Launcher open
launch request
known package change event
user refresh
```

A stale package projection cannot satisfy a fresh launch requirement.

---

## 22. Security boundary

Adapter MUST NOT:

- take ownership of WindowsApps/Xbox game files;
- change ACLs to reach game executables;
- bypass Gaming Services registration;
- launch encrypted/protected game executables directly;
- collect Microsoft/Xbox credentials;
- manipulate license state;
- depend on developer sandbox tools in retail runtime.

---

## 23. Unsupported/OPEN areas

Current SPEC-07 does not promise:

```text
full Xbox/Game Pass cloud library enumeration
proactive license/subscription eligibility check
universal support for every historical Microsoft Store packaging model
retail dependency on wdapp/GDK developer tools
launching games with no usable registered application identity
```

Those may be added only after supported evidence is validated.

---

## 24. Verification cases

```text
V-MSFT-001 known PFN/AUMID detected for current user
V-MSFT-002 package version change preserves stable binding
V-MSFT-003 missing registration reports not installed/unknown correctly
V-MSFT-004 AUMID launch produces HANDOFF_ACCEPTED only
V-MSFT-005 returned activation PID is correlated by PID+creation identity
V-MSFT-006 bootstrap replacement does not falsely mark exit
V-MSFT-007 Xbox app remains running after game exit without blocking EXIT_CONFIRMED
V-MSFT-008 install folder move does not break PFN/AUMID binding
V-MSFT-009 direct executable fallback absent
V-MSFT-010 package present does not become proactive license=owned claim
V-MSFT-011 stale package evidence cannot satisfy fresh launch
V-MSFT-012 unsupported package model is surfaced explicitly
```

---

## 25. Release support label

The Microsoft adapter remains:

```text
PARTIAL_SUPPORTED_V1
```

until verification establishes the exact package/application models that SplitOS supports.

Release notes/Launcher should communicate the supported subset rather than claiming universal Xbox/Game Pass integration.
