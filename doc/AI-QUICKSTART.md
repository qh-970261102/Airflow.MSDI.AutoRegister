# AI / LLM Quick Start

> **Purpose**: a compact, machine-friendly reference for AI coding assistants (Cursor, Copilot, ChatGPT, Claude, …) and humans who want a 2-minute refresher.
> Package: **Airflow.MSDI.AutoRegister 2.0** · Target: Microsoft.Extensions.DependencyInjection 8.0+ · Requires C# 11 for generic attributes.
> 中文用户:概念与示例同样适用,详见 [README.zh-CN.md](./README.zh-CN.md)。

## How to use this file

Paste this whole file (or the "Prompt template" section at the end) into your AI assistant's context, then describe the services you want to register. The assistant should follow the rules below instead of inventing its own registration code.

## What the library does

A Roslyn **source generator**. You decorate classes/methods with attributes and mark one `partial` method with `[AutoRegister]`; at compile time the generator writes the body of that method with plain `services.Add…` calls (no reflection). It also generates, for every service type, a registration-metadata list and an aggregate `IEnumerable<T>` that includes keyed + non-keyed implementations.

## Golden rules

1. **Never write `services.AddSingleton/AddScoped/AddTransient/AddKeyed…` by hand** for attributed services — the generator does it. Hand-written registrations are not visible to the generated collections.
2. **Exactly one `[AutoRegister]` partial method per assembly**; the containing class must be `partial` and the method `static partial` with signature `(IServiceCollection services)`.
3. **One attribute = one registration.** Stack attributes to register the same class as itself and as interfaces.
4. **Named keys must be unique per target type.** Two different implementations cannot use the same key for the same service type (`AMAR2009`, error). Registrations **without** a key may repeat (classic "multiple implementations, `Order` decides the winner").
5. **Key is a `string`.** Do not pass `int`/enum/`Type` keys — they are not supported in 2.0.
6. **Same implementation + same lifetime = one instance**, regardless of interface or key. Internally the generator emits one `object` anchor per (implementation, lifetime) and every attribute becomes an alias over it; different lifetimes get different anchors.
7. **Factory methods**: owning class is auto-registered as a non-keyed singleton; method must be `public` instance, no parameters or exactly one `IServiceProvider`; singleton factories must not take `IServiceProvider` (`AMAR1010`); return type must be a reference type.
8. **Use `GetRequiredKeyedService<T>(key)`** to resolve keyed services; `GetRequiredService<T>()` resolves only non-keyed registrations.
9. Generic form is `[SingletonService<Target>]` (type argument), **not** `[SingletonService(Target)]` — the latter is invalid C# unless you pass `typeof(Target)`.
10. `Order` and `Key` are **named properties**: `[SingletonService<IFoo>(Key = "k", Order = 2)]`.

## Minimal template (copy-paste)

```csharp
using Microsoft.Extensions.DependencyInjection;

namespace MyApp;

// 1) Services
[SingletonService]                                  // self, non-keyed
public sealed class Clock : IClock { }

[ScopedService(typeof(IUnitOfWork))]                // legacy typeof form
public sealed class UnitOfWork : IUnitOfWork { }

[SingletonService<INotifier>(Key = "email")]        // generic form + key
public sealed class EmailNotifier : INotifier { }

[SingletonService(typeof(INotifier), Key = "sms")]  // legacy + key
public sealed class SmsNotifier : INotifier { }

// 2) Registration entry (exactly one per assembly)
internal static partial class Register
{
    [AutoRegister]
    public static partial void AddServicesFromCurrentAssembly(IServiceCollection services);

    public static IServiceCollection AddServices(this IServiceCollection services)
    {
        AddServicesFromCurrentAssembly(services);
        return services;
    }
}

// 3) Usage
var services = new ServiceCollection();
services.AddServices();
var provider = services.BuildServiceProvider();

var clock = provider.GetRequiredService<Clock>();
var email = provider.GetRequiredKeyedService<INotifier>("email");
var all = provider.GetServices<INotifier>().ToList();          // keyed + non-keyed
var table = provider.GetRequiredService<IEnumerable<RegisterItemInfo<INotifier>>>().ToList();
```

## API cheat sheet

### Attributes

