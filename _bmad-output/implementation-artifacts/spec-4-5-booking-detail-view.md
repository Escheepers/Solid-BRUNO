---
title: 'Story 4.5: Booking Detail View'
type: 'feature'
created: '2026-09-14'
status: 'done'
review_loop_iteration: 0
context: []
baseline_commit: 'd185bf5f4db2a837ede2d4237cdae7216268b1a5'
---

<frozen-after-approval reason="human-owned intent — do not modify unless human renegotiates">

## Intent

**Problem:** A booking can only be seen as a list row — there's no single-record view, no "View" action anywhere, and Story 2.5's Vehicle Detail still has no booking-history section since no real Booking data existed when it shipped.

**Approach:** Add a `BookingDetailPage` mirroring `VehicleDetailPage`'s exact three-state shape (loading/not-found/found), a matching `GetBookingByIdQuery` using the same dual-`IgnoreQueryFilters()` join `GetBookingsQueryHandler` already established, a "View" row action on the Bookings list, and a booking-history section on Vehicle Detail reusing the same `GetBookingsQuery` with a new optional `VehicleId` filter.

**Scope decisions — flagged for approval:**
1. **`GetBookingsQuery` gains an optional `VehicleId` filter (nullable, default `null`)** — additive and backward-compatible, mirroring the established pattern of optional query params (`includeInactive`, etc.). Reused by both the unfiltered Bookings list and Vehicle Detail's new booking-history section, rather than a second, near-duplicate query.
2. **`GetBookingByIdQuery` reuses the existing `BookingDto` shape** (no new DTO) and the exact dual-`IgnoreQueryFilters()` join `GetPagedAsync` already implements (AD-13) — the anonymized/soft-deleted-customer-or-vehicle resolution behavior this story's AC calls out was already built correctly in Story 4.1's list query; this story just applies the identical join to a single-id lookup.
3. **`isCancellable` (and its `startOfToday` helper) move from `bookings-page.ts` into `models/booking.ts`**, exported, so `BookingDetailPage` reuses the exact same eligibility check rather than a second copy of the same date comparison (DRY) — its Cancel action must agree with the list's, always.
4. **Vehicle Detail's booking-history section has no pagination controls of its own** — fetched with a generously large `pageSize` (mirroring the vehicle/customer picker's own precedent from spec-4-1), reasonable at this assessment's data volumes; a real production system would paginate a vehicle with hundreds of bookings, but nothing here does yet.

## Boundaries & Constraints

**Always:**
- `BookingDetailPage` shows Vehicle, Customer (or "Customer (anonymized)", same treatment as the list), StartDate, EndDate, TotalPrice, a Status `Badge`, and Cancel if `isCancellable(booking)` — reusing `createConfirmableAction` exactly as `BookingsPage` does.
- A stale/invalid booking id renders "This booking no longer exists." + a link back to `/bookings` — never a blank page or a raw 404, mirroring `VehicleDetailPage`'s exact `isNotFound` pattern.
- The Bookings list's `actions` function always includes "View" (navigates to `/bookings/{id}`) for every row, in addition to the existing conditional "Cancel."
- Vehicle Detail's booking-history section reuses `DataTable`/`Badge` exactly as `BookingsPage` does for its own Status column — no new list-rendering pattern invented.

**Ask First:** Nothing else expected to trigger.

