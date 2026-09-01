# Adversarial Accessibility Review — Bruno Vehicle Hire

**Reviewed:** `DESIGN.md`, `EXPERIENCE.md` (ux-Solid-Bruno-2026-08-27)
**Reviewer date:** 2026-09-01
**Claim under test:** "WCAG 2.2 AA across the whole surface, both color modes" (`EXPERIENCE.md` § Accessibility Floor), plus full keyboard operability, aria-live for inline errors, focus-trap/return on dialogs, and icon-label requirement.

## Overall Verdict: **FAIL**

The AA claim is contradicted by the design's own token values in several places — not implementation guesses, but arithmetic on the hex/rgba values `DESIGN.md` itself defines. The most damaging failure sits exactly where the design says its "at a glance" scanning promise lives: the light-mode status badge (both dot and text) fails contrast. Beyond color, the stated behavioral accessibility rules are scoped narrowly enough that several named interactive patterns (live filtering, sortable columns, live price calc, success toasts) fall outside them. One likely color-only signal (active nav item) was also found.

---

## 1. Contrast audit (computed, not estimated, from the stated hex/rgba values)

Method: WCAG relative-luminance/contrast-ratio formula applied directly to the hex values in `DESIGN.md`'s `colors` block. Where a token is a translucent `rgba()` (all three badge backgrounds), I alpha-composited it over both plausible row backgrounds it will actually render on — `surface` (white card / non-zebra row) and `surface-alt` (zebra row) — since the design never states which; both are shown. These composite numbers are **certain given the stated values**, not guesses about rendering — the only assumption is which literal background token sits underneath, which is itself ambiguous in the doc (see finding C7).

Thresholds used: 4.5:1 normal text, 3:1 large text (≥18.66px bold / 24px regular) and non-text UI/graphical objects. None of the typography sizes in this design (11px–13px, even bold) clear the large-text threshold, so 4.5:1 applies to every text pair below except page-title (19px/600, which does clear it and passes easily at 15–17:1 in all cases — not further discussed).

### Light palette

