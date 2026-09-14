---
title: 'Story 2.5: View Vehicle Detail'
type: 'feature'
created: '2026-09-14'
status: 'done'
review_loop_iteration: 0
context: []
baseline_commit: 'b3e90155ce5f0f5527be39cdefeb0d3b45ceac24'
---

<frozen-after-approval reason="human-owned intent — do not modify unless human renegotiates">

## Intent

**Problem:** There's no single-vehicle view anywhere — only the list. Three deferrals from earlier stories converge here: Story 2.2's Design Notes said a real frontend 404 treatment "belongs [in] Story 2.5, once there's a real, tested case"; Story 1.7's `Skeleton` has only ever been used for list loading, never detail loading; and the AC itself requires displaying a vehicle's "active/soft-deleted state," which only a soft-delete-aware lookup can answer.

**Approach:** The last story in Epic 2, and the smallest full-stack slice since Story 2.3 — everything backend-side already exists (`GetByIdIncludingSoftDeletedAsync` from Story 2.4, `NotFoundException`→404 from Story 2.2). Add one read query, one route, one page. Finally close the `NotFoundError` deferral by giving `NormalizedApiError` a real third kind, since this is the first story that actually needs to distinguish "doesn't exist" from "something went wrong."

**Scope decisions — flagged for approval:**

1. **`GetVehicleByIdQuery` uses `GetByIdIncludingSoftDeletedAsync`, not `GetByIdAsync`.** The AC requires showing "active/soft-deleted state," which is only answerable if a soft-deleted vehicle's detail can be viewed at all — consistent with `EXPERIENCE.md`'s Flow 3 (reopening a soft-deleted vehicle's detail to later restore it).
2. **No Edit/Deactivate/Restore actions on the detail page.** epics.md's AC is "see its full record... before acting on it" — acting happens elsewhere (the list, per Stories 2.2/2.3/2.4's established resolution). Adding mutation actions here would be scope beyond what this story's AC asks for; the list already has all three.
3. **`NormalizedApiError` gains a real `NotFoundError` kind** (`{kind: 'not-found', detail: string}`), replacing the "falls through to `ServerError`" deferral used by Stories 2.2/2.4. This is a shared-infrastructure change: `VehicleFormModal.applyError` (Story 2.2) must add an explicit branch for it (the TypeScript compiler forces this the moment the union grows a third member — accessing `.errors`/`.type` on a narrowed-but-not-fully-handled union no longer compiles).
4. **No Badge component for the active/soft-deleted state.** `DESIGN.md`'s `Badge` is scoped to Booking's three-state status (Active/Completed/Cancelled, Epic 4) — Vehicle's binary active/inactive state reuses the same text-treatment Story 2.4 already established for the dimmed row (`{colors.text-disabled}` for inactive, normal body text for active), not a new Badge variant nothing in the design spec asks for.

## Boundaries & Constraints

