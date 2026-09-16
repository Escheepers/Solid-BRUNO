---
title: 'Story 6.4: Accessibility Verification Pass'
type: 'feature'
created: '2026-09-16'
status: 'done'
review_loop_iteration: 0
context: []
baseline_commit: '169a8f7e227da56a0e8b39684e0d042955958e64'
---

<frozen-after-approval reason="human-owned intent — do not modify unless human renegotiates">

## Intent

**Problem:** The Accessibility Floor claimed throughout DESIGN.md/EXPERIENCE.md was never independently verified against the shipped Epics 1-5 implementation. A live investigation found DataTable's sortable-column-header accessibility (mandated by UX-DR2/EXPERIENCE.md — a real keyboard-activatable control with `aria-sort`) was never actually built, and the nested Booking→Customer modal returns focus to the wrong element on close.

**Approach:** Fix the two confirmed gaps (real keyboard-sortable DataTable headers with `aria-sort`; nested-modal focus returning to the Customer picker field, per EXPERIENCE.md's explicit carve-out for that one case), add the missing `aria-live` on the list result count (EXPERIENCE.md's FilterBar-result-count requirement), then run one consolidated verification pass (automated tests + a live browser contrast/keyboard spot-check) confirming the rest of the Accessibility Floor already holds as documented.

## Boundaries & Constraints

