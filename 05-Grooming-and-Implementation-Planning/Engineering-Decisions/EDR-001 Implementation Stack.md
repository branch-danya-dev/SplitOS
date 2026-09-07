# EDR-001 — Implementation Stack

Status: `CLOSED`

## Decision

Primary implementation stack for first-party SplitOS Windows software:

```text
Language        C#
Runtime         .NET 10 LTS
Architecture    x64 first
Platform        Windows-only
Publishing      self-contained for first-party Windows executables
Interop         supported Win32 / COM / WinRT through narrow managed interop boundaries
```

Primary Windows executables covered by this decision:

- `SplitOS.RuntimeHost.exe`;
- `SplitOS.Broker.Service.exe`;
- `SplitOS.Manager.exe`;
- `SplitOS.GameLauncher.exe`;
- `SplitOS.UpdateBootstrap.exe` where WinRE is not involved;
- Builder and maintenance tooling where Windows runtime constraints allow it.

The SplitOS backend should use the same .NET 10 generation initially (ASP.NET Core) unless a later backend-specific decision provides a concrete reason to diverge.

## Why

The existing specification is heavily Windows-native but does not require a custom kernel/native runtime.

The selected stack directly supports:

```text
Windows Service
Named Pipes
async orchestration
SQLite
DPAPI
HTTP/OIDC
structured logging
ETW/WER integration
Win32/COM/WinRT interop
unit/component testing
self-contained deployment
```

A single primary language also reduces protocol/model duplication during the first implementation program.

## Why not native C++ as the default

C++ remains valid for narrow platform code, but making the entire Runtime/Broker/UI product native would add ownership/lifetime/build complexity without a requirement that justifies it.

The security boundary is provided by:

```text
process/token boundary
+ Broker capability model
+ caller validation
+ typed contracts
```

not by using C++.

## Why not Rust as the default

Rust offers valuable memory-safety properties but would increase the first-program integration cost across WinUI, COM/WinRT, Windows App SDK, service tooling and the existing .NET-oriented product/tooling ecosystem.

It remains a possible future choice for a narrowly isolated component if evidence justifies it.

## Native interop rule

Interop MUST remain narrow and owned by adapter/integration projects.

Forbidden pattern:

```text
any module
→ arbitrary P/Invoke / COM mutation
```

Required pattern:

```text
semantic owner
→ typed Windows adapter
→ narrow interop implementation
→ actual-state read-back
```

Hand-authored or source-generated interop bindings MAY be used. The binding-generation package is an implementation dependency, not an architectural authority.

## Publishing baseline

First-party Windows executables use self-contained .NET publishing initially.

Reason:

- prepared baseline does not need a separately installed machine-wide .NET runtime;
- release directories are more deterministic;
- previous SplitOS release can retain the runtime version it was built/tested with;
- rollback does not depend on a separately serviced .NET runtime.

Size overhead is accepted for the first implementation. It can be revisited only with measured distribution/storage evidence.

## Runtime version rule

The repository MUST pin an exact .NET SDK through `global.json`.

A runtime/SDK patch upgrade is a normal dependency change and must pass affected tests. `net10.0` is the product generation baseline; exact SDK patch is not inferred from developer-machine state.

## x64 scope

v1 engineering starts x64-only.

ARM64 is not forbidden, but becomes a separate support-matrix capability requiring:

- build/publish artifacts;
- Broker/service verification;
- WinUI verification;
- Windows integration adapter verification;
- game/client support evidence;
- SPEC-14 matrix coverage.

## Exception: WinRE recovery

This decision does not assume that the WinRE recovery executable can safely depend on the same managed runtime model.

`EDR-006` must prototype the actual WinRE environment.

If WinRE cannot reliably host a self-contained .NET recovery tool, a minimal native recovery executable MAY be selected by a dedicated decision. It must still implement only the bounded recovery contract from SPEC-11 and must not expose arbitrary shell execution.

## Acceptance evidence for Slice 0

The selected stack is considered proven for Slice 0 when code demonstrates:

```text
RuntimeHost.exe starts unelevated
Broker Service installs/runs
Runtime ↔ Broker Named Pipe hello succeeds
actual client PID/session can be observed
one supported Windows API read succeeds
unknown privileged capability is denied
tests execute through dotnet test
```

## Evidence sources

- Microsoft .NET support policy: https://dotnet.microsoft.com/en-us/platform/support/policy
- Microsoft Windows application development guidance: https://learn.microsoft.com/en-us/windows/apps/

As of this decision, .NET 10 is active LTS and is supported through November 2028.
