---
title: 'Audit fix: unmanaged subscriptions and VehicleFormModal service-layer consistency'
type: 'bugfix'
created: '2026-09-21'
status: 'done'
review_loop_iteration: 0
context: []
baseline_commit: 'b95ac9dc6a756de68dc7a7d6616d2e3b5508c161'
---

<frozen-after-approval reason="human-owned intent — do not modify unless human renegotiates">

## Intent

**Problem:** A full-spec audit found two frontend consistency gaps against `stack.md`'s expected competencies. (1) `wireClearFieldErrorOnChange()` (added in an earlier bugfix, present identically in `VehicleFormModal`/`CustomerFormModal`/`BookingFormModal`) subscribes to each form control's `valueChanges` with no `takeUntilDestroyed()`/unsubscribe — a literal "no unmanaged subscriptions" violation, low practical risk today (these Modal components persist for the page's lifetime rather than being repeatedly created/destroyed) but still worth closing properly. (2) `VehicleFormModal` injects `ApiClient` directly and builds its own inline `injectMutation` calls for create/update, unlike `CustomerFormModal`/`BookingFormModal`'s equivalents which go through `customers.service.ts`/`bookings.service.ts` — an inconsistent application of the "services as the abstraction over the API" pattern (it still routes through the centralized `ApiClient`, so the literal requirement isn't broken, but the pattern isn't applied uniformly).

**Approach:** Add `takeUntilDestroyed()` to all three `wireClearFieldErrorOnChange()` subscriptions. Extract `VehicleFormModal`'s inline create/update mutations into `vehicles.service.ts` as `useCreateVehicleMutation`/`useUpdateVehicleMutation`, mirroring `customers.service.ts`'s existing shape exactly.

## Boundaries & Constraints

**Always:**
- Each `wireClearFieldErrorOnChange()` pipes through `.pipe(takeUntilDestroyed())` (or an explicitly-injected `DestroyRef` passed to `takeUntilDestroyed(destroyRef)`, injected once as a class field so it works correctly regardless of exactly where in the constructor call chain the subscription is wired) — no other behavior change to that method.
- `useCreateVehicleMutation`/`useUpdateVehicleMutation` in `vehicles.service.ts` mirror `useCreateCustomerMutation`/`useUpdateCustomerMutation`'s exact shape (same `injectMutation<TDto, NormalizedApiError, TPayload>` generic pattern, same query-invalidation-on-success behavior `VehicleFormModal`'s current inline mutations already perform).
- `VehicleFormModal` is updated to consume the new service hooks instead of injecting `ApiClient`/building `injectMutation` itself — no change to its own form-handling logic, validation, or error-mapping.
- Existing tests for all three files are updated to match, not deleted — the modals' external behavior (what a user sees/does) does not change at all; only their internal implementation does.

**Ask First:** Nothing else expected to trigger.

**Never:** No change to `CustomerFormModal`/`BookingFormModal`'s own mutation-handling (already correct). No new shared component or abstraction beyond moving the two mutation hooks to their natural home in `vehicles.service.ts`.

## I/O & Edge-Case Matrix

<!-- No new I/O/edge-case scenarios — this is an internal-implementation-only refactor with no behavior change visible to a user. -->

</frozen-after-approval>

## Code Map

- `frontend/src/app/features/vehicles/vehicle-form-modal.ts` -- modify -- add `takeUntilDestroyed()` to `wireClearFieldErrorOnChange()` (`:140`); remove the direct `ApiClient`/`QueryClient`/inline `injectMutation` usage, consume `useCreateVehicleMutation`/`useUpdateVehicleMutation` from `vehicles.service.ts` instead
- `frontend/src/app/features/customers/customer-form-modal.ts` -- modify -- add `takeUntilDestroyed()` to `wireClearFieldErrorOnChange()` (`:147`)
- `frontend/src/app/features/bookings/booking-form-modal.ts` -- modify -- add `takeUntilDestroyed()` to `wireClearFieldErrorOnChange()` (`:203`)
- `frontend/src/app/features/vehicles/vehicles.service.ts` -- modify -- add `useCreateVehicleMutation`/`useUpdateVehicleMutation`, mirroring `customers.service.ts`'s `useCreateCustomerMutation`/`useUpdateCustomerMutation` shape exactly
- `frontend/src/app/features/vehicles/vehicle-form-modal.spec.ts`, `vehicles.service.spec.ts` -- modify -- update to test the new service-hook-based wiring instead of the inline mutation
- `frontend/src/app/features/customers/customer-form-modal.spec.ts`, `frontend/src/app/features/bookings/booking-form-modal.spec.ts` -- modify if needed -- confirm the `takeUntilDestroyed()` addition doesn't change any existing test's outcome

