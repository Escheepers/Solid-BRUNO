---
title: 'Story 5.1: View & Print Customer Summary'
type: 'feature'
created: '2026-09-15'
status: 'done'
review_loop_iteration: 0
context: []
baseline_commit: 'd98de668f8fec1dabece70eca4760714e9cfcc57'
---

<frozen-after-approval reason="human-owned intent — do not modify unless human renegotiates">

## Intent

**Problem:** Staff have no clean, chrome-free way to reference or hand over a customer's identity and booking history — every existing surface is the full editable admin list/detail UI, and epics.md's own "click Summary from their Customer detail" phrasing references a Customer detail page that doesn't exist anywhere in this project's scope (established in Story 3.4).

**Approach:** A dedicated, read-only `GetCustomerSummaryQuery` (its own vertical slice, since this epic is explicitly "its own distinct concern, not bundled into Customer or Booking's CRUD scope") backing a new `CustomerSummaryPage`, reachable as a permanent Customers-list row action and a link from Booking Detail's Customer field. A `@media print` rule strips all app chrome.

**Scope decisions — flagged for approval:**
1. **"Summary" is a Customers-list row action** (epics.md's "Customer detail" doesn't exist — mirrors every prior forward-reference resolution in this project) **and a link from `BookingDetailPage`'s Customer field** (which does exist, per epics.md's own second phrasing, "or from one of their Booking details").
2. **"Summary" becomes available on the anonymized-row action set too** (currently `[]` since Story 3.5 — "nothing left to do" once erased). This story's own AC explicitly requires the summary to work for an anonymized customer, so that state now gets exactly one action: `[Summary]`.
3. **A new `Application/CustomerSummaries` vertical slice** (`Queries/`+`Dtos/` only, no `Commands`/`Validators` — this is permanently read-only) rather than folding into `Customers` or `Bookings`, mirroring the architecture spine's existing `{Entity}/{Commands,Queries,Dtos}` shape for a fourth, purpose-built slice.
4. **The booking-history list on this page does NOT reuse `DataTable`** — `DataTable` always renders a pagination footer, which this story's own AC explicitly forbids ("no navigation chrome"). A plain, minimal read-only table is built instead (still reusing `Badge` for Status, which is a status indicator, not navigation chrome).
5. **The identity card shows only FirstName/LastName (or the anonymized placeholder) and Email/PhoneNumber** — no `IsDeleted`/internal admin state. This is a customer-facing reference document, not an admin view; the AC's own "identity + contact" wording never mentions soft-delete status.
6. **No dedicated Cancel/Edit/anything-mutating action anywhere on this page** — already required by "no edit buttons," stated explicitly for completeness.

## Boundaries & Constraints

**Always:**
- `GetCustomerSummaryQuery(Guid CustomerId)` uses `ICustomerRepository.GetByIdIncludingSoftDeletedAsync` (already exists, Story 3.4) so a deactivated customer's summary still renders, not just an active one; throws `NotFoundException` for a genuinely nonexistent id.
- The booking-history list is unbounded (no pagination) and fetched via a new `IBookingRepository.GetForCustomerWithVehicleAsync(customerId, ct)`, joining Vehicle via `IgnoreQueryFilters()` (AD-13) so a booking referencing an already-soft-deleted vehicle still renders its make/model/registration.
- A customer with zero bookings shows "No bookings on record for this customer" in place of the list.
- An anonymized customer's name renders as "Customer (anonymized)" in the same muted-italic `anonymized-text` treatment used everywhere else; Email/PhoneNumber still render their literal (scrubbed placeholder) values in the same treatment, mirroring Story 3.5's own per-field convention.
- A stale/invalid customer id renders "This customer no longer exists." + a link back to `/customers`, mirroring `VehicleDetailPage`/`BookingDetailPage`'s exact established pattern — never a blank page (not explicitly named by this story's AC, but a defect against this codebase's own established convention if omitted).
- `@media print` hides the global `app-top-app-bar` and every button/link on this page (including any "Back to Customers" link and an optional convenience "Print" button, if added) — leaving only the identity card and booking list in printed output.
- The loading state reuses the `Skeleton` component, mirroring `VehicleDetailPage`/`BookingDetailPage`'s exact pattern.

**Ask First:** Nothing else expected to trigger.

**Never:** No pagination, no filters, no row actions on the booking-history list. No mutating action anywhere on this page.

## I/O & Edge-Case Matrix

