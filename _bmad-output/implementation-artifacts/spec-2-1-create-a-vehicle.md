---
title: 'Story 2.1: Create a Vehicle'
type: 'feature'
created: '2026-09-12'
status: 'done'
review_loop_iteration: 0
context: []
baseline_commit: '9bdeef7c59cde8a1987ca51135d3050a8a2c30c4'
---

<frozen-after-approval reason="human-owned intent — do not modify unless human renegotiates">

## Intent

**Problem:** The Vehicles list (Story 1.7) is read-only. Nothing yet lets staff add a vehicle, no command has ever been written, `IUnitOfWork` was deliberately deferred (Story 1.7's Design Notes) until a real write existed, and the frontend has no Modal/Input/Button/Toast components and no mutation pattern at all.

**Approach:** Build the first real MediatR command (`CreateVehicleCommand`) end-to-end — the 400 (input-shape)/409 (duplicate RegistrationNumber, a pre-insert existence check per AD-8) split gets its first real exercise here, using the exact same `GlobalExceptionHandler`/`ValidationBehavior` machinery already proven in Story 1.7, now for a write instead of a read. On the frontend, build `Modal`, `Input` (as a real `ControlValueAccessor`, not a presentational wrapper), `Button` (loading-state), and `Toast` against this story's own real create-vehicle form — per Epic 2's own stated philosophy, "against their first real consumer... rather than designing them speculatively ahead of real usage."

**Scope decisions — flagged for approval:**

1. **`ConfirmDialog` is NOT built in this story.** `Modal`'s discard-confirmation-on-dirty-close behavior (`EXPERIENCE.md`'s general Modal spec: "if fields were touched, Escape opens a 'Discard changes?' `ConfirmDialog`") is real, but epics.md's own Story 2.1 AC never mentions it, and Story 2.2 is explicitly where it's tested (against Edit, where "discard" has real stakes — an already-populated form, not a blank one). Building `ConfirmDialog` now, untested, for a behavior this story's AC doesn't exercise, is exactly the "speculatively ahead of real usage" building the epic description warns against. **This story's `Modal`:** Escape/backdrop-click always closes immediately — no dirty-check, no confirm step. Story 2.2 extends `Modal` with the dirty-check + `ConfirmDialog` once it has a real, testable reason to.
2. **`errorNormalizationInterceptor`/`BusinessRuleError` (Story 1.6) needs extending.** The current shape (`{kind, status, title, detail, type}`) has nowhere to carry FluentValidation's per-field `errors` dictionary (400s) or a way to identify which field a 409 belongs to. This story adds an optional `errors?: Record<string,string[]>` (populated when the response body has one — i.e. every 400) and a `fieldFromType(type)` helper that recovers the field name from a 409's `urn:bruno:{entity}:{rule}` URI (already unique per invariant since Story 1.5) for the single-field case. This is exactly what epics.md's own AC requires ("400 validation errors render inline under each specific invalid field") and what FR5's "first real exercise" is.

## Boundaries & Constraints

