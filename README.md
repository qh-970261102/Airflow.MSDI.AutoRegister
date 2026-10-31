# Airflow.MSDI.AutoRegister

[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](LICENSE)
[![.NET](https://img.shields.io/badge/.NET-netstandard2.0%20%2B-blueviolet)](src/Airflow.MSDI.AutoRegister)

A **source generator** for Microsoft.Extensions.DependencyInjection (MSDI) that auto-registers your services at compile time — keyed services, generic registration attributes, registration metadata and aggregate collections, **no reflection, zero runtime overhead**.

> 中文文档: [README.zh-CN.md](https://github.com/qh-970261102/Airflow.MSDI.AutoRegister/blob/main/doc/README.zh-CN.md) · **Using an AI assistant?** See [doc/AI-QUICKSTART.md](https://github.com/qh-970261102/Airflow.MSDI.AutoRegister/blob/main/doc/AI-QUICKSTART.md) — a compact, copy-pasteable guide for LLMs.

---

## Why?

Manually registering dozens of services is tedious and error-prone. Reflection-based scanners (like Scrutor) work, but they cost runtime performance and can't be trimmed. This library moves registration to **compile time**:

- ✅ Zero runtime reflection — all registrations are emitted as plain C# code
- ✅ AOT / trimming friendly
- ✅ Keyed and non-keyed services in one uniform model
- ✅ Registration metadata (`RegisterItemInfo<T>`) and aggregate collections (`IEnumerable<T>` covering keyed + non-keyed) generated for you
- ✅ Works against the **MSDI 8.0 API surface** (`Microsoft.Extensions.DependencyInjection.Abstractions` 8.0.0)

## Features

- `[SingletonService]` / `[ScopedService]` / `[TransientService]` — the three built-in registration attributes
- **Generic attributes**: `[SingletonService<TargetType>]`, `[ScopedService<TargetType>]`, `[TransientService<TargetType>]`
- Legacy `typeof(...)` form is still supported and can be mixed: `[SingletonService(typeof(IFoo))]`
- **Keyed services**: `[SingletonService(Key = "primary")]` → `AddKeyedSingleton<T>("primary")`
- **One instance per (implementation, lifetime)**: all registrations of the same class under the same lifetime resolve the same instance — across interfaces and across keys
- `Order` controls registration order (and therefore collection order); for the same target type + key the **last registration wins**
- **Factory method registration**: annotate a method to register its return value as a service (keyed factories supported)
- **Registration metadata**: `IEnumerable<RegisterItemInfo<T>>` lists every registration (key, implementation, lifetime, order)
- **Aggregate collections**: `IEnumerable<T>` includes both keyed and non-keyed implementations — the built-in MSDI enumeration only sees non-keyed ones
- **Metadata helpers**: `GetRegisterItems<T>()`, `FindRegisterItem<T>(key)`, `GetRegisterMap<T>()` and `ToKeyValuePairs()` for reading the registration table without boilerplate
- Extensible: write your own attribute by deriving from `BaseServiceAttribute` and describing it with `[RegistrationOptions]`
- `[AutoRegister]` on a `partial` method triggers code generation (one per assembly)

## Quick Start

### 1. Install

```bash
dotnet add package Airflow.MSDI.AutoRegister
```

### 2. Decorate your services

```csharp
[SingletonService]
class ServiceA { }

[ScopedService]
class ServiceB { }

[TransientService]
class ServiceC { }

// Interface registration, legacy typeof form
[SingletonService(typeof(InterfaceD))]
class ServiceD : InterfaceD { }

// Self + interface registrations share one singleton
[SingletonService]
[SingletonService(typeof(InterfaceE))]
class ServiceE : InterfaceE { }

// Generic attribute form (C# 11+)
[SingletonService<IConnector>(Key = "primary")]
class SqlConnector : IConnector { }

// Keyed self + keyed interface (same key → same instance)
[SingletonService(Key = "primary")]
[SingletonService(typeof(IConnector), Key = "primary")]
class PrimaryConnector : IConnector { }

// Multiple implementations without keys: Order decides who wins, collection still lists all
[SingletonService(typeof(InterfaceF), 2)]
class ServiceF : InterfaceF { }

[SingletonService(typeof(InterfaceF), 1)]
class ServiceG : InterfaceF { }

// Factory method registration
class ServiceH
{
    [SingletonService]
    public ServiceI GetServiceI() => new();

    [ScopedService]
    public ServiceJ GetServiceJ(IServiceProvider sp) => new();
}
```

### 3. Generate the registrations

```csharp
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
```

### 4. Wire it up

```csharp
var services = new ServiceCollection();
services.AddServices();
var provider = services.BuildServiceProvider();

var a = provider.GetRequiredService<ServiceA>();
var primary = provider.GetRequiredKeyedService<IConnector>("primary");
```

The generator emits the body of `AddServicesFromCurrentAssembly`. Every **implementation + lifetime** pair gets one internal `object` key (an "anchor"), and every attribute usage becomes an alias pointing back at it:

```csharp
// anchors: one per (implementation, lifetime)
object __key1 = new object();
services.AddKeyedSingleton<ServiceA>(__key1);
object __key2 = new object();
services.AddKeyedScoped<ServiceB>(__key2);
object __key3 = new object();
services.AddKeyedTransient<ServiceC>(__key3);
object __key4 = new object();
services.AddKeyedSingleton<ServiceD>(__key4);
// ... one more anchor per implementation/lifetime (ServiceE, ServiceF, ServiceG, ...)

// aliases: one per attribute usage, all pointing back at their anchor
services.AddSingleton<ServiceA>(sp => sp.GetRequiredKeyedService<ServiceA>(__key1));
services.AddScoped<ServiceB>(sp => sp.GetRequiredKeyedService<ServiceB>(__key2));
services.AddTransient<ServiceC>(sp => sp.GetRequiredKeyedService<ServiceC>(__key3));
services.AddSingleton<InterfaceD>(sp => sp.GetRequiredKeyedService<ServiceD>(__key4));
services.AddSingleton<InterfaceE>(sp => sp.GetRequiredKeyedService<ServiceE>(__key5));
services.AddKeyedSingleton<IConnector>("primary", (sp, _) => sp.GetRequiredKeyedService<PrimaryConnector>(__key6));
services.AddSingleton<InterfaceF>(sp => sp.GetRequiredKeyedService<ServiceG>(__key7));   // order 1
services.AddSingleton<InterfaceF>(sp => sp.GetRequiredKeyedService<ServiceF>(__key8));   // order 2 → wins

// factory methods: the owning class is a plain non-keyed singleton; the produced value is anchored
services.AddSingleton<ServiceH>();
services.AddKeyedSingleton<ServiceI>(__key9, (sp, _) => sp.GetRequiredService<ServiceH>().GetServiceI());
services.AddSingleton<ServiceI>(sp => sp.GetRequiredKeyedService<ServiceI>(__key9));

// registration metadata + aggregate collections, one block per service type
services.AddSingleton<IEnumerable<RegisterItemInfo<InterfaceD>>>(new RegisterItemInfo<InterfaceD>[]
{
    new(null, typeof(ServiceD), ServiceLifetime.Singleton, 0),
});
services.AddTransient<IEnumerable<InterfaceD>>(sp => new InterfaceD[]
{
    sp.GetRequiredKeyedService<ServiceD>(__key4),
});
```

The exact output is deterministic; check the demo project's `Generated/` folder or enable `EmitCompilerGeneratedFiles` to inspect it.

### 5. Viewing the generated code

By default the generated code is **not written to disk** — it only exists in memory during compilation. If you want to see the generated file, you have two options:

**Option A — view it in the IDE (nothing written to disk):**

In Solution Explorer, expand **Dependencies → Analyzers → Airflow.MSDI.AutoRegister.AutoRegisterGenerator** and double-click `AutoRegister.Register.AddServicesFromCurrentAssembly.g.cs`.

**Option B — persist it to disk, refreshed to the latest on every build:**

```xml
<PropertyGroup>
  <EmitCompilerGeneratedFiles>true</EmitCompilerGeneratedFiles>
  <CompilerGeneratedFilesOutputPath>$(MSBuildProjectDirectory)/Generated</CompilerGeneratedFilesOutputPath>
</PropertyGroup>

<ItemGroup>
  <!-- Reference-only output; must NOT be compiled again (the generator already injects it in memory) -->
  <Compile Remove="Generated/**/*.cs" />
  <None Include="Generated/**/*.cs" Visible="false" />
</ItemGroup>
```

- Whenever the generator runs (i.e. on every real compilation), the `.g.cs` under `Generated/` is **rewritten in full**, so its content always matches the current build.
- The file is a **read-only reference** — editing it doesn't affect compilation.
- Keep the `<Compile Remove>` line: without it the on-disk file would be compiled again alongside the in-memory copy, causing duplicate-definition errors.

## How It Works

### Registration styles

Every attribute usage is one registration; all of them become **aliases** over the anchor of their (implementation, lifetime) pair:

| Attribute usage | Generated alias |
|---|---|
| `[SingletonService]` on `C` | `AddSingleton<C>(sp => sp.GetRequiredKeyedService<C>(anchor))` |
| `[SingletonService(typeof(I))]` on `C : I` | `AddSingleton<I>(sp => sp.GetRequiredKeyedService<C>(anchor))` |
| `[SingletonService<I>]` on `C : I` | `AddSingleton<I>(sp => sp.GetRequiredKeyedService<C>(anchor))` |
| `[SingletonService(Key = "k")]` on `C` | `AddKeyedSingleton<C>("k", (sp, _) => sp.GetRequiredKeyedService<C>(anchor))` |
| `[SingletonService(typeof(I), Key = "k")]` on `C : I` | `AddKeyedSingleton<I>("k", (sp, _) => sp.GetRequiredKeyedService<C>(anchor))` |
| `[SingletonService]` + `[SingletonService(typeof(I))]` on `C : I` | two aliases on the **same** anchor → same instance |

For the owning class of factory methods the generator also emits `AddSingleton<Owner>()` (non-keyed, once per class).

Closed generic service types are supported as-is (`[SingletonService<IGenericService<string>>]`). A generic class itself cannot be registered (`AMAR2003`); close its type parameters with a concrete derived class first.

### Instance sharing (v2 model)

**One instance per (implementation type, lifetime)** — all registrations of the same class under the same lifetime resolve the same instance, no matter which interface or key they use:

```csharp
[SingletonService(Key = "primary")]
[SingletonService(typeof(IConnector), Key = "primary")]
public class PrimaryConnector : IConnector { }

var a = provider.GetRequiredKeyedService<PrimaryConnector>("primary");
var b = provider.GetRequiredKeyedService<IConnector>("primary");
ReferenceEquals(a, b); // true
```

Both registrations are aliases over the same anchor, so this works for any combination: self + interface, several interfaces, several keys, keyed + non-keyed.

Different lifetimes get different instances (`[SingletonService]` + `[ScopedService<IFoo>]` → one singleton for the class, one scoped instance per scope).

### Keys

- `Key` is a **string**; omitted or `null` means a non-keyed registration.
- **Named keys must be unique per target type** (`AMAR2009`): two different implementations cannot register the same service type under the same key.
- Registrations **without** a key may repeat — this is the classic "multiple implementations, `Order` decides the winner" pattern.
- The generated code stores implementations under internal `object` keys ("anchors") so keyed and non-keyed aliases share one instance.

### Order

Registrations are sorted by `(Order, implementation type name, key)`:

- For the same target type + key, the **last registration wins** — a larger `Order` means later registration, i.e. higher priority.
- `Order` also defines the order of entries in the metadata and aggregate collections.
- Unspecified `Order` defaults to `0`; implementations are then ordered by their type name.

### Factory method registration

- The owning class is automatically registered as a **non-keyed singleton** (once per class).
- The method must be a `public` instance method on a plain class, with **no parameters or exactly one `IServiceProvider` parameter**.
- The return type is the service type; a generic target or a `typeof(...)` argument can override it (the return type must be implicitly convertible to that type).
- Singleton factories must not take `IServiceProvider` (`AMAR1010`); scoped/transient factories may.
- Return types must be reference types; value types/arrays/open generics are rejected (`AMAR1007`).

### Registration metadata and aggregate collections

For **every service type** the generator emits two extra registrations:

```csharp
// 1) metadata: one entry per registration (key, implementation, lifetime, order)
services.AddSingleton<IEnumerable<RegisterItemInfo<IConnector>>>(new RegisterItemInfo<IConnector>[]
{
    new("primary", typeof(SqlConnector), ServiceLifetime.Singleton, 0),
    new("replica", typeof(ReadOnlyConnector), ServiceLifetime.Singleton, 0),
});

// 2) instances: keyed + non-keyed implementations in one collection
services.AddTransient<IEnumerable<IConnector>>(sp => new IConnector[]
{
    sp.GetRequiredKeyedService<SqlConnector>(__key1),
    sp.GetRequiredKeyedService<ReadOnlyConnector>(__key2),
});
```

Why this exists: MSDI resolves keyed and non-keyed services through separate paths, so the native `IEnumerable<T>` never contains keyed registrations. The generated collection fixes that inconsistency:

```csharp
provider.GetServices<IConnector>();             // both implementations, keyed included
provider.GetRequiredService<IEnumerable<RegisterItemInfo<IConnector>>>(); // registration table
```

> The generated `IEnumerable<T>` **intentionally overrides** the native MSDI enumeration. A manually added `services.AddSingleton<T>()` for the same service type will not appear in the collection — register it through an attribute instead.

### Reading the registration table (helpers)

You rarely need to write `GetRequiredService<IEnumerable<RegisterItemInfo<T>>>()` yourself:

```csharp
// all registrations of a service type (empty when the type has none)
IReadOnlyList<RegisterItemInfo<IConnector>> items = provider.GetRegisterItems<IConnector>();

// one registration by key (null when missing; null key = the non-keyed registration)
RegisterItemInfo<IConnector>? primary = provider.FindRegisterItem<IConnector>("primary");

// key → implementation type
IReadOnlyDictionary<string, Type> map = provider.GetRegisterMap<IConnector>();

// iterate key → implementation (unkeyed registrations appear with a null key)
foreach (KeyValuePair<string?, Type> pair in items.ToKeyValuePairs())
    Console.WriteLine($"{pair.Key ?? "<none>"} -> {pair.Value.Name}");

// or work directly on a list you already have
var item = items.FindByKey("primary");
```

### Custom attributes

Any subclass of `BaseServiceAttribute` works. Describe your constructor layout with `[RegistrationOptions]` so the generator knows which argument is the lifetime, the service type, and the order:

```csharp
[RegistrationOptions(
    LifetimeIndex = -1,                        // no lifetime ctor arg → use DefaultLifetime
    ServiceTypeIndex = 0,                      // ctor arg #0 is the service type
    OrderIndex = 1,                            // ctor arg #1 is the order
    DefaultLifetime = ServiceLifetime.Transient)]
public sealed class CustomServiceAttribute : BaseServiceAttribute
{
    public CustomServiceAttribute(Type? serviceType = null, double order = 0)
        : base(ServiceLifetime.Transient, serviceType, order) { }
}

[CustomService(typeof(InterfaceX), Key = "v2")]   // Key/Order are inherited properties
public class ServiceX : InterfaceX { }
// → services.AddKeyedTransient<InterfaceX, ServiceX>("v2");
```

`[RegistrationOptions]` also lets an attribute provide **defaults** for everything it does not take as a constructor argument — `DefaultLifetime`, `DefaultServiceType`, `DefaultOrder`. The demo project shows this with `ViewAttribute` / `ViewModelAttribute`: `[View]` registers as `IView` (Transient) by default, `[ViewModel]` as `IViewModel`, and an explicit target overrides the default:

```csharp
[RegistrationOptions(
    LifetimeIndex = -1,
    ServiceTypeIndex = 0,                        // ctor #0 = target type
    OrderIndex = 1,                              // ctor #1 = Order
    DefaultLifetime = ServiceLifetime.Transient,
    DefaultServiceType = typeof(IView),          // [View] without arguments → IView
    DefaultOrder = 0)]
public sealed class ViewAttribute : BaseServiceAttribute
{
    public ViewAttribute(Type? serviceType = null, double order = 0)
        : base(ServiceLifetime.Transient, serviceType, order) { }
}

[View]                                           // → IView (Transient, Order 0)
public class MainView : IView { }

[View(Order = 1)]                                // → IView, ordered after MainView
public class SettingsView : IView { }

[View(typeof(ISpecialView))]                     // explicit target overrides the default
public class SpecialView : IView, ISpecialView { }

[ViewModel(Key = "settings")]                    // → keyed IViewModel registration
public class SettingsViewModel : IViewModel { }
```

The full example lives in `simple/Airflow.MSDI.AutoRegister.Demo/Attributes/` and its output is printed by the demo program.

A generic attribute class with exactly one type parameter is treated as "target type" (the same convention as the built-in generic attributes).

## Diagnostics

The generator reports `AMAR`-prefixed diagnostics — **errors block compilation and the offending registration is skipped**, **warnings are hints**. Messages are localized: English by default, simplified Chinese via satellite resources.

| Rule ID | Severity | Description |
|---|---|---|
| AMAR1001 | Error | Factory method owner must be a plain class |
| AMAR1002 | Error | A class containing factory methods cannot have registration attributes |
| AMAR1003 | Error | Factory method cannot be static |
| AMAR1004 | Error | Factory method must be public |
| AMAR1005 | Error | Factory method cannot be generic |
| AMAR1006 | Error | Factory method cannot return void |
| AMAR1007 | Error | Factory method return type must be a reference type |
| AMAR1008 | Error | Invalid number of factory method parameters |
| AMAR1009 | Error | Single parameter must be IServiceProvider |
| AMAR1010 | Error | Singleton factory method cannot take IServiceProvider |
| AMAR2001 | Error | Registration attribute on an unsupported target |
| AMAR2002 | Error | Service type must be an interface or class |
| AMAR2003 | Error | Generic classes cannot be registered |
| AMAR2005 | Warning | Duplicate registration (same service type + key + implementation) |
| AMAR2007 | Error | Implementation type is not compatible with the service type |
| AMAR2008 | Error | Implementation type cannot be instantiated (abstract/static) |
| AMAR2009 | Error | Duplicate service key for the same target type |
| AMAR3001 | Error | [AutoRegister] method must be declared partial |
| AMAR3002 | Error | [AutoRegister] method's containing class must be partial |
| AMAR3003 | Error | [AutoRegister] method cannot have a manual implementation |
| AMAR3004 | Error | [AutoRegister] method must be static |
| AMAR3005 | Error | [AutoRegister] method signature must be (IServiceCollection services) |
| AMAR3006 | Warning | No registerable services found in the assembly |
| AMAR3007 | Error | Only one [AutoRegister] method is allowed per assembly |
| AMAR4001 | Error | Missing [RegistrationOptions] |
| AMAR4002 | Error | [RegistrationOptions] index exceeds constructor parameter count |
| AMAR4003 | Error | [RegistrationOptions] index points to a parameter of the wrong type |
| AMAR4004 | Error | Invalid lifetime value |
| AMAR4005 | Warning | Multiple [RegistrationOptions] on the inheritance chain |
| AMAR4006 | Error | Generic registration attribute has too many type parameters |
| AMAR4007 | Error | Conflicting service types in the registration attribute |

## Compatibility

- Depends on **`Microsoft.Extensions.DependencyInjection.Abstractions` 8.0.0** (the first version with keyed services).
- **Keyed registrations require MSDI 8.0+ (.NET 8+)** at runtime; non-keyed registrations keep working on older containers, but the package dependency is raised.
- The generator targets `netstandard2.0` and runs on the .NET SDK's Roslyn.
- Generated code needs **C# 9+**; the **generic attribute syntax needs C# 11+** (legacy `typeof(...)` form works on C# 9+).

## Migrating from 1.x

| Change | Impact |
|---|---|
| **One instance per (implementation, lifetime)** | Multiple interface registrations of one class now share a single instance (1.x created one instance per registration unless a self registration existed) |
| Even single registrations are emitted as anchor + alias | one extra keyed descriptor per implementation; behaviour unchanged |
| `[AutoRegister]` still one per assembly | unchanged |
| **Named keys must be unique per target type** (`AMAR2009`, Error) | 1.x had no keys; give each implementation its own key |
| `AMAR2006` removed | Self/interface lifetime mismatches no longer exist: instances are grouped per lifetime |
| `IEnumerable<T>` is generated for every service type | Overrides MSDI's native enumeration so keyed implementations are included; manually registered services of the same type are not part of it |
| `Abstractions` 1.0.0 → 8.0.0 | Keyed services require MSDI 8.0+ at runtime |
| `Key` / `Order` properties, generic attributes, `RegisterItemInfo<T>` | New API |

## Related documents

- Chinese README: [doc/README.zh-CN.md](https://github.com/qh-970261102/Airflow.MSDI.AutoRegister/blob/main/doc/README.zh-CN.md)
- AI/LLM quick start (copy-pasteable prompt + cheat sheet): [doc/AI-QUICKSTART.md](https://github.com/qh-970261102/Airflow.MSDI.AutoRegister/blob/main/doc/AI-QUICKSTART.md)

## License

[MIT](LICENSE)
