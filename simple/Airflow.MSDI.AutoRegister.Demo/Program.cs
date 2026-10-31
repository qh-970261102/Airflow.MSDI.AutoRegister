using Airflow.MSDI.AutoRegister;
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

// --- keyed 注册:同 key 的自身/接口指向同一实例 ---
var sqlSelf = provider.GetRequiredKeyedService<SqlConnector>("primary");
var sqlIface = provider.GetRequiredKeyedService<IConnector>("primary");
Console.WriteLine($"keyed 同 key 自身/接口同一实例: {ReferenceEquals(sqlSelf, sqlIface)}");

// --- keyed 与 key 的唯一性:不同 key 解析到不同实现 ---
var connectorByPrimary = provider.GetRequiredKeyedService<IConnector>("primary");
var connectorByReplica = provider.GetRequiredKeyedService<IConnector>("replica");
Console.WriteLine($"keyed 不同 key 解析到不同实现: {connectorByPrimary.Name} / {connectorByReplica.Name}");

// --- 集合:一次拿到 keyed + 非 keyed 的全部实现(MSDI 原生 IEnumerable 只含非 keyed) ---
var connectors = provider.GetServices<IConnector>().ToList();
Console.WriteLine($"IEnumerable<IConnector> 全部实现({connectors.Count}): [{string.Join(", ", connectors.Select(c => c.Name))}]");

// --- 无 key 多实现:直接解析按 Order 后注册胜出,集合仍能拿到全部 ---
var interfaceFAll = provider.GetServices<InterfaceF>().ToList();
Console.WriteLine($"IEnumerable<InterfaceF> 无 key 多实现({interfaceFAll.Count}): [{string.Join(", ", interfaceFAll.Select(x => x.GetType().Name))}]");

// --- 注册元数据:每个服务类型一份,逐注册列出 key/实现/生命周期/Order ---
var registerItems = provider.GetRegisterItems<IConnector>();
Console.WriteLine($"GetRegisterItems<IConnector>() 共 {registerItems.Count} 条:");
foreach (var pair in registerItems.ToKeyValuePairs())
    Console.WriteLine($"  key={pair.Key ?? "<none>"} -> {pair.Value.Name}");

// --- 扩展方法:按 key 查实现类型、取 key→实现映射 ---
var primaryItem = provider.FindRegisterItem<IConnector>("primary");
Console.WriteLine($"FindRegisterItem<IConnector>(\"primary\") -> {primaryItem?.ImplementationType.Name}");
var registerMap = provider.GetRegisterMap<IConnector>();
Console.WriteLine($"GetRegisterMap<IConnector>(): {string.Join(", ", registerMap.Select(kv => $"{kv.Key}={kv.Value.Name}"))}");

// --- keyed 工厂方法:拥有类自动注册为非 keyed 单例 ---
var redis = provider.GetRequiredKeyedService<ICache>("redis");
Console.WriteLine($"keyed 工厂(Singleton, key=redis): {redis.Name}");
using (var scope1 = provider.CreateScope())
using (var scope2 = provider.CreateScope())
{
    var m1 = scope1.ServiceProvider.GetRequiredKeyedService<ICache>("memory");
    var m2 = scope1.ServiceProvider.GetRequiredKeyedService<ICache>("memory");
    var m3 = scope2.ServiceProvider.GetRequiredKeyedService<ICache>("memory");
    Console.WriteLine($"keyed 工厂(Scoped, key=memory): 同 Scope 同一实例 {ReferenceEquals(m1, m2)}, 跨 Scope 不同实例 {!ReferenceEquals(m1, m3)}");
}

// --- 同一实现、同一生命周期的多条接口注册共享一份实例 ---
var interfaceM1 = provider.GetRequiredService<ISharedLeft>();
var interfaceM2 = provider.GetRequiredService<ISharedRight>();
Console.WriteLine($"同一实现多接口共享实例(ServiceM): {ReferenceEquals(interfaceM1, interfaceM2)}");

// --- 自定义特性:默认 targettype / 默认生命周期(DefaultServiceType / DefaultLifetime) ---
var views = provider.GetServices<IView>().ToList();
Console.WriteLine($"自定义 [View] 默认注册为 IView({views.Count}): [{string.Join(", ", views.Select(v => v.GetType().Name))}]");

var specialView = provider.GetRequiredService<ISpecialView>();
Console.WriteLine($"自定义 [View(typeof(ISpecialView))] 覆盖默认目标: {specialView.GetType().Name}");

var viewModels = provider.GetServices<IViewModel>().ToList();
Console.WriteLine($"自定义 [ViewModel] 默认注册为 IViewModel({viewModels.Count}): [{string.Join(", ", viewModels.Select(v => v.GetType().Name))}]");

var namedViewModel = provider.GetRequiredKeyedService<IViewModel>("settings");
Console.WriteLine($"自定义 [ViewModel(Key = \"settings\")] 命名 key: {namedViewModel.GetType().Name}");

Console.WriteLine("IView 注册元数据:");
foreach (var pair in provider.GetRegisterItems<IView>().ToKeyValuePairs())
    Console.WriteLine($"  key={pair.Key ?? "<none>"} -> {pair.Value.Name}");

Console.WriteLine();
Console.WriteLine("Demo 完成 ✅");
