---
title: 'Bug fix: concurrent-request races produce ugly failures instead of graceful ones'
type: 'bugfix'
created: '2026-09-16'
status: 'done'
review_loop_iteration: 0
context: []
baseline_commit: 'e2cc6c66864afec6cbb4be3b3c6a41ae6c3d959d'
---

<frozen-after-approval reason="human-owned intent — do not modify unless human renegotiates">

## Intent

**Problem:** A QA pass found two related concurrency gaps, both already solved elsewhere in this codebase but never applied here. (1) `CreateVehicleCommandHandler`/`CreateCustomerCommandHandler` do a check-then-insert (`ExistsByRegistrationNumberAsync`/`ExistsByEmailAsync`, then later `SaveChangesAsync`) with no handling for the race where two near-simultaneous requests both pass the check — the loser crashes with an unhandled 500 instead of the same "already in use" 409 the sequential case already produces, even though a real unique DB index exists and would otherwise silently allow the crash. (2) `RestoreVehicleCommandHandler`/`CancelBookingCommandHandler` have no optimistic-concurrency guard, so two near-simultaneous requests against the same row (e.g. a double-click) both read the pre-mutation state, both pass their domain method's guard, and both succeed — silently double-processing instead of the second one hitting the exact "Already active."/"already cancelled" `DomainRuleViolationException` each domain method already throws for the sequential-attempt case.

**Approach:** For (1), mirror `CreateBookingCommandHandler`'s own existing defense (AD-7 layer 2): catch the `DbUpdateException`/`PostgresException` unique-violation around `SaveChangesAsync` and re-run the identical pre-check, which will now correctly find the just-committed row and throw the same `DomainRuleViolationException` the fast-path already throws. For (2), add an EF Core optimistic-concurrency token via Postgres's built-in `xmin` system column (`.UseXminAsConcurrencyToken()` — no schema migration needed) to `Vehicle` and `Booking`, and on `DbUpdateConcurrencyException`, re-fetch the row and re-invoke the same domain method, which will now see the true post-mutation state and throw its own correct exception.

## Boundaries & Constraints

**Always:**
- The unique-violation catch in `CreateVehicleCommandHandler`/`CreateCustomerCommandHandler` mirrors `CreateBookingCommandHandler`'s exact shape: a named `private const string UniqueViolationSqlState = "23505";` (Postgres's real unique-violation SQLSTATE, distinct from `CreateBookingCommandHandler`'s own `"23P01"` exclusion-violation constant used for a different constraint type), a `catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: UniqueViolationSqlState })`, then re-running the exact pre-check call and letting it throw (never swallowing the exception or synthesizing a new message).
- `Vehicle` and `Booking` (not `Customer` — no double-click race was found or is being fixed against a Customer mutation in this spec) gain `.UseXminAsConcurrencyToken()` in `AppDbContext.OnModelCreating`. This requires **no migration**: `xmin` is a Postgres system column already present on every row, not a new mapped column.
- `RestoreVehicleCommandHandler`/`CancelBookingCommandHandler` catch `DbUpdateConcurrencyException` around their `SaveChangesAsync`, re-fetch the entity fresh, and call the exact same domain method a second time (`vehicle.Restore()`/`booking.Cancel()`) — letting IT decide and throw the correct `DomainRuleViolationException` for whatever the true current state now is. Never hand-write a duplicate message.
- Every other command handler and domain method is untouched — this spec fixes exactly these four handlers' race behavior, nothing else.

**Ask First:** Nothing else expected to trigger.

