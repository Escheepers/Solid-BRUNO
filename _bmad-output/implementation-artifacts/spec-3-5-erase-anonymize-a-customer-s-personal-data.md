---
title: 'Story 3.5: Erase (Anonymize) a Customer''s Personal Data'
type: 'feature'
created: '2026-09-14'
status: 'done'
review_loop_iteration: 0
context: []
baseline_commit: '1d0bb8f9c29cd6a965b9f903e6a5d415c397ba3e'
---

<frozen-after-approval reason="human-owned intent — do not modify unless human renegotiates">

## Intent

**Problem:** Customer can be hard-deleted (no bookings) or deactivated (reversible), but has no irreversible erasure path yet — the second, distinct alternative `domain-model.md` requires when a customer with bookings wants their PII genuinely destroyed, never conflated with the reversible Deactivate.

**Approach:** Add `Customer.Anonymize()` (scrubs FirstName/LastName/Email/PhoneNumber to fixed placeholders, sets `IsAnonymized`, idempotent, no counterpart method ever clears it), a matching command/endpoint, and a frontend "Erase personal data" action using `ConfirmDialog`'s destructive variant — its first real consumer.

**Scope decisions — flagged for approval:**
1. **Anonymize is not gated on booking count**, mirroring Story 3.4's Scope decision 2 exactly — epics.md's "customer with bookings" AC phrasing describes the realistic case, not an enforced precondition.
2. **`AnonymizeCustomerCommandHandler` uses `GetByIdIncludingSoftDeletedAsync`** (not the filtered `GetByIdAsync`) so a deactivated customer can also be erased directly, without first requiring Restore.
3. **`Customer`'s EF Core query filter becomes `!IsDeleted && !IsAnonymized`** (AD-13). Since `GetPagedAsync`'s existing `includeInactive` flag already bypasses the filter wholesale via `IgnoreQueryFilters()`, toggling "show inactive" will also surface anonymized customers (consistent with AD-13's "same query filter" mechanism, not a new one) — displayed via the anonymized-name treatment below, with zero row actions.
4. **`ColumnDef<T>` gains an optional `cellClass?: (row: T) => string`** (defaults to omitted/no-op — every existing column is unaffected) so the Name cell can carry `DESIGN.md`'s dedicated `anonymized-text` + italic treatment, which is visually distinct from the existing muted/disabled row styling and cannot be expressed through `rowMuted` alone.
5. **Extract a small shared `createConfirmableAction` helper** (plain signal-based factory, mirrors `FocusTrap`'s "plain TS, not a component" precedent) and refactor Delete/Deactivate to use it alongside the new Erase flow. Story 3.4's own Design Notes flagged this exact trigger ("revisit if Anonymize needs the identical shape as a third instance") — it just did.

## Boundaries & Constraints

**Always:**
- `Anonymize()` sets FirstName="Anonymized", LastName="Customer", Email=`erased-{Id}@anonymized.local` (unique per customer — avoids colliding with the DB's unfiltered `EmailHash` unique index, since a fixed placeholder string would hash identically for every anonymized row), PhoneNumber="0000000000", `IsAnonymized=true`. Bypasses `ValidateInvariants` (placeholders are constructed, not user input).
- `Anonymize()` never touches `IsDeleted` — the two flags stay fully independent (domain-model.md: "never conflated").
- `Anonymize()` is idempotent (calling it again is a no-op, matching `SoftDelete()`'s precedent) — no exception, no state change.
- No method anywhere clears `IsAnonymized` back to `false`; `Restore()` continues to touch only `IsDeleted`.
- Row actions become three-way in `customers-page`: anonymized → none; soft-deleted only → `[Restore, Erase personal data]`; active → `[Edit, Delete, Deactivate, Erase personal data]`.
- `rowMuted` becomes `(c) => c.isDeleted && !c.isAnonymized` — an anonymized row gets its own distinct cell treatment instead of the generic dimmed one.
- The Erase `ConfirmDialog` uses the destructive variant (`confirm-dialog-destructive` styling) and states "permanently," "cannot be undone," and that booking history stays intact.

**Ask First:** Nothing else expected to trigger.

**Never:** No un-anonymize method, ever. No booking-count gate on Anonymize. No new DataTable capability beyond the one `cellClass` field.

## I/O & Edge-Case Matrix

| Scenario | Input / State | Expected Output / Behavior | Error Handling |
|----------|--------------|---------------------------|----------------|
| Anonymize an active customer | Confirm the destructive dialog | `204`; PII scrubbed to placeholders; `IsAnonymized=true`; excluded from default listings | N/A |
| Anonymize a deactivated customer | Customer already has `IsDeleted=true` | `204`; same scrubbing; `IsDeleted` untouched | N/A |
| Anonymize twice | Second call on an already-anonymized customer | `204`; no error, no further change | N/A |
| Anonymize a nonexistent id | Invalid id | `404` | Falls through to `ServerError` |
| "Show inactive" toggled on | List has an anonymized customer | Appears with muted-italic "Customer (anonymized)" name, no row actions | N/A |

## Code Map

**Backend:**
- `src/BrunoVehicleHire.Domain/Customer.cs` -- modify -- add `Anonymize()` per Boundaries
- `tests/BrunoVehicleHire.Domain.Tests/CustomerTests.cs` -- modify -- scrub/idempotent/`IsDeleted`-untouched/`Restore`-doesn't-clear-`IsAnonymized` tests, written first
- `src/BrunoVehicleHire.Application/Customers/Commands/AnonymizeCustomerCommand.cs`, `AnonymizeCustomerCommandHandler.cs` -- new -- mirrors `SoftDeleteCustomerCommandHandler`'s shape but uses `GetByIdIncludingSoftDeletedAsync`; no booking-count check
- `tests/BrunoVehicleHire.Application.Tests/Customers/AnonymizeCustomerCommandHandlerTests.cs` -- new -- active-customer success, deactivated-customer success, 404
- `src/BrunoVehicleHire.Infrastructure/Persistence/AppDbContext.cs` -- modify -- Customer's `HasQueryFilter` → `c => !c.IsDeleted && !c.IsAnonymized`
- `src/BrunoVehicleHire.Api/Controllers/CustomersController.cs` -- modify -- `[HttpPost("{id:guid}/anonymize")]`, mirrors `Deactivate`'s shape
- `tests/BrunoVehicleHire.Integration.Tests/CustomersEndpointTests.cs` -- modify -- every I/O-matrix row, including a raw-DB read confirming the exact placeholder values and that the DB-level EmailHash unique index tolerates anonymizing two different customers back-to-back

**Frontend:**
- `frontend/src/app/shared/data-table/data-table.ts` (+`.html`, `.spec.ts`) -- modify -- optional `ColumnDef.cellClass?: (row: T) => string`, applied on the `<td>` when present
- `frontend/src/app/shared/confirm-dialog/confirmable-action.ts` (+`.spec.ts`) -- new -- `createConfirmableAction({ mutate, defaultMessage, toErrorMessage, onSuccess })` returning `{ current, dialogMessage, open, cancel, confirm }`, per Scope decision 5
- `frontend/src/app/features/customers/customers.service.ts` (+`.spec.ts`) -- modify -- `useAnonymizeCustomerMutation()`, mirrors `useDeactivateCustomerMutation` exactly
- `frontend/src/app/features/customers/customers-page.ts` (+`.html`, `.spec.ts`) -- modify -- refactor Delete/Deactivate onto `createConfirmableAction` (behavior unchanged — existing tests should keep passing unmodified); add the Erase flow the same way with the destructive `ConfirmDialog`; three-way `actions`; updated `rowMuted`; Name/Last-Name/Email/Phone columns render placeholders + `cellClass` when `isAnonymized`

## Tasks & Acceptance

**Execution — backend (TDD throughout):**
- [x] `Customer.Anonymize()` -- write failing Domain.Tests first, then implement
- [x] `AnonymizeCustomerCommand`/`Handler` -- write failing tests first
- [x] Query filter change + `CustomersController`'s `anonymize` action
- [x] `CustomersEndpointTests` -- write failing integration tests first for every I/O-matrix row
- [x] Full `dotnet test`; re-run `ArchitectureFitnessTests`; force `dotnet restore`, check `NU1903`; explicit SOLID/DRY/YAGNI self-check -- confirmed independently: 307/307 passing, 0 vulnerabilities

**Execution — frontend (TDD throughout):**
- [x] `createConfirmableAction` -- write failing unit tests first (open/cancel/confirm-success/confirm-failure)
- [x] Refactor Delete/Deactivate onto it; add Erase the same way -- write failing tests first; confirm pre-existing Delete/Deactivate tests still pass unmodified -- confirmed: existing `delete row action`/`deactivate row action` spec blocks unchanged and passing
- [x] `ColumnDef.cellClass`, three-way `actions`, updated `rowMuted`, anonymized-row cell rendering -- write failing tests first
- [x] `ng build` + `ng test --coverage --watch=false` clean; `npm audit` clean; explicit SOLID/DRY/YAGNI self-check -- confirmed independently: 219/219 passing (up from 197), 97.91% statement coverage, 0 vulnerabilities

**Acceptance Criteria (epics.md, verbatim intent):**
- Given a customer with bookings, when I click "Erase personal data" and confirm the destructive `ConfirmDialog`, then `Customer.Anonymize()` scrubs FirstName/LastName/Email/PhoneNumber to placeholders and sets `IsAnonymized`
- Given an anonymized customer, no method anywhere can reverse it, and they're excluded from default listings and booking selection via the same query filter as `IsDeleted`

## Spec Change Log

- **Frontend implementation**: `frontend/src/app/shared/confirm-dialog/confirm-dialog.ts`/`.html`/`.spec.ts` were not listed in this spec's Frontend Code Map, but the Boundaries explicitly require a destructive `ConfirmDialog` variant, which cannot exist without modifying `ConfirmDialog` itself. Added a `variant: 'neutral' | 'destructive'` input, defaulting to `'neutral'` so Delete/Deactivate (and every other pre-existing consumer) are unaffected. The destructive variant renders a `bg-danger-text` confirm button per `DESIGN.md`'s `confirm-dialog-destructive` treatment. A Code Map omission, not a scope change -- `confirm-dialog.ts`'s own prior doc comment already earmarked this exact story as the point it gains the destructive variant.

## Design Notes

**Why `erased-{Id}@anonymized.local` and not a fixed string:** the DB's unique index on `EmailHash` (Story 3.1) is unfiltered — a fixed placeholder email would hash identically for every anonymized customer and the second anonymize call would throw `DbUpdateException`. Embedding the customer's own `Id` guarantees a unique hash per row.

**Why extract `createConfirmableAction` now, not defer again:** Story 3.4 explicitly pre-committed to revisiting at a third instance; deferring past the exact condition named for revisiting would contradict that commitment and the standing SOLID/DRY/YAGNI requirement. The extraction is deliberately small (a signal-returning factory, not a component/directive) — mirrors `FocusTrap`'s precedent — so it adds a shared *mechanism*, not a shared *policy*: each caller still supplies its own message, mutation, and success copy.

</frozen-after-approval>

## Verification

Independently re-verified after implementation (not just the implementer's self-report):

**Backend:** `git status --short` confirmed the exact expected file set; `dotnet build BrunoVehicleHire.sln` clean (0 warnings, 0 errors); full `dotnet test` 307/307 passing (Domain 67, Application 105, Infrastructure 4, Api 23, Integration 108), up from 293 before this story; `dotnet restore --force` showed no `NU1903` advisories. Read `Customer.cs` directly and confirmed `Anonymize()` is idempotent, leaves `IsDeleted` untouched, bypasses `ValidateInvariants`, and derives its Email placeholder from the customer's own `Id` (avoiding an `EmailHash` unique-index collision across different anonymized customers). Read `AnonymizeCustomerCommandHandler.cs` directly and confirmed it uses `GetByIdIncludingSoftDeletedAsync` (not the filtered `GetByIdAsync`) and has no booking-count check. Read the query filter change in `AppDbContext.cs` directly (`!c.IsDeleted && !c.IsAnonymized`) and the integration tests directly, confirming genuine coverage of every I/O-matrix row including the two-different-customers-back-to-back `EmailHash` collision proof.

**Frontend:** `git status --short` confirmed the exact expected file set; `ng build` clean (0 errors, 0 warnings, 595.55 kB / budget 650 kB); `ng test --coverage --watch=false` 219/219 passing (up from 197), 97.91% statement / 93.44% branch / 97.29% function / 97.7% line coverage; `npm audit` 0 vulnerabilities -- all reproduced independently. Read `confirmable-action.ts`, `confirm-dialog.ts`/`.html`, `data-table.ts`/`.html`, and `customers-page.ts`/`.html` directly and confirmed: the destructive variant defaults to `'neutral'` so Delete/Deactivate are unaffected; `cellClass` replaces (not layers with) the row's default muted/body text class, avoiding a Tailwind class conflict; the three-way `actions` function and `rowMuted` correctly special-case `isAnonymized`; Delete and Deactivate's existing spec test blocks are byte-for-byte unmodified and still pass against the `ConfirmableAction`-refactored implementation.

**SOLID/DRY/YAGNI:** confirmed via direct code reading -- `createConfirmableAction` is a plain, entity-agnostic signal factory (SRP/DRY, mirrors `FocusTrap`'s precedent); `ConfirmDialog`'s new `variant` input and `DataTable`'s new `cellClass` field are both minimal, backward-compatible, single-purpose additions built exactly when their first real consumer needed them (YAGNI), not spec-ahead-of-use.

## Suggested Review Order

1. `src/BrunoVehicleHire.Domain/Customer.cs` -- `Anonymize()`
2. `src/BrunoVehicleHire.Application/Customers/Commands/AnonymizeCustomerCommandHandler.cs`
3. `tests/BrunoVehicleHire.Integration.Tests/CustomersEndpointTests.cs` -- the `EmailHash` collision test
4. `frontend/src/app/shared/confirm-dialog/confirmable-action.ts` -- the extracted shared mechanism
5. `frontend/src/app/features/customers/customers-page.ts` -- the three-way `actions`/`rowMuted` and the Erase flow
6. `frontend/src/app/shared/confirm-dialog/confirm-dialog.ts`/`.html` -- the destructive variant
7. `frontend/src/app/shared/data-table/data-table.ts`/`.html` -- `cellClass`
