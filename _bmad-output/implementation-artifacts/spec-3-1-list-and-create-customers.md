---
title: 'Story 3.1: List & Create Customers'
type: 'feature'
created: '2026-09-14'
status: 'done'
review_loop_iteration: 0
context: []
baseline_commit: 'd68e0a8fd6c15ff43cb0bbaf3bbde8b232558e42'
---

<frozen-after-approval reason="human-owned intent — do not modify unless human renegotiates">

## Intent

**Problem:** Epic 3 starts from nothing — no `Customer` entity, no `Customers` table, and no PII encryption has ever actually been built (AD-12 was written in the architecture phase but never implemented). This is also the first entity in the app with a field that must be BOTH encrypted at rest AND uniquely enforced (`Email`).

**Approach:** Mirror Vehicle's Epic 1/2.1 pattern almost exactly (`Customer.Create()`, `ICustomerRepository`, `GetCustomersQuery`, `CreateCustomerCommand`, a list page + create modal) — but solve one genuinely new problem AD-12 left open: ASP.NET Core's Data Protection API is non-deterministic (a random IV per call), so encrypting the same email twice produces different ciphertext. A DB-level unique index directly on the encrypted `Email` column would never catch a real duplicate. This story adds a deterministic `EmailHash` sidecar column (SHA-256 of the normalized email) purely as an Infrastructure-layer uniqueness mechanism — the standard, real-world pattern for "unique encrypted field" — and puts the actual unique index there instead.

**Scope decisions — flagged for approval:**

1. **`EmailHash` sidecar column + `AppDbContext.SaveChangesAsync` override.** Not in AD-12's literal text, but a necessary completion of what AD-12 already committed to ("Email unique" + "encrypted... via an EF Core value converter" — both true simultaneously requires this). The hash is computed application-side (an `EmailHasher` static utility in Infrastructure) and set on a shadow property before every save — `Customer` (Domain) never knows this exists, keeping AD-1's zero-framework-dependency rule intact.
2. **Customer search covers FirstName/LastName only** — `Email`/`PhoneNumber` are encrypted, and `EF.Functions.ILike` (or any pattern-match) against ciphertext is meaningless. Decrypting every row to support a search box would be both a severe performance antipattern and arguably a PII-handling antipattern (needlessly holding decrypted PII in memory for every list request) — not built.
3. **No `FilterBar` component.** epics.md's AC says "reusing the `DataTable`/`FilterBar`/`Skeleton` pattern already proven in Epic 1" — but Epic 1/2 never actually built a `FilterBar` component; Vehicles has only ever had a single debounced search input (Story 1.7's own scope decision, Story 2.1's Design Notes confirm the same reading). This story follows the same as-built pattern, not the AC's aspirational wording.
4. **`CreateCustomerModal` is create-only**, not generalized to a `CustomerFormModal` yet — mirroring exactly how `CreateVehicleModal` (Story 2.1) stayed create-only until Story 2.2 needed Edit for real. Story 3.2 is next and will do the identical generalization.
5. **Forward note for Story 3.5 (not this story's problem, flagging now):** once anonymization exists, every anonymized customer's `Email` gets replaced with *some* placeholder — if that placeholder is a fixed literal, the second anonymization will collide with `EmailHash`'s unique constraint. Story 3.5 will need a per-row-unique placeholder (e.g. embedding the customer's own id). Noted here so it isn't a surprise later; not addressed by this story's schema beyond the unique constraint existing at all.

## Boundaries & Constraints

