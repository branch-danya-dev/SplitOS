# SplitOS — Failure Handling Matrix

## 1. Purpose

Матрица связывает типовые failures с owner, canonical-state expectation и response class.

Это не замена подробным сценариям, а быстрый cross-system reference.

---

## 2. Runtime failure matrix

| Scenario | Detecting evidence | Handling owner | Canonical state expectation | Response |
|---|---|---|---|---|
| Account backend unavailable | network/backend failure | Product Identity & Entitlement | Windows session unaffected | degraded/offline policy |
| Entitlement cannot be confirmed | stale/expired evidence | Product Identity & Entitlement | Windows desktop remains usable | disable/degrade premium runtime |
| Runtime Host crash in stable mode | process/component loss | Runtime lifecycle + owning state domains | committed mode unchanged until reconciliation | restart + reconcile |
| Runtime Host crash mid-transition | crash + durable transition record | Mode Transition Coordination / Recovery | source mode remains canonical unless commit durable | rollback/recovery |
| Privileged Broker unavailable | IPC dependency failure | owning orchestrator | current stable mode unchanged | reject/degrade; recovery if partial mutation |
| Blocker needs user decision | inspection evidence | Mode Transition Coordination + User | source mode unchanged | await/cancel |
| Game display unavailable before commit | device topology evidence | Display Context + Transition Coordination | source mode unchanged | fallback or rollback |
| Display target not reached | read-back verification | Display Context + Transition Coordination | target mode not committed | retry/fallback/rollback |
| Mandatory service policy failed | SCM actual-state evidence | App/Mode Policy + Transition Coordination | target mode not committed | rollback/recovery |
| Optional helper failed | integration evidence | owning capability | mode may remain valid | degraded continuation |
| Game Client missing | adapter evidence | Game Launch Orchestration | GAME remains committed | fail launch, remain launcher |
| Game Client auth required | client evidence | Game Launch Orchestration | GAME remains committed | user auth + retry |
| Handoff accepted, game not running | process/client verification | Game Launch Orchestration | GAME remains committed | fail attempt, return launcher |
| Game process crashes | process exit evidence | Game Session owner | GAME remains committed if context coherent | return launcher |
| Controller disconnected | device evidence | Input Context | GAME unchanged | fallback/degraded input |
| Active display disconnected in GAME | display evidence | Display Context | GAME may remain if fallback coherent | emergency fallback/recovery |
| Mode transition requested during update | mutation ownership conflict | mutation coordinator | existing owner unchanged | defer/reject |

---

## 3. Update/recovery matrix

| Scenario | Canonical baseline expectation | Response |
|---|---|---|
| Update staging fails before mutation | old baseline remains canonical | fail/retry later |
| Apply fails with no partial changes proven | old baseline remains canonical | fail safely |
| Partial update application | old target identity not replaced | recovery/rollback |
| Planned reboot | transaction remains in-progress | resume + verify |
| Unexpected power loss | no target commit assumed | reconcile transaction + actual state |
| Target version present but health invalid | target not committed healthy | recovery |
| Recovery restores previous state and verifies | recovery target becomes coherent result | complete recovery |
| Recovery commands succeed but verification fails | recovery not complete | next strategy/manual escalation |
| SplitOS runtime cannot recover, Windows works | Windows usability prioritized | disable/degrade runtime |
| Recovery cannot prove bootable/coherent state | no false success | manual recovery/support |

---

## 4. Response priority shorthand

```text
Known safe current state
    ↓
Bounded retry where safe
    ↓
Policy-approved fallback
    ↓
Rollback
    ↓
Recovery
    ↓
Base Windows usable experience
    ↓
Manual recovery
```

Not every scenario uses every level.

---

## 5. Canonical prohibitions

Across the matrix:

```text
technical success != semantic success
partial state != target commit
stale evidence != external truth
recovery command sent != recovery complete
failure != permission to invent new canonical state
```
