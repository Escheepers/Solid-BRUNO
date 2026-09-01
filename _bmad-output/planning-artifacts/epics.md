---
stepsCompleted: [1, 2, 3, 4]
inputDocuments:
  - _bmad-output/specs/spec-bruno-vehicle-hire/SPEC.md
  - _bmad-output/specs/spec-bruno-vehicle-hire/domain-model.md
  - _bmad-output/specs/spec-bruno-vehicle-hire/stack.md
  - _bmad-output/planning-artifacts/architecture/architecture-Solid-Bruno-2026-08-25/ARCHITECTURE-SPINE.md
  - _bmad-output/planning-artifacts/ux-designs/ux-Solid-Bruno-2026-08-27/DESIGN.md
  - _bmad-output/planning-artifacts/ux-designs/ux-Solid-Bruno-2026-08-27/EXPERIENCE.md
  - "C:/Users/EtienneScheepers/Downloads/Bruno - Solid Level Movement 3.pdf"
---

# Bruno Vehicle Hire - Epic Breakdown

## Overview

This document provides the complete epic and story breakdown for Bruno Vehicle Hire, decomposing the requirements from SPEC.md (no PRD was produced for this project — `bmad-spec` was used instead, since the assessment brief was fully specified enough that a PRD would have only restated it), the Architecture spine, and the UX design contract (DESIGN.md + EXPERIENCE.md) into implementable stories.

**Development practice: Test-Driven Development (TDD) throughout.** Every story in this breakdown that touches a command handler, query handler, domain rule, or reusable frontend component is implemented test-first (see NFR5) — its acceptance criteria and Definition of Done assume a failing test exists before the implementation does, not that tests are added afterward. Test project scaffolding is part of Epic 1's setup, before any feature work begins.

**No greenfield starter template is specified.** The Architecture spine prescribes a bespoke Clean Architecture solution structure (`BrunoVehicleHire.Domain/Application/Infrastructure/Api` projects, vertical-slice Application layer, a matching `tests/` tree) rather than scaffolding from a pre-built starter — Epic 1 Story 1 creates this structure from scratch.

## Requirements Inventory

### Functional Requirements

FR1 (CAP-1): Manage vehicles — create, edit, list, and soft-delete vehicles as rentable inventory. Soft-deleted vehicles are excluded from booking availability and default listings.

FR2 (CAP-2): Manage customers — create, edit, list, and delete customers who place bookings. Hard delete succeeds only when the customer has zero bookings; when bookings exist, hard delete is blocked with a clear error and the user is offered soft-delete or anonymize instead (FR11, FR12).

FR3 (CAP-3): Create, view, and cancel bookings linking one vehicle and one customer over a date range. Reject a booking that overlaps an existing booking for the same vehicle, reject EndDate ≤ StartDate, block booking a soft-deleted vehicle, allow cancellation only of future/Active bookings, and track Status (Active / Completed / Cancelled).

FR4 (CAP-4): Search, filter, and paginate vehicles, customers, and bookings via consistent list endpoints/UI.

FR5 (CAP-5): Surface domain business-rule violations to the user at the point of action with a specific, actionable inline error — never a generic failure message.

FR6 (CAP-6): Restrict API access to holders of a valid API key; the frontend attaches the key without exposing it in source control.

FR7 (CAP-7): Demonstrate a Clean Architecture backend with rich domain models and CQRS — Domain/Application/Infrastructure/API layers clearly separated, business rules inside entities/value objects (not controllers/handlers), thin controllers, database schema via code-first migrations.

FR8 (CAP-8): Demonstrate a scalable, feature-based Angular frontend — folder layout, module boundaries, and state management matching the Solid-level expectations fixed in the Architecture spine and UX design contract.

FR9 (CAP-9): Prove core logic correctness with automated tests — at least 2 command tests, 2 query tests, and 1 business-rule test.

FR10 (CAP-10): Make the submission reviewable and reproducible — README explaining architecture, how to run, and assumptions made; seed data loads cleanly on a fresh database; meaningful commit history.

FR11 (CAP-11): Reversibly soft-delete/restore a customer with bookings (e.g. accidental-delete recovery) without touching their PII.

FR12 (CAP-12): Permanently anonymize a customer's PII for a real erasure request while preserving booking referential integrity — irreversible by design, no un-anonymize path exists.

