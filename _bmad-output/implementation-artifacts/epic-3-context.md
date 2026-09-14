# Epic 3 Context: Customer Management & PII Protection

<!-- Compiled from planning artifacts. Edit freely. Regenerate with compile-epic-context if planning docs change. -->

## Goal

Build full Customer CRUD on top of Epic 1/2's foundation, with production-grade PII handling as a first-class, scored concern: encryption at rest for Email/PhoneNumber, and two deliberately distinct, never-conflated ways to stop showing a customer — a reversible Deactivate and an irreversible Anonymize — so the system correctly separates "hide" from "destroy."

## Stories

- Story 3.1: List & Create Customers
- Story 3.2: Edit a Customer
- Story 3.3: Hard-Delete a Customer with No Bookings
- Story 3.4: Deactivate & Restore a Customer
- Story 3.5: Erase (Anonymize) a Customer's Personal Data

## Requirements & Constraints

- FR2: Customer CRUD, hard-delete-when-no-bookings guard. FR11: soft-delete/restore. FR12: anonymize.
- Hard delete is allowed only if the customer has no bookings; if they do, hard delete is blocked and two separate alternatives are offered instead — soft-delete (reversible, PII intact) or anonymize (irreversible, PII scrubbed). These two are never conflated: soft-delete never destroys PII, anonymize never undoes itself.
- UX-DR4: the soft-delete-vs-anonymize choice must be two separate, visually distinct actions (neutral "Deactivate" vs. harsher-styled "Erase personal data") — never merged into one "Delete" control — each with its own `ConfirmDialog` variant stating the specific, correct consequence.

## Technical Decisions

- PII encryption via an EF Core value converter backed by ASP.NET Core Data Protection (`IDataProtector`); Data Protection keys persisted to a docker-compose-mounted volume, never the ephemeral in-container default. Seed/test data must go through the same `SaveChanges` path, never raw SQL, so the converter always applies (AD-12). Already implemented in Story 3.1 via `AppDbContext`'s value converter plus an `EmailHash` shadow property (SHA-256, computed pre-save) backing the real DB-level uniqueness constraint, since Data Protection ciphertext is non-deterministic and can't be uniquely indexed directly.
- `IsAnonymized` participates in the same global query-filter exclusion as `IsDeleted` — an anonymized (or soft-deleted) customer can't be selected for a new booking. Booking→Customer history/detail joins must explicitly call `.IgnoreQueryFilters()` so a soft-deleted/anonymized customer still resolves on historical records (AD-13).
- Aggregates are constructed only via a static factory/non-default constructor; no public settable property; state changes only through aggregate-root methods (AD-15).
- Mutating commands whose effect is a state flip rather than returning a resource (Cancel/SoftDelete/Anonymize/Delete) return `Unit`/204, not a DTO (AD-2).
- `Customer` schema: Id, FirstName, LastName, Email (unique, encrypted), PhoneNumber (encrypted), CreatedDate, IsDeleted, IsAnonymized.

## UX & Interaction Patterns

- Two visually distinct row/detail actions, never merged: neutral "Deactivate" (`ConfirmDialog` neutral variant, states the consequence is reversible) and harsher-styled "Erase personal data" (`ConfirmDialog` destructive variant, danger-toned confirm button, states "permanently," "cannot be undone," and explicitly confirms booking history stays intact under a placeholder name).
- An anonymized customer's placeholder name renders in a distinct muted-italic treatment (`anonymized-text` token) everywhere it appears, including on old bookings' rows in the Bookings list — color is never the only signal (paired with italics + literal "(anonymized)" text suffix), since this must clear WCAG AA as a safety-critical distinction, not just a stylistic one.
- No destructive or state-changing action ever fires on a single click — every one requires its matching `ConfirmDialog`.
- No Customer detail page exists anywhere in this project's scope (established in Story 3.4) — Deactivate/Erase are permanent Customers-list row actions, not detail-view actions, despite epics.md's AC phrasing referencing "their detail view."

## Cross-Story Dependencies

- Story 3.5 is the first real consumer of `ConfirmDialog`'s destructive variant (deferred until now on purpose since nothing before it needed an irreversible action).
- Story 3.5 depends on Story 3.3's booking-existence check pattern (`ExistsForCustomerAsync`) only insofar as both hard-delete and anonymize are alternatives offered when a customer has bookings — but per Story 3.4's precedent (Deactivate is NOT gated on booking count), Anonymize likely follows the same reasoning: nothing in the domain rules requires gating it on booking count either, since epics.md's AC phrasing describes the realistic use case, not an enforced precondition. Confirm this scope decision explicitly in the story spec.