**Never:** No new concurrency tokens on `Customer` or any other entity beyond `Vehicle`/`Booking`. No retry-and-succeed logic (a concurrency conflict always surfaces as the domain's own rejection, never a silent second attempt at the original mutation). No change to `EditVehicleCommandHandler`/`UpdateCustomerCommandHandler`'s own duplicate-check races — out of scope for this spec (a QA finding for a future pass, not confirmed here).

## I/O & Edge-Case Matrix

| Scenario | Input / State | Expected Output / Behavior | Error Handling |
|----------|--------------|---------------------------|----------------|
| Two concurrent Create-vehicle requests, same registration number | Both pass the pre-check | One `201 Created`; the other `409` "This registration number is already in use." | Never a 500 |
| Two concurrent Create-customer requests, same email | Both pass the pre-check | One `201 Created`; the other `409` "This email address is already in use." | Never a 500 |
| Two concurrent Restore requests on the same soft-deleted vehicle | Both read `IsDeleted = true` | One `204`; the other `409` "Already active." | Never a silent double-success |
| Two concurrent Cancel requests on the same Active, eligible booking | Both read `Status = Active` | One `204`; the other `409` "Cannot cancel — booking already cancelled." | Never a silent double-success |

</frozen-after-approval>

## Code Map

- `src/BrunoVehicleHire.Application/Vehicles/Commands/CreateVehicleCommandHandler.cs` -- modify -- wrap `SaveChangesAsync` in a try/catch per Boundaries, re-running `ExistsByRegistrationNumberAsync` on a caught unique violation
- `src/BrunoVehicleHire.Application/Customers/Commands/CreateCustomerCommandHandler.cs` -- modify -- same pattern, re-running `ExistsByEmailAsync`
- `src/BrunoVehicleHire.Infrastructure/Persistence/AppDbContext.cs` -- modify -- add `entity.UseXminAsConcurrencyToken();` inside the `Vehicle` (`:41-75`) and `Booking` (`:119-...`) `modelBuilder.Entity<>` blocks
- `src/BrunoVehicleHire.Application/Vehicles/Commands/RestoreVehicleCommandHandler.cs` -- modify -- wrap `SaveChangesAsync` in a try/catch for `DbUpdateConcurrencyException`, re-fetch via `GetByIdIncludingSoftDeletedAsync` and re-call `.Restore()` on catch
- `src/BrunoVehicleHire.Application/Bookings/Commands/CancelBookingCommandHandler.cs` -- modify -- same pattern, re-fetch via `GetByIdAsync` and re-call `.Cancel()` on catch
- `tests/BrunoVehicleHire.Integration.Tests/VehiclesEndpointTests.cs`, `CustomersEndpointTests.cs` -- modify -- add a genuine concurrent-request test per Create handler (fire two real HTTP requests via `Task.WhenAll`, assert one 201 + one 409, never a 500) -- mirrors `CreateBookingCommandHandler`'s own existing concurrent-overlap test's technique if one exists, otherwise a new Testcontainers-backed test
- `tests/BrunoVehicleHire.Integration.Tests/VehiclesEndpointTests.cs`, `BookingsEndpointTests.cs` -- modify -- add a genuine concurrent-request test per Restore/Cancel handler (two real HTTP requests via `Task.WhenAll`, assert one success + one 409)

## Tasks & Acceptance

**Execution (TDD throughout):**
- [x] Write a failing concurrent-request integration test for Create Vehicle, then add the unique-violation catch
- [x] Write a failing concurrent-request integration test for Create Customer, then add the unique-violation catch
- [x] Add an xmin optimistic-concurrency token to `Vehicle`/`Booking`
- [x] Write a failing concurrent-request integration test for Restore Vehicle, then add the `DbUpdateConcurrencyException` catch-and-retry
- [x] Write a failing concurrent-request integration test for Cancel Booking, then add the same
- [x] Full `dotnet test`, confirm nothing regressed; re-run `ArchitectureFitnessTests`

**Acceptance Criteria:**
- [x] Given two concurrent Create requests with the same unique field value, when both are sent, then exactly one succeeds and the other receives the standard 409 for that rule — never a 500
- [x] Given two concurrent Restore/Cancel requests against the same row, when both are sent, then exactly one succeeds and the other receives the standard domain-rule 409 for that action — never a silent double-success

## Spec Change Log

- **Deviation from Design Notes' literal `.UseXminAsConcurrencyToken()` API:** that convenience method was removed from `Npgsql.EntityFrameworkCore.PostgreSQL` starting with 10.x (present only up to 8.x) — this project is on a version where it no longer exists. The implementer confirmed via the installed package's own docs and used the replacement, `entity.Property<uint>("xmin").IsRowVersion()`, which the provider's `NpgsqlPostgresModelFinalizingConvention.ProcessRowVersionProperty` convention still maps directly onto the real `xmin` system column — identical runtime effect, still genuinely no schema change.
- **Migration requirement discovered during verification, not anticipated at planning time:** even though `xmin` needs no physical DDL, declaring it as an EF Core concurrency token still changes EF Core's own model snapshot, which this project's EF Core version flags via `PendingModelChangesWarning`-as-error at startup. A migration (`20260916123334_AddXminConcurrencyTokens`) was added purely to reconcile the snapshot; its `Up()`/`Down()` are genuine no-ops (the scaffolder's default attempt to physically add/drop a column literally named `xmin` was deleted, since Postgres rejects that as a reserved system-column name). Confirmed via `dotnet ef migrations has-pending-model-changes` reporting clean, and via a real `dotnet run` starting without the crash this spec's own live verification first caught.

## Design Notes

**Why `xmin` rather than a new `RowVersion byte[]` column:** Postgres already maintains `xmin` (the row's current transaction id) on every row with zero schema changes required — Npgsql's EF Core provider maps it directly as a concurrency token via `.UseXminAsConcurrencyToken()`. A dedicated `RowVersion` column would need a migration and manual increment logic for no additional benefit in this case.

**Why re-invoke the same domain method on a concurrency conflict rather than hand-writing a duplicate message:** `Vehicle.Restore()`/`Booking.Cancel()` already contain the exact correct guard logic and message for "this is no longer in the state you thought it was" — re-fetching and re-calling them is the only way to guarantee the concurrency-conflict path can never drift out of sync with the sequential-attempt path's own wording.

## Verification

Independently reproduced (not just re-reading the implementer's report), per this project's standing verification rule — including independently catching and getting the implementer to fix a real startup-crashing bug (the `PendingModelChangesWarning`, see Spec Change Log) that its own `dotnet test`-based verification had not surfaced.

**Commands:**
- `dotnet build` + `dotnet test BrunoVehicleHire.sln` (own runs) — **426/426 passing** (Domain 85, Application 146, Infrastructure 4, Api 23, Integration 168), including `ArchitectureFitnessTests` and all four new genuine concurrent-request tests. One transient, unrelated failure (`CustomerLoggingPiiTests`) on an earlier full-suite run was confirmed flaky (passed in isolation, passed on a clean re-run) — root-caused to heavy concurrent Testcontainers/Docker load from three parallel implementation agents, not a regression from this spec.
- `dotnet restore --force` — no `NU1903`.

**Code read (all diffed files):** `CreateVehicleCommandHandler.cs`/`CreateCustomerCommandHandler.cs` (confirmed the unique-violation catch mirrors `CreateBookingCommandHandler`'s exact AD-7 shape, with a correctly distinct SQLSTATE constant `"23505"` vs. the booking path's own `"23P01"`), `RestoreVehicleCommandHandler.cs`/`CancelBookingCommandHandler.cs` (confirmed the `DbUpdateConcurrencyException` catch re-fetches and re-invokes the exact same domain method, never hand-writing a duplicate message), `AppDbContext.cs` (confirmed the `xmin` mapping on both `Vehicle` and `Booking`, correctly scoped to only those two entities per the Boundaries), the no-op migration (confirmed both `Up()`/`Down()` are genuinely empty with an accurate doc comment).
- **Live verification, independently reproduced:** rebuilt the full solution and started the real API (`dotnet run`) against the real dev Postgres — confirmed it starts cleanly with no `PendingModelChangesWarning` crash (the exact failure I caught and reported back to the implementing agent mid-task). Read the four new `Task.WhenAll`-based concurrent-request integration tests in full and confirmed they exercise genuine HTTP-level concurrency against a real Testcontainers Postgres, not a simulated/sequential race.
- **Cross-spec file overlap confirmed resolved:** `CancelBookingCommandHandler.cs` (this spec) and `Booking.cs`'s `Cancel()` method (spec-booking-form-error-handling-fixes.md) are different files calling into each other as a black box — confirmed this spec's re-invoke-and-let-it-throw pattern remains fully correct regardless of which eligibility rule `Cancel()` enforces internally, and `tests/BrunoVehicleHire.Integration.Tests/BookingsEndpointTests.cs` (touched by both specs) has both sets of changes present and passing together.
- **Process note:** the implementing agent reported killing a "stray leftover" `BrunoVehicleHire.Api.exe` process that was locking build output before it could build — this was already-orphaned (not an active user session, unlike an earlier incident this session with a live Rider debugger), but is still logged here per this project's standing practice of flagging any subagent action that touches the user's environment beyond the git working tree.

## Suggested Review Order

1. `src/BrunoVehicleHire.Application/Vehicles/Commands/CreateVehicleCommandHandler.cs` and `.../Customers/Commands/CreateCustomerCommandHandler.cs` — the unique-violation race fix
2. `src/BrunoVehicleHire.Application/Vehicles/Commands/RestoreVehicleCommandHandler.cs` and `.../Bookings/Commands/CancelBookingCommandHandler.cs` — the optimistic-concurrency race fix
3. `src/BrunoVehicleHire.Infrastructure/Persistence/AppDbContext.cs` — the `xmin` concurrency-token mapping
4. `src/BrunoVehicleHire.Infrastructure/Migrations/20260916123334_AddXminConcurrencyTokens.cs` — the no-op migration and why it's needed
5. The four new `Task.WhenAll`-based concurrent-request tests across `VehiclesEndpointTests.cs`, `CustomersEndpointTests.cs`, and `BookingsEndpointTests.cs`