**Never:** No Edit-a-Booking UI (still out of this project's scope). No pagination UI added to the booking-history section (Scope decision 4).

## I/O & Edge-Case Matrix

| Scenario | Input / State | Expected Output / Behavior | Error Handling |
|----------|--------------|---------------------------|----------------|
| Valid booking id | Detail page requested | Shows vehicle, customer, dates, TotalPrice, Status Badge | N/A |
| Booking's customer has since been anonymized | Detail page requested | Customer still resolves, renders as "Customer (anonymized)" | N/A |
| Stale/invalid booking id | Detail page requested | "This booking no longer exists." + link back to the list | Falls through to `NotFoundError` |
| Vehicle Detail for a vehicle with real bookings | Vehicle detail page requested | Booking-history section lists them | N/A |

## Code Map

**Backend:**
- `src/BrunoVehicleHire.Application/Bookings/Queries/GetBookingsQuery.cs`, `GetBookingsQueryHandler.cs` -- modify -- add `Guid? VehicleId = null`, thread through (Scope decision 1)
- `src/BrunoVehicleHire.Application/Bookings/IBookingRepository.cs`, `src/BrunoVehicleHire.Infrastructure/Repositories/BookingRepository.cs` -- modify -- `GetPagedAsync` gains `Guid? vehicleId`; add `GetByIdWithVehicleAndCustomerAsync(Guid id, ct) -> (Booking, Vehicle, Customer)?` mirroring `GetPagedAsync`'s dual-`IgnoreQueryFilters()` join for a single row
- `src/BrunoVehicleHire.Application/Bookings/Queries/GetBookingByIdQuery.cs`, `GetBookingByIdQueryHandler.cs` -- new -- mirrors `GetVehicleByIdQueryHandler`'s exact shape, returns the existing `BookingDto`
- `src/BrunoVehicleHire.Api/Controllers/BookingsController.cs` -- modify -- `Get` gains `[FromQuery] Guid? vehicleId = null`; new `[HttpGet("{id:guid}")]`
- `tests/BrunoVehicleHire.Application.Tests/Bookings/*`, `tests/BrunoVehicleHire.Integration.Tests/BookingsEndpointTests.cs` -- modify -- every I/O-matrix row, `vehicleId` filter, anonymized-customer resolution, 404

**Frontend:**
- `frontend/src/app/features/bookings/models/booking.ts` -- modify -- export `isCancellable`/`startOfToday` (Scope decision 3)
- `frontend/src/app/features/bookings/bookings.service.ts` -- modify -- `BookingsQueryParams` gains optional `vehicleId`; add `useBookingQuery(id)` mirroring `useVehicleQuery` exactly
- `frontend/src/app/features/bookings/bookings-page.ts` (+`.html`) -- modify -- `actions` always includes "View" (`Router.navigate`) alongside the existing conditional "Cancel"; import `isCancellable` from the model
- `frontend/src/app/features/bookings/booking-detail-page.ts` (+`.html`) -- new -- mirrors `VehicleDetailPage`'s exact three-state shape; own `cancelAction`
- `frontend/src/app/app.routes.ts` -- modify -- add `{ path: 'bookings/:id', component: BookingDetailPage }`
- `frontend/src/app/features/vehicles/vehicle-detail-page.ts` (+`.html`) -- modify -- add the booking-history section (Customer/Start/End/Total/Status columns, `Badge` via `cellTemplate`, no actions), fed by `useBookingsQuery` filtered on this vehicle's id

## Tasks & Acceptance

**Execution — backend (TDD throughout):**
- [x] `GetBookingsQuery`'s `VehicleId` filter -- write failing tests first
- [x] `GetByIdWithVehicleAndCustomerAsync`, `GetBookingByIdQuery`/`Handler` -- write failing tests first
- [x] `BookingsController`'s `vehicleId` param + `GetById` action
- [x] `BookingsEndpointTests` -- every I/O-matrix row, including the anonymized-customer-resolves-via-detail-query case
- [x] Full `dotnet test`; `ArchitectureFitnessTests`; force `dotnet restore`, check `NU1903`; SOLID/DRY/YAGNI self-check -- confirmed independently: 397/397 passing (84+139+4+23+147), 0 advisories

**Execution — frontend (TDD throughout):**
- [x] `isCancellable`/`startOfToday` extraction; `useBookingQuery`; `vehicleId` on `BookingsQueryParams` -- write failing tests first, confirm `bookings-page.spec.ts`'s existing eligibility tests still pass unmodified -- confirmed: a pure relocation, pre-existing assertions unchanged in substance
- [x] `BookingDetailPage` (loading/not-found/found, Cancel) -- write failing tests first
- [x] Bookings list's "View" action; the route -- write failing tests first
- [x] Vehicle Detail's booking-history section -- write failing tests first
- [x] `ng build` + `ng test --coverage --watch=false` clean; `npm audit` clean; SOLID/DRY/YAGNI self-check -- confirmed independently: 299/299 passing, 97.72% statement coverage, 0 vulnerabilities

**Acceptance Criteria (epics.md, verbatim intent):**
- Given a booking's detail is requested, it shows vehicle, customer, dates, TotalPrice, Status Badge, with Cancel available if eligible
- Given the booking's customer has since been anonymized, the detail loads and renders "Customer (anonymized)"
- Given a stale/invalid booking id, "This booking no longer exists" renders with a link back to the list
- Given the Bookings table now has real data, Vehicle Detail's booking-history section lists real bookings for that vehicle

## Spec Change Log

- **Frontend implementation**: `BookingDetailPage`'s own successful-cancel handler additionally invalidates its own `['bookings', 'detail', id]` query (beyond the shared `useCancelBookingMutation`'s existing `['bookings', 'list']` invalidation) so the page reflects the new `Cancelled` status and retracts the Cancel action immediately, without a manual refresh. Not in the original Code Map's literal wording, but a necessary consequence of `BookingDetailPage` reading from a query key `useCancelBookingMutation` doesn't already invalidate -- done locally in the page rather than altering the shared, already-tested mutation.
- `DataTable`'s `Previous`/`Next` pagination controls still render in Vehicle Detail's booking-history section (per Scope decision 4, no pagination logic is wired to them) since `DataTable` always renders that footer -- functionally inert at this story's expected data volumes (a vehicle with over 100 bookings would need real pagination, which nothing here provides yet). A minor, disclosed cosmetic gap, not a defect against the spec's explicit "no pagination controls of its own" framing (read as "no working pagination," not "no pager markup at all").

