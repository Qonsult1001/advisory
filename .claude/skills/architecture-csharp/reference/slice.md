# Vertical slice — anatomy

A use case is a **vertical slice through one context**: Domain → Application → Infrastructure → Web.
Issues are sliced this way (a runnable thread through all layers), never as a horizontal "do all the
Domain first" layer. The snippets below carry the template's **specific conventions** (not default C#) — from the
gold-standard `OrderManagement` / `Identity` contexts. The conventions are the point, not the C# syntax.

## Domain

**Aggregate** (`Domain/Models/A/A.cs`): inherits `Entity, IAggregateRoot`, **private setters**,
behaviour methods `return this`, invariants validated in-aggregate, `RaiseEvent(...)`, **no namespace**.

```csharp
public class Order : Entity, IAggregateRoot
{
    public Order(Guid customerId, DateTime orderDate)
    { ValidateOrderDate(orderDate); /* set */ RaiseEvent(new OrderAddedEvent()); }
    public Guid CustomerId { get; private set; }           // private setter
    public Order AddOrderItem(Guid productId, int quantity) // behaviour returns this
    { ValidateOrderItemQuantity(quantity); OrderItems.Add(new OrderItem(Id, productId, quantity)); return this; }
}
```

**Factory** (`Domain/Factories/AFactory.cs`): `internal`, fluent `WithX()...Build()`, `Build()` asserts
required fields set, then `new`s the aggregate. The aggregate is never `new`'d directly in Application.

## Application — one feature folder per use case

- **Command service** (`A/Commands/X/`): depends on the **domain repository + factory**; orchestrates
  `factory.WithX().Build()` → aggregate behaviour → `repo.Save`; returns a **response DTO**, never the
  entity.
- **Query service** (`A/Queries/X/`): depends on the **query repository** (not the aggregate); returns
  a read DTO.

```csharp
public class CreateOrderService(IOrderDomainRepository repo, IOrderFactory factory) : ICreateOrderService
{
    public async Task<CreateOrderResponse> Create(CreateOrderCommand cmd, CancellationToken ct = default)
    {
        var order = factory.WithOrderDate(cmd.OrderDate).WithCustomerId(cmd.CustomerId).Build();
        cmd.OrderItems.ForEach(i => order.AddOrderItem(i.ProductId, i.Quantity));
        await repo.Save(order, ct);
        return new CreateOrderResponse(order.Id);          // DTO, not the aggregate
    }
}
```

- **Validator**: `FluentValidation` `AbstractValidator<TCommand>`, limits from `CommonModelConstants`.
- **DI**: every service registered in `AApplicationConfiguration`, chained off `AddCommonApplication`.

## Web — the controller

`Web/Features/AController.cs`: primary constructor, inherits **`ApiController`**, actions are
one-liners delegating to the service and ending in **`.ToActionResult()`**. `ApiController` gives
`api/[controller]/[action]`; route params on the verb attribute (`[HttpGet("{id}")]`), never a
separate `[Route]`. No business or data logic.

```csharp
public class IdentityController(IRegisterUserService register) : ApiController
{
    [HttpPost]
    public async Task<ActionResult> Register(RegisterUserCommand command)
        => await register.Register(command).ToActionResult();
}
```

## The slice as a checklist

For "add use case X to context C, aggregate A":

1. **Domain** `Models/A/` — new aggregate method (or value object); update `Factories/` /
   `Repositories/` interface if the shape changed.
2. **Application** `A/Commands/X/` or `A/Queries/X/` — command/query + `IXService` + `XService` +
   validator + response DTO.
3. **Infrastructure** — `Configurations/` if mapping changed; implement the repository method; migration
   only on schema change.
4. **Web** — action on `Features/AController.cs`; register `IXService`→`XService` in
   `CApplicationConfiguration`.
