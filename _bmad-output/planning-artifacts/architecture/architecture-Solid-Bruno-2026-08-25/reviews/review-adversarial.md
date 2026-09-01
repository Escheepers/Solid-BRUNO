---
name: 'Adversarial Review — Bruno Vehicle Hire Architecture Spine'
type: architecture-review
reviews: '../ARCHITECTURE-SPINE.md'
against: ['../../../../specs/spec-bruno-vehicle-hire/SPEC.md', '../../../../specs/spec-bruno-vehicle-hire/domain-model.md', '../../../../specs/spec-bruno-vehicle-hire/stack.md']
method: 'adversarial two-unit divergence construction'
created: '2026-08-26'
verdict: PASS WITH CONCERNS
---

# Adversarial Review — Architecture Spine

## Method

For each AD (AD-1..AD-15), I tried to construct two units one level down (Vehicles vs. Bookings vs. Customers features; backend vs. frontend) that each satisfy the AD's rule **to the letter** while still building something incompatible: a shared-data shape clash, dual ownership of one entity, a conflicting mutation path, an inconsistent error/response shape, or naming drift the conventions table doesn't pin down.

Where a construction succeeds, it's logged below as a **hole**, rated by how much damage it could actually do to the CAP-9/CAP-10 evaluation criteria (correctness, consistency, maintainability). Where I could not construct a divergent pair, the AD is logged as **held**.

---

## Findings by AD

### AD-1 — Dependency direction — HELD

The rule, read carefully, is self-closing: because Application may only reference Domain (never Infrastructure), any repository/service interface an Application handler consumes is *forced* to live in Application or Domain — there's no legal place for Infrastructure to define an interface Application could see. I could not construct a Vehicles-vs-Bookings pair that both obey AD-1 and disagree on where interfaces live. Held.

### AD-2 — CQRS via MediatR — HOLE (Moderate)

**Divergence:** `CreateVehicleCommand : IRequest<Guid>` (handler returns only the new id — caller refetches via a Query) vs. `CreateBookingCommand : IRequest<BookingDto>` (handler returns the fully materialized DTO). Both are legal `IRequest`/`IRequestHandler` pairs satisfying AD-2's letter — "every write is an IRequest command... one handler." Nothing in AD-2, AD-14, or the conventions table says what a write handler must *return*.

**Damage:** the Angular side (AD-3, TanStack Query) needs a uniform answer to "after a mutation succeeds, do I have the new/updated entity in hand, or must I invalidate-and-refetch?" With two different contracts, the frontend either writes two different mutation-completion strategies per feature (drift CAP-8 is explicitly graded on) or over-fetches everywhere to stay safe.

**Fix:** add a rule (or extend AD-2) — every Command handler that creates or mutates an aggregate returns that aggregate's DTO (the same DTO shape its sibling Query returns), never a bare id or `Unit`, except delete/cancel commands which may return `Unit`/204.

### AD-3 — Angular state management — HOLE (Moderate-High)

**Divergence A (mutations):** AD-3 pins TanStack Query for "fetching, caching, and invalidation" — it never says mutations must go through TanStack Query's `mutate`. The Vehicles feature could implement create/edit/delete via TanStack Query mutations (auto cache invalidation), while the Bookings feature implements them as raw `HttpClient` calls with a hand-rolled Signal array update. Both satisfy "Signals for local/UI state; TanStack Query for server-state fetching/caching/invalidation" literally — mutations are arguably neither "fetching" nor local UI state, so the rule is silent on them. Result: Bookings' local Signal array silently desyncs from Vehicles' TanStack cache the moment a booking touches vehicle availability (a booking create should invalidate the vehicle's availability query — impossible to express uniformly if one feature isn't in the TanStack cache at all).

