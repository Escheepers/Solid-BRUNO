---
title: 'Story 1.3: Vehicle Domain Model (TDD)'
type: 'feature'
created: '2026-09-02'
status: 'done'
review_loop_iteration: 0
context: ['{project-root}/_bmad-output/implementation-artifacts/epic-1-context.md']
baseline_commit: 'a3caa5160aec789d8541a93d0f210eaaf39851ad'
---

<frozen-after-approval reason="human-owned intent — do not modify unless human renegotiates">

## Intent

**Problem:** `Vehicle` (from Story 1.2) is a bare property bag with public setters — nothing stops it from existing in an invalid state, and none of its business rules live in the entity itself.

**Approach:** Enrich the same `Vehicle.cs` in place with a `Create(...)` factory enforcing its invariants, private setters, and a `SoftDelete()` method — built test-first. No new migration: the column shape is unchanged, only C# access/behavior changes.

## Boundaries & Constraints

**Always:** `Vehicle.Create(...)` is the only way to construct a valid instance; it validates `RegistrationNumber`/`Make`/`Model` are non-blank, `DailyRate` is positive, and `Year` falls in a plausible range (1900 to next calendar year) — throwing a new `DomainRuleViolationException` (introduced here in `Domain`, consumed generically once Story 1.5 builds the global exception handler) on violation. `Id` is assigned via `Guid.CreateVersion7()` at construction (AD-6). `CreatedDate` is captured via an injectable `TimeProvider` (defaulting to `TimeProvider.System`) so tests can assert an exact value rather than "close to now". `SoftDelete()` sets `IsDeleted` and is idempotent (safe to call twice). Every rule is proven test-first: a failing test exists before the implementation that makes it pass. No entity property keeps a public setter.

**Ask First:** Nothing expected to trigger — the invariants are straightforward and the `TimeProvider` pattern is standard .NET 8+.

