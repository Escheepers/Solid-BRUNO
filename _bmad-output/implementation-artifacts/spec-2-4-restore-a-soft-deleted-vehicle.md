---
title: 'Story 2.4: Restore a Soft-Deleted Vehicle'
type: 'feature'
created: '2026-09-12'
status: 'done'
review_loop_iteration: 0
context: []
baseline_commit: '410ccf0d8e9b80e4c65b70584ecaa18ad77922e4'
---

<frozen-after-approval reason="human-owned intent — do not modify unless human renegotiates">

## Intent

**Problem:** Story 2.3 gave vehicles a one-way door — once deactivated, a vehicle is invisible and unreachable. There's no way to see a soft-deleted vehicle, no way to bring it back, and `IVehicleRepository.GetByIdAsync` (built in Story 2.2) deliberately respects the soft-delete query filter — meaning it would return "not found" for exactly the vehicles this story needs to find.

**Approach:** Add `Vehicle.Restore()` — deliberately the **opposite** design choice from `SoftDelete()`'s idempotency: restoring an already-active vehicle must fail (epics.md's own AC: "the action fails gracefully... rather than silently double-processing"), not silently succeed twice. Add a "show inactive" toggle to the Vehicles list (a new `GetVehiclesQuery` parameter, threaded through the repository), and extend `DataTable` with three small, genuinely generic capabilities the AC directly requires: per-row action sets that differ by row state (active vehicles get Edit/Deactivate, inactive ones get only Restore), a dimmed-row visual treatment, and an inline per-row error display (for "Already active").

