---
title: 'Story 4.4: Automatic Booking Completion'
type: 'feature'
created: '2026-09-14'
status: 'done'
review_loop_iteration: 0
context: []
baseline_commit: '1a3cefbaeac5943eec22d801370a2bc7c7a45843'
---

<frozen-after-approval reason="human-owned intent — do not modify unless human renegotiates">

## Intent

**Problem:** A past-EndDate booking stays `Active` forever unless a staff member happens to notice — `Status` should reflect reality automatically, without any Query handler "helpfully" writing to fix it (which would break CQRS purity, AD-2).

**Approach:** Add `Booking.Complete()` (Active → Completed, the domain rule's boundary mirrors `Cancel()`'s exactly), a `CompleteBookingCommand`/`Handler` with no HTTP endpoint (Completed is never user-settable), and a `BookingCompletionSweepService : BackgroundService` (AD-17) whose actual sweep logic is extracted into a directly-callable, unit-testable method — never proven only by waiting on a real timer.

**Scope decisions — flagged for approval:**
1. **`Booking.Complete()`'s eligibility boundary is the exact complement of `Cancel()`'s**: `Cancel()` already treats `EndDate <= today` as ineligible ("this booking is over"); `Complete()` triggers on that same `EndDate <= today` condition. One boundary, two domain methods, never redefined twice.
2. **`Complete()` is idempotent if already `Completed`** (no-op, mirrors `SoftDelete`'s precedent) but **throws if `Cancelled`** (completing a cancelled booking would misrepresent it) — defense-in-depth against the sweep racing a manual Cancel between its own read and dispatch.
3. **`CompleteBookingCommand` has no HTTP endpoint anywhere** — `BookingsController` gains nothing this story. The sweep is its only caller, dispatched internally via `ISender`. This matches `EXPERIENCE.md`'s own rule that Completed is never directly settable by a user in any form.
4. **The sweep's actual logic lives in a small, directly-testable method** (e.g. `BookingCompletionSweepService.RunSweepAsync(CancellationToken)`), constructor-injected with `TimeProvider` and an `IServiceScopeFactory` (the service itself is a singleton; `IBookingRepository`/`ISender` are scoped) — `ExecuteAsync` is a thin loop that just calls it on an `IOptions`-configured interval. Tests call `RunSweepAsync` directly with a fixed `TimeProvider`, never waiting on a real timer (the AC's own requirement).
5. **A failure completing one booking is caught and logged inside the sweep loop, not allowed to propagate** — an unhandled exception in a `BackgroundService.ExecuteAsync` can crash the whole host; one raced/failed booking must never block the rest of that sweep pass or kill the service.

## Boundaries & Constraints

