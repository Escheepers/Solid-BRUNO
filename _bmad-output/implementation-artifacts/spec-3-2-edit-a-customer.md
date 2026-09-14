---
title: 'Story 3.2: Edit a Customer'
type: 'feature'
created: '2026-09-14'
status: 'done'
review_loop_iteration: 0
context: []
baseline_commit: 'a015584b8cd78e3231fd9e5ec49b55f14fc32da4'
---

<frozen-after-approval reason="human-owned intent — do not modify unless human renegotiates">

## Intent

**Problem:** Customers can only be created, never corrected. `ICustomerRepository.ExistsByEmailAsync` already anticipated this (Story 3.1 built its `excludingId` parameter specifically so this story wouldn't need a breaking signature change), but nothing else exists yet: no `Customer.Update()`, no `GetByIdAsync`, no Edit UI.

**Approach:** A near-exact mirror of Vehicle's Story 2.2 — but meaningfully smaller, since the hard part (extracting `FocusTrap`, building `ConfirmDialog`) already happened there and is reused verbatim here. `CreateCustomerModal` generalizes into `CustomerFormModal` exactly as `CreateVehicleModal` became `VehicleFormModal`, including the identical discard-changes-guard composition (Modal's `open` state never changes while `ConfirmDialog` decides the outcome).

**Given the reduced scope (no new shared components, everything backend-side mirrors an already-proven pattern), this story is implemented as a single combined dispatch**, not the two-phase split used for Story 3.1.

## Boundaries & Constraints

**Always:**
- `Customer.Update(...)` reuses the exact same `ValidateInvariants` private method `Create()` already calls (Story 3.1) — no duplicated invariant logic, mirrors `Vehicle.Update()`'s exact DRY pattern.
- `ICustomerRepository.GetByIdAsync(Guid id, CancellationToken ct)` respects the existing `!IsDeleted` query filter — Customer has no soft-delete/restore yet (Story 3.4), so this is equivalent to "any customer" today, but mirrors Vehicle's exact evolution: `GetByIdAsync` (filtered) arrives with Edit; a `GetByIdIncludingSoftDeletedAsync` variant arrives only when Story 3.4's Restore actually needs one.
- `UpdateCustomerCommandHandler`'s duplicate-email check passes `excludingId: request.CustomerId` to `ExistsByEmailAsync` — a customer keeping their own current email must succeed, never false-positive as a duplicate (the same regression class Vehicle's Story 2.2 explicitly tested).
- `CustomerFormModal`'s discard-guard is byte-for-byte the same composition `VehicleFormModal` already uses: intercept `Modal`'s `closeRequest`; if `form.dirty`, show `ConfirmDialog` instead of changing `Modal`'s `open` state; only actually close on confirm. No new `Modal`/`ConfirmDialog`/`FocusTrap` code — these are reused exactly as shipped in Story 2.2.
- The "Edit" row action is added to `customers-page`'s `DataTable` usage via the existing `actions: (row: T) => RowAction<T>[]` function shape (Story 2.4's API) — Customer has only one row state right now (no active/inactive split yet), so the function returns the same one-entry array for every row.

**Ask First:** Nothing expected to trigger.

**Never:** No Deactivate/Restore/Anonymize (Stories 3.4/3.5). No `Bookings` table (Story 3.3). No changes to `Modal`/`ConfirmDialog`/`FocusTrap` themselves.

## I/O & Edge-Case Matrix

| Scenario | Input / State | Expected Output / Behavior | Error Handling |
|----------|--------------|---------------------------|----------------|
| Valid edit | Change PhoneNumber on an existing customer | `200 OK`, updated `CustomerDto`; PhoneNumber remains encrypted at rest; list reflects it immediately | N/A |
| Duplicate Email (different customer) | Change to another customer's existing Email | `409 Conflict`, same inline-error pattern as Story 3.1 | Inline error under Email |
| Unchanged Email | Submit without changing Email | Succeeds — `excludingId` must not flag a customer's own current value as "duplicate" | N/A |
| Form untouched, Escape/backdrop | No fields changed | Closes immediately, no `ConfirmDialog` | N/A |
| Form touched, Escape/backdrop | At least one field changed | `ConfirmDialog` ("Discard changes?") appears; Modal does not close | N/A |

