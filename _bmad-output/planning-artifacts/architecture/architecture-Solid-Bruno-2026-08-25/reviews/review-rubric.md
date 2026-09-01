---
reviewed: ARCHITECTURE-SPINE.md (Bruno Vehicle Hire, 2026-08-25)
against: SPEC.md, domain-model.md, stack.md (spec-bruno-vehicle-hire)
reviewer: rubric checklist pass
date: 2026-08-26
---

# Architecture Spine Review — Bruno Vehicle Hire

## Verdict: PASS WITH CONCERNS

The spine is well-constructed for what it covers: 15 ADs, each with an explicit binds/prevents/rule shape, a complete CAP-1..CAP-13 map, a memlog showing genuine "verified on web" version checks rather than bare training-data assertions, and a Deferred section that mostly reasons about *why* each item is safe to skip rather than just naming it. That said, several real divergence points that the spec/domain-model surface — and that a below-altitude implementer would have to invent an answer for — are either silent or only implicitly resolved. None of these are fatal to the assessment's grading criteria, but they are exactly the kind of gap this checklist exists to catch.

---

## 1. Divergence points fixed vs. missed

### Well-fixed
AD-1 (dependency direction), AD-2 (CQRS/MediatR), AD-4 (vertical slice), AD-5 (repository-per-aggregate), AD-6 (Guid v7), AD-8 (ProblemDetails), AD-10 (validation pipeline), AD-13 (soft-delete vs anonymize), AD-14 (PagedResult<T>), AD-15 (aggregate mutation) are all concrete, unambiguous, and map directly onto a domain-model or stack.md requirement. A second implementer handed only these rules would land in the same place as the first. Good.

### Missed or under-specified divergence points

**(a) Booking "cancel" vs "delete" semantics — real ambiguity, not just missing detail.**
SPEC CAP-3 says "create, view, and **cancel** bookings"; domain-model.md says "Can **delete** only if the booking is in the future" and separately gives Booking a `Status` enum including `Cancelled`. The spine never states whether the delete-guard operation performs a physical row delete or a `Booking.Cancel()` status transition. AD-15 uses `Booking.Cancel()` as an illustrative example of aggregate-method mutation, which *implies* the answer, but nothing binds CAP-3 to that resolution as a rule. Two implementers reading SPEC.md/domain-model.md literally ("delete") vs. the spine's example ("Cancel()") could genuinely diverge — one hard-deletes booking rows (breaking the FK/audit trail the domain model clearly wants), the other sets Status=Cancelled. This should be an explicit AD, not an incidental example.

**(b) `DateRange.Overlaps` boundary semantics — the exact bug AD-9 claims to prevent, left open.**
AD-9's stated "Prevents" is "time-of-day/timezone ambiguity causing off-by-one overlap bugs." Switching to `DateOnly` removes the timezone axis, but the actual off-by-one question — is a booking ending 2026-09-01 considered overlapping with one starting 2026-09-01 (i.e., is the range `[Start, End)` or `[Start, End]`)? — is never answered anywhere in the spine, domain-model, or SPEC. This is precisely the divergence AD-9 exists to close, and it isn't closed. Needs one sentence added to AD-7 or AD-9, e.g. "Overlaps ⇔ StartA < EndB && StartB < EndA (end date is checkout day, exclusive)."

**(c) Booking `Status: Active → Completed` transition — entire mechanism unaddressed.**
Nothing states how/when a booking becomes `Completed`: a background job/hosted service that sweeps past-EndDate active bookings, a computed value never persisted, or a lazy check-on-read. This changes what "Status" even means for querying/filtering (CAP-4) and is a real architectural fork (stateful vs. derived), not a story-level detail.

**(d) Vehicle soft-delete exclusion pattern is referenced, never stated.**
AD-13 says Customer soft-delete "uses an EF Core global query filter on IsDeleted (same pattern as Vehicle)" — but no AD anywhere actually establishes that Vehicle rule. It's assumed prior context that was never written down. Given "cannot book a soft-deleted vehicle" is called out in SPEC.md as a non-negotiable rule, it deserves its own explicit line (even one sentence) rather than being inferred from a Customer-side aside.

