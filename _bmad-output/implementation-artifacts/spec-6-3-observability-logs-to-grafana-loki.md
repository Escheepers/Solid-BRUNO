---
title: 'Story 6.3 (Optional Stretch): Observability — Logs to Grafana/Loki'
type: 'feature'
created: '2026-09-16'
status: 'done'
review_loop_iteration: 0
context: []
baseline_commit: '613bb03739e1feb6cba6fc4e3350e6d8c44b0b4f'
---

<frozen-after-approval reason="human-owned intent — do not modify unless human renegotiates">

## Intent

**Problem:** All logging today is the ASP.NET Core default (console only) — there's no way to demonstrate production-level observability thinking, and this story's own AC frames it as an explicitly optional stretch goal whose absence must change nothing else.

**Approach:** Serilog replaces the default logging provider (a drop-in for every existing `ILogger<T>` call site, e.g. `BookingCompletionSweepService`'s error log — no call site changes needed), with a Loki sink and structured request logging; Loki + Grafana run as a separate, opt-in Docker Compose stack, with Grafana pre-provisioned with a datasource and one real dashboard panel — no manual Grafana UI setup for an evaluator to get right.

**Scope decisions — flagged for approval:**
1. **Loki + Grafana live in a new, separate `docker-compose.observability.yml`**, started via its own explicit `docker-compose -f docker-compose.observability.yml up -d` — not merged into the main `docker-compose.yml`. This story is explicitly optional/stretch; bundling it into the base compose file would make it de facto mandatory infrastructure for anyone who just wants to run the app.
2. **The Loki sink itself is gated by a `Serilog:Loki:Enabled` config flag** (default `false`), not always-on — so a developer who never starts the observability stack doesn't get connection-refused retry noise in their console. Console logging (via Serilog) is unconditional either way.
3. **No new PII-redaction machinery is added.** The app already never logs request/response bodies or raw entity objects anywhere (`UseSerilogRequestLogging()`'s default output is method/path/status/elapsed-time only); this story adds one automated test proving a customer's plaintext email never appears in any captured log event, rather than building speculative scrubbing infrastructure against a leak that doesn't exist in the current codebase.
4. **Automated testing is scoped to what's genuinely testable in-process** (the "no PII in logs" claim, via an in-memory Serilog sink). Whether logs actually reach Loki and render in a real Grafana panel is verified manually/visually — mirroring this project's own precedent for the print-stylesheet verification in Story 5.1, where no automated tooling exists in this stack for that class of proof either.

## Boundaries & Constraints

**Always:**
- Serilog is wired as the app's logging provider via `builder.Host.UseSerilog(...)`; every existing `ILogger<T>` call site (e.g. the sweep service's error log) automatically flows through it, unmodified.
- `UseSerilogRequestLogging()` middleware provides the structured per-request log line (method, path, status code, elapsed time) — the source of the dashboard's request-rate/rejected-request panel.
- Grafana is provisioned entirely via files (a Loki datasource + at least one dashboard JSON) mounted into the container — opening Grafana requires no manual "add datasource"/"import dashboard" steps.
- The dashboard's one required panel renders real data as soon as the API receives any traffic (e.g. a request-rate panel), not one that requires deliberately triggering a specific rare condition first.
- `README.md` gains a short "Observability (optional)" section: the separate compose command, the Grafana URL/default login, and an explicit statement that this story's absence changes nothing else (mirrors the AC's own framing).

**Ask First:** Nothing else expected to trigger.

**Never:** No merging Loki/Grafana into the main `docker-compose.yml`. No PII-scrubbing infrastructure beyond proving the current codebase doesn't leak any. No CI wiring (this project has none).

## I/O & Edge-Case Matrix

| Scenario | Input / State | Expected Output / Behavior | Error Handling |
|----------|--------------|---------------------------|----------------|
| `Serilog:Loki:Enabled=true`, Loki running | API handles a request | A structured log line ships to Loki | N/A |
| `Serilog:Loki:Enabled=false` (default) | API runs normally | Console logging only, no Loki connection attempted | N/A |
| A request carries a customer's email (e.g. create-customer) | Any captured log event | The plaintext email never appears in any log event's message or properties | N/A |
| The observability stack is never started | Rest of the submission evaluated | No other story's behavior or tests are affected | N/A |

## Code Map

