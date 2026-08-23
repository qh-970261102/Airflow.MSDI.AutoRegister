# Airflow.MSDI.AutoRegister

[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](LICENSE)
[![.NET](https://img.shields.io/badge/.NET-netstandard2.0%20%2B-blueviolet)](src/Airflow.MSDI.AutoRegister)

A **source generator** for Microsoft.Extensions.DependencyInjection (MSDI) that automatically registers your services — no more hand-written `AddSingleton`/`AddScoped`/`AddTransient` boilerplate, no reflection, zero runtime overhead.

> 中文文档: [README.zh-CN.md](./doc/README.zh-CN.md)

---

## Why?

Manually registering dozens of services is tedious and error-prone. Reflection-based scanners (like Scrutor) work, but they cost runtime performance and can't be trimmed. This library moves registration to **compile time**:

- ✅ Zero runtime reflection — all registrations are emitted as plain C# code
- ✅ AOT / trimming friendly
- ✅ Fast IDE experience with full type safety
- ✅ Works against the **MSDI 1.0 API surface** (`Microsoft.Extensions.DependencyInjection.Abstractions` 1.0.0)

## Features

- `[SingletonService]`, `[ScopedService]`, `[TransientService]` — the three built-in registration attributes
- Register a class by itself, by interface, or both — **one attribute = one registration**
- Multiple attributes on one class: all singleton registrations resolve **the same instance** via factory
- `Order` support: when several implementations compete for the same service type, the **largest Order wins** (registered last); unspecified Order falls back to **type-name ordering**
- **Factory method registration**: annotate a method to register its return value as a service
- Extensible: write your own attribute by deriving from `BaseServiceAttribute` and describing it with `[RegistrationOptions]`
- `[AutoRegister]` on a `partial` method triggers code generation; multiple `[AutoRegister]` methods each get their own implementation

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

[SingletonService(typeof(InterfaceD))]
class ServiceD : InterfaceD { }

// Self registration + interface registration share the same singleton instance
[SingletonService]
[SingletonService(typeof(InterfaceE))]
class ServiceE : InterfaceE { }

// Order decides who wins when several types register the same service type
[SingletonService(typeof(InterfaceF), 2)]
class ServiceF : InterfaceF { }

[SingletonService(typeof(InterfaceF), 1)]
class ServiceG : InterfaceF { }

// Factory method registration
class ServiceH
{
    private readonly InterfaceF _serviceG;
    public ServiceH(InterfaceF serviceG) => _serviceG = serviceG;

    [SingletonService]
    public ServiceI GetServiceI() => new ServiceI(_serviceG);

    [ScopedService]
    public ServiceJ GetServiceJ(IServiceProvider sp) => new ServiceJ(_serviceG);
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
```

The generator emits the body of `AddServicesFromCurrentAssembly`, roughly equivalent to:

```csharp
services.AddSingleton<ServiceA>();
services.AddScoped<ServiceB>();
services.AddTransient<ServiceC>();
services.AddSingleton<InterfaceD, ServiceD>();
services.AddSingleton<ServiceE>();
services.AddSingleton<InterfaceE>(sp => sp.GetRequiredService<ServiceE>());
services.AddSingleton<InterfaceF, ServiceG>();   // order 1 first
services.AddSingleton<InterfaceF, ServiceF>();   // order 2 last → wins
services.AddSingleton<ServiceH>();               // factory owner registered as singleton
services.AddSingleton<ServiceI>(sp => sp.GetRequiredService<ServiceH>().GetServiceI());
services.AddScoped<ServiceJ>(sp => sp.GetRequiredService<ServiceH>().GetServiceJ(sp));
```

### 5. Viewing the generated code

By default the generated code is **not written to disk** — it only exists in memory during compilation. If you want to see the generated file, you have two options:

**Option A — view it in the IDE (nothing written to disk):**

In Solution Explorer, expand **Dependencies → Analyzers → Airflow.MSDI.AutoRegister.AutoRegisterGenerator** and double-click `AutoRegister.Register.AddServicesFromCurrentAssembly.g.cs`.

**Option B — persist it to disk, refreshed to the latest on every build:**

Add the following to your `.csproj`:

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
- Configure this in whichever project wants the output; the generator project itself needs nothing.

## How It Works

### Class registration

| Attribute usage | Generated code |
|---|---|
| `[SingletonService]` on `C` | `services.AddSingleton<C>();` |
| `[SingletonService(typeof(I))]` on `C : I` | `services.AddSingleton<I, C>();` |
| `[SingletonService]` + `[SingletonService(typeof(I))]` on `C : I` | `services.AddSingleton<C>();` + `services.AddSingleton<I>(sp => sp.GetRequiredService<C>());` |

The last row is the **same-instance rule**: when a class registers itself *and* an interface, the interface resolution points back to the class registration, so `I` and `C` resolve to the exact same instance. The same pattern applies to `Scoped` and `Transient` — inside a scope, `I` and `C` are the same scoped instance.

**Closed generic service types are supported as-is:**

| Attribute usage | Generated code |
|---|---|
| `[SingletonService(typeof(I<TA>))]` on `C : I<TA>` | `services.AddSingleton<I<TA>, C>();` |
| `[SingletonService]` + `[SingletonService(typeof(I<TA>))]` on `C : I<TA>` | `services.AddSingleton<C>();` + `services.AddSingleton<I<TA>>(sp => sp.GetRequiredService<C>());` |

The type arguments are filled in verbatim. Open/unbound generics (`typeof(I<>)`) and open-constructed generics are rejected with an error. A **generic class itself cannot be registered** (e.g. `class G<T> : I<T>` — an error is reported); close the type parameters first by deriving a concrete class: `class StringG : G<string>` and register `StringG` — "just wrap it one more layer".

### Order

Registrations are sorted by `(Order, implementation type name)`:

- A larger `Order` is registered **later**, and in MSDI the **last registration wins** for a service type. So `Order` acts as a priority.
- When `Order` is not specified (default `0`), implementations are ordered by their type name — the user simply doesn't care about the winner.

### Factory method registration

- The owning class is automatically registered as a **singleton** (once per class).
- The method must be a `public` instance method on a plain class, with **no parameters or exactly one `IServiceProvider` parameter**.
- Return type becomes the service type. The generated registration **omits the explicit type argument** — the service type is inferred from the factory's return type (lambda natural-type inference), so **closed generics are registered as-is** (`List<TypeA>` works, no repetition). Open/unbound generics and arrays are rejected:

```csharp
// parameterless
services.AddSingleton(sp => sp.GetRequiredService<ServiceH>().GetServiceI());
// with IServiceProvider
services.AddScoped(sp => sp.GetRequiredService<ServiceH>().GetServiceJ(sp));
// closed generic return type, inferred the same way
services.AddSingleton(sp => sp.GetRequiredService<ServiceH>().GetList()); // service type = List<TypeA>
```

> Invalid shapes (static method, non-public, wrong parameters, non-plain owner class, etc.) are **silently skipped in v1.0**. Since **v1.1** the generator reports `AMAR`-prefixed diagnostics (errors block compilation, warnings hint).

### Custom attributes

Any subclass of `BaseServiceAttribute` works. Describe your constructor layout with `[RegistrationOptions]` so the generator knows which argument is the lifetime, the service type, and the order:

```csharp
[RegistrationOptions(
    LifetimeIndex = -1,                        // no lifetime ctor arg → use DefaultLifetime
    ServiceTypeIndex = 0,                      // ctor arg #0 is the service type
    OrderIndex = 1,                            // ctor arg #1 is the order
    DefaultLifetime = ServiceLifetime.Transient,
    DefaultServiceType = null,
    DefaultOrder = 0)]
public sealed class CustomServiceAttribute : BaseServiceAttribute
{
    public CustomServiceAttribute(Type? serviceType = null, double order = 0)
        : base(ServiceLifetime.Transient, serviceType, order) { }
}

[CustomService(typeof(InterfaceX))]
public class ServiceX : InterfaceX { }
// → services.AddTransient<InterfaceX, ServiceX>();
```

## Compatibility

- The package depends only on **`Microsoft.Extensions.DependencyInjection.Abstractions` 1.0.0**, the oldest MSDI API surface, so the generated code compiles against any modern MSDI container.
- The generator targets `netstandard2.0` and runs on the .NET SDK's Roslyn (C# 9+ partial methods with accessibility are required for `[AutoRegister]`).

## License

[MIT](LICENSE)
