---
title: 'Story 1.7: Vehicle List — the Walking Skeleton Proof'
type: 'feature'
created: '2026-09-03'
status: 'done'
review_loop_iteration: 0
context: ['{project-root}/_bmad-output/implementation-artifacts/epic-1-context.md']
baseline_commit: '8f7fa8144c2b5363eafab11936f7a1c077d7e144'
---

<frozen-after-approval reason="human-owned intent — do not modify unless human renegotiates">

## Intent

**Problem:** Every piece built in Stories 1.1–1.6 (Domain, auth, error handling, Angular shell, `ApiClient`) is proven in isolation, but never end-to-end. Nothing in the codebase yet proves the full vertical slice: Angular → `ApiClient` → API-key auth → a CQRS query handler → a repository → PostgreSQL → back to a rendered table. Epic 1 also left two explicit IOUs: Story 1.5 deferred the `400`/`FluentValidation.ValidationException` branch on `GlobalExceptionHandler` until a real query and validator existed, and Story 1.6 deferred installing TanStack Query until a real query existed to fetch.

**Approach:** Build the first real CQRS slice (`GetVehiclesQuery` → `IVehicleRepository` → PostgreSQL, with a `ValidationBehavior`-enforced pagination validator), close both Epic 1 IOUs against it, and consume it from a real Angular Vehicles page — the first genuine `DataTable`/`Skeleton` shared components, built against their first real consumer per the project's stated design philosophy (`epics.md`'s Epic 2 description: "designed against their first real consumer rather than speculatively ahead of real usage" — the same principle applies here, one epic earlier, to `DataTable`/`Skeleton`).

**Scope note:** `IUnitOfWork`/`UnitOfWork` (AD-5) is deliberately NOT built in this story — `GetVehiclesQuery` is read-only, nothing commits. It arrives in Story 2.1 (`CreateVehicleCommand`), the first command that needs to. Building it now with zero real callers would be untested scaffolding.

## Boundaries & Constraints

