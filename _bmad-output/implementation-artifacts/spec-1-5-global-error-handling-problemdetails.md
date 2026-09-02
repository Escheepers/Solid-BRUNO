---
title: 'Story 1.5: Global Error Handling (ProblemDetails)'
type: 'feature'
created: '2026-09-02'
status: 'done'
review_loop_iteration: 0
context: ['{project-root}/_bmad-output/implementation-artifacts/epic-1-context.md']
baseline_commit: 'dd875f7e9d5ed79c4a15cfd90e3e8afc648ffdd9'
---

<frozen-after-approval reason="human-owned intent — do not modify unless human renegotiates">

## Intent

**Problem:** No global exception handling exists — an unhandled exception right now would produce ASP.NET Core's default developer-exception-page HTML (or a bare 500 with no body in Production), and `DomainRuleViolationException` (built in Story 1.3) has nothing mapping it to an HTTP response yet.

**Approach:** Register ASP.NET Core's built-in `AddProblemDetails()` + a custom `IExceptionHandler` (AD-8) that maps `DomainRuleViolationException` → `409 Conflict` and anything else unhandled → a generic `500`, both as valid RFC 9457 ProblemDetails objects, with `type` URIs generated from one shared function (never hand-typed per call site).

**Scope split from epics.md — flagged for approval:** Story 1.5's second AC in epics.md tests this handler against `GetVehiclesQuery`'s pagination validator via the `ValidationBehavior` MediatR pipeline (AD-10) → `400 Bad Request`. Neither `GetVehiclesQuery`, MediatR, nor FluentValidation exist yet in this solution (the Application layer is still an empty scaffold) — that's Story 1.7's deliverable, described in epics.md itself as "the Walking Skeleton Proof." Building a throwaway query now to test a pipeline behavior that doesn't exist yet would mean redoing 1.7's actual work twice, and adding a `FluentValidation.ValidationException` branch to this handler today would be untested dead code (no validator exists anywhere in the solution to throw it).

**This story delivers:** the `409` (real, already-built `DomainRuleViolationException`) and `500` (generic fallback) branches, fully tested. **Story 1.7 will extend** the same `GlobalExceptionHandler` with the `400`/`FluentValidation.ValidationException` branch once `GetVehiclesQuery` and its validator exist, proving the exact AC line from epics.md at that point — the mechanism doesn't change, only a third `switch` arm is added once there's a real, testable case for it.

The AC's PII non-interpolation requirement ("no ProblemDetails response ever interpolates a raw PII value into its `detail` string") also can't be tested for real yet — `Customer` (the only PII-bearing entity) doesn't exist until Epic 3. This story enforces the *general* version of that discipline now (the `500` branch never echoes `exception.Message` to the client at all, only a fixed safe string) and documents the specific Customer-PII regression test as Epic 3's responsibility once there's a `Customer.Email`/`PhoneNumber` to test against.

## Boundaries & Constraints

**Always:** `AddProblemDetails()` and `AddExceptionHandler<GlobalExceptionHandler>()` are registered; `app.UseExceptionHandler()` sits as the first middleware in the pipeline so it can catch exceptions from everything after it. Every ProblemDetails response is a valid RFC 9457 object: `Status`, `Title`, `Type`, `Detail` all populated. `Type` URIs follow the template `urn:bruno:{entity-kebab-case}:{rule-kebab-case}` produced by one shared `ProblemTypeUris` helper — never a hand-typed string literal at a handler call site. `DomainRuleViolationException` → `409 Conflict`, `Detail` = the exception's own message (developer-authored via `nameof(...)`, not raw user/PII input — see Design Notes on why this is safe today and what changes for Epic 3). Any other unhandled exception → `500 Internal Server Error` with a fixed, generic `Detail` string — `exception.Message` and stack trace are never echoed to the client, only to server-side logging (standard practice: internal exception details are an information-disclosure risk, not just a PII risk).

**Ask First:** Nothing expected to trigger.

