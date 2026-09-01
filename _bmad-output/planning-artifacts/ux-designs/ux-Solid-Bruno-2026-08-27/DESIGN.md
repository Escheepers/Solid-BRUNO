---
name: 'Bruno Vehicle Hire'
description: 'Calm, dense, professional back-office tool for managing vehicle rentals. Tailwind CSS on Angular 22, hand-built component set — no inherited UI-system defaults to override.'
status: final
created: '2026-08-27'
updated: '2026-08-27'
colors:
  background: '#f7f8fa'
  background-dark: '#0e1116'
  surface: '#ffffff'
  surface-dark: '#171c25'
  surface-header: '#f3f4f7'
  surface-header-dark: '#12161d'
  surface-alt: '#f3f4f7'
  surface-alt-dark: '#14181f'
  border: '#e3e6ec'
  border-dark: '#232a35'
  input-border: '#d7dbe3'
  input-border-dark: '#2a3140'
  text: '#1c2128'
  text-dark: '#f1f3f6'
  text-body: '#2c323c'
  text-body-dark: '#d7dbe2'
  text-muted: '#5b6472'
  text-muted-dark: '#8b93a1'
  text-faint: '#4b5563'
  text-faint-dark: '#8b93a1'
  text-disabled: '#b7bcc4'
  text-disabled-dark: '#454c58'
  primary: '#1d4ed8'
  primary-dark: '#3f6fd1'
  primary-hover: '#3f6fd1'
  primary-hover-dark: '#5b8def'
  primary-foreground: '#ffffff'
  success-text: '#166534'
  success-text-dark: '#86efac'
  success-bg: '#dcfce7'
  success-bg-dark: '#14532d'
  success-dot: '#166534'
  success-dot-dark: '#86efac'
  neutral-text: '#374151'
  neutral-text-dark: '#d1d5db'
  neutral-bg: '#e5e7eb'
  neutral-bg-dark: '#1f2937'
  neutral-dot: '#374151'
  neutral-dot-dark: '#d1d5db'
  danger-text: '#991b1b'
  danger-text-dark: '#fca5a5'
  danger-bg: '#fee2e2'
  danger-bg-dark: '#7f1d1d'
  danger-dot: '#991b1b'
  danger-dot-dark: '#fca5a5'
  error-row-bg: '#fdf1ef'
  error-row-bg-dark: '#241a1c'
  anonymized-text: '#4b5563'
  anonymized-text-dark: '#9ca3af'
  link: '#1d4ed8'
  link-dark: '#7fa6ef'
  input-bg: '#f3f4f7'
  input-bg-dark: '#0e1116'
typography:
  page-title:
    fontFamily: '-apple-system, "Segoe UI", Roboto, Arial, sans-serif'
    fontSize: 19px
    fontWeight: '600'
  section-label:
    fontFamily: '-apple-system, "Segoe UI", Roboto, Arial, sans-serif'
    fontSize: 10.5px
    fontWeight: '600'
    letterSpacing: 0.4px
  body:
    fontFamily: '-apple-system, "Segoe UI", Roboto, Arial, sans-serif'
    fontSize: 12.5px
    fontWeight: '400'
  body-emphasis:
    fontFamily: '-apple-system, "Segoe UI", Roboto, Arial, sans-serif'
    fontSize: 12.5px
    fontWeight: '500'
  button:
    fontFamily: '-apple-system, "Segoe UI", Roboto, Arial, sans-serif'
    fontSize: 13px
    fontWeight: '600'
  caption:
    fontFamily: '-apple-system, "Segoe UI", Roboto, Arial, sans-serif'
    fontSize: 12px
    fontWeight: '400'
rounded:
  sm: 6px
  md: 7px
  lg: 8px
  xl: 12px
  full: 9999px
spacing:
  '1': 4px
  '2': 6px
  '3': 8px
  '4': 10px
  '5': 14px
  '6': 16px
  '7': 18px
  '8': 22px
  '9': 26px
  row-y: 6.5px
  row-x: 18px
