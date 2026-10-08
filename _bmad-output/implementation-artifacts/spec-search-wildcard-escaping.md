---
title: 'Bug fix: unescaped ILIKE wildcards in every search box'
type: 'bugfix'
created: '2026-09-21'
status: 'done'
review_loop_iteration: 0
context: []
baseline_commit: 'edce456ba966e7743651bad258aab4c6544440b1'
---

<frozen-after-approval reason="human-owned intent — do not modify unless human renegotiates">

## Intent

**Problem:** An adversarial re-check found that every search implementation (`VehicleRepository`, `CustomerRepository`, and the just-added `BookingRepository` search) interpolates the raw user-supplied `search` string directly into a Postgres `ILIKE` pattern (`$"%{search}%"`) with no escaping. Postgres `ILIKE` treats `%` and `_` as wildcards — confirmed live: searching for a literal `%` on the running app returns every single row (59/59 bookings) instead of a sane "no results," since the interpolated pattern becomes `%%%` (matches anything). Any of the three search boxes is affected identically.

**Approach:** Add one small, shared escaping helper (Infrastructure layer, since this is purely a persistence/SQL-pattern concern) that escapes `\`, `%`, and `_` in the raw search term before it's wrapped in the surrounding `%...%` wildcard pair. Postgres's `ILIKE`/`LIKE` already treats backslash as the default escape character, so escaping the input this way and passing it through the existing `EF.Functions.ILike(column, pattern)` two-argument overload (no signature change needed) makes a literal `%`/`_`/`\` in a search term match itself, not act as a wildcard.

## Boundaries & Constraints

**Always:**
- The escape helper handles exactly three characters, in this order (order matters — escaping the escape character itself must happen first): `\` → `\\`, then `%` → `\%`, then `_` → `\_`.
- Applied at exactly the same call sites the search predicate is already built at (`VehicleRepository.GetPagedAsync`, `CustomerRepository.GetPagedAsync`, `BookingRepository.GetPagedAsync`) — the raw `search` parameter is escaped once, then used to build the `$"%{escaped}%"` pattern exactly as today, for every `ILike` call in that method.
- No change to the surrounding `%...%` wildcard wrapping itself, to the case-insensitivity, or to which fields are searched — only the literal-vs-wildcard interpretation of the user's own input characters changes.
- A search term with no special characters behaves identically to today (the escape is a no-op for ordinary input) — every existing search test must keep passing unmodified.

**Ask First:** Nothing else expected to trigger.

**Never:** No change to the Bookings-search field set, the Vehicle/Customer search field sets, or any other search behavior beyond wildcard-literal handling. No new NuGet dependency — this is a few lines of plain string replacement.

## I/O & Edge-Case Matrix

| Scenario | Input / State | Expected Output / Behavior | Error Handling |
|----------|--------------|---------------------------|----------------|
| Search term with no special characters | e.g. `"Toyota"` | Behaves exactly as today (substring match) | N/A |
| Search term containing a literal `%` | e.g. `"50%"` | Matches only rows containing a literal `"50%"` substring, never the full unfiltered list | N/A |
| Search term containing a literal `_` | e.g. `"CA_123"` | Matches only rows containing a literal `"CA_123"` substring, never treating `_` as "any single character" | N/A |
| Search term containing a literal backslash | e.g. `"C:\Test"` | Matches only rows containing that literal substring, no SQL pattern corruption | N/A |

</frozen-after-approval>

## Code Map

- `src/BrunoVehicleHire.Infrastructure/Helpers/LikePatternEscaper.cs` -- new -- a small static class with one method, `Escape(string value)`, applying the three-character escape in the Boundaries' specified order
- `src/BrunoVehicleHire.Infrastructure/Repositories/VehicleRepository.cs` -- modify -- `GetPagedAsync` (`:33-35`) wraps `search` in `LikePatternEscaper.Escape(...)` before building the `%...%` pattern
- `src/BrunoVehicleHire.Infrastructure/Repositories/CustomerRepository.cs` -- modify -- same, `GetPagedAsync` (`:34-35`)
- `src/BrunoVehicleHire.Infrastructure/Repositories/BookingRepository.cs` -- modify -- same, `GetPagedAsync` (`:72-76`)
- `tests/BrunoVehicleHire.Integration.Tests/VehiclesEndpointTests.cs`, `CustomersEndpointTests.cs`, `BookingsEndpointTests.cs` -- modify -- add one real end-to-end test per entity covering the I/O matrix's literal-`%`/`_` cases against real seeded/created data

## Tasks & Acceptance

**Execution (TDD throughout):**
- [x] Write a failing test first for the literal-`%` case (any one entity), then add `LikePatternEscaper` and wire it into that repository
- [x] Wire the same escaping into the other two repositories; add their own literal-`%`/`_` tests
- [x] Live check: search the real running app for a literal `%`, confirm it no longer returns the full unfiltered list
- [x] Full `dotnet test`, confirm nothing regressed (every existing ordinary-search test keeps passing unmodified)

**Acceptance Criteria:**
- [x] Given a search term containing a literal `%` or `_`, when searched on any of the three lists, then the result treats that character literally, never as a wildcard matching everything/any-single-character
- [x] Given an ordinary search term with no special characters, when searched, then behavior is unchanged from before this fix

## Spec Change Log

**2026-09-21 — Correction to frozen Approach's escaping mechanism (implementation-discovered):** The frozen Intent's Approach states the existing `EF.Functions.ILike(column, pattern)` two-argument overload needs no signature change. This turned out to be incorrect: EF Core's Npgsql provider translates the two-argument overload to `ILIKE @pattern ESCAPE ''` — an explicit empty-string escape clause that, per Postgres semantics, disables the escape mechanism entirely rather than defaulting to backslash-escaping. Confirmed empirically (query logged during the TDD failing-test step). The actual implementation uses the three-argument `EF.Functions.ILike(column, pattern, escapeCharacter)` overload, passing `LikePatternEscaper.EscapeCharacter` (`"\\"`) explicitly at every call site. This does not change any behavior described in the I/O & Edge-Case Matrix — the observable outcome (literal `%`/`_`/`\` match themselves) is exactly as specified; only the internal EF Core API call needed to achieve it differs from the frozen text's description.

## Design Notes

**Why a shared helper rather than three independent inline fixes:** the exact same escaping logic is needed identically in three places — this is precisely the "second/third real consumer" DRY threshold this codebase has applied consistently elsewhere (e.g. `CapturingLoggerProvider`, `createConfirmableAction`), and a single, once-reasoned-about implementation is safer than three independently-written copies of subtle escape-ordering logic.

**Why Infrastructure, not a shared Domain/Application utility:** escaping for a SQL `LIKE` pattern is entirely a persistence-layer concern — Domain/Application have no reason to know Postgres's wildcard syntax exists, consistent with AD-1's layering.

## Verification

**Commands run (independently, not just by the implementing agent):**
- `dotnet build BrunoVehicleHire.sln` — 0 warnings, 0 errors.
- `dotnet test` (full solution, all 5 projects) — 439/439 passing, including the three new literal-`%`/`_` regression tests (`VehiclesEndpointTests`, `CustomersEndpointTests`, `BookingsEndpointTests`), each of which seeds a decoy row with a same-length, non-matching value to prove the fix actually discriminates rather than merely avoiding a crash.

**Manual checks (browser/API) — performed against the real running app + real dev Postgres DB:**
- Vehicles: unfiltered `totalCount` 13, `search=%` → `totalCount` 0 (no seeded dev row contains a literal `%`).
- Customers: unfiltered `totalCount` 13, `search=%` → `totalCount` 0.
- Bookings: unfiltered `totalCount` 59, `search=%` → `totalCount` 0 — previously this exact request returned 59/59 (the originally-reported bug); now it correctly returns 0.
- Ordinary searches (no special characters) were not separately re-verified live in this pass, since the full existing search test suite (pre-dating this fix) passed unmodified, and this scenario is explicitly pinned by the I/O matrix's first row.

## Suggested Review Order

1. `src/BrunoVehicleHire.Infrastructure/Helpers/LikePatternEscaper.cs` — the shared escaping logic and the doc comment explaining why the three-argument `ILike` overload is required (see Spec Change Log).
2. `src/BrunoVehicleHire.Infrastructure/Repositories/VehicleRepository.cs` — the first, canonical call-site wiring.
3. `CustomerRepository.cs`, `BookingRepository.cs` — the same wiring, mirrored exactly.
4. `tests/BrunoVehicleHire.Integration.Tests/VehiclesEndpointTests.cs`'s `Get_SearchWithLiteralPercentOrUnderscore_TreatsThemLiterally_NeverAsWildcards` — the canonical regression test, mirrored in `CustomersEndpointTests.cs`/`BookingsEndpointTests.cs`.
