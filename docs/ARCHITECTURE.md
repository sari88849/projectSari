# Architecture

> A short version is in [OVERVIEW.md](OVERVIEW.md). Terminology: the assignment's *control* (בקרה) is called a **validation** in the code.

## 1. Overview

```mermaid
flowchart LR
    subgraph Browser
        UI[Angular 21 RTL UI<br/>list · builder · results · AI assistant]
    end
    subgraph API[".NET 10 Web API"]
        C[Controllers + DTOs<br/>ProblemDetails errors]
        VS[ValidationService]
        AS[AiProposalService]
        subgraph Core["ValidationSystem.Core (no DB dependency)"]
            CAT[MetadataCatalog]
            VAL[DefinitionValidator]
            SQL[SqlQueryBuilder]
            RP[ResultProcessor + ReasonBuilder]
            TR[AiResponseTranslator]
        end
        subgraph Infra[ValidationSystem.Infrastructure]
            CP[SqlMetadataCatalogProvider]
            EX[SqlQueryExecutor<br/>EXECUTE AS validation_reader]
            REPO[SqlValidationRepository]
            AIH[HttpAiValidationProvider]
        end
    end
    subgraph DB[SQL Server · ValidationSystemDb]
        HR[(hr.*<br/>business data)]
        META[(meta.*<br/>business catalog)]
        APP[(app.*<br/>validations, runs, results)]
    end
    PY[Python AI mock<br/>POST /generate]

    UI -->|/api| C
    C --> VS & AS
    VS --> VAL & SQL & RP
    VS --> EX & REPO
    AS --> TR --> VAL
    AS --> AIH --> PY
    CP --> META
    CP -. sys.columns .-> DB
    EX --> HR
    REPO --> APP
```

The projects depend in one direction: **Api → Infrastructure → Core**. Core holds every engine decision and is tested without a database. Infrastructure is the only code that talks to SQL Server or HTTP.

| Concern required by the assignment | Where it lives |
|---|---|
| Validation definition | `Core/Definitions/ValidationDefinition.cs` (JSON model), `OperatorCatalog.cs` |
| Business metadata | `meta.*` tables → `SqlMetadataCatalogProvider` → `Core/Metadata/MetadataCatalog.cs` |
| Parsing and validating the definition | `DefinitionJson` (strict parser), `Core/Validation/DefinitionValidator.cs`, `ValueConverter.cs` |
| Dynamic SQL generation | `Core/Sql/SqlQueryBuilder.cs` → `GeneratedQuery` |
| SQL execution | `Infrastructure/SqlQueryExecutor.cs` |
| Result processing | `Core/Results/ResultProcessor.cs`, `ReasonBuilder.cs`, `ValueFormatter.cs` |
| Orchestration and persistence | `Core/Services/ValidationService.cs`, `Infrastructure/SqlValidationRepository.cs` |
| AI (separate from the engine) | `Core/Ai/*`, `Infrastructure/HttpAiValidationProvider.cs`, `ai-mock/` |

## 2. Database

| Schema | Purpose |
|---|---|
| `hr` | Sample business data: `Departments`, `Positions`, `Employees`. Foreign keys include a self-reference for the manager and a department ↔ manager cycle. Deliberately contains invalid rows. |
| `meta` | The **business metadata catalog**: `Entities`, `Fields`, `Relationships`, with Hebrew labels, descriptions and synonyms. This is the only place physical table and column names appear. |
| `app` | `Validations` (definition JSON), `ValidationRuns` (status, start/end, duration, violation count, truncation flag, generated SQL, definition snapshot, result metadata), `ValidationRunResults` (snapshot of violating records with their reasons). |

A database user, `validation_reader` (created without a login), has `SELECT` on schema `hr` only.

The scripts need SQL Server 2016 or later (`OPENJSON`, `ISJSON`, `DROP … IF EXISTS`). They were verified on SQL Server 2019 and LocalDB 2016.

## 3. The validation definition

A validation is **data, not code**. It is a JSON tree that describes the condition identifying *invalid* records:

```json
{
  "rootEntity": "Employee",
  "condition": { "type": "group", "id": "root", "logic": "And", "children": [
    { "type": "condition", "id": "c1", "field": "IsActive", "operator": "Equals",
      "value": { "kind": "constant", "value": true } },
    { "type": "group", "id": "g1", "logic": "Or", "children": [
      { "type": "condition", "id": "c2", "field": "Department.IsActive", "operator": "Equals",
        "value": { "kind": "constant", "value": false } },
      { "type": "condition", "id": "c3", "field": "Manager.IsActive", "operator": "Equals",
        "value": { "kind": "constant", "value": false } } ] } ] },
  "displayFields": ["EmployeeNumber", "FirstName", "LastName", "Department.Name"]
}
```

- **Field paths** are written as `Relationship.Relationship.Field`, using catalog keys, e.g. `Department.Manager.IsActive`. There can be at most 3 relationship hops.
- **Operands** come in three kinds: `constant` (a fixed value), `list` (for `In`) and `field`, which compares against another field, including one on a related entity.
- **Operators** (12): `Equals`, `NotEquals`, `GreaterThan`, `GreaterThanOrEqual`, `LessThan`, `LessThanOrEqual`, `Between`, `In`, `Contains`, `StartsWith`, `IsEmpty`, `IsNotEmpty`. Each operator declares the data types it supports and its operand shape.
- **Parsing is strict.** Unknown properties, unknown node types, unknown or numeric enum values, and a root that is not a group are all rejected.
- **A missing operator is not defaulted.** `Condition.Operator` is nullable. An operator not chosen yet (e.g. a new row in the builder) is reported by the validator on that row ("יש לבחור תנאי", choose an operator); it is never silently read as `Equals`.

Adding a validation means storing a new JSON document; no C# changes are needed. Adding an entity, field or relationship means inserting rows in `meta.*`; again no C# changes are needed (see [EXAMPLES.md §4](EXAMPLES.md#4-adding-a-new-entity-without-code)).

## 4. Metadata catalog (business layer + database metadata)

`SqlMetadataCatalogProvider` builds the approved catalog from two sources and exposes an item only when both agree:
1. **Business metadata** (`meta.*`): keys, Hebrew labels, data types, computed-field definitions, relationships, and the flags `IsTitle`, `IsDefaultDisplay` and `IsMandatory`.
2. **Database metadata** (`sys.columns` / `sys.types`): the table and column must exist, and the SQL type must match the business type (`bit` ↔ Boolean, `date*` ↔ Date, and so on).

Mismatches are logged and skipped, so they can never reach a query. `MetadataCatalog` then enforces that every identifier matches `^[A-Za-z_][A-Za-z0-9_]{0,127}$`, that relationships point to known entities, and that computed fields reference a physical Date field.

**Computed fields** (e.g. `Age`) are rows in the catalog that name a *whitelisted* function (`FullYearsSince`, `DaysSince`) and a source field. The SQL for each function is fixed inside the builder; no SQL text is ever stored in metadata.

The catalog is cached in memory. `POST /api/metadata/refresh` reloads it.

## 5. Validation and SQL generation

`DefinitionValidator` checks every node against the catalog:
- the root entity exists;
- each field path resolves through approved relationships;
- the operator is supported for the field's type, and the operands have the right shape;
- each constant can be converted to the field's type;
- compared fields have the same type;
- node ids are safe and unique;
- limits hold: nesting depth ≤ 5, at most 50 conditions, at most 100 list values, at most 30 display fields.

Errors are returned in Hebrew, with the id of the offending node so the UI can highlight that row.

`SqlQueryBuilder` turns a **validated** definition into a single statement. Here is the actual output for the nested validation above:

```sql
SELECT TOP (@maxRows)
    COUNT(*) OVER () AS [__total],                                  -- exact total, even when capped
    t0.[EmployeeId] AS [__key],
    t0.[EmployeeNumber] AS [f0],
    t0.[FirstName] AS [f1],
    t0.[LastName] AS [f2],
    t1.[Name] AS [f3],
    t2.[LastName] AS [f4],
    t0.[IsActive] AS [f5],
    t1.[IsActive] AS [f6],
    t2.[IsActive] AS [f7],
    CASE WHEN t0.[IsActive] = @p0 THEN 1 ELSE 0 END AS [c0],        -- one flag per condition
    CASE WHEN t1.[IsActive] = @p1 THEN 1 ELSE 0 END AS [c1],
    CASE WHEN t2.[IsActive] = @p2 THEN 1 ELSE 0 END AS [c2]
FROM [hr].[Employees] AS t0
LEFT JOIN [hr].[Departments] AS t1 ON t1.[DepartmentId] = t0.[DepartmentId]   -- path "Department"
LEFT JOIN [hr].[Employees] AS t2 ON t2.[EmployeeId] = t0.[ManagerId]          -- path "Manager"
WHERE (t0.[IsActive] = @p0 AND (t1.[IsActive] = @p1 OR t2.[IsActive] = @p2))
ORDER BY t0.[EmployeeId];
```

