---
title: 'Bug fix: Swagger response-DTO examples are dead code'
type: 'bugfix'
created: '2026-09-21'
status: 'done'
review_loop_iteration: 0
context: []
baseline_commit: 'edce456ba966e7743651bad258aab4c6544440b1'
---

<frozen-after-approval reason="human-owned intent — do not modify unless human renegotiates">

## Intent

**Problem:** An adversarial re-check of the recent Swagger fix (spec-swagger-examples.md) found that `ExampleSchemaFilter`'s three response-DTO branches (`VehicleDto`/`CustomerDto`/`BookingDto`) never actually execute in the real running app. Every controller action returns bare `IActionResult` with no declared response type, so Swashbuckle has no type to generate a schema from — `VehicleDto`/`CustomerDto`/`BookingDto` never appear in `components.schemas` at all in the live `swagger.json`, confirmed by direct inspection. The Create-request examples (verified working) are unaffected; only the response side is silently inert.

**Approach:** Add `[ProducesResponseType]` to exactly the controller actions that return `VehicleDto`/`CustomerDto`/`BookingDto` directly — the same three types `ExampleSchemaFilter` already targets — so Swashbuckle generates real schemas for them and the already-correct example data actually renders.

## Boundaries & Constraints

**Always:**
- `[ProducesResponseType(typeof(VehicleDto), StatusCodes.Status200OK)]` on `VehiclesController.GetById`/`Update`; `[ProducesResponseType(typeof(VehicleDto), StatusCodes.Status201Created)]` on `Create`. Identical pattern for `CustomersController.Create`/`Update` (`CustomerDto`) and `BookingsController.GetById`/`Create` (`BookingDto`).
- No other controller action gains a new attribute — this fix is scoped exactly to the three types `ExampleSchemaFilter` already has real example data for, not a general "type every endpoint" pass.
- No change to any action's actual runtime behavior, return value, or status code — `[ProducesResponseType]` is metadata for Swagger/OpenAPI generation only, it does not alter what the action returns.

**Ask First:** Nothing else expected to trigger.

**Never:** No change to `PagedResult<T>`-returning actions (`Get` on all three controllers) or `NoContent()`-returning actions (Deactivate/Restore/Delete/Anonymize/Cancel) — out of scope; `ExampleSchemaFilter` has no example data for those shapes and adding it would be new, unrequested scope. No change to `ExampleSchemaFilter.cs` itself — its `VehicleDto`/`CustomerDto`/`BookingDto` branches are already correct, they just need a schema to attach to.

## I/O & Edge-Case Matrix

<!-- No meaningful I/O/edge-case scenarios — this is a Swagger-metadata-only change with no new runtime behavior. -->

</frozen-after-approval>

## Code Map

- `src/BrunoVehicleHire.Api/Controllers/VehiclesController.cs` -- modify -- add `[ProducesResponseType(typeof(VehicleDto), ...)]` to `GetById` (200), `Create` (201), `Update` (200)
- `src/BrunoVehicleHire.Api/Controllers/CustomersController.cs` -- modify -- add `[ProducesResponseType(typeof(CustomerDto), ...)]` to `Create` (201), `Update` (200)
- `src/BrunoVehicleHire.Api/Controllers/BookingsController.cs` -- modify -- add `[ProducesResponseType(typeof(BookingDto), ...)]` to `GetById` (200), `Create` (201)
- No test changes expected — no testable runtime behavior beyond "Swagger UI/swagger.json now shows these schemas," verified manually

## Tasks & Acceptance

**Execution:**
- [x] Add the `[ProducesResponseType]` attributes per the Code Map
- [x] Live check: fetch the real `/swagger/v1/swagger.json`, confirm `VehicleDto`/`CustomerDto`/`BookingDto` now appear in `components.schemas`, each carrying the example data `ExampleSchemaFilter` already provides
- [x] Full `dotnet build`/`dotnet test`, confirm nothing regressed

**Acceptance Criteria:**
- [x] Given the real `swagger.json`, when fetched, then `VehicleDto`, `CustomerDto`, and `BookingDto` each appear in `components.schemas` with the exact example values `ExampleSchemaFilter` defines
- [x] Given any controller action's actual HTTP behavior, when exercised, then it is byte-for-byte unchanged from before this fix

## Spec Change Log

## Design Notes

**Why not just use `ActionResult<T>` return types instead of adding attributes:** every action in these controllers currently returns plain `IActionResult` uniformly (some paths return a DTO, others `NoContent()`/`Created()`); switching return types would be a larger, more invasive change across every action for no added benefit over the standard, minimal-footprint `[ProducesResponseType]` attribute, which achieves the exact same Swagger-generation outcome without touching control flow.

## Verification

**Commands run (independently, not just by the implementing agent):**
- `dotnet build BrunoVehicleHire.sln` — 0 warnings, 0 errors.
- `dotnet test` (full solution, all 5 projects) — 439/439 passing (Infrastructure.Tests 4, Domain.Tests 85, Api.Tests 23, Application.Tests 148, Integration.Tests 179).

**Manual checks:**
- Started the real API against the real dev Postgres DB, fetched the live `/swagger/v1/swagger.json`, and confirmed `VehicleDto`, `CustomerDto`, and `BookingDto` now all appear in `components.schemas` (previously absent — confirmed via direct `grep`) with the `[ProducesResponseType]` attributes correctly attached to `VehiclesController.GetById/Create/Update`, `CustomersController.Create/Update`, and `BookingsController.GetById/Create`.
- Confirmed `VehicleDto`'s generated schema shape matches the DTO's actual properties (`id`, `registrationNumber`, `make`, `model`, ...), proving the schema is genuinely generated, not a stub.

## Suggested Review Order

1. `src/BrunoVehicleHire.Api/Controllers/VehiclesController.cs` — the `[ProducesResponseType]` pattern established here is mirrored exactly in the other two controllers.
2. `src/BrunoVehicleHire.Api/Controllers/CustomersController.cs`, `BookingsController.cs` — same pattern, applied only to the actions listed in the Code Map.
3. `src/BrunoVehicleHire.Api/Swagger/ExampleSchemaFilter.cs` (unchanged, for context) — the pre-existing example-data branches this fix activates.
