# SplitOS — Initial Scope and Change Surface

## 1. In scope

### Distribution / Build

- SplitOS Media Builder;
- Microsoft-authorized Windows 11 source as external build input;
- Windows source validation;
- SplitOS Build Manifest;
- Windows Component Matrix;
- Windows 11 base preparation;
- removal/deprovision of `REMOVE` components;
- baseline disablement of `DISABLE` components;
- preservation of `MODE_MANAGED` components for runtime control;
- preservation of `KEEP` platform dependencies;
- SplitOS system packages / provisioning;
- clean-install media/deployment preparation;
- SplitOS-controlled update lifecycle;
- recovery support.

### Installed runtime

- startup/bootstrap after installation;
- SplitOS Account / entitlement context;
- active mode state;
- Work Mode;
- Game Mode;
- strict mode isolation;
- `MODE_MANAGED` Windows component lifecycle;
- transactional transitions;
- runtime state verification;
- rollback/fallback;
- baseline drift awareness.

### Game Mode

- SplitOS Game Launcher;
- official Game Client integrations;
- unified library;
- game launch orchestration;
- Game Profiles;
- Controller / Keyboard & Mouse profiles;
- Game Display profiles;
- game optimization;
- Shared Apps;
- gaming overlay concept.

### Devices

- connected displays;
- audio devices;
- keyboard/mouse;
- supported controllers;
- device switching between modes.

### Work Mode

- clean/optimized Windows context;
- work application lifecycle policies;
- Work-useful Windows capabilities that may be `MODE_MANAGED` and disabled in Game Mode;
- minimal v1 UX customization.

### Account / entitlement

- SplitOS Account as product identity context;
- entitlement consumption by installed runtime;
- pre-install disclosure of significant paid limitations;
- separation from Windows license and Game Platform accounts.

---

## 2. Deferred / later scope

- manually added games;
- unofficial game library support;
- advanced Work Mode desktop redesign;
- SplitOS OEM controller;
- custom communication client;
- partner-specific Discord build;
- social network features;
- extended streaming ecosystem;
- handheld-specific UX;
- laptop-specific battery/power model;
- large multi-user enterprise scenarios.

---

## 3. Explicit out of scope

- custom OS kernel;
- DirectX replacement;
- GPU driver development;
- DRM bypass;
- anti-cheat bypass;
- matchmaking manipulation;
- game network code manipulation;
- server logic modification;
- cheating-oriented input substitution;
- ownership of third-party game licenses/stores;
- treating arbitrary existing Windows installations as the supported SplitOS baseline;
- relying on public redistribution of a ready-made modified Windows ISO as the default product distribution model.

---

## 4. Initial change surface

```text
SplitOS Build / Distribution
│
├── Media Builder
├── Windows Source Validation
├── Build Manifest
├── Windows Component Matrix
├── Offline / Setup Preparation
├── SplitOS Package Injection
├── Installation / Deployment
├── Update Compatibility
└── Recovery Assets

Installed SplitOS Runtime
│
├── Startup / User Context
├── Account / Entitlement
├── Mode State
├── Transition Control
├── Work Context
├── Game Context
│   ├── Game Launcher
│   ├── Game Profiles
│   ├── Shared Apps
│   └── Overlay
├── MODE_MANAGED Component Lifecycle
├── Process / Service Policy
├── Display
├── Audio
├── Input
├── Game Client Integrations
├── Game Configuration
├── Hardware Detection
├── Update Lifecycle
├── Recovery
└── Diagnostics / State Verification
```

---

## 5. External change surface

```text
Microsoft-authorized Windows 11 source
Windows 11
Windows servicing
Windows APIs
Microsoft update source
GPU drivers
Audio drivers
Input drivers
Displays / TVs
Controllers
Steam
Epic Games
Battle.net
Xbox
Games
OBS / streaming systems (future/optional)
```

---

## 6. Primary impact questions for Analysis & Design

- где заканчивается Media Builder responsibility и начинается Installed Runtime responsibility;
- как формально определяется supported Windows source;
- как versioned Build Manifest связывается с Windows build и SplitOS release;
- как классифицируются Windows components: REMOVE / DISABLE / MODE_MANAGED / KEEP;
- какие component changes допустимы offline, during setup и first boot;
- какие MODE_MANAGED capabilities отличаются между Work и Game;
- где проходит граница runtime-компонентов SplitOS;
- кто владеет active mode state;
- как обеспечивается atomicity перехода;
- как хранится desired vs actual state;
- как обнаруживается GAME;
- как launch request удерживается/продолжается;
- как выполняется process/component policy;
- как связываются Game / Client / Profile / Display / Input;
- как Game Launcher получает authoritative library state;
- как применяется game config;
- как валидируется hardware context;
- как entitlement влияет на product capabilities без нарушения boot/recovery safety;
- как обновляется установленный baseline;
- как обнаруживается baseline drift;
- как система восстанавливается после частично неуспешного изменения.