**Always:**
- `Customer.Create(...)` validates non-blank FirstName/LastName/Email/PhoneNumber as domain invariants (`DomainRuleViolationException`, mirroring `Vehicle.Create()`'s exact pattern) — email FORMAT is deliberately NOT a domain invariant, only a FluentValidation shape check (matching epics.md's AC: "malformed email... `400`"), the same Create-vs-domain split precedent as Vehicle's Year.
- `Customer`'s `Email`/`PhoneNumber` properties are encrypted transparently via an EF Core `ValueConverter<string,string>` backed by the ALREADY-REGISTERED `IDataProtectionProvider` (Data Protection has been wired since Story 1.2/1.4 — no new DI registration needed, just a new consumer of it).
- The DB-level unique constraint enforcing "Email unique" lives on the new `EmailHash` shadow column (SHA-256 of `email.Trim().ToLowerInvariant()`), NOT on the encrypted `Email` column itself.
- `ICustomerRepository.ExistsByEmailAsync(string email, Guid? excludingId, CancellationToken ct)` computes the same hash from the incoming plaintext email and compares against the stored `EmailHash` — mirrors `IVehicleRepository.ExistsByRegistrationNumberAsync`'s exact `IgnoreQueryFilters()` reasoning (the DB unique index is itself unfiltered).
- The `Customers` table's query filter is `!IsDeleted` only — `IsAnonymized` customers stay in default listings (per `domain-model.md`'s explicit rule and `EXPERIENCE.md`'s Flow 2: an anonymized customer's row still appears, just rendered differently — that rendering is a future story's job, not this one's).
- Every write (seed data, application writes alike) goes through EF Core `SaveChanges` — AD-12's explicit requirement that the value converter always applies, never bypassed by raw SQL.
- `CreateCustomerCommand` returns the created `CustomerDto` (AD-2), reuses `IUnitOfWork` exactly as-is (already entity-agnostic, no changes needed).

**Ask First:** Nothing expected to trigger.

**Never:** No Edit/Deactivate/Restore/Anonymize/Delete yet (Stories 3.2-3.5). No `Bookings` table (Story 3.3). No `FilterBar` component (Scope decision 3). No `CustomerFormModal` generalization yet (Scope decision 4). No logging of plaintext PII anywhere (AD-12).

## I/O & Edge-Case Matrix

| Scenario | Input / State | Expected Output / Behavior | Error Handling |
|----------|--------------|---------------------------|----------------|
| Migration runs | Fresh database | `Customers` table exists with the `domain-model.md` schema + `EmailHash` | N/A |
| Valid create | Non-blank FirstName/LastName/PhoneNumber, valid Email | `201`, `CustomerDto`; raw DB row's `Email`/`PhoneNumber` columns are NOT plaintext (test reads the DB directly) | N/A |
| Duplicate Email | Matches an existing customer's Email (case/whitespace-insensitive, per the hash normalization) | `409 Conflict`, inline error under Email | Same pattern as Vehicle's RegistrationNumber |
| Malformed Email | e.g. `"not-an-email"` | `400`, `errors.Email` | Inline under Email field |
| Blank FirstName/LastName/PhoneNumber | Empty/whitespace | `400`, field-specific `errors` entry | Inline under that field |
| List customers | Customers exist | Paginated `DataTable`, same `PagedResult<T>` contract as Vehicles | N/A |
| Search | Matches FirstName or LastName | Filtered results | N/A |

## Code Map

