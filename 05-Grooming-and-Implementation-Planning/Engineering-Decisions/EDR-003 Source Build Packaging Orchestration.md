# EDR-003 — Source / Build / Packaging Orchestration

Status: `CLOSED`

## Decision

Initial implementation uses a single Git monorepo and the standard .NET/MSBuild toolchain.

```text
Git repository
↓
pinned .NET SDK (`global.json`)
↓
MSBuild / dotnet CLI
↓
unit/component tests
↓
publish exact x64 artifacts
↓
versioned SplitOS release layout
↓
Builder / Update packaging
```

CI baseline:

```text
GitHub Actions
├── GitHub-hosted Windows runner → restore/build/unit/component/static checks
└── self-hosted Windows lab      → service/IPC/VM/integration/destructive tests
```

## Repository engineering files

Slice 0 SHOULD establish:

```text
SplitOS.sln

global.json
Directory.Build.props
Directory.Build.targets        only when genuinely shared
Directory.Packages.props

src/
tests/
contracts/
knowledge/
eng/
packaging/
artifacts/                    gitignored
```

The exact project tree follows `Repository and Project Topology.md`.

## Version pinning

The repository MUST pin:

- exact .NET SDK;
- exact Windows App SDK package;
- exact first-party NuGet versions through central package management;
- protocol/schema versions;
- SplitOS component version metadata.

Developer-machine latest versions MUST NOT silently determine release artifacts.

## Build entry points

Canonical local build commands must remain ordinary repository commands, for example:

```text
dotnet restore
dotnet build
dotnet test
dotnet publish
```

A thin script/orchestrator MAY compose these operations, but the build must not depend on undocumented local IDE state.

PowerShell is permitted for:

- developer bootstrap;
- CI orchestration;
- Hyper-V lab automation;
- packaging/test harness operations.

PowerShell is **not** permitted to become the product's generic privileged mutation mechanism. Product semantics remain in typed SplitOS executables/contracts.

## Artifact identity

Every publishable executable/component receives deterministic release metadata:

```text
SplitOSReleaseVersion
ComponentName
ComponentVersion
ProtocolVersion
GitCommit
BuildConfiguration
Architecture
```

The build produces a machine-readable artifact manifest before higher-level signing/release packaging.

Artifact names and paths must be deterministic for the same configured build graph even when binary byte-for-byte reproducibility is not yet proven.

## Product packaging model

MSIX/App Installer is **not** the canonical SplitOS product update mechanism.

Production delivery remains:

```text
first installation
→ SplitOS Builder prepared Windows baseline

later releases
→ SplitOS signed update channel
→ versioned release directories
→ Update Bootstrap activation
→ Recovery Capsule rollback
```

Therefore the implementation program packages first-party components as release-owned file sets/manifests consumed by SPEC-10/11 tooling.

A developer-only installer MAY be added later for convenient local testing, but it must not redefine production deployment semantics.

## Versioned release directory target

The client-side output must be compatible with the SPEC-11 model:

```text
%ProgramFiles%\SplitOS\Releases\<releaseId>\
├── RuntimeHost\
├── Broker\
├── Manager\
├── GameLauncher\
├── UpdateBootstrap\
├── Adapters\
├── Knowledge\
└── release metadata
```

Exact physical folder names may be refined in implementation as long as the release remains immutable/version-addressable and activation is not implemented by arbitrary in-place file overwrite.

## CI lanes

### Lane A — hosted build

Runs on normal GitHub-hosted Windows runners:

- restore;
- compile;
- unit tests;
- component tests not requiring admin/service/interactive topology;
- schema/contract validation;
- formatting/static analysis;
- publish smoke;
- artifact manifest generation.

### Lane B — self-hosted Windows integration

Runs against the EDR-004 lab:

- install/start Broker Service;
- interactive Runtime Host;
- Named Pipe ACL/caller tests;
- SQLite durability/corruption fixtures;
- reboot/resume;
- update/recovery fixtures;
- Hyper-V Builder/installation scenarios.

### Lane C — physical hardware

Manual/automated lab for:

- GPU/driver cohorts;
- display topology/refresh/HDR;
- GameInput controllers;
- audio hardware;
- real Game Clients/games;
- performance/frametime acceptance.

## Signing boundary

Ordinary CI MUST NOT receive production private signing keys.

Early development uses unsigned/development-signed fixtures where appropriate. Production signing later passes through the SPEC-12 signing-service/HSM boundary after immutable artifacts are produced.

## No premature build-system multiplication

Rejected for Slice 0 unless evidence appears:

- Bazel as a second build graph;
- custom package manager for source dependencies;
- multiple language-specific build orchestrators;
- mandatory Docker for Windows client builds;
- MSIX as hidden production update authority.

The initial product does not yet have scale that justifies replacing the platform's native `dotnet`/MSBuild toolchain.

## Acceptance evidence

EDR-003 is proven when the first code PR can execute from a clean development machine/CI runner:

```text
checkout
↓
install/use pinned SDK
↓
dotnet restore
↓
dotnet build
↓
dotnet test
↓
publish RuntimeHost/Broker/UI skeletons
↓
generate component/artifact metadata
```

and a self-hosted lane can consume those exact artifacts without rebuilding them inside the VM.

## Evidence sources

- .NET support policy: https://dotnet.microsoft.com/en-us/platform/support/policy
- GitHub hosted runners: https://docs.github.com/en/actions/reference/runners/github-hosted-runners
- GitHub self-hosted runners: https://docs.github.com/en/actions/concepts/runners/self-hosted-runners
