---
title: 'Story 2.2: Edit a Vehicle'
type: 'feature'
created: '2026-09-12'
status: 'done'
review_loop_iteration: 0
context: []
baseline_commit: '1a3d4399d0a232b68f31ff803f3aeace0a9a7dc7'
---

<frozen-after-approval reason="human-owned intent — do not modify unless human renegotiates">

## Intent

**Problem:** Vehicles can only be created, never corrected. `Vehicle` has no post-construction mutation beyond `SoftDelete()` (Story 1.3). Nothing yet triggers an Edit flow from the list (no per-row actions exist on `DataTable`). `Modal`'s close behavior (Story 2.1) closes immediately with no dirty-check — deliberately deferred until a real caller needed one, which is now. `ConfirmDialog` doesn't exist yet.

**Approach:** Add `Vehicle.Update(...)` (the second aggregate mutation method, mirroring `Create`'s invariants via one shared private validator — DRY), a `GetByIdAsync` repository method, an `UpdateVehicleCommand` reusing the exact same duplicate-registration-check/409/400 pattern as Story 2.1 (now excluding the vehicle's own id from the check). On the frontend: extend `DataTable` with a generic row-actions mechanism (needed now — Edit has to be triggered from the list, since the Vehicle Detail page doesn't exist until Story 2.5), generalize Story 2.1's `CreateVehicleModal` into one `VehicleFormModal` used for both create and edit (DRY — the two forms are ~95% identical), and build `ConfirmDialog` (neutral variant) against its first real consumer — the discard-changes guard.

**Scope decisions — flagged for approval:**

1. **`Vehicle` gains a `NotFoundException`-driven 404 path.** Updating a vehicle that doesn't exist (or was soft-deleted, since `GetByIdAsync` respects the existing query filter — there's no "show inactive" toggle to reach a soft-deleted vehicle's Edit flow until Story 2.4) needs to fail gracefully, not `NullReferenceException`. This is a genuine correctness requirement of any update-by-id endpoint, not scope creep. Added: `Application/Common/NotFoundException.cs` + a 4th `GlobalExceptionHandler` branch → `404 Not Found`. **The frontend does NOT get a new `NotFoundError` normalized-error kind for this** — a 404 falls through to the existing `ServerError` catch-all (a generic banner) for now. Story 2.2's own AC never tests a stale-id Edit attempt in the UI; Story 2.5 ("a stale or invalid vehicle id... 'This vehicle no longer exists'") is where a real, tested 404 UI treatment belongs, and building it now would be untested speculation.
2. **`DataTable` gains a generic `actions: RowAction<T>[]` input** (rendered as trailing text-link cells, per `DESIGN.md`: "Secondary actions are text links... not outlined buttons"). Genuinely required now (Edit has nowhere else to launch from) and will be reused as-is by Stories 2.3/2.4 (Deactivate/Restore) without further `DataTable` changes.
3. **`ConfirmDialog` is built with ONLY the neutral variant.** The `destructive` variant (Epic 3's Erase action) is not built — no `variant` prop, no destructive styling — until a real consumer needs it.
4. **A shared `FocusTrap` helper is extracted from `Modal`'s existing inline logic.** `EXPERIENCE.md`'s Accessibility Floor requires "Both `Modal` and `ConfirmDialog` trap focus... and return it... on close" — `ConfirmDialog` needs the exact same trap-tab/capture-trigger/return-focus behavior `Modal` already has inline. Duplicating it would be an immediate, avoidable DRY violation with two real, concrete consumers in this very story (not a speculative extraction).
5. **`CreateVehicleModal` is renamed/generalized to `VehicleFormModal`**, taking an optional `vehicle: Vehicle | null` input (`null` = create, populated = edit) — the two forms are functionally identical except initial values, HTTP verb, and copy. This is Story 2.1's own component evolving under its first second-consumer, not a rewrite of working code.

## Boundaries & Constraints

**Always:**
- `Vehicle.Update(...)` enforces the exact same invariants as `Create` (non-blank RegistrationNumber/Make/Model, positive DailyRate, plausible Year) via one shared private validation method — never duplicated logic between the two (DRY).
- `UpdateVehicleCommand` returns the updated `VehicleDto` (AD-2).
- The duplicate-RegistrationNumber check excludes the vehicle being edited: `ExistsByRegistrationNumberAsync(registrationNumber, excludingId, ct)` — Story 2.1's Create call site passes `excludingId: null` (unchanged behavior, confirmed by its existing tests still passing).
- `GetByIdAsync` respects the existing soft-delete query filter (AD-13) — consistent with every other read in this repository except the deliberately-unfiltered existence check.
- The discard-guard lives entirely in `VehicleFormModal`, NOT in `Modal` itself — `Modal` stays exactly as shipped in Story 2.1 (SRP: it doesn't know what a "dirty form" is). `VehicleFormModal` intercepts `Modal`'s `closeRequest`; if the form is dirty, it shows `ConfirmDialog` instead of changing `Modal`'s `open` state — meaning `Modal` never actually closes (and never runs its own focus-return logic) until the user confirms the discard, which is exactly why "cancelling the discard returns focus to the exact field" falls out naturally rather than needing special-case tracking.
- `ConfirmDialog` (like `Modal`) traps focus and returns it to whatever had focus when it opened — using the same extracted `FocusTrap` helper.
- Escaping while `ConfirmDialog` is open dismisses only the `ConfirmDialog` (`EXPERIENCE.md`: "Escape closes the topmost modal/dialog only") — it must not also bubble to `Modal`'s own Escape handling.

**Ask First:** Nothing expected to trigger.

**Never:** No `destructive` `ConfirmDialog` variant yet (Scope decision 3). No frontend `NotFoundError` kind yet (Scope decision 1). No "show inactive" toggle or Restore action (Story 2.4). No booking-history section (Story 4.5). No changes to `SoftDelete()` or any other existing `Vehicle` method's behavior.

## I/O & Edge-Case Matrix

| Scenario | Input / State | Expected Output / Behavior | Error Handling |
|----------|--------------|---------------------------|----------------|
| Valid edit | Change DailyRate on an existing vehicle | `200 OK`, updated `VehicleDto`; list reflects it immediately | N/A |
| Duplicate RegistrationNumber (different vehicle) | Change to another vehicle's existing RegistrationNumber | `409 Conflict`, same inline-error pattern as Story 2.1 | Inline error under RegistrationNumber |
| Unchanged RegistrationNumber | Submit without changing RegistrationNumber | Succeeds — the exclude-self check must not flag a vehicle's own current value as "duplicate" | N/A |
| Vehicle doesn't exist / soft-deleted | Stale id | `404 Not Found` | Falls through to generic `ServerError` banner on the frontend (Scope decision 1) |
| Form untouched, Escape/backdrop | No fields changed | Closes immediately, no `ConfirmDialog` (matches `EXPERIENCE.md`'s Modal spec: "no confirmation for a still-empty/untouched form") | N/A |
| Form touched, Escape/backdrop | At least one field changed | `ConfirmDialog` ("Discard changes?") appears; Modal does not close | N/A |
| Cancel the discard | User clicks "Keep editing" | `ConfirmDialog` closes, focus returns to the exact field the user was on, Modal stays open with changes intact | N/A |
| Confirm the discard | User clicks "Discard" | Modal closes, form resets, focus returns to the row's "Edit" trigger | N/A |

## Code Map

**Backend:**
- `src/BrunoVehicleHire.Domain/Vehicle.cs` -- modify -- extract `ValidateInvariants(...)` (private static) from `Create`, add `public void Update(string registrationNumber, string make, string model, int year, decimal dailyRate, TimeProvider? timeProvider = null)` calling the same validator then reassigning the private-setter properties
- `tests/BrunoVehicleHire.Domain.Tests/VehicleTests.cs` -- modify -- add `Update(...)` coverage mirroring `Create`'s existing I/O-matrix tests (every invalid-input case, one valid-update case); confirm all existing `Create` tests still pass unchanged
- `src/BrunoVehicleHire.Application/Common/NotFoundException.cs` -- new -- `(string Entity, Guid Id)`, framework-free
- `src/BrunoVehicleHire.Api/ExceptionHandling/GlobalExceptionHandler.cs` -- modify -- 4th branch: `NotFoundException` → `404 Not Found`
- `src/BrunoVehicleHire.Api/ExceptionHandling/ProblemTypeUris.cs` -- modify -- add a fixed `NotFound` constant (single URI, mirroring `ValidationFailure`'s "one fixed type, detail carries the specifics" reasoning)
- `src/BrunoVehicleHire.Application/Vehicles/IVehicleRepository.cs` -- modify -- add `Task<Vehicle?> GetByIdAsync(Guid id, CancellationToken ct)`; change `ExistsByRegistrationNumberAsync` to `(string registrationNumber, Guid? excludingId, CancellationToken ct)`
- `src/BrunoVehicleHire.Infrastructure/Repositories/VehicleRepository.cs` -- modify -- implement `GetByIdAsync` (respects the query filter); update `ExistsByRegistrationNumberAsync` to exclude `excludingId` when provided
- `src/BrunoVehicleHire.Application/Vehicles/Commands/CreateVehicleCommandHandler.cs` -- modify -- update its `ExistsByRegistrationNumberAsync` call site to pass `excludingId: null`
- `src/BrunoVehicleHire.Application/Vehicles/Commands/UpdateVehicleCommand.cs` -- new -- `record(Guid VehicleId, string RegistrationNumber, string Make, string Model, int Year, decimal DailyRate) : IRequest<VehicleDto>`
- `src/BrunoVehicleHire.Application/Vehicles/Commands/UpdateVehicleCommandValidator.cs` -- new -- identical shape rules to `CreateVehicleCommandValidator`
- `src/BrunoVehicleHire.Application/Vehicles/Commands/UpdateVehicleCommandHandler.cs` -- new -- `GetByIdAsync` (throw `NotFoundException` if null) → existence check excluding self → `vehicle.Update(...)` → `SaveChangesAsync` → mapped `VehicleDto`
- `src/BrunoVehicleHire.Api/Controllers/VehiclesController.cs` -- modify -- `[HttpPut("{id:guid}")]` binding `[FromBody] UpdateVehicleCommand`, overriding its `VehicleId` from the route via `command with { VehicleId = id }` (route wins over any body value for id -- avoids a second near-duplicate request DTO)
- `tests/BrunoVehicleHire.Application.Tests/Vehicles/UpdateVehicleCommandValidatorTests.cs`, `UpdateVehicleCommandHandlerTests.cs` -- new
- `tests/BrunoVehicleHire.Api.Tests/ExceptionHandling/GlobalExceptionHandlerTests.cs` -- modify -- `NotFoundException` → 404 branch tests
- `tests/BrunoVehicleHire.Integration.Tests/VehiclesEndpointTests.cs` -- modify -- add `PUT` coverage: success, duplicate-registration-409 (different vehicle), unchanged-registration-succeeds, 404 stale id, 400 blank field

**Frontend:**
- `frontend/src/app/shared/a11y/focus-trap.ts` (+ `.spec.ts`) -- new -- extracted from `Modal`'s current inline logic: capture-trigger-on-activate, focus-first-element, trap-tab, return-focus-on-deactivate
- `frontend/src/app/shared/modal/modal.ts` -- modify -- refactored to use `FocusTrap` instead of its own inline copy of the same logic; no behavior change (existing `modal.spec.ts` must still pass unchanged)
- `frontend/src/app/shared/confirm-dialog/confirm-dialog.ts` (+ `.html` + `.spec.ts`) -- new -- neutral variant only; `open`, `title`, `message` inputs, `confirm`/`cancel` outputs; uses `FocusTrap`; Escape triggers `cancel` and calls `stopPropagation()`
- `frontend/src/app/shared/data-table/data-table.ts` -- modify -- add `actions = input<RowAction<T>[]>([])`; `RowAction<T> { label: string; onClick: (row: T) => void }`; renders a trailing actions cell per row (text-link styling) only when `actions().length > 0`
- `frontend/src/app/shared/data-table/data-table.html` -- modify -- the trailing actions column/cells
- `frontend/src/app/features/vehicles/create-vehicle-modal.ts` (+`.html`+`.spec.ts`) -- rename/generalize to `frontend/src/app/features/vehicles/vehicle-form-modal.ts` (+`.html`+`.spec.ts`) -- add `vehicle = input<Vehicle | null>(null)`; pre-populate the `FormGroup` when `vehicle` is non-null; branch `POST`/`PUT` and the success toast copy ("Vehicle created."/"Vehicle updated.") on its presence; intercept `Modal`'s `closeRequest` with the dirty-check → `ConfirmDialog` flow described in Boundaries & Constraints
- `frontend/src/app/features/vehicles/vehicles-page.ts` (+`.html`) -- modify -- wire `DataTable`'s new `actions` input with an "Edit" action opening `VehicleFormModal` with the clicked row's `Vehicle`; the existing "+ New Vehicle" button continues to open it with `vehicle=null`

## Tasks & Acceptance

**Execution — backend (TDD throughout):**
- [x] `Vehicle.Update(...)` -- write failing Domain.Tests first (every invalid-input case, one valid-update case), extract the shared `ValidateInvariants`, then implement; confirm all pre-existing `Create` tests still pass
- [x] `NotFoundException` + `GlobalExceptionHandler`'s 404 branch -- write failing tests first
- [x] `IVehicleRepository.GetByIdAsync` + `ExistsByRegistrationNumberAsync`'s `excludingId` parameter -- update `CreateVehicleCommandHandler`'s call site, confirm its existing tests still pass unchanged
- [x] `UpdateVehicleCommand`/`Validator`/`Handler` -- write failing tests first (success; duplicate-excluding-self correctly ignores the vehicle's own current value; duplicate against a different vehicle throws; not-found throws `NotFoundException`)
- [x] `VehiclesController`'s `PUT` action
- [x] `VehiclesEndpointTests` -- write failing integration tests first for every backend I/O-matrix row
- [x] Full `dotnet test`; re-run `ArchitectureFitnessTests`; force `dotnet restore` and check for NU1903 warnings; explicit SOLID/DRY/YAGNI self-check -- confirmed: 131/131 passing, 0 vulnerabilities

**Execution — frontend (TDD throughout):**
- [x] Extract `FocusTrap` from `Modal` -- write failing tests first for the extracted helper in isolation, refactor `Modal` to use it, confirm `modal.spec.ts` passes unchanged
- [x] `ConfirmDialog` (neutral only) -- write failing tests first (renders when open, `confirm`/`cancel` emit correctly, focus traps/returns, Escape cancels and stops propagation)
- [x] `DataTable`'s `actions` input -- write failing tests first, then implement
- [x] Generalize `CreateVehicleModal` → `VehicleFormModal` -- write failing tests first for: edit mode pre-populates fields from `vehicle`; a successful edit calls `PUT` with the right payload, invalidates the list query, shows "Vehicle updated.", closes; untouched-form Escape closes immediately (no `ConfirmDialog`); touched-form Escape shows `ConfirmDialog`; confirming discard closes and resets; cancelling discard returns focus to the exact field and keeps the form's values intact; a 409 against a different vehicle's RegistrationNumber still renders inline (regression-proving Story 2.1's pattern still works in edit mode)
- [x] Wire the "Edit" row action into `vehicles-page`
- [x] `ng build` + `ng test --coverage --watch=false` clean; `npm audit` clean; explicit SOLID/DRY/YAGNI self-check -- confirmed: 114/114 passing, 96.73% stmt coverage, 0 vulnerabilities, `modal.spec.ts` diff was literally zero after the FocusTrap refactor

**Acceptance Criteria (epics.md, verbatim intent):**
- Given an existing vehicle and its Edit Modal open, when I change the DailyRate and submit, then the change saves and the Vehicles list reflects it immediately
- Given I attempt to change RegistrationNumber to one already used by a different vehicle, when I submit, then `409 Conflict` with the same inline error pattern as Story 2.1
- Given I've made changes in the Edit Modal, when I press Escape or click the backdrop, then a "Discard changes?" neutral `ConfirmDialog` appears, and cancelling the discard returns focus to the exact field I was on, not the Modal's first field

## Spec Change Log

## Design Notes

**Why `GetByIdAsync` doesn't ignore the query filter (unlike `ExistsByRegistrationNumberAsync`):** those two methods answer different questions. The existence check must match the DB's actual unfiltered unique-constraint scope to avoid a raw `DbUpdateException`. `GetByIdAsync` answers "can this vehicle currently be edited," and there's no UI path to a soft-deleted vehicle's Edit flow yet (Story 2.4 adds the "show inactive" toggle) — so treating a soft-deleted vehicle as "not found" for Edit purposes today is the correct, simplest behavior, not an oversight.

**Why the route id wins over any body id in `PUT`:** `command with { VehicleId = id }` means a client-supplied `vehicleId` in the JSON body (if any) is silently overwritten by the URL segment — a common, deliberate REST convention (the resource identity comes from the URL, the body describes the desired state), avoiding a second near-duplicate `UpdateVehicleRequest` DTO purely to split route-bound from body-bound fields.

**Why `ConfirmDialog`'s cancel path needs no explicit "return to this field" tracking:** because `Modal`'s `open` state never actually changes during the discard-guard flow (see Boundaries & Constraints) — the field the user was on never loses focus in the first place until `ConfirmDialog` itself takes it to ask the question, and `ConfirmDialog`'s own focus-return (via the shared `FocusTrap`) naturally gives it back. No bespoke "remember which field" state is needed anywhere in `VehicleFormModal`. Confirmed working live in-browser, not just asserted in a unit test.

**`ConfirmDialog` stops propagation on every keydown, not just Escape:** since it's rendered nested inside `Modal`'s own projected content during the discard-guard flow, an unstopped Tab would otherwise bubble to `Modal`'s `FocusTrap`, which would cycle across the combined form-and-dialog focusable set instead of staying confined to the topmost dialog — the same "topmost dialog owns keyboard interaction" principle `EXPERIENCE.md` states for Escape extends naturally to Tab.

## Verification — actual results

- Backend: `dotnet build` 0 warnings/0 errors; `dotnet test` 131/131 passing (up from 93 before this story); no vulnerability warnings; `ArchitectureFitnessTests` unchanged/passing. Live manual proof against the real docker-compose Postgres for every PUT scenario including the exclude-self regression case.
- Frontend: `ng build` 0 errors/warnings (574.12kB, within the 650kB budget); `ng test --coverage --watch=false` 114/114 passing, 96.73% statement coverage; `npm audit` 0 vulnerabilities. `Modal`'s existing spec file had a literal zero-line diff after the `FocusTrap` extraction.
- Full live end-to-end: backend + `ng serve` both running against the real docker-compose Postgres. Edited a real vehicle's DailyRate (list updated immediately), triggered a real 409 by editing one vehicle's RegistrationNumber to collide with another's (inline error rendered correctly), and drove the full discard-guard flow live — typed a change, pressed Escape, `ConfirmDialog` appeared with the Modal still open, clicked "Keep editing," confirmed via `document.activeElement` that focus returned to the exact field with the typed value intact, then re-triggered and confirmed "Discard" correctly closed everything and returned focus to the row's "Edit" trigger.

</frozen-after-approval>

## Suggested Review Order

**The discard-guard composition (the point of this story)**

- `VehicleFormModal.onModalCloseRequest`/`onConfirmDiscard`/`onCancelDiscard` — the whole mechanism: `Modal`'s `open` state is never touched by the discard flow, which is why focus-return falls out for free.
  [`vehicle-form-modal.ts:168`](../../frontend/src/app/features/vehicles/vehicle-form-modal.ts#L168)

- `FocusTrap` — the extracted DRY helper both `Modal` and `ConfirmDialog` share.
  [`focus-trap.ts`](../../frontend/src/app/shared/a11y/focus-trap.ts)

- `ConfirmDialog.onKeydown`'s unconditional `stopPropagation()` — why Tab and Escape both need it once nested inside an open `Modal`.
  [`confirm-dialog.ts:56`](../../frontend/src/app/shared/confirm-dialog/confirm-dialog.ts#L56)

**The backend write path**

- `Vehicle.Update`/`ValidateInvariants` — the DRY extraction, confirmed byte-identical to `Create`'s original checks.
  [`Vehicle.cs`](../../src/BrunoVehicleHire.Domain/Vehicle.cs)

- `UpdateVehicleCommandHandler` — fetch → not-found → exclude-self duplicate check → mutate → save → map.
  [`UpdateVehicleCommandHandler.cs`](../../src/BrunoVehicleHire.Application/Vehicles/Commands/UpdateVehicleCommandHandler.cs)

**Proof (test-first, not test-after)**

- The exclude-self regression test — the easiest thing to get backwards in this whole story.
  [`VehiclesEndpointTests.cs:424`](../../tests/BrunoVehicleHire.Integration.Tests/VehiclesEndpointTests.cs#L424)

**Peripherals**

- `DataTable`'s `RowAction<T>` extension.
  [`data-table.ts`](../../frontend/src/app/shared/data-table/data-table.ts)
