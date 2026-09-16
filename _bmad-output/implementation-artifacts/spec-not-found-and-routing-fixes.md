---
title: 'Bug fix: not-found/unknown-route handling app-wide'
type: 'bugfix'
created: '2026-09-16'
status: 'done'
review_loop_iteration: 0
context: []
baseline_commit: 'e2cc6c66864afec6cbb4be3b3c6a41ae6c3d959d'
---

<frozen-after-approval reason="human-owned intent — do not modify unless human renegotiates">

## Intent

**Problem:** A QA pass found three related gaps in how the frontend handles "this doesn't exist" scenarios. (1) The real `QueryClient` (`app.config.ts`) has no `retry` override, so TanStack Query's library default (retry failed queries up to 3 times with backoff) applies in production to every query — including a deterministic 404 that will never succeed on retry. This delays (and, combined with this "experimental" library's own documented instability, can indefinitely stall) the correct "not found" render on Vehicle detail, Booking detail, and Customer Summary. Every one of this app's own test files explicitly sets `retry: false` on its own `QueryClient`, so this is completely invisible to the test suite by construction. (2) A malformed (non-GUID-format) id in a URL is rejected by ASP.NET's `{id:guid}` route constraint with a bodyless 404, which `errorNormalizationInterceptor`'s `isProblemDetails()` guard can't classify, so it's misclassified as a generic `ServerError` ("Something went wrong") instead of `NotFoundError`. (3) There is no wildcard route, so a completely unknown URL silently blanks the page with only a console routing error.

**Approach:** Configure the production `QueryClient` to never retry a deterministic client error (a `NotFoundError` or `BusinessRuleError`) while still retrying a genuine `ServerError` a small, bounded number of times. Treat any 404 response as `NotFoundError` even when its body isn't parseable ProblemDetails (a 404 always means "doesn't exist," regardless of body shape). Add a wildcard route redirecting to the app's home surface, mirroring the existing empty-path redirect.

## Boundaries & Constraints

**Always:**
- `QueryClient`'s `defaultOptions.queries.retry` becomes a function: `false` for a `NormalizedApiError` whose `kind` is `'not-found'` or `'business-rule'` (retrying a deterministic 400/404/409 can never succeed); for `kind === 'server-error'` (or any other thrown value), retry up to a small bounded count (2), matching TanStack Query's own stated best practice of not retrying non-transient errors.
- `error-normalization.interceptor.ts`'s `normalize()`: a 404 status always produces `NotFoundError`, whether or not the body is parseable ProblemDetails — use the parsed `detail` when available, otherwise a generic "This record no longer exists." message. Every other status code's existing body-trust logic is unchanged (a 400/409 still requires a parseable ProblemDetails body to become `BusinessRuleError`; anything else still falls to `ServerError`).
- `app.routes.ts` gets one new final route, `{ path: '**', redirectTo: 'bookings' }` — mirrors the existing `{ path: '', redirectTo: 'bookings', pathMatch: 'full' }` precedent exactly; no new page/component.
- No change to any backend code — every fix here is frontend-only, since the backend already returns correct, well-formed responses; the frontend was misinterpreting or over-retrying them.

**Ask First:** Nothing else expected to trigger.

