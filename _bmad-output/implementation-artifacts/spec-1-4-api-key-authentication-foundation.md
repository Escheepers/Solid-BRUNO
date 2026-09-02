---
title: 'Story 1.4: API-Key Authentication Foundation'
type: 'feature'
created: '2026-09-02'
status: 'done'
review_loop_iteration: 0
context: ['{project-root}/_bmad-output/implementation-artifacts/epic-1-context.md']
baseline_commit: '5dd684d41fac34ebe77fc5d47e28b36d410dede4'
---

<frozen-after-approval reason="human-owned intent — do not modify unless human renegotiates">

## Intent

**Problem:** No request pipeline exists yet — `Program.cs` only migrates the database and exposes a bare OpenAPI document. Nothing stops any future controller from shipping unauthenticated, and there is no real endpoint yet to prove authentication actually works end-to-end.

**Approach:** Implement AD-11 exactly: a custom `AuthenticationHandler` registered via `AddAuthentication().AddScheme<...>()`, enforced globally via a `FallbackPolicy` so every endpoint is protected by default with zero per-endpoint attributes. Two pieces of scaffolding are pulled forward into this story because AD-11 depends on them existing correctly, and no other story owns them:

1. **`AddControllers()`/`MapControllers()` + one minimal `HealthController`** (`GET /api/health`) — Story 1.7 needs the controller pipeline anyway; a health endpoint gives the auth mechanism a real, non-`[AllowAnonymous]` target to enforce against and integration-test, proving the fallback covers *any* future controller automatically.
2. **Swagger UI (Swashbuckle.AspNetCore), replacing the bare `AddOpenApi()`/`MapOpenApi()` scaffold from Story 1.1** — AD-11 explicitly requires the handler to integrate with "the Swagger security definition," which is meaningless without an actual Swagger UI to integrate with. Epic 6's success criterion ("verify every business rule... through both the Angular UI and Swagger") has no other story that adds Swagger. Running Microsoft's bare OpenAPI generator *and* Swashbuckle side by side would produce two redundant, inconsistent API docs, so Swashbuckle replaces it outright.

**Approval note:** these two additions are scope pulled forward from unassigned/dependent requirements, not scope invention — flagging them explicitly since they weren't itemized in epics.md's Story 1.4 AC text verbatim.

## Boundaries & Constraints

**Always:** A global `FallbackPolicy` (`AuthorizationPolicyBuilder(ApiKeyDefaults.Scheme).RequireAuthenticatedUser().Build()`) is registered so every endpoint requires the api-key scheme unless explicitly marked `[AllowAnonymous]` (used nowhere in this story). The configured key is read via `IOptions<ApiKeyOptions>`, sourced from configuration (`appsettings.Development.json` for local dev; a real deployment would supply it via user-secrets/environment variable) — never a literal in `Program.cs` or the handler. The header comparison uses `CryptographicOperations.FixedTimeEquals` (constant-time, avoids a timing side-channel on key comparison). No log call, exception message, or `AuthenticateResult.Fail(...)` string anywhere in the handler ever includes the raw header value or the configured key. Swashbuckle declares an `ApiKey` security scheme (`In = Header, Name = "X-Api-Key"`) plus a global security requirement, so Swagger UI's "Authorize" dialog exercises the real pipeline. `HealthController`'s action carries no `[Authorize]`/`[AllowAnonymous]` attribute — it inherits protection from the fallback policy alone, which is the entire point being proven.

**Ask First:** Nothing expected to trigger — this is a standard, well-documented ASP.NET Core pattern (custom scheme + fallback policy).

