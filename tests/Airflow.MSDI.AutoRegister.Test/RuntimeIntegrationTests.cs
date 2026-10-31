using System.Reflection;
using Microsoft.Extensions.DependencyInjection;

namespace Airflow.MSDI.AutoRegister.Test;

/// <summary>端到端验证:生成器输出 + 真实 MSDI 容器。
/// 被测类型定义在动态编译的程序集中,通过反射解析。</summary>
[TestClass]
public sealed class RuntimeIntegrationTests
{
    private const string Source = """
        using System;
        using System.Collections.Generic;
        using Microsoft.Extensions.DependencyInjection;

        namespace RuntimeDemo;

        [SingletonService]
        public class ServiceA { }

        [ScopedService]
        public class ServiceB { }

        [TransientService]
        public class ServiceC { }

        [SingletonService(typeof(InterfaceD))]
        public class ServiceD : InterfaceD { }

        [SingletonService]
        [SingletonService(typeof(InterfaceE))]
        public class ServiceE : InterfaceE { }

        [SingletonService(typeof(InterfaceF), 2)]
        public class ServiceF : InterfaceF { }

        [SingletonService(typeof(InterfaceF), 1)]
        public class ServiceG : InterfaceF { }

        public interface InterfaceG<T> { }
        public class TypeG1 { }

        // 闭合泛型服务类型:原样生成 AddSingleton<InterfaceG<TypeG1>, ServiceK>()
        [SingletonService(typeof(InterfaceG<TypeG1>))]
        public class ServiceK : InterfaceG<TypeG1> { }

        public class ServiceH
        {
            [SingletonService]
            public ServiceI GetServiceI() => new();
            [ScopedService]
            public ServiceJ GetServiceJ(IServiceProvider sp) => new();
        }

        public class ServiceL
        {
            // 闭合泛型返回类型:生成 AddSingleton(sp => ...GetTypeG1List()),服务类型 List<TypeG1> 由 lambda 推断
            [SingletonService]
            public List<TypeG1> GetTypeG1List() => new();
        }

        // 多接口、同生命周期:共享一个实例
        public interface InterfaceM1 { }
        public interface InterfaceM2 { }
        [SingletonService(typeof(InterfaceM1))]
        [SingletonService(typeof(InterfaceM2))]
        public class ServiceM : InterfaceM1, InterfaceM2 { }

        // keyed:自身 + 接口同 key,共享一个实例
        public interface IKeyed { }

        [SingletonService(Key = "primary")]
        [SingletonService(typeof(IKeyed), Key = "primary")]
        public class KeyedA : IKeyed { }

        // keyed:同一实现不同 key,仍共享一个实例
        [SingletonService(typeof(IKeyed), Key = "k1")]
        [SingletonService(typeof(IKeyed), Key = "k2")]
        public class KeyedB : IKeyed { }

        // keyed 与非 keyed 自注册并存,共享一个实例
        [SingletonService]
        [SingletonService(Key = "named")]
        public class KeyedC { }

        // 泛型特性写法 + scoped
        [ScopedService<IKeyed>(Key = "generic")]
        public class KeyedD : IKeyed { }

        public interface InterfaceD { }
        public interface InterfaceE { }
        public interface InterfaceF { }
        public class ServiceI { }
        public class ServiceJ { }

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
        """;

    private sealed class RuntimeFixture : IDisposable
    {
        private readonly Assembly _assembly;
        private readonly ServiceProvider _provider;

        public RuntimeFixture()
        {
            var (_, outputCompilation, _) = GeneratorTestHelper.RunGenerator(Source);
            _assembly = GeneratorTestHelper.LoadAssembly(outputCompilation);

            var registerType = GetType("Register");
            var method = registerType.GetMethod("AddServicesFromCurrentAssembly", BindingFlags.Public | BindingFlags.Static)!;

            var services = new ServiceCollection();
            method.Invoke(null, new object[] { services });
            _provider = services.BuildServiceProvider();
        }

        public Type GetType(string name) => _assembly.GetType($"RuntimeDemo.{name}", throwOnError: true)!;

        public object Resolve(string typeName) => _provider.GetRequiredService(GetType(typeName));

        public object Resolve(Type serviceType) => _provider.GetRequiredService(serviceType);

        public object ResolveKeyed(string typeName, object key) =>
            _provider.GetRequiredKeyedService(GetType(typeName), key);

        public List<object> ResolveAll(string typeName) =>
            _provider.GetServices(GetType(typeName)).Select(x => (object)x!).ToList();

        public object ResolveFromScope(IServiceScope scope, string typeName) =>
            scope.ServiceProvider.GetRequiredService(GetType(typeName));

        public object ResolveKeyedFromScope(IServiceScope scope, string typeName, object key) =>
            scope.ServiceProvider.GetRequiredKeyedService(GetType(typeName), key);