**Never:** No `Restore()` method (Epic 2's job, once restore is a real user-facing need). No repository, MediatR, or Application-layer code. No FluentValidation — that's a separate, Application-layer concern starting Story 2.1; this story's checks are domain invariants, not a replacement for input-shape validation.

## I/O & Edge-Case Matrix

| Scenario | Input / State | Expected Output / Behavior | Error Handling |
|----------|--------------|---------------------------|----------------|
| Valid input | Non-blank RegistrationNumber/Make/Model, positive DailyRate, plausible Year | `Vehicle` created: `Id` is a v7 Guid, `IsDeleted` false, `CreatedDate` equals the injected `TimeProvider`'s current time exactly | N/A |
| Blank `RegistrationNumber`, `Make`, or `Model` | Empty/whitespace string | Rejected at construction | `DomainRuleViolationException` |
| Non-positive `DailyRate` | Zero or negative | Rejected at construction | `DomainRuleViolationException` |
| Implausible `Year` | e.g. 1899, or two years from now | Rejected at construction | `DomainRuleViolationException` |
| `SoftDelete()` called twice | Already-`IsDeleted` vehicle | `IsDeleted` stays true; no exception | N/A (idempotent, not an error case) |

</frozen-after-approval>

## Code Map

- `src/BrunoVehicleHire.Domain/Vehicle.cs` -- modify -- add `Create(...)` factory, private constructor for EF Core, private setters, `SoftDelete()`
- `src/BrunoVehicleHire.Domain/Exceptions/DomainRuleViolationException.cs` -- new -- minimal exception carrying `Entity`/`Rule`/message, the shared type every future domain rule (Customer, Booking) will also throw
- `tests/BrunoVehicleHire.Domain.Tests/ExampleTests.cs` -- delete -- placeholder retired, replaced by real tests below
- `tests/BrunoVehicleHire.Domain.Tests/VehicleTests.cs` -- new -- covers every row of the I/O matrix, test-first
- `tests/BrunoVehicleHire.Integration.Tests/VehicleMigrationTests.cs` -- modify -- its two `new Vehicle { ... }` object-initializers (lines ~112, ~125) no longer compile once setters go private; switch both to `Vehicle.Create(...)`

## Tasks & Acceptance

**Execution:**
- [x] `src/BrunoVehicleHire.Domain/Exceptions/DomainRuleViolationException.cs` -- minimal exception type (entity name, rule name, message) -- needed now so `Create(...)` has something meaningful to throw; Story 1.5's global handler will map it to 409 generically later
- [x] `tests/BrunoVehicleHire.Domain.Tests/VehicleTests.cs` -- write each failing test first (valid-input success, each invalid-input rejection, idempotent `SoftDelete`), confirm each fails, then implement `Vehicle.Create(...)`/`SoftDelete()` in `src/BrunoVehicleHire.Domain/Vehicle.cs` to make them pass one at a time
- [x] `src/BrunoVehicleHire.Domain/Vehicle.cs` -- private parameterless constructor for EF Core materialization; private full constructor called by `Create(...)`; all properties `{ get; private set; }`
- [x] `tests/BrunoVehicleHire.Domain.Tests/ExampleTests.cs` -- delete
- [x] `tests/BrunoVehicleHire.Integration.Tests/VehicleMigrationTests.cs` -- update both `new Vehicle { ... }` call sites to `Vehicle.Create(...)`, keeping the same test data/intent

**Acceptance Criteria:**
- Given `Vehicle.Create(...)` with valid input, when called, then it returns a `Vehicle` with a v7-Guid `Id`, `IsDeleted == false`, and `CreatedDate` matching the injected `TimeProvider` exactly
- Given any of the invalid-input scenarios in the I/O matrix, when `Create(...)` is called, then a `DomainRuleViolationException` is thrown and no instance is returned
- Given `Vehicle`, when inspected, then no property has a public setter and the only way to construct one is `Create(...)`
- Given `SoftDelete()` is called on an already-soft-deleted vehicle, when called again, then `IsDeleted` remains true and no exception is thrown
- Given `VehicleMigrationTests` (Story 1.2), when `dotnet test` is run after this story's changes, then all of them still pass unchanged in intent, now constructing `Vehicle` via `Create(...)`

## Spec Change Log

## Design Notes

The `Year` plausible-range check (1900–next calendar year) and the choice to make `SoftDelete()` idempotent rather than throwing on a repeat call are both judgment calls with no explicit spec citation — reasonable, low-risk defaults rather than open questions worth blocking on. `TimeProvider` (built into .NET 8+) is used instead of a custom clock abstraction; a minimal test-double subclass in `VehicleTests.cs` is enough to assert exact `CreatedDate` values without adding a mocking package to `Domain.Tests`.

`DomainRuleViolationException` is introduced now, but nothing catches or maps it to an HTTP response yet — that's Story 1.5. Keep it minimal (no dependency on ASP.NET Core types) since it lives in `Domain`, which must stay framework-free.

## Verification

**Commands:**
- `dotnet build BrunoVehicleHire.sln` -- expected: 7/7 projects build, 0 errors, 0 warnings
- `dotnet test BrunoVehicleHire.sln` -- expected: all prior tests still pass, plus the new `VehicleTests` (one per I/O matrix row); total count increases from the current 10 by the number of new `VehicleTests` cases, minus the one deleted `ExampleTests` placeholder -- confirmed: 10 + 17 − 1 = 26, all passing

## Suggested Review Order

**The domain model itself (the point of this story)**

- Entry point: `Create(...)` — every invariant, the Guid v7 assignment, and the TimeProvider-driven `CreatedDate` in one place.
  [`Vehicle.cs:61`](../../src/BrunoVehicleHire.Domain/Vehicle.cs#L61)

- Private constructors and setters — confirm nothing can bypass `Create(...)`.
  [`Vehicle.cs:16`](../../src/BrunoVehicleHire.Domain/Vehicle.cs#L16)

- Idempotent `SoftDelete()` — the one post-construction mutation this story adds.
  [`Vehicle.cs:113`](../../src/BrunoVehicleHire.Domain/Vehicle.cs#L113)

- The shared exception type every future domain rule (Customer, Booking) will also throw.
  [`DomainRuleViolationException.cs:10`](../../src/BrunoVehicleHire.Domain/Exceptions/DomainRuleViolationException.cs#L10)

**Proof (test-first, not test-after)**

- The reflection check proving no property has a public setter — the AC that's easiest to silently violate later.
  [`VehicleTests.cs:153`](../../tests/BrunoVehicleHire.Domain.Tests/VehicleTests.cs#L153)

- Full I/O-matrix coverage plus a boundary case (`Year` exactly next-calendar-year) beyond the minimum asked for.
  [`VehicleTests.cs:117`](../../tests/BrunoVehicleHire.Domain.Tests/VehicleTests.cs#L117)

**Peripherals**

- The two call sites that had to change once setters went private.
  [`VehicleMigrationTests.cs:112`](../../tests/BrunoVehicleHire.Integration.Tests/VehicleMigrationTests.cs#L112)
