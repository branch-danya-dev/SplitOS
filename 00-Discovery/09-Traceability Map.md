# SplitOS — Discovery Traceability Map

## 1. Purpose

Документ связывает discovery-решения с каноническими слоями проекта.

Направление трассировки:

```text
Initial Request
      ↓
Problem / Elicitation
      ↓
Decision
      ↓
Concept
      ↓
Functional / Non-Functional Requirement
      ↓
Analysis & Design
      ↓
Specification / Verification
```

---

## 2. High-level traceability

| Discovery source | Decision | Canonical area | Requirement / Analysis family |
|---|---|---|---|
| EL-001 | DEC-002 Work XOR Game | System Context / Concept | FR-MODE / NFR-REL |
| EL-002 | DEC-003 startup selection | System Context | FR-SETUP / NFR-REL |
| EL-003 | DEC-004/005 remain in Game | Game Mode concept | FR-MODE / NFR-REL |
| EL-004 | DEC-006 GAME != GAME_CLIENT | Application model | FR-APP / FR-GAME |
| EL-005 | DEC-007 launch orchestration | Game Launcher | FR-MODE / FR-LAUNCHER |
| EL-008 | DEC-011 multiple game profiles | Game Profiles | FR-GAME / NFR-REL |
| EL-009 | DEC-012/013 hardware validation | Hardware Detection | FR-GAME / NFR-PERF |
| EL-010..012 | DEC-014 optimization objective | Game Experience Optimization | FR-OPT / NFR-PERF |
| EL-014..015 | DEC-016/017 safe transition | Recovery / Mode Transition | FR-RECOVERY / NFR-TRANS |
| EL-016..017 | DEC-018/019 Shared Apps | Game Mode UX | FR-APP / NFR-PERF / NFR-UX |
| EL-018..019 | DEC-001 distribution model | System Context | FR-DIST / NFR-INSTALL |
| EL-020..021 | DEC-022/023 update lifecycle | Windows Update policy | FR-UPDATE / NFR-UPD |
| EL-022 | DEC-008 Game Launcher | Game Launcher | FR-LAUNCHER / NFR-UX |
| EL-023..024 | DEC-014/024 game config boundary | Game Experience Management | FR-OPT / NFR-SEC |
| EL-025..026 | DEC-020/021 UX priority | MVP scope | NFR-UX |
| EL-027 | DEC-026 displays | Display Management | FR-DISPLAY / NFR-COMP |
| EL-028 | DEC-025 one drive | Storage | FR-STORAGE |
| EL-029 | DEC-027 future ecosystem | Extensibility | FR-FUTURE / NFR-EXT |
| EL-030 | DEC-028 Windows source distribution model | Distribution / Build Boundary | FR-DIST / NFR-INSTALL / Build Pipeline |
| EL-031 | DEC-029 build-time preparation | Distribution Engineering | FR-DIST / NFR-INSTALL / Build Pipeline |
| EL-032 | DEC-030 component lifecycle classes | Windows Component Baseline | FR-DIST / FR-APP / Component Classification Model |
| EL-033 | DEC-031 mode-managed capability | Work/Game Runtime | FR-WORK / FR-GAME / FR-APP / Installed Runtime Boundary |
| EL-034 | DEC-032 build vs runtime responsibility | Runtime Boundary | Responsibilities / Ownership |
| EL-035 | DEC-033 account monetization | User/Entitlement | FR-USER / NFR-SEC / Data/Trust analysis |
| EL-036 | DEC-034 pre-install disclosure | Setup UX | FR-SETUP / NFR-UX |

---

## 3. Current Analysis & Design traceability

Новые boundary artifacts уже закрывают часть перехода `Requirement → Analysis`:

```text
03-Analysis-and-Design/00-Boundaries/
├── System Boundary Analysis.md
├── system-boundary.mmd
├── Windows Component Classification Model.md
├── SplitOS Build Pipeline.md
└── Installed Runtime Boundary.md
```

Связи:

| Analysis artifact | Primary source decisions |
|---|---|
| System Boundary Analysis | DEC-001, DEC-028, DEC-032 |
| SplitOS Build Pipeline | DEC-028, DEC-029, DEC-034 |
| Windows Component Classification Model | DEC-030, DEC-031 |
| Installed Runtime Boundary | DEC-002, DEC-032, DEC-033 |
| system-boundary.mmd | synthesis of build/runtime/external ownership boundaries |

---

## 4. Future use

После появления следующих частей `03-Analysis-and-Design` таблица должна расшириться:

```text
Requirement ID
→ System responsibility
→ Owner
→ State/Flow
→ Interface/Contract
→ Failure behavior
→ Verification case
```

Цель трассировки — не создать таблицу ради таблицы, а иметь возможность ответить:

> Почему существует это требование, какое решение его породило, какая аналитическая модель его объясняет и где проверяется его реализация?
