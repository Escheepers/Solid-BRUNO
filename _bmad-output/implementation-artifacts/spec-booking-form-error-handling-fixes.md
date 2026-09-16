---
title: 'Bug fix: Booking Cancel-eligibility rule, overlap-field mapping, stale validation errors'
type: 'bugfix'
created: '2026-09-16'
status: 'done'
review_loop_iteration: 0
context: []
baseline_commit: 'e2cc6c66864afec6cbb4be3b3c6a41ae6c3d959d'
---

<frozen-after-approval reason="human-owned intent — do not modify unless human renegotiates">

## Intent

**Problem:** A QA pass found three related, user-visible bugs. (1) `Booking.Cancel()` only checks `Status`/`EndDate <= today` — it has no check on `StartDate` at all, so a booking that has already started (car already picked up, `StartDate` in the past, `EndDate` still in the future) is currently considered cancellable. The frontend's `isCancellable()` (`endDate >= startOfToday()`) also disagrees with even that existing backend boundary. Confirmed with the human: a booking should only be cancellable while it is still genuinely in the future — once it has started, "Cancel" no longer makes sense (matches `epics.md`'s own "I want to cancel a **future**, active booking" framing). (2) `BookingFormModal`'s 409 handling assumes only one possible domain-rule violation (soft-deleted vehicle) and hardcodes every non-validation 409 onto the `vehicleId` field — true when written (Story 4.1), false since Story 4.2 added the Overlap rule, so an overlap conflict now renders under the wrong field. (3) All three form modals (Vehicle/Customer/Booking) only clear a displayed server-side field error at form-open and at submit-start — never reactively when the user actually corrects the field — so a stale error stays visible even after the field becomes valid, until the next submit.

**Approach:** Replace `Booking.Cancel()`'s timing guard with a `StartDate > today` check (strictly stronger than, and replacing, the old `EndDate <= today` check — `EndDate > StartDate` is already an always-enforced invariant, so `StartDate > today` implies `EndDate > today` too); update the frontend's `isCancellable()` to match exactly. Map the Overlap rule onto the date fields instead of the vehicle field. Clear a field's server-side error the moment its own value changes, in all three form modals.

## Boundaries & Constraints

**Always:**
- `Booking.Cancel()`'s ineligibility check becomes `Status == BookingStatus.Completed || StartDate <= today` (replacing the old `EndDate <= today`) — a booking is cancellable only while it is `Active` and has not yet started. The thrown message becomes "Cannot cancel — booking has already started." for this branch (accurate for a mid-rental booking, an ended-but-not-yet-swept booking, and a genuinely `Completed` booking alike — all three have necessarily already started). The already-`Cancelled` branch and its message are unchanged.
- `isCancellable()` becomes `booking.status === 'Active' && booking.startDate > startOfToday()` — the exact frontend mirror of the new backend rule (`Active` AND not-yet-started).
- `BookingFormModal.applyError()`'s fallback for a 409 without an `errors` dictionary now distinguishes by the violation's rule name (`fieldFromType(error.type)`, already used elsewhere in this codebase): `isDeleted` still maps to `vehicleId` (unchanged); `overlap` maps to `endDate` (the AC says "under the date fields" — `endDate` is the trailing element of that field pair, consistent with how the existing `EndDate <= StartDate` 400 already targets `endDate`).
- In each of `VehicleFormModal`/`CustomerFormModal`/`BookingFormModal`, when a form control's own value changes (a real edit, not a symptom of `patchValue`-during-reset), clear that field's entry from `serverFieldErrors` if present. Do not touch `serverErrorMessage` (the top-of-form banner) — only field-level errors are addressed by this bug.
- `CancelBookingCommandHandlerTests`/domain tests covering the old `EndDate <= today` boundary are updated to the new `StartDate <= today` boundary, not deleted — the eligibility rule is still fully tested, just against the corrected condition.

**Ask First:** Nothing else expected to trigger.

**Never:** No new shared component. No change to `Booking.Complete()`'s own boundary (already correct, and independent of this change — `Complete()` still keys off `EndDate <= today`, unrelated to Cancel's new `StartDate`-based rule). No change to validation timing/debounce beyond "clear on the control's own valueChanges." No change to the optimistic-concurrency work on `CancelBookingCommandHandler` (handled separately in spec-concurrent-request-race-fixes.md, which only wraps `SaveChangesAsync`/re-invokes `Cancel()` as a black box — fully compatible with this spec's change to `Cancel()`'s internal rule).