components:
  button-primary:
    background: '{colors.primary}'
    backgroundDark: '{colors.primary-dark}'
    hover: '{colors.primary-hover}'
    hoverDark: '{colors.primary-hover-dark}'
    foreground: '{colors.primary-foreground}'
    radius: '{rounded.md}'
    padding: '9px 18px'
  input:
    background: '{colors.input-bg}'
    backgroundDark: '{colors.input-bg-dark}'
    border: '{colors.input-border}'
    borderDark: '{colors.input-border-dark}'
    radius: '{rounded.md}'
    text: '{colors.text-body}'
    textDark: '{colors.text-body-dark}'
    placeholder: '{colors.text-faint}'
    placeholderDark: '{colors.text-faint-dark}'
  top-app-bar:
    background: '{colors.surface}'
    backgroundDark: '{colors.surface-dark}'
    navActive: '{colors.primary}'
    navActiveDark: '{colors.primary-dark}'
    navInactive: '{colors.text-faint}'
    navInactiveDark: '{colors.text-faint-dark}'
  toast:
    background: '{colors.text}'
    backgroundDark: '{colors.surface-dark}'
    foreground: '{colors.surface}'
    foregroundDark: '{colors.text-dark}'
    radius: '{rounded.md}'
  skeleton:
    background: '{colors.surface-alt}'
    backgroundDark: '{colors.surface-alt-dark}'
    radius: '{rounded.sm}'
  customer-summary:
    background: '{colors.surface}'
    backgroundDark: '{colors.surface-dark}'
    radius: '{rounded.xl}'
    maxWidth: '640px'
  badge:
    radius: '{rounded.full}'
    padding: '3px 10px 3px 8px'
    fontSize: '11px'
    fontWeight: '600'
  badge-active:
    background: '{colors.success-bg}'
    foreground: '{colors.success-text}'
    dot: '{colors.success-dot}'
  badge-completed:
    background: '{colors.neutral-bg}'
    foreground: '{colors.neutral-text}'
    dot: '{colors.neutral-dot}'
  badge-cancelled:
    background: '{colors.danger-bg}'
    foreground: '{colors.danger-text}'
    dot: '{colors.danger-dot}'
  card:
    background: '{colors.surface}'
    border: '{colors.border}'
    radius: '{rounded.xl}'
  data-table:
    headerForeground: '{colors.text-faint}'
    rowPaddingY: '{spacing.row-y}'
    rowPaddingX: '{spacing.row-x}'
    zebraBackground: '{colors.surface-alt}'
  error-row:
    background: '{colors.error-row-bg}'
  inline-error:
    foreground: '{colors.danger-text}'
    fontSize: '10.5px'
  confirm-dialog-neutral:
    accent: '{colors.primary}'
  confirm-dialog-destructive:
    accent: '{colors.danger-text}'
---

# DESIGN.md — Bruno Vehicle Hire

## Brand & Style

Bruno Vehicle Hire is a back-office tool, not a consumer product — it exists so a rental-desk agent can move fast through a full shift without the screen tiring them out or shouting at them. The aesthetic posture is **Quiet Enterprise**: calm, confident, low-contrast, and unmistakably professional. Separation between elements comes from whitespace, a single rounded card, and faint zebra tinting — not heavy ruled lines. Status is carried by soft, low-intensity pill badges rather than saturated blocks of color, so a table full of bookings reads as calm at a glance and only draws the eye where something actually needs attention (an error row, a cancelled booking). Nothing here is decorative; every color and shape choice earns its place by helping someone scan a dense table faster or notice an exception sooner.

## Colors

- **`{colors.primary}`** (`#1d4ed8` light / `#3f6fd1` dark) — the one brand color. Used for the primary action button ("+ New Booking"), active nav item, and links. Never used for status.
- **`{colors.background}`** / **`{colors.surface}`** — background sits slightly off-white/off-black (`#f7f8fa` / `#0e1116`), the card floats on it in true white/near-black (`#ffffff` / `#171c25`) — this is what gives the "one calm card" feel rather than a flat page.
- **`{colors.surface-alt}`** — the faint zebra-row tint inside tables. Barely perceptible; a scanning aid, not a design statement.
- **Status semantics** — three colors, used only for status badges and inline errors, never elsewhere:
  - **Success/Active** (`{colors.success-text}` / `{colors.success-bg}`) — a booking currently in effect.
  - **Neutral/Completed** (`{colors.neutral-text}` / `{colors.neutral-bg}`) — a booking that ran its course. Deliberately desaturated gray, not green — completed is not an achievement, it's just closed.
  - **Danger** (`{colors.danger-text}` / `{colors.danger-bg}`) — cancelled bookings, inline rule-violation errors, and the "Erase personal data" destructive action. The one color allowed to feel slightly more assertive than the rest of the palette, because it's the one thing that should interrupt the calm.