**Divergence B (query-key namespacing):** AD-3 never mandates a query-key convention. Two features can independently choose the string `['list']` or `['detail', id]` as a TanStack query key, silently colliding across feature boundaries (TanStack Query dedupes/caches purely by key equality — a collision serves one feature's cached response to another).

**Fix:** extend AD-3 with (a) "all server-state writes (create/update/delete) go through TanStack Query mutations, invalidating the owning entity's query keys," and (b) a query-key convention, e.g. `[entityName, 'list' | 'detail', ...params]`, entity name matching the feature folder name.

### AD-4 — Application layer organization — HELD

Identical rule applied uniformly per entity; there's no legal way for two features to interpret "vertical slice per entity" differently without one of them visibly breaking the rule. Held.

### AD-5 — Repository shape — HOLE (Moderate)

**Divergence:** AD-5 says "a single `IUnitOfWork` coordinates `SaveChangesAsync` across repositories" but never says repositories *must not* call `SaveChangesAsync` themselves. `VehicleRepository.AddAsync()` could legally call `_context.SaveChangesAsync()` internally (a common shortcut), while `BookingRepository.AddAsync()` defers entirely to `IUnitOfWork.SaveChangesAsync()` called by the handler. Both still expose "intention-revealing methods," satisfying AD-5's letter.

**Damage:** this breaks the very feature AD-7's overlap check depends on — a `CreateBookingCommandHandler` that needs to load a vehicle, check `IsDeleted`, and create the booking as one atomic unit will get a false sense of transactional safety on Vehicles-touching flows but not on Booking-only flows (or vice versa), and cross-aggregate operations (e.g., "cancel booking + free up vehicle" in one handler) become non-atomic if one repository auto-commits mid-handler.

**Fix:** state explicitly in AD-5: "repository methods never call `SaveChangesAsync`; only `IUnitOfWork.SaveChangesAsync()`, invoked once per handler, commits."

### AD-6 — Primary key strategy — HOLE (Minor-Moderate)

**Divergence:** AD-6 pins the *algorithm* (`Guid.CreateVersion7()`) but not *where/when* it runs. `Vehicle`'s constructor could self-assign `Id = Guid.CreateVersion7()` at domain-object construction time (id available immediately, before `SaveChangesAsync`), while `Booking`'s id is instead produced by an EF Core value generator / Postgres default at insert time (id is `Guid.Empty` until the row is actually persisted). Both "use Guid v7" per AD-6's letter.

**Damage:** any handler that needs to return the new entity's id in its response (relevant given AD-2's hole above) works for Vehicle and silently returns an empty guid for Booking unless the dev remembers to re-read `booking.Id` post-`SaveChanges`. It's also incompatible with AD-15's "rich domain model" spirit — a value generator strategy means the entity is briefly in an invalid state (no identity) after construction.

**Fix:** pin generation site: "ids are assigned by the entity's constructor/factory (`Guid.CreateVersion7()`), never by an EF Core value generator or DB default."

### AD-7 — Booking overlap check placement — HELD

The rule is specific enough (repository loads *active* bookings; `DateRange.Overlaps` decides) that a compliant Bookings-only implementation has one legal shape. No second unit to diverge against within CAP-3's scope. Held.

### AD-8 — API error contract — HOLE (Moderate-High)

**Divergence A (`type` URI scheme):** AD-8 mandates "a distinct `type` URI per rule" but not a naming scheme. Vehicles' overlap-adjacent rules could use `https://api.brunovh.local/errors/vehicle-soft-deleted`, while Bookings' rules use `urn:bruno:errors:booking-overlap`. Both are "distinct type URIs," but CAP-5's generic Angular error interceptor — which must key off `type` to render feature-specific inline messages — cannot use one parsing/matching strategy across both (one needs URL-path segment extraction, the other needs URN-suffix extraction).

**Divergence B (uncatalogued rules → status code):** AD-8 enumerates by name which rules map to 409 (overlap, delete guards, `EndDate<=StartDate`) and which map to 400 (FluentValidation). But `domain-model.md` has rules AD-8 never lists explicitly — "cannot delete a past booking," "cannot book a soft-deleted vehicle," "customer has bookings → hard-delete blocked." A dev could reasonably implement "past-booking delete" as a FluentValidation check on the Delete command (400) since it's a simple state check, while another dev implements "soft-deleted vehicle" as a domain-entity guard throwing a domain exception (409) — both are defensible readings of an incomplete enumeration, and the two rules end up on different HTTP status codes / envelope shapes despite being structurally identical ("reject because of current entity state").