**Never:** No dedicated "404 page" component (YAGNI — a redirect to the app's existing home surface is proportional for an internal tool with no public-facing SEO/deep-linking concern). No change to the `{id:guid}` route constraints on the backend. No change to retry behavior for mutations (`injectMutation` calls) — this spec is query-retry only.

## I/O & Edge-Case Matrix

| Scenario | Input / State | Expected Output / Behavior | Error Handling |
|----------|--------------|---------------------------|----------------|
| Navigate to a well-formed but nonexistent id | e.g. `/vehicles/{random-guid}` | "This vehicle no longer exists" renders promptly (no multi-second retry delay) | N/A |
| Navigate to a malformed (non-GUID) id | e.g. `/vehicles/not-a-guid` | Same "not found" treatment, not "Something went wrong" | N/A |
| A genuine 5xx/network failure occurs | Backend down or errors | Still retried a bounded number of times before showing the generic server-error message (unchanged from today) | N/A |
| Navigate to a completely unknown route | e.g. `/nonsense` | Redirects to `/bookings`, never a blank page | N/A |

</frozen-after-approval>

## Code Map

- `frontend/src/app/app.config.ts` -- modify -- `provideTanStackQuery(new QueryClient())` (`:15`) gains `defaultOptions: { queries: { retry: (failureCount, error) => ... } }`, typed against `NormalizedApiError`
- `frontend/src/app/core/api-client/error-normalization.interceptor.ts` -- modify -- `normalize()`'s 404 branch (`:67-69`) drops the `isProblemDetails(error.error)` requirement, always returning `NotFoundError` for status 404
- `frontend/src/app/app.routes.ts` -- modify -- add `{ path: '**', redirectTo: 'bookings' }` as the final route
- `frontend/src/app/app.config.spec.ts` or a new spec if none exists for this file -- new/modify -- test the retry predicate directly (not-found/business-rule never retries, server-error retries up to the bound)
- `frontend/src/app/core/api-client/error-normalization.interceptor.spec.ts` -- modify -- add a case for a bodyless/unparseable 404 now producing `NotFoundError`
- `frontend/src/app/app.routes.spec.ts` or wherever routing is tested -- modify if a relevant spec exists -- add a case for an unmatched path redirecting to `/bookings`

## Tasks & Acceptance

**Execution (TDD throughout):**
- [x] Write a failing test for the retry predicate, then configure `QueryClient`'s `retry` option
- [x] Write a failing test for the bodyless-404 case, then relax `normalize()`'s 404 branch
- [x] Write a failing test for the wildcard route, then add it
- [x] Full `ng test --coverage`, confirm nothing regressed
- [x] Live browser check: confirm a nonexistent-id detail page renders "not found" promptly (not stuck on the skeleton), a malformed-id URL does the same, and an unknown route redirects

**Acceptance Criteria:**
- [x] Given a query resolves to a `NotFoundError` or `BusinessRuleError`, when it fails, then it is never retried and the error/not-found UI renders on the first failed attempt
- [x] Given a query resolves to a `ServerError`, when it fails, then it is still retried a small bounded number of times (unchanged reliability for genuine transient failures)
- [x] Given a 404 response with no parseable ProblemDetails body, when normalized, then it produces `NotFoundError`, not `ServerError`
- [x] Given a completely unmatched route, when navigated to, then the app redirects to `/bookings` rather than rendering blank

## Spec Change Log

## Design Notes

**Why disable retry by error kind rather than by HTTP status directly:** `NormalizedApiError`'s `kind` discriminant already captures exactly the distinction that matters (deterministic vs. potentially-transient) — reusing it keeps this fix consistent with how every other part of the frontend already reasons about errors, rather than introducing a second, parallel status-code-based classification.

**Why a bounded retry count (2) for `ServerError` rather than TanStack Query's default (3) or disabling retries entirely:** the existing behavior for genuine transient failures should not regress: a real backend blip should still get a couple of retries before giving up. Two is a deliberate, still-small choice — this is a bug-fix for the deterministic-error case, not a broader retry-policy redesign.

## Verification

Independently reproduced (not just re-reading the implementer's report), per this project's standing verification rule.

**Commands:**
- `ng test --coverage` (own run, twice) — **373/373 passing**, including the new `app.config.spec.ts` (retry predicate) and `error-normalization.interceptor.spec.ts` (bodyless-404 case) test files.

**Code read:** `app.config.ts`'s `queryRetryPredicate` (confirmed it correctly returns `false` for `not-found`/`business-rule` kinds and bounds `server-error` retries at 2; the `unknown`-typed parameter with an internal cast is a legitimate, well-documented workaround for TanStack Query's `DefaultError`-typed `retry` slot — confirmed this was a genuine compile error the implementer hit and solved correctly, not a convenience shortcut), `error-normalization.interceptor.ts` (404 branch no longer requires a parseable body), `app.routes.ts` (wildcard route added, mirrors the existing empty-path redirect exactly).

**Live browser verification (real backend + real seeded Postgres, independently reproduced, not just trusting the implementer's own browser pass):**
- Navigated to a well-formed but nonexistent vehicle id — confirmed via `read_network_requests` that **exactly one** `GET /api/vehicles/...` fired (404), and "This vehicle no longer exists." rendered immediately — no retry delay, no stuck skeleton.
- Navigated to a malformed (non-GUID) vehicle id — confirmed the identical "not found" message rendered, not "Something went wrong."
- Navigated to `/nonsense` — confirmed it redirected to `/bookings` and rendered the real Bookings list, not a blank page.

## Suggested Review Order

1. `frontend/src/app/app.config.ts` — the `queryRetryPredicate` and its `QueryClient` wiring
2. `frontend/src/app/core/api-client/error-normalization.interceptor.ts` — the relaxed 404 classification
3. `frontend/src/app/app.routes.ts` — the wildcard route
4. `frontend/src/app/app.config.spec.ts` and `frontend/src/app/core/api-client/error-normalization.interceptor.spec.ts` — the new direct unit tests
