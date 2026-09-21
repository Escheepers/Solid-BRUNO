---
title: 'Audit fix: Swagger with real descriptions and examples'
type: 'bugfix'
created: '2026-09-21'
status: 'done'
review_loop_iteration: 0
context: []
baseline_commit: 'b95ac9dc6a756de68dc7a7d6616d2e3b5508c161'
---

<frozen-after-approval reason="human-owned intent — do not modify unless human renegotiates">

## Intent

**Problem:** A full-spec audit found `stack.md`'s explicit "Swagger with examples" requirement unmet: `AddSwaggerGen` (`Program.cs`) only configures the API-key security scheme. Despite extensive XML doc comments already written on controllers/handlers/commands throughout the backend, none of it is wired into Swagger — no `IncludeXmlComments`, no request/response examples — so Swagger UI shows only Swashbuckle's bare reflection-based schema.

**Approach:** Enable XML documentation generation on the Api and Application projects and wire `IncludeXmlComments` so the codebase's existing doc comments surface as real Swagger descriptions. Add a small, Api-layer-only `ISchemaFilter` supplying concrete example JSON for the three Create commands (Vehicle/Customer/Booking) and their response DTOs — the request shapes an evaluator would most want a worked example for.

## Boundaries & Constraints

**Always:**
- `BrunoVehicleHire.Api.csproj` and `BrunoVehicleHire.Application.csproj` both get `<GenerateDocumentationFile>true</GenerateDocumentationFile>`, with `<NoWarn>$(NoWarn);CS1591</NoWarn>` added alongside it — this project does not mandate doc comments on every public member, only surfaces the ones that already exist; forcing CS1591 compliance now would be unrelated, disproportionate scope.
- `Program.cs`'s `AddSwaggerGen` gains `options.IncludeXmlComments(...)` for both generated XML files (Api's own and Application's, since Commands/DTOs live in Application).
- The example-provider stays entirely in the `Api` project (e.g. a new `Swagger/` folder) — Application-layer Commands/DTOs are never annotated with Swagger-specific attributes, keeping Clean Architecture's layering intact (Application has zero knowledge of the API/presentation concern of what Swagger shows).
- Example values cover exactly the three Create commands (`CreateVehicleCommand`, `CreateCustomerCommand`, `CreateBookingCommand`) and their response DTOs — realistic, plausible values (e.g. a real-looking registration number/make/model, a synthetic customer name/email, sane booking dates), never placeholder junk like `"string"`/`0`.

**Ask First:** Nothing else expected to trigger.

**Never:** No new Swagger/example NuGet package (e.g. `Swashbuckle.AspNetCore.Filters`) — a plain `ISchemaFilter` is sufficient and avoids a new dependency. No XML doc comments added purely to satisfy this story where none existed before, beyond what's needed to not break the build under the new `GenerateDocumentationFile` setting (there should be none, since `NoWarn` suppresses the only warning this would otherwise produce).

## I/O & Edge-Case Matrix

<!-- No meaningful I/O/edge-case scenarios — this is a documentation/tooling-only change with no new runtime behavior. -->

</frozen-after-approval>

## Code Map

- `src/BrunoVehicleHire.Api/BrunoVehicleHire.Api.csproj` -- modify -- add `GenerateDocumentationFile`/`NoWarn`
- `src/BrunoVehicleHire.Application/BrunoVehicleHire.Application.csproj` -- modify -- same
- `src/BrunoVehicleHire.Api/Program.cs` -- modify -- `AddSwaggerGen` (`:124-138`) gains `IncludeXmlComments` for both XML files and `options.SchemaFilter<...>()` registration
- `src/BrunoVehicleHire.Api/Swagger/ExampleSchemaFilter.cs` -- new -- `ISchemaFilter` implementation providing example JSON for `CreateVehicleCommand`/`VehicleDto`, `CreateCustomerCommand`/`CustomerDto`, `CreateBookingCommand`/`BookingDto`
- No test changes expected — this has no testable runtime behavior beyond "the app still builds and Swagger UI still loads," verified manually

## Tasks & Acceptance

**Execution:**
- [x] Enable `GenerateDocumentationFile`/`NoWarn` on both csproj files; confirm the build stays warning-free
- [x] Wire `IncludeXmlComments` for both XML files in `AddSwaggerGen`
- [x] Add the `ExampleSchemaFilter` with realistic example values for the three Create commands + response DTOs
- [x] Live check: open Swagger UI, confirm operation summaries/descriptions now show real text (not just auto-generated schema), and the three Create endpoints' request bodies show the new example values
- [x] Full `dotnet build`/`dotnet test`, confirm nothing regressed

