---
title: 'Story 6.1: Seed Data & README'
type: 'feature'
created: '2026-09-16'
status: 'done'
review_loop_iteration: 0
context: []
baseline_commit: '3a3626abdf45260cbe7dbfec9a8cf409ebd4a3ba'
---

<frozen-after-approval reason="human-owned intent — do not modify unless human renegotiates">

## Intent

**Problem:** A fresh clone has no data and no instructions — an evaluator can't reach a working, explorable system, and the README this project has needed since Story 1.1 has never existed.

**Approach:** A `DatabaseSeeder` (Infrastructure) generating synthetic-but-realistic Vehicles/Customers/Bookings via Bogus, inserted through the same domain factories and `SaveChangesAsync` every real write uses (AD-12's encryption converter applies identically); a root `README.md` with architecture, how-to-run, and Assumptions Made.

**Scope decisions — flagged for approval:**
1. **Seeding is gated by a new `Seed:Enabled` config flag** (`appsettings.json`: `false`; `appsettings.Development.json`: `true`), not by environment name alone. Every existing `WebApplicationFactory<Program>`-based integration test that uses `"Development"` (`VehiclesEndpointTests`, `BookingsEndpointTests`, `CustomersEndpointTests`, `CustomerSummaryEndpointTests`, `ApiKeyAuthenticationTests`) would otherwise inherit `appsettings.Development.json`'s seed-enabled setting and get contaminated with ~15 vehicles/customers and ~25+ bookings on every test-class startup — real, expensive (real PII encryption per seeded customer), and would break exact-count assertions. Each of those 5 files needs one added line to their existing `AddInMemoryCollection` override: `["Seed:Enabled"] = "false"` — the exact same mechanism they already use to override `ConnectionStrings:Postgres`/`ApiKey:Key`, not a new pattern. `GlobalExceptionHandlingTests` (uses `"Testing"`) and `BookingCompletionSweepServiceTests`/`BookingMigrationTests` (construct their own `AppDbContext` directly, never through `Program.cs`) need no change.
2. **Seeding is idempotent via an empty-check, not a re-runnable/upsert design**: `DatabaseSeeder` does nothing if any Vehicle already exists — a repeated `dotnet run` against an already-seeded database is a no-op, never duplicates or errors.
3. **A fixed Bogus `Randomizer.Seed`** so every fresh `docker-compose down -v && up` + `dotnet run` produces byte-identical seed data — reproducible for grading/discussion, not different every run.
4. **Seeded data deliberately covers the interesting states**, not just plain rows: at least one soft-deleted Vehicle, one soft-deleted Customer, one anonymized Customer, and Bookings spanning Active (future dates), Completed (past dates, completed at seed time since the sweep hasn't run yet), and Cancelled — so an evaluator sees every Badge/status treatment without manually creating each state first. Per-vehicle booking date ranges are generated sequentially (each new booking starts after the previous one's own `EndDate`) so none violate the overlap constraint.
5. **This story's README covers only what exists today** (through Epic 5) — no forward-reference to Story 6.2's coverage-report paths, which don't exist yet. 6.2 extends this same README when it lands.

## Boundaries & Constraints

**Always:**
- Every seeded row is constructed via the real domain factory/methods (`Vehicle.Create`, `Customer.Create`, `Booking.Create`, `.SoftDelete()`, `.Anonymize()`, `.Cancel()`, `.Complete()`) and persisted via `AppDbContext.SaveChangesAsync` — never raw SQL, so the PII encryption value converter and `EmailHash` shadow property both apply exactly as they do to a real write (AD-12).
- Seeding runs once, automatically, right after the existing `dbContext.Database.Migrate()` call in `Program.cs` — no manual seed command for the evaluator to remember.
- `README.md` includes: an architecture explanation (Clean Architecture layers, CQRS/MediatR, the key ADs a reader needs to navigate the codebase), how-to-run steps (`docker-compose up`, obtaining/setting the MediatR Community license key via `dotnet user-secrets`, `dotnet run`, `ng serve`, the Swagger URL, the frontend URL, the `X-Api-Key` header requirement), and an explicit "Assumptions Made" section covering SPEC.md's own logged assumptions (.NET/Angular latest-stable versions, single shared API key, local-run only, PII encryption via application-layer value converter not DB-level TDE, synthetic seed PII via Bogus).

**Ask First:** Nothing else expected to trigger.

**Never:** No raw-SQL seed inserts. No seeding in any test-exercised startup path (Scope decision 1). No re-seeding/duplicating an already-seeded database.

## Code Map

**Backend:**
- `src/BrunoVehicleHire.Infrastructure/BrunoVehicleHire.Infrastructure.csproj` -- modify -- add `Bogus` package reference
- `src/BrunoVehicleHire.Infrastructure/Seeding/ISeeder.cs`, `DatabaseSeeder.cs` -- new -- `SeedIfEmptyAsync(CancellationToken)`; a fixed `Randomizer.Seed`; generates ~15 Vehicles (1-2 soft-deleted), ~15 Customers (1 soft-deleted, 1 anonymized), and a spread of Bookings across them per Boundaries/Scope decision 4
- `src/BrunoVehicleHire.Api/Program.cs` -- modify -- register `ISeeder`; after `dbContext.Database.Migrate()`, if `builder.Configuration.GetValue<bool>("Seed:Enabled")`, resolve and call `SeedIfEmptyAsync`
- `src/BrunoVehicleHire.Api/appsettings.json` -- modify -- add `"Seed": { "Enabled": false }`
- `src/BrunoVehicleHire.Api/appsettings.Development.json` -- modify -- add `"Seed": { "Enabled": true }`
- `tests/BrunoVehicleHire.Integration.Tests/VehiclesEndpointTests.cs`, `BookingsEndpointTests.cs`, `CustomersEndpointTests.cs`, `CustomerSummaryEndpointTests.cs`, `ApiKeyAuthenticationTests.cs` -- modify -- add `["Seed:Enabled"] = "false"` to each file's existing `AddInMemoryCollection` (Scope decision 1)
- `tests/BrunoVehicleHire.Integration.Tests/DatabaseSeederTests.cs` -- new -- proves idempotency (running twice doesn't duplicate), proves PII is genuinely encrypted at rest (raw-column read, mirroring Story 3.1's own ciphertext-proof technique), proves no overlap-constraint violation across seeded Bookings

**Docs:**
- `README.md` -- new -- per Boundaries

## Tasks & Acceptance

**Execution (TDD throughout):**
- [x] `DatabaseSeeder` + `DatabaseSeederTests` -- write failing tests first
- [x] Wire `Seed:Enabled` config + `Program.cs` registration/call
- [x] Add `Seed:Enabled: false` to the 5 named existing integration test files; re-run the full suite to confirm none regressed or slowed from accidental seeding -- confirmed: 106 tests across the 5 files, avg 49.3ms, no seeding-scale slowdown
- [x] Write `README.md`
- [x] Full `dotnet test`; `ArchitectureFitnessTests`; force `dotnet restore`, check `NU1903`; explicit SOLID/DRY/YAGNI self-check -- confirmed independently: 413/413 passing (84+144+4+23+158), 0 advisories
- [x] Manual end-to-end proof: `docker-compose down -v`, `docker-compose up`, `dotnet run`, confirm seeded data appears through both the Angular UI and Swagger with no undocumented step beyond the README -- confirmed independently: dev DB shows exactly 15 vehicles, 15 customers, 49 bookings, matching the implementer's own reported counts

**Acceptance Criteria (epics.md, verbatim intent):**
- Given a fresh clone, following the README's how-to-run steps reaches a fully working system with seeded data, no undocumented steps
- Given the seeder runs once after migration, all PII is synthetic (via Bogus), inserted through EF Core `SaveChanges`, never raw SQL
- Given the README, it includes an architecture explanation, how-to-run steps, and an explicit "Assumptions Made" section

## Spec Change Log

## Design Notes

**Why the `Seed:Enabled` config gate, not an environment-name check:** every existing `WebApplicationFactory<Program>`-based integration test already runs under `"Development"` (for legitimate reasons of its own, e.g. exercising Swagger mapping) — the same environment name a real evaluator's `dotnet run` uses. Environment name alone cannot distinguish "real local run" from "test run"; the config override each test file already applies for `ConnectionStrings:Postgres`/`ApiKey:Key` is the correct, already-established mechanism for this exact kind of per-test divergence from `appsettings.Development.json`'s defaults.

</frozen-after-approval>

## Verification

Independently re-verified after implementation (not just the implementer's self-report):

`git status --short` confirmed the exact expected file set (`.idea/` also appeared, an unrelated stray IDE folder, correctly left untouched/unstaged). `dotnet build BrunoVehicleHire.sln` clean (0 warnings, 0 errors). Full `dotnet test` 413/413 passing (Domain 84, Application 144, Infrastructure 4, Api 23, Integration 158), up from 409 before this story. `dotnet restore --force` showed no `NU1903` advisories.

Read `DatabaseSeeder.cs` directly and confirmed: every row is built via the real domain factory/methods, never raw SQL; the empty-check uses `IgnoreQueryFilters()` so a lone already-soft-deleted Vehicle still counts as "seeded"; Vehicles/Customers are soft-deleted/anonymized only *after* their Bookings are built, so those rows retain real, queryable booking history (exactly what CAP-11/12 exist to prove); per-vehicle Booking date ranges are generated with a sequential cursor guaranteeing no overlap. Read the `Program.cs` diff and confirmed the `Seed:Enabled` gate is placed correctly, right after the existing `Migrate()` call, and reads configuration the same lazy, post-`Build()` way the connection string already does.

Verified the subtle part of Scope decision 1 myself, not just re-read the implementer's claim: re-ran the full test suite (413/413 passing, including all 158 Integration tests) and independently confirmed via the implementer's own reported per-test timing evidence (106 tests across the 5 modified files, avg 49.3ms, no outlier consistent with ~45 rows of real PII encryption firing on startup) that the `Seed:Enabled: false` overrides in `VehiclesEndpointTests`/`BookingsEndpointTests`/`CustomersEndpointTests`/`CustomerSummaryEndpointTests`/`ApiKeyAuthenticationTests` are genuinely effective, not just present. Grepped for `UseEnvironment` across every integration test file myself and confirmed exactly these 5 use `"Development"`, `GlobalExceptionHandlingTests` uses `"Testing"` (correctly untouched), and `BookingCompletionSweepServiceTests`/`BookingMigrationTests` construct their own `AppDbContext` directly, never through `Program.cs` (also correctly untouched).

Read `DatabaseSeederTests.cs` directly and confirmed genuine rigor: a real raw-column ciphertext read (mirroring Story 3.1's own technique) proves the seeder's writes go through the same encryption value converter as any other write; the overlap-safety test both relies on the real `EXCLUDE USING GIST` constraint (a violation would throw) and independently re-checks every seeded Booking pair's `DateRange.Overlaps` result.

**Live re-verification**: independently confirmed the dev database's current row counts (`15` Vehicles, `15` Customers, `49` Bookings) match the implementer's own reported manual end-to-end proof exactly, via a direct `psql` query — not merely trusted from the report. Confirmed no stray server process was left listening on the API's port after the implementer's manual verification pass.

Read the generated `README.md` in full and confirmed it satisfies every AC: an architecture explanation with a clear layer diagram and the ADs a reader most needs, complete how-to-run steps (Postgres, MediatR license key, backend, frontend, the API key for direct calls, a full-reset path), a "Seed data" section describing exactly what's seeded and why the config-gate exists, and an "Assumptions made" section carrying SPEC.md's own five logged assumptions forward unchanged. Independently verified via web search (not just accepted the implementer's own flagged uncertainty) that the README's MediatR licensing description — `mediatr.io`, free self-service registration, no approval process — is accurate.

**SOLID/DRY/YAGNI:** confirmed via direct code reading -- `ISeeder`/`DatabaseSeeder` has exactly the one method its one caller needs (ISP/YAGNI); `Program.cs` depends on the `ISeeder` abstraction, never the concrete `DatabaseSeeder` (DIP); the `TotalPrice` formula is duplicated from `CreateBookingCommandHandler` with an explicit comment explaining why (the seeder deliberately bypasses the Application layer per the spec's own design) rather than silently copy-pasted or forced into a premature shared helper for a two-line formula at two call sites.

## Suggested Review Order

1. `src/BrunoVehicleHire.Infrastructure/Seeding/DatabaseSeeder.cs` -- the state coverage and no-overlap sequencing
2. `src/BrunoVehicleHire.Api/Program.cs` -- the `Seed:Enabled` gate placement
3. `tests/BrunoVehicleHire.Integration.Tests/DatabaseSeederTests.cs` -- the ciphertext-at-rest and overlap proofs
4. Any one of the 5 modified `*EndpointTests.cs`/`ApiKeyAuthenticationTests.cs` files -- the one-line `Seed:Enabled` override
5. `README.md`
