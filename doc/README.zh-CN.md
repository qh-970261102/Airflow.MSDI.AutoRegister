# Airflow.MSDI.AutoRegister

[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](LICENSE)
[![.NET](https://img.shields.io/badge/.NET-netstandard2.0%20%2B-blueviolet)](src/Airflow.MSDI.AutoRegister)

一个面向 **Microsoft.Extensions.DependencyInjection(MSDI)** 的**源生成器**:自动注册你的服务——告别手写 `AddSingleton`/`AddScoped`/`AddTransient` 样板代码,无反射,零运行时开销。

> English docs: [README.md](README.md)

---

## 为什么用它?

手动注册几十个服务繁琐且易错;基于反射的扫描器(如 Scrutor)虽然能用,但有运行时开销、不利于裁剪。本库把注册工作搬到**编译期**:

- ✅ 零运行时反射 —— 所有注册都是直接生成的普通 C# 代码
- ✅ 对 AOT / trimming 友好
- ✅ IDE 体验好,完全类型安全
- ✅ 只依赖 **MSDI 1.0 API 面**(`Microsoft.Extensions.DependencyInjection.Abstractions` 1.0.0)

## 特性(v1.0)

- `[SingletonService]`、`[ScopedService]`、`[TransientService]` —— 三个内置注册特性
- 支持按类自身注册、按接口注册、或两者同时注册——**一个特性 = 一条注册**
- 同一类上多个特性:所有单例注册通过工厂**指向同一实例**
- `Order` 支持:多个实现竞争同一服务类型时,**Order 最大者胜出**(最后注册);未指定 Order 则**按类型名排序**
- **工厂方法注册**:在方法上标注特性,将其返回值注册为服务
- 可扩展:继承 `BaseServiceAttribute` 并配合 `[RegistrationOptions]` 描述元数据,即可自定义特性
- `[AutoRegister]` 打在 `partial` 方法上触发代码生成;多个 `[AutoRegister]` 方法各自生成独立实现

## 快速开始

### 1. 安装

```bash
dotnet add package Airflow.MSDI.AutoRegister
```

### 2. 标注服务

```csharp
[SingletonService]
class ServiceA { }

[ScopedService]
class ServiceB { }

[TransientService]
class ServiceC { }

[SingletonService(typeof(InterfaceD))]
class ServiceD : InterfaceD { }

// 自身注册 + 接口注册共享同一个单例实例
[SingletonService]
[SingletonService(typeof(InterfaceE))]
class ServiceE : InterfaceE { }

// 多个类型注册同一服务类型时,Order 决定谁胜出
[SingletonService(typeof(InterfaceF), 2)]
class ServiceF : InterfaceF { }

[SingletonService(typeof(InterfaceF), 1)]
class ServiceG : InterfaceF { }

// 工厂方法注册
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

### 3. 生成注册

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

### 4. 接入容器

```csharp
var services = new ServiceCollection();
services.AddServices();
```

生成器会填充 `AddServicesFromCurrentAssembly` 的方法体,大致等价于:

```csharp
services.AddSingleton<ServiceA>();
services.AddScoped<ServiceB>();
services.AddTransient<ServiceC>();
services.AddSingleton<InterfaceD, ServiceD>();
services.AddSingleton<ServiceE>();
services.AddSingleton<InterfaceE>(sp => sp.GetRequiredService<ServiceE>());
services.AddSingleton<InterfaceF, ServiceG>();   // order 1 先注册
services.AddSingleton<InterfaceF, ServiceF>();   // order 2 后注册 → 胜出
services.AddSingleton<ServiceH>();               // 工厂方法拥有类自动注册为单例
services.AddSingleton<ServiceI>(sp => sp.GetRequiredService<ServiceH>().GetServiceI());
services.AddScoped<ServiceJ>(sp => sp.GetRequiredService<ServiceH>().GetServiceJ(sp));
```

## 工作原理

### 类注册

| 特性用法 | 生成的代码 |
|---|---|
| 类 `C` 上 `[SingletonService]` | `services.AddSingleton<C>();` |
| `C : I` 上 `[SingletonService(typeof(I))]` | `services.AddSingleton<I, C>();` |
| `C : I` 上 `[SingletonService]` + `[SingletonService(typeof(I))]` | `services.AddSingleton<C>();` + `services.AddSingleton<I>(sp => sp.GetRequiredService<C>());` |

最后一行是**同实例规则**:当类既注册自身又注册接口时,接口解析指向类的注册,因此 `I` 与 `C` 解析到完全相同的实例。`Scoped` 与 `Transient` 同理——在同一个 scope 内,`I` 与 `C` 是同一个 scoped 实例。

### Order 排序

注册按 `(Order, 实现类型名)` 排序:

- Order 越大注册越**靠后**,而 MSDI 中同一服务类型**后注册者胜出**,所以 Order 相当于优先级。
- 未指定 Order(默认 `0`)时,按实现类型名排序——说明使用者并不关心谁胜出。

### 工厂方法注册

- 拥有类自动注册为**单例**(每个类只注册一次)。
- 方法必须是普通类上的 `public` 实例方法,参数为**无参或仅一个 `IServiceProvider`**。
- 方法返回值类型即服务类型:

```csharp
// 无参
services.AddSingleton<ServiceI>(sp => sp.GetRequiredService<ServiceH>().GetServiceI());
// 带 IServiceProvider
services.AddScoped<ServiceJ>(sp => sp.GetRequiredService<ServiceH>().GetServiceJ(sp));
```

> 非法形态(static 方法、非 public、参数不合规、拥有类非普通类等)在 **v1.0 会被静默跳过**。诊断能力规划在 **v1.1** —— 详见 [.tesk/1.1-impl.md](.tesk/1.1-impl.md)。

### 自定义特性

任何 `BaseServiceAttribute` 的子类都可用。用 `[RegistrationOptions]` 描述构造函数布局,让生成器知道哪个参数是生命周期、哪个是服务类型、哪个是 Order:

```csharp
[RegistrationOptions(
    LifetimeIndex = -1,                        // 构造函数无生命周期参数 → 用 DefaultLifetime
    ServiceTypeIndex = 0,                      // 构造参数 #0 是服务类型
    OrderIndex = 1,                            // 构造参数 #1 是 Order
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

## 兼容性

- 包仅依赖 **`Microsoft.Extensions.DependencyInjection.Abstractions` 1.0.0**(最古老的 MSDI API 面),因此生成的代码可配合任意现代 MSDI 容器编译运行。
- 生成器目标 `netstandard2.0`,运行于 .NET SDK 内置 Roslyn(`[AutoRegister]` 需要 C# 9+ 的带可访问性 partial 方法)。

## 路线图

- **v1.1**:非法形态诊断(工厂方法约束、普通类要求、不支持的类型)、增量生成器优化。
- 错误检测的详细规划见 [.tesk/1.1-impl.md](.tesk/1.1-impl.md)。

## License

[MIT](LICENSE)
