---
title: 'Story 4.3: Cancel a Booking'
type: 'feature'
created: '2026-09-14'
status: 'done'
review_loop_iteration: 0
context: []
baseline_commit: 'a6540622f4b1aafa7b32ba412cb414154bc52728'
---

<frozen-after-approval reason="human-owned intent — do not modify unless human renegotiates">

## Intent

**Problem:** A booking can be created but never cancelled — staff have no way to accommodate a change of plans without either leaving a stale Active row or physically deleting history, which AD-16 forbids.

**Approach:** Add `Booking.Cancel()` (Active → Cancelled, never a physical delete), a matching command/endpoint mirroring Vehicle/Customer's Deactivate shape exactly, and a Cancel row action on the Bookings list using the already-extracted `createConfirmableAction` (its fourth consumer, after Delete/Deactivate/Erase).

## Boundaries & Constraints

**Always:**
- `Booking.Cancel(TimeProvider? timeProvider = null)`: throws `DomainRuleViolationException` with "Cannot cancel — booking already cancelled." if `Status == Cancelled`; throws the same type with "Cannot cancel — booking already completed." if `Status == Completed` **or** `EndDate` is on/before today (per the injected `TimeProvider`) — the exact same message for both, since a past-EndDate-but-not-yet-swept booking is ineligible for the identical reason a user would understand as "this booking is over" (AC: "the cancel guard treats it as ineligible... consistent with the domain rule regardless of whether the sweep has run"). Otherwise sets `Status = Cancelled`. No other field changes; the row is never physically removed (AD-16).
- `CancelBookingCommand`/`Handler` mirror `SoftDeleteVehicleCommand`/`Handler`'s exact shape (fetch by id -> 404 if missing -> `Cancel()` -> one `SaveChangesAsync` -> `Unit`/204, per AD-2).
- `IBookingRepository` gains a plain `GetByIdAsync(id, ct) -> Booking?` (Booking has no soft-delete query filter, so this is a direct, unfiltered lookup — no `IgnoreQueryFilters()` consideration needed, unlike Vehicle/Customer).
- The Bookings list's `actions` function offers "Cancel" only for a booking that is currently eligible client-side (`status === 'Active' && endDate >= today`) — mirrors this app's established pattern of never offering an action the backend would always reject (Vehicle/Customer's state-dependent `actions`). An ineligible booking (Completed, Cancelled, or past-EndDate-Active) gets no Cancel action at all; the 409 paths are proven at the API/integration level as defensive backstops for a stale UI/race, not exercised by clicking through the app.
- Cancel uses the neutral `ConfirmDialog` via `createConfirmableAction` (its fourth consumer — Delete/Deactivate/Erase already use it), stating the booking stays in records as Cancelled and that this is not reversible.

**Ask First:** Nothing else expected to trigger.

**Never:** No physical delete of a Booking row, ever. No frontend path that lets a user click Cancel on an already-ineligible row (the 409 is a defensive guard, not a reachable UI flow).

## I/O & Edge-Case Matrix

| Scenario | Input / State | Expected Output / Behavior | Error Handling |
|----------|--------------|---------------------------|----------------|
| Cancel a future Active booking | Confirm the dialog | `204`; `Status` → `Cancelled`; row never removed | N/A |
| Cancel an already-Completed booking | Direct API call (stale UI/race) | `409`, "Cannot cancel — booking already completed." | N/A |
| Cancel an already-Cancelled booking | Direct API call | `409`, "Cannot cancel — booking already cancelled." | N/A |
| Cancel a still-Active booking whose EndDate has passed (not yet swept) | Direct API call | `409`, "Cannot cancel — booking already completed." | N/A |
| Cancel a nonexistent booking id | Invalid id | `404` | Falls through to `ServerError` |

## Code Map

**Backend:**
- `src/BrunoVehicleHire.Domain/Booking.cs` -- modify -- add `Cancel(TimeProvider?)` per Boundaries
- `tests/BrunoVehicleHire.Domain.Tests/BookingTests.cs` -- modify -- success, already-Cancelled, already-Completed, past-EndDate-still-Active cases, written first
- `src/BrunoVehicleHire.Application/Bookings/IBookingRepository.cs`, `src/BrunoVehicleHire.Infrastructure/Repositories/BookingRepository.cs` -- modify -- add `GetByIdAsync`
- `src/BrunoVehicleHire.Application/Bookings/Commands/CancelBookingCommand.cs`, `CancelBookingCommandHandler.cs` -- new -- mirrors `SoftDeleteVehicleCommandHandler`'s exact shape
- `tests/BrunoVehicleHire.Application.Tests/Bookings/CancelBookingCommandHandlerTests.cs` -- new -- success, 404, and the three domain-exception pass-through cases
- `src/BrunoVehicleHire.Api/Controllers/BookingsController.cs` -- modify -- `[HttpPost("{id:guid}/cancel")]`, mirrors `VehiclesController.Deactivate`'s shape
- `tests/BrunoVehicleHire.Integration.Tests/BookingsEndpointTests.cs` -- modify -- every I/O-matrix row through the real API (Completed/past-Active seeded via the existing raw-SQL status-flip helper from spec-4-2)

**Frontend:**
- `frontend/src/app/features/bookings/bookings.service.ts` -- modify -- add `useCancelBookingMutation()`, mirrors `useDeactivateCustomerMutation` exactly
- `frontend/src/app/features/bookings/bookings-page.ts` (+`.html`) -- modify -- a `cancelAction = createConfirmableAction<Booking>(...)`; `actions` becomes a per-row function (`isCancellable(booking)` -> `[Cancel]` or `[]`, replacing the current always-empty `actions` since 4.1 shipped none); a `<app-confirm-dialog>` block for Cancel

## Tasks & Acceptance

**Execution — backend (TDD throughout):**
- [x] `Booking.Cancel()` -- write failing Domain.Tests first, then implement
- [x] `IBookingRepository.GetByIdAsync`, `CancelBookingCommand`/`Handler` -- write failing tests first
- [x] `BookingsController`'s `cancel` action
- [x] `BookingsEndpointTests` -- every I/O-matrix row, written first
- [x] Full `dotnet test`; `ArchitectureFitnessTests`; force `dotnet restore`, check `NU1903`; SOLID/DRY/YAGNI self-check -- confirmed independently: 370/370 passing, 0 advisories

**Execution — frontend (TDD throughout):**
- [x] `useCancelBookingMutation` -- write failing test first
- [x] `bookings-page.ts`'s eligible/ineligible `actions`, `cancelAction`, and its `ConfirmDialog` -- write failing tests first
- [x] `ng build` + `ng test --coverage --watch=false` clean; `npm audit` clean; SOLID/DRY/YAGNI self-check -- confirmed independently: 271/271 passing, 97.68% statement coverage, 0 vulnerabilities

**Acceptance Criteria (epics.md, verbatim intent):**
- Given a future Active booking, clicking Cancel and confirming transitions `Status` to `Cancelled`; the row is never physically deleted
- Given an already-Completed or already-Cancelled booking, attempting to cancel it returns `409` with the exact message for that state
- Given a still-Active booking whose EndDate is already in the past, the cancel guard treats it as ineligible exactly as if it were Completed

## Spec Change Log

## Design Notes

**Why Cancel reuses `createConfirmableAction` rather than a bespoke flow:** this is its fourth consumer (Delete, Deactivate, Erase already use it in Customers) — exactly the kind of cross-feature reuse the extraction was meant to enable; nothing Booking-specific is needed beyond supplying its own mutation, message, and success copy.

</frozen-after-approval>

## Verification

Independently re-verified after implementation (not just the implementer's self-report), and this time the implementer correctly did NOT self-commit — every finalization step below was done by the orchestrating session after full independent verification, per the corrected process:

**Backend:** `git status --short` confirmed the exact expected file set; `dotnet build BrunoVehicleHire.sln` clean (0 warnings, 0 errors); full `dotnet test` 370/370 passing (Domain 79, Application 129, Infrastructure 4, Api 23, Integration 135), up from 355 before this story; `dotnet restore --force` showed no `NU1903` advisories. Read `Booking.cs` directly and confirmed `Cancel(TimeProvider?)` checks already-Cancelled first (most specific message), then Completed-or-past-EndDate (identical message for both) exactly per the Boundaries. Read `CancelBookingCommandHandler.cs` directly and confirmed it's a faithful structural mirror of `SoftDeleteVehicleCommandHandler`. Read the domain test file's reflection-based "already-Completed" test and confirmed it's a legitimate, well-justified technique for isolating the Status-check branch given `Booking` has no public `Complete()` method yet (Story 4.4's job).

**Frontend:** `git status --short` confirmed the exact expected file set; `ng build` clean (0 errors, 616.89 kB / budget 650 kB); `ng test --coverage --watch=false` 271/271 passing, 97.68% statement coverage; `npm audit` 0 vulnerabilities -- all reproduced independently. Read `bookings-page.ts` directly and confirmed `isCancellable`'s client-side eligibility check (`status === 'Active' && endDate >= startOfToday()`) correctly mirrors the domain guard, and `cancelAction` is `createConfirmableAction`'s fourth consumer with no new dialog plumbing invented.

**Live end-to-end pass** (against the real API + Postgres + browser, since this story adds a genuinely new interactive flow with client-side date-eligibility logic that unit tests alone wouldn't fully prove): created a real Vehicle, a Customer (via the nested "+ New Customer" flow), and a future-dated Booking; confirmed the "Cancel" action appeared only on the eligible Active row; clicked it, confirmed the exact dialog copy ("This booking will stay in records as Cancelled. This is not reversible."); confirmed the booking transitioned to a correctly-styled red "Cancelled" `Badge` and the Actions column disappeared entirely once no row had any actions left (`DataTable`'s existing `hasAnyRowActions` behavior, unmodified). The dev database was left empty afterward.

**SOLID/DRY/YAGNI:** confirmed via direct code reading -- `Booking.Cancel()` owns every ineligibility rule itself (SRP); the handler, controller, and frontend mutation are structural mirrors of existing Deactivate/SoftDelete shapes with zero new patterns invented (DRY); the frontend never offers a Cancel action the backend would reject, and no speculative Edit/Complete functionality was added (YAGNI).

## Suggested Review Order

1. `src/BrunoVehicleHire.Domain/Booking.cs` -- `Cancel(TimeProvider?)`
2. `tests/BrunoVehicleHire.Domain.Tests/BookingTests.cs` -- the `Cancel` nested test class, especially the reflection-based already-Completed case
3. `src/BrunoVehicleHire.Application/Bookings/Commands/CancelBookingCommandHandler.cs`
4. `frontend/src/app/features/bookings/bookings-page.ts` -- `isCancellable` and `cancelAction`
