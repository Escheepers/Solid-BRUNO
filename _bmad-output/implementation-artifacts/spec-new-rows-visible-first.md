---
title: 'Bug fix: a newly created row is not visible on any list (Vehicles, Customers, Bookings)'
type: 'bugfix'
created: '2026-10-07'
status: 'done'
review_loop_iteration: 0
context: []
baseline_commit: 'db1981a'
---

<frozen-after-approval reason="human-owned intent — do not modify unless human renegotiates">

## Intent

**Problem:** After creating a Vehicle, Customer or Booking, the new row does not appear on the list, so it looks as if nothing was created. Three causes combine: (1) all three list queries order **oldest first**, so a new row lands at the very end, on the last page; (2) after a create the page keeps whatever page number it was on and any active search/column sort stays, so even a row that is on page 1 can be hidden by a non-matching search or buried by a sort the user clicked earlier; (3) related paging bugs in the same family: toggling "Show inactive" does not reset the page (it can leave the table on a page that no longer exists), and removing the last row of the last page (Deactivate/Delete/Erase/Cancel) does the same.

**Approach:** Order every list newest-first. After a successful create, jump to where the new row is visible (page 1, search cleared, column sort cleared). Reset the page when "Show inactive" changes. Make `DataTable` ask its owner to move to the last valid page whenever the current page is past the end of a non-empty result.

## Boundaries & Constraints

**Always:**
- Vehicle/Customer/Booking `GetPagedAsync` order by `CreatedDate` descending, then `Id` descending (Guid v7, so the tie-break stays time-ordered and the page boundary stays stable).
- "Created" is signalled by an explicit `created` output on each form modal that fires only after a successful CREATE (never an edit, never a cancel/discard).
- After a create the page clears the search, goes to page 1 and bumps a `sortResetToken` that `DataTable` turns into "no column sort".
- The out-of-range-page behaviour lives once, in the shared `DataTable`, not in each page. It is skipped while loading and when the total is 0 (an empty state, not a stale page).
- "Show inactive" changing resets the page to 1, the same way a changed search term already does.

**Ask First:** Nothing else expected to trigger.

**Never:** No change to Customer Summary's own booking-history ordering (`GetForCustomerWithVehicleAsync`, a chronological history view), to which fields are searchable, or to the API contract/DTOs. No new query parameters.

## I/O & Edge-Case Matrix

| Scenario | Input / State | Expected Output / Behavior |
|----------|--------------|---------------------------|
| Create, more rows than one page | 3 older rows + 1 new, page size 2 | Page 1 lists the new row first, then the newest older row; total 4 |
| Create while a search is active | Search "toyota", new row does not match | Search box cleared, list shows page 1 unfiltered with the new row first |
| Create after clicking a column sort | Sort "Total" ascending | Sort cleared, server order (newest first) shown |
| Create while on page 2 | User on page 2 | Back to page 1 |
| Toggle "Show inactive" while on page 2 | Page 2, toggle changes | Page 1 of the new filter, never a stale page |
| Last row of the last page removed | On page 2, total shrinks to one page | Table moves to the last valid page (page 1) |
| Empty result | Total is 0 | No page change (that is the empty state) |
| Edit or cancel/discard in a form modal | — | `created` does not fire; list state untouched |

</frozen-after-approval>

## Code Map

- `src/BrunoVehicleHire.Infrastructure/Repositories/VehicleRepository.cs`, `CustomerRepository.cs`, `BookingRepository.cs` -- modify -- `GetPagedAsync` ordering: `OrderByDescending(CreatedDate).ThenByDescending(Id)`
- `frontend/src/app/shared/data-table/data-table.ts` -- modify -- `sortResetToken` input (`sortState` becomes a `linkedSignal` on it); constructor `effect()` emitting `pageChange(lastPage)` for an out-of-range page
- `frontend/src/app/features/vehicles/vehicle-form-modal.ts`, `bookings/booking-form-modal.ts` -- modify -- new `created` output, emitted on successful create only (`CustomerFormModal` already had one)
- `frontend/src/app/features/{vehicles,customers,bookings}/*-page.ts|html` -- modify -- `sortResetToken` signal, `onCreated()` (clear search, page 1, bump token), `(created)` binding, `[sortResetToken]` binding; Vehicles/Customers: page `linkedSignal` also resets on `showInactive`
- Tests: `Vehicles/Customers/BookingsEndpointTests.cs` (create then list newest-first), `data-table.spec.ts`, `vehicle-form-modal.spec.ts`, `booking-form-modal.spec.ts`, `vehicles/customers/bookings-page.spec.ts`