## Code Map

**Backend:**
- `src/BrunoVehicleHire.Domain/Customer.cs` -- modify -- add `public void Update(string firstName, string lastName, string email, string phoneNumber, TimeProvider? timeProvider = null)` calling the existing `ValidateInvariants`, then reassigning the four mutable fields
- `tests/BrunoVehicleHire.Domain.Tests/CustomerTests.cs` -- modify -- add `Update(...)` coverage mirroring `VehicleTests.cs`'s `Update` tests (every invalid-input case, one valid-update case); confirm all existing `Create` tests still pass unchanged
- `src/BrunoVehicleHire.Application/Customers/ICustomerRepository.cs` -- modify -- add `Task<Customer?> GetByIdAsync(Guid id, CancellationToken ct)`
- `src/BrunoVehicleHire.Infrastructure/Repositories/CustomerRepository.cs` -- modify -- implement it (plain filtered lookup, mirrors `VehicleRepository.GetByIdAsync` exactly)
- `src/BrunoVehicleHire.Application/Customers/Commands/UpdateCustomerCommand.cs`, `UpdateCustomerCommandValidator.cs`, `UpdateCustomerCommandHandler.cs` -- new -- mirror `UpdateVehicleCommand`'s exact shape (validator: same field rules as Create; handler: `GetByIdAsync` 404-if-null → `ExistsByEmailAsync(email, customerId, ct)` 409-if-duplicate → `customer.Update(...)` → `SaveChangesAsync` → `CustomerDto.FromDomain`)
- `src/BrunoVehicleHire.Api/Controllers/CustomersController.cs` -- modify -- `[HttpPut("{id:guid}")]`, mirrors `VehiclesController.Update`'s exact shape (route id wins over any body `customerId` via `command with { CustomerId = id }`)
- `tests/BrunoVehicleHire.Application.Tests/Customers/UpdateCustomerCommandValidatorTests.cs`, `UpdateCustomerCommandHandlerTests.cs` -- new
- `tests/BrunoVehicleHire.Integration.Tests/CustomersEndpointTests.cs` -- modify -- add `PUT` coverage: success + a raw-DB check confirming PhoneNumber is still encrypted after the edit (not just after create), duplicate-email-vs-different-customer 409, own-unchanged-email succeeds, 404 stale id, 400 blank field