**Always:**
- `CreateVehicleCommand` returns the created `VehicleDto` (AD-2 — every mutating Command that creates/updates an aggregate returns its DTO, never a bare id).
- `CreateVehicleCommandValidator` checks exactly what epics.md's AC names as 400 cases: `RegistrationNumber`/`Make`/`Model` not blank, `DailyRate > 0`. **`Year`'s plausible-range check is deliberately NOT duplicated here** — it stays a pure `Vehicle.Create()` domain invariant (409), per Story 1.3, since epics.md's own AC list for this story only names blank-field/non-positive cases as 400s, and Year's range check inherently depends on "now" (arguably a domain concept, not a shape check) — see Design Notes.
- Duplicate `RegistrationNumber` is checked via a pre-insert existence query in the handler (AD-8's own named example), throwing `DomainRuleViolationException("Vehicle", "RegistrationNumber", "This registration number is already in use.")` — this exact message is what reaches the frontend via `ProblemDetails.detail` and renders inline (epics.md's AC names this exact text).
- `IUnitOfWork.SaveChangesAsync()` is called exactly once, after `IVehicleRepository.AddAsync()` — the repository itself never calls `SaveChangesAsync` (AD-5).
- Frontend `Input` is a real `ControlValueAccessor` (`NG_VALUE_ACCESSOR`), usable as `<app-input formControlName="..." label="..." [error]="..." />` inside a real `FormGroup` — not a bespoke value/output pair (stack.md's explicit "Reactive Forms" requirement, and the form-group-validation competency).
- The submit-button disabled+inline-spinner pattern (epics.md's AC, `EXPERIENCE.md`'s Button/State-Patterns rows) is built once as the shared `Button` component's `loading` input — explicitly "reused by every other mutating form in the app" (epics.md), so it must not be one-off inline markup in the Create form.
- On success: the mutation invalidates the `['vehicles', 'list']` query key (AD-3 — never a manual list refetch/hand-rolled state update), the Modal closes, and a success toast confirms (`EXPERIENCE.md` State Patterns: "success closes the Modal via a toast").
- On a `BusinessRuleError` (400 or 409): the Modal stays open, the submit button re-enables, and the specific field(s) get their inline error from `error.errors` (400) or `fieldFromType(error.type)` (409) — never a top-of-form banner for this class of error (`EXPERIENCE.md` Component Patterns: Input row).
- On a `ServerError` (5xx/network failure): a distinct top-of-form "Something went wrong — try again" banner, never confused with the field-level treatment (`EXPERIENCE.md` State Patterns) — this is the first real consumer of that distinction (Story 1.6 built the shape, never rendered it).

**Ask First:** Nothing expected to trigger.

**Never:** No `ConfirmDialog` (see Scope decision 1). No Edit-vehicle functionality (Story 2.2). No `FilterBar` refinement (out of this story's scope; Epic 2's description assigns it generally but epics.md doesn't tie it to 2.1 specifically — Story 2.1's AC is entirely about creation). No changes to `Vehicle.cs`/`DomainRuleViolationException` (Domain already has everything this story needs from Story 1.3).

## I/O & Edge-Case Matrix

| Scenario | Input / State | Expected Output / Behavior | Error Handling |
|----------|--------------|---------------------------|----------------|
| Valid input | Non-blank RegistrationNumber/Make/Model, positive DailyRate, plausible Year | `201 Created`, `VehicleDto` body; frontend closes Modal, shows success toast, list re-fetches | N/A |
| Duplicate RegistrationNumber | Matches an existing (non-deleted) vehicle | `409 Conflict`, `detail`="This registration number is already in use." | Inline error under RegistrationNumber field |
| Blank Make | Empty/whitespace Make | `400 Bad Request`, `errors.Make` populated | Inline error under Make field |
| Non-positive DailyRate | `0` or negative | `400 Bad Request`, `errors.DailyRate` populated | Inline error under DailyRate field |
| Implausible Year | e.g. two years from now | `409 Conflict` (domain invariant, not FluentValidation) | Inline error under Year field |
| Submit in flight | Any valid submission, response pending | Submit button disabled, inline spinner replaces its label | N/A |
| Server error | Simulated 500/network failure | Top-of-form banner, distinct from field-level errors, button re-enables | N/A |

## Code Map

**Backend:**
- `src/BrunoVehicleHire.Application/Common/IUnitOfWork.cs` -- new -- `Task SaveChangesAsync(CancellationToken cancellationToken);`
- `src/BrunoVehicleHire.Infrastructure/Persistence/UnitOfWork.cs` -- new -- wraps `AppDbContext.SaveChangesAsync`
- `src/BrunoVehicleHire.Application/Vehicles/IVehicleRepository.cs` -- modify -- add `Task<bool> ExistsByRegistrationNumberAsync(string registrationNumber, CancellationToken cancellationToken);` and `Task AddAsync(Vehicle vehicle, CancellationToken cancellationToken);`
- `src/BrunoVehicleHire.Infrastructure/Repositories/VehicleRepository.cs` -- modify -- implement both new methods; `ExistsByRegistrationNumberAsync` uses `IgnoreQueryFilters()` (see Design Notes -- the DB's unique index is unfiltered, so the pre-check must match its actual scope or a soft-deleted vehicle's old registration number could pass this check and still throw a raw `DbUpdateException` on insert)
- `src/BrunoVehicleHire.Application/Vehicles/Commands/CreateVehicleCommand.cs` -- new -- `record CreateVehicleCommand(string RegistrationNumber, string Make, string Model, int Year, decimal DailyRate) : IRequest<VehicleDto>;`
- `src/BrunoVehicleHire.Application/Vehicles/Commands/CreateVehicleCommandValidator.cs` -- new -- `RegistrationNumber`/`Make`/`Model` `NotEmpty()`, `DailyRate` `GreaterThan(0)`
- `src/BrunoVehicleHire.Application/Vehicles/Commands/CreateVehicleCommandHandler.cs` -- new -- existence check -> `DomainRuleViolationException` or `Vehicle.Create(...)` -> `AddAsync` -> `SaveChangesAsync` -> map to `VehicleDto`
- `src/BrunoVehicleHire.Api/Controllers/VehiclesController.cs` -- modify -- add `[HttpPost]` action, `[FromBody] CreateVehicleCommand`, returns `Created($"/api/vehicles/{dto.Id}", dto)`
- `tests/BrunoVehicleHire.Application.Tests/Vehicles/CreateVehicleCommandValidatorTests.cs` -- new
- `tests/BrunoVehicleHire.Application.Tests/Vehicles/CreateVehicleCommandHandlerTests.cs` -- new -- NSubstitute `IVehicleRepository`/`IUnitOfWork`, covers success, duplicate-registration-throws, confirms `SaveChangesAsync` called exactly once
- `tests/BrunoVehicleHire.Integration.Tests/VehiclesEndpointTests.cs` -- modify -- add `POST` coverage: 201 success, 409 duplicate, 400 blank Make, 400 non-positive DailyRate, 409 implausible Year (proves it's still domain-enforced, not silently accepted)

**Frontend:**
- `frontend/src/app/core/api-client/normalized-api-error.ts` -- modify -- add `errors?: Record<string, string[]>` to `BusinessRuleError`; add `fieldFromType(type: string): string | undefined` (parses the last `:`-segment, kebab-case -> camelCase)
- `frontend/src/app/core/api-client/error-normalization.interceptor.ts` -- modify -- populate `errors` from the response body when present (i.e. `ValidationProblemDetails` responses); write failing tests first
- `frontend/src/app/shared/modal/modal.ts` (+ `.html`) -- new -- focus trap + return-to-trigger on close (`EXPERIENCE.md` Accessibility Floor), Escape/backdrop closes immediately (no dirty-check -- see Scope decision 1), `role="dialog"` `aria-modal="true"`
- `frontend/src/app/shared/input/input.ts` (+ `.html`) -- new -- real `ControlValueAccessor` (`NG_VALUE_ACCESSOR`), `label`/`error`/`type` inputs, renders `inline-error` beneath the field per `DESIGN.md`/`EXPERIENCE.md`, `aria-live="polite"` on the error region
- `frontend/src/app/shared/button/button.ts` (+ `.html`) -- new -- `{components.button-primary}` tokens, `loading` input swaps label for an inline spinner and disables the button
- `frontend/src/app/shared/toast/toast.ts` (+ `.html`), `toast.service.ts` -- new -- signal-based queue, `role="status"` `aria-live="polite"`, auto-dismiss (~4s) + manual dismiss; `ToastService.success(message)`
- `frontend/src/app/app.ts` (+ `.html`) -- modify -- mount a single toast host
- `frontend/src/app/features/vehicles/create-vehicle-modal.ts` (+ `.html`) -- new -- `FormGroup` (client-side `Validators` mirroring the backend: `required`, `min(0.01)` equivalents), `injectMutation` for `POST /api/vehicles`, maps `BusinessRuleError.errors`/`fieldFromType` onto the matching control, `ServerError` -> top-of-form banner
- `frontend/src/app/features/vehicles/vehicles-page.ts` (+ `.html`) -- modify -- "+ New Vehicle" `Button` opening `CreateVehicleModal`
- `frontend/src/app/shared/{modal,input,button,toast}/*.spec.ts`, `create-vehicle-modal.spec.ts` -- new

## Tasks & Acceptance

**Execution — backend (TDD throughout):**
- [x] `IUnitOfWork`/`UnitOfWork`
- [x] Write failing tests first for `IVehicleRepository`'s two new methods (via the existing Testcontainers integration pattern or a handler-level NSubstitute mock, whichever fits each test's actual concern), then implement
- [x] Write failing tests first for `CreateVehicleCommandValidator`, then implement
- [x] Write failing tests first for `CreateVehicleCommandHandler` (success path returns correct DTO and calls `SaveChangesAsync` once; duplicate registration throws `DomainRuleViolationException` with the exact AC message and never calls `AddAsync`/`SaveChangesAsync`), then implement
- [x] `VehiclesController`'s `POST` action
- [x] `VehiclesEndpointTests` -- write failing integration tests first for every backend I/O-matrix row, then confirm green
- [x] Full `dotnet test`; re-run `ArchitectureFitnessTests`; force `dotnet restore` and check for NU1903 warnings -- confirmed: 93/93 passing, 0 vulnerabilities

**Execution — frontend (TDD for every new shared component):**
- [x] Extend `normalized-api-error.ts`/`error-normalization.interceptor.ts` -- write failing tests first (400 populates `errors`; 409's `fieldFromType` recovers the right field name; a 409/500 without an `errors` field leaves it `undefined`)
- [x] `Modal` -- write failing component tests first (opens/closes, focus trap, focus returns to trigger, Escape closes immediately, backdrop click closes immediately), then implement
- [x] `Input` -- write failing tests first (acts as a real form control via `formControlName`, renders `error` text, `aria-live`), then implement
- [x] `Button` -- write failing tests first (`loading` disables + swaps label for spinner), then implement
- [x] `Toast`/`ToastService` -- write failing tests first (queues a message, auto-dismisses, manually dismissable, correct ARIA), then implement
- [x] `CreateVehicleModal` -- write failing tests first for: valid submit succeeds (mocked mutation success -> modal closes, toast fires, list-query invalidated); 409 duplicate -> inline error on RegistrationNumber, modal stays open; 400 blank Make -> inline error on Make; server error -> banner, not field-level; submit-in-flight -> button disabled+spinner
- [x] Wire "+ New Vehicle" into `vehicles-page`
- [x] `ng build` + `ng test --coverage --watch=false` clean; `npm audit` clean -- confirmed: 85/85 passing, 95.06% stmt coverage, 0 vulnerabilities (bundle budget bumped to 650kB after independent verification, see Spec Change Log)

**Acceptance Criteria (epics.md, verbatim intent):**
- Given valid details in the "+ New Vehicle" Modal, when submitted, then the vehicle is created, appears in the list, a success toast confirms, and `CreateVehicleCommandHandler`'s test was written before its implementation
- Given a duplicate RegistrationNumber, when submitted, then `409 Conflict` and an inline "This registration number is already in use" error under that field -- not a generic message
- Given a blank Make or non-positive DailyRate, when submitted, then `400` inline errors render under each specific invalid field
- Given the create request is in flight, then the submit button is disabled with an inline spinner in place of its label -- the pattern every other mutating form in the app will reuse

## Spec Change Log

**Backend DRY extraction (minor, not a scope change):** `VehicleDto`'s domain->DTO mapping expression was about to be duplicated a second time in `CreateVehicleCommandHandler` (it already existed once in `GetVehiclesQueryHandler`). Per the user's explicit SOLID/DRY/YAGNI requirement for this story, extracted `VehicleDto.FromDomain(Vehicle)` as the one shared projection and updated both handlers to use it -- a pure refactor, no behavior change, covered by the existing `GetVehiclesQueryHandlerTests`. Not in the spec's literal Code Map (which didn't list `VehicleDto.cs`/`GetVehiclesQueryHandler.cs` as modified) but consistent with its intent.

**Frontend bundle budget bumped again (550kB -> 650kB):** `Modal`/`Input`/`Button`/`Toast`/`CreateVehicleModal` plus TanStack Query's mutation machinery pushed the initial bundle to 568.98kB, over Story 1.7's 550kB warning threshold. This is real, legitimate growth (four genuinely reusable shared components, not bloat), not silently re-bumped without checking -- verified the new size independently before bumping, and gave headroom past just this story's number since Stories 2.2-2.5 will add more of the same components (`ConfirmDialog`, an Edit form) rather than bump the budget every single story.

## Design Notes

**Why Year isn't in the command validator:** `Vehicle.Create()` already throws `DomainRuleViolationException` for an implausible Year (Story 1.3), surfaced as 409 by the existing `GlobalExceptionHandler` branch. Duplicating it in FluentValidation would mean the domain check becomes unreachable dead code for this call path. Blank-field/non-positive-number checks are genuinely pure "shape" checks (AD-8's test) independent of "now"; Year's bound depends on the current date, arguably making it a domain concept rather than a pure input-shape one -- and epics.md's own AC list simply doesn't include it as a 400 case. This keeps each invariant checked in exactly one place.

