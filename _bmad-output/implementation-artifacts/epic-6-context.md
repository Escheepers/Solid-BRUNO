# Epic 6 Context: Submission Readiness (+ Optional Observability)

<!-- Compiled from planning artifacts. Edit freely. Regenerate with compile-epic-context if planning docs change. -->

## Goal

An evaluator should be able to clone the repository, follow the README, and run the fully seeded system locally to verify every business rule and architectural decision end-to-end through both the Angular UI and Swagger, with backend and frontend code-coverage reports available to review. As an optional, clearly-scoped stretch sequenced last, structured application logs can ship to Loki and be viewed in Grafana. The epic closes with a verification pass confirming the Accessibility Floor claimed throughout the UX design is actually true in the shipped implementation, not just documented.

## Stories

- Story 6.1: Seed Data & README (done)
- Story 6.2: Code Coverage Reports (done)
- Story 6.3 (Optional Stretch): Observability — Logs to Grafana/Loki (done)
- Story 6.4: Accessibility Verification Pass

## Requirements & Constraints

- Submission must be reviewable and reproducible: a fresh clone + documented run steps (compose up, backend run, frontend serve) reaches a fully working, seeded system with no undocumented steps; README covers architecture, how-to-run, and an explicit assumptions section.
- Seed data must use only synthetic PII (never real personal data) and must be inserted through the normal write path (not raw SQL) so any encryption/value-conversion logic applies to seeded rows exactly as it does to real writes.
- Secrets (API key, DB connection string, encryption keys) must come from environment/config/mounted volumes only, never hardcoded or committed — including for any optional observability stack added in this epic.
- Code coverage (backend and frontend) must be visible as local, on-demand, browsable HTML reports referenced from the README — this is a local visibility aid, not a CI gate (no CI pipeline exists).
- The optional logging/observability stretch is scoped so that skipping it changes no other story's or epic's success criteria; if built, it must be log-only (no metrics/Prometheus), run locally, and PII must never appear in plaintext in shipped log output.
- WCAG 2.2 AA must hold across the entire frontend surface, in both light and dark modes: contrast verified at the token level (not just asserted), full keyboard operability, `aria-live` coverage for every dynamic content update, and no state signaled by color alone.

## Technical Decisions

- Backend coverage: `dotnet test --collect:"XPlat Code Coverage"` (coverlet.collector) processed by ReportGenerator into a browsable HTML report. Frontend coverage: `ng test --code-coverage` (Angular CLI's built-in Istanbul reporter). Both are generated locally and linked from the README.
- Seed data runs through a dedicated `ISeeder` executed once after `Database.Migrate()` on API startup, generating synthetic PII via Bogus and writing through EF Core `SaveChanges` (never raw SQL) so PII encryption value converters apply.
- Optional observability: Serilog with the `Serilog.Sinks.Grafana.Loki` sink; Loki + Grafana added as additional services in the existing `docker-compose.yml`, running alongside Postgres — local dev/demo aid only, no production deployment implied.
- Accessibility verification (Story 6.4) is a testing/confirmation pass over components already built in Epics 1-5, not new component work: verify DataTable sortable headers are real keyboard-activatable controls (Tab/Enter/Space) exposing `aria-sort`, verify Top App Bar active-nav-item state, verify focus trap/return behavior on every `Modal` and `ConfirmDialog` instance (one consolidated test sweep rather than re-deriving per feature), and spot-check rendered contrast against the current token values in the UX design's color spec.
- An earlier adversarial review of the original design tokens found certain, computed contrast failures (DataTable header text on its header background, the anonymized-customer text token, the Active-badge text and status dots in light mode, and the dark-mode primary button's white-on-fill contrast), plus an ambiguity in how translucent badge backgrounds composite over zebra vs. non-zebra rows. The design's color tokens were subsequently revised to fix these: `text-faint` and `anonymized-text` were darkened specifically to clear AA, badges moved from translucent to solid fills (removing the zebra-row ambiguity), and the dark-mode primary button fill was aligned with the light-mode value. Story 6.4 should verify the implementation matches these current (corrected) token values — the failures above describe a resolved design-time issue, not an open requirement.

## UX & Interaction Patterns

The Accessibility Floor to verify in Story 6.4:

- Every sortable `DataTable` column header is a real, keyboard-activatable control (not click-only) with `aria-sort` reflecting current state.
- The Top App Bar's active nav item is marked by color **and** bold weight **and** an underline **and** `aria-current="page"` together — never by color alone.
- `aria-live="polite"` covers every dynamic content update, not just errors: inline business-rule/validation errors, the FilterBar's live result count as the user types, the Booking-create Modal's live-computed Total Price, and success toasts (`role="status"`). List/detail loading skeletons carry `aria-busy="true"`.
- Both `Modal` and `ConfirmDialog` (not `ConfirmDialog` alone) trap focus while open and return it to the triggering element on close. The nested case — an inline "+ New Customer" `Modal` opened from within the Booking-create `Modal` — traps focus in the inner modal and returns focus to the Customer picker field on close. A touched-but-unsaved `Modal` dismissed via Escape opens a "Discard changes?" `ConfirmDialog` instead of closing directly; cancelling that dialog returns focus to the exact field the user was on in the underlying `Modal`, not to the modal's first field.
- No state is signaled by color alone: every Badge pairs color with a text label; the anonymized-customer treatment pairs color with italics plus a literal "(anonymized)" text suffix; the active nav item uses the four-cue treatment above.
- Every icon-only action carries an accessible label.

## Cross-Story Dependencies

- Story 6.4 verifies UI patterns built across Epics 1-5 (every `DataTable`, the Top App Bar, and every `Modal`/`ConfirmDialog` usage including the nested inline-Customer-create-inside-Booking-create flow from Story 4.1) rather than building new features — it depends on that prior work already existing and is a confirmation/test pass over it.
- Stories 6.1-6.3 are already implemented; 6.4 is the remaining story in this epic.
