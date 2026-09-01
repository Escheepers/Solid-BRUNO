# Epic 1 Context: Project Foundation & Walking Skeleton

<!-- Compiled from planning artifacts. Edit freely. Regenerate with compile-epic-context if planning docs change. -->

## Goal

This epic proves the entire technical stack end-to-end through the thinnest real vertical slice, rather than front-loading a full feature epic before anything is verified to connect. It stands up the Clean Architecture backend solution structure, a real Postgres database via Docker Compose with the first EF Core migration, API-key authentication enforced by default, a global ProblemDetails error contract, the Angular app shell with core design tokens and a centralized API client, and TDD-first test-project scaffolding with coverage reporting. The one user-facing proof this epic ships is a staff member viewing a paginated, filterable, read-only list of vehicles — deliberately no create/edit/delete yet — exercising every layer (Angular → ApiClient → API-key auth → query handler → repository → PostgreSQL → rendered table) so every later epic builds on a foundation already known to work.

## Stories

- Story 1.1: Backend Solution Scaffolding & Test Harness
- Story 1.2: Local Database & Docker Compose Environment
- Story 1.3: Vehicle Domain Model (TDD)
- Story 1.4: API-Key Authentication Foundation
- Story 1.5: Global Error Handling (ProblemDetails)
- Story 1.6: Angular App Shell & Design Tokens
- Story 1.7: Vehicle List — the Walking Skeleton Proof

## Requirements & Constraints

- Vehicle management is read-only in this epic: list/filter only, no create/edit/delete (those land in Epic 2). Soft-deleted vehicles are excluded from the default list via a query filter, established here for reuse later.
- All list/search/pagination endpoints must follow one consistent shape and query-param convention from the start (page, pageSize, entity-specific search param), since every later list screen (Customers, Bookings) reuses this exact pattern.
- Every endpoint must require a valid API key by default (secure-by-default fallback policy), not per-endpoint opt-in — this protection must already be in place before any real feature endpoint is added in later epics.
- Domain business-rule violations must surface as specific, actionable errors, never a generic failure — this epic establishes the mechanical split (input-shape problems vs. state-dependent problems) that every later epic's business-rule feedback depends on.
- The backend must demonstrate genuine Clean Architecture/CQRS layering (rich domain model, thin controllers, enforced dependency direction) — this is graded, and every entity added later inherits this same layering.
- The frontend must demonstrate a scalable, feature-based Angular structure with centralized HTTP handling — features never call the HTTP client directly.
- The whole system is built test-first (red-green-refactor) with visible coverage reporting; this epic sets up the test-project harness and coverage tooling every later story is expected to use, not retrofit.
- HTTPS is enforced for all API traffic; secrets (DB connection string, API key, encryption keys) come only from environment/config/mounted volumes, never hardcoded.

## Technical Decisions