**Backend:**
- `src/BrunoVehicleHire.Domain/Customer.cs` -- new -- `Create(firstName, lastName, email, phoneNumber, timeProvider?)`, mirrors `Vehicle`'s private-constructor/factory/no-public-setters shape exactly (AD-15); `IsDeleted`/`IsAnonymized` both default `false`
- `tests/BrunoVehicleHire.Domain.Tests/CustomerTests.cs` -- new -- full I/O matrix (mirrors `VehicleTests.cs`'s structure), including the no-public-setters reflection check
- `src/BrunoVehicleHire.Infrastructure/Helpers/EmailHasher.cs` -- new -- `public static string Compute(string email) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(email.Trim().ToLowerInvariant())));`
- `src/BrunoVehicleHire.Infrastructure/Persistence/AppDbContext.cs` -- modify -- constructor takes `IDataProtectionProvider`, creates one `IDataProtector` (a fixed purpose string, e.g. `"BrunoVehicleHire.PII"`); `DbSet<Customer> Customers`; `Customer` EF configuration (table, unique index on the `EmailHash` shadow property, the PII value converter applied to `Email`/`PhoneNumber`, query filter `!IsDeleted`); override `SaveChangesAsync` to compute and set the `EmailHash` shadow property for every added/modified `Customer` entry before calling `base.SaveChangesAsync`
- `src/BrunoVehicleHire.Infrastructure/Migrations/` -- new migration creating `Customers` (Id, FirstName, LastName, Email, PhoneNumber, EmailHash [shadow, unique-indexed], CreatedDate, IsDeleted, IsAnonymized)
- `src/BrunoVehicleHire.Application/Customers/ICustomerRepository.cs` -- new -- `GetPagedAsync(page, pageSize, search, ct)`, `ExistsByEmailAsync(email, excludingId, ct)`, `AddAsync(customer, ct)` -- mirrors `IVehicleRepository`'s exact shape/doc-comment style
- `src/BrunoVehicleHire.Infrastructure/Repositories/CustomerRepository.cs` -- new -- implements the above; search via `EF.Functions.ILike` on FirstName/LastName ONLY (never Email/PhoneNumber -- Scope decision 2)
- `src/BrunoVehicleHire.Application/Customers/Dtos/CustomerDto.cs` -- new -- `record(Guid Id, string FirstName, string LastName, string Email, string PhoneNumber, DateTime CreatedDate, bool IsDeleted, bool IsAnonymized)` with a `FromDomain` static factory (mirrors `VehicleDto`)
- `src/BrunoVehicleHire.Application/Customers/Queries/GetCustomersQuery.cs`, `GetCustomersQueryValidator.cs`, `GetCustomersQueryHandler.cs` -- new -- mirror `GetVehiclesQuery`'s exact shape (page/pageSize bounds, `PagedResult<CustomerDto>`)
- `src/BrunoVehicleHire.Application/Customers/Commands/CreateCustomerCommand.cs`, `CreateCustomerCommandValidator.cs`, `CreateCustomerCommandHandler.cs` -- new -- mirror `CreateVehicleCommand`'s exact shape; validator: FirstName/LastName/PhoneNumber `.NotEmpty()`, Email `.NotEmpty().EmailAddress()`; handler: `ExistsByEmailAsync` (409 if duplicate, exact message "This email address is already in use.") → `Customer.Create(...)` → `AddAsync` → `SaveChangesAsync` → `CustomerDto.FromDomain`
- `src/BrunoVehicleHire.Api/Controllers/CustomersController.cs` -- new -- `[HttpGet]`/`[HttpPost]`, mirrors `VehiclesController`'s exact shape (no `[Authorize]`, the global `FallbackPolicy` already covers it)
- `tests/BrunoVehicleHire.Application.Tests/Customers/` -- new -- validator/handler tests mirroring the Vehicles equivalents exactly
- `tests/BrunoVehicleHire.Integration.Tests/CustomersEndpointTests.cs` -- new -- mirrors `VehiclesEndpointTests.cs`'s pattern; MUST include a test that reads the raw `Customers` row via a direct `NpgsqlConnection`/raw SQL query (bypassing EF Core entirely, mirroring `VehicleMigrationTests.cs`'s existing raw-connection pattern from Story 1.2) and asserts the stored `Email`/`PhoneNumber` values are NOT the plaintext submitted -- this is the AC's own explicit verification method, not optional

**Frontend:**
- `frontend/src/app/core/models/customer-dto.ts` -- new -- mirrors `vehicle-dto.ts`
- `frontend/src/app/features/customers/models/customer.ts` -- new -- `Customer` view model + `toCustomer` mapper, mirrors `vehicles/models/vehicle.ts`
- `frontend/src/app/features/customers/customers.service.ts` -- new -- `useCustomersQuery` (mirrors `useVehiclesQuery`, query key `['customers', 'list', {page, pageSize, search}]`), `useCreateCustomerMutation` (mirrors the vehicle create mutation pattern)
- `frontend/src/app/features/customers/create-customer-modal.ts` (+`.html`+`.spec.ts`) -- new -- mirrors `CreateVehicleModal`'s Story-2.1-era shape exactly (create-only, `Modal`+`Input`+`Button`, client-side `Validators` mirroring the backend, `applyError` mapping 400/409/not-found/server-error onto fields or a banner)
- `frontend/src/app/features/customers/customers-page.ts` (+`.html`+`.spec.ts`) -- modify (replacing the Story 1.6 placeholder) -- mirrors `vehicles-page.ts`'s Story-1.7/2.1-era shape: debounced search (FirstName/LastName only), `DataTable`, `Skeleton` via `DataTable`, the two empty-state messages, "+ New Customer" button opening `CreateCustomerModal`