- **`{colors.anonymized-text}`** — a distinct muted, italicized tone for an anonymized customer's placeholder name, so staff immediately recognize "this isn't a real name" without reading a tooltip. Darker than a typical "muted" tone specifically because this text is safety-critical (per `EXPERIENCE.md`) — it must clear WCAG AA, not just read as visually de-emphasized.
- Never introduce a fourth semantic color. Never use full-saturation fills for badges — the whole point of Quiet Enterprise is that status recedes until it matters. Badge and status colors use **solid** fills (not translucent overlays) specifically so contrast never shifts depending on which row background — zebra-striped or not — sits underneath.

**Text hierarchy** (four tiers, each with a load-bearing use — none is decorative):
- **`{colors.text}`** — headings and page titles only (`{typography.page-title}`).
- **`{colors.text-body}`** — the default color for all body/table-cell copy (`{typography.body}`, `{typography.body-emphasis}`).
- **`{colors.text-muted}`** — secondary/supporting copy: captions, helper text under a field, the pagination "Showing X–Y of Z" line.
- **`{colors.text-faint}`** — the quietest tier: table column headers (`{typography.section-label}`) and input placeholder text. Darkened in this palette specifically to still clear AA at that small, uppercase, tracked size — "faint" describes its visual weight relative to the other tiers, not permission to under-contrast it.
- **`{colors.text-disabled}`** — disabled form fields and the disabled state of a row action (e.g. "Cancel" on an already-completed booking) and the dimmed treatment for a soft-deleted vehicle's row.
- **`{colors.primary-foreground}`** is shared across both light and dark modes (no `-dark` variant) — white reads correctly on both the light-mode and dark-mode button fill, so a second token would be redundant, not an oversight.
- **`{colors.neutral-dot}`/`{colors.danger-dot}`** intentionally share the same hex as their paired text color in both modes — the dot is a visual echo of the badge's text color, not a separately-tuned accent, so there's nothing mode-specific to tune.

## Typography

Single system sans-serif stack throughout (`-apple-system, "Segoe UI", Roboto, Arial, sans-serif`) — no serif, no monospace, no display face. This is a tool, not an editorial product; typographic personality would compete with the density the job needs.

- **`{typography.page-title}`** (19px/600) — one per screen, e.g. "Bookings".
- **`{typography.section-label}`** (10.5px/600, uppercase, tracked) — table column headers and form field labels. Small and quiet on purpose; they orient without competing with the data.
- **`{typography.body}`** (12.5px/400) — table cell content, the bulk of what's on screen.
- **`{typography.body-emphasis}`** (12.5px/500) — the "headline" cell in a row (e.g. vehicle name in a bookings row) — just enough weight to anchor the eye at the start of a scan.
- **`{typography.button}`** (13px/600) — all button labels.

## Layout & Spacing

Compact by deliberate choice: this is a spreadsheet-adjacent tool where a staff member needs to see many rows at once. Table rows use `{spacing.row-y}` (6.5px) vertical padding — noticeably tighter than a typical consumer-web row height — and `{spacing.row-x}` (18px) horizontal. Page content padding is `{spacing.8}` (22px). Filter-bar fields sit in a single horizontal row above the table, `{spacing.5}` (14px) gap between fields, never wrapping to a second row on desktop.

One card per screen holds the filter bar + table + pagination footer as a single visual unit — not separate floating panels. Max content width is unconstrained (this is a wide-table product, unlike a single-column reading app) but centers with margin on very wide viewports.

## Elevation & Depth

Minimal. The one card uses a `1px` border (`{colors.border}`) plus a soft ambient shadow, never a hard drop shadow. Modals/dialogs elevate one step further with a stronger shadow to clearly separate them from the page behind. No other elevation tiers exist — depth is not a hierarchy device here, whitespace and the single card already do that job.

## Shapes

- **`{rounded.xl}`** (12px) — the outer card, modals.
- **`{rounded.md}`** (7px) — buttons, inputs, select fields.
- **`{rounded.full}`** — status badges only (pill shape). Reserving full-pill exclusively for status keeps it meaningful — the eye learns "pill shape = status" and nothing else competes for that read.
- No sharp (0px) corners anywhere — sharp corners were the "Ledger"/"Command Console" register, explicitly not chosen. Quiet Enterprise is soft-edged throughout.

## Components

→ See `mockups/bookings-list.html` and `mockups/booking-create-modal.html` for a composed rendering of these tokens and components together. `EXPERIENCE.md` also links these; spines win on conflict.

