---
title: 'Feature: search Bookings by vehicle or customer'
type: 'feature'
created: '2026-09-21'
status: 'done'
review_loop_iteration: 0
context: []
baseline_commit: 'b95ac9dc6a756de68dc7a7d6616d2e3b5508c161'
---

<frozen-after-approval reason="human-owned intent — do not modify unless human renegotiates">

## Intent

**Problem:** The Bookings list is the only one of the three entity lists with no search, a deliberate Story 4.1 scope decision made because nothing in that story's AC named a specific field to search — a Booking has no name of its own, unlike Vehicle (Make/Model) or Customer (name). SPEC.md's CAP-4 calls for search across "all three entities." Confirmed with the user: add search now, matching both the vehicle (make/model/registration) and the customer's name.

**Approach:** Add a `Search` parameter to `GetBookingsQuery`, implemented in `BookingRepository.GetPagedAsync` as an `ILike` match against the already-joined Vehicle's `Make`/`Model`/`RegistrationNumber` and Customer's `FirstName`/`LastName` (a single query term matches any of the five fields) — mirroring `VehicleRepository`'s existing `ILike`-based search exactly. Add a debounced search input to the Bookings page, mirroring `VehiclesPage`/`CustomersPage`'s existing pattern exactly.

## Boundaries & Constraints

