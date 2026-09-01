---
name: 'Stack Version & Reality-Check Review'
type: review
target: '../ARCHITECTURE-SPINE.md'
reviewer: 'web-research spot-check'
date: '2026-08-26'
verdict: PASS WITH CONCERNS
---

# Review: Stack table & version-dependent decisions — ARCHITECTURE-SPINE.md

Scope: verify every version/technology claim in the Stack table and the AD rules that
depend on a specific library behavior, via direct web search as of 2026-08-26 (today).
No claim was accepted on training-data recall alone; each row below cites what was
found live.

## Verdict: PASS WITH CONCERNS

All named technologies exist, are current, and are compatible with each other. No
claim was found to be fabricated or clearly wrong. However, several claims are
**plausible-but-incomplete** in ways that matter for a build substrate, and one
architectural decision (AD-3, TanStack Angular Query) rests on a package that is
still explicitly experimental — a real risk the spine doesn't surface. Details below.

---

## Stack table, row by row

### .NET 10 (LTS) — CONFIRMED
Released 2025-11-11 as an LTS release, supported through 2028-11-14. As of
2026-08-26 this is the current LTS and the correct choice for a new project today.
Source: [Announcing .NET 10](https://devblogs.microsoft.com/dotnet/announcing-dotnet-10/),
[dotnet/core release notes](https://github.com/dotnet/core/blob/main/release-notes/10.0/README.md).

### Angular 22 (stable) — CONFIRMED
Released 2026-06-03, i.e. ~3 months before today's date — genuinely current, not
stale. Angular 22 is described as a "consolidation" release: Signal Forms, Zoneless,
OnPush-by-default, and Fetch-backed HttpClient are all now stable. One follow-on
constraint the spine doesn't mention: **Angular v22 requires TypeScript v6** (v5.9 is
no longer supported) — worth a line in the actual tsconfig/setup docs, not
necessarily the spine itself.
Source: [Angular blog — Announcing v22](https://blog.angular.dev/announcing-angular-v22-c52bb83a4664),
[Ninja Squad — What's new in Angular 22.0](https://blog.ninja-squad.com/2026/06/03/what-is-new-angular-22.0).

### EF Core 10 — CONFIRMED
Released alongside .NET 10 (Nov 2025), LTS, requires the .NET 10 SDK/runtime (won't
run on earlier .NET or .NET Framework) — consistent with pairing it with "`.NET 10`"
in the same row.
Source: [EF Core 10 what's new](https://learn.microsoft.com/en-us/ef/core/what-is-new/ef-core-10.0/whatsnew).

### Npgsql.EntityFrameworkCore.PostgreSQL 10.0.3 — CONFIRMED
This exact version exists on NuGet right now.
Source: [nuget.org/packages/npgsql.entityframeworkcore.postgresql/10.0.3](https://www.nuget.org/packages/Npgsql.EntityFrameworkCore.PostgreSQL/10.0.3).
**Unverifiable by search:** which minimum PostgreSQL server version Npgsql 10
formally requires — the official 10.0 release notes page doesn't state a floor.
Npgsql's general policy is to support "all currently supported PostgreSQL versions"
(~5 years back), which comfortably covers PG 17/18, but this specific claim is an
inference, not a confirmed fact from the release notes. Low risk, but flagging per
instructions as plausible-but-unconfirmed.

### PostgreSQL 17+ (18 recommended) — CONFIRMED
PostgreSQL 18 was released 2025-09-25 and has already had multiple patch releases
(18.3 in 2026, 18.6 on 2026-08-13) — it's mature enough to recommend, not a
bleeding-edge pick. 17 remains supported. The row is accurate as written.
Source: [PostgreSQL 18 Released](https://www.postgresql.org/about/news/postgresql-18-released-3142/),
[PostgreSQL 18.3/17.9/... release](https://www.postgresql.org/about/news/postgresql-183-179-1613-1517-and-1422-released-3246/).

### MediatR 14.x, Community edition — CONFIRMED, with one gap
Current MediatR is 14.2.0 (released 2026-07-02); the 14.x line targets .NET 10 and
began with the commercial-license split at v13 (2025-07-02), now under Lucky Penny
Software. The Community-edition terms as stated in AD-2 — free for individuals/orgs
under $5M gross annual revenue (non-profits under $5M budget), education, and
non-production use — match what Lucky Penny Software publishes, **plus one detail
the spine's AD-2 rule doesn't mention**: Community-edition users still must
self-register for a free license key (no cost, no approval, no credit card — but a
required step); an entity that ever received >$10M in outside VC/PE funding is
excluded regardless of revenue. Recommend adding "register a free Community license
key" as an explicit README/setup step so it isn't discovered at first run when the
license check starts logging warnings.
Source: [Lucky Penny Software licensing FAQ](https://luckypennysoftware.com/faq),
[AutoMapper 16.0 / MediatR 14.0 — .NET 10 support](https://www.jimmybogard.com/automapper-16-0-0-and-mediatr-14-0-0-released-with-net-10-support/),
[MediatR 14.2.0 on NuGet](https://www.nuget.org/packages/mediatr/).

### FluentValidation 12.1.1 — CONFIRMED
Exists on NuGet at that exact version. The companion claim that
`FluentValidation.AspNetCore` is deprecated is also CONFIRMED: the FluentValidation
team deprecated the whole package (automatic MVC pipeline integration), citing the
non-async ASP.NET Core validation pipeline and maintenance cost, and now recommend
manual validation (which is exactly what AD-10's `ValidationBehavior<TRequest,TResponse>`
MediatR pipeline pattern does — the architecture correctly avoids the deprecated
package rather than merely name-checking the deprecation).
Source: [FluentValidation.AspNetCore deprecation issue #1960](https://github.com/FluentValidation/FluentValidation/issues/1960),
[FluentValidation 12.1.1 on NuGet](https://www.nuget.org/packages/fluentvalidation/).

### TanStack Query (Angular adapter) — CONFIRMED TO EXIST, BUT FLAGGED
The package is `@tanstack/angular-query-experimental` (latest release 5.102.5, dated
2026-08-26 — actively maintained) and it **still explicitly ships under the
"-experimental" name with an explicit warning that breaking changes may land in
minor *and patch* releases**, recommending version-pinning in production. AD-3
adopts it flatly ("TanStack Query's Angular adapter for server-state fetching...")
without noting this. For a "no NgRx, one clean state story" architectural
commitment, this is worth a one-line caveat in the spine or a pinned exact version
in package.json rather than a caret range, since the library's own docs say patch
releases can break.
Source: [@tanstack/angular-query-experimental on npm](https://www.npmjs.com/package/@tanstack/angular-query-experimental),
[TanStack Query Angular docs — overview](https://tanstack.com/query/latest/docs/framework/angular/overview).

### Serilog + Serilog.Sinks.Grafana.Loki — CONFIRMED
Both are live, maintained NuGet packages (Loki sink latest at 8.3.2). No concerns.
Source: [Serilog.Sinks.Grafana.Loki on NuGet](https://www.nuget.org/packages/Serilog.Sinks.Grafana.Loki/).

### Grafana + Loki — CONFIRMED, with an adjacent note
Grafana is at 13.2.0 (2026-08-18) and Loki at v3.7.5 (2026-08-05) as of today — both
current. Not directly claimed by the spine but surfaced during research and worth
knowing if a docker-compose log-shipping agent is ever added: **Promtail reached
end-of-life on 2026-03-02** and Loki's "Simple Scalable Deployment" mode is
deprecated (removal planned for Loki 4.0) — Grafana Alloy is now the recommended
shipping agent. Since the spine ships logs directly from Serilog's Loki sink (no
Promtail in the stack), this doesn't affect the current design, but it should be
noted before anyone adds a Promtail-based log-shipping step later.
Source: [Grafana Loki docs](https://grafana.com/docs/loki/latest/),
[Loki Docker install docs](https://grafana.com/docs/loki/latest/setup/install/docker/).

---

## Non-Stack-table claims checked

### AD-6 — `Guid.CreateVersion7()` "native since .NET 9+" — CONFIRMED
`Guid.CreateVersion7()` (RFC 9562 UUIDv7) was introduced in .NET 9 and remains
available in .NET 10. Accurate.
Source: [Guid.CreateVersion7 — Microsoft Learn](https://learn.microsoft.com/en-us/dotnet/api/system.guid.createversion7).

### AD-8 — `AddProblemDetails()` + `IExceptionHandler` — CONFIRMED, minor RFC-number staleness
This pattern (`AddProblemDetails()` for the shared infrastructure, `UseExceptionHandler()`
+ `AddExceptionHandler<T>()` for handler registration) is the current, documented
.NET 10 approach. One nit: the spine says "RFC7807 ProblemDetails," but RFC 7807 was
obsoleted by **RFC 9457** in mid-2023 — ASP.NET Core's `ProblemDetails` type is
unaffected either way (same shape, same behavior), so this doesn't change any code,
but the citation itself is stale and should read RFC 9457 if precision matters.
Source: [Handle errors in ASP.NET Core — Learn (10.0)](https://learn.microsoft.com/en-us/aspnet/core/fundamentals/error-handling?view=aspnetcore-10.0).

---

## Summary of flags

| Severity | Item | Issue |
|---|---|---|
| Low | AD-3 / TanStack Angular adapter | Package is still `-experimental`, warns breaking changes possible even in patch releases — spine states it as a settled choice with no caveat or version-pinning guidance |
| Low | AD-2 / MediatR Community edition | Free tier still requires self-registering a license key; not mentioned in the rule, could surprise at first run (log warnings, not a build break) |
| Cosmetic | AD-8 | Cites "RFC7807," which RFC 9457 obsoleted in 2023; no functional impact, but the citation is out of date |
| Unverifiable | Npgsql.EntityFrameworkCore.PostgreSQL 10.0.3 | Exact minimum supported PostgreSQL server version not stated in official release notes found; inferred (not confirmed) that PG 17/18 are covered |
| None | Everything else in the Stack table | Versions, existence, and mutual compatibility all confirmed live as of 2026-08-26 |

No claim in the Stack table was found to be fabricated, discontinued, or
incompatible with its neighbors. The two "Low" items are worth one line each in the
spine or README (pin the TanStack package version; mention the free MediatR license
key step) but do not block the architecture as committed.
