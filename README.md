# Dynamic Data Validation System

A generic system that lets business users, with no SQL knowledge, **define, save, edit, run and analyze data validation rules** in a Hebrew, right-to-left UI. Rules can be built in a visual builder or proposed by AI from a free-text description. Every rule becomes a secure, parameterized `SELECT` generated at run time from a metadata catalog. No validation has dedicated code or a hand-written query.

| Layer | Technology |
|---|---|
| Backend | .NET 10 Web API (C#), Dapper, Microsoft.Data.SqlClient |
| Validation engine | Plain C# (`ValidationSystem.Core`), no database dependency |
| AI mock | Python 3.7+ (standard library only), HTTP service |
| Frontend | Angular 21 (standalone components, signals, zoneless), Angular Material, Hebrew RTL |
| Database | SQL Server (tested on SQL Server 2019) |

Documentation:
- [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md): architecture, the validation engine, dynamic SQL generation, security
- [docs/EXAMPLES.md](docs/EXAMPLES.md): the example validations, their definitions and expected results
- [docs/AI.md](docs/AI.md): the AI flow, response schema, the mock, and how to plug in a real provider

---

## 1. Repository layout

```
db/                      SQL Server scripts: schema, metadata catalog, seed data, verification
backend/
  src/ValidationSystem.Core            engine: definition model, catalog, validator, SQL builder, reasons, AI abstractions
  src/ValidationSystem.Infrastructure  SQL Server access, catalog loader, read-only executor, AI HTTP client
  src/ValidationSystem.Api             REST API (controllers, DTOs, error handling)
  tests/ValidationSystem.Core.Tests        unit tests
  tests/ValidationSystem.IntegrationTests  tests against the real SQL Server + Python mock
ai-mock/                 Python AI mock service + unit tests
shared/ai-response.schema.json   AI response format (used by .NET and Python)
frontend/                Angular application
scripts/start-dev.ps1    starts API + AI mock + Angular in separate windows
docs/                    architecture, examples, AI flow
```

## 2. Prerequisites

| Tool | Version used |
|---|---|
| SQL Server | 2019 (any edition; LocalDB / Express also work, see below) with Windows Authentication |
| `sqlcmd` | any |
| .NET SDK | 10.0 (`backend/global.json` pins 10.0.404, roll-forward allowed) |
| Node.js | 22.x (Angular 21 requires ^20.19 / ^22.12 / ^24) |
| Python | 3.7 or newer (no packages needed) |

The UI loads the Heebo font and Material Icons from Google Fonts. Offline, it falls back to Arial and the icons show as text.

## 3. Installation and running

### 3.1 Database
```powershell
cd db
.\setup-database.ps1                 # default instance "."
# .\setup-database.ps1 -Server ".\SQLEXPRESS"
```
This creates `ValidationSystemDb` and runs all scripts in order, ending with `99_verify.sql`. That script checks the expected violating records of every example, that the catalog matches the physical schema, and that the read-only execution user has only read access. The scripts touch only `ValidationSystemDb`. Re-running resets its sample data and saved validations.

If you use a different server, also update `ConnectionStrings:ValidationDb` in `backend/src/ValidationSystem.Api/appsettings.json`. For the tests, set the environment variable `VALIDATION_TEST_DB`.

### 3.2 Start everything (one command)
```powershell
.\scripts\start-dev.ps1
```
This opens three windows: the API on http://localhost:5080, the AI mock on http://localhost:5005, and Angular on **http://localhost:4200**. The first run executes `npm install` automatically.

### 3.3 Or start each part manually
```powershell
# API
cd backend
dotnet run --project src/ValidationSystem.Api --launch-profile http      # http://localhost:5080

# AI mock
cd ai-mock
python server.py --port 5005                                            # http://localhost:5005/health

# Frontend
cd frontend
npm install
npm start                                                               # http://localhost:4200 (proxies /api to :5080)
```
The OpenAPI document is available at http://localhost:5080/openapi/v1.json (Development environment).

## 4. Tests

```powershell
cd backend
dotnet test                       # 77 unit + 41 integration tests

cd ..\ai-mock
python -m unittest discover -s tests -v     # 15 tests

cd ..\frontend
npx ng build                      # production build (type checking of all templates)
```

Integration tests need the seeded database (step 3.1) and Python on the PATH, because the AI end-to-end tests start `ai-mock/server.py` themselves. They:
- run all 7 example validations through the real engine against SQL Server, and assert the exact violating records and the exact Hebrew failure reasons;
- run ad-hoc nested AND/OR definitions, 2-hop relationships, Between / In / StartsWith, and the row cap;
- attempt SQL injection through values, field names, the root entity and unknown JSON properties;
- verify that the executor refuses non-SELECT text, and that SQL Server itself blocks anything beyond reading the business schema;
- exercise the REST API (CRUD, preview, execute, results search, filter and paging, 400/404/503);
- cover the full AI flow: API → Python mock → translation → validation → save → execute, with the same results as the manual validations.

| Suite | Result at submission |
|---|---|
| `ValidationSystem.Core.Tests` | 77 / 77 passed |
| `ValidationSystem.IntegrationTests` | 41 / 41 passed |
| `ai-mock` (unittest) | 15 / 15 passed |
| `db/99_verify.sql` | all checks passed |
| Angular production build | succeeded, no warnings |

## 5. Using the system

1. **הבדיקות שלי** (validations list): search, filter by entity, run a validation, open its latest results, edit or delete it.
2. **בדיקה חדשה** (validation builder): choose an entity, then build conditions: field, then operator, then a value or another field. Combine them with "כל התנאים (וגם)" / "לפחות אחד (או)" and nested groups. The side panel shows the rule in plain Hebrew, validation errors, and (collapsed) the generated SQL.
3. **Results**: the number of violating records, run status and duration, run history, and a table of records with every failure reason. You can search across records, values and reasons, filter by a specific failed condition, and page through results.
4. **יצירה בעזרת AI** (AI assistant): describe the rule in Hebrew, review the proposal, then approve (save, or save and run) or refine it in the builder. A demo deep link pre-fills and submits a request: `http://localhost:4200/ai?q=עובדים מתחת לגיל 18 עם שכר חודשי מעל 15000`.

## 6. Assumptions

- A validation describes the condition that identifies **invalid** records. The generated query returns the records that match it.
- Relationships in the catalog are **many-to-one** (employee → department, manager, position). Joining through them never duplicates a root record, so each violating record appears once with all of its reasons.
- Standard SQL `NULL` semantics apply: a comparison against a missing value, or through a missing relationship (an employee with no department), is *unknown* and does not flag the record. Use "ריק / חסר" (empty / missing) to detect missing data. For text, "empty" also covers `''` and whitespace-only values.
- Ages and seniority are computed fields (`FullYearsSince(BirthDate)`) evaluated against today's date at run time. Young employees in the seed data are born relative to the current date, so the expected results never go stale.
- Result snapshots are capped at 5,000 records per run (configurable via `Validation:MaxStoredResults`). The total violation count is always exact; the run is marked as truncated when the cap applies.
- No authentication or authorization; this was out of scope for the assignment. Execution security is enforced in the engine and the database (see the architecture document).

## 7. Known limitations

- **One-to-many conditions** ("a department with no employees", "an employee with more than 3 …") are not supported. That would need `EXISTS` / aggregate support in the generator.
- **Operators**: no regular expressions, no case-sensitive text comparison, and no date arithmetic in values (e.g. "today minus 30 days"). Relative dates are covered only through the whitelisted computed fields (`FullYearsSince`, `DaysSince`).
- **NotEquals** follows SQL semantics, so records where the field is `NULL` are not returned.
- **Metadata administration** is done by editing the `meta.*` tables (see `db/06_seed_metadata.sql`), followed by `POST /api/metadata/refresh`. There is no admin UI.
- **AI mock**: a rule-based Hebrew matcher, not a language model. It understands phrasing built from catalog labels and synonyms (see [docs/AI.md](docs/AI.md)), and returns `unsupported` otherwise.
- **Frontend tests**: the Angular application has no unit tests. It is verified by a type-checked production build and was checked manually / visually against the running API. The backend and the AI component carry the automated test coverage.
- Search over results uses `LIKE` on a stored text column, which is fine for snapshots of up to 5,000 rows per run. Larger volumes would call for full-text indexing.

## 8. AI tools used

This solution was developed with **Claude Code (Anthropic's Claude, in VS Code)** acting as a pair programmer:
- **Purpose:** inspecting the environment, proposing the architecture and phased plan (approved before implementation), generating the code, SQL scripts, tests and documentation, running builds and tests after each phase, and diagnosing failures. Examples of issues it diagnosed: unknown JSON node types being accepted at the root, chunked HTTP bodies not being read by the Python server, and Hebrew status words being misread as fields.
- **Verification:** every phase was built and tested before it was committed (see the git history: one commit per phase). The UI was checked with headless-browser screenshots against the running stack.
- **Inside the product:** no external AI service is called. The "AI" in the product is the Python mock described in [docs/AI.md](docs/AI.md), behind an interface designed so a real provider can replace it.
