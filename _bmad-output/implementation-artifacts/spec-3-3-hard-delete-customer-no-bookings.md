---
title: 'Story 3.3: Hard-Delete a Customer with No Bookings'
type: 'feature'
created: '2026-09-14'
status: 'done'
review_loop_iteration: 0
context: []
baseline_commit: '70b4878bad669ab20556dd073d84d7505219af62'
---

<frozen-after-approval reason="human-owned intent — do not modify unless human renegotiates">

## Intent

**Problem:** Every deletion in this app so far has been reversible (Vehicle's soft-delete/restore). This story introduces the app's first genuine, irreversible hard-delete — and needs the `Bookings` table to exist at all, since "blocked if bookings exist" is a real guard query, not a stub (per Epic 3's own description). Nothing about Booking's real feature set (overlap prevention, cancellation, the completion sweep) belongs here — Epic 4 owns all of that.

**Approach:** Add a genuinely minimal `Booking` entity — just enough to construct a valid row for test-seeding and to query "does this customer have any" — plus `HardDeleteCustomerCommand`. This is also the first real use of the HTTP `DELETE` verb in this app, which Story 2.3's Design Notes explicitly anticipated ("keeps `[HttpDelete]` free in case Customers ever need genuine verb-based REST semantics for something different") — this is that case.

**Scope decisions — flagged for approval:**

1. **`Booking.Create(...)` is built now, minimally.** It enforces exactly one context-free invariant domain-model.md states — `EndDate > StartDate` — via `DomainRuleViolationException`, mirroring `Vehicle`/`Customer`'s exact factory pattern (AD-15 has no "except during scaffolding" exception: no entity anywhere may expose a public setter). `Status` defaults to `Active` internally, never a caller-supplied parameter (matches AD-17: "Completed is never directly settable"). **Deliberately NOT built:** overlap prevention (needs cross-row Vehicle-availability logic — Epic 4), the past-booking delete guard, any Cancel/Complete transition. Those all belong to Epic 4's actual Booking feature set.
2. **`IBookingRepository` is created now with exactly one method**, `ExistsForCustomerAsync(Guid customerId, CancellationToken ct)` — mirrors how `IVehicleRepository`/`ICustomerRepository` both started with only what their first story needed. Epic 4 adds the rest.
3. **The Delete confirmation uses the NEUTRAL `ConfirmDialog`, not the destructive variant** — even though hard-delete is irreversible. Epic 3's own description explicitly reserves the destructive variant for Story 3.5's Erase action as its first real use ("Introduces the destructive `ConfirmDialog` variant against its first real use (the Erase action)"). This story's dialog message states the irreversibility explicitly in its copy instead of relying on visual styling to carry that weight — deliberate, not an oversight.
4. **"Delete" is offered as a row action for every customer**, not conditionally hidden based on booking count — there's no booking-creation UI yet (Epic 4 hasn't shipped), so no customer can accumulate real bookings through the app today; the guard only becomes reachable in practice via directly-seeded test data, exactly as the AC's own test methodology describes. The UI doesn't pre-guess which customers would fail; it lets the backend guard do its job and surfaces the 409 gracefully when it happens.
5. **`ICustomerRepository` gains `RemoveAsync`** — the first genuine hard-delete method anywhere in this repository layer (every prior delete has been a soft-delete field flip). Mirrors `AddAsync`'s shape (`dbContext.Customers.Remove(customer)`, no `SaveChangesAsync` — still `IUnitOfWork`'s job).

## Boundaries & Constraints

**Always:**
- The `Bookings` migration creates exactly the columns domain-model.md lists (Id, VehicleId FK→Vehicles, CustomerId FK→Customers, StartDate `date`, EndDate `date`, TotalPrice, Status, CreatedDate) plus indexes on both FK columns (Postgres doesn't auto-index FKs; this is routine schema hygiene, not scope creep) — no `EXCLUDE USING GIST` constraint yet (Epic 4's own migration adds that to this same table).
- `StartDate`/`EndDate` are `DateOnly`, mapped to Postgres `date` columns (AD-9).
- `HardDeleteCustomerCommandHandler`'s guard order: fetch customer (404 if missing) → `ExistsForCustomerAsync` (409 if true, message naming both alternatives verbatim: `"This customer has bookings — deactivate or erase their data instead."`) → `RemoveAsync` → `SaveChangesAsync`.
- The DELETE endpoint is `DELETE /api/customers/{id:guid}` — an actual hard delete, unlike Vehicle's `POST .../deactivate`.
- No new `GlobalExceptionHandler` branches needed — `DomainRuleViolationException`→409 and `NotFoundException`→404 already exist and cover this story's error cases exactly.