**Backend:**
- `src/BrunoVehicleHire.Api/BrunoVehicleHire.Api.csproj` -- modify -- add `Serilog.AspNetCore`, `Serilog.Sinks.Grafana.Loki`
- `src/BrunoVehicleHire.Api/Program.cs` -- modify -- `builder.Host.UseSerilog(...)` (Console sink always; Loki sink only if `Serilog:Loki:Enabled`); `app.UseSerilogRequestLogging()` early in the pipeline
- `src/BrunoVehicleHire.Api/appsettings.json` -- modify -- add `"Serilog": { "Loki": { "Enabled": false, "Url": "http://localhost:3100" } }`
- `tests/BrunoVehicleHire.Api.Tests/*` or `tests/BrunoVehicleHire.Integration.Tests/*` -- new -- an in-memory Serilog sink capturing every log event from a real request carrying a customer's email; asserts the plaintext email never appears in any captured event

**Observability stack:**
- `docker-compose.observability.yml` -- new -- `loki` + `grafana` services, Grafana depending on `loki`, Grafana provisioning volumes mounted read-only
- `observability/grafana/provisioning/datasources/loki.yaml` -- new -- the Loki datasource, pre-configured
- `observability/grafana/provisioning/dashboards/dashboard.yml` -- new -- tells Grafana to load dashboards from a mounted folder
- `observability/grafana/dashboards/bruno-api-overview.json` -- new -- at least one panel (a request-rate panel, per Boundaries) querying Loki via LogQL

**Docs:**
- `README.md` -- modify -- the "Observability (optional)" section per Boundaries

## Tasks & Acceptance

**Execution:**
- [x] Wire Serilog (Console always, Loki gated by config) + `UseSerilogRequestLogging()` — confirmed in `Program.cs`
- [x] Write the in-memory-sink PII test first (TDD), confirm it passes against the current codebase's already-clean logging — `CustomerLoggingPiiTests`, passing
- [x] Author `docker-compose.observability.yml` + Grafana provisioning files + the dashboard JSON
- [x] Manual end-to-end proof: start the observability stack, run the API with `Serilog:Loki:Enabled=true`, generate some real traffic, open Grafana, confirm the provisioned dashboard's panel renders real, non-zero data with no manual setup — independently reproduced, see Verification
- [x] Update `README.md`
- [x] Full `dotnet test` (confirm nothing broke); `dotnet restore --force`, check `NU1903` — 414/414 passing, no advisories

**Acceptance Criteria (epics.md, verbatim intent):**
- [x] Given Serilog with the Loki sink configured and Loki+Grafana added as Compose services, structured logs ship to Loki when the API runs
- [x] Given Grafana is opened, the provisioned dashboard's panel renders real data from the running system
- [x] Given any log line shipped to Loki, no PII appears in plaintext
- [x] Given this story is not completed, nothing else about the submission is affected

## Spec Change Log

## Design Notes

**Why a request-rate panel, not a business-rule-violation-count panel:** both satisfy the AC ("e.g. request rate, or a count of rejected/business-rule-violation requests"), but a request-rate panel renders real, non-zero data the instant the API receives any traffic at all — including from an evaluator just browsing the Angular UI — while a 409-count panel would show nothing until someone deliberately triggers a business-rule violation first. The simpler, always-populated panel is the better demo for a stretch goal that should visibly work with minimal effort.

</frozen-after-approval>

## Verification

