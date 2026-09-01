---
id: SPEC-bruno-vehicle-hire
companions: [domain-model.md, stack.md, architecture-diagrams.md, ../../planning-artifacts/architecture/architecture-Solid-Bruno-2026-08-25/ARCHITECTURE-SPINE.md, ../../planning-artifacts/ux-designs/ux-Solid-Bruno-2026-08-27/DESIGN.md, ../../planning-artifacts/ux-designs/ux-Solid-Bruno-2026-08-27/EXPERIENCE.md]
sources: ["C:/Users/EtienneScheepers/Downloads/Bruno - Solid Level Movement 3.pdf"]
---

> **Canonical contract.** This SPEC and the files in `companions:` are the complete, preservation-validated contract for what to build, test, and validate. Source documents listed in frontmatter are for traceability — consult them only if you need narrative rationale or prose color this contract intentionally omits.

# Bruno Vehicle Hire — Solid Developer Movement Assessment

## Why

This is a technical skill assessment ("Solid Developer — Movement Assessment") set by Bruno Vehicle Hire to evaluate a candidate's ability to architect a small system end-to-end: DDD-flavored domain modelling, cross-entity business rule enforcement, Clean Architecture on the backend, and scalable feature-based structure on the frontend. The candidate (the user) is building the deliverable as a personal submission, targeting completion within about a month, with no external deadline. Evaluation is explicitly against: system design thinking, domain modelling quality, correct enforcement of business rules, architectural consistency, frontend modularization, API quality, edge case handling, and code maintainability — the mandate is to demonstrate production-level engineering judgment against those criteria, not to build a real commercial product for real customers.

## Capabilities

- **CAP-1**
  - **intent:** Manage vehicles (create, edit, list, soft-delete) as the rentable inventory.
  - **success:** Vehicles can be created, edited, and listed; soft-deleted vehicles are excluded from booking availability and from default listings.

- **CAP-2**
  - **intent:** Manage customers (create, edit, list, delete) who place bookings.
  - **success:** Customers can be created, edited, and listed; hard delete succeeds only when the customer has zero bookings; when bookings exist, hard delete is blocked with a clear error and the user is offered soft-delete or anonymize instead (CAP-11, CAP-12).

- **CAP-3**
  - **intent:** Create, view, and cancel bookings linking one vehicle and one customer over a date range.
  - **success:** The system rejects a booking that overlaps an existing booking for the same vehicle, rejects EndDate ≤ StartDate, blocks booking a soft-deleted vehicle, allows deletion only of future bookings, and tracks status (Active / Completed / Cancelled).

- **CAP-4**
  - **intent:** Search, filter, and page through vehicles, customers, and bookings.
  - **success:** List views and their backing endpoints return correctly filtered, correctly paginated subsets for all three entities.

- **CAP-5**
  - **intent:** Surface domain business-rule violations to the user at the point of action, not as generic failures.
  - **success:** Attempting an invalid action (e.g. an overlapping booking) shows a specific, actionable inline error in the UI.

- **CAP-6**
  - **intent:** Restrict API access to holders of a valid API key.
  - **success:** Requests without a valid key are rejected; the frontend attaches the key without exposing it in source control.

- **CAP-7**
  - **intent:** Demonstrate a Clean Architecture backend with rich domain models and CQRS.
  - **success:** Domain, Application, Infrastructure, and API layers are clearly separated; business rules live inside domain entities and value objects, not in controllers or handlers; controllers stay thin; database schema is produced via code-first migrations. Mandated patterns and stack are fixed in `stack.md`.

- **CAP-8**
  - **intent:** Demonstrate a scalable, feature-based Angular frontend.
  - **success:** Folder layout, module boundaries, and state-management approach match the Solid-level expectations fixed in `stack.md` and `architecture-diagrams.md`.

- **CAP-9**
  - **intent:** Prove core logic correctness with automated tests.
  - **success:** At least 2 command tests, 2 query tests, and 1 business-rule test exist and pass.

- **CAP-10**
  - **intent:** Make the submission reviewable and reproducible by an evaluator.
  - **success:** A README explains the architecture, how to run the project, and assumptions made; seed data loads cleanly on a fresh database; commit history is meaningful.

