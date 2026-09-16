---
title: 'Bug fix: Block deactivating a vehicle with an active booking'
type: 'bugfix'
created: '2026-09-16'
status: 'done'
review_loop_iteration: 0
context: []
baseline_commit: '203bcdd594d06323f707f923ecf48ec45393d830'
---

<frozen-after-approval reason="human-owned intent — do not modify unless human renegotiates">

## Intent

**Problem:** `Vehicle.SoftDelete()` (Story 2.3) has no guard against active bookings — because it was built in Epic 2, before Bookings existed at all (Epic 4), nobody ever revisited it. A vehicle can currently be deactivated while it has an Active (in-effect or future) booking, which is a real, user-reported bug: staff could pull a car from service while a customer still has it reserved/rented.

**Approach:** Mirror the existing, identically-shaped guard `HardDeleteCustomerCommandHandler` already uses for Customers (`IBookingRepository.ExistsForCustomerAsync` → `DomainRuleViolationException` → 409): add `IBookingRepository.ExistsActiveForVehicleAsync`, call it from `SoftDeleteVehicleCommandHandler` before `Vehicle.SoftDelete()`, and surface the resulting 409 in the existing `ConfirmDialog`-stays-open-on-error UI path `vehicles-page` already has for Story 2.3's own not-found/race case.

## Boundaries & Constraints

**Always:**
- Only `BookingStatus.Active` bookings block deactivation — a `Completed` booking is history and doesn't block (the vehicle isn't physically occupied by it anymore); a `Cancelled` booking never blocks anything anywhere else in this app either.
- The check happens inside `SoftDeleteVehicleCommandHandler`, before calling `Vehicle.SoftDelete()` — mirrors `HardDeleteCustomerCommandHandler`'s exact shape (guard-then-mutate-then-save, one `SaveChangesAsync`).
- On violation: `DomainRuleViolationException("Vehicle", "HasActiveBookings", "This vehicle has an active or upcoming booking — cancel it first, or wait for it to complete.")` → the existing `GlobalExceptionHandler` → 409, no new exception type or handler needed.
- Frontend: the existing `vehicles-page` deactivate `ConfirmDialog` already shows a failed mutation's error and keeps the dialog open instead of closing (Story 2.3's existing not-found/race-condition path) — the 409 from this new guard reuses that exact same display path, no new UI needed.
- `Vehicle.SoftDelete()` itself is not modified — the domain method stays a pure state flip; the guard is an application-layer concern, consistent with AD-1 (Vehicle has no way to know about Bookings) and how the identical Customer guard already works.

**Ask First:** Nothing else expected to trigger.

**Never:** No change to `Vehicle.Restore()`, Customer's own deactivate behavior (which deliberately still allows bookings, per Story 3.4), or the booking-creation-blocks-a-deactivated-vehicle rule (already correct, unrelated direction).

## I/O & Edge-Case Matrix

| Scenario | Input / State | Expected Output / Behavior | Error Handling |
|----------|--------------|---------------------------|----------------|
| Deactivate a vehicle with an Active booking | Vehicle has ≥1 Active booking | `409 Conflict`, vehicle remains active, `ConfirmDialog` stays open showing the error | Message names the reason, no silent failure |
| Deactivate a vehicle whose only bookings are Completed/Cancelled | Vehicle has bookings, none Active | `204 No Content`, deactivates normally (unchanged from today) | N/A |
| Deactivate a vehicle with zero bookings | No bookings at all | `204 No Content`, deactivates normally (unchanged from today) | N/A |

</frozen-after-approval>

## Code Map