How it is built:
- **Joins:** one `LEFT JOIN` per distinct relationship path, reused when the same path appears again. Multi-hop paths chain aliases (`Department.Manager` → `t1` → `t2`). `LEFT JOIN` keeps root records whose relation is missing; conditions on that missing relation evaluate to UNKNOWN, so the record is not flagged.
- **Logic:** groups become parenthesized `AND` / `OR` expressions, built recursively.
- **Field-to-field comparison:** the right-hand side is simply another column expression, e.g. `t0.[Salary] > t1.[MaxSalary]`.
- **Values:** every value becomes a typed parameter (`@p0…`). `Contains` / `StartsWith` escape `[`, `%` and `_`, so user text is matched literally.
- **Columns:** display fields, title fields and every field used by a condition are selected. These columns supply the actual values quoted in the failure reasons.
- **Aliases** (`t0`, `f0`, `c0`, `@p0`) are generated by the builder. Nothing user-supplied is ever concatenated into the SQL.

## 6. Security (defense in depth)

| Layer | Protection |
|---|---|
| API | No endpoint accepts SQL. The definition is JSON parsed with strict options; unknown properties and node types are rejected. |
| Catalog | Entities, fields and relationships must exist in **both** `meta.*` and the physical schema. Identifiers are whitelisted by regex, then bracket-quoted. |
| Validator | Operators come from a fixed enum and must suit the field type. Values are converted to typed CLR values. Size and depth limits apply. |
| Builder | SQL is assembled only from fixed fragments plus catalog identifiers and generated aliases. Every value is a parameter. |
| Executor | Accepts only a `GeneratedQuery`. A guard refuses text that is not one `SELECT … ;`, or that contains comments. Parameters are bound with an explicit `SqlDbType`. A command timeout and a row cap (`TOP (@maxRows)`) apply. |
| Database | Each query runs under `EXECUTE AS USER = 'validation_reader'`, which has `SELECT` on `hr` only. `REVERT` runs in `finally`, and the connection pool is cleared if the revert fails. Even a SELECT that slipped through every other layer could not read `app` / `meta`, modify data, or create objects. The integration tests prove this. |
| Errors | Users see Hebrew business messages. SQL exceptions and stack traces go only to the log. |

## 7. Failure reasons

Each condition has a flag column (`c0`, `c1` …). For each returned row, `ResultProcessor` **re-evaluates the condition tree in memory from the flags** and collects reasons only from the branches that actually caused the violation:
- `AND` group: satisfied only if all children are; all of their reasons are reported.
- `OR` group: every satisfied child is reported, so a record can have several reasons.
- An unsatisfied branch contributes nothing. In `(A AND B) OR C` with A = 1, B = 0, C = 1, only C is reported, because A alone did not make the record invalid.

The tree contains only AND and OR (no NOT), so this in-memory evaluation always agrees with SQL Server's `WHERE`.

`ReasonBuilder` writes the Hebrew text generically, from catalog labels, the operator catalog and the record's actual values:

| Condition | Reason produced |
|---|---|
| `Age < 18` | `גיל קטן מ-18 (ערך בפועל: 16)` |
| `EndDate < StartDate` | `תאריך סיום העסקה (30/06/2013) מוקדם מ-"תאריך תחילת העסקה" (01/01/2014)` |
| `Department.IsActive = false` | `מחלקה › סטטוס מחלקה: לא פעילה` |
| `Salary > Position.MaxSalary` | `שכר חודשי (52,000) גדול מ-"תפקיד › שכר מקסימלי לתפקיד" (35,000)` |
| `NationalId IsEmpty` | `חסר ערך בשדה "תעודת זהות"` |