**Fix:** (a) pin the `type` URI to one template, e.g. `urn:bruno:{entity}:{rule-kebab-case}`, generated from a single enum/const list rather than hand-typed per handler; (b) state the *general test* for 400 vs. 409 ("FluentValidation validates only the shape/presence of input fields in isolation; anything that requires loading persisted state or an entity method to evaluate is a domain rule → 409"), not just an enumerated list, so uncatalogued-but-structurally-identical rules land consistently.

### AD-9 — Booking date type — HELD

Narrow, single-entity, no second unit to diverge against.

### AD-10 — Validation execution — HOLE (Minor)

**Divergence:** AD-10 says validators run in the pipeline "ahead of every handler," but doesn't say whether Queries require validators at all. Vehicles' `GetVehiclesQuery` (pagination/filter params) could ship with no `IValidator`, silently allowing `pageSize=-1` or `pageSize=100000` through to the repository, while Bookings' `GetBookingsQuery` gets a validator constraining `pageSize` to a sane range. Both satisfy "never invoked manually" — the behavior just no-ops when no validator is registered for a request type. This directly undermines AD-14's promise of one generic, safe pagination contract.

**Fix:** extend AD-10 (or AD-14): "every Query taking pagination/filter parameters has a corresponding validator bounding `page`/`pageSize`; this is not optional per-feature."

### AD-11 — API key authentication — HOLE (Moderate)

**Divergence:** AD-11 pins *how* the auth handler is implemented, not *how broadly* it's applied. `VehiclesController` could apply `[Authorize]` explicitly on every action (opt-in), while `BookingsController` relies on a global `FallbackPolicy` (opt-out, secure-by-default). Both integrate "with `[Authorize]` and the Swagger security definition" per AD-11's letter. The opt-in controller is one forgotten attribute away from an unauthenticated endpoint reaching production — exactly the CAP-6 failure mode ("requests without a valid key are rejected") the AD exists to prevent, and nothing in the spine says which default the API-wide `AuthorizationOptions` must use.

**Fix:** state explicitly: "authentication is enforced via a global `FallbackPolicy` requiring the API-key scheme by default; `[AllowAnonymous]` is the only opt-out, used nowhere in v1."

### AD-12 — PII field encryption — HELD (narrow flag)

The value-converter rule itself is unambiguous and entity-scoped (only `Customer.Email`/`PhoneNumber`), so there's no second *feature* unit to diverge against inside this AD. One adjacent risk worth flagging even though it isn't a two-unit divergence per se: nothing in the spine or `stack.md` says seed data must be inserted through the same EF Core `SaveChanges` path that carries the value converter — a seed script using raw SQL/bulk-copy would silently store plaintext PII, defeating CAP-10's evaluator-facing "inspect the raw DB, confirm it's encrypted" success signal. Worth a one-line note in the README/AD-12, not a new AD.

### AD-13 — Customer soft-delete vs. anonymize — HOLE (High)

**Divergence:** AD-13 pins that soft-delete uses `IsDeleted` and anonymize uses `Customer.Anonymize()` setting `IsAnonymized` — but never states the relationship between the two flags. Two equally "compliant" implementations:

- **Implementation A:** `Anonymize()` also sets `IsDeleted = true`, so anonymized customers vanish from default listings and are blocked from new bookings (mirrors the soft-delete exclusion pattern).
- **Implementation B:** `Anonymize()` leaves `IsDeleted` untouched, so an anonymized customer remains visible in default listings (arguably required — CAP-12 says booking history "remains queryable," implying the customer record itself stays live) and — because nothing blocks it — remains selectable for a **brand-new** booking, which would attach a real future booking to a customer whose name/email/phone are placeholder values.

Both implementations satisfy every literal clause of AD-13 ("never touches PII" for soft-delete; "no un-anonymize method exists" for anonymize). Neither is obviously wrong from the AD text alone, and they produce opposite runtime behavior for "can I book this anonymized customer" — a CAP-3/CAP-12 correctness question graded by the evaluator.

