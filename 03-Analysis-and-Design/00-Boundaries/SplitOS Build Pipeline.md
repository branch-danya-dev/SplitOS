# SplitOS — Build Pipeline

## 1. Purpose

Документ определяет границу и последовательность подготовки устанавливаемой SplitOS-среды.

SplitOS не рассматривается как обычное desktop-приложение, устанавливаемое поверх произвольной пользовательской Windows.

Поддерживаемая SplitOS installation должна формироваться из известного Windows source и SplitOS-owned build inputs до первого пользовательского запуска системы.

---

# 2. Core model

Концептуально:

```text
SplitOS Media Builder
        +
Microsoft-authorized Windows 11 source
        +
SplitOS Build Manifest
        +
SplitOS Packages
        +
Compatibility Matrix
        ↓
Validated source
        ↓
Offline image preparation
        ↓
Prepared SplitOS installation media / deployment source
        ↓
Clean installation
        ↓
Installed SplitOS Windows Baseline
```

Главное правило:

> SplitOS распространяет собственный Builder, manifests, packages и product software; Windows source рассматривается как внешний вход build pipeline и должен происходить из допустимого Microsoft-authorized source.

Точный механизм автоматического получения Windows source требует отдельной licensing / technical validation и на текущем этапе не фиксируется как конкретный API или endpoint.

---

# 3. Why build-time preparation exists

Если SplitOS только запускает `.exe`, который после установки пользователя переключает службы и registry settings, продукт остаётся обычной runtime-прослойкой над произвольным Windows state.

Это противоречит выбранной модели продукта.

SplitOS должен иметь возможность до установки:

- удалить ненужные provisioned applications;
- удалить или deprovision Windows components, подтверждённые как `REMOVE`;
- изменить baseline state компонентов `DISABLE`;
- сохранить компоненты `MODE_MANAGED` для последующего runtime control;
- сохранить `KEEP` dependencies;
- применить privacy / consumer-content baseline;
- встроить SplitOS-owned software;
- подготовить startup / provisioning state;
- подготовить recovery assets;
- закрепить известную совместимость конкретной Windows base version и SplitOS release.

---

# 4. Build inputs

## 4.1 Windows Source

External input.

Минимально должен быть определён:

```text
Edition
Architecture
Build
Language
Image identity / integrity evidence
```

Windows source не считается совместимым только потому, что это Windows 11.

SplitOS должен проверять его против текущего compatibility baseline.

---

## 4.2 SplitOS Build Manifest

SplitOS-owned canonical build definition.

Manifest должен определять как минимум:

```text
Supported Windows base
Component actions
Baseline policies
Packages to inject
Provisioning rules
Recovery requirements
Build version
```

Компонентные действия должны ссылаться на Windows Component Matrix, а не формироваться как неуправляемый набор случайных tweaks.

---

## 4.3 SplitOS Packages

SplitOS-owned software, которое должно присутствовать после установки.

На boundary-этапе конкретный package layout не определяется.

Потенциально сюда входят:

```text
SplitOS runtime software
Manager / configuration UI
Game Launcher
update/recovery tooling
supporting assets
```

Это продуктовые области, а не окончательные имена процессов или сервисов.

---

## 4.4 Compatibility Matrix

Версионированное знание SplitOS о допустимых сочетаниях:

```text
Windows build
Windows edition
SplitOS release
component classification
supported drivers / ranges
supported Game Clients
known compatibility constraints
```

---

# 5. Build stages

## Stage 1 — Source acquisition

Builder получает или принимает Windows 11 source из допустимого Microsoft-authorized source.

Target behavior:

```text
Acquire source
↓
Identify source
↓
Validate compatibility
↓
Continue or reject
```

Если источник или версия не поддерживаются, Builder не должен молча продолжать normal supported build.

---

## Stage 2 — Source validation

Необходимо проверить минимум:

- Windows edition;
- architecture;
- build/version;
- required image structure;
- integrity evidence where available;
- compatibility with selected SplitOS release.

Результат:

```text
SUPPORTED
UNSUPPORTED
INVALID / CORRUPTED
```

---

## Stage 3 — Offline image preparation

Windows image переводится в состояние, пригодное для servicing.

На концептуальном уровне:

```text
Windows image
    ↓
mount / offline servicing context
    ↓
apply SplitOS Build Manifest
```

Конкретная технология (`DISM`, servicing APIs, additional tooling) фиксируется позднее в Specification после technical validation.

---

## Stage 4 — Baseline component transformation

Применяется Windows Component Classification Model:

```text
REMOVE
DISABLE
MODE_MANAGED
KEEP
```

### REMOVE

Компонент удаляется/deprovisioned до установки, если removal подтверждён.

### DISABLE

Компонент сохраняется, но получает неактивный baseline state.

### MODE_MANAGED

Компонент сохраняется полностью, потому что его состояние будет зависеть от Work/Game runtime context.

### KEEP

Компонент сохраняет required platform responsibility.