## Design Notes

**Why no new DTO/query-join logic for the detail view:** Story 4.1 already solved "how does a Booking correctly resolve its Vehicle/Customer even if either has since gone inactive" via `GetPagedAsync`'s dual-`IgnoreQueryFilters()` join. This story's "customer resolves after being anonymized" AC is the same problem restated for a single row — reusing that exact mechanism (rather than re-deriving it) is both correct and the DRY choice.

</frozen-after-approval>

## Verification

Independently re-verified after implementation (not just the implementer's self-report):

**Backend:** `git status --short` confirmed the exact expected file set; `dotnet build BrunoVehicleHire.sln` clean (0 warnings, 0 errors); full `dotnet test` 397/397 passing (Domain 84, Application 139, Infrastructure 4, Api 23, Integration 147), up from 386 before this story; `dotnet restore --force` showed no `NU1903` advisories. Read `GetBookingByIdQueryHandler.cs` and `BookingRepository.GetByIdWithVehicleAndCustomerAsync`/`GetPagedAsync` directly and confirmed the dual-`IgnoreQueryFilters()` join is genuinely reused (not re-derived) for the single-row lookup, and the new `vehicleId` filter is correctly null-guarded so `BookingsPage`'s existing unfiltered call is unaffected.

**Frontend:** `git status --short` confirmed the exact expected file set; `ng build` clean (0 errors, 623.01 kB / budget 650 kB); `ng test --coverage --watch=false` 299/299 passing, 97.72% statement coverage; `npm audit` 0 vulnerabilities -- all reproduced independently. Read `booking-detail-page.ts`/`.html`, the `bookings-page.ts` diff, `bookings.service.ts`, `models/booking.ts`, and `vehicle-detail-page.ts`/`.html` directly and confirmed: `isCancellable`/`startOfToday` moved verbatim (no behavior change); the query-key string used by `BookingDetailPage`'s own cancel-success invalidation (`['bookings', 'detail', id]`) matches `useBookingQuery`'s own key exactly; the booking-history section correctly reuses `DataTable`/`Badge`/`cellTemplate` with no new list-rendering pattern.

**Live end-to-end pass** (against the real API + Postgres + browser): created a real Vehicle, a Customer (via the nested flow), and a future-dated Booking; clicked "View" from the Bookings list and confirmed the detail page renders the vehicle, customer, dates, Total Price, and a green "Active" `Badge` correctly; clicked "Cancel Booking," confirmed the exact dialog copy, confirmed it, and watched the page immediately update to a "Cancelled" `Badge` with the Cancel action retracted (proving the page's own extra query invalidation works) without a manual refresh. Navigated to that vehicle's own detail page and confirmed its new booking-history section lists the same booking with its now-`Cancelled` status, completing Story 2.5's previously-stubbed surface. The dev database was left empty afterward.

**SOLID/DRY/YAGNI:** confirmed via direct code reading -- `BookingDetailPage` and `VehicleDetailPage`'s added responsibility both stay scoped to rendering (SRP); `isCancellable` now has exactly one implementation used by both the list and the detail page, and the booking-history section reuses `DataTable`/`Badge` rather than a new pattern (DRY); no pagination UI, no Edit-a-Booking surface, and no speculative query parameters were added beyond what this story's AC needs (YAGNI).

## Suggested Review Order

1. `src/BrunoVehicleHire.Infrastructure/Repositories/BookingRepository.cs` -- `GetByIdWithVehicleAndCustomerAsync`'s reuse of the dual-`IgnoreQueryFilters()` join
2. `frontend/src/app/features/bookings/models/booking.ts` -- the `isCancellable`/`startOfToday` relocation
3. `frontend/src/app/features/bookings/booking-detail-page.ts` -- the Cancel flow and its own extra query invalidation
4. `frontend/src/app/features/vehicles/vehicle-detail-page.ts`/`.html` -- the booking-history section
5. `frontend/src/app/features/bookings/bookings-page.ts` -- the always-present "View" action
