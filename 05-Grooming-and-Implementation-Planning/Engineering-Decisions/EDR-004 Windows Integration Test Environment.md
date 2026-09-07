# EDR-004 — Disposable Windows Integration-Test Environment

Status: `CLOSED`

## Decision

Canonical automated Windows integration environment:

```text
Hyper-V Generation 2 VM
+
golden approved Windows base VHDX
+
per-run differencing disk
+
PowerShell Direct orchestration
+
self-hosted GitHub Actions runner on the Hyper-V host
```

Physical hardware remains a separate mandatory verification lane for GPU/display/input/audio/game/performance scenarios.

## Why Hyper-V

SplitOS must verify behaviors that ordinary unit tests and lightweight sandboxes cannot adequately model:

- Windows Service installation/start/stop;
- LocalSystem Broker behavior;
- Named Pipe session/caller authorization;
- multiple Windows sessions;
- reboot/resume;
- update activation;
- persistence/crash reconciliation;
- prepared Windows installation;
- WinRE/recovery experiments;
- disk layout and Recovery Store;
- destructive fault injection.

Hyper-V Gen2 provides a real Windows kernel/boot lifecycle while remaining disposable and automatable.

## Golden image model

For every engineering-supported Windows baseline, lab automation owns an immutable golden VHDX/template identity.

Example concept:

```text
Golden-Win11-BaseA.vhdx     READ ONLY
        │
        ├── run-001.diff.vhdx
        ├── run-002.diff.vhdx
        └── run-003.diff.vhdx
```

Each test run gets a fresh differencing child and therefore does not depend on cleanup scripts successfully restoring machine state.

The golden image itself is rebuilt/versioned when its source/base definition changes.

## Why differencing disks over long-lived mutable test VMs

The lab MUST prefer reset-by-discard over reset-by-cleanup.

Bad model:

```text
one integration VM
↓
run 300 tests
↓
hope uninstall/registry/service cleanup restored everything
```

Required model:

```text
known parent
↓
fresh child disk
↓
test
↓
collect evidence
↓
destroy child
```

Hyper-V checkpoints MAY be used inside individual fault-injection scenarios, but they are not the primary clean-room mechanism.

## PowerShell Direct

The Hyper-V host controls guest setup/execution through PowerShell Direct where applicable.

Benefits:

- does not require guest network configuration;
- can copy/run bootstrap scripts from host to guest;
- useful before product networking is stable;
- supports reboot-oriented integration workflows.

Guest commands are test-harness operations only; PowerShell Direct does not become a production SplitOS management interface.

## Self-hosted CI runner

The Hyper-V host is attached as a restricted self-hosted GitHub Actions runner or equivalent lab executor.

It must be scoped so untrusted arbitrary external PR code cannot freely control the privileged Hyper-V host.

The lab workflow consumes artifacts already built by the hosted build lane:

```text
hosted CI
→ publish artifact
→ immutable artifact identity
→ integration workflow
→ fresh VM
→ install/copy exact artifact
→ execute test
→ evidence
```

Integration VM jobs MUST NOT silently rebuild binaries and thereby test a different candidate.

## VM test lanes

### VM-01 Runtime / IPC

Validates:

- RuntimeHost user-session startup;
- Broker Windows Service;
- LocalSystem identity;
- Named Pipe ACL;
- actual PID/session checks;
- cross-session denial;
- capability dispatch;
- idempotency/correlation basics.

### VM-02 Persistence / crash

Validates:

- machine/user SQLite locations;
- writer boundaries;
- process kill/restart;
- transaction recovery;
- disk-full/corruption fixtures where safely injectable.

### VM-03 Mode / Windows context

Validates VM-safe portions:

- Power scheme;
- managed service operations;
- process evidence;
- basic display API behavior available under VM;
- transition journal/rollback.

Physical display semantics are not inferred from this lane.

### VM-04 Builder / install

Validates:

- prepared image generation;
- clean installation;
- OOBE/first sign-in;
- SplitOS bootstrap;
- BuildReceipt/baseline identity.

### VM-05 Update / recovery

Validates:

- side-by-side release staging;
- previous-release capsule;
- reboot/resume;
- hard power-off equivalents;
- rollback convergence;
- recovery metadata;
- WinRE experiments where supported by the VM topology.

## Physical hardware lane

VM PASS never proves these capabilities:

```text
real GPU/driver performance
multi-monitor identity/topology
4K/high-refresh/HDR
controller hot-plug/timing
default audio endpoint behavior
Steam/Epic/Microsoft Gaming launch behavior
anti-cheat-sensitive game scenarios
frametime regression
```

Those require a controlled physical hardware matrix under SPEC-14.

## Windows Sandbox position

Windows Sandbox MAY be used as a developer convenience for quick disposable smoke experiments.

It is **not** the canonical integration/release environment because SplitOS needs stronger control over:

- exact image identity;
- persistent/reboot test sequences;
- virtual disk layout;
- prepared-image boot;
- update/recovery checkpointing;
- reproducible machine baselines.

## Multi-session testing

Because SPEC-01 allows Runtime Host per Windows session but only the active physical console session may control machine-wide state, the lab MUST include explicit multi-session tests.

At minimum prove:

```text
Session A = control owner
Session B = logged in/non-owner

B requests machine mutation
→ denied

control ownership changes
→ stale owner cannot continue mutation
```

Where Hyper-V console semantics cannot represent a physical target scenario exactly, the limitation must be documented and the case moved to a physical lab.

## Fault injection

VM automation should support deliberate interruption at durable checkpoints:

```text
kill RuntimeHost
kill/restart Broker
stop VM abruptly
reboot guest
remove staged file
make recovery artifact unreadable
simulate dependency unavailable
restore clean child and repeat
```

The goal is deterministic safe convergence testing from SPEC-14, not chaos for its own sake.

## Acceptance evidence for Slice 0

The environment is considered sufficient to start implementation when automation can:

```text
create fresh child VM
boot
transfer exact Slice-0 artifacts
install/start Broker
launch RuntimeHost in test user session
run IPC positive/negative tests
collect logs/results
reboot once and resume test
power off/destroy child
repeat from clean parent
```

## Evidence sources

- PowerShell Direct: https://learn.microsoft.com/en-us/windows-server/virtualization/hyper-v/powershell-direct
- Windows Sandbox: https://learn.microsoft.com/en-us/windows/security/application-security/application-isolation/windows-sandbox/
- GitHub self-hosted runners: https://docs.github.com/en/actions/concepts/runners/self-hosted-runners

Hyper-V checkpoints use differencing virtual disks; the lab deliberately uses disposable differencing children as the primary reset boundary.