## Tasks & Acceptance

**Execution — backend (TDD throughout):**
- [x] `Customer.Create(...)` -- write failing Domain.Tests first, then implement
- [x] `EmailHasher` -- write failing tests first (same input → same hash; normalization: case/whitespace-insensitive)
- [x] `AppDbContext` -- Customer configuration, PII value converter, `EmailHash` shadow property + `SaveChangesAsync` override, migration
- [x] `ICustomerRepository`/`CustomerRepository` -- write failing tests first for `ExistsByEmailAsync` (via Testcontainers, proving normalization -- differently-cased/whitespace-padded duplicate emails are still caught) and `GetPagedAsync` (search matches FirstName/LastName, never Email/PhoneNumber)
- [x] `GetCustomersQuery`/`Validator`/`Handler`, `CreateCustomerCommand`/`Validator`/`Handler` -- write failing tests first, mirroring the Vehicles equivalents
- [x] `CustomersController`
- [x] `CustomersEndpointTests` -- write failing integration tests first for every I/O-matrix row, INCLUDING the raw-database plaintext-verification test
- [x] Full `dotnet test`; re-run `ArchitectureFitnessTests`; force `dotnet restore` and check for NU1903 warnings; explicit SOLID/DRY/YAGNI self-check -- confirmed: 217/217 passing, 0 vulnerabilities

**Execution — frontend (TDD throughout):**
- [x] `CustomerDto`/`Customer`/`toCustomer`
- [x] `customers.service.ts` -- write failing tests first
- [x] `CreateCustomerModal` -- write failing tests first, mirroring `create-vehicle-modal.spec.ts`'s (pre-generalization) Story-2.1 coverage
- [x] `customers-page` -- write failing tests first, mirroring `vehicles-page.spec.ts`'s Story-1.7/2.1-era coverage (loading/empty/filtered-empty/populated, create flow)
- [x] `ng build` + `ng test --coverage --watch=false` clean; `npm audit` clean; explicit SOLID/DRY/YAGNI self-check -- confirmed: 168/168 passing, 97.63% stmt coverage, 0 vulnerabilities, `DataTable` itself required zero changes

**Acceptance Criteria (epics.md, verbatim intent):**
- Given this story's own migration creates the `Customers` table, when it runs, then the table exists with Email/PhoneNumber columns backed by the Data Protection value converter
- Given customers exist, when I open the Customers page, then I see a paginated, filterable list
- Given I submit valid "+ New Customer" details, when I submit, then a new customer is created, appears in the list, and a test reading the raw database directly confirms Email/PhoneNumber are encrypted, not plaintext
- Given a duplicate Email, when I submit, then `409 Conflict` with an inline error under Email
- Given invalid input (e.g. malformed email), when I submit, then `400` validation errors render inline under each specific field

## Spec Change Log

**Real bug found and fixed, project-wide (not just this story):** `Program.cs` read `builder.Configuration.GetConnectionString("Postgres")` into a local variable BEFORE `builder.Build()`. `WebApplicationFactory.WithWebHostBuilder`'s `ConfigureAppConfiguration` overrides only take effect once the intercepted `Build()` call actually proceeds -- reading the connection string earlier and capturing it into a plain `string` meant that value was frozen before any test override could apply. Every prior integration test across the whole project (Vehicles included, not just this story's `CustomersEndpointTests`) was silently running against the REAL local docker-compose Postgres instead of the intended ephemeral Testcontainers one -- confirmed by finding leftover `Vehicles` rows in the dev DB matching `VehiclesEndpointTests`' own seed data. Fixed by moving the read inside `AddDbContext`'s options delegate, which isn't evaluated until `AppDbContext` is actually resolved from DI, post-`Build()`, after `WebApplicationFactory`'s configuration layering has already merged in. Independently verified by the reviewer: snapshotted the real dev DB's row counts, ran the full 217-test suite, re-checked the row counts -- byte-identical before and after, confirming true isolation now. A new Infrastructure.Tests project was also created (none existed before) for `EmailHasher`'s pure-logic tests.

## Design Notes

