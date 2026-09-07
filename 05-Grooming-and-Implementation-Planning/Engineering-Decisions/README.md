# SplitOS — Engineering Decision Pack

## Purpose

This pack closes the four implementation decisions that blocked `SLICE-00 Engineering Foundation` after Grooming.

```text
EDR-001 implementation stack          CLOSED
EDR-002 desktop UI framework          CLOSED
EDR-003 build/package orchestration   CLOSED
EDR-004 Windows integration lab       CLOSED
```

These decisions do not redefine product semantics. They bind the already approved SPEC contracts to an implementation technology baseline so delivery can begin.

## Decision summary

| Decision | Selected baseline |
|---|---|
| EDR-001 | C# / .NET 10 LTS, Windows x64 first, self-contained first-party Windows executables |
| EDR-002 | WinUI 3 on Windows App SDK stable 2.4.x, unpackaged + self-contained for Manager and Game Launcher |
| EDR-003 | monorepo + `dotnet`/MSBuild + pinned SDK/dependencies + GitHub Actions; product delivery remains Builder/update-channel based, not MSIX/App Installer based |
| EDR-004 | Hyper-V Gen2 disposable VM lane using golden VHDX + differencing disks + PowerShell Direct; physical hardware lane for GPU/display/input/game/performance |

## Delivery consequence

The previous state was:

```text
SLICE-00
= BLOCKED_BY_DECISION
```

After this pack is accepted:

```text
SLICE-00
= READY_FOR_DELIVERY
```

The next PR should therefore contain implementation, not another architecture layer:

```text
SplitOS source/build skeleton
+ RuntimeHost process
+ Broker Windows Service
+ UI/Runtime Named Pipe skeleton
+ Runtime/Broker Named Pipe skeleton
+ protocol hello/versioning
+ caller/session evidence
+ one read-only broker capability
+ negative privilege-boundary tests
```

## Evidence baseline

The implementation choices are based on current supported platform evidence as of 2026-09:

- .NET 10 is an active LTS release with support through 2028-11;
- Microsoft recommends WinUI 3 / Windows App SDK for new native Windows desktop applications;
- Windows App SDK supports unpackaged and self-contained desktop deployment;
- Hyper-V + PowerShell Direct supports Windows VM automation independent of guest networking;
- GitHub self-hosted runners can execute workloads on custom Windows/virtual/physical infrastructure.

Exact SDK/package patch versions MUST be pinned in the implementation repository and changed only by explicit dependency updates.

## Important exceptions

This pack does **not** force every future executable to be managed .NET code.

A narrow native helper MAY be introduced later only when concrete evidence demonstrates that an already specified Windows/WinRE capability cannot be safely or supportably implemented with the selected managed stack. Such an exception requires its own engineering decision and must not create a generic privileged/native escape hatch.

The most likely candidate for such research remains the WinRE recovery executable/runtime environment.

## Next lifecycle state

```text
Grooming decisions closed
↓
SLICE-00 READY_FOR_DELIVERY
↓
Delivery Support starts
↓
first implementation PR
```