        public List<object> ResolveAllFromScope(IServiceScope scope, string typeName) =>
            scope.ServiceProvider.GetServices(GetType(typeName)).Select(x => (object)x!).ToList();

        /// <summary>反射读取 RegisterItemInfo&lt;T&gt; 列表。</summary>
        public List<(string? Key, string ImplementationType, string Lifetime, double Order)> GetMetadata(string typeName)
        {
            var itemType = typeof(RegisterItemInfo<>).MakeGenericType(GetType(typeName));
            var listType = typeof(IEnumerable<>).MakeGenericType(itemType);
            var items = (System.Collections.IEnumerable)_provider.GetRequiredService(listType);

            var rows = new List<(string?, string, string, double)>();
            foreach (var item in items)
            {
                var type = item!.GetType();
                rows.Add((
                    (string?)type.GetProperty("Key")!.GetValue(item),
                    ((Type)type.GetProperty("ImplementationType")!.GetValue(item)!).Name,
                    type.GetProperty("Lifetime")!.GetValue(item)!.ToString()!,
                    (double)type.GetProperty("Order")!.GetValue(item)!));
            }
            return rows;
        }

        public IServiceScope CreateScope() => _provider.CreateScope();

        public void Dispose() => _provider.Dispose();
    }

    [TestMethod]
    public void Singleton_ReturnsSameInstance()
    {
        using var fixture = new RuntimeFixture();
        var a1 = fixture.Resolve("ServiceA");
        var a2 = fixture.Resolve("ServiceA");
        Assert.AreSame(a1, a2);
    }

    [TestMethod]
    public void Scoped_SharedWithinScope_NewAcrossScopes()
    {
        using var fixture = new RuntimeFixture();
        using var scope1 = fixture.CreateScope();
        using var scope2 = fixture.CreateScope();

        var b1 = fixture.ResolveFromScope(scope1, "ServiceB");
        var b2 = fixture.ResolveFromScope(scope1, "ServiceB");
        var b3 = fixture.ResolveFromScope(scope2, "ServiceB");

        Assert.AreSame(b1, b2);
        Assert.AreNotSame(b1, b3);
    }

    [TestMethod]
    public void Transient_NewInstanceEachTime()
    {
        using var fixture = new RuntimeFixture();
        var c1 = fixture.Resolve("ServiceC");
        var c2 = fixture.Resolve("ServiceC");
        Assert.AreNotSame(c1, c2);
    }

    [TestMethod]
    public void InterfaceDirect_ResolvesToImplementation()
    {
        using var fixture = new RuntimeFixture();
        var d = fixture.Resolve("InterfaceD");
        Assert.AreEqual("RuntimeDemo.ServiceD", d.GetType().FullName);
    }

    [TestMethod]
    public void InterfaceViaSelf_ResolvesToSameSingleton()
    {
        using var fixture = new RuntimeFixture();
        var e1 = fixture.Resolve("ServiceE");
        var e2 = fixture.Resolve("InterfaceE");
        Assert.AreSame(e1, e2);
    }

    [TestMethod]
    public void Order_LargerOrderWins()
    {
        using var fixture = new RuntimeFixture();
        // ServiceG order=1 先注册,ServiceF order=2 后注册 → MSDI 后者胜出
        var f = fixture.Resolve("InterfaceF");
        Assert.AreEqual("RuntimeDemo.ServiceF", f.GetType().FullName);
    }

    [TestMethod]
    public void FactoryMethod_Singleton_Works()
    {
        using var fixture = new RuntimeFixture();
        var i1 = fixture.Resolve("ServiceI");
        var i2 = fixture.Resolve("ServiceI");
        Assert.AreSame(i1, i2);
    }

    [TestMethod]
    public void FactoryMethod_Scoped_WithServiceProviderParam_Works()
    {
        using var fixture = new RuntimeFixture();
        using var scope1 = fixture.CreateScope();
        using var scope2 = fixture.CreateScope();

        var j1 = fixture.ResolveFromScope(scope1, "ServiceJ");
        var j2 = fixture.ResolveFromScope(scope1, "ServiceJ");
        var j3 = fixture.ResolveFromScope(scope2, "ServiceJ");

        Assert.AreSame(j1, j2);
        Assert.AreNotSame(j1, j3);
    }

    [TestMethod]
    public void ClosedGenericInterface_ResolvesToImplementation()
    {
        using var fixture = new RuntimeFixture();
        // InterfaceG<TypeG1> 由 ServiceK 实现,构造闭合泛型类型后解析
        var serviceType = fixture.GetType("InterfaceG`1").MakeGenericType(fixture.GetType("TypeG1"));
        var k = fixture.Resolve(serviceType);
        Assert.AreEqual("RuntimeDemo.ServiceK", k.GetType().FullName);
    }

    [TestMethod]
    public void ClosedGenericFactoryReturnType_Resolves()
    {
        using var fixture = new RuntimeFixture();
        // 工厂返回 List<TypeG1>,推断式注册后真实容器应能解析出该闭合泛型
        var listType = typeof(List<>).MakeGenericType(fixture.GetType("TypeG1"));
        var list = fixture.Resolve(listType);
        Assert.IsInstanceOfType(list, listType);
    }

