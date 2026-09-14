---
title: 'Story 4.2: Booking Overlap Prevention (Application + Database)'
type: 'feature'
created: '2026-09-14'
status: 'done'
review_loop_iteration: 0
context: []
baseline_commit: '76e4d685b807cd667aeb86311aef78d24eb52f00'
---

<frozen-after-approval reason="human-owned intent — do not modify unless human renegotiates">

## Intent

**Problem:** Story 4.1's `CreateBookingCommandHandler` never checks for overlapping bookings — two staff members could double-book the same vehicle for overlapping dates, including under a genuine race condition where both requests read "no conflict" before either commits.

**Approach:** Add a pure, unit-tested `DateRange` value object (domain-model.md's own suggested DDD exercise) with an `Overlaps` method; use it in `CreateBookingCommandHandler` as the fast-path application-level check (AD-7's layer 1); add this story's own migration for a Postgres `EXCLUDE USING GIST` constraint (AD-7's layer 2, the race-condition backstop), translating its rare violation back into the same 409 shape the application check produces.

**Scope decisions — flagged for approval:**
1. **`DateRange` is a standalone value object, not woven into `Booking`'s own StartDate/EndDate properties.** `Booking.Create`'s signature (Story 3.3, already shipped) stays completely unchanged — `DateRange` is constructed ad hoc from a booking's own dates wherever the overlap check runs, avoiding an EF Core remapping of already-migrated columns for zero behavioral gain.
2. **The application-level check is a genuinely-exercised in-memory comparison, not just a decorative unit-tested method:** `CreateBookingCommandHandler` fetches a vehicle's existing non-Cancelled bookings (typically a handful of rows) and calls the real `DateRange.Overlaps` on each in plain C# — not a SQL-translated LINQ predicate. This is what lets `DateRange.Overlaps` be "a pure, unit-tested domain method with zero infrastructure dependencies" (the AC's own wording) that production code actually calls, not a demonstration left uncalled.
3. **The database-level backstop's 409 is produced by catching the specific Postgres exclusion-violation (`SqlState "23P01"`) and re-deriving the exact same conflict message**, rather than a generic "conflict occurred" 500-avoidant fallback — re-running the same overlap lookup after the `DbUpdateException` gives byte-identical error shape to the application-level path, satisfying the AC's "mapped to the same 409 error."
4. **No `excludingBookingId` parameter anywhere in this check** — Booking has no Edit feature in this project's scope (established in Story 4.1's context), so there is never a "check overlap excluding my own row" scenario the way Vehicle/Customer's uniqueness checks need for Edit.

## Boundaries & Constraints

**Always:**
- `DateRange.Overlaps` uses half-open interval semantics: two ranges overlap iff `Start < other.End && other.Start < End` — touching endpoints (one's End equals the other's Start) are NOT an overlap (AD-7, same-day turnover).
- The GIST exclusion constraint's `WHERE` clause is `status != 'Cancelled'` (not `= 'Active'`) — a Completed booking's historical date range still blocks a new overlapping booking for that vehicle, exactly mirroring AD-7's own clause.
- Both layers produce the identical 409 message shape: `"This vehicle is already booked {conflictStart} – {conflictEnd}"` (e.g. "This vehicle is already booked 2 Sep – 4 Sep"), using the *conflicting* booking's own dates, not the new request's dates.
- This story's own migration adds `CREATE EXTENSION IF NOT EXISTS btree_gist;` and the `EXCLUDE USING GIST` constraint via raw SQL (`migrationBuilder.Sql(...)`) — EF Core's fluent API has no representation for this constraint type, so `dotnet ef migrations add` produces an empty scaffold to hand-edit, exactly as it did for nothing else in this codebase yet (first raw-SQL migration).

**Ask First:** Nothing else expected to trigger.

**Never:** No `excludingBookingId`/Edit-overlap-exclusion logic (Scope decision 4). No change to `Booking.Create`'s signature or Story 3.3's existing tests.

## I/O & Edge-Case Matrix