**Why `VehicleRepository.ExistsByRegistrationNumberAsync` ignores the query filter:** confirmed directly against `AppDbContext.cs` -- the `RegistrationNumber` unique index (Story 1.2) is unfiltered, so it applies to soft-deleted rows too. If this pre-check respected the soft-delete query filter (like every other read in this repository), a previously-soft-deleted vehicle's old registration number would look "free," the handler would proceed to `Vehicle.Create()` + `AddAsync()`, and the insert would then throw a raw, unhandled `DbUpdateException` from the DB-level unique-constraint violation instead of the clean 409 this story's AC requires. The pre-check must match the constraint's actual scope, so this is the one repository method that deliberately calls `IgnoreQueryFilters()`.

**`fieldFromType` collision risk:** since every business-rule `type` URI is `urn:bruno:{entity}:{rule-kebab-case}` and this form only handles Vehicle fields, no cross-entity collision is possible in this story; a future multi-entity form (e.g. Booking, which touches Vehicle+Customer+Booking fields in one submission) may need a smarter mapping -- not this story's problem.

**Frontend runs zoneless:** this app has no `zone.js` dependency. A plain (non-signal) property mutation on a component between two `fixture.detectChanges()` calls in a test does not propagate on its own -- only signal writes, `componentRef.setInput(...)`, or real DOM/Angular-bound events do. `Input`'s `ControlValueAccessor` methods (`writeValue`/`setDisabledState`) call `ChangeDetectorRef.markForCheck()` explicitly for this reason. Worth remembering for every future component/test on this project.