- **CAP-11**
  - **intent:** Reversibly hide a customer with bookings (e.g. accidental-delete recovery) without touching their PII.
  - **success:** A soft-deleted customer is excluded from default listings and new bookings, and can be fully restored with all original data intact — mirroring the Vehicle soft-delete pattern.

- **CAP-12**
  - **intent:** Permanently scrub a customer's PII for a real erasure request, while preserving booking referential integrity.
  - **success:** After anonymizing, the customer's name/email/phone are replaced with placeholders, the action cannot be reverted, and existing bookings still reference the customer id and remain intact.

- **CAP-13** *(optional stretch — see Constraints)*
  - **intent:** Persist structured application logs to a queryable, viewable store rather than console-only output.
  - **success:** Logs ship via a Serilog sink to Loki, are queryable/viewable in Grafana with at least one dashboard panel (e.g. request rate or business-rule-violation count), and PII stays redacted per the existing logging constraint.

## Constraints

- Backend: Clean Architecture with CQRS via MediatR on .NET, EF Core code-first migrations, PostgreSQL persistence — mandated stack, see `stack.md`.
- Frontend: Angular with a feature-module structure — mandated framework (candidate's choice, made for lower risk on a graded assessment given existing Angular expertise), see `stack.md`.
- API authentication is API-key only — no OAuth/JWT/user login.
- The domain business rules in `domain-model.md` (no booking overlap per vehicle, soft-delete exclusion, customer/booking delete guards, EndDate > StartDate) are non-negotiable; violating any is treated as an assessment failure.
- Minimum automated test coverage: 2 command tests, 2 query tests, 1 business-rule test.
- Target completion timeframe: about one month, self-imposed and not externally enforced.
- Customer PII (Email, PhoneNumber) is encrypted at rest.
- Logs and the global exception-handling middleware must never contain PII in plaintext.
- HTTPS is enforced for all API traffic.
- Seed data uses only synthetic PII, never real personal data.
- Secrets (API key, DB connection string) come from environment/config only, never hardcoded or committed.
- Customer soft-delete and Customer anonymize are separate operations and must never be conflated: soft-delete is always reversible, anonymize is always irreversible.
- Logging sink is Grafana + Loki (log-only, no Prometheus/metrics) — chosen over Seq for dashboard/portfolio value.
- Grafana and Loki run locally via docker-compose alongside Postgres, for local dev/demo viewing only — not a production deployment requirement.
- CAP-13 (Grafana+Loki logging) is optional/stretch and sequenced last, built only after the rest of the deliverable is solid; dropping it does not affect the success criteria of any other capability.

## Non-goals

- Not a production multi-tenant SaaS — no user accounts or roles beyond the single shared API key.
- No payment processing — `TotalPrice` is calculated and stored, never charged.
- No vehicle telematics, GPS, or hardware integration.
- No CI/CD pipeline or hosting/deployment infrastructure — scope is code plus README.
- No exploration of alternative frontend frameworks (React, React Native, Flutter) — Angular only.

## Success signal

A reviewer can clone the repo, follow the README, run the seeded database and both apps locally, exercise all three modules end-to-end through the Angular UI and Swagger, and see every domain business rule (overlap prevention, soft-delete exclusion, delete guards, date validation) correctly enforced with clear error feedback — with the minimum required test suite passing throughout. Additionally, inspecting the raw database confirms Customer email/phone are stored encrypted (not plaintext), a soft-deleted customer can be restored with original PII intact, and an anonymized customer's PII is irreversibly replaced while their booking history remains queryable.

## Assumptions

- .NET and Angular versions are the latest stable/LTS available at implementation time — the brief does not pin versions.
- A single shared API key is sufficient; no per-user login or roles are needed on either frontend or backend beyond that key.
- Local-run only: the README covers local setup; no cloud hosting or deployment is required.
- Domain Events and Unit of Work are optional/bonus per the brief and will be implemented only if the ~1-month timeframe allows.
- PII encryption is implemented at the application layer via an EF Core value converter (AES), not database-level TDE, for portability across local/dev environments.
- Synthetic seed PII is generated via a fake-data library (e.g. Bogus) rather than any real personal data.