**Always:**
- `Search` is optional (`string?`, default `null`); when absent, behavior is completely unchanged from today (every booking returned, matching the existing `VehicleId` optional-filter precedent).
- The match is case-insensitive substring (`EF.Functions.ILike(field, $"%{search}%")`), OR'd across `Vehicle.Make`, `Vehicle.Model`, `Vehicle.RegistrationNumber`, `Customer.FirstName`, `Customer.LastName` — a single search box, not five separate fields.
- `Customer.Email`/`PhoneNumber` are never searched (they're encrypted, non-deterministic ciphertext at the DB level — pattern-matching them is not possible, and out of scope here regardless).
- The frontend search input mirrors `VehiclesPage`/`CustomersPage`'s existing debounced-search implementation exactly: `toObservable` → `debounceTime(300)` → `distinctUntilChanged()` → `toSignal`, only the debounced value participating in the query key (AD-3).
- `GetBookingsQueryValidator` gains no new constraint beyond accepting the optional `Search` string (mirrors `GetVehiclesQueryValidator`'s own lack of a length/format constraint on `Search`).
- The existing `VehicleId` optional filter (used by Vehicle Detail's booking-history section) and `Search` are independent, composable filters — Vehicle Detail's own call site continues passing `VehicleId` only, `Search` remains `null` there, so its behavior is completely unaffected.

**Ask First:** Nothing else expected to trigger.

**Never:** No search against `Booking.Status`/dates in this story (out of scope — a future story's concern if ever needed). No new shared component — a plain debounced `<input>` mirroring the two existing pages is sufficient (no `Combobox`/`DataTable` changes needed).

## I/O & Edge-Case Matrix

| Scenario | Input / State | Expected Output / Behavior | Error Handling |
|----------|--------------|---------------------------|----------------|
| Search matches a vehicle's Make/Model/RegistrationNumber | e.g. "Ferrari" | Only bookings for matching vehicles returned | N/A |
| Search matches a customer's FirstName/LastName | e.g. "Nkosi" | Only bookings for matching customers returned | N/A |
| Search matches both a vehicle and an unrelated customer | Ambiguous term | Both sets of matching bookings returned (OR, not AND) | N/A |
| Empty/whitespace-only search | `""` or `"   "` | Identical to no search at all (every booking returned) | N/A |
| Search combined with the existing `VehicleId` filter (Vehicle Detail's booking-history section) | `VehicleId` set, `Search` always `null` from that call site | Unchanged from today | N/A |

</frozen-after-approval>

## Code Map

- `src/BrunoVehicleHire.Application/Bookings/Queries/GetBookingsQuery.cs` -- modify -- add `string? Search = null` parameter
- `src/BrunoVehicleHire.Application/Bookings/Queries/GetBookingsQueryHandler.cs` -- modify -- pass `Search` through to the repository call
- `src/BrunoVehicleHire.Application/Bookings/IBookingRepository.cs` -- modify -- `GetPagedAsync`'s signature gains `string? search`
- `src/BrunoVehicleHire.Infrastructure/Repositories/BookingRepository.cs` -- modify -- `GetPagedAsync` (`:50-76`) adds the `ILike`-based `Where` clause across the five fields when `search` is non-empty, mirroring `VehicleRepository.cs:33-35`'s exact `ILike` pattern; applied to the same joined query the total-count and paged-rows queries already share
- `src/BrunoVehicleHire.Api/Controllers/BookingsController.cs` -- modify -- accept a `search` query-string parameter, pass through to `GetBookingsQuery`
- `frontend/src/app/features/bookings/bookings.service.ts` -- modify -- `BookingsQueryParams`/`useBookingsQuery` gain an optional `search` parameter
- `frontend/src/app/features/bookings/bookings-page.ts` -- modify -- add the debounced search signal, mirroring `vehicles-page.ts`'s exact `toObservable`/`debounceTime`/`distinctUntilChanged`/`toSignal` chain
- `frontend/src/app/features/bookings/bookings-page.html` -- modify -- add the search `<input>`, mirroring `vehicles-page.html`'s existing markup/labeling pattern
- `tests/BrunoVehicleHire.Application.Tests/Bookings/Queries/GetBookingsQueryHandlerTests.cs` -- modify -- add test coverage for the new `Search` parameter being passed through
- `tests/BrunoVehicleHire.Integration.Tests/BookingsEndpointTests.cs` -- modify -- add real end-to-end search tests covering the I/O matrix rows above (vehicle match, customer match, no-match, empty search)
- `frontend/src/app/features/bookings/bookings-page.spec.ts`, `bookings.service.spec.ts` -- modify -- add tests for the new search wiring

## Tasks & Acceptance

**Execution (TDD throughout):**
- [x] Add `Search` to `GetBookingsQuery`/handler/repository interface — write failing tests first
- [x] Implement the `ILike` search in `BookingRepository.GetPagedAsync`
- [x] Add the `search` query param to `BookingsController`
- [x] Add the debounced search input to the frontend, mirroring `VehiclesPage`'s exact pattern
- [x] Live browser check: search by a vehicle's make/model/registration, then by a customer's name, confirm results filter correctly and the existing Vehicle Detail booking-history section (which never sets `Search`) is unaffected
- [x] Full `dotnet test`/`ng test --coverage`, confirm nothing regressed

**Acceptance Criteria:**
- [x] Given the Bookings list, when a search term matching a vehicle's make, model, or registration number is entered, then only bookings for matching vehicles are shown
- [x] Given the Bookings list, when a search term matching a customer's first or last name is entered, then only bookings for matching customers are shown
- [x] Given an empty search, when viewed, then every booking is shown exactly as today
- [x] Given Vehicle Detail's booking-history section, when viewed, then its behavior is completely unchanged (it never sets `Search`)

## Spec Change Log

- **`BookingRepository.GetPagedAsync`'s total-count query shape changed, not just extended:** the pre-existing count query ran directly against the un-joined `Bookings` table (a deliberate optimization, since the FK constraints guarantee the join can never drop a row). `Search` needs the joined Vehicle/Customer columns, so the count and paged-rows queries now share one joined (and, when set, search-filtered) query rather than the previous two-different-shapes approach. Verified this doesn't change behavior for the no-search case (the join still can't drop a row per the original reasoning) — confirmed by the full existing `BookingsEndpointTests`/`GetBookingsQueryHandlerTests` suites still passing unmodified in their non-search assertions.

## Design Notes

**Why one search box across five fields (OR), not separate vehicle/customer search inputs:** mirrors how `VehiclesPage`/`CustomersPage` already present exactly one search box each (matching multiple fields per entity) — introducing two separate inputs for Bookings would be a new, inconsistent UI pattern relative to the rest of the app for no clear benefit; a single box that "just finds the booking you're thinking of" (by car or by customer) is simpler and matches this app's established Filter pattern.

**Why Email/PhoneNumber are excluded:** they're encrypted at rest as non-deterministic ciphertext (AD-12) — a `LIKE`/`ILike` pattern match against ciphertext can never match correctly, since the same plaintext encrypts to a different value each time. This is a hard technical constraint, not a scope choice.

## Verification

Independently reproduced (not just re-reading the implementer's report) — the implementer's own sandbox had no Docker, so it could not run the new real-Postgres integration tests it wrote; that gap was closed here.

**Commands:**
- `dotnet test tests/BrunoVehicleHire.Integration.Tests --filter "FullyQualifiedName~BookingsEndpointTests"` (own run, real Testcontainers Postgres) — **37/37 passing**, including every new search test (vehicle match, customer match, OR-union, empty/whitespace search, no-match, `vehicleId`+`search` composability).
- `dotnet test BrunoVehicleHire.sln` (own run, full solution, after all four parallel audit-fixes landed) — 436/436 passing.
- `ng test --coverage` (own run, full suite) — 385/385 passing.

**Code read:** `BookingRepository.GetPagedAsync` (confirmed the `ILike` predicate matches `VehicleRepository`'s exact pattern, correctly OR'd across all five fields, correctly excludes Email/PhoneNumber), `bookings-page.ts` (confirmed the debounced search chain mirrors `VehiclesPage`'s exactly, including the genuinely-empty vs. filtered-empty split).

**Live verification, independently reproduced:** queried the real running API directly (`GET /api/bookings?search=Ferrari`, `GET /api/bookings?search=Friesen`) — both correctly returned only matching bookings. Confirmed live in the browser: typing "Ferrari" into the new Bookings search box correctly filtered to exactly that vehicle's 6 bookings (including one booking that had, in the interim, naturally transitioned from Active to Completed via the real sweep service — confirming no stale/cached state).

## Suggested Review Order

1. `src/BrunoVehicleHire.Infrastructure/Repositories/BookingRepository.cs` — the `ILike` search predicate and the total-count query restructuring
2. `tests/BrunoVehicleHire.Integration.Tests/BookingsEndpointTests.cs` — the real end-to-end search tests
3. `frontend/src/app/features/bookings/bookings-page.ts` and `.html` — the debounced search input
4. `frontend/src/app/features/bookings/bookings.service.ts` — the optional `search` param wiring