**Why `EmailHash` is a shadow property, not a `Customer` domain property:** it's purely a persistence-layer uniqueness mechanism with no business meaning (`Customer` -- the aggregate a domain expert would recognize -- has no concept of "email hash"). Exposing it on the domain class would leak an Infrastructure concern across AD-1's dependency direction for zero business value.

**Why the hash is computed in `SaveChangesAsync`, not in `Customer.Create()`:** `Customer.Create()` (Domain) must stay framework-free (AD-1) -- it can't reference `System.Security.Cryptography` for a persistence-only concern, and even if it could, the resulting hash would still need to reach a column `Customer` doesn't expose a property for. Computing it in `AppDbContext` right before every save is the one place that already has both the plaintext Email (still readable at that point, before the value converter transforms it for the parameter) and direct access to the shadow property.

**Why normalization (`Trim().ToLowerInvariant()`) matters:** without it, `"Jane@Example.com"` and `"jane@example.com "` would hash to different values and both slip past the duplicate check despite being the same real-world email -- the hash's normalization is doing the same job SQL's `citext`/case-insensitive collation would do for a plaintext unique column, which isn't available here since the stored value is ciphertext.

**Frontend `dateFormatter` duplication (minor, deliberate):** `customer-formatters.ts` has its own `dateFormatter` rather than importing Vehicles' -- no feature folder in this codebase imports from a sibling feature folder anywhere else (matches AD-4's vertical-slice-per-entity convention), so a ~10-line duplication was judged the smaller violation against introducing the first cross-feature import for one formatter object.

## Verification — actual results

- Backend: `dotnet build` 0 warnings/0 errors; `dotnet test` 217/217 passing (up from 153 before this story); no vulnerability warnings; `ArchitectureFitnessTests` unchanged/passing. Live manual proof: created a customer via curl against the real docker-compose Postgres, read the raw row directly via `psql` -- `Email`/`PhoneNumber` columns contained genuine Data Protection ciphertext (`CfDJ8...` payload format), `FirstName`/`LastName` remained plaintext as intended.
- **A genuine, project-wide bug was found and fixed during this story**: every prior integration test (Epic 1/2 included) was silently running against the real dev Postgres instead of Testcontainers, due to `Program.cs` reading the connection string too early in the host-building pipeline. Independently re-verified by the reviewer: snapshotted dev-DB row counts, ran the full 217-test suite, re-checked -- byte-identical, confirming true isolation now. See the Spec Change Log entry and [[project-test-isolation-bug-fixed]] (project memory).
- Frontend: `ng build` 0 errors/warnings (587.88kB, within the 650kB budget); `ng test --coverage --watch=false` 168/168 passing, 97.63% statement coverage; `npm audit` 0 vulnerabilities. `DataTable` required zero modifications.
- Full live end-to-end: real backend + `ng serve` + real Postgres. Created a customer (list updated, toast fired), triggered a real 409 by resubmitting the same email (inline error under Email), confirmed client-side email validation catches malformed input before any request fires.

</frozen-after-approval>

## Suggested Review Order

**The novel piece (the point of this story)**

- `AppDbContext`'s `SaveChangesAsync` override + the `EmailHash` shadow property -- the whole solution to non-deterministic encryption vs. a unique constraint, in one place.
  [`AppDbContext.cs`](../../src/BrunoVehicleHire.Infrastructure/Persistence/AppDbContext.cs)

- The raw-database plaintext-verification test -- proves the AC's own explicit requirement, not just an assertion on a DTO.
  [`CustomersEndpointTests.cs:318`](../../tests/BrunoVehicleHire.Integration.Tests/CustomersEndpointTests.cs#L318)

- `EmailHasher` -- the one place normalization/hashing logic exists, called from two directions.
  [`EmailHasher.cs`](../../src/BrunoVehicleHire.Infrastructure/Helpers/EmailHasher.cs)

**The incidental but important find**

- The `Program.cs` connection-string timing fix -- affects every integration test in the project, not just this story's.
  [`Program.cs`](../../src/BrunoVehicleHire.Api/Program.cs)

**The mirrored pattern (mostly a replay of Vehicles' early shape)**

- `Customer.cs`, `CreateCustomerCommandHandler.cs`, `create-customer-modal.ts`, `customers-page.ts` -- each intentionally unsurprising if you've already reviewed the Vehicle equivalents.
