---
title: 'Story 2.3: Soft-Delete a Vehicle'
type: 'feature'
created: '2026-09-12'
status: 'done'
review_loop_iteration: 0
context: []
baseline_commit: 'e15d4d8127252ac3161dc8aa2b536dc19948761a'
---

<frozen-after-approval reason="human-owned intent — do not modify unless human renegotiates">

## Intent

**Problem:** `Vehicle.SoftDelete()` has existed since Story 1.3 but nothing calls it — there's no command, no endpoint, and no UI action. `ConfirmDialog` (built in Story 2.2 for the discard-changes guard) has never been used for its actual named purpose yet.

**Approach:** The smallest story in Epic 2 so far — everything it needs already exists (`Vehicle.SoftDelete()`, `IUnitOfWork`, `NotFoundException`/404, `ConfirmDialog`, `DataTable`'s row-actions mechanism). Add one command, one endpoint, one row action, one `ConfirmDialog` usage.

**Scope decision — flagged for approval:** epics.md's AC says "Given an active vehicle's **detail view**... click 'Deactivate'" — but Story 2.5 (View Vehicle Detail) doesn't exist yet, exactly the same forward-dependency shape Story 2.2 hit with Edit. **Same resolution as 2.2:** "Deactivate" is a second row action on the Vehicles list (reusing `DataTable`'s `actions` mechanism from Story 2.2 — zero `DataTable` changes needed), not launched from a detail page that doesn't exist yet. Story 2.5 can add the identical action to the detail view once it exists, calling the same backend endpoint.

**Given this story's small size, it's implemented as a single dispatch (not the two-phase backend/frontend split used for the larger Stories 1.6/1.7/2.1/2.2)** — everything on both sides is a thin, well-understood addition to already-proven infrastructure.

## Boundaries & Constraints

**Always:**
- `SoftDeleteVehicleCommand` returns `Unit` (MediatR's empty-response type), not a DTO — per AD-2's exception list ("Cancel/SoftDelete/Anonymize/Delete commands... return `Unit`/204").
- The handler reuses `IVehicleRepository.GetByIdAsync` (Story 2.2) — not-found (including an already-soft-deleted vehicle, since the query filter still applies) throws the existing `NotFoundException` → 404, no new exception type.
- The endpoint is `POST /api/vehicles/{id:guid}/deactivate` returning `204 No Content` — a distinct action-named route rather than `DELETE /api/vehicles/{id}`, since "deactivate"/soft-delete is a domain verb this app never pairs with a true hard-delete for Vehicles (see Design Notes).
- The default Vehicles list already excludes soft-deleted vehicles (the AD-13 query filter, since Story 1.7) — no new frontend filtering logic is needed for "the vehicle disappears from the default list"; it happens automatically once the list re-fetches after the mutation invalidates its query key.
- The row action is labeled "Deactivate" (the exact AC/EXPERIENCE.md button text) even though the underlying domain method/command is named `SoftDelete` — UI verb vs. domain verb, consistent with how `Vehicle.SoftDelete()` was already named in Story 1.3.
- The `ConfirmDialog` states the specific, reversible consequence per `EXPERIENCE.md`'s Component Patterns row for the neutral variant ("states the specific consequence... and that it's reversible where true").
- On success: invalidate `['vehicles', 'list']` (AD-3), close the dialog, show a success toast ("Vehicle deactivated.").
- On failure (e.g. a stale/already-deactivated vehicle, a concurrent second staff member having already deactivated it — a real possibility even though epics.md's own AC doesn't test it): the `ConfirmDialog` stays open and shows the error instead of silently closing or failing invisibly — this is baseline correctness for any real mutation, not speculative scope.

**Ask First:** Nothing expected to trigger.

**Never:** No "show inactive" toggle, dimmed-row treatment, or Restore action (Story 2.4). No changes to `Vehicle.SoftDelete()` itself (already correct and idempotent since Story 1.3). No new shared component — `ConfirmDialog`/`DataTable` are reused exactly as they already exist.

## I/O & Edge-Case Matrix

| Scenario | Input / State | Expected Output / Behavior | Error Handling |
|----------|--------------|---------------------------|----------------|
| Active vehicle, confirm deactivate | Valid, currently-active vehicle id | `204 No Content`; `IsDeleted` becomes true; vehicle disappears from the default list; success toast | N/A |
| Cancel the confirmation | User clicks "Keep active" (or similar cancel label) | Dialog closes, nothing happens, vehicle unchanged | N/A |
| Nonexistent / already-deactivated vehicle id | Stale id (race with another staff member, or direct API misuse) | `404 Not Found` | Dialog stays open, shows the error, does not silently close |

## Code Map

**Backend:**
- `src/BrunoVehicleHire.Application/Vehicles/Commands/SoftDeleteVehicleCommand.cs` -- new -- `record SoftDeleteVehicleCommand(Guid VehicleId) : IRequest;`
- `src/BrunoVehicleHire.Application/Vehicles/Commands/SoftDeleteVehicleCommandHandler.cs` -- new -- `GetByIdAsync` (throw `NotFoundException` if null) → `vehicle.SoftDelete()` → `SaveChangesAsync`
- `src/BrunoVehicleHire.Api/Controllers/VehiclesController.cs` -- modify -- `[HttpPost("{id:guid}/deactivate")]` sending `new SoftDeleteVehicleCommand(id)`, returns `NoContent()`
- `tests/BrunoVehicleHire.Application.Tests/Vehicles/SoftDeleteVehicleCommandHandlerTests.cs` -- new -- write failing tests first (success calls `SoftDelete`+`SaveChangesAsync` once; not-found throws `NotFoundException` and never calls `SaveChangesAsync`)
- `tests/BrunoVehicleHire.Integration.Tests/VehiclesEndpointTests.cs` -- modify -- add `POST .../deactivate` coverage: success (204, follow-up GET-list confirms it's gone from default results), 404 for a nonexistent id, 404 for an already-deactivated vehicle (idempotency-adjacent: the SECOND deactivate attempt on the same vehicle correctly 404s rather than silently double-processing, since the query filter already excludes it)

**Frontend:**
- `frontend/src/app/features/vehicles/vehicles.service.ts` -- modify -- add `useDeactivateVehicleMutation()` (mirrors the existing query-hook pattern in this file), `POST`-ing to `vehicles/${id}/deactivate` via `ApiClient`, invalidating `['vehicles', 'list']` on success
- `frontend/src/app/features/vehicles/vehicles-page.ts` (+`.html`) -- modify -- add a `"Deactivate"` entry to the existing `actions: RowAction<Vehicle>[]` array; a `deactivatingVehicle = signal<Vehicle | null>(null)` (opens the confirm flow); a `<app-confirm-dialog>` wired to the new mutation, showing the mutation's error (if any) in place of its normal message when a deactivate attempt fails, keeping the dialog open

## Tasks & Acceptance

**Execution (TDD throughout):**
- [x] `SoftDeleteVehicleCommand`/`Handler` -- write failing tests first, then implement
- [x] `VehiclesController`'s new action
- [x] `VehiclesEndpointTests` -- write failing integration tests first for every backend I/O-matrix row
- [x] `useDeactivateVehicleMutation` -- write failing tests first (calls the right URL, invalidates the list query key on success)
- [x] Wire the "Deactivate" row action + `ConfirmDialog` into `vehicles-page` -- write failing tests first: clicking "Deactivate" opens the dialog with the row's vehicle; confirming calls the mutation and, on success, closes the dialog, shows the toast, and the list query is invalidated; cancelling closes the dialog with no mutation call; a failed mutation keeps the dialog open and displays the error
- [x] Full `dotnet test` + `ng test --coverage --watch=false`; re-run `ArchitectureFitnessTests`; force `dotnet restore`/`npm audit` and check for vulnerabilities; explicit SOLID/DRY/YAGNI self-check -- confirmed: 136/136 backend + 120/120 frontend passing, 0 vulnerabilities

**Acceptance Criteria (epics.md, verbatim intent, adapted per the Scope decision above):**
- Given an active vehicle in the Vehicles list, when I click "Deactivate" and confirm the neutral `ConfirmDialog` (which states the vehicle will disappear from availability searches but remains reversible), then `Vehicle.SoftDelete()` is called, `IsDeleted` is set, the vehicle disappears from the default Vehicles list, and a success toast confirms
- Given the soft-delete command handler, its test was written and failing before the implementation existed (TDD)

## Spec Change Log

## Design Notes

**Why `POST .../deactivate` instead of `DELETE`:** this app never pairs Vehicle's soft-delete with a true hard-delete endpoint (there isn't one, and per the domain model there never will be for Vehicles), so there's no HTTP-verb ambiguity to resolve either way — but naming the route after the domain action (`deactivate`) rather than overloading `DELETE`'s conventional "this resource is now gone" connotation reads more precisely, and keeps `[HttpDelete]` free in case Customers (Epic 3) ever need genuine verb-based REST semantics for something different.

**Why a failed deactivate keeps the dialog open showing the error, rather than a toast:** `ToastService` deliberately has no error variant (Story 2.1's YAGNI choice — "toasts reserved for success confirmations only," matching `EXPERIENCE.md`). The `ConfirmDialog` itself is the natural place to surface a failure tied to the action just attempted, without inventing a new error-toast capability or a new shared error-banner component for a single caller.

**MediatR's `Unit`-returning handler API confirmed, not guessed:** `IRequestHandler<TRequest>` (single-generic, for a plain `IRequest`) requires a bare `Task Handle(TRequest, CancellationToken)` in the installed MediatR 14.2.0 — no `Unit.Value` wrapping needed, confirmed by inspecting the installed assembly directly before writing the handler.

## Verification — actual results

- Backend: `dotnet build` 0 warnings/0 errors; `dotnet test` 136/136 passing (up from 131 before this story); no vulnerability warnings; `ArchitectureFitnessTests` unchanged/passing.
- Frontend: `ng build` 0 errors/warnings (575.58kB, well within the 650kB budget, no meaningful growth); `ng test --coverage --watch=false` 120/120 passing, 96.74% statement coverage; `npm audit` 0 vulnerabilities.
- Full live end-to-end: real backend + `ng serve` + real Postgres. Clicked "Deactivate" on a real vehicle, confirmed the `ConfirmDialog`'s exact copy, confirmed the vehicle vanished from the list (both visually and via a direct API re-query), confirmed the success toast rendered, and confirmed a second deactivate attempt on the same vehicle correctly 404s. All manually-created test rows were cleaned up afterward -- verified zero remaining.

</frozen-after-approval>

## Suggested Review Order

**The write path itself (the point of this story)**

- `SoftDeleteVehicleCommandHandler` — the smallest handler in the app: fetch-or-404, mutate, save.
  [`SoftDeleteVehicleCommandHandler.cs`](../../src/BrunoVehicleHire.Application/Vehicles/Commands/SoftDeleteVehicleCommandHandler.cs)

- The already-deactivated-vehicle 404 test — proves the query filter (not new logic) correctly prevents double-processing.
  [`VehiclesEndpointTests.cs:544`](../../tests/BrunoVehicleHire.Integration.Tests/VehiclesEndpointTests.cs#L544)

- `vehicles-page.onConfirmDeactivate`/`onError` — the dialog-stays-open-on-failure pattern.
  [`vehicles-page.ts`](../../frontend/src/app/features/vehicles/vehicles-page.ts)

**Peripherals**

- `useDeactivateVehicleMutation` — the mutation/invalidation pattern, consistent with the rest of this feature's service file.
  [`vehicles.service.ts:50`](../../frontend/src/app/features/vehicles/vehicles.service.ts#L50)
