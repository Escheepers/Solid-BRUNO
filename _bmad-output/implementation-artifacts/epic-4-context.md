# Epic 4 Context: Booking Management & Business Rules

<!-- Compiled from planning artifacts. Edit freely. Regenerate with compile-epic-context if planning docs change. -->

## Goal

Let staff create, view, and cancel bookings linking one vehicle and one customer over a date range, with every cross-entity business rule enforced via a specific, actionable inline error rather than a generic failure: no overlapping bookings for the same vehicle, no booking a soft-deleted vehicle, `EndDate > StartDate`, and only future/Active bookings can be cancelled (never physically deleted). Booking Status (Active/Completed/Cancelled) is system-managed — Completed is derived by a background sweep, never user-settable.

## Stories

- Story 4.1: List & Create Bookings
- Story 4.2: Booking Overlap Prevention (Application + Database)
- Story 4.3: Cancel a Booking
- Story 4.4: Automatic Booking Completion
- Story 4.5: Booking Detail View

## Requirements & Constraints

- FR3: Booking create/view/cancel, overlap + date + soft-deleted-vehicle rules, Status tracking.
- Booking cannot overlap another booking for the same vehicle; `EndDate` must be greater than `StartDate`; a booking's vehicle must not be soft-deleted at creation time; a customer cannot be deleted while any booking (of any status) references it (already enforced by Story 3.3).
- "Delete" is realized as **Cancel** only — no booking row is ever physically removed via the API; only a future, `Active` booking can be cancelled.
- This project's 27-story scope has no "Edit a Booking" story — `EXPERIENCE.md`'s Flow 1 narrative mentions "extending" a booking's end date as an illustrative example of the overlap-rejection error message, not a scoped feature; only Create (4.1), Cancel (4.3), and the system sweep (4.4) ever change a booking's state.

## Technical Decisions

- `Booking` schema (already created by Story 3.3's migration): Id, VehicleId (FK→Vehicle), CustomerId (FK→Customer), StartDate/EndDate (`DateOnly`, AD-9), TotalPrice, Status (enum, string-serialized), CreatedDate. `Booking.Create(...)` already validates only `EndDate > StartDate`; `Status` always starts `Active`. FK relationships already use `DeleteBehavior.Restrict`.
- Overlap prevention is two-layer (AD-7): an application-level `DateRange.Overlaps` check (pure domain method, half-open interval — touching endpoints are NOT an overlap) plus a database-level Postgres `EXCLUDE USING GIST` constraint (`vehicle_id WITH =, daterange(...) WITH && WHERE status != 'Cancelled'`, requires the `btree_gist` extension) as a race-condition backstop. This story (4.2) owns adding that constraint via its own migration; Story 4.1 does not implement overlap checking at all.
- A soft-deleted vehicle must be rejected with `409` and an inline "This vehicle is not available" message — the handler checks `Vehicle.IsDeleted` explicitly (AD-8), never a generic not-found.
- `TotalPrice` = `Vehicle.DailyRate` × duration in days, computed server-side at creation (the frontend also live-computes it for display before submit).
- `Booking.Cancel()` transitions `Status: Active → Cancelled`; blocked with `409` if already `Completed`/`Cancelled` or if `EndDate` is in the past (even if not yet swept) — the cancel guard is a pure domain-rule check, not dependent on the sweep having run.
- `BookingCompletionSweepService` (a `BackgroundService`, AD-17) dispatches a `CompleteBookingCommand` via MediatR per past-`EndDate` Active booking on a timer; `Booking.Complete()` is the only path that ever sets `Status: Completed` — no Query handler ever writes.
- Booking→Customer/Vehicle joins on the detail view must call `.IgnoreQueryFilters()` on the Customer side so an anonymized or soft-deleted customer/vehicle still resolves on historical records (AD-13), rendering as "Customer (anonymized)" using the same treatment established in Epic 3.

## UX & Interaction Patterns

- Bookings list reuses the existing `DataTable`/pagination/search pattern; Status renders via a new `Badge` component (dot-first, three variants: Active/Completed/Cancelled), introduced in this epic.
- "+ New Booking" opens a `Modal` with vehicle picker, customer picker, date range, and a live-computed Total Price in an `aria-live="polite"` region that updates as vehicle/dates change.
- "+ New Customer" is launchable inline from within the open Booking-create modal (nested one level deep only, per the IA rule) — creating the customer must not lose the vehicle/dates already entered in the Booking form underneath.
- Cancel uses the neutral `ConfirmDialog` (states the booking stays in records as Cancelled and that the action itself is not reversible).
- Every business-rule/validation error (400 or 409) renders via the `inline-error` component directly under the relevant field, never a top-of-form banner.

## Cross-Story Dependencies

- Story 4.1 depends on Epic 2's Vehicle list/DailyRate and Epic 3's Customer list/create (including its "+ New Customer" modal) both already existing and reusable as-is.
- Story 4.2 depends on 4.1's create flow already existing to extend with the overlap check.
- Story 4.3 (Cancel) and 4.5 (Detail) both depend on 4.1's list/create existing first.
- Story 4.5 explicitly completes Story 2.5's previously-stubbed Vehicle-detail booking-history section, once real `Bookings` data exists.