---

## Stage 5 — SplitOS injection / provisioning

В image добавляются SplitOS-owned packages и configuration required for first boot.

Build-time provisioning не должно смешиваться с пользовательскими Work/Game runtime decisions.

Пример разделения:

```text
Build-time:
SplitOS software exists
Mode policy definitions exist
Recovery assets exist

Runtime:
which mode is active
which MODE_MANAGED components are active
which display/input profile is active
```

---

## Stage 6 — Installation media / deployment preparation

Builder формирует поддерживаемый installation/deployment source.

Первый целевой MVP-путь может быть:

```text
Builder
↓
prepared bootable installation media
↓
reboot
↓
clean installation
```

В будущем UX может быть скрыт за более автоматизированным flow:

```text
Builder
↓
prepare deployment
↓
reboot to installer/recovery environment
↓
deploy image
```

Конкретный UX не является boundary decision.

---

## Stage 7 — Destructive-operation disclosure

До форматирования/стирания выбранного накопителя пользователь должен получить явное предупреждение о destructive action.

До этого же момента пользователь должен иметь возможность увидеть продуктовую информацию о SplitOS Account и entitlement model.

Текущая product model:

```text
SplitOS distribution/build tooling
→ free distribution

SplitOS Account
→ product identity / entitlement context

Paid entitlement
→ full/premium SplitOS capabilities, updates/support according to product policy
```

Точный Free/Paid feature split определяется отдельно.

Пользователь не должен узнавать о существенном платном ограничении только после удаления предыдущей системы.

---

## Stage 8 — Clean installation

Поддерживаемый product path предполагает установку подготовленного baseline, а не mutation произвольной существующей Windows installation.

Это сохраняет:

```text
known starting state
known component inventory
known SplitOS package set
known compatibility baseline
```

---

# 6. Build output

Результатом pipeline является не просто "Windows with tweaks".

Определение:

```text
SplitOS Windows Baseline =
    Supported Windows Source
  + SplitOS Build Manifest
  + Validated Component Classification
  + SplitOS Packages
  + Baseline Configuration
```

Для release необходимо иметь воспроизводимую связь:

```text
Windows Base X
+
Build Manifest Y
+
SplitOS Package Set Z
=
SplitOS Release R
```

---

# 7. Build-time ownership

SplitOS является authority для:

- compatible Windows base decision;
- Build Manifest;
- component classification;
- SplitOS package set;
- SplitOS provisioning rules;
- final supported baseline definition.

Microsoft остаётся authority/source для:

- Windows implementation;
- Windows licensing terms;
- Windows source binaries;
- upstream Windows patches.

---

# 8. Builder boundary

Builder не должен становиться permanent runtime orchestrator только потому, что он создал system image.

После успешной установки его build responsibility заканчивается.

```text
Media Builder
→ creates baseline

Installed SplitOS Runtime
→ manages live system
```

Runtime может проверять baseline/version drift, но не должен постоянно повторять destructive build-time removal как обычный mode-management механизм.

---

# 9. Build failures

Минимальные failure classes:

```text
unsupported Windows source
source integrity failure
image servicing failure
component removal failure
SplitOS package injection failure
insufficient disk space
installation media preparation failure
unsupported target configuration
```

Build не должен маркироваться successful, если обязательный baseline action не применён.

Partial build output не должен считаться поддерживаемым SplitOS release image.

---

# 10. Versioning

Build Manifest и Component Matrix должны быть versioned вместе со SplitOS release knowledge.

Изменение Windows build может потребовать:

```text
new component inventory
new dependency validation
changed removal rules
new regression testing
new SplitOS release
```

Старый manifest не должен автоматически считаться корректным для новой Windows build.

---

# 11. Security / legal constraint

На текущем этапе зафиксировано только системное ограничение:

> SplitOS не должен проектироваться в расчёте на публичное перераспространение собственного modified Windows ISO без отдельного подтверждённого права на такое распространение.

Target model использует внешний Microsoft-authorized Windows source и SplitOS-owned transformation/build inputs.

Конкретный автоматизированный source-acquisition mechanism требует legal/licensing validation до реализации production distribution.

---

# 12. Open questions

- Какой конкретный Microsoft-authorized Windows source используется Builder?
- Может ли Builder безопасно автоматизировать source acquisition или пользователь предоставляет source сам?
- Какие Windows editions входят в baseline?
- Как проверяется image integrity?
- Как физически представляется Build Manifest?
- Какие operations выполняются только offline?
- Какие operations допустимо выполнить during setup/first boot?
- Какой recovery mechanism используется при failed deployment?
- Как обновляется уже установленный baseline после изменения Windows base?

---

# 13. Result

SplitOS build pipeline разделяет две разные инженерные задачи:

```text
BUILD-TIME
→ create known Windows baseline

RUNTIME
→ operate Work/Game contexts on that baseline
```

Это разделение является фундаментальным для дальнейшего Responsibilities / Ownership analysis.
