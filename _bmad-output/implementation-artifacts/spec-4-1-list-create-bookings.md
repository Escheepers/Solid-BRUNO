---
title: 'Story 4.1: List & Create Bookings'
type: 'feature'
created: '2026-09-14'
status: 'done'
review_loop_iteration: 0
context: []
baseline_commit: '28f2bcc4cd3420f699cc80402faee0c14127649a'
---

<frozen-after-approval reason="human-owned intent — do not modify unless human renegotiates">

## Intent

**Problem:** `Booking` exists only as a minimal entity (Story 3.3, just enough to construct a row and check "has this customer got any bookings") — Epic 4's real Booking feature, opening with the ability to list and create one, doesn't exist yet. The `BookingsPage` route is still Story 1.6's "Coming soon" placeholder.

**Approach:** Add a real `GetBookingsQuery`/`CreateBookingCommand` pair, a new `Badge` component (Active/Completed/Cancelled, first real use), and a `BookingFormModal` with a vehicle/customer picker, live-computed Total Price, and a nested "+ New Customer" modal — replacing the placeholder `BookingsPage`.

**Scope decisions — flagged for approval:**
1. **`EndDate <= StartDate` is 400, not 409, per this story's own AC** — this duplicates `Booking.Create`'s already-shipped (Story 3.3) domain invariant into a new `CreateBookingCommandValidator` cross-field rule. The domain check stays as-is (defense-in-depth for any other caller); via this HTTP path FluentValidation's 400 wins the race in the pipeline, satisfying the AC literally without touching Story 3.3's tested code.
2. **A soft-deleted vehicle gets an explicit 409** ("This vehicle is not available") via `GetByIdIncludingSoftDeletedAsync` + an explicit `IsDeleted` check in the handler (AD-8) — not a generic 404. An inactive/anonymized **customer** gets a plain 404 via the existing filtered `GetByIdAsync` (asymmetric from Vehicle by design — only Vehicle is named in this story's AC).
3. **No free-text search for the Bookings list yet** — nothing in this story's AC names a field to search (unlike Vehicle's Make/Model or Customer's name); `GetBookingsQuery` takes only `Page`/`PageSize`.
4. **`GetBookingsQueryHandler` joins Vehicle and Customer via `.IgnoreQueryFilters()` on both sides from day one** (AD-13) — not deferred to Story 4.5 — so a booking referencing an already-soft-deleted vehicle or an already-soft-deleted/anonymized customer still renders correctly in this list today, rather than silently dropping the row via EF Core's global query filter on the join.
5. **No Actions column in this story's list** — View (4.5) and Cancel (4.3) don't exist yet; the list is read + create only, mirroring how Story 1.7's Vehicle list shipped actionless before 2.1-2.4 added rows incrementally.
6. **Vehicle/customer pickers are plain native `<select>` elements bound via `ReactiveFormsModule`**, populated by the existing paginated `useVehiclesQuery`/`useCustomersQuery` with a generously large `pageSize` — no new shared `Select` component yet (first consumer, YAGNI) and no new unpaginated "all active" endpoint (reasonable at this assessment's data volumes).
7. **`CustomerFormModal` gains an optional `created = output<Customer>()`**, emitted only on a successful create (never update) — a minimal, backward-compatible addition so `BookingFormModal` can auto-select the customer it just created inline.

## Boundaries & Constraints

**Always:**
- `TotalPrice = Vehicle.DailyRate × (EndDate − StartDate in days)`, computed server-side in `CreateBookingCommandHandler` — never trusted from the request.
- `Booking.Create(...)` is called exactly as it already exists (Story 3.3) — unmodified.
- The Booking-create Modal's Total Price updates live in an `aria-live="polite"` region whenever vehicle or either date changes.
- "+ New Customer" opens `CustomerFormModal` nested inside the open `BookingFormModal` (one level deep); creating a customer never loses the vehicle/dates already entered underneath.
- `BookingDto` carries the display fields needed for the list (`VehicleMake`/`Model`/`RegistrationNumber`, `CustomerFirstName`/`LastName`/`IsAnonymized`) — no navigation properties on the `Booking` domain entity itself (AD-1: aggregates reference each other only by Id).

**Ask First:** Nothing else expected to trigger.

**Never:** No Edit-a-Booking UI (not in this project's 27-story scope). No overlap check yet (Story 4.2). No Cancel/View actions yet.

## I/O & Edge-Case Matrix

| Scenario | Input / State | Expected Output / Behavior | Error Handling |
|----------|--------------|---------------------------|----------------|
| Create with valid vehicle/customer/dates | Active vehicle, active customer, `EndDate > StartDate` | `201`; `Status: Active`; `TotalPrice` correct; appears in list | N/A |
| `EndDate <= StartDate` | Equal or reversed dates | `400` | Inline error under the date fields |
| Soft-deleted vehicle selected | Stale picker / race | `409`, detail "This vehicle is not available." | Inline error under the vehicle field |
| Nonexistent/inactive customer | Stale picker / race | `404` | Falls through to `ServerError` |
| List with a booking whose vehicle/customer is now inactive | `showInactive` N/A — always shown | Row still renders (vehicle name / "Customer (anonymized)") | N/A |

## Code Map

**Backend:**
- `src/BrunoVehicleHire.Application/Bookings/Dtos/BookingDto.cs` -- new -- see Boundaries; `FromDomain`-style static mapper taking `(Booking, Vehicle, Customer)`
- `src/BrunoVehicleHire.Application/Bookings/Commands/CreateBookingCommand.cs`, `CreateBookingCommandValidator.cs`, `CreateBookingCommandHandler.cs` -- new -- validator per Scope decision 1; handler per Scope decision 2, uses `IVehicleRepository.GetByIdIncludingSoftDeletedAsync`/`ICustomerRepository.GetByIdAsync` (both already exist)
- `src/BrunoVehicleHire.Application/Bookings/Queries/GetBookingsQuery.cs`, `GetBookingsQueryValidator.cs`, `GetBookingsQueryHandler.cs` -- new -- mirrors `GetVehiclesQuery*`'s shape minus `search`/`includeInactive` (Scope decision 3)
- `src/BrunoVehicleHire.Application/Bookings/IBookingRepository.cs` -- modify -- add `AddAsync(Booking, ct)` and `GetPagedAsync(page, pageSize, ct)` returning `(IReadOnlyList<(Booking, Vehicle, Customer)>, int TotalCount)`
- `src/BrunoVehicleHire.Infrastructure/Repositories/BookingRepository.cs` -- modify -- implement both; `GetPagedAsync` joins `dbContext.Vehicles.IgnoreQueryFilters()`/`dbContext.Customers.IgnoreQueryFilters()` on the FK ids (Scope decision 4)
- `src/BrunoVehicleHire.Api/Controllers/BookingsController.cs` -- new -- `[HttpGet]`, `[HttpPost]`, mirrors `VehiclesController`'s shape
- `tests/BrunoVehicleHire.Domain.Tests/BookingTests.cs`, `tests/BrunoVehicleHire.Application.Tests/Bookings/*`, `tests/BrunoVehicleHire.Integration.Tests/BookingsEndpointTests.cs` -- new -- every I/O-matrix row, TDD throughout

**Frontend:**
- `frontend/src/app/shared/badge/badge.ts` (+`.html`, `.spec.ts`) -- new -- `status: input.required<'Active'|'Completed'|'Cancelled'>()`, dot-first pill per `DESIGN.md`'s three `badge-*` tokens
- `frontend/src/app/features/customers/customer-form-modal.ts` -- modify -- add `created = output<Customer>()` (Scope decision 7)
- `frontend/src/app/features/bookings/models/booking.ts`, `booking-formatters.ts` -- new -- mirrors `features/customers`'s exact shape (own duplicated currency/date formatters, no cross-feature import)
- `frontend/src/app/features/bookings/bookings.service.ts` -- new -- `useBookingsQuery`, `useCreateBookingMutation`, mirrors `customers.service.ts`
- `frontend/src/app/features/bookings/booking-form-modal.ts` (+`.html`) -- new -- mirrors `CustomerFormModal`'s Reactive Forms + discard-guard shape; native `<select>`s for vehicle/customer (Scope decision 6); nested `<app-customer-form-modal>` wired to the new `created` output; live Total Price computed from the form's own value changes
- `frontend/src/app/features/bookings/bookings-page.ts` (+`.html`) -- replace placeholder -- `DataTable` with Vehicle/Customer/Start/End/Total/Status columns (no actions column, Scope decision 5), `Badge` for Status

## Tasks & Acceptance

**Execution — backend (TDD throughout):**
- [x] `BookingDto`, `CreateBookingCommand`/`Validator`/`Handler`, `GetBookingsQuery`/`Validator`/`Handler` -- write failing tests first
- [x] `IBookingRepository`/`BookingRepository` additions, including the dual-`IgnoreQueryFilters()` join
- [x] `BookingsController`
- [x] `BookingsEndpointTests` -- every I/O-matrix row
- [x] Full `dotnet test`; `ArchitectureFitnessTests`; force `dotnet restore`, check `NU1903`; SOLID/DRY/YAGNI self-check -- confirmed independently: 335/335 passing, 0 vulnerabilities

**Execution — frontend (TDD throughout):**
- [x] `Badge` -- write failing tests first
- [x] `CustomerFormModal`'s `created` output -- write failing test first, confirm existing tests unaffected -- confirmed via diff review: pre-existing update-path behavior untouched, only the create branch gained the emit
- [x] `bookings.service.ts`, `booking-form-modal.ts` (incl. nested-modal + live-price + auto-select-new-customer), `bookings-page.ts` -- write failing tests first
- [x] `ng build` + `ng test --coverage --watch=false` clean; `npm audit` clean; SOLID/DRY/YAGNI self-check -- confirmed independently: 262 tests, exit 0, 97.64% statement coverage, 0 vulnerabilities

**Acceptance Criteria (epics.md, verbatim intent, adapted per Scope decisions above):**
- Given bookings exist, the Bookings page shows a paginated `DataTable` with a Status `Badge`
- Given a valid vehicle/customer/date range, submitting creates an Active booking with the correct `TotalPrice`
- Given "+ New Customer" from the open Booking-create modal, the nested Customer-create modal succeeds without losing the Booking form's vehicle/dates, and the new customer becomes selected
- Given the Total Price field, it recomputes live in an `aria-live="polite"` region as vehicle/dates change

## Spec Change Log

- **Backend implementation**: `src/BrunoVehicleHire.Api/Program.cs` (not in this spec's Code Map) needed `AddJsonOptions` with a `JsonStringEnumConverter` added to `AddControllers()`. `Booking.Status` is the first enum ever exposed on the API's wire shape; without this it would serialize as its numeric ordinal instead of `"Active"`/`"Completed"`/`"Cancelled"`, contradicting the architecture spine's "enums serialize as strings, never ints" convention and this story's own AC (`Status: Active` in the response). A necessary consequence of exposing the first enum-typed DTO field, not a scope change.
- **Frontend implementation**: three additions not named in this spec's Frontend Code Map, all necessary consequences of what the Code Map does specify:
  - `frontend/src/app/core/models/booking-dto.ts` -- the raw wire DTO, required by the DTO/view-model separation convention `models/booking.ts` (which the Code Map does name) depends on, mirroring Vehicle/Customer's existing pattern.
  - `frontend/src/app/shared/data-table/data-table.ts`/`.html` -- added an optional `ColumnDef.cellTemplate?: TemplateRef<{$implicit: T}>`, additive-only (every pre-existing column keeps rendering via `cell()`). Needed because the Status column renders a real `Badge` component, which the existing string-only `cell()` render function cannot produce. This is the exact "revisit" trigger `spec-1-7`'s own Design Notes on `ColumnDef.cell` anticipated.
  - `frontend/src/app/shared/modal/modal.ts` -- added `event.stopPropagation()` in `onKeydown`, mirroring `ConfirmDialog.onKeydown`'s existing precedent. Needed because `BookingFormModal`'s nested `CustomerFormModal` is the app's first Modal-in-Modal composition; without it, the inner modal's Escape/Tab would bubble to the outer modal's own listener.

## Design Notes

**Why the domain's 3.3-era `EndDate>StartDate` check isn't removed:** it remains the single source of truth for `Booking`'s own validity for every caller of `Booking.Create` (tests, future commands), not just this HTTP path — the new validator is boundary-level defense-in-depth, not a replacement.

</frozen-after-approval>

## Verification

Independently re-verified after implementation (not just the implementer's self-report):

**Backend:** `git status --short` confirmed the exact expected file set; `dotnet build BrunoVehicleHire.sln` clean (0 warnings, 0 errors); full `dotnet test` 335/335 passing (Domain 67, Application 120, Infrastructure 4, Api 23, Integration 121), up from 307 before this story; `dotnet restore --force` showed no `NU1903` advisories. Read `CreateBookingCommandHandler.cs`, `CreateBookingCommandValidator.cs`, and `BookingRepository.cs` directly and confirmed all five Scope decisions were implemented exactly as specified, including the subtle reasoning that `GetPagedAsync`'s total count is correctly taken from the `Bookings` table alone (never the join) since the FK `DeleteBehavior.Restrict` constraints guarantee the inner join can never drop a row. Listed the integration test method names directly and confirmed every I/O-matrix row has a dedicated, real Testcontainers-backed test, including two proactive regression tests for the day-one `IgnoreQueryFilters()` fix (a booking whose vehicle is later soft-deleted, and whose customer is later anonymized) that the AC doesn't explicitly demand until Story 4.5.

**Frontend:** `git status --short` confirmed the exact expected file set; `ng build` clean (0 errors, 615.73 kB / budget 650 kB); `ng test --coverage --watch=false` exit 0, 97.64% statement / 93.67% branch / 96.15% function / 97.55% line coverage; `npm audit` 0 vulnerabilities -- all reproduced independently. Read `booking-form-modal.ts`, `bookings-page.ts`/`.html`, `badge.ts`, `data-table.ts`/`.html`, `modal.ts`, and the exact diff to `customer-form-modal.ts` directly and confirmed: the `created` output diff is minimal and correctly scoped to only the create branch (the update branch is untouched); `cellTemplate` correctly falls back to the plain-text `cell()` render for every pre-existing column; the client-side `daysBetween` UTC-midnight-anchored day computation is a correct frontend equivalent of the backend's `DateOnly.DayNumber` subtraction; the nested-modal `stopPropagation` fix directly mirrors `ConfirmDialog`'s already-proven precedent rather than inventing a new mechanism.

**SOLID/DRY/YAGNI:** confirmed via direct code reading -- `Badge` knows nothing about Booking beyond a status string (SRP); `BookingFormModal` is a structural mirror of `CustomerFormModal`/`VehicleFormModal`'s proven Reactive Forms + discard-guard shape, not a reinvention (DRY); both `DataTable.cellTemplate` and `Modal`'s `stopPropagation` fix are minimal, single-purpose, backward-compatible additions built exactly when their first real consumer needed them, not speculative (YAGNI). No shared `Select` component or new "all active" endpoint was built, per Scope decision 6.

**Live end-to-end pass (performed after the above, against the real API + Postgres + browser):** started the real backend (`dotnet run`) and the Angular dev server, created a real Vehicle and, via the nested flow, a real Customer, and completed a real booking end to end. This caught a genuine bug the mocked-service unit tests could not: `PICKER_PAGE_SIZE` was `200`, but `GetVehiclesQueryValidator`/`GetCustomersQueryValidator` both bound `PageSize` to `[1, 100]` (AD-10) — every picker request 400'd, silently leaving both `<select>`s empty with no visible error. Fixed by lowering `PICKER_PAGE_SIZE` to `100` in `booking-form-modal.ts`, with `booking-form-modal.spec.ts`'s hardcoded `pageSize=200` mock expectations updated to match (a test-file correction, not a behavior change — the corrected value is what production code now legitimately sends). Re-verified after the fix: `ng build` clean, `ng test --coverage --watch=false` 262/262 passing (coverage unchanged), and a full live re-run confirmed the vehicle/customer pickers populate, the nested "+ New Customer" modal opens on top without losing the Booking form's vehicle/dates, the newly created customer is auto-selected, Total Price computes correctly (R600/day × 3 days = R1,800.00), and the created booking appears in the list with a green "Active" `Badge`. The dev database was left empty afterward (the test Booking/Customer/Vehicle rows were removed directly via SQL, in FK-safe order, since Story 4.1 itself provides no Cancel/Delete path for a Booking).

## Suggested Review Order

1. `src/BrunoVehicleHire.Application/Bookings/Commands/CreateBookingCommandHandler.cs` -- the vehicle-409/customer-404 asymmetry
2. `src/BrunoVehicleHire.Infrastructure/Repositories/BookingRepository.cs` -- the dual-`IgnoreQueryFilters()` join
3. `tests/BrunoVehicleHire.Integration.Tests/BookingsEndpointTests.cs` -- the day-one anonymized/soft-deleted regression tests
4. `frontend/src/app/features/bookings/booking-form-modal.ts` -- the nested modal, live price, and auto-select-new-customer flow
5. `frontend/src/app/features/customers/customer-form-modal.ts` -- the `created` output diff
6. `frontend/src/app/shared/data-table/data-table.ts`/`.html` -- `cellTemplate`
7. `frontend/src/app/shared/modal/modal.ts` -- the nested-modal `stopPropagation` fix
