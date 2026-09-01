# Mandated Stack & Patterns

Implementation prescription for CAP-7 and CAP-8. Fixed by the assessment brief plus the decisions logged in `.memlog.md` (frontend framework, database).

## Backend

**Language/runtime:** .NET (implied by MediatR/EF Core — brief does not pin a version; assume latest LTS, see SPEC.md Assumptions).

**Architecture — Clean Architecture, strictly enforced:**
- **Domain layer** — rich domain models; business rules live inside entities; Value Objects (e.g. `DateRange` for booking overlap logic).
- **Application layer** — Commands/Queries (CQRS); DTOs; validation.
- **Infrastructure layer** — EF Core; repository implementations.
- **API layer** — thin controllers only.

**Required patterns:**
- Repository Pattern
- CQRS
- MediatR
- FluentValidation
- EF Core code-first migrations
- Domain Events — bonus, optional (see SPEC.md Assumptions)
- Unit of Work — optional but recommended (see SPEC.md Assumptions)

**Database:** PostgreSQL (decided over SQL Server).
- Proper indexing.
- Foreign keys enforced.
- Unique constraints (Vehicle.RegistrationNumber, Customer.Email).
- Soft-delete filtering via EF Core global query filters (Vehicle, and Customer soft-delete per CAP-11).

**API:**
- RESTful endpoints for all three entities.
- API Key authentication (only auth mechanism — see SPEC.md Constraints).
- Global exception-handling middleware — must not leak PII into error responses or logs.
- Logging — structured, with PII fields (email, phone) excluded/redacted from all log output.
- Pagination + filtering on list endpoints.
- Swagger with examples.
- HTTPS enforced (redirect/require TLS).

**Logging & observability (CAP-13, optional stretch — build last):**
- Serilog as the logging library, with a `Serilog.Sinks.Grafana.Loki` sink shipping structured logs to Loki.
- Loki + Grafana run as additional services in the same `docker-compose.yml` as Postgres — local dev/demo only, not a deployment requirement.
- Grafana provisioned with a Loki datasource and at least one dashboard panel (e.g. request rate, or count of business-rule violations like rejected overlapping bookings) — the panel is the actual payoff; raw log tailing alone doesn't demonstrate more than console output would.
- PII redaction rule (below) applies identically to whatever ships to Loki.
- This capability is decoupled from everything else: if time runs short, drop it without touching any other capability's success criteria. Chosen over Seq specifically for the dashboard/portfolio value, accepting the extra setup time as a deliberate tradeoff.

**PII protection (Customer.Email, Customer.PhoneNumber):**
- Encrypted at rest via an EF Core value converter (AES) at the application layer — not database-level TDE, for portability across dev environments.
- Never logged in plaintext anywhere (application logs, exception middleware, request logging).
- Secrets (API key, DB connection string, encryption key) sourced from environment/config only — never hardcoded or committed.
- Seed data uses only synthetic PII (e.g. generated via a fake-data library such as Bogus), never real personal data.
- Soft-delete (reversible, PII intact) and anonymize (irreversible, PII scrubbed) are implemented as two distinct commands — never merged into one operation.

## Frontend — Angular

**Required:**
- Feature modules per entity (Vehicles, Customers, Bookings).
- Shared module.
- Angular services as the abstraction over the API.
- RxJS operators used properly (no unmanaged subscriptions).
- Reactive Forms.
- Interceptor for attaching the API key.
- Route guards — bonus.

**Expected competencies:**
- Smart vs. presentational component split.
- Observable streams for data flow (avoid manual state juggling where an observable fits).
- Form group validation matching backend FluentValidation rules.
- Error interceptor for centralized API error handling.
- Proper module boundaries (no cross-feature reach-in).

**Required functional features (all frameworks, per brief):**
- Full Vehicles, Customers, and Bookings modules.
- Filtering & search.
- Date handling.
- Business-rule feedback (e.g. overlapping bookings) surfaced inline.
- Centralized error handling.
- Reusable modals.
- Confirmation dialogs.
- API key handled securely (never committed, via environment configuration).
- Proper environment configuration (dev vs. prod API base URL, key).

**Architecture expectations (Solid level):**
- Feature-based structure — not entity-by-entity pages dumped in root.
- Centralized API client.
- Separation of Domain models / DTOs / View models.
- Reusable shared components.
- Clear, explicit state-management approach.
- Scalable folder layout (see `architecture-diagrams.md`).

## Non-functional requirements

- Unit tests (minimum): 2 Commands, 2 Queries, 1 business-rule test.
- Meaningful commit history.
- Proper error handling throughout.
- Clean README: architecture explanation, how to run, assumptions made.
- Seed data.