**Frontend:**
- `frontend/src/app/features/customers/create-customer-modal.ts` (+`.html`+`.spec.ts`) -- rename/generalize to `frontend/src/app/features/customers/customer-form-modal.ts` (+`.html`+`.spec.ts`) -- add `customer = input<Customer | null>(null)`; pre-populate the `FormGroup` when non-null (mirror `VehicleFormModal`'s exact `effect()`-on-open-transition pattern); branch `POST`/`PUT` and the title/toast copy on its presence; add the discard-guard (`showDiscardConfirm` signal + `ConfirmDialog` usage) -- copy `VehicleFormModal`'s implementation directly, this is not new logic
- `frontend/src/app/features/customers/customers.service.ts` -- modify -- add `useUpdateCustomerMutation()` mirroring the vehicle update-mutation pattern
- `frontend/src/app/features/customers/customers-page.ts` (+`.html`) -- modify -- add an `actions: (customer: Customer) => RowAction<Customer>[]` function with an "Edit" entry, wired to open `CustomerFormModal` with the clicked row's `Customer`

## Tasks & Acceptance

**Execution (TDD throughout):**
- [x] `Customer.Update(...)` -- write failing tests first, then implement
- [x] `ICustomerRepository.GetByIdAsync` -- write failing tests first, then implement
- [x] `UpdateCustomerCommand`/`Validator`/`Handler` -- write failing tests first (success; duplicate-excluding-self correctly ignores the customer's own current email; duplicate against a different customer throws; not-found throws)
- [x] `CustomersController`'s `PUT` action
- [x] `CustomersEndpointTests` -- write failing integration tests first for every backend I/O-matrix row, including the post-edit encryption check
- [x] Generalize `CreateCustomerModal` → `CustomerFormModal` -- write failing tests first (edit-mode pre-population; successful edit calls `PUT`, invalidates the list, shows "Customer updated.", closes; untouched-form Escape closes immediately; touched-form Escape shows `ConfirmDialog`; confirming discard closes/resets; cancelling discard keeps the form's values and returns focus correctly; a 409 against a different customer's email still renders inline)
- [x] Add the "Edit" row action to `customers-page`
- [x] Full `dotnet test` + `ng test --coverage --watch=false`; re-run `ArchitectureFitnessTests`; force `dotnet restore`/`npm audit` and check for vulnerabilities; explicit SOLID/DRY/YAGNI self-check -- confirmed: 253/253 backend + 178/178 frontend passing, 0 vulnerabilities

**Acceptance Criteria (epics.md, verbatim intent):**
- Given an existing customer and their Edit Modal open, when I change their PhoneNumber and submit, then the change saves, remains encrypted at rest, and the list reflects it
- Given I attempt to change Email to one already used by a different customer, when I submit, then `409 Conflict`, same inline pattern as Story 3.1
- Given I've made changes in the Edit Modal, when I try to close without saving, then the same "Discard changes?" neutral `ConfirmDialog` from Story 2.2 appears

## Spec Change Log

## Design Notes

**Why this story is smaller than Vehicle's Story 2.2:** 2.2 had to invent `ConfirmDialog` and extract `FocusTrap` from `Modal`'s inline logic — genuinely new shared infrastructure. This story has zero new shared components; it only wires already-proven, already-tested pieces to a second entity. The only real new logic is the backend `Update`/`GetByIdAsync`/`UpdateCustomerCommand` slice, which itself mirrors Vehicle's exact shape.

**Reviewer fix, post-implementation:** the delivered `Customer.Update(...)` initially accepted an unused `TimeProvider?` parameter, kept only for superficial signature symmetry with `Vehicle.Update()` (which genuinely needs one, for its Year invariant). Customer has no time-dependent invariant, so this was a real YAGNI violation -- an accepted parameter with zero callers relying on it. Removed the parameter (and the one test call site that redundantly passed it); reran the full suite to confirm nothing else depended on the old five-argument signature.

## Verification — actual results

- Backend: `dotnet build` 0 warnings/0 errors; `dotnet test` 253/253 passing (up from 217 before this story, after the reviewer's YAGNI fix); no vulnerability warnings; `ArchitectureFitnessTests` unchanged/passing.
- Frontend: `ng build` 0 errors/warnings (589.54kB, within the 650kB budget); `ng test --coverage --watch=false` 178/178 passing, 97.74% statement coverage; `npm audit` 0 vulnerabilities.
- The discard-guard composition confirmed identical to `VehicleFormModal`'s: `Modal`'s `[open]` binding is never touched by the discard flow, only `ConfirmDialog`'s own `open` (`showDiscardConfirm()`) changes.
- The post-edit encryption test and the exclude-self regression test both read directly, confirmed genuinely rigorous (the same test exercises both "email unchanged" and "PhoneNumber re-encrypted after edit" simultaneously).
- No live browser end-to-end check this time -- the implementer judged the existing automated coverage (real-Postgres integration tests exercising the full encryption path, full component tests exercising the discard-guard against a real reactive form) sufficient, and avoided touching the shared dev database unnecessarily. Reviewer accepted this judgment given the strength of the automated proof.

</frozen-after-approval>