| Scenario | Input / State | Expected Output / Behavior | Error Handling |
|----------|--------------|---------------------------|----------------|
| New booking overlaps an existing Active booking (2 Sep–4 Sep) | Same vehicle, overlapping range | `409`, detail "This vehicle is already booked 2 Sep – 4 Sep" | Inline error under the date fields |
| New booking's StartDate equals existing booking's EndDate (same-day turnover) | Same vehicle, touching endpoints | `201` — succeeds | N/A |
| New booking overlaps an existing Completed booking | Same vehicle, overlapping range, existing Status=Completed | `409`, same message shape | N/A |
| New booking overlaps an existing Cancelled booking | Same vehicle, overlapping range, existing Status=Cancelled | `201` — succeeds, Cancelled bookings never block | N/A |
| Two concurrent requests both pass the app-level check for the same overlapping range | Simulated race via Testcontainers | Exactly one `201`; the other `409` via the DB constraint, same message shape | N/A |

## Code Map

**Domain:**
- `src/BrunoVehicleHire.Domain/DateRange.cs` -- new -- `readonly record struct DateRange(DateOnly Start, DateOnly End)` with `Overlaps(DateRange other)` per Boundaries
- `tests/BrunoVehicleHire.Domain.Tests/DateRangeTests.cs` -- new -- full overlap/non-overlap/touching-endpoint/identical-range matrix, written first, zero infrastructure dependencies

**Application:**
- `src/BrunoVehicleHire.Application/Bookings/IBookingRepository.cs` -- modify -- add `GetNonCancelledForVehicleAsync(vehicleId, ct) -> IReadOnlyList<Booking>`
- `src/BrunoVehicleHire.Infrastructure/Repositories/BookingRepository.cs` -- modify -- implement it (`WHERE VehicleId = @id AND Status != Cancelled`)
- `src/BrunoVehicleHire.Application/Bookings/Commands/CreateBookingCommandHandler.cs` -- modify -- after the vehicle/customer checks (Story 4.1, unchanged), fetch non-Cancelled bookings for the vehicle, find the first `DateRange.Overlaps` conflict and throw `DomainRuleViolationException` with the exact message (Scope decision 2); wrap `SaveChangesAsync` in a try/catch for `DbUpdateException` whose `InnerException` is a `Npgsql.PostgresException` with `SqlState == "23P01"` -- on that specific match, re-run the same overlap lookup and throw the identical exception (Scope decision 3); any other `DbUpdateException` rethrows unmodified

**Infrastructure:**
- `src/BrunoVehicleHire.Infrastructure/Migrations/{timestamp}_AddBookingOverlapExclusionConstraint.cs` -- new -- raw SQL per Boundaries; `Down` drops the constraint (leaves the extension)

**Tests:**
- `tests/BrunoVehicleHire.Application.Tests/Bookings/CreateBookingCommandHandlerTests.cs` -- modify -- add the application-level overlap/touching-endpoint/Cancelled-doesn't-block cases
- `tests/BrunoVehicleHire.Integration.Tests/BookingsEndpointTests.cs` -- modify -- the overlap/touching-endpoint/Completed-blocks/Cancelled-doesn't-block I/O-matrix rows through the real API
- `tests/BrunoVehicleHire.Integration.Tests/BookingMigrationTests.cs` -- modify -- assert the exclusion constraint exists (query `pg_constraint`/`pg_indexes` for its contype `x`) and that a real overlapping direct-DB insert is rejected
- A new or existing integration test -- the concurrency proof: two overlapping inserts issued through two separate, concurrently-committing connections/transactions (bypassing the application-level pre-check, e.g. via direct repository/DbContext calls racing each other) against a real Testcontainers Postgres -- exactly one succeeds, the other throws with `SqlState "23P01"`

## Tasks & Acceptance

