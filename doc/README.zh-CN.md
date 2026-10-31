# Airflow.MSDI.AutoRegister

[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](LICENSE)
[![.NET](https://img.shields.io/badge/.NET-netstandard2.0%20%2B-blueviolet)](src/Airflow.MSDI.AutoRegister)

一个面向 **Microsoft.Extensions.DependencyInjection(MSDI)** 的**源生成器**:编译期自动注册服务——支持 keyed 服务、泛型注册特性、注册元数据与实现集合,**无反射,零运行时开销**。

> English docs: [README.md](../README.md) · **用 AI 助手写代码?** 见 [AI-QUICKSTART.md](./AI-QUICKSTART.md)(可直接喂给 LLM 的速查指南)。

---

## 为什么用它?

手动注册几十个服务繁琐且易错;基于反射的扫描器(如 Scrutor)虽然能用,但有运行时开销、不利于裁剪。本库把注册工作搬到**编译期**:

- ✅ 零运行时反射 —— 所有注册都是直接生成的普通 C# 代码
- ✅ 对 AOT / trimming 友好
- ✅ keyed 与非 keyed 服务统一建模
- ✅ 自动生成**注册元数据**(`RegisterItemInfo<T>`)与**实现集合**(`IEnumerable<T>`,同时包含 keyed 与非 keyed)
- ✅ 只依赖 **MSDI 8.0 API 面**(`Microsoft.Extensions.DependencyInjection.Abstractions` 8.0.0)

## 特性

- `[SingletonService]`、`[ScopedService]`、`[TransientService]` —— 三个内置注册特性
- **泛型特性**:`[SingletonService<TargetType>]`、`[ScopedService<TargetType>]`、`[TransientService<TargetType>]`
- 旧的 `typeof(...)` 写法保留,可与泛型写法混用:`[SingletonService(typeof(IFoo))]`
- **keyed 服务**:`[SingletonService(Key = "primary")]` → `AddKeyedSingleton<T>("primary")`
- **同一实现、同一生命周期共享一份实例**:跨接口、跨 key 都指向同一实例
- `Order` 控制注册顺序(以及集合顺序);同一 targettype + key 下**后注册者胜出**
- **工厂方法注册**:在方法上标注特性,将其返回值注册为服务(支持 keyed 工厂)
- **注册元数据**:`IEnumerable<RegisterItemInfo<T>>` 列出每条注册(key、实现、生命周期、Order)
- **实现集合**:`IEnumerable<T>` 同时包含 keyed 与非 keyed 实现(MSDI 原生枚举只能看到非 keyed)
- **元数据便捷扩展**:`GetRegisterItems<T>()`、`FindRegisterItem<T>(key)`、`GetRegisterMap<T>()`、`ToKeyValuePairs()`,不用手写遍历样板
- 可扩展:继承 `BaseServiceAttribute` 并配合 `[RegistrationOptions]` 描述元数据,即可自定义特性
- `[AutoRegister]` 打在 `partial` 方法上触发代码生成(每个程序集一个)

## 快速开始

### 1. 安装

```bash
dotnet add package Airflow.MSDI.AutoRegister
```

### 2. 给服务加特性

```csharp
[SingletonService]
class ServiceA { }

[ScopedService]
class ServiceB { }

[TransientService]
class ServiceC { }

// 接口注册(旧 typeof 写法)
[SingletonService(typeof(InterfaceD))]
class ServiceD : InterfaceD { }

// 自身 + 接口:共享同一单例
[SingletonService]
[SingletonService(typeof(InterfaceE))]
class ServiceE : InterfaceE { }

// 泛型特性写法(C# 11+)
[SingletonService<IConnector>(Key = "primary")]
class SqlConnector : IConnector { }

// keyed 自身 + keyed 接口(同 key → 同一实例)
[SingletonService(Key = "primary")]
[SingletonService(typeof(IConnector), Key = "primary")]
class PrimaryConnector : IConnector { }

// 多个实现都不带 key:Order 决定胜出者,集合仍能列出全部
[SingletonService(typeof(InterfaceF), 2)]
class ServiceF : InterfaceF { }

[SingletonService(typeof(InterfaceF), 1)]
class ServiceG : InterfaceF { }

// 工厂方法注册
class ServiceH
{
    [SingletonService]
    public ServiceI GetServiceI() => new();

    [ScopedService]
    public ServiceJ GetServiceJ(IServiceProvider sp) => new();
}
```

### 3. 声明注册入口

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
var provider = services.BuildServiceProvider();

var a = provider.GetRequiredService<ServiceA>();
var primary = provider.GetRequiredKeyedService<IConnector>("primary");
```

生成器会补全 `AddServicesFromCurrentAssembly` 的方法体。**每个(实现, 生命周期)组合**都会得到一个内部 `object` key(锚点),每条特性注册都是指向该锚点的别名:

```csharp
// 锚点:每个 (实现, 生命周期) 一个
object __key1 = new object();
services.AddKeyedSingleton<ServiceA>(__key1);
object __key2 = new object();
services.AddKeyedScoped<ServiceB>(__key2);
object __key3 = new object();
services.AddKeyedTransient<ServiceC>(__key3);
object __key4 = new object();
services.AddKeyedSingleton<ServiceD>(__key4);
// ... 其余实现各有一个锚点(ServiceE、ServiceF、ServiceG ...)

// 别名:每条特性注册一条,全部指回自己的锚点
services.AddSingleton<ServiceA>(sp => sp.GetRequiredKeyedService<ServiceA>(__key1));
services.AddScoped<ServiceB>(sp => sp.GetRequiredKeyedService<ServiceB>(__key2));
services.AddTransient<ServiceC>(sp => sp.GetRequiredKeyedService<ServiceC>(__key3));
services.AddSingleton<InterfaceD>(sp => sp.GetRequiredKeyedService<ServiceD>(__key4));
services.AddSingleton<InterfaceE>(sp => sp.GetRequiredKeyedService<ServiceE>(__key5));
services.AddKeyedSingleton<IConnector>("primary", (sp, _) => sp.GetRequiredKeyedService<PrimaryConnector>(__key6));
services.AddSingleton<InterfaceF>(sp => sp.GetRequiredKeyedService<ServiceG>(__key7));   // order 1
services.AddSingleton<InterfaceF>(sp => sp.GetRequiredKeyedService<ServiceF>(__key8));   // order 2 → 胜出

// 工厂方法:拥有类是非 keyed 普通单例,产出的值走锚点
services.AddSingleton<ServiceH>();
services.AddKeyedSingleton<ServiceI>(__key9, (sp, _) => sp.GetRequiredService<ServiceH>().GetServiceI());
services.AddSingleton<ServiceI>(sp => sp.GetRequiredKeyedService<ServiceI>(__key9));

// 注册元数据 + 实现集合,每个服务类型一组
services.AddSingleton<IEnumerable<RegisterItemInfo<InterfaceD>>>(new RegisterItemInfo<InterfaceD>[]
{
    new(null, typeof(ServiceD), ServiceLifetime.Singleton, 0),
});
services.AddTransient<IEnumerable<InterfaceD>>(sp => new InterfaceD[]
{
    sp.GetRequiredKeyedService<ServiceD>(__key4),
});
```

输出是确定性的;想看真实结果可以打开 Demo 的 `Generated/` 目录,或开启 `EmitCompilerGeneratedFiles`。

### 5. 查看生成的代码

默认生成的代码**不会落盘**——只在编译期存在于内存。想查看有两种方式:

**方式 A —— 在 IDE 里看(不落盘):**

在解决方案资源管理器里展开 **Dependencies → Analyzers → Airflow.MSDI.AutoRegister.AutoRegisterGenerator**,双击 `AutoRegister.Register.AddServicesFromCurrentAssembly.g.cs`。

**方式 B —— 落盘,每次构建全量刷新:**

```xml
<PropertyGroup>
  <EmitCompilerGeneratedFiles>true</EmitCompilerGeneratedFiles>
  <CompilerGeneratedFilesOutputPath>$(MSBuildProjectDirectory)/Generated</CompilerGeneratedFilesOutputPath>
</PropertyGroup>

<ItemGroup>
  <!-- 仅供查看/参考,不参与编译(生成器已内存注入;再编译会重复定义) -->
  <Compile Remove="Generated/**/*.cs" />
  <None Include="Generated/**/*.cs" Visible="false" />
</ItemGroup>
```

- 每次生成器运行(即每次真实编译)都会**全量重写** `Generated/` 下的 `.g.cs`,内容始终与当前构建一致;
- 该文件是**只读参考**,改动它不影响编译;
- 保留 `<Compile Remove>` 那一行:否则磁盘上的文件会与内存注入的版本重复编译,导致重复定义错误。

## 工作原理

### 注册方式

每条特性注册都是**一条别名**,统一指向其(实现, 生命周期)的锚点:

| 特性写法 | 生成的别名 |
|---|---|
| `[SingletonService]` 打在 `C` 上 | `AddSingleton<C>(sp => sp.GetRequiredKeyedService<C>(anchor))` |
| `[SingletonService(typeof(I))]` 打在 `C : I` 上 | `AddSingleton<I>(sp => sp.GetRequiredKeyedService<C>(anchor))` |
| `[SingletonService<I>]` 打在 `C : I` 上 | `AddSingleton<I>(sp => sp.GetRequiredKeyedService<C>(anchor))` |
| `[SingletonService(Key = "k")]` 打在 `C` 上 | `AddKeyedSingleton<C>("k", (sp, _) => sp.GetRequiredKeyedService<C>(anchor))` |
| `[SingletonService(typeof(I), Key = "k")]` 打在 `C : I` 上 | `AddKeyedSingleton<I>("k", (sp, _) => sp.GetRequiredKeyedService<C>(anchor))` |
| `[SingletonService]` + `[SingletonService(typeof(I))]` 打在 `C : I` 上 | 两条别名指向**同一个**锚点 → 同一实例 |

工厂方法的拥有类还会额外生成 `AddSingleton<Owner>()`(非 keyed,每类一次)。

闭合泛型服务类型原样支持(`[SingletonService<IGenericService<string>>]`)。泛型类本身不能注册(`AMAR2003`),请先用具体子类闭合类型参数("多包一层")。

### 实例共享(2.0 模型)

**同一实现类型、同一生命周期只有一份实例**——无论通过哪个接口或哪个 key 解析,都指向同一实例:

```csharp
[SingletonService(Key = "primary")]
[SingletonService(typeof(IConnector), Key = "primary")]
public class PrimaryConnector : IConnector { }

var a = provider.GetRequiredKeyedService<PrimaryConnector>("primary");
var b = provider.GetRequiredKeyedService<IConnector>("primary");
ReferenceEquals(a, b); // true
```

两条注册都是同一个锚点的别名,所以"自身 + 接口、多接口、多 key、keyed + 非 keyed"任意组合都能共享同一个实例。

不同生命周期各自一份(`[SingletonService]` + `[ScopedService<IFoo>]` → 类本身一个单例,接口每个 scope 一份)。

### key

- `Key` 是**字符串**;不写或写 `null` 表示非 keyed 注册;
- **命名 key 在同一 targettype 下必须唯一**(`AMAR2009`):不同实现不能注册同一服务类型的同一 key;
- **无 key 的注册可以重复**——这就是 1.x 的"多实现 + Order 决定胜出者"模式;
- 生成代码会用内部 `object` key("锚点")保存实现实例,让 keyed / 非 keyed 别名共享同一实例。

### Order

注册按 `(Order, 实现类型名, key)` 排序:

- 同一 targettype + key 下,**后注册者胜出**——`Order` 越大越后注册,即优先级越高;
- `Order` 同时决定元数据与实现集合里的排列顺序;
- 未指定 `Order` 时为 `0`,此时按实现类型名排序。

### 工厂方法注册

- 拥有类自动注册为**非 keyed 单例**(每类一次);
- 方法必须是普通类上的 `public` 实例方法,参数为**无参**或**仅一个 `IServiceProvider`**;
- 返回类型即服务类型;可用泛型目标或 `typeof(...)` 覆盖(返回类型必须可隐式转换到该类型);
- 单例工厂不允许带 `IServiceProvider`(`AMAR1010`);scoped/transient 工厂可以;
- 返回类型必须是引用类型;值类型/数组/开放泛型会被拒绝(`AMAR1007`)。

### 注册元数据与实现集合

生成器会为**每个服务类型**额外生成两条注册:

```csharp
// 1) 元数据:逐注册一条(key、实现、生命周期、Order)
services.AddSingleton<IEnumerable<RegisterItemInfo<IConnector>>>(new RegisterItemInfo<IConnector>[]
{
    new("primary", typeof(SqlConnector), ServiceLifetime.Singleton, 0),
    new("replica", typeof(ReadOnlyConnector), ServiceLifetime.Singleton, 0),
});

// 2) 实例集合:keyed + 非 keyed 实现放在一个集合里
services.AddTransient<IEnumerable<IConnector>>(sp => new IConnector[]
{
    sp.GetRequiredKeyedService<SqlConnector>(__key1),
    sp.GetRequiredKeyedService<ReadOnlyConnector>(__key2),
});
```

为什么需要它:MSDI 的 keyed 与非 keyed 走两套解析路径,原生 `IEnumerable<T>` 永远拿不到 keyed 注册。生成的集合正是为了消除这个不一致:

```csharp
provider.GetServices<IConnector>();                                        // 全部实现(含 keyed)
provider.GetRequiredService<IEnumerable<RegisterItemInfo<IConnector>>>();  // 注册清单
```

> 生成的 `IEnumerable<T>` 会**有意覆盖** MSDI 原生枚举。对同一个服务类型手动 `services.AddSingleton<T>()` 的注册不会进入集合——请统一改用特性注册。

### 读取注册清单(便捷扩展)

基本不用再手写 `GetRequiredService<IEnumerable<RegisterItemInfo<T>>>()`:

```csharp
// 某个服务类型的全部注册(没有注册时返回空列表)
IReadOnlyList<RegisterItemInfo<IConnector>> items = provider.GetRegisterItems<IConnector>();

// 按 key 取一条注册(找不到返回 null;null key 表示无 key 的那条)
RegisterItemInfo<IConnector>? primary = provider.FindRegisterItem<IConnector>("primary");

// key → 实现类型
IReadOnlyDictionary<string, Type> map = provider.GetRegisterMap<IConnector>();

// 遍历 key → 实现(无 key 的注册 key 为 null)
foreach (KeyValuePair<string?, Type> pair in items.ToKeyValuePairs())
    Console.WriteLine($"{pair.Key ?? "<none>"} -> {pair.Value.Name}");

// 手里已有清单时也可以直接查
var item = items.FindByKey("primary");
```

### 自定义特性

继承 `BaseServiceAttribute` 即可。用 `[RegistrationOptions]` 描述构造函数布局,生成器据此读取生命周期、服务类型与 Order:

```csharp
[RegistrationOptions(
    LifetimeIndex = -1,                        // 没有生命周期构造参数 → 用 DefaultLifetime
    ServiceTypeIndex = 0,                      // 构造参数 #0 是服务类型
    OrderIndex = 1,                            // 构造参数 #1 是 Order
    DefaultLifetime = ServiceLifetime.Transient)]
public sealed class CustomServiceAttribute : BaseServiceAttribute
{
    public CustomServiceAttribute(Type? serviceType = null, double order = 0)
        : base(ServiceLifetime.Transient, serviceType, order) { }
}

[CustomService(typeof(InterfaceX), Key = "v2")]   // Key/Order 是继承来的可写属性
public class ServiceX : InterfaceX { }
// → services.AddKeyedTransient<InterfaceX, ServiceX>("v2");
```

`[RegistrationOptions]` 还可以为"没写进构造参数的东西"提供**默认值**——`DefaultLifetime`、`DefaultServiceType`、`DefaultOrder`。Demo 里用 `ViewAttribute` / `ViewModelAttribute` 演示了这一点:`[View]` 默认注册为 `IView`(Transient),`[ViewModel]` 默认注册为 `IViewModel`,显式传入目标类型则覆盖默认值:

```csharp
[RegistrationOptions(
    LifetimeIndex = -1,
    ServiceTypeIndex = 0,                        // 构造参数 #0 = 目标类型
    OrderIndex = 1,                              // 构造参数 #1 = Order
    DefaultLifetime = ServiceLifetime.Transient,
    DefaultServiceType = typeof(IView),          // [View] 不写参数时 → IView
    DefaultOrder = 0)]
public sealed class ViewAttribute : BaseServiceAttribute
{
    public ViewAttribute(Type? serviceType = null, double order = 0)
        : base(ServiceLifetime.Transient, serviceType, order) { }
}

[View]                                           // → IView(Transient,Order 0)
public class MainView : IView { }

[View(Order = 1)]                                // → IView,排在 MainView 之后
public class SettingsView : IView { }

[View(typeof(ISpecialView))]                     // 显式目标覆盖默认值
public class SpecialView : IView, ISpecialView { }

[ViewModel(Key = "settings")]                    // → keyed IViewModel 注册
public class SettingsViewModel : IViewModel { }
```

完整示例在 `simple/Airflow.MSDI.AutoRegister.Demo/Attributes/`,运行 Demo 可以看到实际输出。

带 **1 个类型参数**的泛型特性会被当作"目标类型"处理(与内置泛型特性同一约定)。

## 诊断

生成器输出 `AMAR` 前缀的诊断——**Error 会阻止编译并跳过该条注册**,**Warning 只是提示**。消息支持本地化:默认英文,`zh-Hans` 走卫星资源。

| 规则 | 级别 | 说明 |
|---|---|---|
| AMAR1001 | Error | 拥有工厂方法的类必须是普通类 |
| AMAR1002 | Error | 拥有工厂方法的类不能再带注册特性 |
| AMAR1003 | Error | 工厂方法不能是 static |
| AMAR1004 | Error | 工厂方法必须 public |
| AMAR1005 | Error | 工厂方法不能是泛型方法 |
| AMAR1006 | Error | 工厂方法不能返回 void |
| AMAR1007 | Error | 工厂方法返回类型必须是引用类型 |
| AMAR1008 | Error | 工厂方法参数个数不合法 |
| AMAR1009 | Error | 单参数必须是 IServiceProvider |
| AMAR1010 | Error | 单例工厂不能带 IServiceProvider 参数 |
| AMAR2001 | Error | 注册特性作用于不支持的目标 |
| AMAR2002 | Error | 服务类型必须是接口或类 |
| AMAR2003 | Error | 泛型类不能注册 |
| AMAR2005 | Warning | 完全重复的注册(同服务类型 + key + 实现) |
| AMAR2007 | Error | 实现类型与服务类型不兼容 |
| AMAR2008 | Error | 实现类型不可实例化(abstract/static) |
| AMAR2009 | Error | 同一 targettype 下命名 key 重复 |
| AMAR3001 | Error | [AutoRegister] 方法必须声明为 partial |
| AMAR3002 | Error | [AutoRegister] 方法所在类必须 partial |
| AMAR3003 | Error | [AutoRegister] 方法不能有手写实现 |
| AMAR3004 | Error | [AutoRegister] 方法必须是 static |
| AMAR3005 | Error | [AutoRegister] 方法签名必须是 (IServiceCollection services) |
| AMAR3006 | Warning | 程序集内没有可注册的服务 |
| AMAR3007 | Error | 每个程序集只允许一个 [AutoRegister] 方法 |
| AMAR4001 | Error | 缺少 [RegistrationOptions] |
| AMAR4002 | Error | [RegistrationOptions] 索引超出构造函数参数个数 |
| AMAR4003 | Error | [RegistrationOptions] 索引指向的参数类型不对 |
| AMAR4004 | Error | 生命周期枚举值非法 |
| AMAR4005 | Warning | 继承链上存在多个 [RegistrationOptions] |
| AMAR4006 | Error | 泛型注册特性的类型参数超过 1 个 |
| AMAR4007 | Error | 注册特性中服务类型冲突 |

## 兼容性

- 依赖 **`Microsoft.Extensions.DependencyInjection.Abstractions` 8.0.0**(keyed 服务的最低版本);
- **keyed 注册在运行期需要 MSDI 8.0+(.NET 8+)**;非 keyed 注册可在旧容器上继续工作,但包依赖已被抬高;
- 生成器目标 `netstandard2.0`,运行于 .NET SDK 自带的 Roslyn;
- 生成的代码需要 **C# 9+**;**泛型特性语法需要 C# 11+**(旧 `typeof(...)` 写法 C# 9+ 即可)。

## 从 1.x 迁移

| 变化 | 影响 |
|---|---|
| **同一实现、同一生命周期共享一份实例** | 同一类的多条接口注册现在共用一个实例(1.x 在没有自身注册时是每条一份) |
| 单条注册也生成锚点 + 别名 | 每个实现多一条 keyed descriptor,行为不变 |
| `[AutoRegister]` 仍是每个程序集一个 | 不变 |
| **命名 key 在同一 targettype 下必须唯一**(`AMAR2009`,Error) | 1.x 没有 key;多实现请各给一个 key |
| 移除 `AMAR2006` | 实例已按生命周期分组,不再存在自身/接口生命周期错位 |
| 每个服务类型都会生成 `IEnumerable<T>` | 覆盖 MSDI 原生枚举以纳入 keyed 实现;手动注册的同类型服务不在其中 |
| `Abstractions` 1.0.0 → 8.0.0 | keyed 运行期需要 MSDI 8.0+ |
| `Key` / `Order` 属性、泛型特性、`RegisterItemInfo<T>` | 新增 API |

## 相关文档

- 英文 README:[README.md](../README.md)
- AI/LLM 快速上手(可直接粘贴的提示词 + 速查表):[AI-QUICKSTART.md](./AI-QUICKSTART.md)

## 许可

[MIT](../LICENSE)
