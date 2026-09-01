---
name: 'Bruno Vehicle Hire'
status: final
sources:
  - '../../../specs/spec-bruno-vehicle-hire/SPEC.md'
  - '../../architecture/architecture-Solid-Bruno-2026-08-25/ARCHITECTURE-SPINE.md'
updated: '2026-08-27'
---

# Bruno Vehicle Hire — Experience Spine

> Single-surface desktop web (Angular 22, Tailwind CSS, hand-built component set — see `DESIGN.md` for the visual identity these behaviors render into). Internal, single-role tool: one staff/admin persona, no login, no permission tiers (API-key auth per the architecture spine). Paired with `DESIGN.md`. Spines win on conflict with any mock or wireframe.

## Foundation

Desktop-first single-page web app. No native mobile surface, no responsive-multi-app requirement — the brief and the chosen persona (a rental-desk agent working a desk, not on the move) don't call for one. Angular 22 with Signals for local state and TanStack Query for server state (per `ARCHITECTURE-SPINE.md` AD-3); Tailwind CSS with a small hand-built shared component set (Badge, Modal, ConfirmDialog, DataTable, FilterBar) rather than an inherited UI-system's defaults — chosen specifically so the calm, soft-pill "Quiet Enterprise" look isn't fighting a library's opinions. `DESIGN.md` is the visual identity reference for every component named below.

Single persona: one internal staff/admin role. No customer login or self-service portal exists or is planned — a "customer view" need is satisfied entirely within the staff tool as a read-focused summary screen (see Information Architecture), never as a separate authenticated surface.

## Information Architecture

| Surface | Reached from | Purpose |
|---|---|---|
| Bookings (list) | App landing / top nav | The busiest daily surface — filter, search, scan status, create, cancel |
| Booking detail | Row "View" in Bookings list | Full booking record, cancel action if eligible |
| New/Edit Booking (modal) | "+ New Booking" / row action | Create a booking: pick vehicle + customer, dates, see live price |
| Vehicles (list) | Top nav | Filter/search inventory, create, edit, soft-delete/restore |
| Vehicle detail | Row "View" in Vehicles list | Full vehicle record, its booking history, soft-delete/restore |
| New/Edit Vehicle (modal) | "+ New Vehicle" / row action | Create/edit a vehicle record |
| Customers (list) | Top nav | Filter/search customers, create, deactivate/restore, erase |
| Customer detail | Row "View" in Customers list | Full customer record, booking history, deactivate/erase actions |
| New/Edit Customer (modal) | "+ New Customer" / row action, or inline from the booking-create flow | Create/edit a customer without leaving the booking flow |
| Customer Summary (read view) | "Summary" action on Customer or Booking detail | Clean, shareable/printable view of a customer + their bookings — the staff-facing "customer view" need, no new auth |

Top app bar: Bookings / Vehicles / Customers links, Bookings is the landing surface. Modals stack one level deep only (e.g. creating a customer inline from the booking-create modal opens on top of it, but nothing opens on top of that).

→ Composition reference: `mockups/bookings-list.html` (Bookings list) and `mockups/booking-create-modal.html` (New Booking modal, showing the overlap-error state from Key Flow 1). The remaining 8 surfaces are deliberately spine-text-only — they reuse the same `DataTable`/`Modal`/`ConfirmDialog` components and `DESIGN.md` tokens already specified, and a mockup would mostly repeat the same visual language. Spine wins on conflict with either mockup.

## Voice and Tone

Brand aesthetic lives in `DESIGN.md.Brand & Style`. Microcopy here is plain, specific, and names the actual rule — never generic.

| Do | Don't |
|---|---|
| "Cannot cancel — booking already completed" | "Action failed" |
| "This vehicle is already booked 25 Aug – 2 Sep" | "Booking conflict error" |
| "Erase this customer's personal data permanently? This cannot be undone." | "Are you sure?" |
| "No bookings yet — create one" | "No data" / empty table with nothing else |
| "Customer (anonymized)" | Showing a blank or "null" where an erased customer's name was |