FR13 (CAP-13, optional stretch — sequenced last, dropping it affects no other capability's success criteria): Ship structured application logs via a Serilog sink to Loki, viewable/queryable in Grafana with at least one dashboard panel; PII stays redacted in all log output.

### NonFunctional Requirements

NFR1: Backend uses Clean Architecture with CQRS via MediatR on .NET 10, EF Core code-first migrations, PostgreSQL persistence.

NFR2: Frontend uses Angular 22 with a feature-module structure (mandated framework, candidate's choice).

NFR3: API authentication is API-key only — no OAuth/JWT/user login — enforced secure-by-default via a global fallback authorization policy, not opt-in `[Authorize]` attributes.

NFR4: The domain business rules (no booking overlap per vehicle, soft-delete exclusion, customer/booking delete guards, EndDate > StartDate) are non-negotiable; violating any is treated as an assessment failure.

NFR5: The entire system — backend and frontend — is built using **Test-Driven Development (TDD)**: for every command handler, query handler, domain method/business rule, and reusable frontend component, a failing test is written first, then the minimum implementation to pass it, then refactor (red-green-refactor). This is the actual development practice for the whole build, not an after-the-fact coverage pass. The assessment's stated floor — at least 2 command tests, 2 query tests, and 1 business-rule test — is the documented minimum a TDD-built system will clear naturally many times over, not a target to write tests toward after the fact. Tooling: xUnit, NSubstitute, FluentAssertions for backend unit tests; Testcontainers.PostgreSql for integration tests exercising real relational constraints; an equivalent test-first discipline for frontend components via Angular's built-in test runner. Code coverage is visible, not just measured internally: backend coverage is collected via `coverlet.collector` (10.0.1, `dotnet test --collect:"XPlat Code Coverage"`) and rendered as a browsable HTML report via `ReportGenerator`; frontend coverage uses Angular CLI's built-in `ng test --code-coverage` (Istanbul-based HTML report, no extra dependency). Both reports are generated locally and referenced from the README — this is a local visibility tool, not a CI gate (no CI pipeline exists per the non-goals).

NFR6: Customer PII (Email, PhoneNumber) is encrypted at rest and never appears in plaintext in logs or the global exception-handling middleware's error responses.

NFR7: HTTPS is enforced for all API traffic.

NFR8: Seed data uses only synthetic PII, never real personal data.

NFR9: Secrets (API key, DB connection string, Data Protection encryption keys) come from environment/config/mounted volumes only — never hardcoded or committed.

NFR10: Customer soft-delete and Customer anonymize are separate operations that must never be conflated — soft-delete is always reversible, anonymize is always irreversible.

NFR11: WCAG 2.2 AA accessibility across the whole frontend surface, in both light and dark modes — token-level contrast verified (not just asserted), full keyboard operability, `aria-live` coverage for every dynamic content update, no state signaled by color alone.

NFR12: Target completion timeframe is about one month, self-imposed and not externally enforced.

NFR13 (applies only if FR13 is built): The logging sink is Grafana + Loki (log-only, no Prometheus/metrics), running locally via docker-compose alongside Postgres — a local dev/demo aid, not a production deployment requirement.

### Additional Requirements

- No starter/scaffolding template — bespoke Clean Architecture solution structure per the Architecture spine's Structural Seed: `BrunoVehicleHire.Domain/Application/Infrastructure/Api` projects, vertical-slice Application layer (`Vehicles/Customers/Bookings` × `{Commands,Queries,Dtos,Validators}`), and a matching `tests/` tree (`Domain.Tests`, `Application.Tests`, `Integration.Tests`).
- Dependency direction is enforced by an architecture fitness test (NetArchTest.Rules or ArchUnitNET) asserting no type in `Api.Controllers` references `Infrastructure.*` (AD-1), run as part of the test suite.
- CQRS via MediatR, Community edition — self-register the free Community license key at startup. Every mutating Command handler returns its aggregate's DTO (the same shape its sibling Query returns), except Cancel/SoftDelete/Anonymize/Delete commands, which return `Unit`/204 (AD-2).
- One specific repository interface per aggregate root (`IVehicleRepository`, `ICustomerRepository`, `IBookingRepository`) + a single `IUnitOfWork.SaveChangesAsync()` invoked once per handler; repository methods never call `SaveChangesAsync` themselves (AD-5).
- Primary keys are `Guid.CreateVersion7()` (time-ordered), assigned by the entity's constructor/factory at construction time — never by an EF Core value generator or DB default (AD-6).
- Booking overlap is enforced two ways: an application-level check (`DateRange.Overlaps`, half-open interval — `EndDate` is checkout day, exclusive) AND a Postgres `EXCLUDE USING GIST` constraint (requires the `btree_gist` extension) as a database-level race-condition backstop (AD-7).
- API error contract: RFC 9457 ProblemDetails via `AddProblemDetails()` + `IExceptionHandler`. General test for 400 vs 409: FluentValidation (input-shape-only) → 400; anything requiring loaded state or an entity method to evaluate (overlap, cancel guards, booking a soft-deleted vehicle, duplicate `RegistrationNumber`/`Email`) → 409. Every `type` URI follows `urn:bruno:{entity}:{rule-kebab-case}` from one const list. Never interpolate raw PII into a `ProblemDetails.detail` string (AD-8).
- Booking `StartDate`/`EndDate` are `DateOnly` (AD-9).
- FluentValidation runs via a MediatR pipeline `ValidationBehavior<TRequest,TResponse>`, never invoked manually; every Query with pagination/filter parameters has a validator bounding `page`/`pageSize` (AD-10).
- API key authentication via a custom `AuthenticationHandler` (`AddAuthentication().AddScheme<...>()`), integrated with `[Authorize]` and the Swagger security definition; the key travels in an `X-Api-Key` request header (AD-11).
- PII encryption via an EF Core value converter backed by ASP.NET Core's Data Protection API (`IDataProtector`); Data Protection keys persisted via `PersistKeysToFileSystem` to a docker-compose-mounted volume (never the ephemeral in-container default); seed data must be inserted through the same EF Core `SaveChanges` path, never raw SQL, so the value converter always applies (AD-12).
- `IsAnonymized` participates in the same global query-filter exclusion as `IsDeleted` (an anonymized customer can't be selected for a new booking); Booking→Customer history/detail joins explicitly call `.IgnoreQueryFilters()` so a soft-deleted/anonymized customer still resolves on historical records (AD-13).
- Every paginated list endpoint returns one `PagedResult<T>` shape (`{ items, totalCount, page, pageSize }`); request side uses `page` (1-indexed), `pageSize`, and an entity-specific `search` param, consistently named (AD-14).
- Aggregates are constructed only via a static factory/non-default constructor (`Vehicle.Create(...)`, `Booking.Create(...)`); no entity exposes a public settable (`set`/`init`) property; state changes only through aggregate-root methods (AD-15).
- Booking "delete" is implemented as **Cancel** (`Status: Active → Cancelled` via `Booking.Cancel()`) — never a physical row delete (AD-16).
- Booking `Completed` transition is handled by a `BookingCompletionSweepService : BackgroundService` running on a timer, dispatching a `CompleteBookingCommand` through MediatR per past-EndDate Active booking — never a Query-handler write (AD-17).
- Testing stack: xUnit, NSubstitute, FluentAssertions; `Testcontainers.PostgreSql` (4.12.0) for Postgres-backed integration tests verifying unique/FK constraints and the AD-7 `EXCLUDE` constraint, which EF Core's InMemory provider can't enforce (AD-18).
- Migrations applied via `Database.Migrate()` on API startup; a dedicated `ISeeder` runs once after migration, generating synthetic PII via Bogus, inserted through EF Core `SaveChanges`.
- CORS: a named policy allowing only the Angular dev server origin (`localhost:4200`), dev-only.
- Frontend: one centralized `ApiClient` in `core/` (base URL, error normalization, API-key interceptor) — features never call `HttpClient` directly; `core/models` holds API DTOs, `features/{x}/models` holds view models, with an explicit per-feature DTO→ViewModel mapper function.
- Frontend state: Angular Signals (local/UI) + TanStack Query's Angular adapter (server state) — all server-state writes go through TanStack mutations invalidating the owning entity's query keys (`[entityName, 'list'|'detail', ...params]`); pin an exact version of `@tanstack/angular-query-experimental` (no caret range) since it still ships as experimental (AD-3).
- Explicitly deferred/out of scope: Domain Events, Redis/distributed caching, CI/CD pipeline, hosting/deployment infrastructure, rate limiting, an un-anonymize/PII-recovery window.

### UX Design Requirements

UX-DR1: Implement the full "Quiet Enterprise" design token set (Tailwind config): colors (background/surface/surface-alt/border/input-bg/input-border, four text tiers, primary/link, success/neutral/danger semantic pairs, anonymized-text — each with verified light+dark hex per DESIGN.md), typography (5 roles: page-title, section-label, body, body-emphasis, button — single system sans-serif stack, no serif/mono), rounded scale (sm/md/lg/xl/full), spacing scale (steps 1-9 plus the `row-y`/`row-x` density tokens).

UX-DR2: Build the shared component library exactly to DESIGN.md's spec: Button (primary), Input, Badge (Active/Completed/Cancelled variants), DataTable (zebra striping, keyboard-sortable headers with `aria-sort`, error-row variant), FilterBar, Top App Bar, Toast, Skeleton, ConfirmDialog (neutral + destructive variants), Modal, Customer Summary card, Pager.

UX-DR3: Implement all 10 IA surfaces: Bookings list/detail/create-edit modal, Vehicles list/detail/create-edit modal, Customers list/detail/create-edit modal, and the read-only/printable Customer Summary.

UX-DR4: Implement the soft-delete-vs-anonymize UI pattern as two separate, visually distinct actions (a neutral "Deactivate" and a harsher-styled "Erase personal data") — never merged into one "Delete" control — each opening its own ConfirmDialog variant stating the specific, correct consequence.

UX-DR5: Implement Booking Status as a Badge that is never directly settable by the user — Completed never appears as an option in any form, since it's system-derived; only Cancel (user action) and the sweep service produce a status change.

UX-DR6: Implement the full State Patterns matrix: list loading (skeleton rows, `aria-busy`), detail loading, record-not-found, server/network error (5xx, distinct from 400/409), empty list, business-rule error (409, inline), validation error (400, inline), submitting/in-flight (disabled button + spinner), success (toast), anonymized-customer rendering (muted italic + literal "(anonymized)" suffix, everywhere the name appears including historical booking rows), soft-deleted-vehicle rendering (dimmed row + Restore action), Customer Summary's empty-bookings state.

UX-DR7: Implement the full Accessibility Floor: WCAG 2.2 AA contrast (both modes, per DESIGN.md's verified token values), full keyboard operability including real keyboard-activatable sortable column headers with `aria-sort`, `aria-live="polite"` covering every dynamic update (inline errors, live filter result count, live computed Total Price, success toasts), focus-trap/return on both Modal and ConfirmDialog including the nested inline-Customer-create-inside-Booking-create case, accessible labels on any icon-only action, and zero color-only state signaling (active nav = color + bold + underline + `aria-current`; badges pair color with a text label; anonymized text pairs color with italics and a literal suffix).

UX-DR8: Implement the Interaction Primitives: standard Tab/Enter/Escape keyboard model (no shortcuts layer), row "View" click (never the whole row) navigates to detail, every destructive/state-changing action requires its matching ConfirmDialog (no single-click destructive action anywhere), live/debounced filtering rather than an Apply button, pagination via page-number links (never infinite scroll).

UX-DR9: Implement the Voice and Tone microcopy rules exactly — specific, rule-naming copy for every error/empty/confirmation state (per EXPERIENCE.md's Do/Don't table), never a generic message like "Action failed" or "Are you sure?".

UX-DR10: Implement print-specific styles for the Customer Summary screen that strip the Top App Bar and all button chrome, leaving only the identity + booking-list card.

UX-DR11: Wire the centralized `ApiClient`'s error normalization to the backend's ProblemDetails/`type`-URI contract so one `inline-error` component renders both 400 and 409 failures without the frontend needing to know which occurred.

UX-DR12: Pin an exact version of `@tanstack/angular-query-experimental` in `package.json` (no caret range), given the package's own docs warn breaking changes can land in patch releases.

### FR Coverage Map

FR1: Epic 1 (walking-skeleton proof: read-only, filterable list) + Epic 2 (completion: create/edit/soft-delete/restore) - Vehicle management
FR2: Epic 3 - Customer CRUD, hard-delete-when-no-bookings guard
FR3: Epic 4 - Booking create/view/cancel, overlap + date + soft-deleted-vehicle rules, Status tracking
FR4: Epic 1 (established, vehicle list filtering) - continued in Epic 2 (vehicle search refinement), Epic 3 (Customers), Epic 4 (Bookings)
FR5: Epic 2 (first real business-rule feedback: Vehicle rules) - continued in Epic 3 (Customer rules) and Epic 4 (Booking rules)
FR6: Epic 1 - API-key authentication foundation (applies to every endpoint added from Epic 2 onward)
FR7: Epic 1 (established) - Clean Architecture/CQRS backend foundation; every entity added in Epics 2-4 follows the same layering
FR8: Epic 1 (established) - Angular feature-module foundation, core design tokens, ApiClient; component library built out progressively against real consumers in Epics 2-4, not pre-built speculatively
FR9: Epic 1 (established) - TDD workflow and test-project scaffolding with coverage reporting; every story in every epic is built test-first against this foundation
FR10: Epic 6 - README, seed data, commit history
FR11: Epic 3 - Customer soft-delete/restore
FR12: Epic 3 - Customer anonymize
FR13: Epic 6 (optional) - Grafana + Loki observability stretch goal

## Epic List

### Epic 1: Project Foundation & Walking Skeleton

Proves the entire technical stack end-to-end through the thinnest real slice, rather than bundling foundation work into a full feature epic. Stands up: the Clean Architecture solution structure (`BrunoVehicleHire.Domain/Application/Infrastructure/Api` + matching `tests/` tree), Postgres via Docker Compose with an EF Core code-first migration creating only the `Vehicles` table (the table this epic's own story needs — Customers and Bookings tables are created later, by the epic whose story first needs them), API-key authentication (secure-by-default fallback policy), the RFC 9457 ProblemDetails error contract, the Angular app shell (routing, the centralized `ApiClient`, and only the core "Quiet Enterprise" design tokens — colors/typography/spacing, not the full component library), and TDD-first test-project scaffolding (xUnit/NSubstitute/FluentAssertions/Testcontainers.PostgreSql) with Coverlet+ReportGenerator / `ng test --code-coverage` coverage reporting wired up. The one user-facing proof this epic ships: staff can view a paginated, filterable list of vehicles — just enough real feature (DataTable, FilterBar, Skeleton, Top App Bar) to prove every layer actually connects, with no create/edit/delete yet.

**FRs covered:** FR1 (partial: read-only list only); FR4, FR6, FR7, FR8, FR9 (established here, continued in every later epic).

### Story 1.1: Backend Solution Scaffolding & Test Harness

As a developer,
I want the Clean Architecture solution structure and test projects in place with a passing example test in each,
So that all future work has a consistent, verified structure to build on, following TDD from the first line of code.

**Acceptance Criteria:**

**Given** a fresh clone of the repository
**When** `dotnet build` is run
**Then** all four projects (`BrunoVehicleHire.Domain`, `.Application`, `.Infrastructure`, `.Api`) and three test projects (`Domain.Tests`, `Application.Tests`, `Integration.Tests`) compile successfully
**And** `Domain` has zero project references, `Application` references only `Domain`, `Infrastructure`/`Api` reference `Application` + `Domain` (AD-1)

**Given** the dependency-direction fitness test exists (NetArchTest.Rules or ArchUnitNET)
**When** `dotnet test` is run
**Then** the fitness test passes, asserting no type in `Api.Controllers` references `Infrastructure.*`
**And** a trivial example test in each of the three test projects passes, proving the harness itself works end-to-end

### Story 1.2: Local Database & Docker Compose Environment

As a developer,
I want Postgres running locally via Docker Compose with the initial migration applied,
So that the application has a real, disposable database to develop and test against from day one.

**Acceptance Criteria:**

**Given** `docker-compose.yml` defines a Postgres 18 service with the `btree_gist` extension enabled and a volume for Data Protection keys
**When** `docker-compose up` is run
**Then** the Postgres container starts and is reachable on the configured port

**Given** the API is started with `dotnet run`
**When** the application boots
**Then** `Database.Migrate()` applies the initial migration, creating the `Vehicles` table with the schema from `domain-model.md` (Id, RegistrationNumber unique, Make, Model, Year, DailyRate, IsDeleted, CreatedDate)
**And** Data Protection keys are persisted to the mounted volume, not the ephemeral in-container default (AD-12)

### Story 1.3: Vehicle Domain Model (TDD)

As a developer,
I want a rich Vehicle domain entity built test-first,
So that vehicle invariants are enforced by the domain itself, not by whoever happens to call it.

**Acceptance Criteria:**

**Given** a failing test asserting `Vehicle.Create(...)` rejects a blank or malformed `RegistrationNumber`
**When** the test is written before the implementation
**Then** it fails first, then `Vehicle.Create(...)` is implemented to make it pass (red-green-refactor)
**And** `Vehicle` exposes no public settable (`set`/`init`) properties — construction only via `Vehicle.Create(...)` (AD-15)

**Given** a failing test asserting `Vehicle.SoftDelete()` sets `IsDeleted` to true
**When** the test is written first and then implemented
**Then** it passes, and no public method or setter exists anywhere on `Vehicle` capable of reversing `IsDeleted` directly (restore is a separate, explicit method added when Epic 2 needs it)
**And** the vehicle's `Id` is assigned via `Guid.CreateVersion7()` at construction time, never left empty pending a database round-trip (AD-6)

### Story 1.4: API-Key Authentication Foundation

As a developer,
I want every API endpoint protected by API-key authentication by default,
So that no endpoint can ever accidentally ship unauthenticated.

**Acceptance Criteria:**

**Given** a custom `AuthenticationHandler` and a global `FallbackPolicy` requiring the API-key scheme are registered
**When** any request omits the `X-Api-Key` header
**Then** it is rejected with `401 Unauthorized`, regardless of which controller or action is hit — no `[Authorize]` attribute needed per-endpoint (AD-11)

**Given** a request supplies a valid API key in the `X-Api-Key` header
**When** the request is processed
**Then** it proceeds normally
**And** the key never appears in plaintext in any log output

### Story 1.5: Global Error Handling (ProblemDetails)

As a developer,
I want a global exception handler producing consistent ProblemDetails responses,
So that every error — now and in every future epic — has one predictable shape the frontend can parse.

**Acceptance Criteria:**

**Given** `AddProblemDetails()` and a custom `IExceptionHandler` are registered
**When** an unhandled exception occurs anywhere in the request pipeline
**Then** the response is a valid RFC 9457 ProblemDetails object

**Given** the `GetVehiclesQuery`'s pagination validator (via the `ValidationBehavior` pipeline, AD-10) rejects an invalid `page`/`pageSize`
**When** the request is made
**Then** the response is a `400 Bad Request` ProblemDetails object naming the invalid field
**And** a test asserts no ProblemDetails response ever interpolates a raw PII value into its `detail` string

### Story 1.6: Angular App Shell & Design Tokens

As a developer,
I want the Angular app shell, routing, the centralized ApiClient, and the full Quiet Enterprise design token set in place,
So that every feature module built afterward shares one consistent visual and technical foundation.

**Acceptance Criteria:**

**Given** the Angular app is started
**When** it loads
**Then** the Top App Bar renders with Bookings/Vehicles/Customers nav links (Vehicles functional, the other two routed but empty this epic)
**And** Tailwind is configured with the complete `DESIGN.md` token set (colors for both light and dark, typography, rounded scale, spacing scale)

**Given** the centralized `ApiClient` service exists in `core/`
**When** a unit test calls it against a mocked backend
**Then** the test proves the `X-Api-Key` header is attached to every request
**And** a ProblemDetails error response is normalized into one consistent shape the rest of the app consumes, regardless of 400 or 409
**And** a 5xx or network-failure response is normalized into a distinct "server error" shape — never mistaken for a 400/409 business rule by whatever renders it

### Story 1.7: Vehicle List — the Walking Skeleton Proof

As a staff member,
I want to view a paginated, filterable list of vehicles,
So that I can see what's currently in the fleet.

**Acceptance Criteria:**

**Given** vehicles exist in the database
**When** I open the Vehicles page
**Then** I see a paginated `DataTable` of vehicles matching the `PagedResult<T>` contract (`items`, `totalCount`, `page`, `pageSize`) (AD-14)
**And** soft-deleted vehicles never appear in the list (query filter, AD-13)

**Given** I type into the search field
**When** I pause typing
**Then** the list filters live (debounced), with no separate "Apply" button and no full page reload

**Given** the list is loading
**When** the request is in flight
**Then** Skeleton rows render in place of the table, with `aria-busy="true"` on the container — never a spinner
**And** this entire flow is proven end-to-end: Angular → `ApiClient` → API-key auth → `GetVehiclesQuery` handler → `IVehicleRepository` → PostgreSQL → back to a rendered table

**Given** no vehicles exist yet, or none match the current filters
**When** the list renders
**Then** it shows "No vehicles yet — create one" (genuinely empty) or "No vehicles match these filters" + a "Clear all filters" action (filtered-to-empty) — never a bare, silent empty table

### Epic 2: Vehicle Fleet Management

Completes vehicle management on top of Epic 1's proven list screen: staff can create, edit, soft-delete, and restore vehicles. Introduces Modal, Input, and the neutral ConfirmDialog variant against their first real consumer — a genuine create/edit form — rather than designing them speculatively ahead of real usage. This is also where the inline business-rule feedback pattern (FR5) gets its first real exercise, and where FR4's filtering gets refined beyond Epic 1's minimal version.

**FRs covered:** FR1 (completion); FR4, FR5 (first real exercise), FR9 (continuing the Epic 1 foundation).

### Story 2.1: Create a Vehicle

As a staff member,
I want to add a new vehicle to the fleet,
So that it becomes available for booking.

**Acceptance Criteria:**

**Given** I open the "+ New Vehicle" Modal and enter valid details (RegistrationNumber, Make, Model, Year, DailyRate)
**When** I submit
**Then** a new vehicle is created, appears in the Vehicles list, and a success toast confirms it
**And** the `CreateVehicleCommand` handler returns the created `VehicleDto` (AD-2), and its test was written before the implementation (TDD)

**Given** I enter a `RegistrationNumber` already used by another vehicle
**When** I submit
**Then** the request is rejected with `409 Conflict`, and an inline error renders under the RegistrationNumber field ("This registration number is already in use") — not a generic failure message

**Given** I submit with a blank Make or a non-positive DailyRate
**When** I submit
**Then** `400` validation errors render inline under each specific invalid field

**Given** I've submitted the form
**When** the create request is in flight
**Then** the submit button is disabled and shows an inline spinner in place of its label, preventing a double-submit on a slow connection — this pattern is established here and reused by every other mutating form in the app

### Story 2.2: Edit a Vehicle

As a staff member,
I want to edit an existing vehicle's details,
So that I can correct or update information without recreating it.

**Acceptance Criteria:**

**Given** an existing vehicle and its Edit Modal open (reusing the Input/Modal components from Story 2.1)
**When** I change the DailyRate and submit
**Then** the change saves and the Vehicles list reflects it immediately

**Given** I attempt to change RegistrationNumber to one already used by a different vehicle
**When** I submit
**Then** `409 Conflict` with the same inline error pattern as Story 2.1

**Given** I've made changes in the Edit Modal
**When** I press Escape or click the backdrop
**Then** a "Discard changes?" neutral `ConfirmDialog` appears (not an immediate close), and cancelling the discard returns focus to the exact field I was on, not the Modal's first field

### Story 2.3: Soft-Delete a Vehicle

As a staff member,
I want to soft-delete a vehicle that's no longer available,
So that it stops appearing in availability searches without losing its history.

**Acceptance Criteria:**

**Given** an active vehicle's detail view
**When** I click "Deactivate" and confirm the neutral `ConfirmDialog` (which states the vehicle will disappear from availability searches but remains reversible)
**Then** `Vehicle.SoftDelete()` is called, `IsDeleted` is set, the vehicle disappears from the default Vehicles list, and a success toast confirms

**Given** the soft-delete command handler
**When** implemented
**Then** its test was written and failing before the implementation existed (TDD)

### Story 2.4: Restore a Soft-Deleted Vehicle

As a staff member,
I want to restore a previously soft-deleted vehicle, and be able to find it in the first place,
So that I can bring it back into service if it was deactivated by mistake or has become available again.

**Acceptance Criteria:**

**Given** the Vehicles list has a "show inactive" filter toggle
**When** I enable it
**Then** soft-deleted vehicles appear with a dimmed row treatment and a "Restore" action in place of the usual row actions

**Given** a soft-deleted vehicle shown via the toggle
**When** I click "Restore"
**Then** `IsDeleted` is cleared, the vehicle reappears in default listings, and it's immediately bookable again

**Given** a vehicle that is already active (e.g. a second staff member already restored it, stale UI state)
**When** I click "Restore" on it anyway
**Then** the action fails gracefully with an inline "Already active" message rather than silently double-processing

### Story 2.5: View Vehicle Detail

As a staff member,
I want to open a vehicle and see its full record,
So that I can check its details in one place before acting on it.

**Acceptance Criteria:**

**Given** I click "View" on a vehicle row
**When** the detail page loads
**Then** it shows the vehicle's full record (RegistrationNumber, Make, Model, Year, DailyRate, CreatedDate, active/soft-deleted state)
**And** its booking-history section is added in Epic 4 once the `Bookings` table and `GetBookingsQuery` exist (Story 4.5) — this story does not depend on Epic 4 to be complete and useful on its own

**Given** a stale or invalid vehicle id
**When** the detail page is opened
**Then** a "This vehicle no longer exists" message renders with a link back to the Vehicles list — never a blank page or a raw 404

**Given** the detail page is loading
**When** the request is in flight
**Then** a skeleton loading state renders, establishing the detail-loading pattern reused by Customer and Booking detail pages

### Epic 3: Customer Management & PII Protection

Staff can create, edit, and filter customers, and — depending on whether the customer has bookings — either hard-delete (no bookings), reversibly deactivate (soft-delete, bookings exist), or irreversibly erase (anonymize) a customer's personal data. PII (email, phone) is encrypted at rest throughout via the Data Protection-backed EF Core value converter. Introduces the destructive ConfirmDialog variant against its first real use (the Erase action) — Toast was already established in Epic 2. Customer detail's "booking history" need (per the UX contract's IA table) is satisfied by linking to the Customer Summary screen (Epic 5) rather than duplicating the same booking list inline on the detail page — one implementation, reached from two places. Fully functional and testable standalone: this epic's first story creates the `Customers` table (what it actually needs), and a later story creates a minimal `Bookings` table (schema fully known from `domain-model.md` — just the columns needed for the FK relationship and status, no database-level overlap constraint yet) specifically because the "blocked if bookings exist" guard is a real query against Bookings, not a stub. The guard is correctly implemented and unit/integration-tested with directly-inserted test data, without needing Epic 4's booking-creation UI or API to exist. Epic 4 owns Booking's actual feature set and adds the `EXCLUDE USING GIST` database-level constraint to this same table via its own migration.

### Story 3.1: List & Create Customers

As a staff member,
I want to view a filterable list of customers and add new ones,
So that I can manage who places bookings.

**Acceptance Criteria:**

**Given** this story's own migration creates the `Customers` table (schema per `domain-model.md`: Id, FirstName, LastName, Email unique, PhoneNumber, CreatedDate, plus `IsDeleted`/`IsAnonymized`)
**When** the migration runs
**Then** the table exists with Email/PhoneNumber columns backed by the Data Protection value converter (AD-12)

**Given** customers exist in the database
**When** I open the Customers page
**Then** I see a paginated, filterable list reusing the `DataTable`/`FilterBar`/`Skeleton` pattern already proven in Epic 1 — no new list-view infrastructure needed

**Given** I open "+ New Customer" and enter valid details (FirstName, LastName, Email, PhoneNumber)
**When** I submit
**Then** a new customer is created and appears in the list
**And** a test reading the raw database directly confirms Email and PhoneNumber are stored encrypted, not as plaintext (AD-12)

**Given** I enter an Email already used by another customer
**When** I submit
**Then** `409 Conflict` with an inline error under the Email field

**Given** I submit invalid input (e.g. malformed email)
**When** I submit
**Then** `400` validation errors render inline under each specific field

### Story 3.2: Edit a Customer

As a staff member,
I want to edit an existing customer's details,
So that I can keep their contact information current.

**Acceptance Criteria:**

**Given** an existing customer and their Edit Modal open
**When** I change their PhoneNumber and submit
**Then** the change saves, remains encrypted at rest, and the list reflects it

**Given** I attempt to change Email to one already used by a different customer
**When** I submit
**Then** `409 Conflict`, same inline pattern as Story 3.1

**Given** I've made changes in the Edit Modal
**When** I try to close without saving
**Then** the same "Discard changes?" neutral `ConfirmDialog` from Story 2.2 appears

### Story 3.3: Hard-Delete a Customer with No Bookings

As a staff member,
I want to permanently delete a customer who has never made a booking,
So that I can clean up records that were created in error.

**Acceptance Criteria:**

**Given** this story's own migration creates a minimal `Bookings` table (columns per `domain-model.md`: Id, VehicleId FK, CustomerId FK, StartDate, EndDate, TotalPrice, Status, CreatedDate — no database-level overlap constraint yet, that's Epic 4's job)
**When** the migration runs
**Then** the table exists with correct foreign keys to the already-existing `Vehicles` (Epic 1) and `Customers` (Story 3.1) tables

**Given** a customer with zero rows in `Bookings`
**When** I click Delete and confirm
**Then** the customer is permanently removed from the database
**And** the guard query and command handler are built test-first, with test data inserted directly into `Bookings` (no booking-creation UI needed, since Epic 4 hasn't shipped yet)

**Given** a customer who has at least one row in `Bookings`
**When** I attempt to delete
**Then** the request is rejected with `409 Conflict` and a message naming both alternatives: "This customer has bookings — deactivate or erase their data instead"

### Story 3.4: Deactivate & Restore a Customer

As a staff member,
I want to reversibly deactivate a customer who has bookings, and restore them later if needed,
So that I can hide them from active use without losing their data, in case it was a mistake.

**Acceptance Criteria:**

**Given** a customer with bookings, on their detail view
**When** I click "Deactivate" (a distinct button from "Erase personal data") and confirm the neutral `ConfirmDialog`
**Then** `IsDeleted` is set, their PII is untouched, and they're excluded from default listings and from selection in new bookings

**Given** the Customers list's "show inactive" toggle (reusing the pattern from Story 2.4)
**When** I enable it and find a deactivated customer
**Then** I can click "Restore," which clears `IsDeleted` and returns them to normal listings and booking eligibility

### Story 3.5: Erase (Anonymize) a Customer's Personal Data

As a staff member,
I want to permanently erase a customer's personal data when they request it,
So that Bruno Vehicle Hire honors a real erasure request while keeping booking records intact.

**Acceptance Criteria:**

**Given** a customer with bookings, on their detail view
**When** I click "Erase personal data" — visually distinct and harsher-styled than "Deactivate" — and confirm the destructive `ConfirmDialog` (which states "permanently," "cannot be undone," and confirms booking history stays intact)
**Then** `Customer.Anonymize()` scrubs FirstName/LastName/Email/PhoneNumber to placeholders and sets `IsAnonymized`

**Given** an anonymized customer
**When** the codebase is inspected
**Then** no method exists anywhere capable of reversing anonymization — irreversible by construction, not by convention
**And** the anonymized customer is excluded from default listings and from selection in new bookings, via the same query filter as `IsDeleted` (AD-13)

**FRs covered:** FR2, FR11, FR12 (continuing FR4, FR5, FR9 on the Epic 1 foundation).

### Epic 4: Booking Management & Business Rules

Staff can create, view, and cancel bookings linking one vehicle and one customer over a date range, with every cross-entity business rule enforced with a specific, actionable inline error: no overlapping bookings for the same vehicle (application-level `DateRange` check plus a Postgres `EXCLUDE USING GIST` database-level backstop), no booking a soft-deleted vehicle, `EndDate > StartDate`, and only future/Active bookings can be cancelled (never a physical delete). Booking `Status` (Active/Completed/Cancelled) is tracked via the Badge component (introduced here), with `Completed` derived by a background sweep service rather than user-settable. This epic's own migration adds the `EXCLUDE USING GIST` constraint (and the `btree_gist` extension) to the `Bookings` table Epic 3 already created.

**FRs covered:** FR3 (continuing FR4, FR5, FR9).

### Story 4.1: List & Create Bookings

As a staff member,
I want to view a filterable list of bookings and create new ones,
So that I can record rentals and see what's currently booked.

**Acceptance Criteria:**

**Given** bookings exist
**When** I open the Bookings page
**Then** I see a paginated, filterable `DataTable` reusing the existing list pattern, with a `Badge` showing each booking's Status (Active/Completed/Cancelled)

**Given** I open "+ New Booking," pick a vehicle and an existing customer, and set a valid date range
**When** I submit
**Then** a booking is created with `Status: Active`, `TotalPrice` computed from `DailyRate` × duration, and it appears in the list

**Given** I need to book for a first-time customer
**When** I click "+ New Customer" inline from the Booking-create Modal
**Then** the Customer-create Modal (Story 3.1) opens nested on top (one level deep, per the IA rule), and creating the customer succeeds without losing the vehicle/dates already selected in the Booking form

**Given** I change the vehicle or either date
**When** the form recalculates
**Then** the Total Price field updates live inside an `aria-live="polite"` region

**Given** I submit with `EndDate <= StartDate`
**When** I submit
**Then** `400` validation error renders inline under the date fields

**Given** I select a soft-deleted vehicle
**When** I submit
**Then** `409 Conflict` with an inline error ("This vehicle is not available") — the handler checks `Vehicle.IsDeleted` explicitly rather than surfacing a generic not-found (AD-8)

### Story 4.2: Booking Overlap Prevention (Application + Database)

As a staff member,
I want the system to prevent double-booking a vehicle at both the application and database level,
So that I never accidentally commit a vehicle to two customers at once, even under concurrent requests.

**Acceptance Criteria:**

**Given** an existing Active booking for a vehicle from 2 Sep to 4 Sep
**When** I try to create another booking for the same vehicle overlapping that range
**Then** `409 Conflict` with an inline error under the date fields: "This vehicle is already booked 2 Sep – 4 Sep"
**And** the `DateRange.Overlaps` check is a pure, unit-tested domain method with zero infrastructure dependencies, built test-first

**Given** one booking's EndDate equals another's StartDate for the same vehicle (same-day turnover)
**When** the second booking is created
**Then** it succeeds — the half-open interval means touching endpoints are not an overlap (AD-7)

**Given** this story's own migration adds the `EXCLUDE USING GIST` constraint (`vehicle_id WITH =, daterange(...) WITH &&`, requiring `btree_gist`) to the `Bookings` table
**When** an integration test (via `Testcontainers.PostgreSql`) simulates two concurrent requests both attempting to book the same overlapping range
**Then** only one succeeds; the other fails via the database constraint, mapped to the same `409` error the application-level check produces

### Story 4.3: Cancel a Booking

As a staff member,
I want to cancel a future, active booking,
So that I can accommodate a change of plans without destroying the historical record.

**Acceptance Criteria:**

**Given** a future booking with `Status: Active`
**When** I click "Cancel" and confirm the neutral `ConfirmDialog` (which states the booking stays in records as Cancelled and that this action is not reversible)
**Then** `Booking.Cancel()` transitions `Status` to `Cancelled` — the row is never physically deleted (AD-16)

**Given** a booking that is already `Completed` or already `Cancelled`
**When** I attempt to cancel it
**Then** `409 Conflict` with the inline message "Cannot cancel — booking already completed" (or the equivalent for an already-cancelled booking)

**Given** a booking whose `EndDate` is in the past but still shows `Status: Active` (not yet swept)
**When** I attempt to cancel it
**Then** the cancel guard treats it as ineligible (past bookings cannot be cancelled), consistent with the domain rule regardless of whether the sweep has run yet

### Story 4.4: Automatic Booking Completion

As a developer,
I want past-EndDate Active bookings to automatically transition to Completed,
So that Booking Status always reflects reality without manual staff intervention, and without breaking CQRS purity.

**Acceptance Criteria:**

**Given** an Active booking whose EndDate is in the past
**When** the `BookingCompletionSweepService`'s timer fires
**Then** a `CompleteBookingCommand` is dispatched through MediatR for that booking, and `Booking.Complete()` transitions `Status` to `Completed` (AD-17)

**Given** `Booking.Complete()` and the sweep service
**When** built
**Then** both are implemented test-first — `Complete()` as a pure domain-method unit test, the sweep as a test using an injected/fake time source, not a real timer

**Given** the codebase
**When** every Query handler is reviewed
**Then** none of them perform a write — the sweep's Command is the only path that ever sets `Status: Completed`

### Story 4.5: Booking Detail View

As a staff member,
I want to open a booking and see its full details, including vehicle and customer information,
So that I can review or act on a specific booking.

**Acceptance Criteria:**

**Given** a booking's detail is requested
**When** the page loads
**Then** it shows the vehicle, customer, dates, TotalPrice, and Status Badge, with Cancel available if the booking is eligible (Story 4.3's rules apply identically here)

**Given** the booking's customer has since been anonymized
**When** the detail loads
**Then** the customer still resolves and renders as "Customer (anonymized)" — the query explicitly calls `.IgnoreQueryFilters()` on the Customer join so the global exclusion filter (AD-13) doesn't silently drop it

**Given** a stale or invalid booking id
**When** the detail page is opened
**Then** a "This booking no longer exists" message renders with a link back to the Bookings list — never a blank page or a raw 404

**Given** the `Bookings` table now exists with real data
**When** Vehicle Detail (Story 2.5) is revisited
**Then** its previously-stubbed booking-history section now lists real bookings referencing that vehicle, completing the surface Story 2.5 left open

### Epic 5: Customer Summary (Cross-Entity Reporting)

Staff can pull up a clean, read-only, printable summary of a customer and their booking history — its own distinct concern (cross-entity reporting/reference), not bundled into either the Customer or Booking epic's CRUD scope. Necessarily sequenced after Epic 3 and Epic 4, since it has nothing meaningful to show until real customers and real booking history both exist. Satisfies the "customer-facing view" need entirely within the staff tool, with no new authentication.

**FRs covered:** part of FR8 (frontend architecture — demonstrates the read-only view pattern), realized via Customer (Epic 3) and Booking (Epic 4) data.

### Story 5.1: View & Print Customer Summary

As a staff member,
I want to view a clean, printable summary of a customer and their booking history,
So that I can reference or hand it over during a phone call without exposing the full editable admin UI.

**Acceptance Criteria:**

**Given** a customer with booking history
**When** I click "Summary" from their Customer detail or from one of their Booking details
**Then** I see a read-only card: identity + contact (or the anonymized placeholder) + their full booking list — no filters, no edit buttons, no navigation chrome

**Given** a customer with zero bookings
**When** I view their summary
**Then** the identity/contact section still renders, with "No bookings on record for this customer" in place of the list

**Given** the customer has been anonymized
**When** I view their summary
**Then** their name renders as "Customer (anonymized)" in the same muted-italic treatment used everywhere else in the app

**Given** the summary is open
**When** I use the browser's print function
**Then** the Top App Bar and all button chrome are stripped from the printed output, leaving only the identity + booking-list card

**Given** the summary is loading
**When** the request is in flight
**Then** a skeleton loading state renders, reusing the detail-loading pattern established in Epic 1

### Epic 6: Submission Readiness (+ Optional Observability)

An evaluator can clone the repository, follow the README (architecture explanation, how to run, assumptions made), and run the fully seeded system locally (synthetic PII via Bogus) to verify every business rule and architectural decision end-to-end through both the Angular UI and Swagger, with backend and frontend code-coverage reports available to review. As an optional, clearly-scoped stretch goal sequenced last — dropping it affects no other capability's success criteria — structured application logs can be shipped to Loki and viewed in Grafana with at least one dashboard panel.

**FRs covered:** FR10; FR13 (optional).

### Story 6.1: Seed Data & README

As an evaluator,
I want synthetic seed data and a clear README,
So that I can clone, run, and understand the project without guessing.

**Acceptance Criteria:**

**Given** a fresh clone of the repository
**When** I follow the README's "how to run" steps (`docker-compose up`, `dotnet run`, `ng serve`)
**Then** I reach a fully working system with seeded data, no undocumented steps

**Given** the `ISeeder` runs once after migration
**When** seed data is generated
**Then** all PII is synthetic (via Bogus), inserted through EF Core `SaveChanges` — never raw SQL — so the encryption value converter applies to seeded data exactly as it does to real writes (AD-12)

**Given** the README
**When** read
**Then** it includes an architecture explanation, how-to-run steps, and an explicit "Assumptions Made" section (covering the items already logged in SPEC.md's Assumptions, e.g. MediatR Community-edition licensing, latest .NET/Angular versions)

### Story 6.2: Code Coverage Reports

As an evaluator,
I want to see backend and frontend code coverage reports,
So that I can verify the TDD claim is real, not just asserted.

**Acceptance Criteria:**

**Given** `dotnet test --collect:"XPlat Code Coverage"` is run
**When** `ReportGenerator` processes the resulting Cobertura output
**Then** a browsable HTML coverage report is produced locally and its location is referenced from the README

**Given** `ng test --code-coverage` is run
**When** the frontend test suite completes
**Then** an Istanbul-based HTML coverage report is produced and referenced from the README

**Given** both reports
**When** reviewed
**Then** they are local, on-demand artifacts, not wired to any CI pipeline — consistent with the project's non-goals

### Story 6.3 (Optional Stretch): Observability — Logs to Grafana/Loki

As a developer,
I want structured application logs shipped to Loki and viewable in Grafana,
So that I can demonstrate production-level observability thinking as a clearly-scoped stretch goal.

**Acceptance Criteria:**

**Given** Serilog with the `Serilog.Sinks.Grafana.Loki` sink is configured, and Loki + Grafana are added as services in `docker-compose.yml`
**When** the API runs
**Then** structured logs ship to Loki

**Given** Grafana is opened
**When** the provisioned dashboard is viewed
**Then** at least one panel (e.g. request rate, or a count of rejected/business-rule-violation requests) renders real data from the running system

**Given** any log line shipped to Loki
**When** inspected
**Then** no PII appears in plaintext, consistent with the logging constraint enforced everywhere else in the system

**Given** this story is not completed
**When** the rest of the submission is evaluated
**Then** nothing else is affected — this story's absence changes no other story's or epic's success criteria

### Story 6.4: Accessibility Verification Pass

As an evaluator,
I want to verify the Accessibility Floor claims are actually true, not just documented,
So that WCAG 2.2 AA is a checked fact by the time the submission is reviewed.

**Acceptance Criteria:**

**Given** every `DataTable`'s sortable column headers
**When** inspected
**Then** each is a real, keyboard-activatable control (Tab/Enter/Space) with `aria-sort` reflecting current state — not a click-only decoration

**Given** the Top App Bar
**When** a nav item is active
**Then** it's marked by color, bold weight, an underline, and `aria-current="page"` together — confirmed to never rely on color alone

**Given** every `Modal` and `ConfirmDialog` built across Epics 1-5, including the nested inline-Customer-create-inside-Booking-create case (Story 4.1)
**When** each is opened and closed
**Then** focus traps correctly while open and returns to the exact triggering element (or field) on close — verified with one consolidated test sweep across all dialog usages, not re-derived per feature

**Given** the full frontend surface in both light and dark mode
**When** contrast is spot-checked against the token values fixed in `DESIGN.md`
**Then** every text/badge/background combination in active use clears WCAG 2.2 AA — confirming the design-time verification actually held once implemented