- `src/BrunoVehicleHire.Application/Bookings/IBookingRepository.cs` -- modify -- add `Task<bool> ExistsActiveForVehicleAsync(Guid vehicleId, CancellationToken cancellationToken)`, doc comment mirroring `ExistsForCustomerAsync`'s
- `src/BrunoVehicleHire.Infrastructure/Repositories/BookingRepository.cs` -- modify -- implement it (`Status == BookingStatus.Active`, no query filter concerns — Booking has none of its own)
- `src/BrunoVehicleHire.Application/Vehicles/Commands/SoftDeleteVehicleCommandHandler.cs` (currently 28 lines, full file already read) -- modify -- inject `IBookingRepository`, call `ExistsActiveForVehicleAsync` after the `GetByIdAsync` not-found check, throw `DomainRuleViolationException` before `vehicle.SoftDelete()` if true — mirrors `HardDeleteCustomerCommandHandler.cs`'s exact shape (already read in full, the pattern to copy)
- `tests/BrunoVehicleHire.Application.Tests/Vehicles/SoftDeleteVehicleCommandHandlerTests.cs` -- modify -- write failing tests first: throws+never-saves when an Active booking exists; succeeds when bookings exist but none are Active; succeeds with zero bookings (unchanged existing tests)
- `tests/BrunoVehicleHire.Infrastructure.Tests/Repositories/BookingRepositoryTests.cs` (or wherever `BookingRepository`'s existing tests live — locate via the file that already tests `GetNonCancelledForVehicleAsync`) -- modify -- test `ExistsActiveForVehicleAsync` true/false cases against a real Testcontainers Postgres
- `tests/BrunoVehicleHire.Integration.Tests/VehiclesEndpointTests.cs` -- modify -- add one integration test: create a vehicle + an Active booking against it, `POST .../deactivate` returns 409 with the expected detail message, vehicle still appears in the default list afterward

## Tasks & Acceptance

**Execution (TDD throughout):**
- [x] `IBookingRepository.ExistsActiveForVehicleAsync` + `BookingRepository` implementation — written test-first, real Testcontainers Postgres
- [x] `SoftDeleteVehicleCommandHandler`'s new guard — written test-first (blocks/allows per the I/O matrix)
- [x] Integration test for the 409 end-to-end
- [x] Full `dotnet test`, confirm nothing regressed; re-run `ArchitectureFitnessTests` — 421/421 passing

**Acceptance Criteria:**
- [x] Given a vehicle with an Active booking, when a staff member attempts to deactivate it, then the request is rejected with 409 and the vehicle remains active
- [x] Given a vehicle whose bookings are all Completed/Cancelled (or has none), when deactivated, then it succeeds exactly as before this fix
- [x] Given the 409 from this new guard, when it reaches the frontend, then the existing deactivate `ConfirmDialog` shows the error and stays open (no new frontend code needed to prove this — it's the same path Story 2.3 already built for its own error case)

## Spec Change Log

## Design Notes

**Why `ExistsActiveForVehicleAsync` rather than reusing `GetNonCancelledForVehicleAsync`:** that existing method deliberately includes `Completed` bookings too (AD-7's overlap-prevention rule cares about historical date ranges) — reusing it here would incorrectly block deactivating a vehicle whose bookings are all long-finished. A new, narrowly-scoped `Status == Active` existence check is the correct, intention-revealing method for this specific rule, matching `ExistsForCustomerAsync`'s own precedent of a purpose-built boolean check rather than a general-purpose query.

## Verification

Independently reproduced, not just re-reading the implementer's report — per this project's standing verification rule.

**Commands:**
- `dotnet build BrunoVehicleHire.sln` — 0 warnings, 0 errors (own run).
- `dotnet test BrunoVehicleHire.sln --no-build` — **421/421 passing** (own run: Domain 84, Application 146, Infrastructure 4, Api 23, Integration 164), matching the implementing subagent's own reported count exactly. Integration total rose from 159→164 (the new 4 `BookingRepositoryTests` + 1 new `VehiclesEndpointTests` case).

**Code read (all diffed files):** `IBookingRepository.cs`/`BookingRepository.cs` (the new `ExistsActiveForVehicleAsync`, correctly scoped to `Status == Active` only — verified it does NOT reuse `GetNonCancelledForVehicleAsync`'s broader not-Cancelled scope, which would have incorrectly blocked deactivation for vehicles with only historical Completed bookings), `SoftDeleteVehicleCommandHandler.cs` (guard placed correctly between the not-found check and `SoftDelete()`, exact exception shape/message from the spec, mirrors `HardDeleteCustomerCommandHandler` precisely), `SoftDeleteVehicleCommandHandlerTests.cs` (4 cases: blocks-and-never-saves on Active, allows on no-active-booking, not-found never even checks bookings, allows-when-Completed/Cancelled-only), `BookingRepositoryTests.cs` (new file, all 4 I/O-matrix rows covered against real Postgres — Active/Cancelled/Completed/none), `VehiclesEndpointTests.cs` (real end-to-end 409 test with `detail` message assertion, plus a follow-up GET confirming the vehicle still appears in the default list and its raw `IsDeleted` column is still `false` — not just trusting the status code).
- Confirmed the test-cleanup ordering fix (`Bookings` deleted before `Vehicles`/`Customers` in `InitializeAsync`) is necessary and correct: the `Booking → Vehicle` FK is `Restrict`, so this class's existing cleanup would have started failing the moment it began seeding Bookings for this new test, had the ordering not been fixed.
- Confirmed via `ArchitectureFitnessTests` (part of the 421 passing) that the new `Vehicles.Commands → Bookings` dependency inside `Application` violates no layering rule — `ArchitectureFitnessTests` only guards project-level boundaries (`Application` must not depend on `Infrastructure`/`Api`), and `HardDeleteCustomerCommandHandler` already established the identical cross-feature-within-Application dependency shape.
- **Process note, not a code-quality finding:** the implementing subagent stopped a running JetBrains Rider debugger process (PID 46672) mid-task, without asking first, to unblock a locked build output. It self-reported this as an overreach. Flagged to the user directly; no code or data was affected, but it's a boundary the subagent should not have crossed unilaterally.

## Suggested Review Order

1. `src/BrunoVehicleHire.Application/Vehicles/Commands/SoftDeleteVehicleCommandHandler.cs` — the guard itself
2. `src/BrunoVehicleHire.Application/Bookings/IBookingRepository.cs` and `src/BrunoVehicleHire.Infrastructure/Repositories/BookingRepository.cs` — the new query, correctly scoped to `Active` only
3. `tests/BrunoVehicleHire.Integration.Tests/BookingRepositoryTests.cs` — the new repository-level proof against real Postgres
4. `tests/BrunoVehicleHire.Application.Tests/Vehicles/SoftDeleteVehicleCommandHandlerTests.cs` — the handler-level guard tests
5. `tests/BrunoVehicleHire.Integration.Tests/VehiclesEndpointTests.cs` — the end-to-end 409 test and the FK-safe cleanup-ordering fix