## Component Patterns

Behavioral; visual specs live in `DESIGN.md.Components`.

| Component | Use | Behavioral rules |
|---|---|---|
| DataTable | Bookings/Vehicles/Customers lists | Sortable by clicking a column header where meaningful (date, price) — the header is a real `<button>` with `aria-sort` on the `<th>`, reachable and activatable via Tab/Enter/Space, current sort direction shown by a persistent visual indicator, not just on hover. Row click on "View" navigates to detail, never the whole row (avoids accidental navigation while scanning). An error state on a row (e.g. a just-attempted invalid Cancel) renders via the `inline-error` component (`DESIGN.md`) under the row's actions, per `DESIGN.md`'s error-row treatment — never a page-level toast for this class of error. |
| Input | Every text/select/date field in FilterBar and every Modal form | Per `DESIGN.md.components.input`. Validation/business-rule errors render via `inline-error` directly beneath the specific field, never a top-of-form banner. |
| FilterBar | All three lists | Search + per-entity filters + "Clear all filters," always visible above the table (never a collapsed/hidden filter panel — staff filter constantly, it should never take an extra click to reveal). Filters apply on change (debounced for the search field), not on a separate "Apply" button. The result count/"no matches" state updates in an `aria-live="polite"` region so a screen-reader user gets the same signal a sighted user gets from the re-rendered table. |
| Button (primary) | "+ New {Entity}" on every list, form submit in every Modal | Disabled + a small inline spinner replacing its label while its action is in flight (create/update/cancel/etc.) — prevents double-submit on a slow connection. Re-enables on response (success closes the Modal via a toast; failure re-enables with the error shown per the relevant State Pattern). |
| Top App Bar | Persistent header | The active nav item (Bookings/Vehicles/Customers) is marked by color (`{colors.primary}`) **and** bold weight **and** an underline, plus `aria-current="page"` for screen readers — never color alone. |
| Toast | Success confirmations only | `role="status"` / `aria-live="polite"` so it's announced without stealing focus. Auto-dismisses after a few seconds; also manually dismissable. Never used for business-rule or validation errors (see State Patterns). |
| Skeleton | List loading | Container carries `aria-busy="true"` while skeleton rows are shown, so a screen-reader user tabbing in mid-load gets a signal data is still arriving rather than silence. |
| Badge | Booking Status | Active / Completed / Cancelled, dot-first per `DESIGN.md`. Completed is never directly settable by a user in any form — it's system-derived (`ARCHITECTURE-SPINE.md` AD-17), so it never appears as an option in a Status dropdown; only Cancel (user action) and the system sweep produce a status change. |
| Modal | Create/Edit Vehicle, Customer, Booking | Opens over the list, preserving the list's filter/scroll state underneath. Traps and returns focus exactly like `ConfirmDialog` (see Accessibility Floor — this applies to both components, not `ConfirmDialog` alone). Closing without saving (Escape or backdrop click) discards changes with no confirmation for a still-empty form; if fields were touched, Escape opens a "Discard changes?" `ConfirmDialog` instead of closing directly — focus moves into that dialog, and if the user cancels the discard, focus returns to the exact field it was on in the `Modal` beforehand, not to the `Modal`'s first field. An inline "+ New Customer" `Modal` opened from within the Booking-create `Modal` is the one permitted nesting case (per Information Architecture's one-level-deep rule); the inner `Modal` traps focus while open and returns it to the Customer picker field on close. The Booking-create `Modal`'s computed Total Price field updates live as dates/vehicle change, inside an `aria-live="polite"` region so a screen-reader user hears the new total without re-navigating to it. |
| ConfirmDialog (neutral) | Cancel booking, Deactivate customer, Soft-delete vehicle | States the specific consequence ("This booking will be cancelled. It stays in your records as Cancelled.") and that it's reversible where true (Deactivate/Soft-delete only — Cancel is not reversible per the domain rule, and the dialog says so). |
| ConfirmDialog (destructive) | Erase customer's personal data | Visually distinct per `DESIGN.md`'s `confirm-dialog-destructive` treatment (danger-toned confirm button). Copy explicitly states "permanently" and "cannot be undone," and separately confirms the customer's booking history will remain intact under a placeholder name — staff should never wonder whether erasing loses the booking record too. |
| Inline booking-rule feedback (`inline-error`) | Booking/Vehicle/Customer create/edit forms | On a 409 (overlap, soft-deleted vehicle, duplicate registration/email, delete/cancel guards) or a 400 (FluentValidation), the specific `ProblemDetails.detail` renders via the `inline-error` component directly under the relevant field — not as a top-of-form banner, so the agent can immediately see which input to change. Announced via `aria-live="polite"`. |
| Customer Summary | Reached from Customer/Booking detail | Read-only, print-friendly layout (no filters, no row actions) — customer identity + contact (or "(anonymized)" placeholder, same treatment as everywhere else) + their booking list. Exists specifically so an agent can reference or print it during a phone call without exposing the full editable admin UI. |

## State Patterns

| State | Surface | Treatment |
|---|---|---|
| List loading | Bookings/Vehicles/Customers | Skeleton rows (4-8, matching the real row layout), `aria-busy="true"` on the container — never a spinner, since the table shape itself is part of what orients the user while data loads. |
| Detail loading | Booking/Vehicle/Customer detail, Customer Summary | Same skeleton treatment as list loading, shaped to the detail layout instead of a table — a direct link or refresh into a detail page never shows a blank screen while data fetches. |
| Record not found | Any detail surface reached by a stale/invalid id | A dedicated "not found" message ("This booking no longer exists") + a link back to the relevant list — never a blank page or a raw 404. |
| Server/network error (5xx or connection failure) | Any surface, any request | A page-level or field-level "Something went wrong — try again" message with a retry action, distinct from both the 400 (validation) and 409 (business-rule) inline treatments — the user should never mistake an infrastructure failure for a rule they broke. |
| Empty list | Any list, no records match filters | "No {entity} match these filters" + a "Clear all filters" action if filters are active; "No {entity} yet — create one" + primary create action if genuinely empty. |
| Business-rule error (409) | Booking/Customer/Vehicle create-edit forms, row actions | Inline at point of action via `inline-error` (per Component Patterns above), `DESIGN.md`'s danger tokens, `aria-live="polite"`. Covers overlap, delete/cancel guards, and duplicate `RegistrationNumber`/`Email` alike — the same treatment for every "requires loaded state to evaluate" rule, not just the booking-overlap case. Never a toast — the agent needs the message to stay visible while they decide what to do next, not disappear after a few seconds. |
| Validation error (400) | Any form | Inline under the specific invalid field via `inline-error`, same visual treatment as a business-rule error but triggered by FluentValidation shape checks rather than a loaded-state rule (per `ARCHITECTURE-SPINE.md` AD-8's 400/409 split — the user doesn't need to know which, the field-level message reads the same either way). |
| Submitting / in-flight | Any Modal form, any row action button | Primary button disabled + inline spinner (see Component Patterns) — prevents a double-submit on a slow connection. |
| Success | Create/edit/cancel/deactivate/erase actions | A brief toast confirmation ("Booking created," "Customer deactivated"), `role="status"` — success is the one case a toast is appropriate for, since there's nothing further the user needs to act on. |
| Anonymized customer | Anywhere a customer name renders, including Customer Summary and historical booking rows | `DESIGN.md`'s anonymized-text token (muted, italic, contrast-verified) + literal "(anonymized)" suffix — never just a blank or the placeholder value alone, so it reads unambiguously as "this was erased," not "this is broken." |
| Soft-deleted vehicle | Vehicle list (default view excludes it); if surfaced via an explicit "show inactive" toggle | Dimmed row treatment (`{colors.text-disabled}`) + a "Restore" action in place of the usual row actions. |
| Customer Summary, no bookings | Customer Summary for a customer with zero bookings | "No bookings on record for this customer" — the summary still renders (identity/contact section), just with an empty booking list rather than the whole screen looking broken. |

## Interaction Primitives

**Mouse/keyboard, no shortcuts layer.** This is a desk tool used continuously through a shift, but the brief doesn't call for a power-user keyboard-first surface (unlike a dev tool) — standard form tabbing and button clicks are the whole interaction model. Tab order follows visual/reading order on every screen; Enter submits the focused form; Escape closes the topmost modal/dialog only (never more than one level).

- Click "View" (not the row) to open a detail record.
- Click "+ New {Entity}" to open the create modal.
- Row-level "Cancel"/"Deactivate"/"Erase" links always require their matching ConfirmDialog — no destructive or state-changing action fires on a single click anywhere in the app.
- Filters apply live (debounced on text input); pagination is page-number links, never infinite scroll (matches the dense/scannable posture — an agent needs to know how many pages exist).

**Banned:** infinite scroll, any destructive action without a confirm step, merging Deactivate and Erase into one control, toasts for business-rule/validation errors.

## Accessibility Floor

Behavioral; visual contrast lives in `DESIGN.md` (both light and dark palettes designed to WCAG AA for text/badge combinations).

- WCAG 2.2 AA across the whole surface, both color modes — verified token-by-token in `DESIGN.md` (see its Colors section), not just asserted.
- Full keyboard operability: every action reachable via Tab/Enter/Escape, visible focus rings on all interactive elements. Sortable column headers are real, keyboard-activatable controls with `aria-sort`, not click-only decorations.
- `aria-live="polite"` covers every dynamic content update, not just errors: inline business-rule/validation errors, FilterBar's live result count as the user types, the Booking-create Modal's live-computed Total Price, and success toasts (`role="status"`). Any content that changes without a page navigation gets an announcement.
- Every icon-only action (if any row action becomes icon-only in implementation) carries an accessible label — no icon-only button ships without one.
- Both `Modal` and `ConfirmDialog` trap focus while open and return it to the triggering element (or, for a nested Modal-over-Modal case, to the specific field the user was on) on close — this is not `ConfirmDialog`-only. See Component Patterns for the exact nested-dialog focus behavior.
- No state is signaled by color alone: the active Top App Bar nav item carries color + bold weight + underline + `aria-current="page"`; every Badge pairs its color with a text label; the anonymized-customer treatment pairs color with italics and a literal text suffix.

## Inspiration & Anti-patterns

Four full visual directions were built out and compared before choosing (`.working/` in this workspace) — three were explicitly rejected, not just unchosen:

- **Rejected — "Ledger"** (utilitarian spreadsheet grid: full cell borders, monospace data, zero corner radius, bracket-style `[ACTIVE]` tags). Too visually harsh for a tool used continuously through a full shift; the "everything is a ruled line" density reads as louder than the calm posture this product wants.
- **Rejected — "Command Console"** (terminal/ops feel: monospace everywhere, sharp edges, amber/phosphor-green accent). Reads as a monitoring/ops dashboard, not a rental-desk tool a non-technical staff member uses all day — the register was aimed at the wrong audience.
- **Rejected — "Structured Paper"** (editorial serif/sans pairing, thin rules, near-print minimal chrome). The editorial voice fights the "professional back-office tool" register — a serif display face implies content to *read*, not a table to *scan*.
- **Chosen — "Quiet Enterprise"** — see `DESIGN.md § Brand & Style` for the full rationale (whitespace + one card + soft pill badges over ruled lines and saturated fills).

## Key Flows

### Flow 1 — Phone booking, then an overlap on the follow-up call (Sipho, rental-desk agent)

*Exercises CAP-2 (Customer management), CAP-3 (Booking management), CAP-4 (Search & filtering), CAP-5 (Business-rule feedback UX).*

1. Sipho takes a call: a customer wants a car for next weekend. He opens **Bookings**, but starts from **Vehicles** to check what's free — filters by the requested date range, sees three available options, reads the daily rate on a compact SUV off the row.
2. Customer decides. Sipho searches **Customers** by name — no match, this is a first-timer. He opens **+ New Customer** inline from the booking-create flow (not a separate page navigation) and creates the record without losing his place.
3. He opens **+ New Booking**, picks the vehicle and the new customer, sets the date range. No rule is violated; the booking creates immediately, a success toast confirms, and he reads the total price back to the customer from the confirmation.
4. Later that day, the same customer calls back wanting to extend by a day. Sipho opens the booking's detail, attempts to extend the end date.
5. **Climax:** the form rejects it inline, under the date field, with the specific message: "This vehicle is already booked 2 Sep – 4 Sep." Not a generic failure — Sipho can read that sentence straight to the customer on the phone and explain exactly why, without needing to go investigate what actually happened.

Failure variant: if Sipho tries to cancel a booking that already completed, the row's inline error reads "Cannot cancel — booking already completed" — again specific enough to explain without extra digging.

### Flow 2 — A customer asks to be forgotten (Sipho, rental-desk agent)

*Exercises CAP-2 (Customer management), CAP-11 (Soft-delete/restore customer), CAP-12 (Anonymize customer).*

1. A former customer calls asking Bruno Vehicle Hire to delete their personal information. Sipho opens their **Customer detail** — they have booking history, so the usual hard-delete is blocked (per the domain rule).
2. He sees two distinct, visually different actions: a neutral "Deactivate" and a harsher-styled "Erase personal data" (per `DESIGN.md`'s destructive-confirm-dialog treatment). He picks Erase.
3. The destructive `ConfirmDialog` states plainly: this permanently removes their name, email, and phone number; it cannot be undone; their booking history stays intact under a placeholder name.
4. **Climax:** Sipho confirms. The customer's row now shows "Customer (anonymized)" in the muted italic style everywhere it appears — including on the old bookings' rows in the Bookings list — so anyone looking at historical records immediately understands why the name looks like that, rather than assuming a bug.

Failure variant: if Sipho clicks Deactivate instead by mistake, nothing irreversible happens — the customer just stops appearing in default lists and can be restored, exactly as the two-button separation in `DESIGN.md` is designed to make hard to get wrong in the first place.

### Flow 3 — Retiring a vehicle, then a walk-in customer's summary (Sipho, rental-desk agent)

*Exercises CAP-1 (Vehicle management), CAP-10 (Submission quality via a clean day-to-day workflow), the Customer Summary surface.*

1. One of the fleet's older vehicles fails an inspection. Sipho opens **Vehicles**, finds it, opens its detail, and clicks Soft-delete — a neutral `ConfirmDialog` confirms it'll disappear from availability searches but stay reversible.
2. He confirms. The vehicle vanishes from the default Vehicles list and can no longer be selected in a new Booking's vehicle picker — but its historical bookings still show its name and plate normally, unaffected.
3. A regular customer walks in and asks Sipho to print a copy of their booking history for their own records. Sipho opens the customer's detail and clicks "Summary."
4. **Climax:** the Customer Summary screen renders — a single clean card, no filters, no edit buttons, no navigation chrome — just the customer's contact details and their full booking list, ready to print. Sipho hits print and hands over a page that looks nothing like the dense admin table he works in all day, because it was never meant to.
5. Two months later, the mechanic reports the same vehicle passed re-inspection. Sipho reopens its detail — the Soft-delete state, being reversible, shows a "Restore" action in place of the usual row controls. He restores it, and it's immediately bookable again.

Failure variant: if Sipho tries to restore a vehicle that was never soft-deleted (stale UI state, e.g. a second staff member already restored it), the restore action fails with an inline message ("Already active") rather than silently succeeding twice.
