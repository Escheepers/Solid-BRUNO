---
title: 'Story 6.2: Code Coverage Reports'
type: 'feature'
created: '2026-09-16'
status: 'done'
review_loop_iteration: 0
context: []
baseline_commit: '562e506530e7135e53a86d61c26288f0e5aec2a8'
---

<frozen-after-approval reason="human-owned intent — do not modify unless human renegotiates">

## Intent

**Problem:** Coverlet is already referenced in every backend test project and the frontend's Vitest-based `ng test --coverage` already prints a terminal coverage table (both established from the very start of this build), but neither produces a durable, browsable HTML artifact an evaluator can actually open and click through.

**Approach:** A local .NET tool manifest pinning `dotnet-reportgenerator-globaltool`, converting the Cobertura XML `dotnet test --collect:"XPlat Code Coverage"` already produces into a browsable HTML report; a dedicated npm script for the frontend's existing Istanbul HTML output; both referenced from the README, both `.gitignore`d as generated artifacts, neither wired to any CI (this project has none).

**Scope decisions — flagged for approval:**
1. **epics.md's AC names the frontend flag `--code-coverage`; this codebase's actual, already-working, already-used-all-session flag is `--coverage`** (this project's `ng test` runs on Vitest, not the older Karma-based Angular test runner epics.md's phrasing presumably assumed). The README documents the flag that actually works in this repo, not the AC's literal string.
2. **A local (not global) dotnet tool manifest** (`dotnet new tool-manifest` + `dotnet tool install dotnet-reportgenerator-globaltool`) so `dotnet tool restore` is the only setup step a fresh clone needs — no assumption the evaluator has (or wants) a machine-wide global tool install.
3. **No new automated test for this story** — there is no application behavior to unit-test; the acceptance proof is that running the documented commands genuinely produces a browsable HTML report in the stated location, verified manually (and independently re-verified, per this project's standing practice) rather than via an automated test asserting file existence.

## Boundaries & Constraints

**Always:**
- `dotnet test --collect:"XPlat Code Coverage"` (already works, Coverlet is already referenced everywhere) followed by `reportgenerator` produces a browsable `index.html` under a documented, `.gitignore`d output directory.
- `ng test --coverage --watch=false` (the frontend's own already-correct invocation, used throughout this entire build) produces a browsable Istanbul HTML report under `frontend/coverage/`, already `.gitignore`d.
- Both report locations are referenced from `README.md`, with the exact commands to regenerate them.
- Both are explicitly documented as local, on-demand artifacts — never wired to a CI pipeline (this project has none, so trivially true, but the README says so explicitly per the AC).

**Ask First:** Nothing else expected to trigger.

**Never:** No CI/pipeline wiring of any kind. No committed coverage output (HTML reports and raw Cobertura/`.trx` files are all generated, `.gitignore`d artifacts).

## Code Map

- `.config/dotnet-tools.json` -- new -- local tool manifest pinning `dotnet-reportgenerator-globaltool`
- `.gitignore` -- modify -- add patterns for the backend's Cobertura output directory and the generated HTML report directory (frontend's `coverage/` is already ignored)
- `README.md` -- modify -- new "Code Coverage Reports" section: exact commands for both backend and frontend, output paths, the "local/on-demand, not CI" note
- `frontend/package.json` -- modify -- add a `"test:coverage": "ng test --coverage --watch=false"` script (a clean, discoverable one-liner; Scope decision 1's flag correction lives here)

## Tasks & Acceptance

**Execution:**
- [x] Add the local tool manifest; confirm `dotnet tool restore` + `dotnet test --collect:"XPlat Code Coverage"` + `reportgenerator` produces a genuinely browsable `index.html`
- [x] Add the frontend npm script; confirm it produces a browsable `frontend/coverage/frontend/index.html`
- [x] Update `.gitignore`; confirm neither report's output is visible in `git status` after generating both -- confirmed unnecessary: the existing generic `[Tt]est[Rr]esult*/`/`[Cc]overage*/` patterns already cover both, verified via `git status`
- [x] Update `README.md` with the new section
- [x] Full `dotnet test` (confirm nothing broke); `dotnet restore --force`, check `NU1903` -- confirmed: 413/413 passing, 0 advisories, 0 npm vulnerabilities

**Acceptance Criteria (epics.md, verbatim intent, adapted per Scope decision 1):**
- Given `dotnet test --collect:"XPlat Code Coverage"`, when `ReportGenerator` processes the resulting Cobertura output, a browsable HTML report is produced locally and referenced from the README
- Given the frontend's coverage command, when the test suite completes, an Istanbul-based HTML coverage report is produced and referenced from the README
- Given both reports, they are local, on-demand artifacts, not wired to any CI pipeline

## Spec Change Log

- **Implementation**: the Code Map's planned `.gitignore` modification turned out to be unnecessary. The repo's existing `.gitignore` already carries generic `[Tt]est[Rr]esult*/` and `[Cc]overage*/` patterns (case-insensitive, matched at any depth), which already cover both the backend's root-level `TestResults/`/`coverage/backend/` and the frontend's `coverage/` output without any new entry. Confirmed via `git status` after generating both reports -- neither appeared. No file was touched for this Code Map line.

## Design Notes

**Why a local tool manifest, not a global tool install instruction:** a global `dotnet tool install -g` asks the evaluator to install something machine-wide just to view this one project's coverage; a local manifest (`dotnet-tools.json`, committed) makes `dotnet tool restore` the only step, consistent with this project's overall "no undocumented steps, nothing assumed already on the machine" README philosophy from Story 6.1.

</frozen-after-approval>

## Verification

Implemented directly (no dispatch — a small, self-contained tooling/docs story), verified by actually running every documented command, not just reading them:

- `dotnet new tool-manifest` initially landed at the repo root (`dotnet-tools.json`); recreated correctly under `.config/` (the convention `dotnet tool restore` expects), then confirmed `dotnet tool restore` from the repo root finds and restores it.
- `dotnet test --collect:"XPlat Code Coverage" --results-directory ./TestResults` produced 5 `coverage.cobertura.xml` files (one per test project); `dotnet reportgenerator -reports:"./TestResults/**/coverage.cobertura.xml" -targetdir:"./coverage/backend" -reporttypes:Html` produced a genuine, browsable `coverage/backend/index.html` ("Summary - Coverage Report" title, per-class HTML pages) in under a second.
- `npm run test:coverage` (the new script) produced a genuine, browsable `frontend/coverage/frontend/index.html` with per-folder index pages, coverage numbers matching the same 97.84%/92.12%/96.33%/97.76% figures already established as of Story 5.1 — confirming nothing regressed.
- `git status` after generating both reports showed neither — the existing `.gitignore` patterns already cover them, so the Code Map's planned `.gitignore` edit was correctly skipped (see Spec Change Log).
- Full `dotnet test`: 413/413 passing. `dotnet restore --force`: no `NU1903` advisories. `npm audit`: 0 vulnerabilities.
- Read the new README section and confirmed it states plainly that both reports are local/on-demand, never CI-wired (trivially true — this project has no CI), and documents the actual working `--coverage` flag rather than epics.md's literal `--code-coverage` string, with the discrepancy explained inline.

## Suggested Review Order

1. `.config/dotnet-tools.json` -- the local tool manifest
2. `README.md`'s new "Code coverage reports" section
3. `frontend/package.json` -- the new `test:coverage` script