| Pair | Ratio | Needs | Result |
|---|---|---|---|
| text `#1c2128` / background `#f7f8fa` | 15.23:1 | 4.5:1 | PASS |
| text `#1c2128` / surface `#ffffff` | 16.18:1 | 4.5:1 | PASS |
| text-body / background, surface | 12.13–12.89:1 | 4.5:1 | PASS |
| text-muted `#6b7280` / background `#f7f8fa` | **4.55:1** | 4.5:1 | PASS (marginal — 0.05 above threshold, no rendering headroom) |
| text-muted / surface | 4.83:1 | 4.5:1 | PASS |
| **text-faint `#9aa1ac` / surface-header `#f3f4f7`** (this is the DataTable's `headerForeground` token — every column header in every table) | **2.37:1** | 4.5:1 | **FAIL, badly** |
| text-faint / surface | 2.60:1 | 4.5:1 | FAIL |
| link `#3f6fd1` / surface `#ffffff` | 4.77:1 | 4.5:1 | PASS |
| **link `#3f6fd1` / background `#f7f8fa`** | **4.49:1** | 4.5:1 | **FAIL (marginal, by 0.01)** — a link rendered directly on the page background rather than inside the white card technically misses AA |
| danger-text `#c23b32` / error-row-bg `#fdf1ef` | 4.79:1 | 4.5:1 | PASS |
| danger-text / surface (inline error text) | 5.30:1 | 4.5:1 | PASS |
| **anonymized-text `#9aa1ac` / surface** (the "Customer (anonymized)" placeholder — real, meaningful text, not decorative) | **2.60:1** | 4.5:1 | **FAIL, badly** |
| anonymized-text / surface-alt | 2.37:1 | 4.5:1 | FAIL |
| **badge-active fg `#1f9d52` / composited success-bg** (~`#e9f5ee` on surface, ~`#deebe6` on zebra) | **3.13:1 / 2.85:1** | 4.5:1 | **FAIL, badly, in both row states** |
| badge-completed fg `#5b6472` / composited neutral-bg | 5.38:1 / 4.89:1 | 4.5:1 | PASS |
| **badge-cancelled fg `#c23b32` / composited danger-bg** | 4.71:1 (surface) / **4.29:1 (zebra)** | 4.5:1 | **PASS on white rows, FAIL on zebra rows** — inconsistent, and the failing case is not rare (roughly half of all rows) |
| success-dot `#2fae62` / composited success-bg (surface) | 2.55:1 | 3:1 (non-text) | **FAIL** |
| neutral-dot `#8b93a1` / composited neutral-bg | 2.79:1 | 3:1 | **FAIL** |
| danger-dot `#e5645a` / composited danger-bg | 2.96:1 | 3:1 | FAIL (marginal) |
| primary-foreground (white) / primary `#3f6fd1` (button bg) | 4.77:1 | 4.5:1 | PASS |
| input-border `#d7dbe3` / surface (input boundary) | 1.39:1 | 3:1 (non-text UI) | FAIL — see note below |
| card border `#e3e6ec` / background | 1.18:1 | 3:1 | FAIL — see note below |

### Dark palette

| Pair | Ratio | Needs | Result |
|---|---|---|---|
| text-dark / background-dark, surface-dark | 15.37–17.01:1 | 4.5:1 | PASS |
| text-body-dark / background-dark, surface-dark | 12.30–13.62:1 | 4.5:1 | PASS |
| text-muted-dark / background-dark, surface-dark | 5.52–6.11:1 | 4.5:1 | PASS |
| text-faint-dark `#7c8492` / surface-header-dark `#12161d` (table header) | 4.81:1 | 4.5:1 | PASS |
| text-faint-dark / surface-dark | **4.53:1** | 4.5:1 | PASS (marginal) |
| link-dark `#7fa6ef` / surface-dark, background-dark | 7.00–7.75:1 | 4.5:1 | PASS |
| danger-text-dark / error-row-bg-dark, surface-dark | 8.80–8.87:1 | 4.5:1 | PASS |
| **anonymized-text-dark `#6d7480` / surface-dark** | **3.63:1** | 4.5:1 | **FAIL** |
| anonymized-text-dark / surface-alt-dark | 3.78:1 | 4.5:1 | FAIL |
| badge-active/-completed/-cancelled fg / composited bg (dark) | 6.28–8.08:1 | 4.5:1 | PASS (dark-mode badges are the one area that comfortably clears AA in both row states) |
| success-dot-dark / composited bg | 6.03:1 | 3:1 | PASS |
| danger-dot-dark / composited bg | 4.32:1 | 3:1 | PASS |
| **primary-foreground (white) / primary-dark `#5b8def`** (dark-mode primary button fill) | **3.23:1** | 4.5:1 | **FAIL** |
| input-border-dark / surface-dark | 1.31:1 | 3:1 | FAIL — see note below |
| border-dark / background-dark | 1.31:1 | 3:1 | FAIL — see note below |

### Key findings from the contrast audit

1. **Badges fail contrast in light mode — dot and text both, on the exact component the design calls out as its core scanning mechanism.** `DESIGN.md` states "the dot alone should be scannable in a fast glance down a column," but `success-dot`, `neutral-dot`, and `danger-dot` all fall under the 3:1 non-text threshold (2.55–2.96:1), and the `badge-active` text fails outright at 3.13:1/2.85:1. This is a direct product of the stated design philosophy ("never use full-saturation fills for badges... status recedes until it matters") — the low-intensity aesthetic and the AA claim are in tension, and in light mode the aesthetic wins. `badge-cancelled` also fails specifically on zebra rows (4.29:1), meaning the same badge is compliant or non-compliant depending on which row it lands in — an inconsistency that will read as a bug in an accessibility audit even if occasionally passing.

2. **`anonymized-text` fails AA in both palettes** (2.60:1 light / 3.63:1 dark). This is the single most consequential text-contrast failure: it's the token used to render "Customer (anonymized)," which `EXPERIENCE.md` explicitly frames as safety-critical information staff "must notice... immediately" and "never wonder" about. A low-vision user is the population most likely to miss exactly this text.

3. **`text-faint` (light mode) as the DataTable header color is a severe fail (2.37:1 vs. 4.5:1 required)** — every column header on every list screen, in light mode, is under half the required contrast. This is load-bearing UI (section-label headers orient every scan of a dense table) rendered nearly illegible for low-vision users.

4. **Dark-mode primary button fails (3.23:1)** — `primary-dark` (`#5b8def`) is lighter than `primary` (`#3f6fd1`), but the foreground stays white in both modes; the lighter fill needs a darker (or the palette needs an inverted) foreground to clear 4.5:1. This directly contradicts "both light and dark palettes designed to WCAG AA."

5. **`link` on `background` and `text-faint-dark`/`text-muted` on their lighter pairing are marginal passes/fails within 0.01–0.05 of the threshold** — mathematically on the wrong or barely-right side, with zero tolerance for anti-aliasing, sub-pixel rendering, or a slightly different monitor gamma. These should be treated as fails for a real AA certification, not passes.

6. **Input/card borders never clear 3:1 non-text contrast against their backgrounds** (1.18–1.39:1 light, 1.31:1 dark) in either palette. This matters *if* the border is the only cue marking an input field's boundary (WCAG 2.2 SC 1.4.11 applies to UI component boundaries). The design doesn't specify a compensating cue (e.g., a background-color change or shadow on the input itself, distinct from border), so this is flagged as a likely gap rather than a certain one — noted as an estimate, since actual input styling in implementation could add a fill or shadow not captured in the token list.

7. **Composited badge backgrounds are ambiguous in the spec** — `success-bg`/`neutral-bg`/`danger-bg` are `rgba()` values with no stated "renders over X" rule, and the resulting contrast changes materially (up to 0.3:1 swing) depending on whether a badge sits on a zebra row or not. A token spec claiming AA compliance needs to either fix opaque colors per row-state or state unambiguously what they composite against — right now two different valid readings of the same token produce a pass and a fail for `badge-cancelled`.

---

## 2. Behavioral accessibility floor — coverage gaps

`EXPERIENCE.md`'s Accessibility Floor states four rules: (a) full keyboard operability, (b) aria-live for inline business-rule/validation errors, (c) icon-only actions carry a label, (d) confirm dialogs trap and return focus. Checking these against every interactive pattern actually named in both documents:

| Pattern (named in DESIGN.md / EXPERIENCE.md) | Covered by the floor? | Gap |
|---|---|---|
| FilterBar live filtering-as-you-type (debounced) | **No** | The floor's aria-live rule is scoped to "inline business-rule and validation errors" only. When the table re-renders after a filter/search keystroke, nothing announces the new result count or "no matches" to a screen-reader user (WCAG 4.1.3 Status Messages). A blind user typing into search has no non-visual signal the table changed at all. |
| New/Edit Booking modal's **live price** ("see live price" as dates/vehicle change) | **No** | Same status-message gap as above — a dynamically recalculated price with no aria-live region is invisible to a screen-reader user unless they re-navigate to it manually after every date change. |
| Success toast ("Booking created," "Customer deactivated") | **No** | The floor's aria-live rule is explicitly restricted to *error* states. A transient toast that disappears "after a few seconds" (per the design's own contrast with the never-auto-dismiss error pattern) needs `role="status"`/`aria-live="polite"` to be perceivable at all by a screen-reader user — this is unaddressed. |
| DataTable **sortable column headers** | **Partially** | Keyboard-operability is claimed generally, but nothing states the sort control's semantics: is it a real `<button>` with `aria-sort` on the `<th>`, reachable and activatable via Tab/Enter/Space? No visual sort-direction indicator token exists in `DESIGN.md` either (no chevron/icon component listed), so there's no confirmation the *sighted* keyboard user even gets a persistent visual cue of current sort state, let alone the screen-reader-facing `aria-sort` state. |
| **Modal** (Create/Edit forms) focus trap/return | **Ambiguous** | The floor's exact wording is "**Confirm dialogs** trap focus... and return focus." `Modal` and `ConfirmDialog` are named as two distinct components throughout both docs. As written, the floor's trap/return guarantee textually applies only to `ConfirmDialog`, leaving the (arguably higher-traffic) `Modal` component's focus-trap/return behavior unstated. This is very likely an oversight rather than an intentional carve-out, but as written it's a gap an implementer could legitimately read literally. |
| **Nested dialog cases**: (a) inline Customer-create Modal opened on top of the Booking-create Modal; (b) the "Discard changes?" ConfirmDialog appearing on top of an already-open Modal | **No** | Neither the floor nor the "Escape closes the topmost modal/dialog only" rule addresses what happens to the focus trap when a second layer opens. Concretely: `EXPERIENCE.md` says Escape "closes the topmost modal... only," but also says an Escape on a touched, unsaved Modal should *not* close it but instead **open** a new nested Discard-changes prompt — these two rules are in direct tension, and neither says where the focus trap boundary moves in that moment, or where focus lands if the user cancels the discard prompt (does it return to the Modal's last-focused field, or somewhere else?). |
| List loading (skeleton rows) | **No** | No `aria-busy`/loading announcement is specified; a screen-reader user tabbing into a list mid-load gets skeleton markup with no indication data is still arriving. Minor relative to the others, but the floor is silent on it. |
| Pagination (page-number links) | Yes (implicitly) | Standard links/buttons, covered by general keyboard-operability + focus-visible rules. No gap found. |
| Row "View" click / row-level Cancel/Deactivate/Erase links | Yes | Covered by keyboard operability + (for destructive actions) the ConfirmDialog focus rule. No gap. |

**Summary:** the floor's aria-live guarantee is written narrowly enough ("business-rule and validation errors") that it excludes every other kind of dynamic content update the app has — live filtering, live price, and success toasts — even though all three are exactly the WCAG 4.1.3 Status Messages scenario the aria-live mechanism exists for. And the focus-trap guarantee, read literally, doesn't extend to the plain `Modal` component or to nested dialog-over-dialog cases that the flows themselves describe (inline customer creation, discard-changes prompt).

---

## 3. Color-only signaling audit

Checked every stated state (badge, anonymized text, error row, soft-deleted row, active nav) for a non-color cue (label, icon, pattern, text change):

| State | Color cue | Non-color cue present? | Verdict |
|---|---|---|---|
| Badge (Active/Completed/Cancelled) | dot + text color | Yes — distinct text label per state ("Active"/"Completed"/"Cancelled"), always paired with the dot per `DESIGN.md`: "pill shape, dot + **label**" | OK — not color-only (separate from the contrast failures in §1, which are a different problem) |
| Anonymized customer name | muted italic tone | Yes — italic style **and** literal "(anonymized)" text suffix, explicitly required by `EXPERIENCE.md` ("never just a blank or the placeholder value alone") | OK — not color-only (again, contrast is the separate problem found in §1) |
| Error row (inline business-rule/validation error) | `error-row-bg` tint | Yes — accompanying inline error message text under the row's actions | OK |
| Soft-deleted vehicle row | dimmed/reduced-opacity row | Yes — "Restore" action replaces the normal row actions, a structural/textual change, not just a shade shift | OK |
| Destructive vs. neutral action (Erase vs. Deactivate) | danger-text tone on the Erase control | Yes — distinct button *label* ("Erase personal data" vs. "Deactivate"), plus distinct dialog copy ("permanently," "cannot be undone") | OK |
| **Active top-nav item** (Bookings/Vehicles/Customers) | `{colors.primary}` applied to "active nav item" | **No stated non-color cue** — `DESIGN.md`'s Colors section lists primary's uses as "the primary action button..., active nav item, and links," with no mention of an underline, background pill, bold weight, or `aria-current` for the active tab. As written, the only distinguishing signal for which of the three top-level sections you're in is a color change. | **Flag — likely color-only signal** (WCAG 1.4.1). This may be an underspecification rather than an intentional decision — worth an explicit non-color treatment (e.g., `aria-current="page"` for screen readers plus an underline or filled-background tab state for sighted users) before implementation. |

No other color-only cases were found; the design is otherwise consistently disciplined about pairing color with text/label, which is a genuine strength worth crediting.

---

## Summary of severity

**Certain failures (computed directly from stated hex/rgba values, no implementation assumption required):**
- `text-faint` on `surface-header` (light) — DataTable headers — 2.37:1
- `anonymized-text` on `surface` — both palettes — 2.60:1 light / 3.63:1 dark
- `badge-active` text and all three dots — light mode — 2.55–3.13:1
- `badge-cancelled` — light mode, zebra rows only — 4.29:1
- dark-mode primary button (white on `primary-dark`) — 3.23:1

**Marginal/edge failures (within 0.01–0.05 of threshold, no rendering headroom):**
- `link` on `background` (light) — 4.49:1
- `text-muted` on `background` (light) — 4.55:1
- `text-faint-dark` on `surface-dark` — 4.53:1

**Likely gaps (estimated, dependent on implementation choices not specified in the docs):**
- Input/card border non-text contrast (1.18–1.39:1) — depends on whether implementation adds a compensating fill/shadow
- Aria-live coverage does not extend to live filtering, live price calc, or success toasts
- Focus-trap/return wording covers `ConfirmDialog` but not explicitly `Modal` or nested dialog-over-dialog cases
- Active nav-item state likely relies on color alone

Given the number and severity of certain, directly-computable failures against the design's own stated tokens, the "WCAG 2.2 AA across both light and dark palettes" claim in `EXPERIENCE.md` does not hold as written. The dark palette is materially closer to compliant than light (most dark-mode pairs pass comfortably); light mode has multiple outright failures on load-bearing components (table headers, the Active badge specifically).