## Tasks & Acceptance

**Execution (TDD throughout):**
- [x] Failing endpoint tests (3) proving oldest-first ordering, then switch the three repositories to newest-first
- [x] `DataTable`: failing tests for sort reset and out-of-range page, then implement both
- [x] `created` output on the Vehicle and Booking modals (tests: emitted on create, not on edit/failure)
- [x] Page behaviour for all three pages: after create (page 1 / search cleared / sort cleared), Show-inactive page reset, stale-page clamp
- [x] Mutation check: with the page fixes disabled the new page tests fail (4 of 4)
- [x] Full backend and frontend suites, production build

**Acceptance Criteria:**
- [x] Given any list with more rows than one page, when a row is created, then it is the first row on page 1
- [x] Given an active search or a clicked column sort or a later page, when a row is created, then the list shows page 1 unfiltered, unsorted, with the new row first
- [x] Given "Show inactive" is toggled or the current page stops existing, then the list never stays on a non-existent page

## Spec Change Log

## Design Notes

**Why newest-first rather than "jump to the last page":** the user's mental model is "I created it, it should be at the top"; newest-first also makes the most recent activity the most visible by default on every list. Jumping to the last page would need the new row's position from the server and breaks as soon as anything else is created.

**Why the clamp lives in `DataTable`:** all three pages (and the Vehicle Detail booking history) use the same table and the same `page`/`totalCount` inputs; one effect fixes every consumer, instead of three copies of the same page-bounds logic. It deliberately does not fire while loading or for a total of 0, otherwise it would fight a normal page change that is still in flight or an empty state.

**Why `created` is separate from `closeRequest`:** `closeRequest` fires for Cancel/Escape/Discard as well, so a page cannot tell "something was created" from "the user closed the form".

**Known limit:** the client-side column sort only ever applies to the loaded page (unchanged). A column sort chosen by the user is cleared on create on purpose, because a sort that buries the new row defeats the point of the confirmation.

## Verification

**Commands run (independently, in an isolated copy of the working tree because a Rider debug session was holding the API's DLLs):**
- Red/green: the three new endpoint tests failed against the old ordering (page 1 showed the OLDEST rows) and pass after the fix. The two `DataTable` behaviour tests failed before and pass after. A mutation check (page fixes disabled in `VehiclesPage`) made 4 of the 4 new page tests fail.
- `ng test --watch=false` -- 405/405 (385 before + 20 new); `ng build` -- clean.
- `dotnet test BrunoVehicleHire.sln` -- every test touching this change passes. **4 pre-existing failures unrelated to this change**, identical on a pristine checkout of `db1981a`: `CancelBookingCommandHandlerTests.Handle_ExistingEligibleBooking_...`, `CompleteBookingCommandHandlerTests.Handle_StillActiveBookingWithFutureEndDate_...`, `BookingsEndpointTests.Cancel_FutureActiveBooking_...` and `Cancel_TwoConcurrentRequestsOnSameActiveBooking_...`. They hard-code early-October-2026 booking dates against the real clock, so they started failing once the calendar passed those dates ("Cannot cancel -- booking has already started"). `CustomerLoggingPiiTests` failed once under full-suite load and passed in isolation (flaky).

**Manual checks (live, real browser against an API built from this change on a throwaway seeded database):**
- Bookings, 49 rows / 3 pages: on page 2, created a booking through the form -> back on page 1, the new booking is the first row, form closed, search empty, "Booking created." toast.
- Vehicles, search `a` active and "Daily Rate" sorted ascending: created a vehicle that does not match the search -> search cleared, sort indicator reset to none, the new vehicle is first, remaining rows in server (newest-first) order.

## Suggested Review Order

1. `src/BrunoVehicleHire.Infrastructure/Repositories/*Repository.cs` -- the ordering change that fixes the core symptom, and the three endpoint tests that pin it.
2. `frontend/src/app/shared/data-table/data-table.ts` -- `sortResetToken` and the out-of-range-page effect.
3. `frontend/src/app/features/*/…-page.ts` -- `onCreated()` and the `showInactive`-aware page reset.
4. The two modal `created` outputs.