    [TestMethod]
    public void RegistrationCount_InterfaceF_HasTwo()
    {
        using var fixture = new RuntimeFixture();
        var list = fixture.ResolveAll("InterfaceF");
        Assert.HasCount(2, list, "InterfaceF 应有两次注册(ServiceG + ServiceF)");
    }

    [TestMethod]
    public void MultipleInterfaces_SameLifetime_ShareInstance()
    {
        using var fixture = new RuntimeFixture();
        var m1 = fixture.Resolve("InterfaceM1");
        var m2 = fixture.Resolve("InterfaceM2");
        Assert.AreSame(m1, m2, "同一实现、同一生命周期的多条接口注册应共享实例");
    }

    [TestMethod]
    public void KeyedSelfAndInterface_SameKey_ShareInstance()
    {
        using var fixture = new RuntimeFixture();
        var self = fixture.ResolveKeyed("KeyedA", "primary");
        var iface = fixture.ResolveKeyed("IKeyed", "primary");
        Assert.AreSame(self, iface);
    }

    [TestMethod]
    public void KeyedRegistration_CrossKey_ShareInstance()
    {
        using var fixture = new RuntimeFixture();
        var k1 = fixture.ResolveKeyed("IKeyed", "k1");
        var k2 = fixture.ResolveKeyed("IKeyed", "k2");
        Assert.AreSame(k1, k2, "同一实现的不同 key 注册应共享实例");
    }

    [TestMethod]
    public void UnkeyedAndKeyedSelf_ShareInstance()
    {
        using var fixture = new RuntimeFixture();
        var plain = fixture.Resolve("KeyedC");
        var named = fixture.ResolveKeyed("KeyedC", "named");
        Assert.AreSame(plain, named);
    }

    [TestMethod]
    public void GenericAttributeKeyedScoped_ResolvesPerScope()
    {
        using var fixture = new RuntimeFixture();
        using var scope1 = fixture.CreateScope();
        using var scope2 = fixture.CreateScope();

        var d1 = fixture.ResolveKeyedFromScope(scope1, "IKeyed", "generic");
        var d2 = fixture.ResolveKeyedFromScope(scope1, "IKeyed", "generic");
        var d3 = fixture.ResolveKeyedFromScope(scope2, "IKeyed", "generic");

        Assert.AreEqual("RuntimeDemo.KeyedD", d1.GetType().FullName);
        Assert.AreSame(d1, d2);
        Assert.AreNotSame(d1, d3);
    }

    [TestMethod]
    public void Collection_IncludesKeyedImplementations()
    {
        using var fixture = new RuntimeFixture();
        using var scope = fixture.CreateScope();
        var all = fixture.ResolveAllFromScope(scope, "IKeyed");

        Assert.HasCount(4, all, "IKeyed 集合应包含全部 4 条注册(含 keyed)");
        CollectionAssert.AreEquivalent(
            new[] { "RuntimeDemo.KeyedA", "RuntimeDemo.KeyedB", "RuntimeDemo.KeyedB", "RuntimeDemo.KeyedD" },
            all.Select(x => x.GetType().FullName).ToArray());
    }

    [TestMethod]
    public void Metadata_DescribesKeyedRegistrations()
    {
        using var fixture = new RuntimeFixture();
        var rows = fixture.GetMetadata("IKeyed");

        Assert.HasCount(4, rows);
        CollectionAssert.AreEquivalent(
            new[] { "primary", "k1", "k2", "generic" },
            rows.Select(r => r.Key ?? "<null>").ToArray());
        Assert.IsTrue(rows.All(r => r.Lifetime is "Singleton" or "Scoped"));
    }

    [TestMethod]
    public void Metadata_UnkeyedMultiImplementation_HasBothEntries()
    {
        using var fixture = new RuntimeFixture();
        var rows = fixture.GetMetadata("InterfaceF");

        Assert.HasCount(2, rows);
        Assert.IsTrue(rows.All(r => r.Key is null));
        CollectionAssert.AreEquivalent(
            new[] { "ServiceG", "ServiceF" },
            rows.Select(r => r.ImplementationType).ToArray());
        Assert.AreEqual(1d, rows[0].Order);
        Assert.AreEqual(2d, rows[1].Order);
    }

    [TestMethod]
    public void MetadataAndCollection_ForSameInstanceType()
    {
        using var fixture = new RuntimeFixture();
        var rows = fixture.GetMetadata("KeyedC");

        // 一个无 key、一个 keyed:元数据两条,集合两项(均解析到同一实例)
        Assert.HasCount(2, rows);
        var all = fixture.ResolveAll("KeyedC");
        Assert.HasCount(2, all);
        Assert.AreSame(all[0], all[1]);
    }
}