**Always:**
- `Booking.Complete(TimeProvider? timeProvider = null)`: no-op if already `Completed`; throws `DomainRuleViolationException` if `Cancelled`; otherwise sets `Status = Completed`. No other field changes.
- `IBookingRepository` gains `GetActivePastEndDateAsync(DateOnly asOf, ct)` -- `WHERE Status = Active AND EndDate <= asOf`, mirroring `Cancel()`'s own boundary exactly.
- The sweep interval is configurable via `IOptions<BookingCompletionSweepOptions>` (default 15 minutes), bound from configuration -- never hardcoded (matches the architecture spine's config convention).
- Every existing Query handler (`GetVehiclesQueryHandler`, `GetCustomersQueryHandler`, `GetBookingsQueryHandler`) is re-read and confirmed to perform no write, as the AC's own literal audit requirement -- no code change expected here, just a recorded confirmation.

**Ask First:** Nothing else expected to trigger.

**Never:** No HTTP-reachable way to complete a booking. No Query handler ever gains a write. No test that waits on `BookingCompletionSweepService`'s real timer loop.

## I/O & Edge-Case Matrix

| Scenario | Input / State | Expected Output / Behavior | Error Handling |
|----------|--------------|---------------------------|----------------|
| Active booking, EndDate already passed | Sweep runs | `CompleteBookingCommand` dispatched; `Status` → `Completed` | N/A |
| Active booking, EndDate today | Sweep runs | Also completed (matches `Cancel()`'s `<=` boundary) | N/A |
| Active booking, EndDate in the future | Sweep runs | Untouched, stays `Active` | N/A |
| Already-Completed booking | Sweep runs | Never selected by the query (`Status = Active` filter); untouched | N/A |
| Cancelled booking whose EndDate has passed | Sweep runs | Never selected (`Status = Active` filter); untouched | N/A |
| One booking's completion throws mid-sweep | Simulated failure | That booking's error is logged; every other eligible booking in the same pass still completes | N/A |

## Code Map

**Domain:**
- `src/BrunoVehicleHire.Domain/Booking.cs` -- modify -- add `Complete(TimeProvider?)` per Boundaries
- `tests/BrunoVehicleHire.Domain.Tests/BookingTests.cs` -- modify -- success (EndDate today/past), already-Completed (idempotent), already-Cancelled (throws), still-eligible-future-EndDate (throws, defense-in-depth) -- written first

**Application:**
- `src/BrunoVehicleHire.Application/Bookings/Commands/CompleteBookingCommand.cs`, `CompleteBookingCommandHandler.cs` -- new -- mirrors `CancelBookingCommandHandler`'s exact shape, returns `Unit`/no HTTP surface
- `tests/BrunoVehicleHire.Application.Tests/Bookings/CompleteBookingCommandHandlerTests.cs` -- new -- success, 404, domain-exception pass-through
- `src/BrunoVehicleHire.Application/Bookings/IBookingRepository.cs`, `src/BrunoVehicleHire.Infrastructure/Repositories/BookingRepository.cs` -- modify -- add `GetActivePastEndDateAsync`

**Infrastructure:**
- `src/BrunoVehicleHire.Infrastructure/BackgroundServices/BookingCompletionSweepService.cs` -- new -- `BackgroundService` per Scope decisions 4/5; `BookingCompletionSweepOptions.cs` (interval config) alongside it
- `src/BrunoVehicleHire.Api/Program.cs` -- modify -- `builder.Services.Configure<BookingCompletionSweepOptions>(...)`, `builder.Services.AddHostedService<BookingCompletionSweepService>()`
- `src/BrunoVehicleHire.Api/appsettings.json` -- modify -- add the sweep's config section with its default interval

**Tests:**
- `tests/BrunoVehicleHire.Integration.Tests/BookingCompletionSweepServiceTests.cs` -- new -- calls `RunSweepAsync` directly against a real Testcontainers Postgres with a fixed `TimeProvider` injected: eligible booking completes, future-EndDate booking untouched, already-Cancelled/Completed untouched, and the per-booking-failure-doesn't-block-the-rest case

## Tasks & Acceptance

**Execution (TDD throughout):**
- [x] `Booking.Complete()` -- write failing Domain.Tests first, then implement
- [x] `IBookingRepository.GetActivePastEndDateAsync`, `CompleteBookingCommand`/`Handler` -- write failing tests first
- [x] `BookingCompletionSweepService` + `RunSweepAsync` -- write failing integration tests first, using a fixed `TimeProvider`, never a real timer
- [x] Register the hosted service + `IOptions` config in `Program.cs`/`appsettings.json`
- [x] Re-read `GetVehiclesQueryHandler`/`GetCustomersQueryHandler`/`GetBookingsQueryHandler` and confirm none perform a write (the AC's literal audit) -- confirmed, none call `SaveChangesAsync` or any mutator
- [x] Full `dotnet test`; `ArchitectureFitnessTests`; force `dotnet restore`, check `NU1903`; explicit SOLID/DRY/YAGNI self-check -- confirmed independently: 386/386 passing, 0 advisories

**Acceptance Criteria (epics.md, verbatim intent):**
- Given an Active booking whose EndDate is in the past, when the sweep's timer fires, a `CompleteBookingCommand` is dispatched and `Booking.Complete()` transitions `Status` to `Completed`
- Given `Booking.Complete()` and the sweep service, both are implemented test-first -- `Complete()` as a pure domain-method unit test, the sweep via an injected/fake time source, never a real timer
- Given the codebase, no Query handler performs a write -- the sweep's Command is the only path that ever sets `Status: Completed`

## Spec Change Log

- **Interpretation confirmation**: the Boundaries' "Always" bullet for `Complete()` was terser than the Scope decisions/Code Map about a third branch -- whether `Complete()` should throw if called on a booking whose `EndDate` is still in the future (defense-in-depth, symmetric with `Cancel()`'s own `EndDate <= today` check). The implementer correctly inferred this from Scope decision 1 ("one boundary, two domain methods, never redefined twice") and the Code Map's explicit test list ("still-eligible-future-EndDate (throws, defense-in-depth)"), and flagged the interpretation for confirmation rather than guessing silently. Confirmed correct on review -- this is exactly the intended design; the Boundaries bullet was just incomplete prose, not a different intent.
- **`src/BrunoVehicleHire.Infrastructure/BrunoVehicleHire.Infrastructure.csproj`** (not in the Code Map) needed explicit `PackageReference`s to `Microsoft.Extensions.Hosting.Abstractions` (for `BackgroundService`), `Microsoft.Extensions.Options` (for `IOptions<T>`), `Microsoft.Extensions.DependencyInjection.Abstractions` (for `IServiceScopeFactory`), and `MediatR` (for `ISender`) -- all pinned to versions already transitively resolved elsewhere in the solution. A necessary consequence of `BookingCompletionSweepService` living in Infrastructure per AD-17's own placement, not a scope change.
- **`tests/BrunoVehicleHire.Integration.Tests/BrunoVehicleHire.Integration.Tests.csproj`** needed an explicit `MediatR` package reference so `BookingCompletionSweepServiceTests`'s hand-built `ServiceCollection` could register the real MediatR pipeline (including a test-only `IPipelineBehavior` for the mid-sweep-failure case) without going through `WebApplicationFactory`.

## Design Notes

**Why `RunSweepAsync` is a separate method rather than testing `ExecuteAsync` directly:** `ExecuteAsync`'s own loop (`RunSweepAsync` then `Task.Delay(interval)`, repeat) is deliberately left without a dedicated unit test -- proving the timer mechanics themselves work would require either waiting on a real interval or mocking `Task.Delay`, both of which the AC explicitly steers away from. `RunSweepAsync` carries all the logic actually worth testing (which bookings get completed) and is directly callable without touching the loop at all.

</frozen-after-approval>

## Verification

Independently re-verified after implementation (not just the implementer's self-report):

**Backend:** `git status --short` confirmed the exact expected file set; `dotnet build BrunoVehicleHire.sln` clean (0 warnings, 0 errors); full `dotnet test` 386/386 passing (Domain 84, Application 134, Infrastructure 4, Api 23, Integration 141), up from 370 before this story; `dotnet restore --force` showed no `NU1903` advisories. Read `Booking.Complete()` directly and confirmed it's idempotent if already `Completed`, throws if `Cancelled`, and throws (defense-in-depth) if `EndDate` is still in the future -- exactly matching Scope decision 1's intent despite the Boundaries' terser prose (see Spec Change Log). Read `BookingCompletionSweepService.cs` directly and confirmed the `IServiceScopeFactory`-per-pass pattern is correct for a singleton `BackgroundService` resolving scoped dependencies, `Task.Delay` uses the injected `TimeProvider` overload, and per-booking failures are caught and logged without propagating. Read `CompleteBookingCommandHandler.cs` and confirmed it's a faithful structural mirror of `CancelBookingCommandHandler` with no HTTP endpoint anywhere in `BookingsController`.

Read `BookingCompletionSweepServiceTests.cs` directly and confirmed the reasoning to avoid `WebApplicationFactory<Program>` is sound: booting the full host would also start the real production sweep on the real system clock, racing the test's own fixed-clock sweep against the same Postgres container. The hand-built `ServiceCollection` correctly wires the real repository/handler/MediatR pipeline instead. The mid-sweep-failure test's injected `IPipelineBehavior<CompleteBookingCommand, Unit>` is a genuine, deterministic way to simulate a failure on one specific booking while the real handler chain still runs for the other -- confirmed both outcomes are asserted (the failing booking's `SaveChangesAsync` never committed; the other genuinely completed) and that `RunSweepAsync` itself never throws.

**Live end-to-end pass** (against the real API + Postgres, since this story wires a production `BackgroundService` that no automated test exercises through the actual DI registration in `Program.cs`): created a real Vehicle, Customer, and a Booking dated fully in the past (1-5 Aug 2026, "today" being 14 Sept 2026); it correctly showed `Active` immediately after creation (the API's own startup sweep had already run before this booking existed). Restarted the API -- `BookingCompletionSweepService.ExecuteAsync` calls `RunSweepAsync` immediately on startup, before its first `Task.Delay` -- and the booking's `Status` genuinely flipped to `Completed` within seconds, confirmed by reloading the Bookings list and seeing the correctly-styled neutral-gray "Completed" `Badge`. This is a stronger proof than the isolated integration tests alone: it confirms the real `Program.cs` registration (`AddSingleton(TimeProvider.System)`, `Configure<BookingCompletionSweepOptions>`, `AddHostedService<...>`) actually wires correctly end to end, not just the extracted `RunSweepAsync` logic in a hand-built test container. The dev database was left empty afterward.

**SOLID/DRY/YAGNI:** confirmed via direct code reading -- `Booking.Complete()` owns its entire eligibility rule (SRP); the handler/controller-absence/sweep-loop shapes all mirror existing patterns (`CancelBookingCommandHandler`, `IServiceScopeFactory`-per-request) rather than inventing new ones (DRY); no HTTP surface, no generic clock abstraction beyond the already-established `TimeProvider`, and no broader exception handling than the one specific per-booking case the spec asked for (YAGNI).

## Suggested Review Order

1. `src/BrunoVehicleHire.Domain/Booking.cs` -- `Complete(TimeProvider?)`
2. `src/BrunoVehicleHire.Infrastructure/BackgroundServices/BookingCompletionSweepService.cs` -- the scope-per-pass pattern and per-booking failure isolation
3. `tests/BrunoVehicleHire.Integration.Tests/BookingCompletionSweepServiceTests.cs` -- especially the `WebApplicationFactory`-avoidance reasoning and the `IPipelineBehavior`-based failure simulation
4. `src/BrunoVehicleHire.Application/Bookings/Commands/CompleteBookingCommandHandler.cs`
5. `src/BrunoVehicleHire.Api/Program.cs` -- the hosted-service/`IOptions` registration