Date operators use date wording ("מוקדם מ" / "מאוחר מ"). Booleans use the field's own labels ("פעיל" / "לא פעילה"). Numbers are grouped and dates are formatted `dd/MM/yyyy`. The same builder produces the readable rule shown in the builder preview and in the AI proposal.

## 8. Execution, results and history

`ValidationService.ExecuteAsync` runs these steps:
1. Insert a run with status `Running`, together with a snapshot of the definition.
2. Build the query, which validates the definition against the *current* catalog.
3. Execute it with `TOP (5000)`.
4. Process the rows into records with their reasons.
5. In one transaction, bulk-insert the snapshot (`SqlBulkCopy`) and mark the run `Succeeded`, storing the count, duration, truncation flag, generated SQL, and the result columns and condition descriptions.

Any failure marks the run `Failed` with a Hebrew message and is returned, not thrown, so the list always shows the latest status.

Results are searched and filtered on the server: `LIKE` over a stored search text (title + values + reasons), and filtering by failed condition via `OPENJSON` over the stored reasons, with paging.

## 9. AI integration

See [AI.md](AI.md). In short, `IAiValidationProvider` is the single extension point. Its response is translated into the same `ValidationDefinition` and passes through the same `DefinitionValidator`. Nothing is saved or executed without the user's approval. The engine has no knowledge of AI.

## 10. REST API

| Method | Path | Purpose |
|---|---|---|
| GET | `/api/metadata/entities` | Business entities (Hebrew labels) |
| GET | `/api/metadata/entities/{key}/fields?maxHops=2` | Fields of an entity, including fields of related entities |
| GET | `/api/metadata/operators` | Operators, labels, supported types, operand shape |
| POST | `/api/metadata/refresh` | Reload the catalog |
| GET | `/api/validations?search=&entity=` | Saved validations, each with its latest run status and count |
| GET | `/api/validations/{id}` | One validation, with its definition |
| POST / PUT / DELETE | `/api/validations[/{id}]` | Create / update / delete (400 with Hebrew errors when invalid) |
| POST | `/api/validations/preview` | Validate a definition; returns the Hebrew rule text and the generated SQL without executing |
| POST | `/api/validations/{id}/execute` | Run now; returns the run summary |
| GET | `/api/validations/{id}/runs` | Run history |
| GET | `/api/runs/{runId}` | Run summary, result columns and conditions |
| GET | `/api/runs/{runId}/results?search=&conditionId=&page=&pageSize=` | Violating records with reasons |
| GET | `/api/ai/context?rootEntity=` | The metadata context sent to the AI provider |
| POST | `/api/ai/proposals` | Natural language → validated proposal (not saved) |

## 11. Frontend

```
core/      models.ts, api.service.ts, metadata.store.ts (signals cache), definition.utils.ts, errors.ts, notify.service.ts
shared/    status-badge, state-panel (loading / empty / error), confirm-dialog, Hebrew paginator labels
features/
  validation-list/       search, entity filter, latest status, run / edit / delete
  validation-builder/    page + recursive condition-group-editor + type-aware condition-editor
  validation-results/    summary, history, search, reason filter, paging, reasons per record
  ai-assistant/          prompt → proposal review → approve / edit in builder
```

- **Components:** standalone, with `input()` / `output()` and signals. Routes are lazy-loaded, and route parameters are bound as component inputs.
- **Builder:** works directly on the engine's JSON model. A debounced `/preview` call validates the rule with the backend validator, so there is only one source of truth. The UI never contains SQL or physical names.

## 12. Performance considerations

- **One query per run.** The total count comes from `COUNT(*) OVER ()`, flags are computed in the same pass, and rows are capped with `TOP`.
- **Joins only where needed.** Only relationship paths that are actually used are joined. FK columns are indexed (`IX_Employees_*`).
- **Command timeout** is configurable (`Database:CommandTimeoutSeconds`).
- **Snapshots** are written with `SqlBulkCopy` in a single transaction. Results are paged on the server, and runs are indexed by `(ValidationId, StartedAt)`.
- **Catalog** is cached in memory and loaded with a single round trip (`QueryMultiple`).
- **Possible next steps** for large data sets: indexes driven by the fields validations use most, scheduled or asynchronous execution, and full-text search over results.
