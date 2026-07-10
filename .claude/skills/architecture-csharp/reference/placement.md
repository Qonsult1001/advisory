# Placement — the four-project skeleton

Reached when SKILL.md Q1 decides a **new context** (its own language + invariants, would otherwise
pollute). That decision is hard-to-reverse → offer an ADR and add the context to `CONTEXT-MAP.md`.
This page is the skeleton to create once that decision is made.

## The four-project skeleton (new context)

For a new context `{Context}`, create four projects under `Template/{Context}/`, mirroring
`OrderManagement`:

```
{Context}/
├── {Context}.Domain/
│   ├── {Context}.Domain.csproj          → references Common.Domain
│   ├── DomainConfiguration.cs
│   ├── Models/{Aggregate}/
│   │   ├── {Aggregate}.cs               : Entity, IAggregateRoot
│   │   ├── {Aggregate}ModelConstants.cs
│   │   └── {Aggregate}Status.cs         (Enumeration, if needed)
│   ├── Factories/
│   │   ├── I{Aggregate}Factory.cs
│   │   └── {Aggregate}Factory.cs        (internal)
│   └── Repositories/
│       └── I{Aggregate}DomainRepository.cs   : IDomainRepository<{Aggregate}>
│
├── {Context}.Application/
│   ├── {Context}.Application.csproj      → references own Domain, Common.Application
│   ├── {Context}ApplicationConfiguration.cs  → Add{Context}Application(...)
│   └── {Feature}/
│       ├── Commands/{UseCase}/           (command, I*Service, *Service, validator, response)
│       └── Queries/{UseCase}/            (query, I*Service, *Service, read DTO, I*QueryRepository)
│
├── {Context}.Infrastructure/
│   ├── {Context}.Infrastructure.csproj   → references own Application + Domain, Common.Infrastructure
│   ├── InfrastructureConfiguration.cs    → Add{Context}Infrastructure(...)
│   ├── Persistence/
│   │   ├── {Context}DbContext.cs         : BaseDbContext
│   │   └── {Context}DbInitializer.cs
│   ├── Configurations/
│   │   └── {Aggregate}Configuration.cs   : IEntityTypeConfiguration<{Aggregate}>
│   ├── Repositories/
│   │   └── {Aggregate}Repository.cs      (implements domain + query interfaces)
│   └── Migrations/                       (only if EF migrations are used)
│
└── {Context}.Web/
    ├── {Context}.Web.csproj              → references own Application, Common.Web
    ├── WebConfiguration.cs               → Add{Context}WebComponents()
    └── Features/
        └── {Aggregate}Controller.cs      : ApiController
```

Then wire it up:

- Add the four projects to `ASP.NET-Domain-Driven-Design-Template.sln`.
- In `ProjectStartup`, reference `{Context}.Infrastructure` and `{Context}.Web`, and call
  `Add{Context}Application` / `Add{Context}Infrastructure` / `Add{Context}WebComponents` in the
  host's registration chain (follow how an existing context is wired).
- Package versions come from `Directory.Packages.props` (central package management) — don't pin
  versions in the `.csproj`.

## What goes where (one-line reminder)

| Artifact | Project | Folder |
|----------|---------|--------|
| Aggregate, value object, entity | Domain | `Models/{Aggregate}/` |
| Factory (interface + impl) | Domain | `Factories/` |
| Repository **interface** | Domain | `Repositories/` |
| Command/query + service + validator + DTO | Application | `{Feature}/Commands|Queries/{UseCase}/` |
| Event handler | Application | `Handlers/` |
| Port to another context | Application | `Contracts/` |
| DbContext, initializer | Infrastructure | `Persistence/` |
| EF entity configuration | Infrastructure | `Configurations/` |
| Repository **implementation** | Infrastructure | `Repositories/` |
| Typed HTTP client to another context | Infrastructure | `HttpServices/` |
| Controller | Web | `Features/` |

Cross-context event contracts and shared base types → `Common.*`, never a context.
