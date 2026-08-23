# Airflow.MSDI.AutoRegister 使用教程

> 面向 NuGet 用户的完整使用指南。本教程与 1.0 版本配套。
> This tutorial targets the 1.0 release and is also available as the package README on NuGet.

## 目录 / Contents

1. [安装 / Installation](#1-安装--installation)
2. [三步上手 / Three-step Quick Start](#2-三步上手--three-step-quick-start)
3. [注册方式 / Registration Styles](#3-注册方式--registration-styles)
4. [同实例规则 / Same-Instance Rule](#4-同实例规则--same-instance-rule)
5. [Order 排序 / Ordering](#5-order-排序--ordering)
6. [工厂方法注册 / Factory Method Registration](#6-工厂方法注册--factory-method-registration)
7. [自定义特性 / Custom Attributes](#7-自定义特性--custom-attributes)
8. [多个 AutoRegister 方法 / Multiple AutoRegister Methods](#8-多个-autoregister-方法--multiple-autoregister-methods)
9. [常见问题 / FAQ](#9-常见问题--faq)

---

## 1. 安装 / Installation

```bash
dotnet add package Airflow.MSDI.AutoRegister
```

包依赖 `Microsoft.Extensions.DependencyInjection.Abstractions` **1.0.0**(最古老 API 面),可配合任意现代 MSDI 容器使用。

## 2. 三步上手 / Three-step Quick Start

**Step 1 — 给服务加特性 / Decorate your services**

```csharp
[SingletonService]
public class MyService { }
```

**Step 2 — 声明注册入口 / Declare the registration entry**

```csharp
internal static partial class Register
{
    [AutoRegister]
    public static partial void AddServicesFromCurrentAssembly(IServiceCollection services);
}
```

**Step 3 — 接入容器 / Wire up**

```csharp
var services = new ServiceCollection();
Register.AddServicesFromCurrentAssembly(services);
// 或扩展方法: services.AddServices();
```

完成。`AddServicesFromCurrentAssembly` 的方法体由源生成器在编译期填充,运行时零反射。

## 3. 注册方式 / Registration Styles

### 3.1 按类自身注册 / Self registration

```csharp
[SingletonService]   // AddSingleton<ServiceA>()
[ScopedService]      // AddScoped<ServiceB>()
[TransientService]   // AddTransient<ServiceC>()
```

### 3.2 按接口注册 / Interface registration

```csharp
[SingletonService(typeof(InterfaceD))]
public class ServiceD : InterfaceD { }
// → services.AddSingleton<InterfaceD, ServiceD>();
```

一个特性 = 一条注册。同一类可叠加多个特性。

## 4. 同实例规则 / Same-Instance Rule

当类**既注册自身又注册接口**时,接口解析通过工厂指向类注册,保证 `I` 与 `C` 是**同一个实例**:

```csharp
[SingletonService]
[SingletonService(typeof(InterfaceE))]
public class ServiceE : InterfaceE { }

// → services.AddSingleton<ServiceE>();
// → services.AddSingleton<InterfaceE>(sp => sp.GetRequiredService<ServiceE>());
//   ↑ InterfaceE 与 ServiceE 解析到同一单例
```

`Scoped` / `Transient` 同理:同一 scope 内 `I` 与 `C` 是同一个 scoped 实例。

## 5. Order 排序 / Ordering

多个实现竞争同一服务类型时,注册按 `(Order, 实现类型名)` 排序,MSDI 中**后注册者胜出**,因此 **Order 越大优先级越高**:

```csharp
[SingletonService(typeof(InterfaceF), 2)]
public class ServiceF : InterfaceF { }

[SingletonService(typeof(InterfaceF), 1)]
public class ServiceG : InterfaceF { }

// → services.AddSingleton<InterfaceF, ServiceG>();   // order 1 先
// → services.AddSingleton<InterfaceF, ServiceF>();   // order 2 后 → 胜出
```

未指定 Order(默认 `0`)时按**类型名**排序——表示使用者不关心胜出者。

## 6. 工厂方法注册 / Factory Method Registration

在方法上标注特性,方法的**返回值类型即服务类型**,拥有类自动注册为**单例**(每类仅一次):

```csharp
public class ServiceH
{
    [SingletonService]
    public ServiceI GetServiceI() => new();

    [ScopedService]
    public ServiceJ GetServiceJ(IServiceProvider sp) => new();
}

// → services.AddSingleton<ServiceH>();
// → services.AddSingleton<ServiceI>(sp => sp.GetRequiredService<ServiceH>().GetServiceI());
// → services.AddScoped<ServiceJ>(sp => sp.GetRequiredService<ServiceH>().GetServiceJ(sp));
```

**方法约束(1.0 静默跳过非法形态,1.1 起报错):**

- 必须是普通类上的 `public` 实例方法
- 参数只能是:**无参** 或 **仅一个 `IServiceProvider`**
- 返回值不能是 `void` / 泛型

> ⚠️ 单例工厂方法不允许带 `IServiceProvider` 参数——单例的依赖应来自拥有类的构造注入,若需解析 scoped 依赖说明生命周期用错了(1.1 报 `ASRG1010`)。

## 7. 自定义特性 / Custom Attributes

继承 `BaseServiceAttribute` 并用 `[RegistrationOptions]` 描述构造函数布局:

```csharp
[RegistrationOptions(
    LifetimeIndex = -1,                        // 无生命周期构造参数 → 用 DefaultLifetime
    ServiceTypeIndex = 0,                      // 构造参数 #0 = 服务类型
    OrderIndex = 1,                            // 构造参数 #1 = Order
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

**规则:** 所有 `BaseServiceAttribute` 子类**必须**使用 `[RegistrationOptions]` 描述元数据(缺失时 1.1 报 `ASRG4001`)。

## 8. 多个 AutoRegister 方法 / Multiple AutoRegister Methods

多个 `[AutoRegister]` 方法各自生成**独立实现**,每个都包含完整注册体:

```csharp
internal static partial class Register
{
    [AutoRegister] public static partial void AddAllA(IServiceCollection services);
    [AutoRegister] public static partial void AddAllB(IServiceCollection services);
}
```

按需调用其中一个即可完成全量注册。

## 9. 常见问题 / FAQ

**Q: 生成的代码在哪?**
构建产物中 `AutoRegister.*.g.cs`。本地查看:加编译参数 `-p:EmitCompilerGeneratedFiles=true`。

**Q: 为什么我的服务没被注册?**
1.0 对非法形态静默跳过。检查:特性是否拼写正确、是否 `BaseServiceAttribute` 子类、工厂方法是否满足约束(普通类 / public / 无参或单 IServiceProvider)、`[AutoRegister]` 方法是否 partial。

**Q: 需要 .NET 几?**
生成器运行于 .NET SDK 自带 Roslyn(C# 9+ partial methods);包本身 `netstandard2.0`,任何支持 .NET (Core) 的应用均可使用。

**Q: 与 Scrutor 有什么区别?**
Scrutor 运行时反射扫描;本库编译期生成,无运行时开销、利于 AOT/裁剪。

**Q: 支持泛型开放注册 / Keyed services 吗?**
1.x 不支持开放泛型注册;Keyed services 需要 MSDI 6.0+,与"仅 MSDI 1.0 API 面"的约束冲突,暂不支持。

---

## 许可 / License

[MIT](../LICENSE)

## 相关文档 / Related

- 需求文档: [.tesk/1.0-impl.md](1.0-impl.md)
- 1.1 错误检测规划: [.tesk/1.1-impl.md](1.1-impl.md)
