---
title: 'Story 3.4: Deactivate & Restore a Customer'
type: 'feature'
created: '2026-09-14'
status: 'done'
review_loop_iteration: 0
context: []
baseline_commit: '2d1b7912a946fc5042373774762106028f3998a3'
---

<frozen-after-approval reason="human-owned intent — do not modify unless human renegotiates">

## Intent

**Problem:** Customer can be created, edited, and hard-deleted, but has no reversible hide/unhide pair yet — the exact capability Vehicle got in Stories 2.3/2.4. This story is that pair, combined into one, mirroring Vehicle's two stories' worth of mechanism for a second entity where almost everything reusable already exists (`ConfirmDialog`, `FocusTrap`, `DataTable`'s `rowMuted`/`rowKey`/`rowError`/per-row `actions` function, the `includeInactive` query-threading pattern).

**Scope decisions — flagged for approval:**

1. **No Customer detail page exists anywhere in this project's 27-story scope** (unlike Vehicle, which got one in Story 2.5) — Epic 5's "Customer Summary" is a separate, read-only, print-only screen with no action buttons (per `EXPERIENCE.md`: "no edit buttons, no navigation chrome"), not a general detail/actions page. epics.md's AC phrase "on their detail view" is therefore read the same way Vehicle's forward-referencing stories were resolved (2.2/2.3/2.4) — **except this is a PERMANENT resolution for Customer, not a temporary stand-in awaiting a later story**: Deactivate (like Edit and Delete before it) is a row action on the Customers list.
2. **Deactivate is NOT gated on "has bookings."** epics.md's AC phrasing ("a customer with bookings... click Deactivate") describes Deactivate's realistic use case (Story 3.3 already sends zero-booking customers to hard-delete instead), not a system-enforced precondition — nothing in `domain-model.md`'s rules requires bookings to exist before deactivating. `SoftDeleteCustomerCommandHandler` does not check booking count.
3. **`Customer.Restore()` is non-idempotent** (throws `"Already active."` if already active), mirroring `Vehicle.Restore()`'s exact deliberate asymmetry with `SoftDelete()`'s idempotency (Story 2.4's precedent).
4. **Deactivate gets its own `ConfirmDialog` block, separate from Story 3.3's Delete dialog** (a second, near-identical signal-pair + dialog block in the same component) — considered and deliberately NOT extracted into a shared "confirmable action" abstraction. Two occurrences within one file doesn't yet justify a new abstraction's design cost (a "rule of three" judgment call); revisit if a third confirmation-gated action ever lands on this page.

## Boundaries & Constraints

**Always:**
- `Customer.SoftDelete()` is idempotent (mirrors `Vehicle.SoftDelete()` exactly — a second call is a no-op, not an error).
- `Customer.Restore()` throws `DomainRuleViolationException("Customer", "IsDeleted", "Already active.")` if not currently deactivated, else clears `IsDeleted` — mirrors `Vehicle.Restore()`'s exact message/shape.
- `RestoreCustomerCommandHandler` uses a NEW `GetByIdIncludingSoftDeletedAsync` repository method (mirrors `IVehicleRepository`'s exact Story-2.4 addition) — the existing filtered `GetByIdAsync` would incorrectly 404 every deactivated customer asked to be restored.
- `GetCustomersQuery`/`ICustomerRepository.GetPagedAsync` gain an `IncludeInactive`/`includeInactive` parameter defaulting to `false` (preserves every existing call site's behavior unchanged) — mirrors Vehicle's exact Story-2.4 addition.
- Deactivating untouches PII: `SoftDelete()` never reads or modifies `Email`/`PhoneNumber` — the encryption/value-converter machinery is completely unaffected, since only the `IsDeleted` flag changes.
- The per-row `actions` function on `customers-page` becomes state-dependent: an active customer shows Edit/Delete/Deactivate; a deactivated customer (visible only via the "show inactive" toggle) shows only Restore — mirrors `vehicles-page.ts`'s exact Story-2.4 pattern.
- Restore has no confirmation step (mirrors Vehicle's exact reasoning: gating an undo behind a second confirmation is friction, not safety) and uses `DataTable`'s `rowMuted`/`rowKey`/`rowError` mechanism for its dimmed styling and inline "Already active" error display — not a `ConfirmDialog`.
- Deactivate DOES use a `ConfirmDialog` (a second, separate one from Story 3.3's Delete dialog) with the "dialog stays open showing the error on failure" pattern, mirroring both Story 2.3's Vehicle-Deactivate and this same component's own Story-3.3 Delete flow.

**Ask First:** Nothing expected to trigger.

**Never:** No Customer detail page. No booking-count gate on Deactivate. No shared "confirmable action" abstraction extraction yet (Scope decision 4).

## I/O & Edge-Case Matrix

| Scenario | Input / State | Expected Output / Behavior | Error Handling |
|----------|--------------|---------------------------|----------------|
| Deactivate an active customer | Confirm the dialog | `204`; `IsDeleted` set; PII columns unchanged; excluded from default listings | N/A |
| Toggle "show inactive" | List has a deactivated customer | Deactivated customer appears, dimmed, with only "Restore" | N/A |
| Restore a deactivated customer | Click Restore | `204`; `IsDeleted` cleared; reappears in default listings | N/A |
| Restore an already-active customer | Stale UI / race | `409`, `detail`="Already active." | Inline error + dimmed-row-adjacent tint on that specific row, no toast |
| Restore a nonexistent id | Invalid id | `404` | Falls through to the generic `ServerError` treatment |

## Code Map

**Backend:**
- `src/BrunoVehicleHire.Domain/Customer.cs` -- modify -- add `SoftDelete()` and `Restore()`, mirroring `Vehicle.cs`'s exact two methods
- `tests/BrunoVehicleHire.Domain.Tests/CustomerTests.cs` -- modify -- add coverage mirroring `VehicleTests.cs`'s `SoftDelete`/`Restore` tests exactly
- `src/BrunoVehicleHire.Application/Customers/ICustomerRepository.cs` -- modify -- add `GetByIdIncludingSoftDeletedAsync`; `GetPagedAsync` gains `bool includeInactive`
- `src/BrunoVehicleHire.Infrastructure/Repositories/CustomerRepository.cs` -- modify -- implement both, mirroring `VehicleRepository`'s exact Story-2.4 implementation
- `src/BrunoVehicleHire.Application/Customers/Queries/GetCustomersQuery.cs`, `GetCustomersQueryHandler.cs` -- modify -- add `bool IncludeInactive = false`, thread through
- `src/BrunoVehicleHire.Application/Customers/Commands/SoftDeleteCustomerCommand.cs`, `SoftDeleteCustomerCommandHandler.cs` -- new -- mirrors `SoftDeleteVehicleCommand`/`Handler` exactly
- `src/BrunoVehicleHire.Application/Customers/Commands/RestoreCustomerCommand.cs`, `RestoreCustomerCommandHandler.cs` -- new -- mirrors `RestoreVehicleCommand`/`Handler` exactly
- `src/BrunoVehicleHire.Api/Controllers/CustomersController.cs` -- modify -- `Get` gains `[FromQuery] bool showInactive = false`; new `[HttpPost("{id:guid}/deactivate")]` and `[HttpPost("{id:guid}/restore")]`
- `tests/BrunoVehicleHire.Application.Tests/Customers/SoftDeleteCustomerCommandHandlerTests.cs`, `RestoreCustomerCommandHandlerTests.cs` -- new
- `tests/BrunoVehicleHire.Integration.Tests/CustomersEndpointTests.cs` -- modify -- add: deactivate success (204, excluded from default list, PII still encrypted -- reuse the raw-DB check pattern to confirm deactivating doesn't touch the ciphertext), `showInactive=true` includes it, restore success (204, reappears), restore-already-active 409 exact message, restore-nonexistent-id 404, default `showInactive` omitted/false regression test

**Frontend:**
- `frontend/src/app/features/customers/customers.service.ts` -- modify -- `CustomersQueryParams` gains `showInactive: boolean`; `useCustomersQuery`'s query key and `ApiClient.get` call both include it; add `useDeactivateCustomerMutation()` and `useRestoreCustomerMutation()`, mirroring `useDeactivateVehicleMutation`/`useRestoreVehicleMutation` exactly
- `frontend/src/app/features/customers/customers-page.ts` (+`.html`) -- modify -- a `showInactive` signal (checkbox); the `actions` function becomes state-dependent on `customer.isDeleted` (mirrors `vehicles-page.ts`'s exact Story-2.4 pattern); `rowMuted={(c) => c.isDeleted}`, `rowKey={(c) => c.id}` wired into `DataTable`; `restoreErrorRowKey`/`restoreErrorMessage` signals computed into `DataTable`'s `rowError`; a second `deactivatingCustomer`/`deactivateErrorMessage` signal pair + `ConfirmDialog` block for Deactivate (mirrors this same file's existing Delete-dialog pattern from Story 3.3, and `vehicles-page.ts`'s Deactivate pattern from Story 2.3)

## Tasks & Acceptance

**Execution — backend (TDD throughout):**
- [x] `Customer.SoftDelete()`/`Restore()` -- write failing Domain.Tests first, then implement
- [x] `GetByIdIncludingSoftDeletedAsync` + `GetPagedAsync`'s `includeInactive` param -- write failing tests first
- [x] `GetCustomersQuery`'s `IncludeInactive` param -- thread through, confirm existing tests unaffected when omitted/false
- [x] `SoftDeleteCustomerCommand`/`Handler`, `RestoreCustomerCommand`/`Handler` -- write failing tests first, mirroring the Vehicle equivalents exactly
- [x] `CustomersController`'s `showInactive` param + new `deactivate`/`restore` actions
- [x] `CustomersEndpointTests` -- write failing integration tests first for every I/O-matrix row, including the deactivate-doesn't-touch-encryption check
- [x] Full `dotnet test`; re-run `ArchitectureFitnessTests`; force `dotnet restore` and check for NU1903 warnings; explicit SOLID/DRY/YAGNI self-check -- confirmed: 293/293 passing, 0 vulnerabilities, PII proven byte-identical before/after deactivate

**Execution — frontend (TDD throughout):**
- [x] `showInactive` threading through `useCustomersQuery`; `useDeactivateCustomerMutation`/`useRestoreCustomerMutation` -- write failing tests first
- [x] Wire the toggle, the state-dependent `actions` function, the `rowMuted`/`rowKey`/`rowError` inputs, and the second `ConfirmDialog` for Deactivate into `customers-page` -- write failing tests first: toggling "show inactive" includes deactivated rows, dimmed, with only Restore; Deactivate confirmation flow (open/confirm-success/confirm-failure/cancel) mirrors the existing Delete flow's tests; Restore success clears any row error and toasts; Restore failure (simulated 409) shows the inline error + tint on that row only
- [x] `ng build` + `ng test --coverage --watch=false` clean; `npm audit` clean; explicit SOLID/DRY/YAGNI self-check -- confirmed independently: 197/197 passing (up from 184), 97.76% statement coverage, 0 vulnerabilities, `DataTable`/`ConfirmDialog` untouched

**Acceptance Criteria (epics.md, verbatim intent, adapted per Scope decision 1):**
- Given a customer, when I click "Deactivate" (distinct from "Erase personal data") and confirm the neutral `ConfirmDialog`, then `IsDeleted` is set, their PII is untouched, and they're excluded from default listings
- Given the Customers list's "show inactive" toggle, when I enable it and find a deactivated customer, then I can click "Restore," which clears `IsDeleted` and returns them to normal listings

## Spec Change Log

- **Backend implementation**: `tests/BrunoVehicleHire.Integration.Tests/CustomerRepositoryTests.cs` (a pre-existing file from Story 3.1, not listed in this spec's Code Map) failed to compile once `GetPagedAsync`'s signature gained the `includeInactive` parameter. Fixed mechanically and extended with matching `includeInactive`/`GetByIdIncludingSoftDeletedAsync` coverage rather than leaving it under-covered. Not a scope change -- a necessary consequence of the Code Map's own `GetPagedAsync` signature change.

## Design Notes

**Why Deactivate isn't extracted into a shared abstraction with Delete despite being near-identical:** two occurrences in one file is a real but modest amount of duplication (~15-20 lines each) against the real design cost of a generic "confirmable action with inline stay-open error" composable/directive (naming, API shape, how it'd interact with `DataTable`'s separate `rowError` mechanism Restore already uses). Deferred per a rule-of-three judgment call, not overlooked -- worth revisiting if Anonymize (Story 3.5) turns out to need the identical shape as a third instance.

</frozen-after-approval>

## Verification

Independently re-verified after implementation (not just the implementer's self-report):

**Backend:** `git status --short` confirmed the exact expected file set; `dotnet build BrunoVehicleHire.sln` clean (0 warnings, 0 errors); `dotnet restore --force` showed no `NU1903` advisories; full `dotnet test` 293/293 passing (Domain 63, Application 102, Infrastructure 4, Api 23, Integration 101), up from 271 before this story. Read `Customer.cs` directly and confirmed `SoftDelete()`/`Restore()` mirror `Vehicle.cs`'s exact pattern, including the precise `"Already active."` exception message and idempotent/non-idempotent asymmetry. Read `RestoreCustomerCommandHandler.cs` directly and confirmed it uses the new `GetByIdIncludingSoftDeletedAsync` (not the filtered `GetByIdAsync`) -- the single most important correctness detail for this story. Read the `Deactivate_DoesNotTouchEncryptedPiiColumns_CiphertextIsByteIdenticalBeforeAndAfter` integration test directly and confirmed it reads raw `Email`/`PhoneNumber` columns via a direct `NpgsqlConnection` before/after deactivate and asserts byte-identical ciphertext, plus separately confirms it isn't plaintext.

**Frontend:** `git status --short` confirmed the exact expected file set (`DataTable`/`ConfirmDialog` absent -- untouched); `ng build` clean (0 errors, 0 warnings, 593.47 kB / budget 650 kB); `ng test --coverage --watch=false` 197/197 passing (up from 184), 97.76% statement / 92.66% branch / 97.48% function / 97.51% line coverage; `npm audit` 0 vulnerabilities -- all reproduced independently, not taken on the implementer's word. Read `customers-page.ts`/`.html` and `customers.service.ts` directly and confirmed: the `actions` function is correctly state-dependent on `customer.isDeleted`; `rowMuted`/`rowKey`/`rowError` are wired into `DataTable` exactly as specified; a second, independent `deactivatingCustomer`/`deactivateErrorMessage` signal pair and `ConfirmDialog` block sit alongside (not merged with) Story 3.3's existing Delete dialog, per Scope decision 4; `onRestoreClick` mutates directly with no confirmation step; both new mutations mirror `useDeactivateVehicleMutation`/`useRestoreVehicleMutation`'s exact shape.

**SOLID/DRY/YAGNI:** confirmed via direct code reading -- `DataTable` and `ConfirmDialog` remain generic and unmodified (SRP); Customer's Deactivate/Restore mechanism is a line-for-line mirror of Vehicle's Story 2.3/2.4 mechanism, no new logic invented (DRY); no shared "confirmable action" abstraction was extracted despite now having two near-identical dialogs, and no booking-count gate was added to Deactivate (YAGNI, per Scope decisions 2 and 4).

A full live end-to-end pass was also run against the real API and a real dev Postgres instance (not just mocks): create → deactivate → toggle "show inactive" → restore → force a genuine 409 race on Restore (double-click) and confirm the inline "Already active." error renders on the correct row only. The dev database was left empty afterward.

## Suggested Review Order

1. `src/BrunoVehicleHire.Domain/Customer.cs` -- `SoftDelete()`/`Restore()`
2. `src/BrunoVehicleHire.Application/Customers/Commands/RestoreCustomerCommandHandler.cs` -- the `GetByIdIncludingSoftDeletedAsync` correctness detail
3. `tests/BrunoVehicleHire.Integration.Tests/CustomersEndpointTests.cs` -- the PII-untouched-by-deactivate raw-DB test
4. `frontend/src/app/features/customers/customers-page.ts` -- the state-dependent `actions` function and the second `ConfirmDialog` block
5. `frontend/src/app/features/customers/customers.service.ts` -- `useDeactivateCustomerMutation`/`useRestoreCustomerMutation`
