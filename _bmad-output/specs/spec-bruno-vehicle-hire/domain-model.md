# Domain Model

Entities, fields, and business rules as mandated by the assessment brief. These rules are non-negotiable (see SPEC.md Constraints).

## Vehicle

| Field | Type | Notes |
|---|---|---|
| Id | Guid | |
| RegistrationNumber | string | unique |
| Make | string | |
| Model | string | |
| Year | int | |
| DailyRate | decimal | |
| IsDeleted | bool | soft-delete flag |
| CreatedDate | datetime | |

Rules:
- Soft delete only — never hard-delete a vehicle.
- Cannot book a soft-deleted vehicle.
- Cannot book overlapping date ranges for the same vehicle.

## Customer

| Field | Type | Notes |
|---|---|---|
| Id | Guid | |
| FirstName | string | |
| LastName | string | |
| Email | string | unique, encrypted at rest |
| PhoneNumber | string | encrypted at rest |
| CreatedDate | datetime | |
| IsDeleted | bool | soft-delete flag (added beyond brief, for PII handling — see below) |
| IsAnonymized | bool | anonymization flag (added beyond brief, for PII handling — see below) |

Rules:
- Hard delete allowed only if the customer has no bookings (per brief).
- If the customer has bookings, hard delete is blocked. Two separate alternatives are offered instead:
  - **Soft delete** (reversible) — hides the customer from default listings/new bookings; PII stays intact and encrypted; fully restorable at any time.
  - **Anonymize** (irreversible) — permanently replaces FirstName/LastName/Email/PhoneNumber with placeholders; cannot be undone; existing bookings keep referencing the customer id unaffected.
- Soft-delete and anonymize are never conflated: soft-delete never destroys PII, anonymize never un-does itself.

## Booking

| Field | Type | Notes |
|---|---|---|
| Id | Guid | |
| VehicleId | Guid | FK → Vehicle |
| CustomerId | Guid | FK → Customer |
| StartDate | date | |
| EndDate | date | |
| TotalPrice | decimal | |
| Status | enum | Active / Completed / Cancelled |
| CreatedDate | datetime | |

Rules:
- Cannot overlap bookings for the same vehicle.
- EndDate must be greater than StartDate.
- Cannot delete a past booking.
- Can delete only if the booking is in the future.

## Cross-entity constraints

- A booking's vehicle must not be soft-deleted at creation time.
- A booking's date range must not overlap any other active booking for the same vehicle (candidate DDD exercise: model as a `DateRange` value object with an overlap check, per the brief's suggestion).
- A customer cannot be deleted while any booking (of any status) references it.