**Execution (TDD throughout):**
- [x] `DateRange` + `DateRangeTests` -- write failing tests first, then implement -- 7 tests (identical/partial/contained/separate/both touching directions/adjacent)
- [x] `IBookingRepository`/`BookingRepository`'s `GetNonCancelledForVehicleAsync` -- write failing tests first
- [x] `CreateBookingCommandHandler`'s app-level overlap check + `DbUpdateException` translation -- write failing tests first -- 4 new handler unit tests
- [x] The `AddBookingOverlapExclusionConstraint` migration -- hand-edit the empty scaffold; verify against a real Testcontainers Postgres -- confirmed independently against a live container (see Verification)
- [x] The concurrency proof -- two racing inserts, only one succeeds -- confirmed independently across 3 separate fresh-container runs, not just once
- [x] Full `dotnet test`; re-run `ArchitectureFitnessTests`; force `dotnet restore`, check `NU1903`; explicit SOLID/DRY/YAGNI self-check -- confirmed independently: 355/355 passing, 0 advisories

**Acceptance Criteria (epics.md, verbatim intent):**
- Given an existing Active booking for a vehicle from 2 Sep to 4 Sep, creating another overlapping booking for the same vehicle returns `409` with "This vehicle is already booked 2 Sep – 4 Sep"
- Given one booking's EndDate equals another's StartDate for the same vehicle, the second booking succeeds (half-open interval)
- Given two concurrent requests both attempting to book the same overlapping range, only one succeeds; the other fails via the database constraint, mapped to the same `409`

## Spec Change Log

