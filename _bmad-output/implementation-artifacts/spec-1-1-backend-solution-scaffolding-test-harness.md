---
title: 'Story 1.1: Backend Solution Scaffolding & Test Harness'
type: 'feature'
created: '2026-09-01'
status: 'done'
review_loop_iteration: 1
context: ['{project-root}/_bmad-output/implementation-artifacts/epic-1-context.md']
baseline_commit: 'ff632150e5b6f35454b0a4b05dc07ce31c45b26c'
---

<frozen-after-approval reason="human-owned intent — do not modify unless human renegotiates">

## Intent

**Problem:** The repository has no application code yet — no backend solution, no test projects, and no mechanical enforcement of the mandated Clean Architecture layering (only documentation of it).

**Approach:** Scaffold a 4-project Clean Architecture .NET 10 solution (`Domain`/`Application`/`Infrastructure`/`Api`) plus three matching test projects, wire an automated dependency-direction fitness test (NetArchTest.Rules), and prove the whole TDD harness works end-to-end with one passing example test per test project.

## Boundaries & Constraints

**Always:** `Domain` has zero project/package references beyond the .NET BCL. `Application` references only `Domain`. `Infrastructure` and `Api` each reference `Application` + `Domain`. Every project targets `net10.0`. Naming follows `BrunoVehicleHire.<Layer>` exactly. The fitness test must first be proven to actually fail against a deliberate violation before being proven to pass against the clean scaffold (TDD, not a trivially-green check).

**Ask First:** Nothing expected to trigger — this is fully-specified scaffolding with no open design decisions.

**Never:** No feature code (no `Vehicle` entity, no EF Core, no controllers, no MediatR) — those belong to Stories 1.2 onward. No NuGet packages beyond xUnit, NSubstitute, FluentAssertions, and NetArchTest.Rules — do not pre-install MediatR/EF Core/FluentValidation/Serilog speculatively.

</frozen-after-approval>

## Code Map

- `BrunoVehicleHire.sln` -- new solution file at repo root, references all 7 projects
- `src/BrunoVehicleHire.Domain/BrunoVehicleHire.Domain.csproj` -- new classlib, net10.0, zero references
- `src/BrunoVehicleHire.Application/BrunoVehicleHire.Application.csproj` -- new classlib, references Domain only
- `src/BrunoVehicleHire.Infrastructure/BrunoVehicleHire.Infrastructure.csproj` -- new classlib, references Application + Domain
- `src/BrunoVehicleHire.Api/BrunoVehicleHire.Api.csproj` -- new webapi project (empty, no real controllers yet), references Application + Domain
- `tests/BrunoVehicleHire.Domain.Tests/BrunoVehicleHire.Domain.Tests.csproj` -- new xunit project, references Domain
- `tests/BrunoVehicleHire.Application.Tests/BrunoVehicleHire.Application.Tests.csproj` -- new xunit project, references Application + NSubstitute + FluentAssertions
- `tests/BrunoVehicleHire.Integration.Tests/BrunoVehicleHire.Integration.Tests.csproj` -- new xunit project, references Api + Infrastructure + NetArchTest.Rules 1.3.2 (current, verified .NET-10-compatible)

## Tasks & Acceptance