| Scenario | Input / State | Expected Output / Behavior | Error Handling |
|----------|--------------|---------------------------|----------------|
| Customer with booking history | Summary requested | Identity + contact + full booking list | N/A |
| Customer with zero bookings | Summary requested | Identity + contact + "No bookings on record for this customer" | N/A |
| Anonymized customer | Summary requested | Name renders "Customer (anonymized)", muted-italic | N/A |
| Stale/invalid customer id | Summary requested | "This customer no longer exists." + link back | Falls through to `NotFoundError` |
| Printed via browser print | Summary open | Top App Bar and all page buttons/links stripped | N/A |

## Code Map

**Backend:**
- `src/BrunoVehicleHire.Application/CustomerSummaries/Dtos/CustomerSummaryDto.cs` -- new -- `Id`, `FirstName`, `LastName`, `Email`, `PhoneNumber`, `IsAnonymized`, `IReadOnlyList<CustomerSummaryBookingDto> Bookings`; `CustomerSummaryBookingDto` carries `Id`, `VehicleMake`, `VehicleModel`, `VehicleRegistrationNumber`, `StartDate`, `EndDate`, `TotalPrice`, `Status`
- `src/BrunoVehicleHire.Application/CustomerSummaries/Queries/GetCustomerSummaryQuery.cs`, `GetCustomerSummaryQueryHandler.cs` -- new -- per Boundaries
- `src/BrunoVehicleHire.Application/Bookings/IBookingRepository.cs`, `src/BrunoVehicleHire.Infrastructure/Repositories/BookingRepository.cs` -- modify -- add `GetForCustomerWithVehicleAsync(customerId, ct) -> IReadOnlyList<(Booking, Vehicle)>`, Vehicle joined via `IgnoreQueryFilters()`
- `src/BrunoVehicleHire.Api/Controllers/CustomersController.cs` -- modify -- `[HttpGet("{id:guid}/summary")]`
- `tests/BrunoVehicleHire.Application.Tests/CustomerSummaries/*`, `tests/BrunoVehicleHire.Integration.Tests/CustomerSummaryEndpointTests.cs` -- new -- every I/O-matrix row

**Frontend:**
- `frontend/src/app/features/customer-summary/customer-summary-page.ts` (+`.html`) -- new -- three-state (loading/not-found/found) mirroring `VehicleDetailPage`/`BookingDetailPage`; plain read-only booking table (Scope decision 4), `Badge` for Status
- `frontend/src/app/features/customer-summary/customer-summary.service.ts` -- new -- `useCustomerSummaryQuery(id)` mirroring `useVehicleQuery`/`useBookingQuery`'s exact shape
- `frontend/src/app/app.routes.ts` -- modify -- add `{ path: 'customers/:id/summary', component: CustomerSummaryPage }`
- `frontend/src/app/features/customers/customers-page.ts` (+`.html`) -- modify -- `actions` gains "Summary" for every state, including the anonymized branch (Scope decision 2)
- `frontend/src/app/features/bookings/booking-detail-page.ts` (+`.html`) -- modify -- Customer field becomes a link to `/customers/{customerId}/summary`
- `frontend/src/styles.css` -- modify -- `@media print` rule hiding `app-top-app-bar`

## Tasks & Acceptance

**Execution — backend (TDD throughout):**
- [x] `CustomerSummaryDto`, `GetCustomerSummaryQuery`/`Handler` -- write failing tests first
- [x] `IBookingRepository.GetForCustomerWithVehicleAsync` -- write failing tests first
- [x] `CustomersController`'s `summary` action
- [x] `CustomerSummaryEndpointTests` -- every I/O-matrix row -- the implementer's own sandbox had no Docker and could not run these; confirmed independently in an environment with Docker: all pass
- [x] Full `dotnet test`; `ArchitectureFitnessTests`; force `dotnet restore`, check `NU1903`; SOLID/DRY/YAGNI self-check -- confirmed independently: 409/409 passing (84+144+4+23+154), 0 advisories

**Execution — frontend (TDD throughout):**
- [x] `useCustomerSummaryQuery`, `CustomerSummaryPage` (loading/not-found/zero-bookings/found/anonymized) -- write failing tests first
- [x] Customers list's "Summary" action (all three states); Booking Detail's Customer link -- write failing tests first, confirm existing Customer/Booking detail tests still pass
- [x] `@media print` rule -- verify manually (no automated print-rendering test in this stack) -- confirmed via live browser check
- [x] `ng build` + `ng test --coverage --watch=false` clean; `npm audit` clean; SOLID/DRY/YAGNI self-check -- confirmed independently: 317/317 passing, 97.84% statement coverage, 0 vulnerabilities