## I/O & Edge-Case Matrix

| Scenario | Input / State | Expected Output / Behavior | Error Handling |
|----------|--------------|---------------------------|----------------|
| Booking with `StartDate` in the future | View its detail/list row | Cancel IS offered and succeeds | N/A |
| Booking with `StartDate` = today or in the past, `EndDate` still in the future (mid-rental) | View its detail/list row | Cancel is NOT offered as eligible; a direct attempt gets 409 "Cannot cancel — booking has already started." | N/A |
| Booking with `EndDate` in the past, not yet swept | Attempt to cancel | Still ineligible (subsumed by the new `StartDate <= today` check, since `StartDate < EndDate` always) | N/A |
| Create a booking overlapping an existing one | Submit | 409 renders under End Date, not Vehicle | N/A |
| A 400/409 field error is shown, user then corrects that field's value | Field becomes valid | The stale error clears immediately, before any resubmit | N/A |
| A 400/409 field error is shown, user edits a DIFFERENT field | Other field changes | The original field's error stays displayed (only the changed field's own error clears) | N/A |

</frozen-after-approval>

## Code Map

- `src/BrunoVehicleHire.Domain/Booking.cs` -- modify -- `Cancel()` (`:115-133`): replace `EndDate <= today` with `StartDate <= today`; update the thrown message for that branch
- `tests/BrunoVehicleHire.Domain.Tests/BookingTests.cs` (or wherever `Cancel()` is unit-tested) -- modify -- update the boundary test fixtures/assertions to the new `StartDate`-based rule; add a case for a mid-rental booking (`StartDate` in the past, `EndDate` in the future) now correctly rejected
- `frontend/src/app/features/bookings/models/booking.ts` -- modify -- `isCancellable` (`:90`): `booking.status === 'Active' && booking.startDate > startOfToday()`
- `frontend/src/app/features/bookings/booking-form-modal.ts` -- modify -- `applyError()`'s fallback branch (`:366`, currently `this.serverFieldErrors.set({ vehicleId: error.detail })`) branches on `fieldFromType(error.type)`: `'isDeleted'` → `vehicleId`, `'overlap'` → `endDate`; add the reactive clear-on-change (subscribe to `this.form.valueChanges` or per-control `valueChanges`, clearing only the changed control's `serverFieldErrors` entry)
- `frontend/src/app/features/vehicles/vehicle-form-modal.ts` -- modify -- same reactive clear-on-change addition, mirroring `booking-form-modal.ts`'s new logic exactly
- `frontend/src/app/features/customers/customer-form-modal.ts` -- modify -- same reactive clear-on-change addition
- `frontend/src/app/features/bookings/bookings-page.spec.ts`, `booking-detail-page.spec.ts` -- modify -- any test asserting Cancel-eligibility needs its fixtures/expectations updated to the new `StartDate`-based rule
- `frontend/src/app/features/bookings/booking-form-modal.spec.ts`, `vehicle-form-modal.spec.ts`, `customer-form-modal.spec.ts` -- modify -- add failing tests first for the overlap-field-mapping fix and the reactive-clear-on-change behavior in each of the three modals

## Tasks & Acceptance

**Execution (TDD throughout):**
- [x] Fix `Booking.Cancel()`'s eligibility rule (backend); update/add domain tests first
- [x] Fix `isCancellable`'s rule to match (frontend); update/add tests
- [x] Fix `BookingFormModal`'s Overlap→`endDate` mapping; add a failing test first reproducing the wrong-field bug
- [x] Add reactive clear-on-change to all three form modals; add a failing test first per modal
- [x] Full `dotnet test` and `ng test --coverage`, confirm nothing regressed

**Acceptance Criteria:**
- [x] Given a booking whose `StartDate` is today or in the past, when checked for cancel-eligibility (frontend) or actually cancelled (backend), then it is NOT eligible / receives 409 "Cannot cancel — booking has already started."
- [x] Given a booking whose `StartDate` is still in the future, when cancelled, then it succeeds exactly as before
- [x] Given a new booking that overlaps an existing one, when the 409 is received, then the error renders under End Date, not Vehicle
- [x] Given any of the three forms showing a field-level error, when the user corrects that field to a valid value, then the error clears immediately without needing to resubmit

## Spec Change Log

## Design Notes

