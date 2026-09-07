# SplitOS Slice-00 Windows Lab

This harness proves the real Windows process/privilege topology of the engineering foundation on a clean Windows 11 machine or VM.

## What it installs

```text
C:\Program Files\SplitOS\Releases\slice00-dev\
├── Broker\
├── RuntimeHost\
├── Manager\
└── GameLauncher\
```

Machine topology:

```text
SplitOSBroker Windows Service
→ LocalSystem
→ Automatic
→ Session 0

RuntimeHost scheduled task
→ current Windows user
→ interactive logon
→ RunLevel Limited
```

The release directory ACL grants:

- LocalSystem: Full Control;
- Builtin Administrators: Full Control;
- Builtin Users: Read/Execute only.

## Run

From an elevated PowerShell session on the target Windows machine:

```powershell
./eng/publish.ps1 -Configuration Release
./eng/lab/install-slice00.ps1
./eng/lab/verify-slice00.ps1
```

Cleanup:

```powershell
./eng/lab/uninstall-slice00.ps1 -RemoveReleaseFiles
```

## Verification meaning

`verify-slice00.ps1` requires all of the following:

```text
Broker service = Running + Automatic + LocalSystem + Session 0
RuntimeHost = exact release image + current interactive session
RuntimeHost task = Limited
release root = ordinary Users cannot write
Manager --health-probe = exit 0
```

A successful Manager probe means:

```text
Manager
→ Runtime UI pipe
→ RuntimeHost
→ Broker pipe
→ Broker.Health.Read
→ Runtime health = HEALTHY
```

So this is an end-to-end process/privilege proof, not only four independent process launches.

## Security boundary

This Slice-00 hardening adds:

```text
explicit pipe ACL
+
OS-derived PID/session
+
exact protected release path
```

It does **not** complete production release trust.

```text
exact protected path
!= Authenticode publisher verification
!= authorized SplitOS release provenance
```

Authenticode, release metadata authorization, key rotation/revocation and final publisher policy remain owned by SPEC-12 / release-security delivery.

The v1 threat model also does not claim resistance to a hostile unrestricted local administrator, kernel, firmware or hypervisor.

## VM usage

A clean Windows 11 Hyper-V VM is the preferred manual acceptance environment for this harness. Reboot the VM after installation and rerun `verify-slice00.ps1` to prove automatic Broker startup plus RuntimeHost logon startup.