**Always:**
- `GetVehicleByIdQueryHandler` throws the existing `NotFoundException` (→404, already wired since Story 2.2) when `GetByIdIncludingSoftDeletedAsync` returns null.
- The detail page shows: RegistrationNumber, Make, Model, Year, DailyRate (currency-formatted, matching the list's existing formatting), CreatedDate (formatted, matching the list), and the active/soft-deleted state.
- Loading state: `Skeleton`, arranged for a detail card rather than a table — the exact same generic component from Story 1.7, just a different row count/container, establishing "the detail-loading pattern reused by Customer and Booking detail pages" (epics.md's own words) without any `Skeleton` changes.
- Not-found state: "This vehicle no longer exists" + a link back to the Vehicles list — never a blank page, never a raw error dump (epics.md's exact requirement).
- No booking-history section — epics.md is explicit that Story 4.5 adds it later and this story "does not depend on Epic 4 to be complete and useful on its own."
- The Vehicles list's "View" row action (new) navigates to `/vehicles/:id` — added to BOTH the active-vehicle and soft-deleted-vehicle action sets (Story 2.4's per-row `actions` function), since viewing a soft-deleted vehicle's detail is a real, AC-required flow, not just the active case.

**Ask First:** Nothing expected to trigger.

**Never:** No Edit/Deactivate/Restore buttons on the detail page (Scope decision 2). No new Badge variant (Scope decision 4). No booking-history UI of any kind yet.

## I/O & Edge-Case Matrix

| Scenario | Input / State | Expected Output / Behavior | Error Handling |
|----------|--------------|---------------------------|----------------|
| Valid active vehicle id | Existing, active vehicle | `200`, full record renders, state shows "Active" | N/A |
| Valid soft-deleted vehicle id | Existing, soft-deleted vehicle | `200`, full record renders, state shows "Inactive" (dimmed) | N/A |
| Stale/invalid vehicle id | Nonexistent id | `404` | "This vehicle no longer exists" + link back to Vehicles list |
| Request in flight | Any id | `Skeleton` renders in a detail-card arrangement | N/A |

## Code Map

**Backend:**
- `src/BrunoVehicleHire.Application/Vehicles/Queries/GetVehicleByIdQuery.cs` -- new -- `record(Guid VehicleId) : IRequest<VehicleDto>;`
- `src/BrunoVehicleHire.Application/Vehicles/Queries/GetVehicleByIdQueryHandler.cs` -- new -- `GetByIdIncludingSoftDeletedAsync` (throw `NotFoundException` if null) → `VehicleDto.FromDomain`
- `src/BrunoVehicleHire.Api/Controllers/VehiclesController.cs` -- modify -- `[HttpGet("{id:guid}")]` sending `GetVehicleByIdQuery`
- `tests/BrunoVehicleHire.Application.Tests/Vehicles/GetVehicleByIdQueryHandlerTests.cs` -- new -- write failing tests first (found → correct DTO incl. a soft-deleted vehicle's `IsDeleted:true`; not found → `NotFoundException`)
- `tests/BrunoVehicleHire.Integration.Tests/VehiclesEndpointTests.cs` -- modify -- add `GET /api/vehicles/{id}` coverage: active vehicle 200, soft-deleted vehicle 200 with `isDeleted:true`, nonexistent id 404

**Frontend:**
- `frontend/src/app/core/api-client/normalized-api-error.ts` -- modify -- add `NotFoundError { kind: 'not-found'; detail: string }`, add it to the `NormalizedApiError` union
- `frontend/src/app/core/api-client/error-normalization.interceptor.ts` -- modify -- a request whose status is 404 and whose body is a valid ProblemDetails shape normalizes to `NotFoundError`, not the `ServerError` catch-all; write failing tests first
- `frontend/src/app/features/vehicles/vehicle-form-modal.ts` -- modify -- `applyError` gains an explicit `error.kind === 'not-found'` branch (shown as the top-of-form banner, same treatment as a `ServerError`) -- required the moment `NormalizedApiError` grows a third member (a TypeScript compile error otherwise, not a style choice)
- `frontend/src/app/features/vehicles/vehicles.service.ts` -- modify -- add `useVehicleQuery(id: () => string | undefined)`, query key `['vehicles', 'detail', id]` (AD-3), `enabled` guarded on a defined id
- `frontend/src/app/features/vehicles/vehicle-detail-page.ts` (+`.html`+`.spec.ts`) -- new -- reads the route's `id` param, renders the three states (loading/not-found/found) described above
- `frontend/src/app/app.routes.ts` -- modify -- add `{ path: 'vehicles/:id', component: VehicleDetailPage }`
- `frontend/src/app/features/vehicles/vehicles-page.ts` (+`.html`) -- modify -- the per-row `actions` function (Story 2.4) gains a "View" entry in both branches (active and soft-deleted), navigating to `/vehicles/${vehicle.id}` via the Router

## Tasks & Acceptance

**Execution — backend (TDD throughout):**
- [x] `GetVehicleByIdQuery`/`Handler` -- write failing tests first, then implement
- [x] `VehiclesController`'s new `GET {id}` action
- [x] `VehiclesEndpointTests` -- write failing integration tests first for every backend I/O-matrix row
- [x] Full `dotnet test`; re-run `ArchitectureFitnessTests`; force `dotnet restore` and check for NU1903 warnings; explicit SOLID/DRY/YAGNI self-check -- confirmed: 153/153 passing, 0 vulnerabilities

**Execution — frontend (TDD throughout):**
- [x] `NotFoundError` + interceptor recognition -- write failing tests first, confirm all pre-existing interceptor tests still pass unchanged
- [x] `VehicleFormModal.applyError`'s new branch -- confirm the project still compiles and its existing tests pass unchanged
- [x] `useVehicleQuery` -- write failing tests first
- [x] `VehicleDetailPage` -- write failing tests first for all three states (loading/not-found/found, including the soft-deleted-state-rendering case) and the route wiring
- [x] Add the "View" row action to `vehicles-page`
- [x] `ng build` + `ng test --coverage --watch=false` clean; `npm audit` clean; explicit SOLID/DRY/YAGNI self-check -- confirmed: 145/145 passing, 96.99% stmt coverage, 0 vulnerabilities

**Acceptance Criteria (epics.md, verbatim intent):**
- Given I click "View" on a vehicle row, when the detail page loads, then it shows the vehicle's full record (RegistrationNumber, Make, Model, Year, DailyRate, CreatedDate, active/soft-deleted state)
- Given a stale or invalid vehicle id, when the detail page is opened, then a "This vehicle no longer exists" message renders with a link back to the Vehicles list — never a blank page or a raw 404
- Given the detail page is loading, when the request is in flight, then a skeleton loading state renders, establishing the detail-loading pattern reused by Customer and Booking detail pages

## Spec Change Log

## Design Notes

**Why `NotFoundError` had to be added now, not deferred again:** Stories 2.2 and 2.4 both explicitly deferred it with the same reasoning — "no story yet renders a 404 for real." This is that story. Deferring a fourth time would mean shipping this story's own AC ("never... a raw 404") without the type-level distinction that makes it possible to tell "not found" apart from "something went wrong" in the first place.

**Why `VehicleFormModal` needs a change it doesn't conceptually care about:** it never used to see a `NotFoundError` in practice (Create never 404s; Edit only would on a genuine race), but the union type it pattern-matches against just grew a member, and TypeScript's exhaustiveness makes ignoring that impossible once the compiler is asked to narrow `error.errors`/`error.type` off a type that no longer guarantees they exist. This is a mechanical, compiler-forced touch, not new scope.

**A pre-existing test became stale, correctly:** `vehicles-page.spec.ts` had a test asserting a 404-on-deactivate showed a generic "An unexpected error occurred" message -- correct under the old interceptor (which had no way to distinguish a 404 from any other non-business-rule failure). Once `NotFoundError` exists, the same 404 correctly surfaces its real `detail` message instead, via `toErrorMessage`'s already-unchanged `error.kind === 'server-error' ? error.message : error.detail` -- `NotFoundError.detail` slots in without needing new mapping code. The test assertion was updated to match; nothing else about that test changed.

**Formatter extraction (`vehicle-formatters.ts`), not in the original Code Map:** `vehicles-page.ts`'s currency/date formatters needed to be shared with the new detail page rather than reinstantiated, per the DRY instruction to reuse "the exact same instances." A three-line extraction, not a new abstraction layer.

## Verification — actual results

- Backend: `dotnet build` 0 warnings/0 errors; `dotnet test` 153/153 passing (up from 147 before this story); no vulnerability warnings; `ArchitectureFitnessTests` unchanged/passing. Live manual proof of active/soft-deleted/nonexistent GET-by-id against the real docker-compose Postgres.
- Frontend: `ng build` 0 errors/warnings (580.80kB, well within the 650kB budget) -- this build succeeding is itself proof the compiler-forced `NotFoundError` branch was correctly added everywhere required. `ng test --coverage --watch=false` 145/145 passing, 96.99% statement coverage; `npm audit` 0 vulnerabilities. Every pre-existing interceptor and `VehicleFormModal` test still passes.
- Full live end-to-end: real backend + `ng serve` + real Postgres. Viewed an active vehicle's full record, toggled "show inactive" and viewed a soft-deleted vehicle's detail (confirmed "Inactive" styling), and navigated directly to a nonexistent vehicle id, confirming "This vehicle no longer exists." plus a working link back -- never a blank page or raw error.

</frozen-after-approval>

## Suggested Review Order

**Closing the deferral (the point of this story)**

- `NormalizedApiError`'s real third kind, and the interceptor recognizing it.
  [`normalized-api-error.ts`](../../frontend/src/app/core/api-client/normalized-api-error.ts) · [`error-normalization.interceptor.ts`](../../frontend/src/app/core/api-client/error-normalization.interceptor.ts)

- `VehicleFormModal`'s compiler-forced branch — proof the type system, not just a reviewer, caught the ripple effect.
  [`vehicle-form-modal.ts`](../../frontend/src/app/features/vehicles/vehicle-form-modal.ts)

- `VehicleDetailPage`'s three states, especially the soft-deleted-vehicle case that only `GetByIdIncludingSoftDeletedAsync` makes possible.
  [`vehicle-detail-page.ts`](../../frontend/src/app/features/vehicles/vehicle-detail-page.ts) · [`vehicle-detail-page.html`](../../frontend/src/app/features/vehicles/vehicle-detail-page.html)

**Backend**

- `GetVehicleByIdQueryHandler` — the smallest handler pattern in the app, reused verbatim from `RestoreVehicleCommandHandler`'s lookup.
  [`GetVehicleByIdQueryHandler.cs`](../../src/BrunoVehicleHire.Application/Vehicles/Queries/GetVehicleByIdQueryHandler.cs)

**Peripherals**

- The now-correct stale-test update, and the formatter extraction.
  [`vehicles-page.spec.ts`](../../frontend/src/app/features/vehicles/vehicles-page.spec.ts) · [`vehicle-formatters.ts`](../../frontend/src/app/features/vehicles/vehicle-formatters.ts)
