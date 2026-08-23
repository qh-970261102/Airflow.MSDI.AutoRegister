using Airflow.MSDI.AutoRegister.Demo;
using Microsoft.Extensions.DependencyInjection;

// 1. 定义服务(用 AutoRegister 特性标记,源生成器自动注册)
// 2. 调用 AddServices() 一次性注册整个程序集

var services = new ServiceCollection();
services.AddServices();

var provider = services.BuildServiceProvider();

Console.WriteLine("========== AutoRegister Demo ==========");
Console.WriteLine();

// --- 基本生命周期 ---
var a1 = provider.GetRequiredService<ServiceA>();
var a2 = provider.GetRequiredService<ServiceA>();
Console.WriteLine($"ServiceA  (Singleton)  同一实例: {ReferenceEquals(a1, a2)}");

using (var scope1 = provider.CreateScope())
using (var scope2 = provider.CreateScope())
{
    var b1 = scope1.ServiceProvider.GetRequiredService<ServiceB>();
    var b2 = scope1.ServiceProvider.GetRequiredService<ServiceB>();
    var b3 = scope2.ServiceProvider.GetRequiredService<ServiceB>();
    Console.WriteLine($"ServiceB  (Scoped)    同 Scope 同一实例: {ReferenceEquals(b1, b2)}, 跨 Scope 不同实例: {!ReferenceEquals(b1, b3)}");
}

var c1 = provider.GetRequiredService<ServiceC>();
var c2 = provider.GetRequiredService<ServiceC>();
Console.WriteLine($"ServiceC  (Transient) 每次新实例: {!ReferenceEquals(c1, c2)}");

// --- 接口注册 ---
var d = provider.GetRequiredService<InterfaceD>();
Console.WriteLine($"ServiceD -> InterfaceD, 实际类型: {d.GetType().Name}");

// --- 类自身 + 接口,同一份单例 ---
var e1 = provider.GetRequiredService<ServiceE>();
var e2 = provider.GetRequiredService<InterfaceE>();
Console.WriteLine($"ServiceE 与 InterfaceE 指向同一单例: {ReferenceEquals(e1, e2)}");

// --- Order:ServiceG(order=1) 先注册,ServiceF(order=2) 后注册,后者胜出 ---
var f = provider.GetRequiredService<InterfaceF>();
Console.WriteLine($"InterfaceF 由后注册的 ServiceF 提供 (Order 较大者胜): {f is ServiceF}");

// --- 工厂方法注册 ---
var i1 = provider.GetRequiredService<ServiceI>();
var i2 = provider.GetRequiredService<ServiceI>();
Console.WriteLine($"ServiceI  工厂方法(Singleton,持有 ServiceG): 同一实例 {ReferenceEquals(i1, i2)}, 内部依赖注入成功: {i1 != null}");

using (var scope1 = provider.CreateScope())
using (var scope2 = provider.CreateScope())
{
    var j1 = scope1.ServiceProvider.GetRequiredService<ServiceJ>();
    var j2 = scope1.ServiceProvider.GetRequiredService<ServiceJ>();
    var j3 = scope2.ServiceProvider.GetRequiredService<ServiceJ>();
    Console.WriteLine($"ServiceJ  工厂方法(Scoped,带 IServiceProvider): 同 Scope 同一实例 {ReferenceEquals(j1, j2)}, 跨 Scope 不同实例 {!ReferenceEquals(j1, j3)}");
}

// --- 闭合泛型服务类型注册:原样填入生成 AddSingleton<IGenericService<string>, StringService>() ---
var g = provider.GetRequiredService<IGenericService<string>>();
Console.WriteLine($"IGenericService<string> 由具体类闭合泛型后提供: {g is StringService}");

// --- 闭合泛型工厂方法:AddSingleton(sp => ...) 的服务类型由 lambda 返回值推断为 List<string> ---
var list = provider.GetRequiredService<List<string>>();
Console.WriteLine($"List<string> 由工厂方法注册并解析成功: {list != null}");

Console.WriteLine();
Console.WriteLine("Demo 完成 ✅");
