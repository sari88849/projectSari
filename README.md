# Dynamic Data Validation System

A generic system that lets business users, with no SQL knowledge, **define, save, edit, run and analyze data validation rules** in a Hebrew, right-to-left UI. Rules can be built in a visual builder or proposed by AI from a free-text description. Every rule becomes a secure, parameterized `SELECT` generated at run time from a metadata catalog. No validation has dedicated code or a hand-written query.

> **Terminology.** The assignment's *control* (בקרה) is called a **validation** throughout the code, API and documentation. Examples: `app.Validations`, `/api/validations`, `ValidationService`. In the UI it is a "בדיקה" (check).

| Layer | Technology |
|---|---|
| Backend | .NET 10 Web API (C#), Dapper, Microsoft.Data.SqlClient |
| Validation engine | Plain C# (`ValidationSystem.Core`), no database dependency |
| AI mock (Stage B) | Python 3.7+ (standard library only), HTTP service. **Simulated AI, no real model.** |
| Frontend | Angular 21 (standalone components, signals, zoneless), Angular Material, Hebrew RTL |
| Database | SQL Server 2016 or later |

Documentation:
- **[docs/OVERVIEW.md](docs/OVERVIEW.md): short submission guide.** Architecture and validation mechanism in brief, and where each deliverable is.
- [docs/STAGE_B_DEMO.md](docs/STAGE_B_DEMO.md): step-by-step Stage B (AI) demonstration with screenshots
- [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md): detailed architecture, the engine, dynamic SQL generation, security
- [docs/EXAMPLES.md](docs/EXAMPLES.md): the example validations, their definitions and expected results
- [docs/AI.md](docs/AI.md): AI context, response schema, the mock, and how to plug in a real provider

---

## 1. Repository layout

```
db/                      SQL Server scripts: schema, metadata catalog, seed data, self-check (99_verify.sql)
backend/
  src/ValidationSystem.Core            engine: definition model, catalog, validator, SQL builder, reasons, AI abstractions
  src/ValidationSystem.Infrastructure  SQL Server access, catalog loader, read-only executor, AI HTTP client
  src/ValidationSystem.Api             REST API (controllers, DTOs, error handling)
  tests/ValidationSystem.Core.Tests        unit tests
  tests/ValidationSystem.IntegrationTests  tests against the real SQL Server + Python mock
ai-mock/                 Python AI mock service + unit tests
shared/ai-response.schema.json   AI response format (used by .NET and Python)
frontend/                Angular application
scripts/start-dev.cmd    double-click launcher (runs start-dev.ps1)
scripts/start-dev.ps1    starts API + AI mock + Angular in separate windows
docs/                    overview, Stage B demo, architecture, examples, AI flow
```

## 2. Prerequisites

The project was developed and verified on Windows 10 with the versions below.

| Requirement | Needed | Verified with |
|---|---|---|
| SQL Server | **2016 or later**, Windows authentication | SQL Server 2019 Enterprise (default instance `.`); **LocalDB 2016** (`(localdb)\MSSQLLocalDB`, Express engine): setup, self-check and all 42 integration tests pass. A named SQL Express *instance* was not available for testing. |
| `sqlcmd` | ODBC-based sqlcmd or the newer go-sqlcmd, on PATH | ODBC sqlcmd 15 and **go-sqlcmd v1.10.0**: setup and self-check pass with both. The script always passes `-C` (trust the server certificate, needed by sqlcmd 18, which encrypts by default) and passes `-f 65001` only to clients that support it. go-sqlcmd has no `-f` and reads UTF-8 natively. sqlcmd 18 (ODBC Driver 18) itself was not tested. |
| .NET SDK | **Any .NET 10 SDK** (`backend/global.json`: `10.0.100` with `rollForward: latestFeature`) | 10.0.404 (installed) and 10.0.115 (oldest feature band). Build with no warnings, and all 80 unit + 42 integration tests pass with both. |
| Node.js | 20.19+, 22.12+ or 24+ (Angular 21 requirement), with npm | 22.23.5 |
| Python | 3.7+, available as `python` or `py -3` | 3.7.9. No packages are needed. |
| Browser | Any modern browser | Chrome |

**Database permissions.** The Windows account that runs `setup-database.ps1` must be able to create a database (e.g. `sysadmin` or `dbcreator`). The account that runs the API must be able to read and write `ValidationSystemDb` and impersonate the user `validation_reader` (`EXECUTE AS USER`). Everything was verified with a `sysadmin` login. `db_owner` of `ValidationSystemDb` is expected to be sufficient for the API, but this was not tested.

**Folder location (Windows path length).** Extract the project to a reasonably short path, e.g. `C:\ValidationSystem` or a folder in Downloads; keep the project folder path under about 100 characters. Inside a very deep folder, the full path of the native SQL client library in the build output (`…\bin\…\runtimes\win-x64\native\Microsoft.Data.SqlClient.SNI.dll`) goes past the Windows limit of about 255 characters. Database access then fails with `DllNotFoundException … The filename or extension is too long`. This was reproduced at 255 characters; the same package worked at 178 and 236.

**Internet.** `npm install` and the NuGet restore on the first build need internet access. The UI loads the Heebo font and Material Icons from Google Fonts; offline, it falls back to Arial and the icons show as text.

### Configuration

| Setting | Where | Default |
|---|---|---|
| Database connection | `ConnectionStrings:ValidationDb` in `backend/src/ValidationSystem.Api/appsettings.json`, or the environment variable `ConnectionStrings__ValidationDb` | `Server=.;Database=ValidationSystemDb;Trusted_Connection=True;TrustServerCertificate=True` |
| Query timeout / execution user | `Database:CommandTimeoutSeconds`, `Database:ExecutionUser` | 30 s, `validation_reader` |
| Result snapshot cap | `Validation:MaxStoredResults` | 5000 |
| AI service URL | `AiService:BaseUrl`, `AiService:TimeoutSeconds` | `http://localhost:5005`, 15 s |
| CORS origins | `Cors:Origins` | `http://localhost:4200` |
| Ports | API `backend/.../Properties/launchSettings.json` (5080); Angular proxy `frontend/proxy.conf.json` (→ 5080); AI mock `--port` (5005) | 5080 / 4200 / 5005 |
| Test database | Environment variable `VALIDATION_TEST_DB` (full connection string) | same as the API default |
| Python for tests | Environment variable `PYTHON` (executable used by the AI end-to-end tests) | `python` |

**go-sqlcmd and LocalDB:** go-sqlcmd cannot resolve the `(localdb)\MSSQLLocalDB` server name ("no named pipe instance matching …"). Pass the instance's named pipe instead. It changes each time LocalDB starts, so read it first:
```powershell
sqllocaldb start MSSQLLocalDB
$pipe = ((sqllocaldb info MSSQLLocalDB | Select-String 'Instance pipe name') -split ': ', 2)[1]   # np:\\.\pipe\LOCALDB#...\tsql\query
.\db\setup-database.ps1 -Server $pipe
```
The ODBC-based sqlcmd accepts `(localdb)\MSSQLLocalDB` directly. The API and tests (Microsoft.Data.SqlClient) always accept the `(localdb)` name.

**Using LocalDB instead of the default instance:**
```powershell
.\db\setup-database.ps1 -Server "(localdb)\MSSQLLocalDB"
$env:ConnectionStrings__ValidationDb = 'Server=(localdb)\MSSQLLocalDB;Database=ValidationSystemDb;Trusted_Connection=True;TrustServerCertificate=True'
$env:VALIDATION_TEST_DB = $env:ConnectionStrings__ValidationDb    # for dotnet test
```

## 3. Installation and running

### 3.1 Database
```powershell
.\db\setup-database.ps1                                  # default instance "."
# .\db\setup-database.ps1 -Server "(localdb)\MSSQLLocalDB"
# .\db\setup-database.ps1 -Server ".\SQLEXPRESS"
```
The script first checks that `sqlcmd` exists, that the server is reachable and that it is SQL Server 2016+. It then creates `ValidationSystemDb` and runs all scripts in order, ending with `99_verify.sql`. That script checks:
- the expected violating records of every example;
- that the catalog matches the physical schema;
- that the read-only execution user has only read access.

The scripts touch only `ValidationSystemDb`. **Re-running resets** its sample data and saved validations.

### 3.2 Start everything
From Explorer, double-click **`scripts\start-dev.cmd`**. Double-clicking a `.ps1` file only opens it in Notepad. From a terminal:
```powershell
.\scripts\start-dev.ps1
```
This opens three windows: the API on http://localhost:5080, the AI mock on http://localhost:5005, and Angular on **http://localhost:4200** (open this one). The first run executes `npm install` automatically. The script:
- refuses to start if one of these ports is already taken by an earlier run, and shows which process holds it;
- uses the first working Python 3 command (`python`, `py -3`);
- if no Python 3 is found, starts everything except the AI mock.

**Troubleshooting.** If the page shows "שרת ה-API אינו זמין" (API unavailable), or `/api/...` calls fail with HTTP 500 and an empty `text/plain` body, the Angular proxy cannot reach the API. Check that the API window shows `Now listening on: http://localhost:5080`, and that http://localhost:5080/api/health returns `{"status":"ok"}`. Real API errors always return a JSON problem-details body with a Hebrew title.

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
dotnet test                                  # 80 unit + 42 integration tests

cd ..\ai-mock
python -m unittest discover -s tests -v      # 15 tests

cd ..\frontend
npx ng build                                 # production build (type-checks all templates)
```

Integration tests need the seeded database (step 3.1) and Python 3, because the AI end-to-end tests start `ai-mock/server.py` themselves. Stop a running API first: `dotnet test` rebuilds the API project, and a running API locks its files. The tests:
- run all 7 example validations through the real engine against SQL Server, and assert the exact violating records and the exact Hebrew failure reasons;
- run ad-hoc nested AND/OR definitions, 2-hop relationships, Between / In / StartsWith, and the row cap;
- attempt SQL injection through values, field names, the root entity and unknown JSON properties;
- verify that the executor refuses non-SELECT text, and that SQL Server itself blocks anything beyond reading the business schema;
- exercise the REST API (CRUD, preview including unfinished rules, execute, results search, filter and paging, 400/404/503);
- cover the full AI flow: API → Python mock → translation → validation → save → execute, with the same results as the manual validations.

| Suite | Result |
|---|---|
| `ValidationSystem.Core.Tests` | 80 / 80 passed |
| `ValidationSystem.IntegrationTests` | 42 / 42 passed: SQL Server 2019, and also LocalDB 2016 |
| `ai-mock` (unittest) | 15 / 15 passed |
| `db/99_verify.sql` | all checks passed: SQL Server 2019 and LocalDB 2016 |
| Angular production build | succeeded, no warnings |

The tests add run history (and temporarily create and delete validations). Re-run `setup-database.ps1` for a clean demo state.

## 5. Using the system

1. **הבדיקות שלי** (my validations): search, filter by entity, run a validation, open its latest results, edit or delete it.
2. **בדיקה חדשה** (validation builder): choose an entity, then build conditions: field, then operator, then a value or another field. Combine them with "כל התנאים (וגם)" (all conditions, AND) / "לפחות אחד (או)" (at least one, OR) and nested groups. The side panel shows the rule in plain Hebrew, validation errors, and (collapsed) the generated SQL.
3. **Results**: the number of violating records, run status and duration, run history, and a table of records with every failure reason. You can search across records, values and reasons, filter by a specific failed condition, and page through results.
4. **יצירה בעזרת AI** (AI assistant, Stage B): describe the rule in Hebrew, review the proposal, then approve (save, or save and run) or refine it in the builder. See [docs/STAGE_B_DEMO.md](docs/STAGE_B_DEMO.md).

## 6. Assumptions

- A validation describes the condition that identifies **invalid** records. The generated query returns the records that match it.
- Relationships in the catalog are **many-to-one** (employee → department, manager, position). Joining through them never duplicates a root record, so each violating record appears once with all of its reasons.
- Standard SQL `NULL` semantics apply: a comparison against a missing value, or through a missing relationship (an employee with no department), is *unknown* and does not flag the record. Use "ריק / חסר" (empty / missing) to detect missing data. For text, "empty" also covers `''` and whitespace-only values.
- Ages and seniority are computed fields (`FullYearsSince(BirthDate)`) evaluated against today's date at run time. Young employees in the seed data are born relative to the current date, so the expected results never go stale.
- Result snapshots are capped at 5,000 records per run (configurable). The total violation count is always exact; the run is marked as truncated when the cap applies.
- Business users work only with the entities, fields and relationships an administrator has configured in the metadata catalog (`meta.*`).
- Single-user local deployment. Authentication and authorization were out of scope. Execution security is enforced in the engine and in the database (see the architecture document).

## 7. Known limitations

- **One-to-many conditions** ("a department with no employees", "an employee with more than 3 …") are not supported.
- **Operators**: no regular expressions, no case-sensitive text comparison, and no date arithmetic in values (e.g. "today minus 30 days"). Relative dates are covered only through the whitelisted computed fields (`FullYearsSince`, `DaysSince`).
- **NotEquals** follows SQL semantics, so records where the field is `NULL` are not returned.
- **Metadata administration** is done by editing the `meta.*` tables (see `db/06_seed_metadata.sql`), followed by `POST /api/metadata/refresh`. There is no admin UI.
- **AI (Stage B) is simulated.** The Python mock is a rule-based Hebrew matcher, not a language model. It understands phrasing built from catalog labels and synonyms (see [docs/AI.md](docs/AI.md)), and returns `unsupported` otherwise.
- **Execution is synchronous.** A run executes inside the HTTP request, bounded by the command timeout. There is no scheduling and no background execution.
- **Frontend tests**: the Angular application has no unit tests. It is verified by a type-checked production build and by visual checks against the running system. The backend and the AI component carry the automated test coverage.
- Search over results uses `LIKE` on a stored text column, which is fine for snapshots of up to 5,000 rows per run.
- Windows-oriented scripts (`.ps1` / `.cmd`). The .NET, Angular and Python parts are cross-platform, but only Windows was tested.

## 8. Possible future extensions (not implemented)

- **Real AI provider:** implement `IAiValidationProvider` (or a Python `AiProvider`) that calls an LLM with the existing context and schema. The engine, validator and UI stay unchanged ([docs/AI.md §5](docs/AI.md#5-replacing-the-mock-with-a-real-model)).
- **One-to-many rules:** `EXISTS` / `COUNT` conditions over child collections (e.g. "department without employees").
- **More operators and functions:** relative-date values ("within the last N days"), regex / pattern matching, more whitelisted computed functions.
- **Metadata admin screen** to manage entities, fields, labels and synonyms without SQL.
- **Scheduling and notifications:** periodic runs, asynchronous execution for large tables, alerts when the violation count changes.
- **Security and multi-user:** authentication, roles (who may define / run / view), and an audit trail of definition changes.
- **Results:** export to Excel/CSV, trends across runs, full-text search for very large snapshots.
- **Frontend tests:** component and end-to-end tests (e.g. Playwright).

## 9. AI tools used

| Tool | Purpose |
|---|---|
| **Claude Code** (Anthropic Claude, in VS Code) | Code generation, development assistance, debugging and project improvements. Specifically: generating the backend, frontend, Python mock, SQL scripts, tests and documentation according to an architecture and phased plan that the developer approved; running builds and tests after each phase; diagnosing failures. Examples: unknown JSON node types accepted at the root; chunked HTTP bodies not read by the Python server; an unfinished builder row rejected as malformed JSON; a stale dev server causing HTTP 500s. |
| **ChatGPT** (OpenAI) | Planning, understanding the assignment requirements, and technical guidance. |

Every change was built and tested before it was committed (see the git history). The UI was checked with headless-browser screenshots against the running system.

**Inside the product no external AI service is called.** The "AI" of Stage B is the Python mock described in [docs/AI.md](docs/AI.md), behind an interface designed so a real provider can replace it.