**Execution:**
- [x] `BrunoVehicleHire.sln` -- `dotnet new sln` then `dotnet sln add` for all 7 projects -- single solution entry point
- [x] `src/BrunoVehicleHire.Domain/` -- `dotnet new classlib -f net10.0` -- future entities/value objects live here, must stay dependency-free
- [x] `src/BrunoVehicleHire.Application/` -- `dotnet new classlib -f net10.0`, `dotnet add reference` to Domain -- future CQRS handlers
- [x] `src/BrunoVehicleHire.Infrastructure/` -- `dotnet new classlib -f net10.0`, reference Application + Domain -- future EF Core/repositories
- [x] `src/BrunoVehicleHire.Api/` -- `dotnet new webapi -f net10.0`, reference Application + Domain; delete the template's default `WeatherForecast.cs` and its controller -- future thin controllers only
- [x] `tests/BrunoVehicleHire.Domain.Tests/` -- `dotnet new xunit -f net10.0`, reference Domain; add one trivial passing example test
- [x] `tests/BrunoVehicleHire.Application.Tests/` -- `dotnet new xunit -f net10.0`, reference Application, add NSubstitute + FluentAssertions packages; add one trivial passing example test
- [x] `tests/BrunoVehicleHire.Integration.Tests/` -- `dotnet new xunit -f net10.0`, reference Api + Infrastructure, add NetArchTest.Rules 1.3.2; add one trivial passing example test
- [x] `tests/BrunoVehicleHire.Integration.Tests/ArchitectureFitnessTests.cs` -- write the dependency-direction fitness test first against a deliberately-violating dummy type, confirm it fails, then confirm it passes against the real (clean) scaffold -- TDD proof the check actually detects the real rule, not a trivially-green assertion
- [x] `.gitignore` -- extend with standard .NET ignores (`bin/`, `obj/`, `.vs/`) -- these projects didn't exist before this story; nothing to ignore until now
- [x] `tests/BrunoVehicleHire.Integration.Tests/ArchitectureFitnessTests.cs` -- add `Domain_Should_Not_Have_Any_BrunoVehicleHire_Dependencies` (assert no type in `BrunoVehicleHire.Domain` depends on `BrunoVehicleHire.Application`/`.Infrastructure`/`.Api`) and `Application_Should_Not_Depend_On_Infrastructure_Or_Api` (assert no type in `BrunoVehicleHire.Application` depends on `BrunoVehicleHire.Infrastructure`/`.Api`) -- the spec's "Always" section states all three layering rules as invariants, but only the Controllers→Infrastructure rule had automated enforcement; a reviewer proved this gap by adding an unrelated NuGet package directly to `Domain` and confirming `dotnet build`/`dotnet test` stayed fully green. Same TDD discipline as the existing rule: prove each new rule fails against a deliberate violation first, then passes clean
- [x] `global.json` -- pin the .NET SDK to the installed `10.0.204` with a `rollForward` policy -- this machine has .NET 7/8/10 SDKs installed side by side; without a pin, `dotnet build`/`dotnet test` could silently resolve to a different SDK on another machine or CI
- [x] `tests/BrunoVehicleHire.Integration.Tests/ArchitectureFitnessTests.cs` -- remove the redundant `using System.Linq;` and `using Xunit;` directives -- both are already covered by `ImplicitUsings` and the csproj's global `<Using Include="Xunit" />`

**Acceptance Criteria:**
- Given a fresh clone, when `dotnet build` is run at the solution root, then all 7 projects compile with zero errors
- Given the project reference graph, when inspected, then `Domain` has zero references, `Application` references only `Domain`, and `Infrastructure`/`Api` each reference `Application` + `Domain`
- Given `ArchitectureFitnessTests`, when `dotnet test` is run, then the fitness test passes (asserting no type in `BrunoVehicleHire.Api.Controllers` depends on `BrunoVehicleHire.Infrastructure.*`), and one example test in each of the three test projects also passes
- Given the fitness test's rule is temporarily inverted during development (a dummy `Api.Controllers` type given a fake `Infrastructure` dependency), when run as a sanity check, then it fails -- proving the assertion is real, not vacuous -- before being reverted to its passing, correct form
- Given a dummy type or package reference is temporarily added to `Domain` or `Application` that violates their stated dependency limits, when the two new fitness tests are run as a sanity check, then each fails -- proving they are real, not vacuous -- before being reverted
- Given `global.json` is present, when `dotnet --version` is run in the repo root, then it resolves to the pinned SDK version

## Spec Change Log

- Implementation note: `dotnet new sln` on the .NET 10 SDK defaults to the new XML `.slnx` format. Regenerated with `-f sln` to produce the classic `BrunoVehicleHire.sln` named in the Code Map, since the spec names that exact file.
- Implementation note: pinned `FluentAssertions` to `7.2.2` (last MIT-licensed release) rather than the latest `8.x`, which requires a paid commercial license (via Xceed) for for-profit use. The spec named the package without a version pin; NetArchTest.Rules was the only package with an explicit pin. Flagging this choice since it affects licensing cost exposure for a commercial product.
- Review finding: the initial build carried 4 `NU1903` warnings for a **high-severity** (CVSS 7.5) vulnerability in `Microsoft.OpenApi` 2.0.0 (GHSA-v5pm-xwqc-g5wc, circular-schema-reference stack overflow), pulled in transitively by the .NET 10 webapi template's default `Microsoft.AspNetCore.OpenApi` package. A patched version (2.7.5+) exists, so an explicit `Microsoft.OpenApi` 2.7.5 `PackageReference` was added to `Api` and `Integration.Tests` to override the vulnerable transitive version — a security-motivated version pin on an already-present dependency, not new feature scope. Rebuilt clean: 0 warnings, 0 errors, all 4 tests still passing.
- **Loopback 1 (bad_spec).** Triggering finding: three review layers (blind-hunter, edge-case-hunter, verification-gap) independently flagged that the fitness test suite only enforces the Controllers→Infrastructure rule, leaving the "Always" section's other two stated invariants (`Domain` zero deps, `Application` deps only on `Domain`) with zero automated coverage. Verification-gap proved this concretely: added `Newtonsoft.Json` directly to `Domain.csproj`, `dotnet build`/`dotnet test` stayed fully green, then reverted. **Amended:** added two new tasks/ACs for the missing fitness tests, plus a `global.json` SDK pin and a trivial redundant-`using` cleanup (folded in rather than spending a second loop on them). **Known-bad state avoided:** a future story silently violating Domain/Application layering (e.g. adding MediatR or EF Core to the wrong project) would have shipped green with no test failure. **KEEP instructions:** the existing solution/project structure, the full reference graph, all package versions already chosen (xunit 2.9.3, xunit.runner.visualstudio 3.1.4, NSubstitute 6.2.0, FluentAssertions 7.2.2, NetArchTest.Rules 1.3.2, Microsoft.OpenApi 2.7.5), the `WeatherForecast` template-file deletion, and the existing `Controllers_Should_Not_Depend_On_Infrastructure` fitness test with its TDD red-then-green proof are all correct and must NOT be regenerated or redone — this amendment is additive only.

