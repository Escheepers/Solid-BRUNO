# Solution Design — Bruno Vehicle Hire

*Narrative companion to `ARCHITECTURE-SPINE.md`. The spine is the binding contract (terse, rule-shaped); this document explains the reasoning behind it in prose suitable for dropping into a README's "Architecture" section.*

## What this is

A full-stack vehicle hire system (Vehicles, Customers, Bookings) built as a Solid-level technical assessment. The architecture demonstrates Clean Architecture + CQRS on a .NET 10 backend and a feature-based Angular 22 frontend, with particular attention to two things the brief explicitly evaluates: correct enforcement of cross-entity business rules, and production-level thinking beyond the literal CRUD ask (PII protection, DB-level race safety, secure-by-default auth).

## Why Clean Architecture + CQRS

The brief mandates this paradigm explicitly, so it wasn't an open choice — but it's the right one regardless: it forces business rules (no double-booking, no deleting a customer with bookings, no booking a retired vehicle) to live inside the domain model rather than scattered across controllers, which is exactly what "domain modelling quality" and "correct enforcement of business rules" grade for. Four layers, each only allowed to depend inward:

```
Api → Application → Domain
Infrastructure → Application → Domain
```

Domain has zero framework dependencies — no EF Core, no ASP.NET Core, nothing. It's the part of the codebase that would survive a total rewrite of everything else.

## The hard part: booking overlap

The one rule the brief calls non-negotiable — no two bookings can overlap for the same vehicle — sounds simple and has two real failure modes if you don't think it through:

1. **The boundary bug.** Does a booking ending on the 5th conflict with one starting on the 5th? We treat `EndDate` as checkout day, exclusive — same-day turnover is allowed, which also matches how rental businesses actually operate.
2. **The race condition.** Two people could both check "is this vehicle free?", both get "yes," and both book it, if the check and the insert aren't atomic. An application-level check (load existing bookings, compare ranges) is necessary but not sufficient — so there's also a Postgres `EXCLUDE USING GIST` constraint that makes a genuinely overlapping insert physically impossible at the database level, independent of application logic. Belt and suspenders: the app-level check gives good error messages; the DB constraint gives correctness guarantees no application bug can violate.

## PII: doing it the way a real system would

The brief only asked for basic CRUD, but customer records carry real personal data (email, phone), so this build treats it the way production software should, not the way a take-home minimally could:

- **Encrypted at rest**, via an EF Core value converter backed by ASP.NET Core's Data Protection API — the framework-managed way to do field-level AES encryption, rather than hand-rolling key/IV handling.
- **Two distinct deletion operations**, not one conflated action. This came out of a real design tension: "soft delete" (reversible, for accidental-delete recovery) and "anonymize" (irreversible, for an actual erasure request) are opposites by nature — if you can undo an anonymization, it never really erased anything. So they're separate commands: soft-delete hides a customer without touching their data; anonymize permanently scrubs it. Neither can do the other's job.
- **Anonymized customers stay historically visible but operationally invisible** — they can't be booked again, but their name (now a placeholder) still resolves correctly on old booking records, so history isn't corrupted by the erasure.

## Frontend: modern Angular, deliberately not NgRx

Angular 22's Signals plus TanStack Query cover this app's actual state needs — local UI state and server-state caching — without the ceremony of a full Redux-style store, which would be disproportionate for three CRUD-ish feature modules. Each feature module (`vehicles`, `customers`, `bookings`) owns its own query and mutation logic, but all of them route through one shared, centralized API client — so there's one place that knows how to attach the API key, one place that normalizes errors, and every feature benefits from that without duplicating it.

## Errors the frontend can actually act on

Every API error comes back as a [ProblemDetails](https://www.rfc-editor.org/rfc/rfc9457) response with a consistent, predictable shape. The dividing line between a `400` and a `409` is simple and mechanical: if the check only needs the request itself (is this field present, is this email well-formed), it's a `400`. If it needs to look something up first (is this vehicle already booked, does this vehicle even still exist), it's a `409`. That consistency is what lets the Angular error interceptor show a specific, useful inline message — "this vehicle is already booked those dates" — instead of a generic failure toast.

## Testing

Unit tests use xUnit with NSubstitute for mocking repositories — fast, no I/O. But some things (does the unique-email constraint actually work? does the overlap-prevention database constraint actually fire?) can't be honestly tested without a real Postgres instance, since EF Core's in-memory test provider doesn't enforce relational constraints at all. `Testcontainers.PostgreSql` spins up a real, disposable Postgres in Docker for those integration tests — the only way to know the database-level safety net actually catches what it's supposed to.

## What's deliberately not built

- **Domain Events, Unit of Work beyond the basics, rate limiting** — bonus/nice-to-have per the brief; built only if time allows after the graded core is solid.
- **Redis caching, CI/CD, cloud deployment** — out of scope; this runs locally via `docker-compose`. Noted here rather than built, so the reasoning is visible without spending the assessment's time budget on infrastructure nobody asked for.
- **Grafana + Loki log dashboard** — a deliberate stretch goal, sequenced dead last and fully decoupled from everything else, specifically because it's the one piece that could be dropped without touching anything the assessment actually grades.

## How the pieces map to what's graded

| Evaluator looks at... | Where to find it |
|---|---|
| Domain modelling / DDD | `Domain/Bookings/DateRange.cs`, aggregate factory methods (no public setters anywhere) |
| Business rule enforcement | Overlap check (app-level + DB constraint), delete/cancel guards, soft-delete filters |
| Architectural consistency | The AD-numbered rules in `ARCHITECTURE-SPINE.md` — every one exists because two people could otherwise have built it two different, incompatible ways |
| Frontend modularization | `frontend/src/app/features/*`, each a self-contained vertical slice mirroring the backend |
| API quality | Swagger, ProblemDetails, pagination/filtering, API-key auth |
| Edge case handling | Overlap boundary semantics, race-condition backstop, anonymize/soft-delete interaction, PII redaction in error messages |
| Production-level thinking | PII encryption + key persistence, secure-by-default auth, the deferred-items list itself (knowing what to leave out is part of the judgment being evaluated) |