**Ask First:** Nothing expected to trigger.

**Never:** No overlap-prevention logic, no Cancel/Complete transitions, no past-booking delete guard (all Epic 4). No destructive `ConfirmDialog` styling for this story's Delete action (Scope decision 3). No booking-creation UI or API.

## I/O & Edge-Case Matrix

| Scenario | Input / State | Expected Output / Behavior | Error Handling |
|----------|--------------|---------------------------|----------------|
| Migration runs | Fresh database | `Bookings` table exists with correct FKs to `Vehicles`/`Customers` | N/A |
| Customer, zero bookings | Delete + confirm | `204`, customer permanently removed from the database | N/A |
| Customer, ≥1 booking (seeded directly) | Attempt delete | `409 Conflict`, exact message naming both alternatives | Dialog stays open, shows the message |
| Nonexistent customer id | Stale id | `404 Not Found` | Falls through to the generic `ServerError` treatment (same deliberate deferral as prior stories) |
| Cancel the confirmation | User declines | Dialog closes, nothing happens | N/A |

## Code Map

**Backend:**
- `src/BrunoVehicleHire.Domain/Booking.cs` -- new -- minimal entity (see Scope decision 1); `BookingStatus` enum (`Active`, `Completed`, `Cancelled`) as a nested or sibling type
- `tests/BrunoVehicleHire.Domain.Tests/BookingTests.cs` -- new -- valid creation, `EndDate <= StartDate` throws, `Status` defaults to `Active`, no public setters (reflection check, mirroring `VehicleTests`/`CustomerTests`)
- `src/BrunoVehicleHire.Infrastructure/Persistence/AppDbContext.cs` -- modify -- `DbSet<Booking> Bookings`, EF configuration (table, FKs, `Status` via `.HasConversion<string>()`, indexes on `VehicleId`/`CustomerId`, `StartDate`/`EndDate` as `date`)
- `src/BrunoVehicleHire.Infrastructure/Migrations/` -- new migration creating `Bookings`
- `src/BrunoVehicleHire.Application/Bookings/IBookingRepository.cs` -- new -- `Task<bool> ExistsForCustomerAsync(Guid customerId, CancellationToken ct);`
- `src/BrunoVehicleHire.Infrastructure/Repositories/BookingRepository.cs` -- new -- implements it
- `src/BrunoVehicleHire.Application/Customers/ICustomerRepository.cs` -- modify -- add `Task RemoveAsync(Customer customer, CancellationToken ct);`
- `src/BrunoVehicleHire.Infrastructure/Repositories/CustomerRepository.cs` -- modify -- implement it
- `src/BrunoVehicleHire.Application/Customers/Commands/HardDeleteCustomerCommand.cs`, `HardDeleteCustomerCommandHandler.cs` -- new -- no validator needed (no input fields beyond the route-bound id, mirroring `SoftDeleteVehicleCommand`'s reasoning)
- `src/BrunoVehicleHire.Api/Controllers/CustomersController.cs` -- modify -- `[HttpDelete("{id:guid}")]`
- `src/BrunoVehicleHire.Api/Program.cs` -- modify -- register `IBookingRepository`
- `tests/BrunoVehicleHire.Application.Tests/Customers/HardDeleteCustomerCommandHandlerTests.cs` -- new
- `tests/BrunoVehicleHire.Integration.Tests/BookingMigrationTests.cs` -- new -- mirrors `VehicleMigrationTests.cs`'s pattern (table exists, correct columns, FK constraints actually reject an orphaned reference)
- `tests/BrunoVehicleHire.Integration.Tests/CustomersEndpointTests.cs` -- modify -- add `DELETE` coverage: success (204, follow-up GET-list confirms gone) for a customer with zero bookings; 409 with the exact message for a customer with a directly-seeded booking (seeding requires a real Vehicle + Customer row first, for FK integrity); 404 for a nonexistent id

**Frontend:**
- `frontend/src/app/features/customers/customers.service.ts` -- modify -- add `useHardDeleteCustomerMutation()` mirroring `useDeactivateVehicleMutation`'s exact pattern (`DELETE` instead of `POST`, check `ApiClient`'s `delete<T>` method signature)
- `frontend/src/app/features/customers/customers-page.ts` (+`.html`) -- modify -- add a "Delete" entry to the per-row `actions` function; a `deletingCustomer` signal; a neutral `<app-confirm-dialog>` stating the permanent, irreversible nature of the action in its message copy, wired to the new mutation with the same "stays open and shows the error on failure" pattern `vehicles-page.ts` already established for Deactivate

## Tasks & Acceptance

