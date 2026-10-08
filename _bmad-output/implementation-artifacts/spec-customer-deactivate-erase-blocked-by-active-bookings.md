---
title: 'Behaviour change: a customer with an Active booking can be neither deactivated nor erased'
type: 'feature'
created: '2026-10-07'
status: 'done'
review_loop_iteration: 0
context: []
baseline_commit: 'db1981a'
---

<frozen-after-approval reason="human-owned intent — do not modify unless human renegotiates">

## Intent

**Problem:** A customer could be deactivated or have their personal data erased while still holding an Active (in-effect or upcoming) booking, leaving a live rental attached to a hidden or scrubbed customer. The vehicle side already refuses this (a vehicle with an Active booking cannot be deactivated); the customer side did not. This was a deliberate earlier decision (spec-3-4 / spec-3-5 chose not to gate Deactivate/Erase on bookings) which the human has now reversed.

**Approach:** Add the mirror of the vehicle rule: Deactivate and Erase return a 409 while the customer has an Active booking. Completed and Cancelled bookings are history and never block (that history is exactly what Erase exists to preserve). Hard Delete keeps its stricter existing rule (ANY booking blocks it).

## Boundaries & Constraints

**Always:**
- "Active booking" means a Booking whose `Status` is `Active` (the same definition as the vehicle rule).
- Deactivate and Erase both throw `DomainRuleViolationException("Customer", "HasActiveBookings", "This customer has an active or upcoming booking — cancel it first, or wait for it to complete.")`, surfaced as the usual 409 ProblemDetails (`urn:bruno:customer:has-active-bookings`).
- The rule and its message live in ONE place shared by both handlers (`CustomerActiveBookingGuard`).
- The check runs after the not-found check and before the domain mutation, so a blocked request changes nothing (nothing is saved, personal data untouched).
- Erase is idempotent: repeating it on an already-anonymized customer is still a silent no-op and is NOT re-checked against bookings.

**Ask First:** Nothing else expected to trigger.

**Never:** No change to Hard Delete's any-booking rule or message, to Restore, to the database schema, or to the seeder (it builds state through the domain methods directly and is unaffected). No UI gating: the frontend shows the API's 409 message in the existing confirm dialog.

## I/O & Edge-Case Matrix

| Scenario | Input / State | Expected Output / Behavior |
|----------|--------------|---------------------------|
| Deactivate, Active booking | Customer with an Active booking | 409 with the message above; customer stays active |
| Erase, Active booking | Customer with an Active booking | 409 with the message above; personal data untouched |
| Deactivate / Erase, history only | Only Completed or Cancelled bookings | 204 as before; Erase keeps the bookings |
| Deactivate / Erase, no bookings | None | 204 as before |
| Erase again, already anonymized | Already anonymized (even with a legacy Active booking) | 204 no-op, not re-checked |
| Another customer's Active booking | Active booking belongs to a different customer | Does not block |
| Hard Delete | Any booking | Unchanged: 409 |

</frozen-after-approval>

## Code Map

- `src/BrunoVehicleHire.Application/Bookings/IBookingRepository.cs`, `src/BrunoVehicleHire.Infrastructure/Repositories/BookingRepository.cs` -- modify -- `ExistsActiveForCustomerAsync(customerId)`: any booking with `CustomerId` and `Status == Active`
- `src/BrunoVehicleHire.Application/Customers/Commands/CustomerActiveBookingGuard.cs` -- new -- the shared rule and message
- `SoftDeleteCustomerCommandHandler.cs`, `AnonymizeCustomerCommandHandler.cs` -- modify -- take `IBookingRepository`, call the guard before mutating (Erase skips it when already anonymized)
- `frontend/.../customers-page.ts` -- comment only (the UI still never gates; the API message is shown)
- Tests: `SoftDeleteCustomerCommandHandlerTests`, `AnonymizeCustomerCommandHandlerTests`, `CustomerConcurrencyConflictTests` (constructor change), `BookingRepositoryTests`, `CustomersEndpointTests`

## Tasks & Acceptance

**Execution (test-first):**
- [x] Handler unit tests: blocked (409, nothing saved or scrubbed), allowed with history only, not-found never checks bookings, Erase idempotent when already anonymized
- [x] Repository tests: Active -> true; Cancelled / Completed only -> false; someone else's Active booking -> false
- [x] HTTP tests: both actions 409 with an Active booking and leave the customer untouched; both 204 with Cancelled/Completed history (Erase keeps the booking)
- [x] Implement the repository query, the shared guard and the two handler changes
- [x] Full backend suite in an isolated copy of the working tree

**Acceptance Criteria:**
- [x] Given a customer with an Active booking, when Deactivate or Erase is requested, then it is refused with the 409 above and nothing changes
- [x] Given a customer whose bookings are all Completed or Cancelled, when Deactivate or Erase is requested, then it succeeds and the bookings are kept
- [x] Given an already-anonymized customer, when Erase is repeated, then it is still a successful no-op

## Spec Change Log

**2026-10-07 -- reverses spec-3-4 Scope decision 2 and spec-3-5 Scope decision 1.** Both said Deactivate/Erase are deliberately NOT gated on bookings ("epics.md's 'customer with bookings' phrasing describes the realistic case, not an enforced precondition"). The human has asked for the vehicle-style guard instead. The realistic erasure case (a customer with booking HISTORY) still works; only a customer with a live booking is refused.

## Design Notes

**Why Active-only, mirroring vehicles:** an Active booking (in effect or upcoming) means the customer is still a party to a live rental; a Completed/Cancelled booking is history. Using the same definition for both entities keeps the two rules explainable in one sentence.

**Why Erase skips the check when already anonymized:** the domain defines Erase as idempotent. Seed data (and any legacy row) can hold an anonymized customer with an Active booking; repeating Erase on it must not suddenly return a 409.

**Known limits:** (1) check-then-act: a booking created in the same instant a customer is deactivated could slip through (vehicles have the same limit). (2) Hard Delete's existing message still says "deactivate or erase their data instead", which for a customer with an Active booking now leads to another 409; the wording was left unchanged because several tests assert it.

## Verification

**Commands run (isolated copy of the working tree, because a Rider debug session was holding the API's DLLs):**
- `dotnet test BrunoVehicleHire.sln` -- Integration 198 pass, Application 179 pass. Every new test passes. The only failures are 4 pre-existing, date-dependent tests (hard-coded early-October-2026 booking dates now in the past) that fail identically on a pristine checkout of `db1981a`.

**Not done by the implementer:** a live API/browser pass of this rule and a full end-to-end run of the Bruno collection; the HTTP-level tests cover the behaviour through the real pipeline instead.

## Suggested Review Order

1. `CustomerActiveBookingGuard.cs` -- the rule and message in one place.
2. `SoftDeleteCustomerCommandHandler.cs`, `AnonymizeCustomerCommandHandler.cs` -- where it is applied (and the already-anonymized exception).
3. `ExistsActiveForCustomerAsync` in the repository.
4. The six HTTP tests in `CustomersEndpointTests.cs`.
