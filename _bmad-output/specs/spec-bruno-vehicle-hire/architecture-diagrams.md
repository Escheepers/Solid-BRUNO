# Architecture Diagrams

## Angular frontend folder layout (mandated by brief for the chosen framework)

```
src/app/
├── core/
├── shared/
├── features/
│   ├── vehicles/
│   ├── customers/
│   └── bookings/
```

## Backend Clean Architecture layering (conceptual, per brief)

```
src/
├── Domain/            (entities, value objects, business rules, domain events)
├── Application/        (commands, queries, DTOs, validation - CQRS via MediatR)
├── Infrastructure/     (EF Core, repository implementations, migrations)
└── Api/                (thin controllers, Swagger, middleware)
```