**Execution — backend (TDD throughout):**
- [x] `Booking.Create(...)` -- write failing Domain.Tests first, then implement
- [x] `AppDbContext`'s `Booking` configuration + migration -- confirm it applies cleanly and FK constraints actually reject orphaned references (an explicit test, not assumed)
- [x] `IBookingRepository`/`BookingRepository` -- write failing tests first (via Testcontainers: a customer with a directly-seeded booking → `true`; a customer with none → `false`)
- [x] `ICustomerRepository.RemoveAsync` -- write failing tests first
- [x] `HardDeleteCustomerCommand`/`Handler` -- write failing tests first (success removes and saves; has-bookings throws the exact message and never calls `RemoveAsync`/`SaveChangesAsync`; not-found throws `NotFoundException`)
- [x] `CustomersController`'s `DELETE` action; register `IBookingRepository` in `Program.cs`
- [x] `BookingMigrationTests`, `CustomersEndpointTests` additions -- write failing integration tests first for every I/O-matrix row
- [x] Full `dotnet test`; re-run `ArchitectureFitnessTests`; force `dotnet restore` and check for NU1903 warnings; explicit SOLID/DRY/YAGNI self-check -- confirmed: 271/271 passing, 0 vulnerabilities

**Execution — frontend (TDD throughout):**
- [x] `useHardDeleteCustomerMutation` -- write failing tests first
- [x] Wire the "Delete" row action + `ConfirmDialog` into `customers-page` -- write failing tests first: clicking "Delete" opens the dialog with the right customer context; confirming calls the mutation and, on success, closes the dialog, shows a success toast, list re-fetches; cancelling closes the dialog with no mutation call; a failed (409) mutation keeps the dialog open and displays the exact "has bookings" message
- [x] `ng build` + `ng test --coverage --watch=false` clean; `npm audit` clean; explicit SOLID/DRY/YAGNI self-check -- confirmed: 184/184 passing, 97.72% stmt coverage, 0 vulnerabilities, `ConfirmDialog` untouched

**Acceptance Criteria (epics.md, verbatim intent):**
- Given this story's own migration creates a minimal `Bookings` table, when it runs, then the table exists with correct foreign keys to `Vehicles` and `Customers`
- Given a customer with zero rows in `Bookings`, when I click Delete and confirm, then the customer is permanently removed, and the guard query and command handler were built test-first with directly-inserted test data
- Given a customer with at least one row in `Bookings`, when I attempt to delete, then `409 Conflict` with a message naming both alternatives: "This customer has bookings — deactivate or erase their data instead"

## Spec Change Log

**Backend judgment call, confirmed sound:** the FK relationships to `Vehicles`/`Customers` use `DeleteBehavior.Restrict` rather than EF Core's default `Cascade` -- the DB itself now refuses to let a Vehicle or Customer row disappear out from under an existing Booking, a defense-in-depth backstop behind the Application-layer guard this story builds. Not explicitly specified by the spec; a reasonable inference from domain-model.md's cross-entity constraints, confirmed correct on review.

## Design Notes

**Why the Delete confirmation stays neutral despite being irreversible:** a visual/styling decision explicitly deferred to Story 3.5 by Epic 3's own description, not an inconsistency this story introduces by accident. The message copy carries the "permanent" weight instead of the dialog's visual treatment.

**Why `Booking.Create()` exists at all in a story that doesn't build any booking-creation UI:** the AC's own test methodology requires seeding real `Booking` rows directly for the guard's tests — and per AD-15, no entity anywhere may be constructed by bypassing a validating factory, even for test fixtures. Building `Create()` now, scoped to only the one context-free invariant this story can honestly validate, is the correct minimal step — not a preview of Epic 4's real feature set.

## Verification — actual results

- Backend: `dotnet build` 0 warnings/0 errors; `dotnet test` 271/271 passing (up from 253 before this story); no vulnerability warnings; `ArchitectureFitnessTests` unchanged/passing. Live manual proof of the full lifecycle (create → delete-no-bookings-204 → create+seed-booking → delete-attempt-409-exact-message → cleanup) against the real docker-compose Postgres.
- Frontend: `ng build` 0 errors/warnings (590.81kB, within the 650kB budget); `ng test --coverage --watch=false` 184/184 passing, 97.72% statement coverage; `npm audit` 0 vulnerabilities. `ConfirmDialog` confirmed completely untouched via `git diff`.
- Full live end-to-end, including the real 409 case: real backend + `ng serve` + real Postgres, plus a directly-seeded Booking row to force the guard for real (not just via the automated Testcontainers tests) -- confirmed the em-dash in the exact backend message renders correctly in the dialog.

</frozen-after-approval>