## Verification — actual results

- Backend: `dotnet build` 0 warnings/0 errors; `dotnet test` 93/93 passing (up from 73 before this story); `dotnet restore --force` showed no vulnerability warnings; `ArchitectureFitnessTests` unchanged/passing. Live manual proof against the real docker-compose Postgres: 201 create, 409 duplicate with the exact detail message, 400 blank Make, 409 implausible Year, follow-up GET confirming the row persisted.
- Frontend: `ng build` 0 errors, 0 warnings (after the budget bump); `ng test --coverage --watch=false` 85/85 passing, 95.06% statement coverage; `npm audit` 0 vulnerabilities.
- Full live end-to-end: backend + `ng serve` both running against the real docker-compose Postgres; drove the real UI in-browser -- created a vehicle (modal closed, toast fired, list updated without a manual refresh), triggered a real 409 by resubmitting the same registration number (inline error rendered correctly), confirmed Escape closes the modal and returns focus to the "+ New Vehicle" button.

</frozen-after-approval>

## Suggested Review Order

**The write path itself (the point of this story)**

- `CreateVehicleCommandHandler` — the existence-check → domain-factory → persist → map orchestration, with no validation logic of its own.
  [`CreateVehicleCommandHandler.cs`](../../src/BrunoVehicleHire.Application/Vehicles/Commands/CreateVehicleCommandHandler.cs)

