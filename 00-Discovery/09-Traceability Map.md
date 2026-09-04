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

| Discovery source | Decision | Canonical area | Requirement family |
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
| EL-018..019 | DEC-001 distribution model | System Context | FR-UPDATE / NFR-INSTALL |
| EL-020..021 | DEC-022/023 update lifecycle | Windows Update policy | FR-UPDATE / NFR-UPD |
| EL-022 | DEC-008 Game Launcher | Game Launcher | FR-LAUNCHER / NFR-UX |
| EL-023..024 | DEC-014/024 game config boundary | Game Experience Management | FR-OPT / NFR-SEC |
| EL-025..026 | DEC-020/021 UX priority | MVP scope | NFR-UX |
| EL-027 | DEC-026 displays | Display Management | FR-DISPLAY / NFR-COMP |
| EL-028 | DEC-025 one drive | Storage | FR-STORAGE |
| EL-029 | DEC-027 future ecosystem | Extensibility | FR-FUTURE / NFR-EXT |

---

## 3. Future use

После появления `03-Analysis-and-Design` таблица должна расшириться:

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

> Почему существует это требование, какое решение его породило и где проверяется его реализация?
