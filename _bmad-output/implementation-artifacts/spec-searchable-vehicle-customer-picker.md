---
title: 'Bug fix: Searchable Vehicle/Customer pickers in Booking-create'
type: 'bugfix'
created: '2026-09-16'
status: 'done'
review_loop_iteration: 0
context: []
baseline_commit: '203bcdd594d06323f707f923ecf48ec45393d830'
---

<frozen-after-approval reason="human-owned intent — do not modify unless human renegotiates">

## Intent

**Problem:** The Vehicle/Customer pickers in the Booking-create modal are plain native `<select>` elements (Story 4.1's Scope decision 6 — a deliberate but now-reconsidered YAGNI trade-off: "no new shared `Select` component yet, first consumer"). With real data volumes this is a genuine usability problem — user-reported. The original UX mockup already depicted a searchable typeahead for the customer picker.

**Approach:** Build one new, generic shared `Combobox<T>` component (a real ARIA `combobox` + `listbox` pattern, `ControlValueAccessor` so it drops into `formControlName` with zero `FormGroup` changes) and swap both pickers in `BookingFormModal` to use it — the second consumer that now justifies extracting this shared component, mirroring how `CapturingLoggerProvider`/`createConfirmableAction` were each extracted on their own second-real-consumer trigger.

## Boundaries & Constraints

**Always:**
- `Combobox<T>` is generic over the option type (mirrors `DataTable<T>`'s own precedent) — takes `options: T[]`, a caller-supplied `optionLabel: (option: T) => string`, and `optionValue: (option: T) => string` (the value written to the form control, e.g. `.id`). It has no idea what a "vehicle" or "customer" is (SRP) — `BookingFormModal` supplies `vehicleLabel`/`customerLabel` (already exist) as `optionLabel`.
- Implements `ControlValueAccessor` (mirrors `Input`'s exact pattern) so both pickers stay `<app-combobox formControlName="vehicleId" .../>` — no change to `BookingFormModal`'s `FormGroup` definition, validators, or submit/error-handling logic.
- Real ARIA Combobox (List Autocomplete) pattern: a text input with `role="combobox"`, `aria-expanded`, `aria-controls` pointing to the popup, `aria-autocomplete="list"`; the popup is `role="listbox"` with `role="option"` children; the active option is tracked via `aria-activedescendant`, not just visual highlighting.
- Filtering is a case-insensitive substring match against `optionLabel(option)`, live as the user types (no debounce needed — filtering an in-memory list already loaded by `useVehiclesQuery`/`useCustomersQuery`, not a new network call).
- Keyboard: ArrowDown/ArrowUp move the active option (opens the popup on first ArrowDown if closed); Enter selects the active option and closes the popup; Escape closes the popup and reverts the visible text to the currently selected option's label (does not clear the selection); Tab/blur closes the popup without altering the selection.
- When a value is set externally via `writeValue` (form patch, or `BookingFormModal.onCustomerCreated`'s `setValue`) before or after `options` finishes loading, the displayed text correctly resolves to that option's label once `options` is available — never stuck blank.
- No behavior change to anything already correct: the 400/404/409 field-level error paths, the `PICKER_PAGE_SIZE` cap, `showInactive: false` filtering, and the nested "+ New Customer" flow (including spec-6-4's focus-return-to-Customer-picker fix) all continue working exactly as today, now against the combobox's own focusable text-input element as the returned-to element.
- Both pickers keep their existing `id`/label wiring (`booking-vehicle`/`booking-customer`) and `aria-describedby` error association — `Combobox` accepts an `id` input for this.

**Ask First:** Nothing else expected to trigger.

**Never:** No debounced/server-side search (the full page is already loaded client-side, per Story 4.1's existing `PICKER_PAGE_SIZE`). No multi-select. No changes to `useVehiclesQuery`/`useCustomersQuery` or any backend endpoint. No new shared component beyond `Combobox` itself — do not also generalize `DataTable` or `Input`.

## I/O & Edge-Case Matrix

| Scenario | Input / State | Expected Output / Behavior | Error Handling |
|----------|--------------|---------------------------|----------------|
| Type a partial make/model or name | Popup open, options loaded | List filters live to matching options only | N/A |
| Type text matching nothing | No option matches | Popup shows an empty/no-matches state, no option selectable | N/A |
| ArrowDown then Enter | Popup open, options filtered | The active (highlighted) option is selected, popup closes, form control updated | N/A |
| Escape while typing an unsaved filter | A value was already selected | Popup closes, visible text reverts to the selected option's label, selection unchanged | N/A |
| `writeValue` called before `options` finishes loading (nested customer-create) | Async load still pending | Once `options` arrives, the display text resolves to the correct label (never stuck blank) | N/A |

</frozen-after-approval>

## Code Map

- `frontend/src/app/shared/combobox/combobox.ts` (+`.html`) -- new -- `Combobox<T>` component per Boundaries above; styled consistently with `{components.input}` (`frontend/src/app/shared/input/input.html`'s border/bg/radius/focus-ring classes) for the text field, plus a popup panel (`bg-surface`, `border-border`, `rounded-md`, shadow) for the listbox
- `frontend/src/app/shared/combobox/combobox.spec.ts` -- new -- filtering, keyboard nav (ArrowDown/Up/Enter/Escape), `ControlValueAccessor` contract (`writeValue`/`registerOnChange`/`registerOnTouched`), the "value set before options load" case, ARIA attributes present and correct
- `frontend/src/app/features/bookings/booking-form-modal.html` -- modify -- replace both `<select id="booking-vehicle">`/`<select id="booking-customer" #customerSelect>` blocks with `<app-combobox id="booking-vehicle" [options]="vehicles()" [optionLabel]="vehicleLabel" [optionValue]="vehicleIdOf" formControlName="vehicleId" .../>` (and the customer equivalent, keeping `#customerSelect` on it — the existing focus-return target from spec-6-4)
- `frontend/src/app/features/bookings/booking-form-modal.ts` -- modify -- import `Combobox`; `vehicleLabel`/`customerLabel` (already exist, unchanged) become the `optionLabel` inputs; add trivial `optionValue` accessors (`(v: Vehicle) => v.id`, `(c: Customer) => c.id`) since `ColumnDef`-style function inputs need one
- `frontend/src/app/features/bookings/booking-form-modal.spec.ts` -- modify -- update picker interaction helpers (currently `.value = ...; dispatchEvent(new Event('change'))` against a native `<select>`) to drive `Combobox`'s new interaction shape instead; the two spec-6-4 focus-return tests keep passing against `#customerSelect` now resolving to the combobox's internal input

## Tasks & Acceptance

**Execution (TDD throughout):**
- [x] `Combobox<T>` component + its own spec — 24 tests, written test-first: filtering, keyboard nav, CVA contract, and the async-value-resolves-once-loaded case
- [x] Wire both pickers in `BookingFormModal` to `Combobox`, updating its spec's picker-interaction helpers
- [x] Live browser check: type-filter both pickers against real seeded data, confirm keyboard-only operation, confirm the nested "+ New Customer" focus-return (spec-6-4) still lands correctly on the customer combobox
- [x] `ng test --coverage`, confirm nothing regressed — 355/355 passing

**Acceptance Criteria:**
- [x] Given the Booking-create modal's Vehicle or Customer field, when the user types, then the option list filters live to matching entries only
- [x] Given a filtered list, when the user uses Arrow keys and Enter (never touching the mouse), then the correct option is selected and the form control updates exactly as the old `<select>` did
- [x] Given an option is already selected and the user presses Escape while the filter text differs, then the selection is unchanged and the visible text reverts to the selected option's label
- [x] Given the existing 400/404/409 error paths and the nested "+ New Customer" focus-return fix (spec-6-4), when exercised against the new combobox, then they behave identically to before this change

## Spec Change Log

- **Deviation from the Boundaries' literal `id` input naming:** the component's public input is named `controlId`, not `id`. Angular reflects a plain (unbound) `id="..."` attribute onto a component's own host element *in addition to* feeding a same-named `@Input`/`input()`, silently creating a duplicate DOM `id` (both the host `<app-combobox>` tag and the inner `<input>` would carry it) — breaking `document.querySelector`/`getElementById`/`<label for>` lookups for it. `controlId` avoids the collision entirely; the effective behavior the Boundaries actually cared about (external `<label for="booking-vehicle">` correctly targets the rendered text input) is fully preserved. Found and fixed by the implementer during testing, not visible from reading the code alone.

## Design Notes

**Why a full ARIA `combobox`/`listbox` pattern rather than a simpler "filter input above a list":** a native `<select>` is itself a fully keyboard-operable, screen-reader-correct control — replacing it with anything less accessible would be a regression against this project's own Accessibility Floor (just verified end-to-end in spec-6-4). The ARIA Authoring Practices' Combobox pattern is the only well-established way to keep that same guarantee while adding free-text filtering.

**Why `Combobox<T>` is generic rather than two bespoke components:** mirrors `DataTable<T>`'s existing precedent exactly — one reusable shape, callers supply `optionLabel`/`optionValue` functions, matching this codebase's established DRY threshold (a second real consumer, here Vehicle and Customer at the same time, justifies the shared abstraction immediately rather than waiting for a third).

## Verification

Independently reproduced (not just re-reading the implementer's report), per this project's standing verification rule. The implementing agent's own session was interrupted mid-task by a usage-limit error during its own live browser check; it was resumed with full context and completed the same verification afterward — independently re-confirmed below rather than taken on trust given the interruption.

**Commands:**
- `ng test --coverage` (own run) — **355/355 passing** (was 333 before this fix; +22 net: `combobox.spec.ts`'s 24 new tests minus 2 obsoleted `<select>`-specific assertions). `Combobox` itself: 96.3% statement / 90.6% branch coverage.
- `ng build` (own run) — clean, 0 errors. Initial bundle 630.86 kB raw / 160.29 kB transfer, still within the 650 kB budget (was 575.58 kB pre-Story-6.4).

**Code read (all diffed/new files):** `combobox.ts` (the `filterOverride`/`displayText` computed-not-effect design correctly avoids the async-race the spec's I/O matrix names; `onOptionMousedown`'s `preventDefault()` correctly sequences mousedown-before-blur so a mouse click on an option is never lost to the popup closing first; Escape's `stopPropagation()` only fires while this component's own popup is open, so it never swallows the Modal's own discard-guard Escape handling when the popup is already closed — verified this distinction directly in the switch statement), `combobox.html` (real ARIA Combobox List Autocomplete pattern — `role="combobox"` on the `<input>` per current ARIA 1.2 APG, not the deprecated wrapping-div pattern), `combobox.spec.ts` (24 tests; spot-checked the trickiest one, the "resolves the displayed text once options finish loading, even when writeValue ran first" case — it genuinely empties `options`, calls `setValue` while still empty, asserts blank, then populates `options` and asserts the label resolves, which is exactly the scenario the fix addresses), `booking-form-modal.ts`/`.html` (clean swap, `vehicleIdOf`/`customerIdOf` are correctly `this`-independent pure functions so passing them unbound as template expressions is safe, `customerComboboxRef` correctly retyped from `ElementRef<HTMLSelectElement>` to `Combobox<Customer>` with focus-return now calling its public `focus()` method), `booking-form-modal.spec.ts` (picker-interaction helpers now genuinely drive the combobox via real ArrowDown/click/mousedown-then-click sequences, not a shortcut; the focus-return assertions correctly updated to expect the resolved display label, e.g. `'New Guy'`/`'Toyota Corolla — CA123456'`, rather than a raw id, matching the new component's actual semantics).
- **Live browser check, fully independent (real backend + real seeded Postgres, not the implementer's own session):** typed "Ferrari" into the Vehicle combobox — confirmed via direct DOM inspection that `aria-expanded="true"`, the filtered list narrowed to exactly "Ferrari Alpine — GU 829 984", and `aria-activedescendant` correctly pointed to that option's id. Pressed a real keyboard Enter — selection committed, input displayed the full label. Opened the nested "+ New Customer" modal, filled and submitted it, and confirmed via direct DOM inspection that the customer combobox's value became "Newly Created" and `document.activeElement` was genuinely the `#booking-customer` input. Filled dates and submitted the whole booking — confirmed via network inspection (`POST /api/bookings → 201 Created`) and a follow-up direct API query that both the new customer and the new booking (against the correct vehicle) were persisted for real, not just optimistically rendered.
- **Cleanup:** the test booking/customer created during live verification cannot be physically removed (`Booking.Cancel()` never deletes rows — AD-16 — and Customer hard-delete is blocked while any booking, including a Cancelled one, references it). Cancelled the booking, then anonymized the customer (the only further cleanup this system's own domain rules allow) rather than leaving live plaintext test PII in the dev database. Stopped both the API and frontend dev-server processes used for this verification afterward.

## Suggested Review Order

1. `frontend/src/app/shared/combobox/combobox.ts` — the component itself, especially the `filterOverride`/`displayText` computed design and the mousedown/blur sequencing
2. `frontend/src/app/shared/combobox/combobox.html` — the ARIA Combobox markup
3. `frontend/src/app/shared/combobox/combobox.spec.ts` — especially the async "value before options load" test
4. `frontend/src/app/features/bookings/booking-form-modal.ts` and `.html` — the swap from native `<select>` to `Combobox`
5. `frontend/src/app/features/bookings/booking-form-modal.spec.ts` — the updated picker-interaction helpers and focus-return assertions
