# SLICE-00 — Delivery Handoff

Status: `READY_FOR_DELIVERY`

## Purpose

This document marks the boundary between design/planning and implementation for SplitOS.

The four Slice-0 blocking decisions are closed:

```text
EDR-001 implementation stack          CLOSED
EDR-002 UI framework                  CLOSED
EDR-003 build/package orchestration   CLOSED
EDR-004 integration-test environment  CLOSED
```

Therefore the next change set should contain real source code.

## Coding starts here

The first implementation branch/PR should be:

```text
delivery/slice-00-engineering-foundation
```

It should establish a compilable/runnable source tree rather than another specification layer.

## Required first code scope

Minimum implementation:

```text
global.json
Directory.Build.props
Directory.Packages.props
SplitOS.sln

src/
├── SplitOS.RuntimeHost/
├── SplitOS.Broker.Service/
├── SplitOS.Manager/
├── SplitOS.GameLauncher/
└── SplitOS.Contracts/

tests/
├── SplitOS.Contracts.Tests/
├── SplitOS.RuntimeHost.Tests/
├── SplitOS.Broker.Tests/
└── SplitOS.IntegrationTests/
```

The exact project tree may include additional narrow infrastructure projects from the Grooming topology, but must not create a generic `Shared` project that bypasses semantic ownership.

## First vertical

The first code milestone is intentionally narrow:

```text
Windows user session
↓
SplitOS.RuntimeHost.exe
↓ authenticated local IPC
SplitOS.Broker.Service.exe (Windows Service)
```

Plus:

```text
Manager → Runtime UI pipe
Launcher → Runtime UI pipe
```

No mode switching, account backend or gaming breadth is required in the first PR.

## Protocol scope

Implement only the protocol skeleton needed to prove the boundary:

```text
ProtocolHello
ProtocolHelloAck
HealthReadRequest
HealthReadResult
ErrorResponse
```

Common envelope carries at least:

```text
protocolVersion
requestId
operationId
correlationId
```

Broker-side connection evidence must use OS-derived caller/session identity where available rather than trusting claimed values from the request payload.

## Broker capability scope

Allowed first capability:

```text
Broker.Health.Read
```

Required negative tests:

```text
unknown capability
→ DENIED

arbitrary command capability name
→ DENIED

UI process attempts direct Broker use
→ denied by ACL/auth boundary where test topology permits

wrong/non-control session mutation-style request
→ DENIED
```

Slice 0 MUST NOT introduce:

- `RunCommand`;
- generic PowerShell execution;
- arbitrary registry path mutation;
- arbitrary service-name mutation;
- arbitrary executable launch through Broker.

## Process requirements

### Runtime Host

```text
normal user token
one instance per eligible Windows session
no elevation prompt
structured startup/shutdown events
```

### Broker

```text
Windows Service
LocalSystem baseline
one per machine
narrow Named Pipe server
no user account tokens
no generic network service role
```

### Manager / Game Launcher

For Slice 0 they can be minimal WinUI shells proving:

```text
process starts
↓
connects to Runtime
↓
receives runtime health/version snapshot
```

No final UX is required.

## Build requirements

A clean checkout must support:

```text
dotnet restore
dotnet build
dotnet test
```

and publish x64 artifacts using the exact SDK/dependency versions pinned in the repository.

Hosted CI must verify build/unit/component tests.

## Integration acceptance

Hyper-V lane must prove:

```text
fresh VM
↓
install Broker Service
↓
start Broker
↓
launch RuntimeHost in interactive session
↓
ProtocolHello succeeds
↓
Broker.Health.Read succeeds
↓
unknown capability denied
↓
caller PID/session recorded from OS evidence
↓
restart RuntimeHost
↓
reconnect works
↓
reboot VM
↓
service/runtime test can resume
```

## Definition of Done

SLICE-00 is done when:

- repository builds from a clean checkout;
- RuntimeHost/Broker/Manager/Launcher projects exist and run in their specified privilege context;
- UI processes cannot become privileged mutation owners;
- Runtime↔Broker protocol hello/version negotiation works;
- actual caller/session evidence is available to Broker authorization code;
- first allowlisted read-only capability works;
- unknown/generic privileged requests fail closed;
- correlation/version fields are emitted in structured events;
- unit/component tests run in hosted CI;
- positive/negative service+IPC integration runs in the disposable Windows lab;
- implementation evidence does not contradict SPEC-01/02/13/14.

## Out of scope for this first code PR

Do not pull these forward:

- real WORK/GAME transition;
- SQLite canonical schemas beyond a placeholder only if needed;
- OAuth/account implementation;
- display/power mutation;
- Steam adapter;
- Game Profiles;
- Builder servicing;
- update/recovery;
- production signing.

Those belong to later slices.

## Lifecycle transition

After this handoff is accepted:

```text
05 Grooming
    DONE for Slice 0

06 Delivery Support
    ACTIVE

Implementation
    STARTS
```

From this point, new implementation evidence may trigger updates back into SSAD knowledge, but documentation work no longer precedes every code change as a separate phase.