**Always:**
- `IVehicleRepository` (Application) exposes one intention-revealing method, `GetPagedAsync(page, pageSize, search, ct)`, returning domain `Vehicle` entities (never DTOs) — mapping to `VehicleDto` happens in the query handler, not the repository (AD-1/AD-5).
- `AppDbContext`'s `Vehicle` configuration gets a global query filter (`HasQueryFilter(v => !v.IsDeleted)`) so soft-deleted vehicles never surface through EF Core queries anywhere in the app, present or future (AD-13) — no new migration needed (query filters are query-time, not schema).
- `GetVehiclesQuery`'s validator (`Page >= 1`, `PageSize` bounded 1–100) runs through the shared `ValidationBehavior<TRequest,TResponse>` MediatR pipeline (AD-10) — never invoked manually in the handler.
- A `FluentValidation.ValidationException` thrown by the pipeline is caught by `GlobalExceptionHandler` (extending, not replacing, Story 1.5's handler) and mapped to `400 Bad Request` using ASP.NET Core's own `ValidationProblemDetails` (its `errors` dictionary names the specific invalid field(s) — the literal AC requirement from Story 1.5/1.7's epics.md text).
- `PagedResult<T>` (`Application/Common/`) is the one shape every future paginated endpoint (Vehicles, Customers, Bookings) returns (AD-14): `{ items, totalCount, page, pageSize }` (camelCase over the wire — ASP.NET Core's default JSON casing already does this, no extra config).
- MediatR's Community license key is read from configuration (`builder.Configuration["MediatR:LicenseKey"]`, sourced from `dotnet user-secrets` in Development — never committed to `appsettings.json`) and passed to `cfg.LicenseKey` at `AddMediatR` registration.
- Frontend: `@tanstack/angular-query-experimental` is added, pinned to an exact patch version (no caret range, per AD-3's explicit caveat about breaking changes in patch releases). Query-key convention: `['vehicles', 'list', { page, pageSize, search }]`. The search input is a Signal, debounced via `toObservable` → `debounceTime(300)` → `distinctUntilChanged` → `toSignal` before it participates in the query key — no manual `setTimeout` debounce.
- `DataTable` and `Skeleton` are built as genuinely reusable `shared/` components (column-def + per-cell render-function API for `DataTable`, matching DESIGN.md's description of `DataTable` as "the core component" reused across Vehicles/Customers/Bookings) — not Vehicle-specific markup. The pagination footer (`Pager`) is built INSIDE `DataTable`, per `DESIGN.md`'s own explicit note that it is "page furniture local to `DataTable`, styled inline with it rather than specified separately," not a standalone component.
- Loading state renders `Skeleton` rows (4–8) with `aria-busy="true"` on the container — never a spinner (`EXPERIENCE.md`).
- Two distinct empty-state messages: genuinely empty ("No vehicles yet — create one") vs. filtered-to-empty ("No vehicles match these filters" + a "Clear all filters" action) — never a bare silent empty table.

**Ask First:** Nothing expected to trigger.

**Never:** No `FilterBar` component yet (`DESIGN.md`'s full FilterBar — per-column filters + "Clear all filters" link as a persistent bar — is Epic 2's "FR4 refined beyond Epic 1's minimal version"; this story needs only a single search input). No `IUnitOfWork` (see Scope note above). No Vehicle create/edit/delete UI or commands (Epic 2). No `FluentValidation.AspNetCore` package (AD-10 explicitly names it deprecated) — use `FluentValidation.DependencyInjectionExtensions` for `AddValidatorsFromAssembly`.

## I/O & Edge-Case Matrix

| Scenario | Input / State | Expected Output / Behavior | Error Handling |
|----------|--------------|---------------------------|----------------|
| Vehicles exist | `GET /api/vehicles?page=1&pageSize=20` | `PagedResult<VehicleDto>` with `items`/`totalCount`/`page`/`pageSize` | `200 OK` |
| Soft-deleted vehicle exists | Same request, one vehicle has `IsDeleted=true` | That vehicle never appears in `items`, doesn't count toward `totalCount` | N/A |
| Invalid `page`/`pageSize` | `page=0` or `pageSize=500` | `400 Bad Request`, `ValidationProblemDetails.errors` names the invalid field | `400 Bad Request` |
| Search matches | `?search=Toyota` | Only vehicles whose Make/Model/RegistrationNumber match (case-insensitive) returned | N/A |
| No vehicles at all | Empty `Vehicles` table | Frontend shows "No vehicles yet — create one" | N/A |
| Vehicles exist, none match filter | `?search=zzz-nomatch` | Frontend shows "No vehicles match these filters" + "Clear all filters" | N/A |
| Request in flight | Any list fetch | Frontend shows 4–8 `Skeleton` rows, `aria-busy="true"` on the container | N/A |
| User types in search | Rapid keystrokes | List re-fetches once, ~300ms after the user pauses — not once per keystroke | N/A |

## Code Map

**Backend:**
- `src/BrunoVehicleHire.Application/Common/PagedResult.cs` -- new -- `public record PagedResult<T>(IReadOnlyList<T> Items, int TotalCount, int Page, int PageSize);`
- `src/BrunoVehicleHire.Application/Common/ValidationBehavior.cs` -- new -- `IPipelineBehavior<TRequest,TResponse>` running every registered `IValidator<TRequest>`, throwing `FluentValidation.ValidationException` on failure
- `src/BrunoVehicleHire.Application/Vehicles/Dtos/VehicleDto.cs` -- new -- `record VehicleDto(Guid Id, string RegistrationNumber, string Make, string Model, int Year, decimal DailyRate, DateTime CreatedDate);`
- `src/BrunoVehicleHire.Application/Vehicles/IVehicleRepository.cs` -- new -- `Task<(IReadOnlyList<Vehicle> Items, int TotalCount)> GetPagedAsync(int page, int pageSize, string? search, CancellationToken ct);`
- `src/BrunoVehicleHire.Application/Vehicles/Queries/GetVehiclesQuery.cs` -- new -- `record GetVehiclesQuery(int Page, int PageSize, string? Search) : IRequest<PagedResult<VehicleDto>>;`
- `src/BrunoVehicleHire.Application/Vehicles/Queries/GetVehiclesQueryValidator.cs` -- new -- `Page >= 1`, `PageSize` in `[1, 100]`
- `src/BrunoVehicleHire.Application/Vehicles/Queries/GetVehiclesQueryHandler.cs` -- new -- calls the repository, maps to `VehicleDto`, wraps in `PagedResult`
- `src/BrunoVehicleHire.Infrastructure/Repositories/VehicleRepository.cs` -- new -- implements `IVehicleRepository` against `AppDbContext`, search via `EF.Functions.ILike` across Make/Model/RegistrationNumber
- `src/BrunoVehicleHire.Infrastructure/Persistence/AppDbContext.cs` -- modify -- add `entity.HasQueryFilter(v => !v.IsDeleted);` to the existing `Vehicle` configuration
- `src/BrunoVehicleHire.Api/ExceptionHandling/GlobalExceptionHandler.cs` -- modify -- add a third `switch` arm: `FluentValidation.ValidationException` → `400 Bad Request` `ValidationProblemDetails` (errors keyed by property name)
- `src/BrunoVehicleHire.Api/Controllers/VehiclesController.cs` -- new -- `[ApiController] [Route("api/[controller]")]`, `GET` action taking `page`/`pageSize`/`search` query params, sends `GetVehiclesQuery` via `IMediator`
- `src/BrunoVehicleHire.Api/Program.cs` -- modify -- `AddMediatR` (license key from config, `RegisterServicesFromAssembly` + `AddOpenBehavior(typeof(ValidationBehavior<,>))`), `AddValidatorsFromAssembly`, `AddScoped<IVehicleRepository, VehicleRepository>()`
- `src/BrunoVehicleHire.Api/BrunoVehicleHire.Api.csproj` -- modify -- add `MediatR` 14.2.0 (a `UserSecretsId` was already added and the license key already stored via `dotnet user-secrets set "MediatR:LicenseKey" ...` ahead of implementation, at the user's request — `builder.Configuration["MediatR:LicenseKey"]` picks it up automatically in Development, no extra wiring needed)
- `src/BrunoVehicleHire.Application/BrunoVehicleHire.Application.csproj` -- modify -- add `MediatR` 14.2.0, `FluentValidation` 12.1.1, `FluentValidation.DependencyInjectionExtensions` 12.1.1
- `src/BrunoVehicleHire.Infrastructure/BrunoVehicleHire.Infrastructure.csproj` -- modify -- no new package expected (EF Core already present)
- `tests/BrunoVehicleHire.Application.Tests/Vehicles/GetVehiclesQueryValidatorTests.cs` -- new -- boundary cases for page/pageSize
- `tests/BrunoVehicleHire.Application.Tests/Vehicles/GetVehiclesQueryHandlerTests.cs` -- new -- NSubstitute-mocked `IVehicleRepository`, proves correct mapping/pass-through
- `tests/BrunoVehicleHire.Api.Tests/ExceptionHandling/GlobalExceptionHandlerTests.cs` -- modify -- add the new `FluentValidation.ValidationException` → 400 branch's unit tests
- `tests/BrunoVehicleHire.Integration.Tests/VehiclesEndpointTests.cs` -- new -- `WebApplicationFactory` + Testcontainers Postgres, full I/O matrix against the real `GET /api/vehicles` endpoint (seeded rows including a soft-deleted one)

**Frontend:**
- `frontend/package.json` -- modify -- add `@tanstack/angular-query-experimental` (exact version pin)
- `frontend/src/app/app.config.ts` -- modify -- register TanStack Query's Angular provider
- `frontend/src/app/core/models/paged-result.ts` -- new -- `PagedResult<T>` DTO shape
- `frontend/src/app/shared/data-table/data-table.ts` (+ `.html`) -- new -- generic `columns`/`rows`/`loading`/`page`/`pageSize`/`totalCount`/`(pageChange)` component, pager built in
- `frontend/src/app/shared/skeleton/skeleton.ts` (+ `.html`) -- new -- reusable skeleton-row placeholder
- `frontend/src/app/features/vehicles/models/vehicle.ts` -- new -- `Vehicle` view model + a `toVehicle(dto: VehicleDto): Vehicle` mapper (the frontend model-separation convention's first real exercise)
- `frontend/src/app/features/vehicles/vehicles.service.ts` -- new -- `injectQuery` wrapping `ApiClient.get<PagedResult<VehicleDto>>('vehicles', params)`
- `frontend/src/app/features/vehicles/vehicles-page.ts` (+ `.html`) -- modify (replacing the Story 1.6 placeholder) -- search input (debounced Signal), `DataTable` wiring, loading/empty-state handling
- `frontend/src/app/features/vehicles/vehicles-page.spec.ts` -- new -- covers loading/empty/filtered-empty/populated states
- `frontend/src/app/shared/data-table/data-table.spec.ts`, `frontend/src/app/shared/skeleton/skeleton.spec.ts` -- new

## Tasks & Acceptance

**Execution — backend (TDD throughout):**
- [x] `PagedResult<T>` + write failing tests for `GetVehiclesQueryValidator` first (page/pageSize boundaries), then implement the validator
- [x] Write failing tests for `GetVehiclesQueryHandler` first (NSubstitute `IVehicleRepository`), then implement `VehicleDto`, `IVehicleRepository`, `GetVehiclesQuery`, the handler
- [x] `ValidationBehavior<TRequest,TResponse>` -- unit test proving it short-circuits to the validator's failures before the handler runs
- [x] `AppDbContext` query filter + `VehicleRepository` (Infrastructure) -- implement against real Postgres, proven by the new `VehiclesEndpointTests` (Testcontainers)
- [x] `GlobalExceptionHandler`'s new `ValidationException` → 400 branch -- write failing unit test first, then implement
- [x] `VehiclesController` + full `Program.cs` wiring (MediatR incl. license key from config, FluentValidation registration, repository DI)
- [x] `VehiclesEndpointTests` -- write failing integration tests first (every I/O-matrix backend row, seeded via `AppDbContext` directly including a soft-deleted row), then confirm green
- [x] Full `dotnet test`; re-run `ArchitectureFitnessTests`; force `dotnet restore` and check for NU1903 warnings -- confirmed: 72/72 passing, 0 vulnerabilities, fitness tests unchanged

**Execution — frontend (TDD for `DataTable`/`Skeleton`/the debounce logic):**
- [x] Add `@tanstack/angular-query-experimental`, register its provider
- [x] `DataTable`/`Skeleton` shared components -- write failing component tests first, then implement
- [x] Vehicles feature: `Vehicle` model + mapper, `vehicles.service.ts` (`injectQuery`), the debounced search Signal
- [x] `vehicles-page` -- wire `DataTable`/`Skeleton`/empty states/search; write failing tests first for each of: loading state, genuinely-empty state, filtered-to-empty state, populated state, debounce behavior (fake timers)
- [x] `ng build` + `ng test --coverage --watch=false` clean; `npm audit` clean -- confirmed: 39/39 passing, 96.27% stmt coverage, 0 vulnerabilities

**Acceptance Criteria (from epics.md, verbatim intent):**
- Given vehicles exist in the database, when I open the Vehicles page, then I see a paginated `DataTable` matching the `PagedResult<T>` contract, and soft-deleted vehicles never appear
- Given I type into the search field, when I pause typing, then the list filters live (debounced), no "Apply" button, no full page reload
- Given the list is loading, when the request is in flight, then Skeleton rows render with `aria-busy="true"`, never a spinner, and the entire flow is proven end-to-end (Angular → `ApiClient` → API-key auth → `GetVehiclesQuery` → `IVehicleRepository` → PostgreSQL → rendered table)
- Given no vehicles exist yet, or none match the current filters, when the list renders, then the correct one of the two distinct empty-state messages shows — never a bare silent empty table

## Spec Change Log

**Loopback 1 (bad_spec, minor):** During frontend implementation, cross-checking the real backend response against `error-normalization.interceptor.ts`'s `isProblemDetails` guard (Story 1.6) surfaced that `GlobalExceptionHandler`'s new `ValidationException` → 400 branch never populated `ValidationProblemDetails.Detail`, unlike the existing 409/500 branches. Since the interceptor's guard requires `type`/`title`/`status`/`detail` all present to classify a response as a `BusinessRuleError`, every 400 validation response was silently misclassified as a `ServerError` on the frontend — the opposite of AD-8's intent. Fixed by adding a fixed summary `Detail` string to the branch (`"One or more fields failed validation. See the errors property for details."`) and a regression test (`TryHandleAsync_ValidationException_PopulatesDetail_...`) locking it in. Did not block on this for Story 1.7 itself (nothing in this story's own UI renders a 400), but it would have silently broken Epic 2's inline field-level error rendering had it shipped unfixed. Also bumped `frontend/angular.json`'s initial-bundle warning budget from 500kB to 550kB to accommodate TanStack Query's real, legitimate added weight (510kB actual) without a permanent build warning.

## Design Notes

**Validation-error `type` URI:** unlike the per-entity/per-rule `urn:bruno:{entity}:{rule}` template for `409`s (AD-8), every `400` validation failure uses one fixed type, `urn:bruno:validation:invalid-request` — the field-level detail already lives in `ValidationProblemDetails.errors`, so a per-field URI would be redundant. This is a judgment call: AD-8's "single const list, never hand-typed" concern is about business-rule URIs varying meaningfully per entity/rule; a validation failure's interesting detail is which field(s), already structured data, not prose needing its own URI.

**Repository read model:** `IVehicleRepository.GetPagedAsync` returns domain `Vehicle` entities, not DTOs — keeps the repository ignorant of Application-layer DTO shapes (a repository returning `VehicleDto` would invert the dependency AD-1 establishes: Infrastructure depends on Application, not the other way around via leaked return types).

**Search matching:** `EF.Functions.ILike` (Postgres case-insensitive `ILIKE`) across `Make`, `Model`, `RegistrationNumber` — chosen over `.ToLower().Contains()` because it translates to a single indexable SQL operator rather than three, and is the idiomatic Npgsql pattern for this.

**`DataTable`'s cell API:** column definitions carry a `cell: (row: T) => string` render function rather than Angular structural-directive cell templates — sufficient for Vehicle's all-primitive fields (including currency-formatted `DailyRate`) and simpler to test; revisit if a future entity (e.g. Booking's status badge) needs richer per-cell markup than a string can express.

**Pagination-reset timing:** resetting `page` to 1 when the debounced search changes uses `linkedSignal` rather than `effect()` + `page.set(1)` — an effect-based reset fires a tick after the query's own reactive read, letting one wasted request go out for `{page: <stale>, search: <new>}` before a corrected one lands. `linkedSignal` resolves synchronously in the same signal-graph evaluation, so only the correct request is ever issued.

## Verification — actual results

- Backend: `dotnet build` 0 warnings/0 errors; `dotnet test` 73/73 passing (up from 52 before this story); `dotnet restore --force` showed no NU1903/vulnerability warnings; `ArchitectureFitnessTests` unchanged/passing.
- Backend manual proof: ran the real API against the real docker-compose Postgres, curled `GET /api/vehicles` (200, real `PagedResult<VehicleDto>`), `?page=0` (400, `ValidationProblemDetails` with `detail` populated after the Loopback 1 fix), no key (401).
- Frontend: `ng build` 0 errors, 0 warnings (after the budget bump); `ng test --coverage --watch=false` 39/39 passing, 96.27% statement / 95.32% branch / 100% function / 97.53% line coverage; `npm audit` 0 vulnerabilities.
- Full live end-to-end: backend + `ng serve` both running against the real docker-compose Postgres; curled `http://localhost:4200/api/vehicles` through the dev proxy with the dev API key and got real Postgres-backed data back, proving Angular → `ApiClient` → proxy → API-key auth → `GetVehiclesQuery` → `IVehicleRepository` → PostgreSQL round-trips correctly end-to-end.

</frozen-after-approval>

## Suggested Review Order

**The vertical slice itself (the point of this story)**

- `GetVehiclesQueryHandler` + `VehicleRepository` — the query filter, the search, the mapping to DTO.
  [`GetVehiclesQueryHandler.cs`](../../src/BrunoVehicleHire.Application/Vehicles/Queries/GetVehiclesQueryHandler.cs) · [`VehicleRepository.cs`](../../src/BrunoVehicleHire.Infrastructure/Repositories/VehicleRepository.cs)

- The `AppDbContext` query filter — one line, the entire AD-13 guarantee for this story.
  [`AppDbContext.cs:48`](../../src/BrunoVehicleHire.Infrastructure/Persistence/AppDbContext.cs#L48)

- `GlobalExceptionHandler`'s new 400 branch, including the `Detail` fix (Loopback 1).
  [`GlobalExceptionHandler.cs:45`](../../src/BrunoVehicleHire.Api/ExceptionHandling/GlobalExceptionHandler.cs#L45)

- `vehicles-page.ts` — the debounced search, the `linkedSignal` page-reset, the two empty states.
  [`vehicles-page.ts`](../../frontend/src/app/features/vehicles/vehicles-page.ts)

**Proof (test-first, not test-after)**

- `VehiclesEndpointTests` — the soft-delete-exclusion proof (verifies `IsDeleted=true` was actually persisted before asserting the filter works), the search case-insensitivity proof.
  [`VehiclesEndpointTests.cs`](../../tests/BrunoVehicleHire.Integration.Tests/VehiclesEndpointTests.cs)

- The `Detail`-populated regression test locking in Loopback 1's fix.
  [`GlobalExceptionHandlerTests.cs`](../../tests/BrunoVehicleHire.Api.Tests/GlobalExceptionHandlerTests.cs)

**Peripherals**

- `DataTable`/`Skeleton` — genuinely reusable shared components, built against their first real consumer.
  [`data-table.ts`](../../frontend/src/app/shared/data-table/data-table.ts) · [`skeleton.ts`](../../frontend/src/app/shared/skeleton/skeleton.ts)
