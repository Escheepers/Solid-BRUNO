# Bruno Vehicle Hire

A small vehicle-hire management system built as a "Solid Developer — Movement Assessment" submission: a Clean Architecture, CQRS-flavored .NET backend and a feature-modular Angular frontend, managing Vehicles, Customers, and Bookings with the domain business rules (booking overlap prevention, soft-delete/anonymize semantics, cancellation rules) enforced in the domain model itself, not scattered across handlers or controllers.

This README covers the complete system — all 6 epics (Vehicles, Customers, Bookings, the Customer Summary view, and submission-readiness work including seed data, code coverage, and optional observability) — architecture, how to run it locally, and the assumptions made along the way.

## Architecture

**Clean Architecture, four layers, one dependency direction:**

```
Api  ──────────┐
               ├──▶ Application ──▶ Domain
Infrastructure ┘
```

- **`BrunoVehicleHire.Domain`** — the rich domain model: `Vehicle`, `Customer`, `Booking`, and the `DateRange` value object. Zero project references, zero framework dependencies. Every entity is constructed only via a static factory (`Vehicle.Create`, `Customer.Create`, `Booking.Create`) that enforces its own invariants; every subsequent state change goes through a named method on the aggregate (`SoftDelete()`, `Anonymize()`, `Cancel()`, `Complete()`, `Restore()`) — no entity exposes a public settable property.
- **`BrunoVehicleHire.Application`** — CQRS via [MediatR](https://github.com/jbogard/MediatR): every write is a `Command`, every read a `Query`, each with exactly one handler, organized as a vertical slice per entity (`Application/Vehicles`, `Application/Customers`, `Application/Bookings`, each with its own `Commands/Queries/Dtos/Validators`). FluentValidation validators run inside a shared MediatR pipeline behavior (`ValidationBehavior<TRequest,TResponse>`) ahead of every handler — never invoked manually.
- **`BrunoVehicleHire.Infrastructure`** — EF Core (`AppDbContext`), the Postgres-backed repositories (`IVehicleRepository`/`ICustomerRepository`/`IBookingRepository`, one per aggregate root, no generic `IRepository<T>`), the `BookingCompletionSweepService` background service, and `DatabaseSeeder`.
- **`BrunoVehicleHire.Api`** — thin ASP.NET Core controllers that only dispatch through MediatR; the DI composition root (`Program.cs`).

An architecture fitness test (`ArchitectureFitnessTests`, via NetArchTest) mechanically enforces the dependency direction above — e.g. that nothing in `Api.Controllers` references `Infrastructure` directly.

**A few architecture decisions worth knowing before reading the code** (the full rationale for all 18 lives in `_bmad-output/planning-artifacts/architecture/.../ARCHITECTURE-SPINE.md`):

| # | Decision |
|---|---|
| AD-6 | All ids are `Guid.CreateVersion7()` (time-ordered), assigned by the entity's own constructor — never a DB-generated value. |
| AD-7 | Booking overlap prevention is two layers: an application-level check using the pure `DateRange.Overlaps()` value object, backstopped by a database-level Postgres `EXCLUDE USING GIST` constraint (`btree_gist`) that closes the race window between two concurrent requests. |
| AD-8 | Every error is an RFC 9457 `ProblemDetails` response. Shape/presence validation (missing fields) → `400`; anything requiring loaded state to evaluate (overlap, cancel guards, duplicate email/registration number, booking a soft-deleted vehicle) → `409`. |
| AD-11 | API-key authentication via a custom `AuthenticationHandler`, enforced by a global `FallbackPolicy` (secure by default) — the key travels in an `X-Api-Key` header. |
| AD-12 | `Customer.Email`/`PhoneNumber` are encrypted at rest via an EF Core value converter backed by ASP.NET Core's Data Protection API (AES under a framework-managed key ring) — never a hand-rolled cipher. A separate `EmailHash` shadow column (deterministic SHA-256) carries the DB-level "email unique" constraint, since the encrypted column itself is non-deterministic ciphertext and can't be indexed for uniqueness. |
| AD-13 | Customer soft-delete (reversible, PII untouched) and anonymize (irreversible, PII scrubbed) are two entirely separate operations, never conflated — both use the same `IsDeleted`/`IsAnonymized` global-query-filter exclusion mechanism as Vehicle's own soft-delete. |
| AD-16/17 | A "deleted" booking is `Cancel()`led, never physically removed — Bookings only ever transition `Active → Cancelled` or `Active → Completed`. The `Completed` transition is driven by `BookingCompletionSweepService`, a timer-based `BackgroundService` that dispatches a normal `CompleteBookingCommand` for every past-`EndDate` Active booking — no Query handler ever writes. |
| AD-18 | Business-rule and constraint tests run against a real, ephemeral Postgres container (Testcontainers), not EF Core's InMemory provider — InMemory doesn't enforce unique indexes, FKs, or the AD-7 exclusion constraint. |

**Frontend:** Angular, feature-module structure (`frontend/src/app/features/{vehicles,customers,bookings,customer-summary}`), native Angular Signals for local/UI state, TanStack Query's Angular adapter for server-state fetching/caching/invalidation. No NgRx.

## Tech stack

| | |
|---|---|
| Backend | .NET 10, ASP.NET Core, EF Core 10 (code-first migrations), MediatR 14 (Community edition), FluentValidation |
| Database | PostgreSQL 18 (`btree_gist` extension), run via `docker-compose` |
| Frontend | Angular 22, TanStack Query (`@tanstack/angular-query-experimental`) |
| Testing | xUnit, FluentAssertions, Testcontainers.PostgreSql, NetArchTest.Rules |
| Observability *(optional, Story 6.3)* | Serilog + Serilog.Sinks.Grafana.Loki, Grafana/Loki via `docker-compose.observability.yml` |

## How to run it

### Prerequisites

- .NET 10 SDK
- Node.js + npm (for the Angular frontend)
- Docker (for PostgreSQL, via `docker-compose`)

### 1. Start PostgreSQL

```bash
docker-compose up -d
```

This starts a PostgreSQL 18 container (`bruno-postgres`, port 5432) with the `btree_gist` extension pre-created (`docker/postgres-init`), plus a Docker volume for the Data Protection key ring so encrypted PII survives a `docker-compose down`/`up` cycle.

### 2. Obtain and set the MediatR license key

MediatR 14 requires a license key at startup — a free, no-approval **Community edition** key, obtained by registering at MediatR's licensing portal ([mediatr.io](https://mediatr.io)). Store it via `dotnet user-secrets` on the Api project (never in `appsettings.json` — it's not a secret you'd want committed, but this keeps the same pattern as every other credential in this project):

```bash
cd src/BrunoVehicleHire.Api
dotnet user-secrets set "MediatR:LicenseKey" "<your-key>"
```

If you skip this step, the app still runs — MediatR logs a one-time warning that the Community edition is "allowed for development and testing scenarios" — but a real license key is one minute of setup and removes the warning.

### 3. Set the dev API key and DB connection string via user-secrets

Like the MediatR license key above, the local dev API key and the Postgres connection string (with its password) are sourced via `dotnet user-secrets` rather than committed to `appsettings.Development.json`. Set them to the same placeholder values used throughout local dev and this README:

```bash
cd src/BrunoVehicleHire.Api
dotnet user-secrets set "ApiKey:Key" "local-dev-only-key-change-me"
dotnet user-secrets set "ConnectionStrings:Postgres" "Host=localhost;Port=5432;Database=brunovehiclehire;Username=bruno;Password=bruno_dev_password"
```

### 4. Create the `.env` file for docker-compose

`docker-compose.yml` reads the Postgres password from a gitignored `.env` file at the repo root rather than committing it. Copy the provided template (already using the same placeholder password as above):

```bash
cp .env.example .env
```

### 5. Create the frontend's dev environment file

`frontend/src/environments/environment.development.ts` is gitignored (it carries the dev API key). Copy the provided template (already using the same placeholder key as above):

```bash
cp frontend/src/environments/environment.development.ts.example frontend/src/environments/environment.development.ts
```

### 6. Run the backend

```bash
cd src/BrunoVehicleHire.Api
dotnet run
```

On startup this:
1. Applies any pending EF Core migrations against the `docker-compose` Postgres instance (no manual migration step, ever).
2. Seeds the database with synthetic demo data — Vehicles, Customers, and Bookings — **only** in the `Development` environment (the default for `dotnet run`), and **only** if the database is currently empty of Vehicles. A repeated `dotnet run` against an already-seeded database is a no-op, never a duplicate. See [Seed data](#seed-data) below.

The API listens on `https://localhost:7291` (and `http://localhost:5297`). Swagger UI is at **`https://localhost:7291/swagger`** — use its "Authorize" button to supply the API key (see below) and exercise every endpoint directly.

### 7. Run the frontend

```bash
cd frontend
npm install   # first time only
npm start     # ng serve
```

The Angular app is at **`http://localhost:4200`**. Its dev server proxies `/api/*` requests to the backend (`frontend/proxy.conf.json`, targeting `https://localhost:7291`) and already attaches the `X-Api-Key` header automatically (`frontend/src/app/core/api-client/api-key.interceptor.ts`), reading the key from `frontend/src/environments/environment.ts` (replaced with `environment.development.ts` at build time) — pre-set to match the dev API key from step 3, so no extra configuration beyond copying the file in step 5 is needed for a local run.

### The API key, if calling the API directly

Every endpoint (except Swagger's own UI assets) requires an `X-Api-Key` header. The local development key is `local-dev-only-key-change-me` (set via `dotnet user-secrets`, see step 3) — e.g.:

```bash
curl https://localhost:7291/api/vehicles -H "X-Api-Key: local-dev-only-key-change-me"
```

### Resetting to a completely fresh database

```bash
docker-compose down -v   # -v also removes the Postgres data volume
docker-compose up -d
cd src/BrunoVehicleHire.Api && dotnet run
```

## Seed data

`DatabaseSeeder` (`src/BrunoVehicleHire.Infrastructure/Seeding`) populates a fresh database with synthetic-but-realistic data via [Bogus](https://github.com/bchavez/Bogus), constructed through the exact same domain factories/methods and `SaveChangesAsync` call as any real write — never raw SQL, so PII encryption applies identically to seed data and to a real customer created through the UI.

Every seed run uses a fixed Bogus random seed, so a fresh `docker-compose down -v && up` + `dotnet run` produces **byte-identical** seed data every time — reproducible for grading/discussion, not different each run. It deliberately covers the interesting states, not just plain rows, so every status/badge treatment is visible without manually creating each one first:

- **15 Vehicles**, 2 soft-deleted (reappear via "Show inactive vehicles" / Restore)
- **15 Customers**, 1 soft-deleted and 1 anonymized (reappear via "Show inactive customers"; the anonymized customer's booking history is still visible, showing "Customer (anonymized)")
- **~45+ Bookings**, spanning all three statuses: `Active` (future dates), `Completed` (past dates — completed by the seeder itself, since the background sweep only completes bookings that were already `Active` when it runs), and `Cancelled`

Seeding is gated by a `Seed:Enabled` configuration flag (`appsettings.json`: `false`; `appsettings.Development.json`: `true`) rather than checking the environment name directly — every integration test that exercises the full ASP.NET Core pipeline also runs under the `"Development"` environment name (for good reasons of its own), so environment name alone can't distinguish "a real local run" from "a test run." Each such test file explicitly overrides `Seed:Enabled` back to `false`.

## Running the tests

```bash
dotnet test
```

Runs the full suite: Domain, Application, and Infrastructure unit tests, plus the Integration.Tests project — the latter spins up real, ephemeral PostgreSQL containers via Testcontainers for anything that needs a genuine relational database (unique constraints, foreign keys, the AD-7 overlap exclusion constraint, and the AD-12 PII value converter), so it takes a few minutes and requires Docker to be running.

## Code coverage reports

Both reports below are **local, on-demand artifacts** — generated on request, browsed locally, and `.gitignore`d. Neither is wired to a CI pipeline (this project has none).

### Backend

```bash
dotnet tool restore                                            # first time only -- installs ReportGenerator locally, from .config/dotnet-tools.json
dotnet test --collect:"XPlat Code Coverage" --results-directory ./TestResults
dotnet reportgenerator -reports:"./TestResults/**/coverage.cobertura.xml" -targetdir:"./coverage/backend" -reporttypes:Html
```

Open `coverage/backend/index.html` in a browser. Coverage is collected via [Coverlet](https://github.com/coverlet-coverage/coverlet) (already referenced in every test project) and rendered by [ReportGenerator](https://github.com/danielpalme/ReportGenerator).

### Frontend

```bash
cd frontend
npm run test:coverage   # ng test --coverage --watch=false
```

Open `frontend/coverage/frontend/index.html` in a browser. This is Angular's own Istanbul-based coverage reporter — no extra tooling needed.

> **Note:** epics.md's Story 6.2 names the frontend flag `--code-coverage` — this project's `ng test` runs on Vitest (Angular 22's default test runner), whose actual, already-working flag (used throughout this build) is `--coverage`. The command above is what genuinely works in this repo.

## Observability (optional)

This is Story 6.3, an explicitly optional stretch goal — **its absence changes nothing else about this submission.** The backend logs via [Serilog](https://serilog.net/) unconditionally (console output, replacing the ASP.NET Core default provider as a drop-in for every existing `ILogger<T>` call site), but shipping those structured logs to [Grafana Loki](https://grafana.com/oss/loki/) for visualization is a separate, opt-in step:

```bash
docker-compose -f docker-compose.observability.yml up -d
```

This starts Loki (port 3100) and Grafana (port 3000) as their own stack — deliberately **not** merged into the base `docker-compose.yml` (Postgres only), so running the app normally never requires it. Grafana is pre-provisioned entirely via mounted files (a Loki datasource plus one dashboard, `Bruno Vehicle Hire API Overview`, with a request-rate panel) — no manual "add datasource"/"import dashboard" steps.

To actually ship logs there, also set `Serilog:Loki:Enabled` to `true` before running the backend (it defaults to `false`, so a plain `dotnet run` never attempts a Loki connection and never produces connection-refused retry noise):

```bash
cd src/BrunoVehicleHire.Api
Serilog__Loki__Enabled=true dotnet run   # PowerShell: $env:Serilog__Loki__Enabled="true"; dotnet run
```

Then open **`http://localhost:3000`** (login `admin` / `bruno-admin`) and browse to the provisioned dashboard — its request-rate panel starts rendering real, non-zero data the moment the API receives any traffic at all (e.g. just browsing the Angular UI or hitting Swagger), no deliberately-triggered condition required.

No PII-scrubbing infrastructure was added: the app already never logs request/response bodies or raw entity objects (`UseSerilogRequestLogging()`'s output is method/path/status/elapsed-time only), proven by an automated test (`CustomerLoggingPiiTests`) that captures every log event from a real create-customer request and asserts the plaintext email never appears in any of them.

When you're done, tear the stack back down:

```bash
docker-compose -f docker-compose.observability.yml down
```

## Assumptions made

These are the explicit assumptions logged against the brief (`SPEC.md`), carried through unchanged:

- **.NET and Angular versions** are the latest stable available at implementation time — the brief does not pin versions (.NET 10, Angular 22 as built).
- **A single shared API key** is sufficient; no per-user login, roles, or OAuth/JWT are implemented on either frontend or backend beyond that key.
- **Local-run only** — this README covers local setup via `docker-compose` + `dotnet run` + `ng serve`; no cloud hosting or deployment is in scope.
- **PII encryption is application-layer**, via an EF Core value converter backed by ASP.NET Core's Data Protection API (AES), not database-level Transparent Data Encryption — chosen for portability across local/dev environments without a managed database service.
- **Synthetic seed PII is generated via Bogus**, never real personal data, and is inserted through the identical domain-factory + `SaveChangesAsync` path as any other write.

## Project layout

```
src/
  BrunoVehicleHire.Domain          -- entities, value objects (no dependencies)
  BrunoVehicleHire.Application     -- CQRS commands/queries, validators, DTOs
  BrunoVehicleHire.Infrastructure  -- EF Core, repositories, background services, seeding
  BrunoVehicleHire.Api             -- controllers, auth, Program.cs composition root
tests/
  BrunoVehicleHire.Domain.Tests
  BrunoVehicleHire.Application.Tests
  BrunoVehicleHire.Infrastructure.Tests
  BrunoVehicleHire.Api.Tests
  BrunoVehicleHire.Integration.Tests  -- full-pipeline + real-Postgres tests
frontend/
  src/app/core       -- API client, auth interceptor, shared models
  src/app/features   -- one folder per feature: vehicles, customers, bookings, customer-summary
  src/app/shared     -- shared UI components (data-table, modal, badge, toast, ...)
docker-compose.yml               -- PostgreSQL (+ btree_gist init script)
docker-compose.observability.yml -- optional, opt-in: Loki + Grafana (Story 6.3)
observability/grafana/           -- Grafana provisioning (datasource + dashboard JSON)
```