- **`src/BrunoVehicleHire.Application/BrunoVehicleHire.Application.csproj`** (not in this spec's Code Map) needed direct `PackageReference`s to `Microsoft.EntityFrameworkCore` (10.0.11, for `DbUpdateException`) and `Npgsql` (10.0.3, for `PostgresException`/`SqlState`), pinned to the exact versions `BrunoVehicleHire.Infrastructure` already resolves. A necessary consequence of the Code Map's own explicit assignment of the `DbUpdateException`/`PostgresException` catch to `CreateBookingCommandHandler` (Application layer) rather than to a repository/Infrastructure method -- `ArchitectureFitnessTests` still passes since Application still has no project reference to Infrastructure/Api, only these two NuGet packages, which the fitness tests never examine. Flagged here since it's a small, deliberate loosening of "Application knows nothing about persistence" for the sake of Scope decision 3's byte-identical 409 shape.
- **Spec-4-2's `Cancelled-doesn't-block` I/O-matrix row could not be exercised inside `CreateBookingCommandHandlerTests.cs` (Application unit tests) using a real `Cancelled` `Booking`**: `Booking` (Story 3.3) has no public `Cancel()`/`Complete()` transition yet -- Status is always `Active` from `Booking.Create`. The unit test instead documents/proves the handler's side of `IBookingRepository.GetNonCancelledForVehicleAsync`'s own contract (fed an empty result, as the repository would genuinely return, the handler raises no conflict). The actual Completed-blocks/Cancelled-doesn't-block behavior is proven for real in `BookingsEndpointTests.cs` and `BookingMigrationTests.cs`, both of which flip the `"Status"` column directly via raw SQL after a normal seed -- exactly as a future Cancel/Complete feature eventually would, and exactly what the Code Map's own row assignment for those two files already anticipated.

## Design Notes

**Why the app-level check reads all non-Cancelled bookings for one vehicle into memory rather than a SQL-translated overlap predicate:** a vehicle realistically has a small number of bookings at any time (this assessment's scale), and doing the comparison in plain C# via the real `DateRange.Overlaps` method means that method is genuinely exercised by production code, not just proven correct in isolation and then reimplemented separately in SQL for the actual enforcement path — one implementation of the overlap rule, not two.

</frozen-after-approval>

## Verification

Independently re-verified after implementation (not just the implementer's self-report):

`git status --short` confirmed the exact expected file set (new: `DateRange.cs`, `DateRangeTests.cs`, the hand-edited migration pair; modified: `IBookingRepository.cs`, `BookingRepository.cs`, `CreateBookingCommandHandler.cs`, `BrunoVehicleHire.Application.csproj`, `CreateBookingCommandHandlerTests.cs`, `BookingMigrationTests.cs`, `BookingsEndpointTests.cs`). `dotnet build BrunoVehicleHire.sln` clean (0 warnings, 0 errors). Full `dotnet test` across all five projects: 355/355 passing (Domain 74, up from 67; Application 124, up from 120; Infrastructure 4, unchanged; Api 23, unchanged; Integration 130, up from 121). `dotnet restore --force` showed no `NU1903` or other audit advisories. `ArchitectureFitnessTests` re-run in isolation, still 3/3 passing -- confirms the new `Microsoft.EntityFrameworkCore`/`Npgsql` package references in Application did not introduce a forbidden dependency on Infrastructure/Api.

Read `DateRange.cs`, `CreateBookingCommandHandler.cs`, `BookingRepository.cs`, and the hand-edited migration directly and confirmed: `Overlaps` uses the exact half-open `Start < other.End && other.Start < End` predicate from Boundaries; `EnsureNoOverlapAsync` is called both as the fast-path check and, verbatim, again inside the `DbUpdateException`/`SqlState "23P01"` catch, so both layers produce byte-identical exception objects (Scope decision 3) -- confirmed via the exact string `"This vehicle is already booked {start} – {end}"` appearing in exactly one place in the handler; no `excludingBookingId` parameter exists anywhere (Scope decision 4); `Booking.Create`'s signature is untouched and Story 3.3's existing `BookingTests.cs` still passes unmodified. The migration's raw SQL quotes `"Bookings"`/`"VehicleId"`/`"StartDate"`/`"EndDate"`/`"Status"` with the exact PascalCase casing EF Core already used in `AddBookingsTable`'s Designer snapshot; `Down` drops only the constraint, leaving `btree_gist` installed, exactly as the Boundaries specify.

The concurrency proof (`ConcurrentOverlappingInserts_ForSameVehicle_ExactlyOneSucceeds_TheOtherThrowsExclusionViolation` in `BookingMigrationTests.cs`) was run three times in isolation, each against a freshly-created Testcontainers Postgres container (not the same container reused, which could hide a race by accident): two separate `AppDbContext`/connection instances insert genuinely overlapping ranges for the same vehicle directly (bypassing `CreateBookingCommandHandler`'s application-level pre-check entirely, per the Code Map's own instruction), dispatched together via `Task.WhenAll`. All three runs: exactly one `SaveChangesAsync` succeeded, the other threw `DbUpdateException` whose `InnerException` was a real `Npgsql.PostgresException` with `SqlState == "23P01"` -- the database-level backstop firing under a genuine race, not merely the (bypassed) application-level check.

**SOLID/DRY/YAGNI:** confirmed via direct code reading -- `DateRange` has exactly one responsibility (the overlap predicate) and no infrastructure dependency (SRP); `EnsureNoOverlapAsync` is called from both the fast path and the database-backstop catch so the conflict-message construction exists in exactly one place, never duplicated (DRY), matching Scope decision 3's own reasoning; no `excludingBookingId` parameter, no `Cancel`/`Complete` methods on `Booking`, and no SQL-translated overlap predicate were added beyond what this story's AC actually needs (YAGNI) -- `DateRange.Overlaps` is called for real by production code (Scope decision 2), not left as a proven-but-unused method.

## Suggested Review Order

1. `src/BrunoVehicleHire.Domain/DateRange.cs` + `tests/BrunoVehicleHire.Domain.Tests/DateRangeTests.cs` -- the pure overlap predicate and its full matrix
2. `src/BrunoVehicleHire.Application/Bookings/Commands/CreateBookingCommandHandler.cs` -- `EnsureNoOverlapAsync` called from both the fast path and the `DbUpdateException` catch
3. `src/BrunoVehicleHire.Infrastructure/Migrations/20260914154054_AddBookingOverlapExclusionConstraint.cs` -- the hand-written raw SQL, the first in this codebase
4. `tests/BrunoVehicleHire.Integration.Tests/BookingMigrationTests.cs` -- the concurrency proof (`ConcurrentOverlappingInserts_...`) and the Cancelled/Completed raw-SQL status flips
5. `tests/BrunoVehicleHire.Integration.Tests/BookingsEndpointTests.cs` -- the overlap/touching-endpoint/Completed/Cancelled I/O-matrix rows through the real API
6. `src/BrunoVehicleHire.Application/BrunoVehicleHire.Application.csproj` -- the new `Microsoft.EntityFrameworkCore`/`Npgsql` package references (Spec Change Log)
