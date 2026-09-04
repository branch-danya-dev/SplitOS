# SplitOS — Initial Scope and Change Surface

## 1. In scope

### Distribution

- Windows 11 base preparation;
- removal/disablement of non-required components;
- SplitOS system components;
- SplitOS-controlled update lifecycle;
- recovery support.

### Mode management

- startup mode selection;
- Work Mode;
- Game Mode;
- strict mode isolation;
- transactional transitions;
- rollback/fallback.

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
- minimal v1 UX customization.

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
- ownership of third-party game licenses/stores.

---

## 4. Initial change surface

```text
SplitOS Distribution
│
├── Startup / User Context
├── Mode State
├── Transition Control
├── Work Context
├── Game Context
│   ├── Game Launcher
│   ├── Game Profiles
│   ├── Shared Apps
│   └── Overlay
├── Process / Service Policy
├── Display
├── Audio
├── Input
├── Game Client Integrations
├── Game Configuration
├── Hardware Detection
├── Update Lifecycle
├── Recovery
└── Diagnostics
```

---

## 5. External change surface

```text
Windows 11
Windows servicing
Windows APIs
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

- где проходит граница runtime-компонентов SplitOS;
- кто владеет active mode state;
- как обеспечивается atomicity перехода;
- как хранится desired vs actual state;
- как обнаруживается GAME;
- как launch request удерживается/продолжается;
- как выполняется process policy;
- как связываются Game / Client / Profile / Display / Input;
- как Game Launcher получает authoritative library state;
- как применяется game config;
- как валидируется hardware context;
- как обновляется distribution;
- как система восстанавливается после частично неуспешного изменения.