Independently reproduced from scratch (not just re-reading the implementer's report), since the implementing subagent's task-notification initially returned truncated/incomplete ("I'll stop here and wait for the background `dotnet test` task...") — a full independent redo was required per this project's standing verification rule:

- **Build:** `dotnet build BrunoVehicleHire.sln` — 0 warnings, 0 errors.
- **Full test suite (own run, not the subagent's):** `dotnet test BrunoVehicleHire.sln` — **414/414 passing** (Domain 84, Application 144, Infrastructure 4, Api 23, Integration 159 — includes `CustomerLoggingPiiTests` and the refactored `ApiKeyAuthenticationTests`). An earlier run reported 54 failures, root-caused to a stale `BrunoVehicleHire.Api.deps.json` from a concurrent `dotnet build` racing the test run's own output folder — not a real regression; the clean rebuild-then-test sequence above is the authoritative result.
- **Dependency check:** `dotnet restore --force` — clean, no `NU1903` advisories on the new Serilog/Loki packages.
- **Code read:** `Program.cs`'s `UseSerilog` wiring (`ClearProviders()` before `UseSerilog(..., writeToProviders: true)`, Console unconditional, Loki gated behind `Serilog:Loki:Enabled`, `UseSerilogRequestLogging()` as the first pipeline middleware), `CustomerLoggingPiiTests.cs`, and the shared `TestSupport/CapturingLoggerProvider.cs` all read in full and confirmed to match the spec's Design Notes and Boundaries exactly.
- **Config-gating files:** `appsettings.json` (`Serilog:Loki:Enabled: false` default) and the observability provisioning files (`loki.yaml`, `dashboard.yml`, `bruno-api-overview.json`) read in full — the datasource's fixed `uid: loki` correctly matches the dashboard JSON's own datasource reference; the panel's LogQL (`sum(count_over_time({app="bruno-vehicle-hire-api"} |= "HTTP" [1m])) by (app)`) targets exactly the message shape `UseSerilogRequestLogging()` emits.
- **Core proof, independently reproduced end-to-end (this is the part that actually matters for this story):**
  1. Started the observability stack myself: `docker-compose -f docker-compose.observability.yml up -d` — Loki became healthy, Grafana started.
  2. Ran the real API (`dotnet run --no-build`) with `Serilog__Loki__Enabled=true` against the dev Postgres, generated real HTTP traffic (`GET /api/vehicles`, `/api/customers` with a valid API key, 13 requests total).
  3. Queried Loki directly (`/loki/api/v1/query_range`) — confirmed real shipped log lines, including the exact `HTTP {RequestMethod} {RequestPath} responded {StatusCode}` structured events the dashboard targets.
  4. Ran the dashboard panel's *exact* LogQL query against Loki's HTTP API — returned `value: 8`, matching the request count sent up to that point (not a placeholder/zero result).
  5. Logged into Grafana at `localhost:3000` with the documented `admin`/`bruno-admin` credentials — **no forced password-change interstitial**, confirming that scope decision. Navigated to `/d/bruno-api-overview` — the dashboard was already there with zero manual import/add-datasource steps, titled "Bruno Vehicle Hire API Overview".
  6. Generated more traffic and screenshotted the live dashboard: the "API request rate (per second)" panel rendered real, non-zero green bars (8–12 req/s) tracking the actual requests sent, updating live with the panel's 5s auto-refresh.
  7. Confirmed via raw Loki query that shipped log lines contain only method/path/status/elapsed/DB-command text — no PII.
- **PII-in-logs automated proof:** `CustomerLoggingPiiTests.Post_CreateCustomerWithEmail_NoCapturedLogEventEverContainsThePlaintextEmail` passes as part of the 414/414 run — a real end-to-end `WebApplicationFactory` + Testcontainers Postgres test asserting a POST-ed customer email never appears in any captured Serilog event (message or structured property).
- **Silent-when-disabled:** confirmed via the subagent's own reproducible check (re-verified by code read of the `if (context.Configuration.GetValue<bool>("Serilog:Loki:Enabled"))` gate) — default `dotnet run` never attempts a Loki connection.
- **Cleanup:** stopped the local API process, `docker-compose -f docker-compose.observability.yml down -v` (containers + `loki-data` volume removed). Final `docker ps -a` shows only the normal `bruno-postgres` container — no stray containers, no orphaned Testcontainers instances left running.
- **SOLID/DRY/YAGNI:** confirmed — Serilog wiring is a single composition-root concern; every existing `ILogger<T>` call site required zero changes (Open/Closed preserved, AD-1's Domain/Application logging-framework independence untouched); `CapturingLoggerProvider` was extracted to `TestSupport/` on its second real consumer (DRY, rule of two); no speculative PII-scrubbing infrastructure, no CI wiring, exactly one dashboard panel (YAGNI).

## Suggested Review Order

1. `src/BrunoVehicleHire.Api/Program.cs` — Serilog wiring (`ClearProviders`, `UseSerilog`, `UseSerilogRequestLogging` placement)
2. `tests/BrunoVehicleHire.Integration.Tests/TestSupport/CapturingLoggerProvider.cs` and `CustomerLoggingPiiTests.cs` — the PII proof
3. `tests/BrunoVehicleHire.Integration.Tests/ApiKeyAuthenticationTests.cs` — confirm the DRY extraction is behavior-preserving
4. `docker-compose.observability.yml` and `observability/grafana/**` — the opt-in stack and its provisioning
5. `README.md`'s "Observability (optional)" section