| Attribute | Generated registration (alias over the implementation's anchor) |
|---|---|
| `[SingletonService]` on `C` | `AddSingleton<C>(sp => sp.GetRequiredKeyedService<C>(anchor))` |
| `[ScopedService]` on `C` | `AddScoped<C>(sp => sp.GetRequiredKeyedService<C>(anchor))` |
| `[TransientService]` on `C` | `AddTransient<C>(sp => sp.GetRequiredKeyedService<C>(anchor))` |
| `[SingletonService(typeof(I))]` on `C : I` | `AddSingleton<I>(sp => sp.GetRequiredKeyedService<C>(anchor))` |
| `[SingletonService<I>]` on `C : I` | `AddSingleton<I>(sp => sp.GetRequiredKeyedService<C>(anchor))` |
| `[SingletonService(Key = "k")]` on `C` | `AddKeyedSingleton<C>("k", (sp, _) => sp.GetRequiredKeyedService<C>(anchor))` |
| `[SingletonService(typeof(I), Key = "k")]` on `C : I` | `AddKeyedSingleton<I>("k", (sp, _) => sp.GetRequiredKeyedService<C>(anchor))` |
| `[SingletonService]` + `[SingletonService(typeof(I))]` on `C : I` | two aliases on the **same** anchor → same instance |
| Factory method on owner `O` | `AddSingleton<O>()` + `AddKeyedSingleton<R>(anchor, (sp, _) => sp.GetRequiredService<O>().M())` + alias |

The anchor (`object __keyN = new object();` + `AddKeyed{Lifetime}T(anchor)`) is generated for **every** (implementation, lifetime), including single-registration cases — so keyed and non-keyed aliases always share one instance.

Closed generic service types work as-is: `[SingletonService<IRepo<Order>>] class OrderRepo : IRepo<Order>` → `AddSingleton<IRepo<Order>, OrderRepo>()`.

Generic classes themselves cannot be registered (`AMAR2003`) — derive a concrete class that closes the type parameters, then register that.

### Shared properties (on any registration attribute)

| Property | Type | Meaning |
|---|---|---|
| `Key` | `string?` | service key; `null`/omitted = non-keyed |
| `Order` | `double` | registration order (last wins per target type + key); also orders collections |

### Factory methods

```csharp
public sealed class ConnectionFactory
{
    private readonly IOptions<DbOptions> _options;
    public ConnectionFactory(IOptions<DbOptions> options) => _options = options;

    [SingletonService]                                  // service type = return type (IDbConnection)
    public IDbConnection Create() => new SqlConnection(_options.Value.ConnectionString);

    [ScopedService]                                     // may take IServiceProvider
    public IUnitOfWork CreateUnitOfWork(IServiceProvider sp) =>
        new UnitOfWork(sp.GetRequiredService<IDbConnection>());
}
```

- `[SingletonService]` on a method **with** `IServiceProvider` → `AMAR1010` error.
- Generic target can override the service type: `[SingletonService<IHandler>(Key = "a")] public HandlerA CreateA() => new();` (return type must be convertible to `IHandler`).

### Generated metadata & collections

```csharp
// metadata: one entry per registration (key, implementation, lifetime, order)
IEnumerable<RegisterItemInfo<INotifier>> table =
    provider.GetRequiredService<IEnumerable<RegisterItemInfo<INotifier>>>();

// aggregate collection: keyed + non-keyed implementations in one place
IEnumerable<INotifier> all = provider.GetServices<INotifier>();
```

`RegisterItemInfo<TService>` lives in namespace `Airflow.MSDI.AutoRegister` and exposes `Key`, `ImplementationType`, `ServiceType`, `Lifetime`, `Order`.

Reading the table does not require boilerplate — use the built-in helpers instead:

```csharp
var items = provider.GetRegisterItems<INotifier>();          // IReadOnlyList<RegisterItemInfo<INotifier>> (empty if none)
var item  = provider.FindRegisterItem<INotifier>("email");   // RegisterItemInfo<INotifier>? — null when missing
var map   = provider.GetRegisterMap<INotifier>();            // IReadOnlyDictionary<string, Type> (key → implementation)

foreach (var pair in items.ToKeyValuePairs())                // key → implementation, unkeyed entries have null key
    Console.WriteLine($"{pair.Key ?? "<none>"} -> {pair.Value.Name}");

var byKey = items.FindByKey("email");                        // same lookup on a list you already have
```

### Custom attributes

```csharp
[RegistrationOptions(
    LifetimeIndex = -1,
    ServiceTypeIndex = 0,
    OrderIndex = 1,
    DefaultLifetime = ServiceLifetime.Transient)]
public sealed class MyServiceAttribute : BaseServiceAttribute
{
    public MyServiceAttribute(Type? serviceType = null, double order = 0)
        : base(ServiceLifetime.Transient, serviceType, order) { }
}
```

`[RegistrationOptions]` can provide defaults for anything the constructor does not take: `DefaultLifetime`, `DefaultServiceType`, `DefaultOrder`. Example (see the demo's `ViewAttribute`): `[View]` registers as `IView` (Transient) by default, `[View(typeof(ISpecialView))]` overrides the target, `[View(Order = 1)]` sets the order.

A generic attribute class with exactly one type parameter is treated as "target type".

## Diagnostics → what to change

| ID | Fix |
|---|---|
| AMAR1001/1002/1003/1004/1005/1006/1007/1008/1009/1010 | Fix the factory method shape (plain owner class, `public` instance method, no/one `IServiceProvider` parameter, reference return type; no `sp` on singleton factories) |
| AMAR2001 | Move the attribute to a class or an ordinary method |
| AMAR2002 | Use an interface or class as the target type; no arrays/open generics/value types |
| AMAR2003 | Close the generic type parameters with a concrete class |
| AMAR2005 | Duplicate registration (same type + key + implementation) — remove one |
| AMAR2007 | The implementation does not implement/inherit the target type |
| AMAR2008 | Implementation is abstract/static — make it concrete |
| AMAR2009 | Two implementations share a named key for the same target type — give each its own key |
| AMAR3001/3002/3003/3004/3005/3007 | Make `[AutoRegister]` `static partial` inside a `partial` class, signature `(IServiceCollection services)`, no body, only one per assembly |
| AMAR4001–4007 | Fix `[RegistrationOptions]` / generic attribute usage |

## Common AI mistakes to avoid

1. **Writing manual registrations** next to attributes — they bypass metadata/collections.
2. **`[SingletonService(IFoo)]`** — invalid; use `[SingletonService<IFoo>]` or `[SingletonService(typeof(IFoo))]`.
3. **`[SingletonService(Key = 5)]`** — key must be a string.
4. **Two implementations with the same key** — error; use distinct keys or no keys at all.
5. **Resolving keyed services with `GetRequiredService<T>()`** — use `GetRequiredKeyedService<T>(key)`.
6. **Forgetting `[AutoRegister]`** — no code is generated at all. Also remember: exactly one entry method per assembly.
7. **Expecting hand-written registrations in `IEnumerable<T>`** — the generated collection only contains attributed registrations.
8. **Multiple `[AutoRegister]` methods "to split the file"** — only the first is used; the rest report `AMAR3007`.
9. **Singleton factory with `IServiceProvider` parameter** — `AMAR1010`; move the dependency to the owning class constructor.
10. **Assuming keyed services work on .NET 6/7** — keyed resolution requires MSDI 8.0+ (.NET 8+).
11. **Expecting `AddSingleton<I, T>()` / `AddSingleton<T>()` lines** — v2 always emits one `object` anchor per implementation+lifetime plus aliases; that is expected, not a bug.

## Prompt template

Copy this into your assistant:

```text
You are writing C# code for a project that uses the NuGet package
"Airflow.MSDI.AutoRegister" version 2.0 (MSDI source generator).

Rules:
- Register services ONLY with [SingletonService] / [ScopedService] / [TransientService]
  (generic forms like [SingletonService<IFoo>] are allowed), never with manual services.Add* calls
  for attributed services.
- Key/Order are named properties: [SingletonService<IFoo>(Key = "primary", Order = 1)].
  Keys are strings and must be unique per target type; omit the key to allow multiple implementations.
- Exactly one [AutoRegister] static partial method per assembly:
    internal static partial class Register
    {
        [AutoRegister] public static partial void AddServices(IServiceCollection services);
    }
- Factory methods must be public instance methods returning a reference type,
  with no parameters or exactly one IServiceProvider; singleton factories must not take it.
- Resolve keyed services with GetRequiredKeyedService<T>(key).
- If you need the registration list use IEnumerable<RegisterItemInfo<T>> from
  namespace Airflow.MSDI.AutoRegister; implement-to-interface collections come from GetServices<T>().

Given the following services, write the decorated classes and the Register entry:

<describe your services here>
```

## See also

- Full README: [../README.md](../README.md) · Chinese: [README.zh-CN.md](./README.zh-CN.md)