- Solution structure: `BrunoVehicleHire.Domain/Application/Infrastructure/Api` projects plus a matching `tests/` tree (`Domain.Tests`, `Application.Tests`, `Integration.Tests`). Domain has zero project references; Application depends only on Domain; Infrastructure/Api depend on Application + Domain. Enforced by an architecture fitness test (NetArchTest.Rules or ArchUnitNET) asserting no `Api.Controllers` type references `Infrastructure.*`.
- CQRS via MediatR (Community edition, free license key self-registered at startup). Application layer organized as vertical slices per entity (`Vehicles/{Commands,Queries,Dtos,Validators}`), never a horizontal Commands/Queries split.
- One repository interface per aggregate root (`IVehicleRepository`), with a single `IUnitOfWork.SaveChangesAsync()` invoked once per handler — repositories never call `SaveChangesAsync` themselves.
- Aggregates are constructed only via a static factory (`Vehicle.Create(...)`); no public settable properties; state changes only via aggregate methods (e.g. `Vehicle.SoftDelete()`). Ids are `Guid.CreateVersion7()`, assigned at construction, never by an EF Core value generator or DB default.
- FluentValidation validators run inside a MediatR `ValidationBehavior<TRequest,TResponse>` pipeline, never invoked manually; every paginated/filterable Query needs a validator bounding `page`/`pageSize`.
- API error contract: RFC 9457 ProblemDetails via `AddProblemDetails()` + a custom `IExceptionHandler`. Rule of thumb: pure input-shape validation (FluentValidation) → 400; anything requiring loaded state to evaluate → 409. Never interpolate raw PII into a `ProblemDetails.detail` string.
- Paginated list responses use one shared `PagedResult<T>` shape (`items`, `totalCount`, `page`, `pageSize`) across all entities.
- API-key authentication via a custom `AuthenticationHandler` registered through `AddAuthentication().AddScheme<...>()`, enforced via a global `FallbackPolicy` (secure-by-default, no per-endpoint `[Authorize]`), integrated with the Swagger security definition. Key travels in the `X-Api-Key` header and must never appear in plaintext logs.
- Postgres via Docker Compose, with the `btree_gist` extension enabled (needed later for booking overlap, not used yet) and a mounted volume for Data Protection keys (needed later for PII encryption). `Database.Migrate()` runs on API startup. This epic's migration creates only the `Vehicles` table (Id, RegistrationNumber unique, Make, Model, Year, DailyRate, IsDeleted, CreatedDate).
- CORS: named policy allowing only the Angular dev server origin (`localhost:4200`), dev-only.
- Testing stack: xUnit, NSubstitute, FluentAssertions for unit tests; `Testcontainers.PostgreSql` for integration tests (needed for real constraint verification later, scaffolded now). Coverage: `coverlet.collector` + `ReportGenerator` (backend HTML report), `ng test --code-coverage` (frontend Istanbul HTML report) — both local, on-demand, not CI-gated.
- Frontend: one centralized `ApiClient` in `core/` owns base URL, error normalization (distinguishing 400/409 business errors from 5xx/network failures), and the API-key header attachment — feature modules never call `HttpClient` directly. `core/models` holds API DTOs; `features/{x}/models` will hold view models with an explicit mapper (pattern established here, exercised fully once more entities exist).
- Stack pins: .NET 10, Angular 22, EF Core 10, PostgreSQL 17+ (18 recommended), MediatR 14.2.0, FluentValidation 12.1.1.

## UX & Interaction Patterns

- Implement only the core "Quiet Enterprise" design tokens this epic needs: colors (light+dark), typography, rounded scale, spacing scale — not the full shared component library (Modal, ConfirmDialog, Badge, etc. arrive with their first real consumer in later epics).
- Components needed now: Top App Bar (Bookings/Vehicles/Customers nav, only Vehicles functional), DataTable (zebra striping, keyboard-sortable headers with `aria-sort`), FilterBar (live/debounced filtering, no "Apply" button), Skeleton (loading placeholder, never a spinner).
- List loading state: Skeleton rows with `aria-busy="true"` on the container. Empty states are distinct: "No vehicles yet — create one" (genuinely empty) vs. "No vehicles match these filters" + "Clear all filters" (filtered-to-empty) — never a bare silent empty table.
- Pagination is page-number links, never infinite scroll. Filtering is live/debounced on the search field, never gated behind a separate "Apply" action.
- The ApiClient's error normalization must produce a distinct "server error" shape for 5xx/network failures, separate from the 400/409 business-error shape, so later UI never conflates an infrastructure failure with a rule violation.

## Cross-Story Dependencies

- Story 1.1 (solution scaffolding) is the prerequisite for every other story in this epic — the projects and test harness it creates are what 1.2–1.5 add code into.
- Story 1.2's migration depends on Story 1.3's `Vehicle` entity existing (schema is generated from the domain model).
- Stories 1.4 and 1.5 (auth and error handling) are cross-cutting API concerns that Story 1.7's end-to-end flow depends on being in place first.
- Story 1.6 (Angular shell/tokens) and Stories 1.1–1.5 (backend) can proceed in parallel; Story 1.7 is the integration point requiring all of 1.1–1.6 complete, since it is the full-stack proof slice.
- `Vehicle.SoftDelete()` is built in Story 1.3, but no `Restore()` method exists yet — Epic 2 adds it when restore functionality is needed. Vehicle create/edit/soft-delete UI and the rest of the shared component library (Modal, Input, ConfirmDialog) are explicitly deferred to Epic 2, which builds directly on this epic's list screen and design tokens.