**Always:**
- DataTable sortable headers apply per-column client-side sort over the currently loaded page of rows only (no backend query changes) — proportional to this story's "verification pass, not a new feature epic" framing (epic-6-context.md). Ascending/descending/none cycle; `aria-sort` reflects state; a persistent (not hover-only) visual indicator shows direction; activatable via click AND Tab+Enter/Space.
- Only columns where EXPERIENCE.md calls sorting "meaningful" (date, price) get `sortable: true` — not every column.
- The nested Customer-create modal explicitly returns focus to the Booking form's Customer `<select>` on close, on both the "created" and "cancelled" paths — not wherever `FocusTrap`'s default "return to trigger" lands.
- Every other Accessibility Floor item already confirmed compliant by investigation (Top App Bar's 4-cue active state, `aria-live` on inline errors/Toast/Skeleton/Total Price, Badge/anonymized color-pairing, icon-only labels, discard-confirm-returns-to-field, contrast tokens matching DESIGN.md) is verified, not re-implemented.
- One consolidated automated test sweep covers focus-trap-and-return across every Modal/ConfirmDialog usage, not per-feature duplicate tests.

**Ask First:** Nothing else expected to trigger.

**Never:** No backend/API changes. No new shared components. No re-litigating already-corrected DESIGN.md token values.

## I/O & Edge-Case Matrix

| Scenario | Input / State | Expected Output / Behavior | Error Handling |
|----------|--------------|---------------------------|----------------|
| Click a sortable header once | Unsorted table | Rows re-sort ascending, `aria-sort="ascending"`, indicator shown | N/A |
| Click same header again | Already ascending | Re-sorts descending, `aria-sort="descending"` | N/A |
| Tab to a sortable header, press Enter/Space | Keyboard-only user | Same effect as a click | N/A |
| "+ New Customer" inside Booking-create Modal, customer created | Nested modal completes | Inner modal closes, focus lands on the Booking form's Customer select | N/A |
| Same nested modal, cancelled instead | User closes without creating | Focus still lands on the Customer select | N/A |

</frozen-after-approval>

## Code Map

- `frontend/src/app/shared/data-table/data-table.ts` -- modify -- add `sortable?: boolean` to `ColumnDef<T>` (currently `:12-35`, no sort concept), a sort-state signal, `aria-sort` computation
- `frontend/src/app/shared/data-table/data-table.html` -- modify -- header (`:8-12`, currently a plain `<th>`) becomes a real button with keydown handling + `aria-sort` + visual indicator; footer result-count span (`:62`) gets `aria-live="polite"`
- `frontend/src/app/features/vehicles/vehicles-page.ts`, `customers-page.ts`, `bookings-page.ts` -- modify -- mark date/price columns `sortable: true`
- `frontend/src/app/features/bookings/booking-form-modal.ts` -- modify -- `onCustomerModalClose`/`onCustomerCreated` (`:210-229`) explicitly focus the `#booking-customer` select instead of relying on `FocusTrap`'s default return-to-trigger
- `frontend/src/app/shared/data-table/data-table.spec.ts` -- new tests -- sort cycle, `aria-sort`, keyboard activation
- A new or extended spec under `frontend/src/app/shared/` -- new/extend -- one consolidated sweep asserting focus trap+return across every Modal/ConfirmDialog usage, including the nested case
- No other files expected to change (Top App Bar, Badge, contrast tokens, other `aria-live` usages — verified only, per investigation already confirming PASS)

## Tasks & Acceptance

**Execution:**
- [x] Add sortable header support to `DataTable` (`aria-sort`, keyboard activation, visual indicator, client-side per-page sort) — real `<button>` per header, zero custom keydown code (native Enter/Space semantics)
- [x] Mark meaningful (date/price) columns sortable in Vehicles/Customers/Bookings list pages
- [x] Add `aria-live="polite"` to `DataTable`'s result-count footer
- [x] Fix nested Booking→Customer modal focus return to the Customer picker select (both create and cancel paths)
- [x] Write/extend a consolidated focus-trap-and-return test sweep across all Modal/ConfirmDialog usages — `focus-trap-sweep.spec.ts`
- [x] Live browser spot-check: contrast in light+dark mode across list/detail/modal surfaces; keyboard-only pass through a sortable header and the nested modal case — found and fixed one new dark-mode contrast failure, see Verification
- [x] `ng test --coverage`, confirm nothing regressed — 333/333 passing

**Acceptance Criteria (epics.md, verbatim intent):**
- [x] Given every `DataTable`'s sortable column headers, when inspected, then each is a real keyboard-activatable control (Tab/Enter/Space) with `aria-sort` reflecting current state — not a click-only decoration
- [x] Given the Top App Bar, when a nav item is active, then it's marked by color, bold weight, an underline, and `aria-current="page"` together — confirmed to never rely on color alone
- [x] Given every `Modal` and `ConfirmDialog` built across Epics 1-5, including the nested inline-Customer-create-inside-Booking-create case, when each is opened and closed, then focus traps correctly while open and returns to the exact triggering element (or field) on close — verified with one consolidated test sweep
- [x] Given the full frontend surface in both light and dark mode, when contrast is spot-checked against the token values fixed in DESIGN.md, then every text/badge/background combination in active use clears WCAG 2.2 AA

## Spec Change Log

- **Finding during implementation-verification:** the "no matches" filtered-empty-state text (`vehicles-page.html`/`customers-page.html`) also lacked `aria-live`, alongside the result-count footer this spec's Boundaries named explicitly. EXPERIENCE.md's Component Patterns table covers both ("The result count/'no matches' state updates in an `aria-live=\"polite\"` region") under one requirement, so both empty-state messages were given `aria-live="polite"` too, alongside the originally-scoped result-count fix. Small, additive, no test broke.
- **Finding during implementation-verification, not anticipated at planning time:** a live contrast spot-check (per this spec's own Task) found the Top App Bar's active-nav-link text in **dark mode** measured 3.58:1 against its background — below AA — using `{colors.primary-dark}` (`#3f6fd1`) as literally specified in `DESIGN.md`'s `top-app-bar.navActiveDark` token. This is a newly-discovered failure, not a re-litigation of the earlier (already-corrected) token set the Boundaries' "Never" clause protects. Fixed by using `{colors.link-dark}` (`#7fa6ef`, 7:1) instead — already an existing design-system token for primary-blue text directly on a dark surface, value-identical to `primary` in light mode (zero light-mode visual change). `DESIGN.md`'s `navActiveDark` token reference and its Top App Bar prose, plus `EXPERIENCE.md`'s equivalent row, were corrected to match; `top-app-bar.html` now uses `text-link` instead of `text-primary` for the active state, and `top-app-bar.spec.ts` was updated accordingly. Confirmed by the user before applying (a shared design-token change).

## Design Notes

**Why client-side, current-page-only sort, not a global/backend sort:** EXPERIENCE.md's requirement is that headers are real, keyboard-accessible controls with correct `aria-sort` — not that sorting spans paginated results server-side. Adding `sortBy`/`sortDirection` query params to every list endpoint would be new scope beyond "verification pass, not new feature epic" (epic-6-context.md); pages are small (10-20 rows), so sorting the visible page client-side is a proportional interpretation that still satisfies the AC's literal keyboard/`aria-sort` requirement.

**Why the nested-modal case needs an explicit override, not just `FocusTrap`'s default:** `FocusTrap`'s default (return focus to whatever was focused when the overlay opened) is correct for every other Modal/ConfirmDialog in the app, including the "cancel a discard confirmation, return to the exact field" case. EXPERIENCE.md carves out exactly one exception: "the inner Modal ... returns it to the Customer picker field on close" — because after creating a customer inline, the useful next action is picking that customer, not re-clicking "+ New Customer." `BookingFormModal` overrides post-close focus explicitly for this one case only.

## Verification

Independently reproduced (not just re-reading the implementer's report), per this project's standing verification rule — the implementing subagent's own report was thorough and itself flagged two genuine open items (below), which were investigated and resolved directly rather than taken on trust.

**Commands:**
- `ng test --coverage` — **333/333 passing**, run independently three times (after the subagent's implementation, after the two `aria-live` additions, and after the contrast-token fix) — no regression at any point. Coverage rose from 96.25% → 97.46% statements.

**Code read (all diffed files):** `data-table.ts`/`.html` (sort-state signal keyed by column header so it survives `columns()` re-creation; real `<button>` per sortable `<th>` with zero custom keydown code — Enter/Space activation is a native browser default action, not reimplemented; `aria-sort` correctly `null` for non-sortable columns vs `"none"`/`"ascending"`/`"descending"` for sortable ones), `booking-form-modal.ts` (the nested-modal focus override deliberately uses `setTimeout` to run after `FocusTrap.deactivate()`'s effect — verified this ordering is safe: a macrotask always runs after synchronous code and microtask-scheduled effects from the same tick, regardless of whether Angular's `open`-signal effect fires synchronously or as a microtask), `focus-trap-sweep.spec.ts` (genuine `document.activeElement` assertions against real Escape dispatch and macrotask flushes, not shallow structural checks), `focus-trap.ts` (read to confirm the override-ordering reasoning above).

**Manual checks (browser, independently performed):**
- Sorting: clicked "Daily Rate"/"Created" headers on the live Vehicles list against the real seeded dev DB — `aria-sort` cycled `none → ascending → descending → none` correctly, visual indicator (⇅/▲/▼) updated, rows re-sorted.
- Tab-reachability: confirmed via the accessibility tree that each sortable header is a real, focusable `button` (not a decorated `th`).
- Keyboard activation (Enter/Space triggering the native button): attempted via this session's own browser-automation tool and hit the **same limitation the implementing subagent reported** — the tool's synthetic `Return`/`Enter` keydown event arrives with an empty or incomplete `code` and does not trigger the button's native click default-action. Instrumented a `keydown` listener to confirm the event *was* delivered (with `key: "Enter"`) but no corresponding `click` fired — independently confirming this is a synthetic-event limitation of the automation tooling itself, not an app defect. Architectural guarantee stands in its place: the header is a genuine `<button>` with **zero custom keydown code**, so Enter/Space activation is native, spec-guaranteed browser behavior for any real keypress or any properly-trusted synthetic input (e.g. Playwright/Selenium-level dispatch) — a real physical-keyboard check is the only way to close this residual gap, consistent with what the implementing subagent already recommended.
- Nested-modal focus return: unit-tested end-to-end (both create and cancel paths assert `document.activeElement` lands on `#booking-customer`); not re-driven live since the unit coverage already exercises real DOM focus, not a mock.
- Contrast spot-check (the actual "core proof" task for this story): computed real WCAG ratios via `getComputedStyle` in both light and dark mode across Top App Bar, DataTable header, all three Badge variants, and anonymized-customer text. Found the Top App Bar's dark-mode active-nav-link text failing at 3.58:1 (`DESIGN.md`'s `top-app-bar.navActiveDark: {colors.primary-dark}` token, as literally specified). Computed a fix using this project's own existing `{colors.link-dark}` token (`#7fa6ef`) — verified 7.00:1 via the standard WCAG relative-luminance formula before applying, then re-verified live in the browser post-fix: `getComputedStyle` on the active nav link returned `rgb(127, 166, 239)` (`#7fa6ef` exactly) on `rgb(23, 28, 37)` (`#171c25`, `surface-dark`) background, with `font-weight: 700`, `text-decoration: underline`, and `aria-current="page"` all present together. Screenshotted for visual confirmation. Every other checked combination (light mode active-nav at 6.70:1, DataTable header, Badges, anonymized text) already passed.
- Cleaned up: hard-deleted (via the real `DELETE /api/customers/{id}` endpoint, not raw SQL) one stray test customer row ("Test A11y") the implementing subagent's own live verification had left in the local dev Postgres — confirmed it had zero associated bookings first. Stopped both the API and frontend dev-server processes used for this verification afterward.

**Open item deliberately left for reviewer awareness (not blocking):** the literal Enter/Space-triggers-click keypress-to-native-action translation could not be automation-verified in this session's browser tooling (see above) — a physical-keyboard smoke test is recommended as final confirmation, though the architectural guarantee (a genuine `<button>` with no custom keydown handling) makes this a very low-risk residual gap.

## Suggested Review Order

1. `frontend/src/app/shared/data-table/data-table.ts` and `.html` — the sortable-header implementation (sort-state signal, `aria-sort`, native button)
2. `frontend/src/app/shared/data-table/data-table.spec.ts` — the sort-cycle/keyboard-operability-by-construction tests
3. `frontend/src/app/features/bookings/booking-form-modal.ts` and `.spec.ts` — the nested-modal focus-return override and its two-path tests
4. `frontend/src/app/shared/a11y/focus-trap-sweep.spec.ts` — the consolidated focus-trap-and-return sweep
5. `frontend/src/app/shared/top-app-bar/top-app-bar.html` — the dark-mode contrast fix (`text-link` instead of `text-primary`)
6. `_bmad-output/planning-artifacts/ux-designs/ux-Solid-Bruno-2026-08-27/DESIGN.md` and `EXPERIENCE.md` — the corrected `navActiveDark` token reference
7. `frontend/src/app/features/vehicles/vehicles-page.html` and `frontend/src/app/features/customers/customers-page.html` — the additional `aria-live` on empty-state messages
