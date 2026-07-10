# Conventions — the rulebook

The DDD bounded-context rulebook for a clean-architecture .NET solution, with a generic
`OrderManagement` context as the gold-standard reference. This file **is** the source of truth; the
inline snippets here and in [reference/slice.md](./reference/slice.md) carry the conventions.

## Bounded context = four projects

Each business area is a folder `Template/{Context}/` with up to four projects:

```
{Context}/
├── {Context}.Domain/
├── {Context}.Application/
├── {Context}.Infrastructure/
└── {Context}.Web/
```

`ProjectStartup/` is the composition root (host only) — it references each context's
**Infrastructure** and **Web** and wires DI. It holds no business logic. `Common.*` is the shared
kernel, **not** a context.

## Dependency direction (inward only)

| From | May reference | Must NOT reference |
|------|---------------|--------------------|
| `{Context}.Domain` | `Common.Domain` | its own App/Infra/Web; **any** other context |
| `{Context}.Application` | own `Domain`, `Common.Application` | own Infra, own Web, **any** `{Other}.*` |
| `{Context}.Infrastructure` | own App + Domain, `Common.Infrastructure` | own Web; any `{Other}.Domain` |
| `{Context}.Web` | own Application, `Common.Web` | own Domain, own Infra, **any** `{Other}.*` |

Cross-context = events (`IEventHandler<T>` + contract in `Common.Domain`) or HTTP ports
(`Contracts/` interface + `HttpServices/` client). Never a project reference.

## Allowed folders per layer

**Domain** — `Models/{Aggregate}/`, `Factories/`, `Repositories/` (interfaces only), `Events/`
(context-local), `Services/` (rare, pure), `Specifications/` (optional). No EF, HTTP, or app-dispatch.

**Application** — vertical slice per feature:
```
{Feature}/
├── Commands/{UseCase}/   ← command type, I*Service, *Service, validator, response DTO
├── Queries/{UseCase}/    ← query type, I*Service, *Service, read DTO
└── Common/               ← feature-wide DTOs/helpers (optional)
```
Plus roots: `Handlers/` (`IEventHandler<T>`), `Contracts/` (ports to other contexts), `Services/`
(thin orchestration not tied to one slice), `Settings/` (`IOptions<T>` types). No DbContext, no
repository implementations, no controllers, no `{Other}.Domain` types.

**Infrastructure** — `Persistence/` (DbContext : BaseDbContext, DbInitializer), `Configurations/`
(`IEntityTypeConfiguration<>`), `Repositories/` (impl of domain + query interfaces — one class may
do both), `Migrations/`, `HttpServices/` (typed clients), `Services/` (JWT, files, email),
`Extensions/`. No handlers, no controllers, no domain invariants.

**Web** — `Features/` (controllers, one per aggregate/area). Optional `Filters/`, `Models/` (only
when a request/response can't live on the command/query). No EF, no repositories, no handlers,
no validators, no direct DbContext calls.

Root `*Configuration.cs` files (`DomainConfiguration.cs`, `WebConfiguration.cs`,
`{Context}ApplicationConfiguration.cs`, `InfrastructureConfiguration.cs`) are allowed at project root.

## Naming & code style (evidence from the template)

- **No namespaces.** Implicit global namespace in every project. A `namespace` declaration is a bug.
- **Aggregate** — `class Order : Entity, IAggregateRoot`. Private setters. Behaviour methods validate
  invariants in-aggregate and return `this` (fluent). Raise events with `RaiseEvent(new XEvent())`.
- **Factory** — `internal class OrderFactory : IOrderFactory` with `WithX(...)` setters returning
  `this` and a `Build()` that asserts all required fields are set.
- **Repository** — interface in Domain: `IOrderDomainRepository : IDomainRepository<Order>`; queries
  via `IOrderQueryRepository`. Implementations live in Infrastructure `Repositories/`.
- **Service** — `class CreateOrderService : ICreateOrderService` with a **verb** method
  (`Create`, `GetDetails`, `Register`, `ChangePassword`). Takes the command/query + a
  `CancellationToken cancellationToken = default`. Commands return a response DTO, never an entity.
- **Validator** — `class XCommandValidator : AbstractValidator<XCommand>` (FluentValidation), one per
  command, limits pulled from `CommonModelConstants` / `{Aggregate}ModelConstants`.
- **DI** — registered in `{Context}ApplicationConfiguration.Add{Context}Application(...)` as
  `.AddScoped<IXService, XService>()`, chained off `.AddCommonApplication(configuration, Assembly)`.
- **Controller** — `public class OrdersController(IXService x, ...) : ApiController` (primary
  constructor). Inherits `ApiController` → route `api/[controller]/[action]`. Each action:
  `=> await x.Verb(command).ToActionResult();`. Route params go on the verb attribute
  (`[HttpGet("{id}")]`), not a separate `[Route("{id}")]`.
- **Result** — services return `Result` / `Result<T>`; Web maps via `.ToActionResult()`.

## Violation table (flag on sight)

| Violation | Correct placement |
|-----------|-------------------|
| `namespace Foo { ... }` | Remove — implicit global namespace |
| Controller calling `DbContext` / a repository | Call an Application `*Service` |
| `Web` referencing own `Domain` | Web sees Application DTOs only |
| Context A referencing Context B's project | Event (`Common.Domain`) or HTTP port (`Contracts/`+`HttpServices/`) |
| EF type / repo implementation in `Application` | Move to Infrastructure |
| Repository implementation in `Domain` | Domain holds the **interface** only |
| Service returning an entity to `Web` | Return a response DTO |
| Controller not inheriting `ApiController` / custom routes | Inherit `ApiController`, use `api/[controller]/[action]` |
| Business rule in a controller or repository | Domain (invariant) or Application (orchestration) |
| Feature logic in `Common.*` | Move to the owning context |

## New use case — the checklist

1. **Domain** — extend `Models/{Aggregate}/`; update `Factories/`, `Repositories/` if needed.
2. **Application** — add `{Feature}/Commands/{UseCase}/` or `Queries/{UseCase}/`.
3. **Infrastructure** — update `Configurations/`, `Repositories/`; migration only if schema changes.
4. **Web** — add the action on `Features/{Aggregate}Controller.cs`; register the service in
   `{Context}ApplicationConfiguration`.