- **Button (primary)** — `{colors.primary}` fill, `{colors.primary-hover}` on hover, white text, `{rounded.md}`, used once per screen for the dominant create action (e.g. "+ New Booking"). Secondary actions are text links (`{colors.link}`), not outlined buttons — keeps the row-level actions (View / Cancel) visually light against the primary button's weight.
- **Input** (`{components.input}`) — the shared spec for every text field, select, and date field in `FilterBar` and every Create/Edit `Modal`. Distinguished from the surrounding card by a filled background (`{colors.input-bg}`), not by border contrast alone — the low-contrast border (`{colors.input-border}`) is a secondary cue, so the field's boundary is still identifiable even where the border itself falls under 3:1. Placeholder text uses `{colors.text-faint}`. Focus state gets a visible, high-contrast ring (see `EXPERIENCE.md` Accessibility Floor) distinct from the resting border.
- **Badge** — pill shape, dot + label, three variants (Active/Completed/Cancelled per `{components.badge-active}` / `{components.badge-completed}` / `{components.badge-cancelled}`). Always dot-first — the dot alone should be scannable in a fast glance down a column. Solid (non-translucent) fills per variant, verified against WCAG AA in both light and dark.
- **DataTable** — the core component. Zebra striping via `{components.data-table.zebraBackground}`, uppercase `{typography.section-label}` headers in `{colors.text-faint}` against `{colors.surface-header}`, an `{components.error-row}` background variant for a row currently showing an inline error (see the "Cannot cancel — booking already completed" pattern from the mockup). Inline validation/rule messages under a row or field use `{components.inline-error}` (`{colors.danger-text}`, `{typography.caption}` scale) — the same token whether the failure came from a 400 or a 409 (per `ARCHITECTURE-SPINE.md` AD-8, the user doesn't need to know which).
- **Top App Bar** (`{components.top-app-bar}`) — the persistent header carrying the Bookings/Vehicles/Customers nav. The active section is marked by **both** `{colors.primary}` text color **and** a bold weight + underline — never color alone (see `EXPERIENCE.md` Accessibility Floor).
- **Toast** (`{components.toast}`) — reserved for success confirmations only (per `EXPERIENCE.md` State Patterns). High-contrast solid fill (`{colors.text}` background / `{colors.surface}` foreground in light, inverted in dark), bottom-of-screen, auto-dismisses after a few seconds, manually dismissable.
- **Skeleton** (`{components.skeleton}`) — loading placeholder for list rows, using `{colors.surface-alt}` as a flat placeholder block (no shimmer animation — kept simple and calm, consistent with the rest of the palette's restraint).
- **ConfirmDialog** — two visual variants: **neutral** (`{components.confirm-dialog-neutral}`, for reversible actions like Cancel booking or Deactivate customer) and **destructive** (`{components.confirm-dialog-destructive}`, for the irreversible Erase-personal-data action). The destructive variant uses `{colors.danger-text}` on its confirm button and states the word "permanently" in its body copy — visual weight matches the actual stakes of the action.
- **Modal** — used for Create/Edit forms (Vehicle, Customer, Booking) launched from the list screen rather than full page navigation, so staff never lose their filter/scroll position mid-task.
- **FilterBar** — search input + per-column filters + a "Clear all filters" text link, always left-to-right in one row, search field widest. Built from `Input`.
- **Customer Summary** (`{components.customer-summary}`) — a single narrower card (`{components.customer-summary.maxWidth}`, not full table width) holding customer identity + booking list, no `FilterBar`, no row actions. Print styles strip `{components.top-app-bar}` and any button chrome, leaving only this card.
- **Pager** — the pagination footer (page-number links + current-page highlight). Not a standalone reusable component — page furniture local to `DataTable`, styled inline with it rather than specified separately.

## Do's and Don'ts

| Do | Don't |
|---|---|
| Use `{colors.primary}` only for the one dominant action + nav + links | Use primary color for status or decoration |
| Keep badges as low-intensity pills (`{components.badge}` tokens) | Use solid, saturated badge fills — breaks the "calm until it matters" rule |
| Keep table rows dense (`{spacing.row-y}`) | Add consumer-web row padding "for friendliness" — this is a scanning tool |
| Give the destructive (Erase) action its own distinct, harsher-styled button | Merge Deactivate and Erase into one "Delete" button or menu item |
| Reserve `{rounded.full}` for status badges only | Use pill shapes on buttons or cards |
| Render an anonymized customer's name in `{colors.anonymized-text}`, italic | Show an anonymized customer's placeholder name in normal body styling — staff must notice it immediately |