**Acceptance Criteria:**
- [x] Given Swagger UI is opened, when a controller action is expanded, then its summary text reflects the real XML doc comment already written in the source, not a blank/auto-generated placeholder
- [x] Given the Create Vehicle/Customer/Booking endpoints' request bodies in Swagger UI, when viewed, then each shows a concrete, realistic example value, not `"string"`/`0`-style defaults

## Spec Change Log

## Design Notes

**Why an `ISchemaFilter` instead of XML `<example>` tags on the Command records:** the three Create commands are positional-parameter `record`s (e.g. `CreateVehicleCommand(string RegistrationNumber, ...)`). Swashbuckle's XML-comments integration reads `<example>` tags from individual property doc comments, which positional records don't cleanly support without converting every command to verbose property-based declarations — a disproportionate refactor for a documentation-only fix. A schema filter achieves the same visible result in Swagger UI with a single new Api-layer file, and keeps Swagger-specific concerns out of the Application layer entirely.

## Verification

Independently reproduced (not just re-reading the implementer's report), per this project's standing verification rule.

**Commands:**
- `dotnet build BrunoVehicleHire.sln` (own run) — 0 warnings, 0 errors, confirming `NoWarn CS1591` correctly suppresses the new-doc-generation warning without masking any other issue.
- `dotnet test BrunoVehicleHire.sln` (own run, full solution) — 436/436 passing (Domain 85, Application 148, Infrastructure 4, Api 23, Integration 176).

**Code read:** `ExampleSchemaFilter.cs` (confirmed it references Application-layer types only via `typeof()` checks — no Swagger attribute leaks into Application; realistic, non-placeholder example values for all six targeted types). `Program.cs`'s `AddSwaggerGen` (confirmed `IncludeXmlComments` wired for both XML files and `SchemaFilter<ExampleSchemaFilter>()` registered).

**Live verification, independently reproduced:** started the real API and fetched `/swagger/v1/swagger.json` directly (bypassing a self-signed-cert browser block). Confirmed: `GET /api/Bookings/{id}` carries a real multi-sentence `summary` pulled from its actual XML doc comment (not blank); `CreateBookingCommand`'s schema carries both a real `description` (from its XML doc comment) and a genuine `example` block (`vehicleId`/`customerId` as proper GUIDs, sane `startDate`/`endDate`); `CreateVehicleCommand`'s example shows a realistic registration/make/model/year/rate; `CreateCustomerCommand`'s example shows a realistic synthetic name/email/phone. None are `"string"`/`0`-style placeholders.

**Unplanned but necessary fix, confirmed correct:** enabling `GenerateDocumentationFile` surfaced 5 pre-existing broken XML `cref`/`paramref` references (cross-assembly crefs, a stale paramref, a typo) that would otherwise have been silent since XML doc generation wasn't previously enabled. Read the fix in each of the 5 touched files (`CancelBookingCommandHandler.cs`, `NotFoundException.cs`, `ValidationBehavior.cs`, `UpdateCustomerCommandValidator.cs`, `VehiclesController.cs`) — each was corrected with the minimal existing-convention fix (converting a cross-layer cref to a plain `<c>` tag, matching the pattern already used elsewhere in the same files), never adding new documentation content. Confirmed via the clean 0-warning build above.

**Known, disclosed limitation (not a defect):** `VehicleDto`/`CustomerDto`/`BookingDto` never appear in the generated spec's `components.schemas` at all, since every controller action returns bare `IActionResult` with no `[ProducesResponseType]` — Swashbuckle has no declared response type to generate a schema from. `ExampleSchemaFilter`'s response-DTO example branches are implemented correctly but currently inert until/unless a future story adds `[ProducesResponseType]` attributes (out of scope here — would mean editing Controllers, which were being concurrently modified by a parallel spec during this implementation). The spec's actual Acceptance Criteria (real operation summaries + real Create-request examples) are fully met regardless.

## Suggested Review Order

1. `src/BrunoVehicleHire.Api/Program.cs` — the `IncludeXmlComments`/`SchemaFilter` wiring
2. `src/BrunoVehicleHire.Api/Swagger/ExampleSchemaFilter.cs` — the example provider
3. `src/BrunoVehicleHire.Api/BrunoVehicleHire.Api.csproj` and `src/BrunoVehicleHire.Application/BrunoVehicleHire.Application.csproj` — the `GenerateDocumentationFile`/`NoWarn` additions
4. The 5 incidental `cref`/`paramref` fixes (`CancelBookingCommandHandler.cs`, `NotFoundException.cs`, `ValidationBehavior.cs`, `UpdateCustomerCommandValidator.cs`, `VehiclesController.cs`) — confirm each is a minimal, content-preserving correction
