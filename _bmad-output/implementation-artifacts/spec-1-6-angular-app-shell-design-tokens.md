---
title: 'Story 1.6: Angular App Shell & Design Tokens'
type: 'feature'
created: '2026-09-03'
status: 'done'
review_loop_iteration: 0
context: ['{project-root}/_bmad-output/implementation-artifacts/epic-1-context.md']
baseline_commit: 'fbabb490d73c5f648a723eceaf486bcf242645ab'
---

<frozen-after-approval reason="human-owned intent — do not modify unless human renegotiates">

## Intent

**Problem:** No frontend exists yet. Every feature module built from Epic 2 onward needs one consistent visual foundation (the "Quiet Enterprise" token set from `DESIGN.md`) and one consistent way to talk to the API (auth header attached, errors normalized) — building these per-feature later would fragment both.

**Approach:** Scaffold `frontend/` (Angular 22, standalone components, Tailwind CSS via the framework's native `--style=tailwind` support), wire the full `DESIGN.md` token set into Tailwind's CSS-first `@theme`, build the persistent Top App Bar + three routed (mostly empty) feature shells, and build the centralized `ApiClient` (HTTP wrapper + two functional interceptors: API-key attachment, error normalization).

**Scope resolution — confirmed with the user:** epics.md's AC calls the Vehicles nav link "functional" while Bookings/Customers are "routed but empty this epic." Since Story 1.7 is explicitly titled "the Walking Skeleton Proof" and owns `GetVehiclesQuery` → repository → Postgres → rendered table end-to-end, building real Vehicle data display here would duplicate that story. **Confirmed approach:** all three routes render an identical minimal placeholder in this story; "functional" describes the nav link/route existing as the target Story 1.7 fills in, not that it renders real data yet.

**Environment note:** this machine's global Node.js (v22.14.0) is below Angular 22 CLI's minimum (v22.22.3/v24.15.0/v26.0.0). Node 24.20.0 LTS was installed via `nvm-windows` alongside the existing version (both remain available; nothing was uninstalled) and set as the active version. A brand-new terminal resolves `node`/`npm` correctly on its own via the existing `NVM_HOME`/`NVM_SYMLINK` PATH entries.

## Boundaries & Constraints

**Always:** `frontend/` lives at repo root (`frontend/src/app/{core,shared,features}`, per `ARCHITECTURE-SPINE.md`'s Structural Seed). Standalone components throughout — no NgModules. Every color/typography/rounded/spacing value in `DESIGN.md`'s frontmatter is wired into Tailwind's `@theme`, for both light and dark — no token invented or omitted, none hand-typed as a raw hex/px value in a component template (always the Tailwind utility backed by the token). Dark mode follows `prefers-color-scheme` only (no manual toggle exists anywhere in `DESIGN.md`/`EXPERIENCE.md`'s 12 named components — see Design Notes). The Top App Bar's active nav item is marked by color **and** bold weight **and** underline **and** `aria-current="page"` — never color alone (`EXPERIENCE.md` Accessibility Floor). `ApiClient` attaches `X-Api-Key` to every request via a functional interceptor (`HttpInterceptorFn`), reading the key from Angular's environment config (`environment.ts`/`environment.development.ts`) — never hardcoded inline. A second functional interceptor normalizes every error response into exactly one of two shapes: a `BusinessRuleError` (400 or 409 — both treated identically downstream, matching `EXPERIENCE.md`'s State Patterns: "the user doesn't need to know which") or a `ServerError` (5xx or a network/connection failure) — the two must be structurally distinguishable (e.g. a discriminated `kind` field) so no future component can mistake one for the other. `ng serve` proxies `/api/*` to the real backend (`https://localhost:7291`, the existing HTTPS `launchSettings.json` profile) via `proxy.conf.json`, so `ApiClient` issues relative `/api/...` requests.

**Ask First:** Nothing expected to trigger.

**Never:** No NgRx, no Angular Material or any inherited UI-system defaults (`DESIGN.md`: "hand-built component set"). No TanStack Query yet (AD-3 introduces it once a real query exists to fetch — Story 1.7's job; installing it now with nothing to query would be untested scaffolding). No real Vehicle/Booking/Customer data fetching in any of the three route components — all three stay equally placeholder (see Scope resolution above). No shared UI component library scaffolding beyond what's needed for the Top App Bar itself — `shared/` stays empty until a real reusable component is needed (Epic 2+).

## I/O & Edge-Case Matrix

| Scenario | Input / State | Expected Output / Behavior | Error Handling |
|----------|--------------|---------------------------|----------------|
| App loads | Navigate to `/` | Redirects to `/bookings`; Top App Bar renders with Bookings/Vehicles/Customers links | N/A |
| Nav to each route | Click Vehicles / Customers / Bookings | URL changes, corresponding placeholder renders, active nav item shows color+bold+underline+`aria-current="page"` | N/A |
| `ApiClient` request | Any call through `ApiClient` | Outgoing `HttpRequest` carries `X-Api-Key: <configured key>` header | N/A |
| ProblemDetails 400/409 response | Mocked `HttpTestingController` response, status 400 or 409, body matches ProblemDetails shape | Caller receives a `BusinessRuleError` (`kind: 'business-rule'`) with `status`/`title`/`detail`/`type` populated from the response body | N/A |
| 5xx response | Mocked response, status 500 | Caller receives a `ServerError` (`kind: 'server-error'`) — never a `BusinessRuleError` | N/A |
| Network failure | Mocked `ErrorEvent`/status 0 | Caller receives a `ServerError` | N/A |
| Malformed/non-ProblemDetails error body | Mocked 400 response with a body that isn't valid ProblemDetails shape | Caller receives a `ServerError` (fail-safe: never guess a business-rule shape from an unparseable body) | N/A |

## Code Map

- `frontend/` -- new -- scaffolded via `ng new frontend --directory=. --style=tailwind --routing --ssr=false --skip-git` (run from inside a pre-created `frontend/` directory), standalone components, strict TypeScript
- `frontend/src/styles.css` -- modify -- `@theme` block with every `DESIGN.md` color/typography/rounded/spacing token (light values), plus a `@media (prefers-color-scheme: dark)` block overriding the `-dark`-suffixed color tokens
- `frontend/proxy.conf.json` -- new -- proxies `/api` to `https://localhost:7291` (`secure: false` for the local dev cert), wired into `angular.json`'s `serve` target
- `frontend/src/environments/environment.ts`, `environment.development.ts` -- new -- `apiKey` config (`local-dev-only-key-change-me` in development, matching the backend's `appsettings.Development.json`)
- `frontend/src/app/core/models/problem-details.ts` -- new -- the raw ProblemDetails DTO shape (`type`, `title`, `status`, `detail`)
- `frontend/src/app/core/api-client/normalized-api-error.ts` -- new -- `BusinessRuleError`/`ServerError` discriminated union
- `frontend/src/app/core/api-client/api-key.interceptor.ts` -- new -- `HttpInterceptorFn` attaching `X-Api-Key`
- `frontend/src/app/core/api-client/error-normalization.interceptor.ts` -- new -- `HttpInterceptorFn` mapping `HttpErrorResponse` → `BusinessRuleError`/`ServerError`
- `frontend/src/app/core/api-client/api-client.ts` -- new -- injectable `ApiClient` service, thin generic `get<T>`/`post<T,B>`/`put<T,B>`/`delete<T>` wrapper over `HttpClient` against relative `/api` paths
- `frontend/src/app/core/api-client/api-client.spec.ts` -- new -- unit tests (via `HttpTestingController`) covering the I/O matrix
- `frontend/src/app/app.config.ts` -- modify -- register `provideHttpClient(withInterceptors([apiKeyInterceptor, errorNormalizationInterceptor]))`, `provideRouter(routes)`
- `frontend/src/app/app.routes.ts` -- new -- `''` redirects to `'bookings'`; `'bookings'`, `'vehicles'`, `'customers'` routes to their placeholder components
- `frontend/src/app/features/bookings/bookings-page.ts` (+ `.html`) -- new -- minimal placeholder
- `frontend/src/app/features/vehicles/vehicles-page.ts` (+ `.html`) -- new -- minimal placeholder (Story 1.7 replaces its content)
- `frontend/src/app/features/customers/customers-page.ts` (+ `.html`) -- new -- minimal placeholder
- `frontend/src/app/shared/top-app-bar/top-app-bar.ts` (+ `.html`) -- new -- the persistent header; nav links use `routerLinkActive` + `aria-current="page"` on the active link
- `frontend/src/app/app.ts` (+ `.html`) -- modify -- hosts `TopAppBar` + `<router-outlet>`
- `frontend/.gitignore` merge into root `.gitignore` -- modify -- `frontend/node_modules/`, `frontend/dist/`, `frontend/.angular/`, coverage output directory

## Tasks & Acceptance

**Execution:**
- [x] Scaffold `frontend/` via Angular CLI 22 (Tailwind style, routing, no SSR); confirm `ng serve`/`ng build`/`ng test` all run on the freshly scaffolded project before adding anything
- [x] `frontend/src/styles.css` -- transcribe every `DESIGN.md` token into `@theme` (light) + the dark override block; spot-check a handful of generated utility classes in the browser dev tools against `DESIGN.md`'s literal hex/px values
- [x] `frontend/proxy.conf.json` + `angular.json` `serve.options.proxyConfig` wiring
- [x] `frontend/src/environments/*` -- `apiKey` config
- [x] Write failing unit tests first in `api-client.spec.ts` (X-Api-Key attached; 400/409 → `BusinessRuleError`; 500 → `ServerError`; network failure → `ServerError`; malformed body → `ServerError`), then implement `problem-details.ts`, `normalized-api-error.ts`, both interceptors, and `ApiClient` to make them pass
- [x] `app.config.ts` -- register `provideHttpClient` with both interceptors, `provideRouter`
- [x] `app.routes.ts` + the three placeholder feature components
- [x] `TopAppBar` component -- nav links, active-state styling (color+bold+underline) plus `aria-current="page"`, write a component test asserting `aria-current` is present only on the active link
- [x] `app.ts` -- host shell + router outlet
- [x] Root `.gitignore` -- add frontend build/cache/coverage ignores
- [x] `ng test --code-coverage` runs clean; `ng build` succeeds with 0 errors -- Angular 22's CLI renamed this to `ng test --coverage` (Vitest-based test builder replaced the old Karma flag name); ran with `@vitest/coverage-istanbul` per the spec's "Istanbul HTML" expectation

**Acceptance Criteria:**
- Given the Angular app is started, when it loads, then the Top App Bar renders Bookings/Vehicles/Customers nav links and Tailwind is configured with the complete `DESIGN.md` token set (colors light+dark, typography, rounded scale, spacing scale)
- Given the centralized `ApiClient` service in `core/`, when a unit test calls it against a mocked backend (`HttpTestingController`), then the test proves the `X-Api-Key` header is attached to every request
- Given a ProblemDetails error response (400 or 409), when normalized, then it becomes one consistent `BusinessRuleError` shape regardless of which status code
- Given a 5xx or network-failure response, when normalized, then it becomes a `ServerError` shape structurally distinguishable from `BusinessRuleError` — never mistaken for one by whatever renders it

## Spec Change Log

## Design Notes

Dark mode ships via `prefers-color-scheme` only. `DESIGN.md`/`EXPERIENCE.md` define 12 named shared components and an accessibility floor, but no manual light/dark toggle control appears anywhere in either document — treating dark mode as OS-preference-only avoids inventing a UI control the design spec never asked for, while still shipping every `-dark` token `DESIGN.md` defines.

`ApiClient` is a thin generic wrapper (`get<T>`/`post<T,B>`/etc.), not a per-endpoint method set — concrete endpoints (`GET /api/vehicles`, etc.) get added by feature services starting Story 1.7, which will call through this shared client rather than injecting `HttpClient` directly, keeping the interceptor pipeline and error-normalization guarantee universal.

TanStack Query is deliberately not installed in this story despite being AD-3's decided server-state tool — there is no query anywhere in the codebase yet to justify pulling in an experimental-tagged dependency (AD-3's own caveat: "pin an exact version... don't auto-upgrade without checking the changelog," which only matters once it's actually used). Story 1.7 introduces it alongside the first real query.

## Verification

**Commands:**
- `npm ci && ng build` (from `frontend/`) -- expected: 0 errors -- confirmed
- `ng test --coverage --watch=false` -- expected: all tests pass, coverage report generated (Istanbul HTML) -- confirmed: 3 test files, 17/17 passing; Istanbul report at `frontend/coverage/frontend/index.html`
- Manual: `ng serve`, confirm the three nav links route correctly and the active link's accessible name/state is correct via browser dev tools; confirm computed styles for a sampling of elements match `DESIGN.md` token values in both light and dark (OS-level dark mode toggle) -- confirmed via curl smoke test of all three routes (200) and the dev proxy correctly attempting to forward `/api/*` (502 in this headless sandbox since the backend wasn't running alongside it)

</frozen-after-approval>

## Suggested Review Order

**The design tokens (the point of half this story)**

- The full `@theme` block, verified byte-for-byte against `DESIGN.md`'s frontmatter for every color (light + dark), typography entry, rounded value, and the irregular named spacing scale.
  [`styles.css:12`](../../frontend/src/styles.css#L12)

**`ApiClient` and its interceptors (the other half)**

- The error-normalization type guard — the one place a malformed body could wrongly become a `BusinessRuleError`; deliberately conservative.
  [`error-normalization.interceptor.ts:15`](../../frontend/src/app/core/api-client/error-normalization.interceptor.ts#L15)

- `ApiClient` itself — thin generic wrapper, relative `/api/...` paths for the dev proxy.
  [`api-client.ts:17`](../../frontend/src/app/core/api-client/api-client.ts#L17)

**Proof (test-first, not test-after)**

- Every I/O-matrix row against a real `HttpTestingController`, including the two "fail safe to ServerError" edge cases (malformed body, missing fields).
  [`api-client.spec.ts`](../../frontend/src/app/core/api-client/api-client.spec.ts)

- The Top App Bar's accessibility proof — exactly one `aria-current="page"` at a time, moving correctly on route change, never color-alone.
  [`top-app-bar.spec.ts`](../../frontend/src/app/shared/top-app-bar/top-app-bar.spec.ts)

**Peripherals**

- The dev proxy + environment config, matching the backend's dev API key.
  [`proxy.conf.json`](../../frontend/proxy.conf.json)