**Acceptance Criteria (epics.md, verbatim intent):**
- Given a customer with booking history, Summary shows identity + contact + full booking list, no filters/edit buttons/nav chrome
- Given a customer with zero bookings, the identity/contact section still renders with the "no bookings" message
- Given an anonymized customer, their name renders as "Customer (anonymized)"
- Given the summary is open, browser print strips the Top App Bar and all button chrome
- Given the summary is loading, a skeleton renders

## Spec Change Log

- **Frontend implementation**: added an optional convenience "Print" button (`window.print()`) alongside the "Back to Customers" link, both flagged `.no-print`. Not required by the AC (browser print works via keyboard/menu regardless), but reasonable, disclosed polish -- and it doubles as a second real consumer proving the `.no-print` mechanism generalizes beyond the one link the spec named.

## Design Notes

**Why a new `CustomerSummaries` slice instead of folding into `Customers`:** this read model spans two aggregates (Customer + Booking) and epics.md's own epic description calls it out as "its own distinct concern... not bundled into either the Customer or Booking epic's CRUD scope." A dedicated, query-only vertical slice matches that framing precisely, and avoids awkwardly placing a cross-aggregate DTO inside either single-aggregate folder.

</frozen-after-approval>

## Verification

Independently re-verified after implementation (not just the implementer's self-report):

**Backend:** `git status --short` confirmed the exact expected file set; `dotnet build BrunoVehicleHire.sln` clean (0 warnings, 0 errors); `dotnet restore --force` showed no `NU1903` advisories. The implementer's own sandbox had no Docker daemon and could not run `CustomerSummaryEndpointTests` (the real proof of every backend I/O-matrix row) -- confirmed independently in an environment with Docker available: full `dotnet test` 409/409 passing (Domain 84, Application 144, Infrastructure 4, Api 23, Integration 154, up from 397 before this story), including all 7 new endpoint tests. Read `GetCustomerSummaryQueryHandler.cs`, `CustomerSummaryDto.cs`, and `BookingRepository.GetForCustomerWithVehicleAsync` directly and confirmed correct reuse of `GetByIdIncludingSoftDeletedAsync` (deactivated customers still render) and the established `IgnoreQueryFilters()` join idiom (AD-13).

**Frontend:** `git status --short` confirmed the exact expected file set; `ng build` clean (0 errors, 628.35 kB / budget 650 kB); `ng test --coverage --watch=false` 317/317 passing, 97.84% statement coverage; `npm audit` 0 vulnerabilities -- all reproduced independently. Read `customer-summary-page.ts`/`.html`, the `customers-page.ts` diff, and the `styles.css` diff directly and confirmed: the booking-history table is genuinely plain HTML, not `DataTable` (no pagination chrome exists to hide); the anonymized-row behavior change (now `[Summary]` instead of `[]`) is deliberate and correctly reflected in the updated test; the `@media print` rule correctly targets both the global `app-top-app-bar` and every `.no-print`-flagged element.

**Process note:** the implementer's own live-browser verification pass left test rows behind in a soft-deleted/cancelled/anonymized state rather than genuinely empty (reasoning that "no PII remains" once anonymized was sufficient) -- found and corrected during independent verification via direct SQL cleanup, restoring a genuinely empty dev database. This project's standing practice is an empty database after verification, not merely a PII-free one; noted for future dispatches.

**SOLID/DRY/YAGNI:** confirmed via direct code reading -- `CustomerSummaryPage`/`GetCustomerSummaryQueryHandler` both stay scoped to orchestration/rendering (SRP); the new `CustomerSummaries` vertical slice and its DTOs don't duplicate `BookingDto`/`CustomerDto`'s existing shapes, just denormalize what this one screen needs (DRY); no pagination, filters, or mutating actions were added to the summary page (YAGNI), matching Scope decisions 4-6 exactly.

## Suggested Review Order

1. `src/BrunoVehicleHire.Application/CustomerSummaries/Queries/GetCustomerSummaryQueryHandler.cs`
2. `src/BrunoVehicleHire.Infrastructure/Repositories/BookingRepository.cs` -- `GetForCustomerWithVehicleAsync`
3. `frontend/src/app/features/customer-summary/customer-summary-page.ts`/`.html` -- the plain booking table and the anonymized-field treatment
4. `frontend/src/app/features/customers/customers-page.ts` -- the anonymized-row behavior change (Scope decision 2)
5. `frontend/src/styles.css` -- the `@media print` rule