## Tasks & Acceptance

**Execution:**
- [x] Add `takeUntilDestroyed()` to all three `wireClearFieldErrorOnChange()` methods
- [x] Add `useCreateVehicleMutation`/`useUpdateVehicleMutation` to `vehicles.service.ts`, mirroring the Customer equivalents
- [x] Update `VehicleFormModal` to consume the new hooks; remove its direct `ApiClient`/`QueryClient` injection
- [x] Update/add tests for all touched files
- [x] Full `ng test --coverage`, confirm nothing regressed; confirm zero behavior change via a live browser pass (create/edit a vehicle, confirm success toast + list invalidation still work identically)

**Acceptance Criteria:**
- [x] Given any of the three form modals, when the component is destroyed (e.g. navigating away), then its `wireClearFieldErrorOnChange()` subscription is torn down (no lingering subscription against a destroyed component)
- [x] Given `VehicleFormModal`'s create/update flow, when exercised, then it behaves identically to before (same success/error handling, same query invalidation) while routing through `vehicles.service.ts` like `CustomerFormModal`/`BookingFormModal` already do

## Spec Change Log

## Design Notes

**Why this is a pure refactor, not a behavior change:** `VehicleFormModal`'s current inline mutations already go through the same underlying `ApiClient`/`injectMutation` machinery `useCreateCustomerMutation` wraps — moving that code into `vehicles.service.ts` changes where it lives, not what it does. The `takeUntilDestroyed()` addition is similarly inert under this app's current usage pattern (Modal components aren't repeatedly created/destroyed today) — it closes a literal correctness gap without being observable as a behavior change to any current user flow.

## Verification

Independently reproduced (not just re-reading the implementer's report), per this project's standing verification rule.

**Commands:**
- `ng test --coverage` (own run, full suite, after all four parallel audit-fixes landed) — 385/385 passing.

**Code read (all diffed files):** `vehicle-form-modal.ts` (confirmed the inline `injectMutation`/`ApiClient`/`QueryClient` usage is fully removed, replaced by `useCreateVehicleMutation`/`useUpdateVehicleMutation` consumed via an `isSubmitting` computed and explicit create/update branching in `onSubmit` — byte-for-byte equivalent behavior to before, just relocated), `vehicles.service.ts` (confirmed `useCreateVehicleMutation`/`useUpdateVehicleMutation` mirror `useCreateCustomerMutation`/`useUpdateCustomerMutation`'s exact shape), all three `wireClearFieldErrorOnChange()` methods (confirmed `takeUntilDestroyed(this.destroyRef)` correctly added, with `destroyRef` injected as its own class field exactly as the spec required — safe regardless of the exact call-chain context).

**Live verification, independently reproduced:** created and edited a vehicle through the real running app (real backend, real seeded Postgres) — confirmed the success toast, list refresh, and the duplicate-registration 409 inline-error path all behave identically to before this refactor. Cleaned up the one stray test vehicle (`ZZ999888`) left in the dev DB from the implementer's own live verification pass (deactivated via the app's own action — Vehicles have no hard-delete by design).

## Suggested Review Order

1. `frontend/src/app/features/vehicles/vehicles.service.ts` — the new `useCreateVehicleMutation`/`useUpdateVehicleMutation` hooks
2. `frontend/src/app/features/vehicles/vehicle-form-modal.ts` and `.html` — consuming the new hooks, the `isSubmitting` computed
3. The `takeUntilDestroyed()` addition in all three form modals' `wireClearFieldErrorOnChange()`
4. `frontend/src/app/features/vehicles/vehicles.service.spec.ts` — the new mutation tests