**Why `StartDate <= today` fully replaces `EndDate <= today` rather than adding to it:** `CreateBookingCommandValidator` already strictly enforces `EndDate > StartDate` for every booking that exists — so `StartDate > today` mathematically guarantees `EndDate > today` too. The new, stricter condition is a strict superset of ineligibility versus the old one; no booking that was correctly ineligible before becomes eligible now, and the mid-rental case (previously and incorrectly eligible) becomes correctly ineligible.

**Why one shared message ("has already started") for all three now-ineligible sub-cases (mid-rental, ended-but-unswept, genuinely Completed):** each of the three is, factually, a booking whose start has already passed — a single accurate message is simpler and no less correct than trying to distinguish them, and avoids inventing new wording for a distinction the user doesn't need to act differently on (in every case, the booking simply can't be cancelled anymore).

**Why `endDate`, not `startDate`, for the Overlap error:** the AC's own wording ("under the date fields," plural) doesn't name one specifically. `endDate` is chosen as the trailing field of the pair — consistent with how the existing `EndDate <= StartDate` 400 already targets `endDate`, so both date-range-related errors land in the same place a user would expect to look.

## Verification

Independently reproduced (not just re-reading the implementer's report), per this project's standing verification rule.

**Commands:**
- `dotnet build` + `dotnet test BrunoVehicleHire.sln` (own runs, twice) — **426/426 passing** (Domain 85, Application 146, Infrastructure 4, Api 23, Integration 168). One transient failure (`CustomerLoggingPiiTests`, unrelated to this spec) on the first full-suite run was confirmed flaky by re-running it in isolation (passed) and re-running the full suite again (clean) — root-caused to heavy concurrent Docker/Testcontainers load from three parallel implementation agents all running integration suites at once, not a real regression.
- `ng test --coverage` (own run) — **373/373 passing**.

**Code read (all diffed files):** `Booking.cs`'s `Cancel()` (confirmed `StartDate <= today` correctly replaces `EndDate <= today`, message updated, doc comment accurate), `booking.ts`'s `isCancellable` (exact frontend mirror), `booking-form-modal.ts`'s `applyError()` (confirmed `fieldFromType(error.type)` branches `isDeleted`→`vehicleId`/`overlap`→`endDate`, with a sensible fallback to the top-of-form banner for any unrecognized rule name — a defensive addition beyond the spec's literal text, in keeping with this codebase's established style), the `wireClearFieldErrorOnChange()` addition mirrored identically across `booking-form-modal.ts`/`vehicle-form-modal.ts`/`customer-form-modal.ts`.
- **Live browser verification (real backend + real seeded Postgres, independently reproduced):** created a genuine mid-rental test booking (`StartDate` yesterday, `EndDate` in 4 days) via direct API call; confirmed its detail page renders no Cancel action at all; confirmed a direct `POST .../cancel` against it returns `409` with the exact new message `"Cannot cancel — booking has already started."`. Separately, created an overlapping booking against a vehicle with an existing Active booking through the real UI (combobox pickers, real keyboard selection) and confirmed via direct DOM inspection that the resulting 409 message rendered under the End Date field's own error region (`app-input-1-error`), with the Vehicle field's error region (`booking-vehicle-error`) empty — confirming the field-mapping fix, not just trusting the screenshot.
- **Cleanup:** the mid-rental test booking cannot be cancelled (that's the fix being proven) or hard-deleted (bookings are never physically deletable, AD-16) — it remains `Active` in the dev DB and will be automatically completed by the sweep service once its `EndDate` (2026-09-20) passes, consistent with how this exact situation was already handled earlier in this session.
- **Shared-file reconciliation confirmed:** `tests/BrunoVehicleHire.Integration.Tests/BookingsEndpointTests.cs` was touched by both this spec (message-text corrections) and spec-concurrent-request-race-fixes.md (a new concurrent-Cancel test) — verified both sets of changes are present together and the full file's tests pass (29/29).

## Suggested Review Order

1. `src/BrunoVehicleHire.Domain/Booking.cs` — the corrected `Cancel()` eligibility rule
2. `frontend/src/app/features/bookings/models/booking.ts` — the matching frontend `isCancellable` rule
3. `frontend/src/app/features/bookings/booking-form-modal.ts` — the Overlap→`endDate` mapping fix and the `wireClearFieldErrorOnChange` addition
4. `frontend/src/app/features/vehicles/vehicle-form-modal.ts` and `frontend/src/app/features/customers/customer-form-modal.ts` — the same reactive-clear addition mirrored
5. `tests/BrunoVehicleHire.Domain.Tests/BookingTests.cs` — the updated/new boundary tests, including the mid-rental case