**Scope decisions — flagged for approval (this story changes an already-shipped component's public API):**

1. **`DataTable.actions` changes from a static `RowAction<T>[]` to a per-row function `(row: T) => RowAction<T>[]`.** Directly required — epics.md's AC says inactive rows show "a 'Restore' action **in place of** the usual row actions," not alongside them. Every existing call site (`vehicles-page`) is updated; `DataTable`'s existing tests are updated to call through the new function shape (mechanical, no behavior change for the active-row case).
2. **`DataTable` gains `rowMuted: (row: T) => boolean` (dimmed-row styling) and `rowKey`+`rowError` (a single, generic per-row inline-error slot, matching `DESIGN.md`'s `error-row` treatment and `EXPERIENCE.md`'s explicit guidance: "An error state on a row... renders via the `inline-error` component under the row's actions... never a page-level toast for this class of error").** Both stay entity-agnostic — `DataTable` never learns what "soft-deleted" or "already active" means; the caller supplies a predicate/message.
3. **`Restore` has no `ConfirmDialog` step** — unlike Deactivate (Story 2.3), epics.md's AC describes a direct "click Restore" flow with no confirmation, which makes sense: gating the *undo* of an already-reversible, already-confirmed action behind a second confirmation would be redundant friction, not safety.
4. **`VehicleDto`/`Vehicle` (frontend) gain an `isDeleted` field** — necessary now that soft-deleted vehicles can be returned to the frontend at all (via the new toggle), so it can drive the dimmed-row/action-set logic.

## Boundaries & Constraints

**Always:**
- `Vehicle.Restore()` throws `DomainRuleViolationException("Vehicle", "IsDeleted", "Already active.")` if the vehicle is NOT currently soft-deleted — non-idempotent, by design (see Intent). If it IS soft-deleted, clears `IsDeleted` and does nothing else (no other field changes).
- `RestoreVehicleCommandHandler` fetches via a NEW repository method, `GetByIdIncludingSoftDeletedAsync` (`IgnoreQueryFilters()`, mirroring `ExistsByRegistrationNumberAsync`'s existing reasoning) — NOT the existing `GetByIdAsync`, which would incorrectly 404 every soft-deleted vehicle it's asked to restore.
- `GetVehiclesQuery`/`IVehicleRepository.GetPagedAsync` gain a `bool IncludeInactive = false` parameter (default preserves every existing call site's behavior unchanged) — when true, the repository ignores the soft-delete query filter for that query only.
- Every active vehicle's row keeps exactly its Story 2.2/2.3 actions (Edit, Deactivate); every soft-deleted vehicle's row (only ever visible when the toggle is on) shows only Restore, with the dimmed-row treatment (`{colors.text-disabled}` per `EXPERIENCE.md`'s State Patterns row for "Soft-deleted vehicle").
- Restore has no confirmation step (Scope decision 3) — clicking it calls the mutation directly.
- On restore success: invalidate `['vehicles', 'list']` (both toggle states — the vehicle needs to both disappear from the "inactive" view and appear in the default view), toast "Vehicle restored."
- On restore failure (already active): the SPECIFIC row shows the inline "Already active" message with the `error-row` background treatment — never a toast, never a page-level banner (`EXPERIENCE.md`'s explicit rule for this class of error).

**Ask First:** Nothing expected to trigger.

**Never:** No changes to `SoftDelete()`'s existing idempotent behavior. No new `ConfirmDialog` usage for Restore. No booking-availability integration (`Vehicle` doesn't know about bookings at all yet — Epic 4).

## I/O & Edge-Case Matrix

| Scenario | Input / State | Expected Output / Behavior | Error Handling |
|----------|--------------|---------------------------|----------------|
| Toggle "show inactive" on | Vehicles list, some soft-deleted rows exist | Soft-deleted vehicles appear, dimmed, with only a "Restore" action | N/A |
| Toggle off | Same list | Reverts to the existing default (soft-deleted excluded) — unchanged Story 1.7 behavior | N/A |
| Restore a soft-deleted vehicle | Valid, currently-inactive vehicle | `204`; `IsDeleted` cleared; disappears from the "inactive" view, reappears in the default view; success toast | N/A |
| Restore an already-active vehicle | Stale UI / race (e.g. a second staff member already restored it) | `409 Conflict`, `detail`="Already active." | Inline "Already active" message + `error-row` tint on that specific row, no toast |
| Restore a nonexistent vehicle id | Invalid id | `404 Not Found` | Falls through to the generic `ServerError` treatment (same deliberate deferral as Story 2.2's Scope decision 1 — no dedicated frontend 404 UI yet, Story 2.5's job) |

## Code Map

**Backend:**
- `src/BrunoVehicleHire.Domain/Vehicle.cs` -- modify -- add `public void Restore()` (throws if `!IsDeleted`, else clears it)
- `tests/BrunoVehicleHire.Domain.Tests/VehicleTests.cs` -- modify -- `Restore()` on a soft-deleted vehicle succeeds; `Restore()` on an active vehicle throws with the exact "Already active." message and leaves `IsDeleted` unchanged
- `src/BrunoVehicleHire.Application/Vehicles/IVehicleRepository.cs` -- modify -- add `Task<Vehicle?> GetByIdIncludingSoftDeletedAsync(Guid id, CancellationToken ct)`; change `GetPagedAsync` to accept `bool includeInactive`
- `src/BrunoVehicleHire.Infrastructure/Repositories/VehicleRepository.cs` -- modify -- implement both
- `src/BrunoVehicleHire.Application/Vehicles/Queries/GetVehiclesQuery.cs` -- modify -- add `bool IncludeInactive = false`
- `src/BrunoVehicleHire.Application/Vehicles/Queries/GetVehiclesQueryHandler.cs` -- modify -- pass it through to `GetPagedAsync`
- `src/BrunoVehicleHire.Application/Vehicles/Dtos/VehicleDto.cs` -- modify -- add `bool IsDeleted` to the record and `FromDomain`
- `src/BrunoVehicleHire.Application/Vehicles/Commands/RestoreVehicleCommand.cs` -- new -- `record(Guid VehicleId) : IRequest;`
- `src/BrunoVehicleHire.Application/Vehicles/Commands/RestoreVehicleCommandHandler.cs` -- new -- `GetByIdIncludingSoftDeletedAsync` (404 if truly missing) → `vehicle.Restore()` (lets `DomainRuleViolationException` propagate) → `SaveChangesAsync`
- `src/BrunoVehicleHire.Api/Controllers/VehiclesController.cs` -- modify -- `[HttpGet]` gains `[FromQuery] bool showInactive = false`; new `[HttpPost("{id:guid}/restore")]`
- `tests/BrunoVehicleHire.Application.Tests/Vehicles/RestoreVehicleCommandHandlerTests.cs` -- new
- `tests/BrunoVehicleHire.Integration.Tests/VehiclesEndpointTests.cs` -- modify -- add: `showInactive=true` returns soft-deleted rows with `isDeleted:true`; default (`showInactive` omitted/false) still excludes them (regression); restore success (204, follow-up GET with `showInactive=true` confirms `isDeleted:false`, follow-up default GET confirms it's back); restore-already-active returns 409 with `detail`="Already active."; restore-nonexistent-id returns 404

**Frontend:**
- `frontend/src/app/core/models/vehicle-dto.ts` -- modify -- add `isDeleted: boolean`
- `frontend/src/app/features/vehicles/models/vehicle.ts` -- modify -- add `isDeleted: boolean` to `Vehicle` and `toVehicle`
- `frontend/src/app/shared/data-table/data-table.ts` (+`.html`+`.spec.ts`) -- modify -- `actions` becomes `input<(row: T) => RowAction<T>[]>(() => [])`; add `rowMuted = input<((row: T) => boolean) | null>(null)`; add `rowKey = input<((row: T) => string) | null>(null)` and `rowError = input<{ key: string; message: string } | null>(null)` (a single per-row error slot — only one action is ever in flight at a time in practice); apply the dimmed treatment when `rowMuted?.(row)` is true and the `error-row` background + inline message when `rowKey?.(row) === rowError()?.key`
- `frontend/src/app/features/vehicles/vehicles.service.ts` -- modify -- `VehiclesQueryParams` gains `showInactive: boolean`; `useVehiclesQuery`'s query key and `ApiClient.get` call both include it; add `useRestoreVehicleMutation()` (mirrors `useDeactivateVehicleMutation`, `POST vehicles/{id}/restore`)
- `frontend/src/app/features/vehicles/vehicles-page.ts` (+`.html`+`.spec.ts`) -- modify -- a `showInactive` signal (checkbox), passed into the query; `actions` becomes a function returning `[Edit, Deactivate]` for an active vehicle or `[Restore]` for an inactive one; `restoreErrorRowKey`/`restoreErrorMessage` signals wired to `DataTable`'s `rowKey`/`rowError`; `rowMuted` wired to `(v) => v.isDeleted`

## Tasks & Acceptance

**Execution — backend (TDD throughout):**
- [x] `Vehicle.Restore()` -- write failing Domain.Tests first, then implement
- [x] `GetByIdIncludingSoftDeletedAsync` + `GetPagedAsync`'s `includeInactive` param -- update `GetVehiclesQueryHandler`'s call site
- [x] `GetVehiclesQuery`'s `IncludeInactive` param; `VehicleDto`'s `IsDeleted` field (update `FromDomain`, confirm `GetVehiclesQueryHandlerTests`/`CreateVehicleCommandHandlerTests` still pass with the new field present)
- [x] `RestoreVehicleCommand`/`Handler` -- write failing tests first (success; already-active throws; not-found throws `NotFoundException`)
- [x] `VehiclesController`'s `showInactive` query param + new `restore` action
- [x] `VehiclesEndpointTests` -- write failing integration tests first for every backend I/O-matrix row
- [x] Full `dotnet test`; re-run `ArchitectureFitnessTests`; force `dotnet restore` and check for NU1903 warnings; explicit SOLID/DRY/YAGNI self-check -- confirmed: 147/147 passing, 0 vulnerabilities

**Execution — frontend (TDD throughout):**
- [x] `VehicleDto`/`Vehicle`'s `isDeleted` field
- [x] `DataTable`'s three extensions -- write failing tests first for each (actions-as-function still renders correctly per row; `rowMuted` applies dimmed styling; `rowKey`+`rowError` renders the inline message + `error-row` tint only on the matching row), update its existing tests to the new `actions` function shape, confirm no regression in the active-row case
- [x] `useRestoreVehicleMutation` + `showInactive` threading through `useVehiclesQuery` -- write failing tests first
- [x] Wire the toggle, the per-row-state action function, and the restore error display into `vehicles-page` -- write failing tests first: toggling "show inactive" includes soft-deleted rows; an inactive row shows only Restore, dimmed; clicking Restore on an active-again row (simulated failure) shows the inline error + tint on that row only; a successful restore clears the error state and shows the toast
- [x] `ng build` + `ng test --coverage --watch=false` clean; `npm audit` clean; explicit SOLID/DRY/YAGNI self-check -- confirmed: 133/133 passing, 97% stmt coverage, 0 vulnerabilities, all pre-existing DataTable tests pass unchanged in behavior

**Acceptance Criteria (epics.md, verbatim intent):**
- Given the Vehicles list has a "show inactive" filter toggle, when I enable it, then soft-deleted vehicles appear with a dimmed row treatment and a "Restore" action in place of the usual row actions
- Given a soft-deleted vehicle shown via the toggle, when I click "Restore", then `IsDeleted` is cleared, the vehicle reappears in default listings, and it's immediately bookable again
- Given a vehicle that is already active, when I click "Restore" on it anyway, then the action fails gracefully with an inline "Already active" message rather than silently double-processing

## Spec Change Log

**Backend, minor:** `IVehicleRepository.GetPagedAsync`'s new `includeInactive` parameter has no default value at the interface level (C# doesn't allow an optional parameter before a required trailing `CancellationToken`, and this interface's existing convention never defaults `CancellationToken`) — `GetVehiclesQuery`'s own `IncludeInactive = false` default is what actually preserves every real caller's behavior; the repository interface itself just requires the argument explicitly. Two existing `GetVehiclesQueryHandlerTests` substitute call sites needed a mechanical one-argument update (`false` added) to keep compiling — no behavior change.

## Design Notes

**Why `Restore()` is deliberately non-idempotent (unlike `SoftDelete()`):** epics.md's own AC requires it — "fails gracefully... rather than silently double-processing." This is a genuine, intentional asymmetry between the two mutation methods, not an oversight: `SoftDelete()` being a no-op on a second call is safe because deactivating twice has no observable difference; `Restore()` succeeding twice would be misleading (it implies "this just came back from being unavailable," which isn't true the second time) and the AC explicitly wants that surfaced as a real, actionable error.

**Why `DataTable`'s new `rowMuted`/`rowKey`/`rowError` inputs stay generic:** consistent with `DataTable`'s existing design (see spec-1-7/2-2's Design Notes) — it has never known what any entity's fields mean, only rendered what callers hand it via functions/predicates. This story's additions follow the identical shape: a predicate for "should this row look muted," a key function for "how do I identify this row," and a message for "what went wrong on it" — none of it Vehicle-specific.

**Why a single `rowError` slot (not a map) is sufficient:** only one row action can realistically be in flight/have just failed at a time in this UI (no bulk actions exist anywhere in the app) — a `Record<key, message>` would be unused generality.

**`hasAnyRowActions` (frontend, minor, not in the original Code Map):** since `actions` became per-row, whether to render the actions column at all needed to become a computed check (`rows().some(row => actions()(row).length > 0)`) rather than the old static `actions().length > 0` — necessary to preserve the pre-existing "no actions column when nothing to show" behavior once `actions` could no longer be inspected without a row.

## Verification — actual results

- Backend: `dotnet build` 0 warnings/0 errors; `dotnet test` 147/147 passing (up from 136 before this story); no vulnerability warnings; `ArchitectureFitnessTests` unchanged/passing. Live manual proof of the full lifecycle (create → deactivate → show-inactive GET → restore → default GET → re-restore-409 → restore-nonexistent-404) against the real docker-compose Postgres.
- Frontend: `ng build` 0 errors/warnings (577.84kB, well within the 650kB budget); `ng test --coverage --watch=false` 133/133 passing, 97% statement coverage; `npm audit` 0 vulnerabilities. Every pre-existing `DataTable` test still passes with unchanged rendered behavior for the active-row case, after being mechanically updated to the new function-shaped `actions` input.
- Full live end-to-end, including the hardest case: toggled "show inactive," saw a dimmed vehicle with only Restore, restored it successfully, then genuinely forced the 409 race (a direct authenticated fetch bypassing the Angular app to restore it a second time, then clicking Restore in the now-stale UI) and confirmed the inline "Already active." message rendered with the error-row tint on exactly that row.

</frozen-after-approval>

## Suggested Review Order

**The deliberate asymmetry (the point of this story)**

- `Vehicle.Restore()` vs. `Vehicle.SoftDelete()` — non-idempotent by design, the opposite choice from its sibling method.
  [`Vehicle.cs:123`](../../src/BrunoVehicleHire.Domain/Vehicle.cs#L123)

- `GetByIdIncludingSoftDeletedAsync` vs. `GetByIdAsync` — why Restore needed a second lookup method, not a flag on the existing one.
  [`VehicleRepository.cs:83`](../../src/BrunoVehicleHire.Infrastructure/Repositories/VehicleRepository.cs#L83)

- The default-behavior regression test — proves `includeInactive=false` is byte-for-byte the pre-2.4 behavior.
  [`VehiclesEndpointTests.cs:587`](../../tests/BrunoVehicleHire.Integration.Tests/VehiclesEndpointTests.cs#L587)

**The DataTable evolution (a breaking change to an already-shipped component)**

- `DataTable`'s three new generic capabilities, still entity-agnostic.
  [`data-table.ts`](../../frontend/src/app/shared/data-table/data-table.ts)

- `vehicles-page`'s per-row `actions` function — where all the Vehicle-specific meaning actually lives.
  [`vehicles-page.ts:102`](../../frontend/src/app/features/vehicles/vehicles-page.ts#L102)

**Peripherals**

- `onRestoreClick` — no `ConfirmDialog`, direct mutate, per-row error state.
  [`vehicles-page.ts:206`](../../frontend/src/app/features/vehicles/vehicles-page.ts#L206)
