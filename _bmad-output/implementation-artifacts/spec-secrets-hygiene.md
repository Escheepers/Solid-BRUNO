---
title: 'Audit fix: move committed dev secrets to user-secrets/.env/gitignored files'
type: 'bugfix'
created: '2026-09-21'
status: 'done'
review_loop_iteration: 0
context: []
baseline_commit: 'b95ac9dc6a756de68dc7a7d6616d2e3b5508c161'
---

<frozen-after-approval reason="human-owned intent — do not modify unless human renegotiates">

## Intent

**Problem:** A full-spec audit found `appsettings.Development.json` (committed) contains a real, working dev API key and a Postgres connection string with a password; `docker-compose.yml` (committed) contains that same Postgres password in plaintext; `frontend/src/environments/environment.development.ts` (committed) contains the matching dev API key. All three are clearly-labeled, low-risk, local-only placeholder values — but they technically contradict SPEC.md's literal constraint: "Secrets (API key, DB connection string) come from environment/config only, never hardcoded or committed." Confirmed with the user: fix this properly for a graded submission, accepting one extra manual setup step as the cost.

**Approach:** Mirror the project's own existing pattern for its one genuinely secret value (the MediatR license key, already sourced via `dotnet user-secrets`, never committed): move the dev API key and Postgres connection string out of `appsettings.Development.json` into `dotnet user-secrets`. Move the Postgres password out of `docker-compose.yml` into a gitignored `.env` file, with a committed `.env.example` template. Move the frontend's dev API key out of a committed `environment.development.ts` into a gitignored copy, with a committed `environment.development.ts.example` template. Update the README's setup steps to document the (now three, up from one) `dotnet user-secrets set`/copy-the-example-file steps required for a fresh clone to run.

## Boundaries & Constraints