**Never:** No `[Authorize]` attribute added to any controller/action anywhere (that would contradict "no per-endpoint attribute needed"). No hardcoded key literal. No keeping both `AddOpenApi()` and Swashbuckle registered simultaneously. No scope beyond auth + the two forced-forward dependencies above — no Vehicle endpoints, no MediatR wiring (Story 1.5/1.7's job).

## I/O & Edge-Case Matrix

| Scenario | Input / State | Expected Output / Behavior | Error Handling |
|----------|--------------|---------------------------|----------------|
| Missing header | `GET /api/health`, no `X-Api-Key` header | Request rejected | `401 Unauthorized` |
| Blank header | `X-Api-Key: ` (empty/whitespace) | Request rejected | `401 Unauthorized` |
| Wrong key | `X-Api-Key: wrong-value` | Request rejected | `401 Unauthorized` |
| Correct key | `X-Api-Key: <configured value>` | Request proceeds, `HealthController` returns `200 OK` | N/A |
| Swagger document | `GET /swagger/v1/swagger.json` | Document includes an `ApiKey` security scheme (header, name `X-Api-Key`) and a global security requirement | N/A |
| Log output on auth failure/success | Any of the above | No log entry anywhere contains the literal configured key or the literal submitted header value | N/A |

## Code Map

- `src/BrunoVehicleHire.Api/Auth/ApiKeyDefaults.cs` -- new -- scheme name (`"ApiKey"`) and header name (`"X-Api-Key"`) constants
- `src/BrunoVehicleHire.Api/Auth/ApiKeyOptions.cs` -- new -- `public class ApiKeyOptions { public string Key { get; set; } = string.Empty; }`, bound from configuration section `"ApiKey"`
- `src/BrunoVehicleHire.Api/Auth/ApiKeyAuthenticationHandler.cs` -- new -- `AuthenticationHandler<AuthenticationSchemeOptions>` reading the `X-Api-Key` header, comparing via `CryptographicOperations.FixedTimeEquals`, returning `AuthenticateResult.Success`/`.Fail` (generic messages, no raw key material) accordingly
- `src/BrunoVehicleHire.Api/Controllers/HealthController.cs` -- new -- `[ApiController] [Route("api/[controller]")]`, single `GET` action returning `Ok()`; no auth attributes -- proves the fallback policy alone
- `src/BrunoVehicleHire.Api/Program.cs` -- modify -- register `AddAuthentication(ApiKeyDefaults.Scheme).AddScheme<AuthenticationSchemeOptions, ApiKeyAuthenticationHandler>(...)`, `AddAuthorization` with the `FallbackPolicy`, `AddControllers()`/`MapControllers()`, `Configure<ApiKeyOptions>(...)`; replace `AddOpenApi()`/`MapOpenApi()` with Swashbuckle's `AddSwaggerGen()` (with the `ApiKey` security definition + requirement) and `UseSwagger()`/`UseSwaggerUI()`; add `app.UseAuthentication()`/`app.UseAuthorization()` in the correct pipeline order (after `UseHttpsRedirection()`, before `MapControllers()`); add `public partial class Program { }` marker for `WebApplicationFactory<Program>` test access
- `src/BrunoVehicleHire.Api/appsettings.Development.json` -- modify -- add `"ApiKey": { "Key": "local-dev-only-key-change-me" }`
- `src/BrunoVehicleHire.Api/appsettings.json` -- modify -- add `"ApiKey": { "Key": "" }` (blank; a real deployment must override via user-secrets/environment variable -- documented later in Story 6.1's README)
- `src/BrunoVehicleHire.Api/BrunoVehicleHire.Api.csproj` -- modify -- remove `Microsoft.AspNetCore.OpenApi` (no longer used); add `Swashbuckle.AspNetCore` 10.2.3
- `tests/BrunoVehicleHire.Integration.Tests/BrunoVehicleHire.Integration.Tests.csproj` -- modify -- add `Microsoft.AspNetCore.Mvc.Testing` 10.0.11
- `tests/BrunoVehicleHire.Integration.Tests/ApiKeyAuthenticationTests.cs` -- new -- `WebApplicationFactory<Program>`-based tests (own Testcontainers Postgres instance, matching `VehicleMigrationTests`' pattern, with configuration overridden for both `ConnectionStrings:Postgres` and `ApiKey:Key`) covering every I/O-matrix row plus the Swagger security-scheme assertion
- `tests/BrunoVehicleHire.Api.Tests/` -- new project -- unit tests for `ApiKeyAuthenticationHandler` in isolation (no ASP.NET Core host), asserting the constant-time comparison behavior and that no captured log message contains the raw key on any failure path
- `BrunoVehicleHire.sln` -- modify -- add the new `BrunoVehicleHire.Api.Tests` project
- `tests/BrunoVehicleHire.Integration.Tests/ArchitectureFitnessTests.cs` -- no change expected, but re-run to confirm `Auth/`, `Controllers/` additions in Api don't violate any layering rule

## Tasks & Acceptance

**Execution:**
- [x] `tests/BrunoVehicleHire.Api.Tests/` -- scaffold new xUnit project (coverlet.collector, FluentAssertions 7.2.2, NSubstitute for the fake `ILogger`), add to `.sln`
- [x] `src/BrunoVehicleHire.Api/Auth/ApiKeyDefaults.cs`, `ApiKeyOptions.cs` -- minimal constants/options types
- [x] Write failing unit tests first in `tests/BrunoVehicleHire.Api.Tests/` for `ApiKeyAuthenticationHandler`: missing header -> fail, blank header -> fail, wrong key -> fail, correct key -> success with a claims principal, no raw key value ever passed to the injected `ILogger`
- [x] `src/BrunoVehicleHire.Api/Auth/ApiKeyAuthenticationHandler.cs` -- implement to make the above pass
- [x] `src/BrunoVehicleHire.Api/Controllers/HealthController.cs` -- minimal health endpoint
- [x] `src/BrunoVehicleHire.Api/Program.cs` -- wire authentication/authorization/fallback policy, controllers, Swashbuckle (replacing `AddOpenApi`/`MapOpenApi`), `public partial class Program` marker
- [x] `src/BrunoVehicleHire.Api/appsettings.json`, `appsettings.Development.json` -- add `ApiKey` configuration section
- [x] Write failing integration tests first in `tests/BrunoVehicleHire.Integration.Tests/ApiKeyAuthenticationTests.cs` against a real `WebApplicationFactory<Program>` + Testcontainers Postgres: each I/O-matrix row, then implement/adjust Program.cs wiring until green
- [x] Confirm `dotnet test` end-to-end and re-run `ArchitectureFitnessTests` to confirm no layering violation from the new `Auth`/`Controllers` folders

**Acceptance Criteria:**
- Given a request to any controller/action omits the `X-Api-Key` header, when processed, then it is rejected with `401 Unauthorized` -- proven against `HealthController`, a real endpoint, not a mock
- Given a request supplies the correct `X-Api-Key` value, when processed, then it proceeds and reaches the controller action
- Given any authentication failure or success, when logs are inspected, then no log entry contains the raw configured key or the raw submitted header value
- Given the Swagger document is requested, when inspected, then it declares an `ApiKey` header security scheme and a global security requirement matching the actual enforcement (AD-11's stated failure mode: Swagger claiming something inconsistent with what's actually enforced)
- Given `ArchitectureFitnessTests`, when run after this story's changes, then all still pass unchanged in intent

## Spec Change Log

## Design Notes

`AuthenticationSchemeOptions` (the built-in base, not a custom options subclass) is sufficient here since the handler needs no scheme-specific configuration beyond the shared `ApiKeyOptions` read via DI — introducing a dedicated options subclass would be a needless layer for a single string comparison.

A blank `ApiKey:Key` in `appsettings.json` for non-Development environments is a deliberate fail-safe default: because the constant-time comparison never matches an empty configured key against any non-empty submitted header, an operator who forgets to set the real key via user-secrets/environment variable gets total lockout (safe failure) rather than an accidentally-open endpoint.

The health endpoint is intentionally trivial (no dependency on `AppDbContext` or any domain type) so this story stays scoped to proving the auth mechanism, not building a real feature.

## Verification

**Commands:**
- `dotnet build BrunoVehicleHire.sln` -- expected: all projects build (7 existing + 1 new `Api.Tests`), 0 errors, 0 warnings
- `dotnet test BrunoVehicleHire.sln` -- expected: all prior tests still pass, plus new `ApiKeyAuthenticationHandler` unit tests and `ApiKeyAuthenticationTests` integration tests, all green
- Manual: `dotnet run` in `src/BrunoVehicleHire.Api`, open `/swagger`, confirm the "Authorize" button accepts the dev key from `appsettings.Development.json` and `GET /api/health` returns 200 only after authorizing

</frozen-after-approval>

## Suggested Review Order

**The auth mechanism itself (the point of this story)**

- `HandleAuthenticateAsync` — the constant-time comparison and the generic, no-raw-key failure messages.
  [`ApiKeyAuthenticationHandler.cs:34`](../../src/BrunoVehicleHire.Api/Auth/ApiKeyAuthenticationHandler.cs#L34)

- The global `FallbackPolicy` registration — the whole "secure-by-default, no `[Authorize]` needed" mechanism in one place.
  [`Program.cs:19`](../../src/BrunoVehicleHire.Api/Program.cs#L19)

- `HealthController` — deliberately zero auth attributes, proving the fallback alone protects it.
  [`HealthController.cs:15`](../../src/BrunoVehicleHire.Api/Controllers/HealthController.cs#L15)

**Proof (test-first, not test-after)**

- The "never logs the raw key" tests — the easiest AC to silently violate later with an innocent-looking debug log line.
  [`ApiKeyAuthenticationHandlerTests.cs:136`](../../tests/BrunoVehicleHire.Api.Tests/ApiKeyAuthenticationHandlerTests.cs#L136)

- End-to-end proof against a real `WebApplicationFactory` pipeline, not a mock — every I/O-matrix row plus the Swagger security-scheme assertion.
  [`ApiKeyAuthenticationTests.cs:73`](../../tests/BrunoVehicleHire.Integration.Tests/ApiKeyAuthenticationTests.cs#L73)

**Peripherals**

- Swashbuckle wiring replacing the Story 1.1 `AddOpenApi`/`MapOpenApi` scaffold.
  [`Program.cs:30`](../../src/BrunoVehicleHire.Api/Program.cs#L30)

- The intentional blank `ApiKey:Key` fail-safe default for non-Development environments.
  [`appsettings.json:15`](../../src/BrunoVehicleHire.Api/appsettings.json#L15)