**Never:** No `FluentValidation.ValidationException`/`400` branch yet (Story 1.7's job, once FluentValidation exists in the solution — see Intent). No new production-reachable endpoint that deliberately throws — the two diagnostic endpoints added for this story's own integration tests (see Code Map) are mapped only when `IWebHostEnvironment.EnvironmentName == "Testing"`, a value the real app is never started with (Development/Production only), so they carry zero Production/Development attack surface. No touching `HealthController`, `ApiKeyAuthenticationHandler`, or anything from Story 1.4.

## I/O & Edge-Case Matrix

| Scenario | Input / State | Expected Output / Behavior | Error Handling |
|----------|--------------|---------------------------|----------------|
| `DomainRuleViolationException` thrown anywhere in the pipeline | e.g. `Entity="Vehicle", Rule="Year"` | Valid ProblemDetails: `Status=409`, `Type="urn:bruno:vehicle:year"`, `Detail=`exception message | `409 Conflict` |
| Any other unhandled exception | e.g. a generic `InvalidOperationException` | Valid ProblemDetails: `Status=500`, `Type` = a fixed "unexpected error" URI, `Detail` = fixed generic string (never `exception.Message`) | `500 Internal Server Error` |
| `ProblemTypeUris.For(entity, rule)` | `("Vehicle", "RegistrationNumber")` | `"urn:bruno:vehicle:registration-number"` (kebab-cased both segments) | N/A |
| Response reachability | Any exception path, with a valid `X-Api-Key` header (Story 1.4's fallback policy still applies to these endpoints) | Response body deserializes as `ProblemDetails` with `Content-Type: application/problem+json` | N/A |

## Code Map

- `src/BrunoVehicleHire.Api/ErrorHandling/ProblemTypeUris.cs` -- new -- `public static class ProblemTypeUris { public static string For(string entity, string rule) => $"urn:bruno:{ToKebabCase(entity)}:{ToKebabCase(rule)}"; public const string UnexpectedError = "urn:bruno:server:unexpected-error"; }` plus a private `ToKebabCase` (PascalCase → kebab-case) helper
- `src/BrunoVehicleHire.Api/ErrorHandling/GlobalExceptionHandler.cs` -- new -- `IExceptionHandler` implementation; `DomainRuleViolationException` → 409 via `ProblemTypeUris.For(ex.Entity, ex.Rule)`; anything else → 500 via `ProblemTypeUris.UnexpectedError` and a fixed generic `Detail`; writes via injected `IProblemDetailsService`
- `src/BrunoVehicleHire.Api/Program.cs` -- modify -- `builder.Services.AddProblemDetails()`, `builder.Services.AddExceptionHandler<GlobalExceptionHandler>()`, `app.UseExceptionHandler()` as the first pipeline middleware; two diagnostic minimal-API endpoints (`GET /api/test/throw-domain-rule`, `GET /api/test/throw-unhandled`) mapped only `if (app.Environment.IsEnvironment("Testing"))`, existing solely so this story's own integration tests can exercise the full pipeline against a real HTTP request without waiting for a real domain-throwing endpoint (Story 1.7)
- `tests/BrunoVehicleHire.Api.Tests/GlobalExceptionHandlerTests.cs` -- new -- unit tests calling `TryHandleAsync` directly against a bare `DefaultHttpContext` for both branches (409 shape, 500 shape, 500 never contains `exception.Message`), plus `ProblemTypeUris` kebab-casing tests
- `tests/BrunoVehicleHire.Integration.Tests/GlobalExceptionHandlingTests.cs` -- new -- `WebApplicationFactory<Program>` (`UseEnvironment("Testing")`) hitting the two diagnostic endpoints with a valid `X-Api-Key` header, asserting real HTTP status codes, `Content-Type: application/problem+json`, and full ProblemDetails shape

## Tasks & Acceptance

**Execution:**
- [x] `src/BrunoVehicleHire.Api/ErrorHandling/ProblemTypeUris.cs` -- write failing unit tests for `ToKebabCase`/`For`/`UnexpectedError` first, then implement
- [x] `src/BrunoVehicleHire.Api/ErrorHandling/GlobalExceptionHandler.cs` -- write failing unit tests first (`DomainRuleViolationException` → 409 shape; generic exception → 500 shape; 500 detail never contains `exception.Message`), then implement against a real `IProblemDetailsService` (from `AddProblemDetails()`)
- [x] `src/BrunoVehicleHire.Api/Program.cs` -- register `AddProblemDetails()`/`AddExceptionHandler<GlobalExceptionHandler>()`/`UseExceptionHandler()` (first middleware), add the two `Testing`-only diagnostic endpoints
- [x] `tests/BrunoVehicleHire.Integration.Tests/GlobalExceptionHandlingTests.cs` -- write failing integration tests first against the real pipeline, then confirm green
- [x] Full `dotnet test`; re-run `ArchitectureFitnessTests`

**Acceptance Criteria:**
- Given `AddProblemDetails()` and `GlobalExceptionHandler` are registered, when a `DomainRuleViolationException` is thrown anywhere in the pipeline, then the response is a valid RFC 9457 ProblemDetails object with `Status=409` and a `Type` URI in the `urn:bruno:{entity}:{rule}` shape
- Given any other unhandled exception, when it occurs, then the response is a valid RFC 9457 ProblemDetails object with `Status=500`, and `exception.Message`/stack trace never appear in the response body
- Given `ProblemTypeUris.For(...)`, when called with any entity/rule pair, then the result always matches the `urn:bruno:{entity-kebab}:{rule-kebab}` template — no handler ever hand-types a `type` string literal
- Given `ArchitectureFitnessTests`, when run after this story's changes, then all still pass unchanged in intent

## Spec Change Log

## Design Notes

`GlobalExceptionHandler` lives in `Api`, not `Application` or `Infrastructure`, because `IExceptionHandler`/`HttpContext`/`IProblemDetailsService` are ASP.NET Core types — keeping it here matches the existing precedent (`Auth/` also lives in `Api` for the same reason).

`DomainRuleViolationException.Message` is safe to echo today because every current call site (`Vehicle.Create`) builds it from a `nameof(...)`-sourced property name and a static string, never from raw user input. This assumption **must be re-checked in Epic 3**: once `Customer`'s domain rules exist, any `DomainRuleViolationException` message that might embed `Customer.Email`/`PhoneNumber` needs either a PII-safe message (reference the customer by id, per AD-8) or a redaction step in this handler before Epic 3 ships — flagging this now so it isn't forgotten. A concrete regression test for "no raw PII in `detail`" belongs in Epic 3's spec, once there's a real PII field to assert against; asserting it today against a `Vehicle` (no PII fields at all) would be a vacuously-true test that gives false confidence.

The `type` URI's "rule" segment is `DomainRuleViolationException.Rule`, itself always `nameof(...)`-sourced today — this satisfies AD-8's "never hand-typed per handler" intent via one shared kebab-casing function fed by compiler-checked property names, which is a lighter-weight mechanism than maintaining a separate enumerated const registry and was chosen over one for that reason.

`UseExceptionHandler()` is placed as the very first pipeline middleware (before `UseSwagger`, `UseHttpsRedirection`, everything from Story 1.4) so it can catch exceptions thrown by any later middleware, not just endpoint handlers.

## Verification

**Commands:**
- `dotnet build BrunoVehicleHire.sln` -- expected: 0 errors, 0 warnings -- confirmed
- `dotnet test BrunoVehicleHire.sln` -- expected: all prior 38 tests still pass, plus new `GlobalExceptionHandlerTests` (unit) and `GlobalExceptionHandlingTests` (integration), all green -- confirmed: 17 + 1 + 16 + 18 = 52 passing, 0 failed

</frozen-after-approval>

## Suggested Review Order

**The handler itself (the point of this story)**

- `TryHandleAsync` — the 409/500 split, and the fixed generic detail that never echoes `exception.Message`.
  [`GlobalExceptionHandler.cs:26`](../../src/BrunoVehicleHire.Api/ErrorHandling/GlobalExceptionHandler.cs#L26)

- `ProblemTypeUris.For` — the one shared kebab-casing function every `type` URI goes through.
  [`ProblemTypeUris.cs:20`](../../src/BrunoVehicleHire.Api/ErrorHandling/ProblemTypeUris.cs#L20)

- `UseExceptionHandler()` as the first pipeline middleware.
  [`Program.cs:80`](../../src/BrunoVehicleHire.Api/Program.cs#L80)

**Proof (test-first, not test-after)**

- The "500 never leaks the original exception message" tests — the easiest AC to silently violate later.
  [`GlobalExceptionHandlerTests.cs`](../../tests/BrunoVehicleHire.Api.Tests/GlobalExceptionHandlerTests.cs)

- End-to-end proof against a real pipeline, including confirming Story 1.4's FallbackPolicy still applies to the new diagnostic endpoints.
  [`GlobalExceptionHandlingTests.cs:109`](../../tests/BrunoVehicleHire.Integration.Tests/GlobalExceptionHandlingTests.cs#L109)

**Peripherals**

- The two `Testing`-only diagnostic endpoints that exist solely to give this story's tests a real HTTP surface, ahead of Story 1.7's real domain-throwing endpoints.
  [`Program.cs:95`](../../src/BrunoVehicleHire.Api/Program.cs#L95)
