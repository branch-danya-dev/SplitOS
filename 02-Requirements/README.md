# SplitOS — Requirements

Этот каталог содержит требования и requirements-level system context.

## Canonical artifacts

```text
02-Requirements/
├── README.md
├── SplitOS Functional Requirements.md
├── SplitOS — Non-Functional Requirements.md
├── SplitOS Distribution and Entitlement Requirements.md
├── SplitOS System Context.md
└── Requirements Open Questions.md
```

---

## Knowledge ownership

### Functional behavior

Основной функциональный baseline:

```text
SplitOS Functional Requirements.md
```

Новые принятые требования, появившиеся после исходного baseline и связанные с Media Builder, Windows source, component lifecycle и entitlement, находятся в:

```text
SplitOS Distribution and Entitlement Requirements.md
```

Они являются частью того же requirements baseline, а не отдельным продуктом.

Позднее при крупной requirements consolidation эти families могут быть объединены в один document без изменения semantic ownership.

### Non-functional properties

Canonical NFR:

```text
SplitOS — Non-Functional Requirements.md
```

### Open questions

Единственный canonical register открытых requirements-вопросов:

```text
Requirements Open Questions.md
```

Раздел `Remaining Open Requirements Questions` внутри старой версии `SplitOS Functional Requirements.md` следует считать историческим snapshot. Он **не является отдельным source of truth** и не должен обновляться независимо.

### System context

`SplitOS System Context.md` сохраняет requirements-level high-level context:

- actors;
- external systems;
- high-level dependencies;
- product environment.

Canonical ownership boundaries после начала Analysis & Design принадлежат:

```text
03-Analysis-and-Design/00-Boundaries/System Boundary Analysis.md
```

При расхождении boundary semantics приоритет имеет A&D boundary model.

---

## Requirements maturity vs analysis baseline

В исходных FR/NFR individual requirements имеют status `DRAFT` до отдельного confirmation/review.

При этом текущий согласованный набор используется как:

```text
ANALYSIS BASELINE
```

Это два разных измерения:

```text
Requirement maturity = DRAFT / CONFIRMED / ...

Document usage = ANALYSIS_BASELINE
```

Использование requirements в анализе не означает автоматического повышения каждого requirement до `CONFIRMED`.

Изменение product decision должно:

1. обновить Decision Log;
2. обновить соответствующий canonical requirement;
3. обновить affected A&D model;
4. обновить Traceability Map.

---

## Current requirement extensions

После принятия решений DEC-028..034 requirements baseline дополнен следующими областями:

```text
Media Builder / Windows source
Build Manifest
Windows component classification
Build-time vs runtime separation
SplitOS Account / Entitlement
pre-destructive-step disclosure
```

Подробные requirements находятся в `SplitOS Distribution and Entitlement Requirements.md`.