**Always:**
- `appsettings.Development.json` keeps its structure (e.g. `Seed:Enabled: true`) but `ApiKey.Key` becomes an empty string (matching `appsettings.json`'s own existing empty default) and `ConnectionStrings.Postgres` is removed entirely — both sourced via `dotnet user-secrets set` instead, documented in the README using the exact same placeholder values as today (`local-dev-only-key-change-me`, `bruno_dev_password`) so a fresh setup produces functionally identical behavior to before, just no longer committed.
- `docker-compose.yml`'s `POSTGRES_PASSWORD` becomes `${POSTGRES_PASSWORD}`, sourced from a new, gitignored `.env` file at the repo root; a committed `.env.example` documents the exact variable name with the same placeholder value as before.
- `frontend/src/environments/environment.development.ts` becomes gitignored; a committed `environment.development.ts.example` (identical content, `.example` suffix) is the new template. `.gitignore` also needs a carve-out so the `.example` file itself isn't accidentally excluded by whatever pattern excludes the real file.
- The three new user-secrets/`.env`/environment-file setup steps are added to the README's existing "How to run it" section, immediately alongside the existing MediatR-license-key step (same style, same level of detail) — a fresh clone must still be fully runnable by following the README top-to-bottom, just with these additional one-time steps.
- Every placeholder value used in documentation/`.example` files stays exactly what it is today (`local-dev-only-key-change-me`, `bruno_dev_password`) — this is a relocation of where these values live, not a change to what they are.

**Ask First:** Nothing else expected to trigger.

**Never:** No change to the actual runtime behavior of the app (the API key/connection string/Postgres password still resolve to the same values in a correctly-set-up local dev environment) — this is purely about where those values are allowed to live in the committed tree. No attempt to add real secret-management infrastructure (Vault, Key Vault, etc.) — that's out of scope for a local-only assessment submission per SPEC.md's own Assumptions.

## I/O & Edge-Case Matrix

<!-- No new I/O/edge-case scenarios — this changes where configuration values are sourced from, not any runtime behavior a test would exercise differently. -->

</frozen-after-approval>

## Code Map

- `src/BrunoVehicleHire.Api/appsettings.Development.json` -- modify -- `ApiKey.Key` → `""`; remove `ConnectionStrings.Postgres` entirely
- `docker-compose.yml` -- modify -- `POSTGRES_PASSWORD: bruno_dev_password` → `POSTGRES_PASSWORD: ${POSTGRES_PASSWORD}`
- `.env.example` -- new -- `POSTGRES_PASSWORD=bruno_dev_password`
- `.gitignore` -- modify -- add `.env` (backend/root) and `frontend/src/environments/environment.development.ts`, with an explicit `!frontend/src/environments/environment.development.ts.example` carve-out
- `frontend/src/environments/environment.development.ts.example` -- new -- identical content to the current (about-to-be-removed-from-git) `environment.development.ts`
- `frontend/src/environments/environment.development.ts` -- remove from git tracking (`git rm --cached`), keep the working-tree file as-is so local dev is uninterrupted
- `README.md` -- modify -- "How to run it" section gains steps for: `dotnet user-secrets set "ApiKey:Key" "..."`, `dotnet user-secrets set "ConnectionStrings:Postgres" "..."`, copying `.env.example` → `.env`, copying `environment.development.ts.example` → `environment.development.ts` -- placed alongside the existing MediatR-license-key step, before "Run the backend"/"Run the frontend"
- `src/BrunoVehicleHire.Api/appsettings.json` -- modify (found during implementation, see Spec Change Log) -- remove the same hardcoded `ConnectionStrings.Postgres` value from the base (non-Development) file
- `src/BrunoVehicleHire.Infrastructure/Persistence/AppDbContextFactory.cs` -- modify (found during implementation, see Spec Change Log) -- replace its hardcoded password with an unambiguous, never-really-used placeholder

## Tasks & Acceptance

**Execution:**
- [x] Update `appsettings.Development.json`, `docker-compose.yml`, add `.env.example`
- [x] Update `.gitignore`; add `environment.development.ts.example`; `git rm --cached` the real `environment.development.ts` (verify the working-tree file is untouched/still present locally)
- [x] Update README with the three new setup steps
- [x] Verify end-to-end from a simulated fresh state: follow the updated README's steps in order (set user-secrets, copy `.env.example`/`environment.development.ts.example`), confirm `docker-compose up -d`, `dotnet run`, and `npm start` all work exactly as before
- [x] Confirm `git status` shows `environment.development.ts` as untracked (not deleted) after the `git rm --cached`, and that the app still runs locally using its current, unchanged content

**Acceptance Criteria:**
- [x] Given a completely fresh clone of the repository, when a developer follows the README's updated setup steps in order, then the app runs identically to how it does today — same dev API key, same DB password, same behavior
- [x] Given `git log`/`git show` on the new commit, when inspected, then no real API key or DB password value appears newly-committed anywhere (only placeholder/example files with the same non-sensitive dev values, matching what was already public in the previous commit's history — this fix prevents *future* commits from continuing to carry them forward as live config, it does not (and cannot) purge them from prior git history)

## Spec Change Log

- **Two residual instances of the same class of issue found during independent verification, outside the original Code Map:** (1) `src/BrunoVehicleHire.Api/appsettings.json` (the base, non-Development file — always loaded regardless of environment) also hardcoded `ConnectionStrings.Postgres` with the exact same plaintext password. Fixed by removing the key entirely (mirroring `ApiKey.Key`'s existing empty-string-in-base pattern), so it correctly falls through to the app's existing friendly "connection string not configured" exception when neither environment override nor user-secrets supplies it — confirmed no test relies on this base value (every `WebApplicationFactory`-based test already overrides `ConnectionStrings:Postgres` itself, and the two exceptions found by grep, `ExampleTests.cs`/`BookingCompletionSweepServiceTests.cs`, don't actually touch a real DB connection through the full host). (2) `src/BrunoVehicleHire.Infrastructure/Persistence/AppDbContextFactory.cs` (used only by `dotnet ef migrations add`/`database update` design-time tooling) also hardcoded the same password — but its own doc comment already states this connection string is "never used at application runtime," only needing to be syntactically valid for Npgsql to generate SQL offline. Fixed by replacing the value with an unambiguous `design-time-only-unused` placeholder rather than sourcing it from user-secrets, since routing inert, never-connected tooling config through the same secret-management machinery as a real credential would be disproportionate.

## Design Notes

**Why the same placeholder values are kept, not rotated:** these values were never sensitive in any real security sense (a local-only dev Postgres instance, an assessment-only API key) — the fix is about the *pattern* (secrets sourced from environment/local files, not committed config) matching what the spec's constraint literally asks for, not about actually rotating a credential that was never at risk. Keeping the same values also means this fix is a pure relocation, verifiable by diffing behavior before/after and finding none.

**Why this doesn't rewrite git history:** the previous commits already contain these values in the repository's history regardless of what's fixed going forward — purging history (`git filter-repo`/BFG) is a destructive, disruptive operation explicitly out of scope for a bugfix commit, and unnecessary here since the values were never real secrets to begin with. This fix's contract is "stop committing this going forward," not "erase that it was ever committed."

## Verification

Independently reproduced (not just re-reading the implementer's report), per this project's standing verification rule — including independently finding and fixing the two residual instances logged above.

**Commands:**
- `dotnet build BrunoVehicleHire.sln` (own run) — 0 warnings, 0 errors, after both the implementer's changes and my own two supplementary fixes.
- `dotnet test BrunoVehicleHire.sln` (own run, full solution) — 436/436 passing, confirming no test relied on the now-removed base `appsettings.json` connection string or the changed `AppDbContextFactory.cs` placeholder.

**Live verification, independently reproduced (the real proof for this spec):** started the real API (`dotnet run`) against the actual dev Postgres container — it started cleanly and served real `GET /api/vehicles`/`GET /api/customers` requests successfully, confirming the relocated `ApiKey:Key`/`ConnectionStrings:Postgres` values resolve correctly from user-secrets on a genuinely fresh process start (not just the implementer's own claimed check). Confirmed the dev API key value returned by a real authenticated request still matches the documented `local-dev-only-key-change-me`.

**Code/file read:** `.gitignore` (confirmed `.env` and the real `environment.development.ts` are excluded, with the `.example` carve-out correctly scoped), `appsettings.Development.json`/`appsettings.json` (confirmed both `ApiKey.Key`/`ConnectionStrings.Postgres` are gone, not just emptied, from the Development file, and the base file's connection string is fully removed), `docker-compose.yml` (confirmed `${POSTGRES_PASSWORD}` substitution), `.env.example` and `environment.development.ts.example` (confirmed both carry the exact same non-sensitive placeholder values as before, per the Design Notes' "pure relocation" intent), `README.md` (confirmed the three new setup steps read clearly and are placed consistently with the existing MediatR-license-key step's style).

## Suggested Review Order

1. `README.md` — the three new setup steps, read as a first-time cloner would
2. `src/BrunoVehicleHire.Api/appsettings.Development.json` and `appsettings.json` — confirm both no longer carry a real connection string/API key value
3. `docker-compose.yml` and `.env.example` — the Postgres password relocation
4. `.gitignore` and `frontend/src/environments/environment.development.ts.example` — the frontend secret relocation
5. `src/BrunoVehicleHire.Infrastructure/Persistence/AppDbContextFactory.cs` — the design-time-only placeholder fix (found during verification, not in the original Code Map)