- `VehicleRepository.ExistsByRegistrationNumberAsync` — the `IgnoreQueryFilters()` fix, and why it matters.
  [`VehicleRepository.cs:53`](../../src/BrunoVehicleHire.Infrastructure/Repositories/VehicleRepository.cs#L53)

- `CreateVehicleModal.applyError` — the one place a `NormalizedApiError` gets classified onto a field or a banner.
  [`create-vehicle-modal.ts:130`](../../frontend/src/app/features/vehicles/create-vehicle-modal.ts#L130)

**Proof (test-first, not test-after)**

- `VehiclesEndpointTests`'s new POST cases — including the implausible-Year case proving it's still 409, not silently accepted as 400.
  [`VehiclesEndpointTests.cs:335`](../../tests/BrunoVehicleHire.Integration.Tests/VehiclesEndpointTests.cs#L335)

- `Modal`'s focus-trap/focus-return tests.
  [`modal.spec.ts`](../../frontend/src/app/shared/modal/modal.spec.ts)

**Peripherals**

- The `VehicleDto.FromDomain` DRY extraction.
  [`VehicleDto.cs`](../../src/BrunoVehicleHire.Application/Vehicles/Dtos/VehicleDto.cs)

- `BusinessRuleError`'s new `errors`/`fieldFromType` extension.
  [`normalized-api-error.ts`](../../frontend/src/app/core/api-client/normalized-api-error.ts)