**(e) API key transport contract (header name / scheme) — unspecified.**
AD-11 fixes the *implementation pattern* (custom `AuthenticationHandler`, not middleware) but not the actual wire contract: header name (e.g. `X-Api-Key` vs `Authorization: ApiKey ...`), scheme name, or where the Angular interceptor sources the key from at build time. This is exactly the kind of contract that two independently-built halves (API vs. Angular interceptor, per stack.md's own separation) must agree on byte-for-byte, and nothing in the spine pins it.

**(f) Overlap check concurrency / race condition — no DB-level backstop.**
AD-7 is purely an application-level check-then-decide pattern (load active bookings, then `DateRange.Overlaps`). SPEC.md treats "no booking overlap" as non-negotiable ("violating any is treated as an assessment failure"), yet nothing addresses two concurrent booking requests for the same vehicle racing past the load-then-insert window. A DB-level safeguard (unique/exclusion constraint on Postgres, e.g. `EXCLUDE USING gist`, or a serializable transaction) isn't mentioned, nor is the risk explicitly accepted as out of scope given single-user local demo conditions. Worth at minimum a one-line risk acceptance.

**(g) Duplicate-key errors (RegistrationNumber, Email) not routed through AD-8.**
stack.md mandates unique constraints on Vehicle.RegistrationNumber and Customer.Email. AD-8's ProblemDetails mapping only explicitly covers "domain business-rule violations" (409) and FluentValidation failures (400) — a DB-level unique-constraint violation is neither, unless there's an app-layer pre-check query. Whether a duplicate-key failure becomes a translated `DbUpdateException` → 409, or a pre-check validator → 400, is left to whoever writes the handler. Minor but concrete.

**(h) Search/filter request contract for CAP-4 — response shape fixed (AD-14), request shape isn't.**
AD-14 nails the paginated *response* envelope, but nothing fixes the filter *request* convention (per-entity query-string parameters vs. a shared filter-DTO pattern). Lower stakes than (e)/(f) since it's arguably story-level, but flagged since CAP-4 is explicitly bound only to AD-14 in the capability map, which slightly overstates AD-14's coverage.

**(i) "Centralized API client" (stack.md) vs. "feature modules own their own API calls" (spine's Design Paradigm) — unreconciled.**
stack.md's Architecture expectations require a "Centralized API client." The spine's own Design Paradigm section says frontend feature modules mirror backend vertical slices, "each owning its own API calls, queries, and components." These aren't necessarily contradictory (a shared HttpClient/interceptor config in `core/` with per-feature typed service wrappers is a standard reconciliation), but the spine never states that reconciliation explicitly, and a reader could implement either a single monolithic ApiClient service or fully independent per-feature HTTP calls with no shared abstraction. Worth one clarifying sentence.

---

## 2. Enforceability of AD Rules

Most Rules are either compiler-enforced (AD-1's Domain/Application reference chain, AD-6/AD-9's type choices, AD-15's private-setter pattern) or framework-structurally-enforced (AD-8, AD-10, AD-12's converter, AD-14's DTO shape). Two exceptions:

- **AD-1's "Api never calls Infrastructure types directly — only via interfaces resolved through DI"** is not actually enforceable by the project-reference graph as drawn: the Structural Seed's own mermaid diagram shows `Api -.DI composition root.-> Infrastructure`, meaning the Api project *does* reference the Infrastructure assembly (it must, to register services in `Program.cs`). Nothing stops a developer from `new`-ing an Infrastructure type inside a controller — it will compile fine. The Rule is real but currently enforced only by code-review discipline, not by architecture. Recommend adding one sentence: an architecture fitness test (e.g., NetArchTest.Rules / ArchUnitNET) asserting "no type in `Api.Controllers` namespace references `Infrastructure.*`", run in CI or at minimum as a unit test — otherwise AD-1's headline claim ("Api never calls Infrastructure types directly") is aspirational, not enforced.

- **AD-12's Data Protection API choice has an unaddressed key-persistence risk that undermines its own success criterion.** ASP.NET Core's `IDataProtector`/Data Protection key ring is ephemeral by default outside of a stable deployment target — in a Docker container without an explicitly configured, volume-mounted (or otherwise durable) key-storage location, the key ring regenerates on every container restart. If that happens here, every previously-encrypted `Customer.Email`/`PhoneNumber` becomes permanently undecryptable garbage after a `docker-compose down/up` cycle — directly breaking the Success Signal's explicit requirement that "a soft-deleted customer can be restored with original PII intact." AD-12 should add: persist Data Protection keys to a mounted volume/local directory (`PersistKeysToFileSystem`) rather than relying on ephemeral defaults. This is a correctness gap, not a stylistic one — it's the kind of thing that fails silently until a demo restart.

---

## 3. Deferred section — safe to defer?

- **Domain Events, Redis, rate limiting** — correctly and safely deferred; none creates cross-unit divergence risk given SPEC's explicit non-goals.
- **Un-anonymize/PII recovery window** — correctly rejected with reasoning, not just deferred.
- **Deployment & environments / CI-CD / infra-provider strategy** — this is the operational/environmental envelope the checklist calls out by name, and it *is* explicitly addressed (not silently dropped): "Local-only via docker-compose... no environment promotion, no cloud provider decision needed," which is a legitimate decision, correctly traced to SPEC.md's own non-goals. This item passes.

No Deferred entry currently creates a two-units-diverge risk on its own. The risk instead comes from things that were never listed under Deferred *or* Decided at all (see §4).

---

## 4. Dimensions left completely silent (not even in Deferred)

The checklist calls special attention to a whole dimension going unaddressed. Two clear instances here:

**(a) HTTPS enforcement.** SPEC.md Constraints states flatly: "HTTPS is enforced for all API traffic." This is a non-negotiable constraint, yet it appears nowhere in the spine — no AD, no Consistency Convention row, no Deferred note. Kestrel dev-cert handling, `UseHttpsRedirection()`, and how Angular's dev server talks to an HTTPS-only local API are all real, divergence-prone choices that a below-altitude builder has to invent unassisted. This should be at minimum a one-line Consistency Convention entry.

**(b) Testing strategy/tooling.** CAP-9 is a graded capability with a hard numeric minimum (2 command tests, 2 query tests, 1 business-rule test), yet the spine's Structural Seed tree shows only `src/` and `frontend/src/app/` — no `tests/` folder at all — and no AD or convention names a test framework (xUnit/NUnit/MSTest), a mocking approach for `IBookingRepository`/`IVehicleRepository` (Moq/NSubstitute/hand-written fakes), or an integration-test strategy against Postgres (Testcontainers vs. EF Core InMemory, the latter being explicitly unsuited to verifying relational constraints like the unique/FK rules stack.md mandates). Given CAP-9 is explicitly graded and explicitly bound to AD-5/AD-7 in the capability map (which govern production code shape, not test infrastructure), this is a genuine "whole dimension left silent" finding.

**(c) Migration application / seed-data mechanism (minor-moderate).** CAP-10's success criterion is "seed data loads cleanly on a fresh database," and SPEC assumptions name Bogus for synthetic PII generation, but nothing decides *how* migrations get applied (manual `dotnet ef database update` vs. `Database.Migrate()` on startup) or where seeding lives (EF `HasData()` in a migration vs. a startup `IHostedService`/seeder class). Two implementations could diverge in a way that affects the literal "clone and run" reviewer experience the Success Signal promises.

**(d) CORS (minor).** Angular dev server and .NET API run on different local ports; nothing mentions a CORS policy. Likely resolved trivially in code, but it's a real local-dev integration point currently unaddressed anywhere in the spine. Lower priority than (a)–(c), noted for completeness.

---

## 5. Named tech currency

This is the strongest part of the spine. The companion `.memlog.md` shows explicit "verified on web" / "verified current on the web" annotations against .NET 10 (LTS, Nov 2025), Angular 22 (stable, June 2026), MediatR 14.x (correctly flags the v13+ commercial-licensing change and reasons through the Community-edition eligibility), FluentValidation 12.1.1 (correctly flags `FluentValidation.AspNetCore` as deprecated, which is *why* AD-10 routes validation through a MediatR pipeline behavior instead), and Npgsql.EntityFrameworkCore.PostgreSQL 10.0.3 / PostgreSQL 18. These read as genuinely checked, not asserted from stale training-data memory — the memlog even shows the reasoning chain (e.g., "FluentValidation.AspNetCore package is deprecated, confirming the pipeline-behavior approach... as the correct current integration path"), which is a stronger signal than a bare version number would be.

Two items correctly avoid over-asserting: TanStack Query's Angular adapter and Serilog.Sinks.Grafana.Loki are both pinned as "current per npm/NuGet at build time" rather than a specific version number that would rot — appropriate hedging for fast-moving/optional dependencies rather than a false-precision claim.

No findings here.

---

## 6. Capability coverage (CAP-1..CAP-13)

The Capability → Architecture Map table explicitly covers all thirteen capabilities with governing ADs. Complete as a mapping exercise. The gaps are not in *coverage* (every CAP has a row) but in *depth* for a few rows — CAP-3's row lists AD-1/4/5/6/7/9 but, per §1 above, doesn't actually pin the cancel/delete semantics, the Completed-transition mechanism, or the overlap boundary; CAP-4's row cites only AD-14 (response shape) with no request/filter-contract AD; CAP-9's row cites AD-5/AD-7, which govern production-code testability, not test infrastructure itself (§4b).

---

## Summary of findings by severity

**Should fix before build starts (moderate-to-real divergence/correctness risk):**
1. Booking cancel-vs-delete semantics — pin as an explicit AD (physical delete is very likely wrong given the Status enum and audit expectations).
2. `DateRange.Overlaps` boundary inclusivity — one sentence closes the exact bug class AD-9 exists to prevent.
3. AD-12 Data Protection key persistence — add explicit durable key-ring storage, or the PII-restore success criterion can silently fail on container restart.
4. Testing strategy/tooling (framework, mocking, Postgres-integration approach) — currently a fully silent dimension despite CAP-9 being graded with a hard minimum.
5. HTTPS enforcement — a SPEC.md non-negotiable constraint with zero coverage anywhere in the spine.

**Worth a line each, lower urgency:**
6. API key header/scheme name — pin the wire contract, not just the implementation pattern.
7. Vehicle soft-delete query-filter — state it directly instead of only referencing it from AD-13's Customer aside.
8. Booking Status Active→Completed transition mechanism.
9. Concurrency backstop (or explicit risk acceptance) for the overlap check.
10. Duplicate-key (RegistrationNumber/Email) → HTTP error mapping.
11. "Centralized API client" vs. per-feature API ownership — reconcile in one sentence.
12. Migration-apply / seed-data mechanism.
13. CORS policy for local dev.

**No findings:**
- Capability coverage (CAP-1..CAP-13 mapping) — complete.
- Named tech currency — well-verified, appropriately hedged where it should be.
- Deployment/environment/infra dimension — explicitly and correctly decided as local-only, not silently dropped.