## Design Notes

`dotnet new webapi` scaffolds a sample `WeatherForecast.cs` endpoint and controller by default — these must be deleted; this story ships an empty, clean `Api` project with no real controllers (those arrive with Story 1.7's `GetVehiclesQuery` endpoint). Keep NuGet surface area minimal: only the four test-tooling packages named above land in this story.

## Verification

**Commands:**
- `dotnet build` -- expected: 7/7 projects build, 0 errors
- `dotnet test` -- expected: 6 tests total (3 fitness tests + 1 example test × 3 test projects), all passing -- updated from the original 4 after Loopback 1 added the two new fitness tests

## Suggested Review Order

**Architecture enforcement (the point of this story)**

- Entry point: three reflection-based rules replace documentation with mechanical proof — start here to see what's actually enforced.
  [`ArchitectureFitnessTests.cs:37`](../../tests/BrunoVehicleHire.Integration.Tests/ArchitectureFitnessTests.cs#L37)

- Application may depend on Domain only — the second half of the layering rule the review loop added.
  [`ArchitectureFitnessTests.cs:56`](../../tests/BrunoVehicleHire.Integration.Tests/ArchitectureFitnessTests.cs#L56)

- The original rule from the first pass, kept unchanged per the KEEP instructions.
  [`ArchitectureFitnessTests.cs:21`](../../tests/BrunoVehicleHire.Integration.Tests/ArchitectureFitnessTests.cs#L21)

**Dependency graph (what the fitness tests are proving)**

- Domain has zero references — the innermost layer, verify it stayed clean.
  [`BrunoVehicleHire.Domain.csproj`](../../src/BrunoVehicleHire.Domain/BrunoVehicleHire.Domain.csproj)

- Application references Domain only.
  [`BrunoVehicleHire.Application.csproj:4`](../../src/BrunoVehicleHire.Application/BrunoVehicleHire.Application.csproj#L4)

- Infrastructure and Api both reference Application + Domain, never each other.
  [`BrunoVehicleHire.Infrastructure.csproj:4`](../../src/BrunoVehicleHire.Infrastructure/BrunoVehicleHire.Infrastructure.csproj#L4)
  [`BrunoVehicleHire.Api.csproj:14`](../../src/BrunoVehicleHire.Api/BrunoVehicleHire.Api.csproj#L14)

**Tooling & security**

- SDK pin added during review — prevents silent drift across the multiple .NET SDKs installed on this machine.
  [`global.json`](../../global.json)

- Security-patched `Microsoft.OpenApi` pin overriding a high-severity transitive vulnerability from the webapi template.
  [`BrunoVehicleHire.Api.csproj:10`](../../src/BrunoVehicleHire.Api/BrunoVehicleHire.Api.csproj#L10)

**Peripherals**

- Minimal Api entry point, template sample endpoint already removed.
  [`Program.cs:1`](../../src/BrunoVehicleHire.Api/Program.cs#L1)

- Placeholder tests proving each test project's harness/tooling wiring works, replaced by real tests starting Story 1.3.
  [`ExampleTests.cs:11`](../../tests/BrunoVehicleHire.Domain.Tests/ExampleTests.cs#L11)
  [`ExampleTests.cs:11`](../../tests/BrunoVehicleHire.Application.Tests/ExampleTests.cs#L11)

- Standard .NET build-artifact exclusions.
  [`.gitignore:4`](../../.gitignore#L4)
