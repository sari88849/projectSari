# Submission overview

**Dynamic Data Validation System**: business users define, save, run and analyze data *controls* (בקרות, called **validations** in the code) in a Hebrew RTL UI, without SQL. Stage B adds natural-language definition through a **simulated** AI (Python mock).

## 1. Where to find each deliverable

| Requirement | Location |
|---|---|
| Frontend source | `frontend/` (Angular 21) |
| Backend source | `backend/` (.NET 10: `Core` engine, `Infrastructure`, `Api`, plus tests) |
| Python AI mock | `ai-mock/` and `shared/ai-response.schema.json` |
| DB creation and sample data scripts | `db/00`–`07` (schema, metadata catalog, security, seed data), `db/99_verify.sql` (self-check), `db/setup-database.ps1` |
| Installation and execution instructions | [README.md §2–§4](../README.md#2-prerequisites) |
| Architecture and control mechanism | This page (short) and [ARCHITECTURE.md](ARCHITECTURE.md) (detailed) |
| Configurable, executable control examples | [EXAMPLES.md](EXAMPLES.md): 7 seeded validations with expected results |
| Stage B demonstration with the mock | [STAGE_B_DEMO.md](STAGE_B_DEMO.md) (screenshots) and [AI.md](AI.md) |
| Assumptions, limitations, future extensions | [README.md §6–§8](../README.md#6-assumptions) |
| AI tools used | [README.md §9](../README.md#9-ai-tools-used) |

Quick start: `.\db\setup-database.ps1`, then double-click `scripts\start-dev.cmd`, then open http://localhost:4200.

## 2. Architecture in brief

```
Angular (RTL UI) ──/api──► .NET Web API ──► Core engine ──► SQL Server (hr: business data · meta: catalog · app: validations/runs/results)
                                   └──────► AI provider interface ──HTTP──► Python mock (simulated AI)
```

- **Core** (`ValidationSystem.Core`): the definition model, metadata catalog, validator, SQL generator, reason builder and AI translation. It has no database dependency and is unit-tested in isolation.
- **Infrastructure:** loads the catalog (`meta.*` cross-checked against `sys.columns`), executes queries with the least privilege needed, stores runs and results (Dapper), and calls the AI service over HTTP.
- **Api:** REST controllers, DTOs, and Hebrew problem-details errors.
- **Frontend:** list, builder, results and AI-assistant screens; standalone components with signals.

## 3. How a control works

1. **Configure.** A control is a JSON definition, not code. It names a root entity and a tree of AND/OR groups and conditions. Each condition is *field path* + *operator* + *value / list / another field*, e.g. `Department.IsActive Equals false` or `EndDate LessThan StartDate`. It is built in the UI builder, or proposed by the AI, and stored in `app.Validations`.
2. **Business metadata.** Users see only Hebrew labels from `meta.*`. Adding an entity, field, relationship or computed field (e.g. Age) is a data change; no C# code is needed.
3. **Validate.** Every entity, field path, relationship, operator and value type is checked against the approved catalog. Unknown items or properties are rejected, with Hebrew errors pointing at the specific condition.
4. **Generate SQL.** The definition becomes one parameterized `SELECT`:
   - a `LEFT JOIN` for each relationship path;
   - nested `AND`/`OR` in the `WHERE`;
   - every value as a typed parameter;
   - one `CASE WHEN` flag column per condition;
   - `COUNT(*) OVER ()` for the exact total.

   Identifiers come only from the catalog.
5. **Execute.** The query runs as the database user `validation_reader`, which may only `SELECT` from the business schema. Only a single generated `SELECT` is accepted, with a timeout and a row cap.
6. **Results.** For each violating record, the flags are re-evaluated against the AND/OR tree, and a Hebrew reason is produced for every condition that caused the violation, with the actual values. Example: `גיל קטן מ-18 (ערך בפועל: 16)` ("age less than 18 (actual value: 16)"). The run (status, duration, count) and a snapshot of up to 5,000 records are stored. The UI shows the count, the records and their reasons, with search, filter by reason, and paging.

## 4. Stage B (AI): implemented vs. simulated

| Implemented (real code, tested) | Simulated |
|---|---|
| Building the AI context from the catalog: labels, synonyms, operators, response schema | The "model" itself: `ai-mock/mock_provider.py` is a **rule-based Hebrew matcher**, not an LLM |
| `IAiValidationProvider` interface + HTTP client to the AI service | |
| Strict response format (`shared/ai-response.schema.json`) and its translation to the same definition used in Stage A | |
| Validation of the proposal with the same validator as manual controls | |
| UI: free text → proposal (rule text, confidence, notes, raw JSON) → **user approval** → save / run, or refine in the builder | |

Replacing the mock with a real model means implementing the provider interface. The engine, validator and UI do not change ([AI.md §5](AI.md#5-replacing-the-mock-with-a-real-model)).

## 5. Verification

- **Tests:** 80 unit tests, 42 integration tests (against SQL Server, including the real Python mock) and 15 Python tests pass. The DB self-check passes on SQL Server 2019 and LocalDB 2016, with both ODBC sqlcmd and go-sqlcmd. The backend builds and passes all tests with .NET SDK 10.0.404 and 10.0.115. The Angular production build has no warnings.
- **Example controls:** the integration tests assert the exact violating records and Hebrew reasons of all 7 controls.
- **Security:** they also cover SQL-injection attempts and database-level permission enforcement.
