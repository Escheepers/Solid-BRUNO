---
title: 'Story 1.2: Local Database & Docker Compose Environment'
type: 'feature'
created: '2026-09-01'
status: 'done'
review_loop_iteration: 0
context: ['{project-root}/_bmad-output/implementation-artifacts/epic-1-context.md']
baseline_commit: 'ab5f3d90453615827da4837efb2e44a1532ad946'
---

<frozen-after-approval reason="human-owned intent — do not modify unless human renegotiates">

## Intent

**Problem:** Story 1.1 proved the solution compiles and its architecture rules are enforced, but nothing persists data yet — there is no real database, no EF Core wiring, and no migration.

**Approach:** Add Postgres via Docker Compose (with `btree_gist` enabled for Epic 4's later use, and a mounted volume for Epic 3's later Data Protection keys), add EF Core + Npgsql to `Infrastructure`, define a minimal schema-only `Vehicle` entity in `Domain` (properties matching `domain-model.md` exactly; Story 1.3 enriches this same class with real domain behavior — no second migration needed since the column shape won't change), generate the initial code-first migration, and wire `Database.Migrate()` to run on `Api` startup.

## Boundaries & Constraints

**Always:** Postgres 18 via `docker-compose.yml`, with the `btree_gist` extension created on startup and a named volume mounted for Data Protection keys (both unused this story, needed from Epic 3/4 onward — provisioning them now avoids a second infra change later). EF Core + `Npgsql.EntityFrameworkCore.PostgreSQL` (both pinned to the versions verified in the architecture spine: EF Core 10, Npgsql 10.0.3) added to `Infrastructure` only. The `AppDbContext` and its configuration live in `Infrastructure/Persistence/`. `Api` gains a project reference to `Infrastructure` for this exact purpose — DI composition root registration in `Program.cs` — which is the permitted exception to AD-1: only `Api.Controllers` (enforced by the existing fitness test) may not depend on `Infrastructure`, not the whole `Api` project. `Database.Migrate()` runs on `Api` startup, not as a manual step. The migration is proven by a real integration test against a live Postgres container (`Testcontainers.PostgreSql`, already pinned in the architecture), not just manual inspection.

**Ask First:** Nothing expected to trigger — schema and infra choices are already fully specified by `domain-model.md` and the architecture spine.

**Never:** No rich `Vehicle` domain behavior yet (no `Create()`/`SoftDelete()` factory methods, no private setters, no invariant checks) — that is Story 1.3's job, applied to the same file. No repository interfaces/implementations, no MediatR commands/queries — those arrive with Story 1.7. No query filters or soft-delete exclusion logic yet — that's wired when Story 1.7 actually needs it.

## I/O & Edge-Case Matrix

| Scenario | Input / State | Expected Output / Behavior | Error Handling |
|----------|--------------|---------------------------|----------------|
| Fresh database, first startup | Empty Postgres instance, `dotnet run` | `Database.Migrate()` applies the initial migration; `Vehicles` table exists with the exact schema from `domain-model.md` | N/A |
| Second startup, already migrated | Postgres instance with migration already applied | `Database.Migrate()` is a no-op; startup succeeds unchanged | N/A |
| Duplicate `RegistrationNumber` at the database level | Two rows inserted with the same `RegistrationNumber` via direct `DbContext` access (no application validation yet — that's Story 2.1) | Second insert fails on the unique index/constraint | Database-level `DbUpdateException`, not handled yet at this layer — Story 2.1 turns this into a proper 409 |

</frozen-after-approval>

## Code Map

- `docker-compose.yml` -- new, repo root -- Postgres 18 service, `btree_gist` extension init, named volume for Data Protection keys
- `src/BrunoVehicleHire.Domain/Vehicle.cs` -- new -- schema-only entity: `Id`, `RegistrationNumber`, `Make`, `Model`, `Year`, `DailyRate`, `IsDeleted`, `CreatedDate`, public settable properties for now (Story 1.3 enriches in place)
- `src/BrunoVehicleHire.Infrastructure/BrunoVehicleHire.Infrastructure.csproj` -- modify -- add EF Core + Npgsql packages
- `src/BrunoVehicleHire.Infrastructure/Persistence/AppDbContext.cs` -- new -- `DbSet<Vehicle>`, unique index on `RegistrationNumber`
- `src/BrunoVehicleHire.Infrastructure/Migrations/` -- new, generated -- initial migration creating `Vehicles`
- `src/BrunoVehicleHire.Api/BrunoVehicleHire.Api.csproj` -- modify -- add `ProjectReference` to `Infrastructure` (DI composition root only)
- `src/BrunoVehicleHire.Api/Program.cs` -- modify -- register `AppDbContext` with the Postgres connection string, call `Database.Migrate()` on startup, register Data Protection with `PersistKeysToFileSystem` pointing at the mounted volume path
- `src/BrunoVehicleHire.Api/appsettings.json` / `appsettings.Development.json` -- modify -- add the Postgres connection string
- `tests/BrunoVehicleHire.Integration.Tests/BrunoVehicleHire.Integration.Tests.csproj` -- modify -- add `Testcontainers.PostgreSql` 4.12.0 (already pinned in the architecture spine, unused until now)
- `tests/BrunoVehicleHire.Integration.Tests/VehicleMigrationTests.cs` -- new -- proves the migration against a real ephemeral Postgres container

## Tasks & Acceptance

**Execution:**
- [x] `docker-compose.yml` -- define a `postgres:18` service (or latest current patch), an init script creating the `btree_gist` extension, and a named volume mounted at the Data Protection key path -- gives every later story a real, disposable database
- [x] `src/BrunoVehicleHire.Domain/Vehicle.cs` -- create the schema-only entity matching `domain-model.md` exactly -- just enough shape for EF Core to map; Story 1.3 replaces public setters with `Vehicle.Create(...)`/private setters in this same file
- [x] `src/BrunoVehicleHire.Infrastructure/` -- add `Microsoft.EntityFrameworkCore` + `Microsoft.EntityFrameworkCore.Design` (10.x) and `Npgsql.EntityFrameworkCore.PostgreSQL` (10.0.3)
- [x] `src/BrunoVehicleHire.Infrastructure/Persistence/AppDbContext.cs` -- `DbSet<Vehicle> Vehicles`, `OnModelCreating` configuring a unique index on `RegistrationNumber`
- [x] `src/BrunoVehicleHire.Api/` -- add `ProjectReference` to `Infrastructure`; register `AppDbContext` in `Program.cs` via `AddDbContext` reading the connection string from configuration
- [x] `src/BrunoVehicleHire.Api/Program.cs` -- call `app.Services.GetRequiredService<AppDbContext>().Database.Migrate()` (or equivalent scoped-service pattern) before `app.Run()`; register ASP.NET Core Data Protection with `PersistKeysToFileSystem` targeting the docker-compose-mounted key volume path
- [x] `tests/BrunoVehicleHire.Integration.Tests/` -- add `Testcontainers.PostgreSql` 4.12.0
- [x] `tests/BrunoVehicleHire.Integration.Tests/VehicleMigrationTests.cs` -- write a failing integration test first: spin up a real Postgres container via Testcontainers, run the migration against it, assert the `Vehicles` table exists with the correct columns and a unique constraint on `RegistrationNumber` -- then make it pass (TDD, matching the I/O matrix's first two rows)
- [x] `dotnet ef migrations add InitialCreate` -- generate the actual migration files from the `Vehicle` entity + `AppDbContext` configuration

**Acceptance Criteria:**
- Given `docker-compose.yml`, when `docker-compose up -d` is run, then the Postgres container starts, is reachable, and has the `btree_gist` extension available
- Given a fresh Postgres instance, when the Api starts via `dotnet run`, then `Database.Migrate()` applies the initial migration, creating `Vehicles` with the exact schema from `domain-model.md`
- Given the Api has already migrated once, when it starts again, then `Database.Migrate()` is a no-op and startup is unaffected
- Given `VehicleMigrationTests`, when `dotnet test` is run, then it passes against a real Testcontainers-provisioned Postgres instance, proving the migration and unique constraint both work — not just asserted by manual inspection
- Given Data Protection is configured, when the Api starts, then keys are persisted to the mounted volume path, not the ephemeral in-container default

## Spec Change Log

- Implementation note: Postgres 18's official image changed its data-directory layout — mounting the volume at the old `.../postgresql/data` path makes the container refuse to start. Fixed by mounting at `/var/lib/postgresql` instead.
- Implementation note: `Npgsql.EntityFrameworkCore.PostgreSQL` 10.0.3 only floors `Microsoft.EntityFrameworkCore.Relational` at `>= 10.0.4`, so without an explicit pin the Api resolved a different EF Core Relational version than Infrastructure was built against, causing a runtime `FileNotFoundException` on `Database.Migrate()`. Fixed with an explicit `Microsoft.EntityFrameworkCore.Relational` 10.0.11 pin in Infrastructure.
- Implementation note: the Data Protection key volume is declared in `docker-compose.yml` for Epic 3/4 but not yet attached to any service, since the Api isn't containerized until a later epic — it runs via `dotnet run` this story, persisting keys to a local host folder at the same intended future path. Not a deviation from the AC's actual behavior, just a heads-up that the volume itself won't appear in `docker volume ls` until the Api is containerized.
- Review finding: the initial build carried a **high-severity** (CVSS 7.1) `NU1903` warning for `SSH.NET` 2025.1.0 (GHSA-q939-rpr3-3284, arbitrary file write via server-controlled SCP filenames), pulled in transitively by `Testcontainers.PostgreSql`. A patched version (2026.0.0+) exists, so an explicit `SSH.NET` 2026.0.0 `PackageReference` was added to `Integration.Tests` to override the vulnerable transitive version. Same pattern as Story 1.1's `Microsoft.OpenApi` fix.
- Review finding: `new PostgreSqlBuilder().WithImage(...)` triggered a `CS0618` obsolete-API warning (Testcontainers deprecated the parameterless constructor). Fixed to `new PostgreSqlBuilder("postgres:18")`. Rebuilt clean: 0 warnings, 0 errors, all 10 tests still passing.

## Design Notes

`Vehicle` is intentionally split across two stories: this story gives it only the property shape EF Core needs to generate a correct schema; Story 1.3 enriches the *same file* with `Vehicle.Create(...)`, private setters, and invariant checks. This is safe because the column shape (names/types) doesn't change between the two stories — only C# access modifiers and behavior do — so no second migration is needed. Do not create a separate "V2" entity or a new migration for Story 1.3's enrichment.

The `dotnet-ef` global tool was updated to `10.0.11` (from a stale `9.0.2`) before starting this story, to match the EF Core 10 packages being added — an older tool version generating migrations against newer EF Core packages is a common, avoidable source of migration bugs.

## Verification

**Commands:**
- `docker-compose up -d` -- expected: Postgres container reports healthy
- `dotnet build BrunoVehicleHire.sln` -- expected: 7/7 projects build, 0 errors
- `dotnet test BrunoVehicleHire.sln` -- expected: 8 tests total (the 6 from Story 1.1 + 1 new Testcontainers-based `VehicleMigrationTests` — note: the fitness-test count assumes Story 1.1's 3 rules are unaffected; if Story 1.1 landed a different count, adjust the expectation accordingly rather than treating a mismatch as a failure), all passing
- `docker-compose down -v && docker-compose up -d && dotnet run --project src/BrunoVehicleHire.Api` -- expected: clean startup log shows the migration applying against a genuinely fresh database

## Suggested Review Order

**Database schema (what this story actually creates)**

- Entry point: the schema-only shape EF Core maps to `domain-model.md`'s exact columns.
  [`Vehicle.cs:9`](../../src/BrunoVehicleHire.Domain/Vehicle.cs#L9)

- The unique index and column mappings the migration is generated from.
  [`AppDbContext.cs:1`](../../src/BrunoVehicleHire.Infrastructure/Persistence/AppDbContext.cs#L1)

- The generated migration itself — confirm it matches the entity/context above exactly.
  [`Migrations/`](../../src/BrunoVehicleHire.Infrastructure/Migrations/)

**Startup wiring (Program.cs composition root)**

- DbContext registration, `Database.Migrate()` on startup, and Data Protection key persistence — all three AD-1-permitted DI composition concerns in one place.
  [`Program.cs:1`](../../src/BrunoVehicleHire.Api/Program.cs#L1)

- The one new project reference this story needed (Api → Infrastructure), and why it doesn't violate AD-1.
  [`BrunoVehicleHire.Api.csproj`](../../src/BrunoVehicleHire.Api/BrunoVehicleHire.Api.csproj)

**Proof the migration actually works (not just inspected)**

- Real Postgres via Testcontainers proving schema, idempotent re-migrate, and the unique constraint all hold.
  [`VehicleMigrationTests.cs:16`](../../tests/BrunoVehicleHire.Integration.Tests/VehicleMigrationTests.cs#L16)

**Infrastructure & security**

- Postgres 18 service, the `btree_gist` extension init script, and the volume layout fix for Postgres 18's new data-directory convention.
  [`docker-compose.yml`](../../docker-compose.yml)

- `SSH.NET` security pin overriding a high-severity transitive vulnerability from Testcontainers.
  [`BrunoVehicleHire.Integration.Tests.csproj`](../../tests/BrunoVehicleHire.Integration.Tests/BrunoVehicleHire.Integration.Tests.csproj)

**Peripherals**

- Connection string and Data Protection key-path configuration.
  [`appsettings.json`](../../src/BrunoVehicleHire.Api/appsettings.json)

- Design-time factory letting `dotnet ef migrations add` scaffold without needing a live Postgres or running `Program.cs`.
  [`AppDbContextFactory.cs:1`](../../src/BrunoVehicleHire.Infrastructure/Persistence/AppDbContextFactory.cs#L1)