**Fix:** AD-13 needs an explicit third clause: state whether `IsAnonymized` participates in the same global query filter / booking-eligibility check as `IsDeleted` (recommendation: it should — an anonymized customer is not a valid target for new bookings, only a referential-integrity anchor for old ones), and whether it appears in default listings.

### AD-14 — Paginated list response shape — HOLE (Moderate)

**Divergence:** AD-14 pins the *response* envelope (`{ items, totalCount, page, pageSize }`) but not the *request* shape that produces it. `GET /api/vehicles?page=1&pageSize=20&search=...` vs. `GET /api/bookings?pageNumber=0&limit=20&q=...` both legally produce the mandated `PagedResult<T>` on the way out. Whether `page` is 0- or 1-indexed is also unstated. This directly contradicts AD-14's own stated purpose — "breaking a generic Angular list/query hook" is exactly what divergent request params (and divergent page-indexing) do, even with the response shape held constant.

**Fix:** extend AD-14 (or add a convention-table row) pinning the query-string contract: `page` (1-indexed), `pageSize`, plus a per-entity `filter`/`search` param, consistently named across Vehicles/Customers/Bookings.

### AD-15 — Aggregate mutation — HOLE (Moderate)

**Divergence:** AD-15 bans "public property setters called from Application handlers" for *mutation*, but says nothing about *construction*. `Vehicle` could be built with `public string Make { get; init; }` properties and instantiated by the handler as `new Vehicle { Make = dto.Make, RegistrationNumber = dto.RegistrationNumber, ... }` (a bare object initializer — arguably not "a setter called from a handler" in the mutation sense AD-15 is policing, since it's construction, not post-construction mutation). `Booking`, meanwhile, could be built via a private constructor plus `Booking.Create(vehicleId, customerId, dateRange)` static factory, with no public settable property anywhere. Both entities are constructed differently — one exposes public `init` setters (a crack that `set` instead of `init` could slip through unnoticed in review), the other doesn't expose any — while both technically satisfy "mutation only via aggregate root methods." This is exactly the kind of drift CAP-9 (testability) and CAP-7 (rich domain model, evaluated directly) would penalize if the evaluator diffs Vehicle vs. Booking construction style.

**Fix:** extend AD-15: "aggregates are constructed only via a static factory method or non-default constructor performing invariant checks (e.g. `Vehicle.Create(...)`, `Booking.Create(...)`); no entity exposes a public settable (`set` or `init`) property — persistence-only mutation happens through EF Core's backing-field/private-setter materialization, never a public accessor."

---

## Cross-cutting holes (not tied to one AD)

### C-1 — JSON casing / DTO property naming is unpinned (High)

The conventions table pins **C#** naming (`PascalCase`) and **Angular file** naming (`kebab-case`), but says nothing about the **wire format** in between: is the JSON payload PascalCase (matching C# DTO properties as written) or camelCase (ASP.NET's default `System.Text.Json` policy)? Two controllers configured with different `JsonSerializerOptions` (or one dev overriding the global default on a single controller for "consistency with C#") would produce `{ "DailyRate": 45.0 }` from Vehicles and `{ "dailyRate": 45.0 }` from Bookings — both "PascalCase C# types" per the letter of the convention row, radically different over the wire. Needs its own row: "JSON payloads: camelCase (ASP.NET Core default), uniformly, no per-controller override."

### C-2 — REST route/verb naming is unpinned (Moderate)

Nothing pins `/api/vehicles` vs `/api/vehicle`, plural vs. singular, or verb-vs-noun for non-CRUD actions (`POST /api/bookings/{id}/cancel` vs. `POST /api/bookings/{id}?action=cancel` vs. a dedicated `CancelBookingCommand` behind `PATCH /api/bookings/{id}/status`). Three feature controllers can each invent their own action-route idiom for the "domain method, not a setter" mutations AD-15 mandates (Cancel, SoftDelete, Anonymize, Restore) with no shared pattern for the Angular services (AD-3, stack.md's "centralized API client") to build against generically.

### C-3 — Spine's frontend API-ownership model contradicts its own companion doc (Moderate)

The spine's Design Paradigm section says feature modules "each own their own API calls" (implying per-feature `VehiclesApiService`, `BookingsApiService`, etc. with no shared abstraction beyond the interceptor). `stack.md` — a document the spine's own frontmatter lists as a `companions:` source it is bound to project — explicitly requires a **"Centralized API client"** as a Solid-level architecture expectation. These aren't obviously the same thing: "centralized API client" usually means one base `ApiClient`/`HttpService` wrapper (base URL, error mapping, retry) that feature services sit on top of, but the spine never says feature API services must be thin wrappers over one shared client rather than independent `HttpClient` consumers. A Vehicles feature built as "owns its own API calls" literally (raw `HttpClient` injected directly, no shared client) and a Bookings feature built with a shared `ApiClient` base would both satisfy the spine's own wording while only one satisfies `stack.md`. This should be reconciled explicitly, e.g.: "feature services own their query/mutation *logic* and DTO mapping, but all HTTP calls route through one shared `ApiClient` in `core/` providing base URL, error normalization, and the API-key interceptor hook."

### C-4 — Frontend Domain model / DTO / View model separation is asserted but not operationalized (Minor-Moderate)

`stack.md` lists "Separation of Domain models / DTOs / View models" as a Solid-level expectation, but the spine has no AD or convention row translating that into a folder/naming rule the way AD-4 does for the backend. Vehicles could define `Vehicle` (API DTO) and `VehicleViewModel` (UI-shaped) as two interfaces with a mapper function; Bookings could just reuse one flat `Booking` interface for both API and view layers. Both are "a feature module," neither is provably wrong against the spine text, but only one satisfies the graded expectation in `stack.md`. Given CAP-8 is a directly graded capability, this gap is worth closing with a row/AD analogous to AD-4 but for the frontend (e.g., `core/models` for API DTOs, `features/{x}/models` for view models, an explicit mapper convention).

### C-5 — Enum wire representation is unpinned (Minor)

`Booking.Status` (Active/Completed/Cancelled) is the only enum in the domain model, so today there's no second enum to diverge against — but nothing in the conventions table states whether enums serialize as string or int over JSON/Swagger. Worth a one-line convention-table addition before a second enum (e.g. a future `VehicleCategory`) actually creates the two-unit clash.

---

## Verdict

**PASS WITH CONCERNS.**

The 15 ADs are individually well-formed and several (AD-1, AD-4, AD-7, AD-9, AD-12) are tight enough that no adversarial two-unit construction succeeds against them. But roughly two-thirds of the ADs (AD-2, AD-3, AD-5, AD-6, AD-8, AD-10, AD-11, AD-13, AD-14, AD-15) leave at least one dimension — response shape, request shape, construction-vs-mutation boundary, transaction-commit ownership, or a flag-interaction rule — unpinned, such that two features can each follow the letter of the rule and still be incompatible with each other or with the Angular consumer the spine is written to serve. The highest-severity holes are **AD-13** (soft-delete/anonymize flag interaction — a real correctness bug, not just style drift), **AD-8** (error `type` URI scheme + incomplete rule enumeration — breaks the generic error interceptor CAP-5 depends on), **AD-11** (no stated secure-by-default posture — a real CAP-6 risk), and **C-1/C-3** (JSON casing and the spine-vs-stack.md API-ownership contradiction — both hit the Vehicles/Bookings/Customers boundary on every single endpoint, not just an edge case).

None of these require re-deriving the paradigm; each is a one- or two-sentence tightening of an existing AD or one new convention-table row. Given this is a solo, single-timeline build (no literal second team to diverge), the practical risk is lower than "PASS WITH CONCERNS" might suggest for a multi-team project — but several holes (AD-13 especially) are genuine correctness ambiguities the *same* developer could resolve inconsistently between Customer and Booking work done weeks apart, which is the scenario CAP-9/CAP-7's "architectural consistency" grading criterion is designed to catch.
