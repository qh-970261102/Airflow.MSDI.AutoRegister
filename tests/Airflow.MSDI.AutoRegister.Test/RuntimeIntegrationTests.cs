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

        public class ServiceH
        {
            [SingletonService]
            public ServiceI GetServiceI() => new();
            [ScopedService]
            public ServiceJ GetServiceJ(IServiceProvider sp) => new();
        }

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

        public object ResolveFromScope(IServiceScope scope, string typeName) =>
            scope.ServiceProvider.GetRequiredService(GetType(typeName));

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
    public void RegistrationCount_InterfaceF_HasTwo()
    {
        using var fixture = new RuntimeFixture();
        using var scope = fixture.CreateScope();
        var list = scope.ServiceProvider.GetServices(fixture.GetType("InterfaceF")).ToList();
        Assert.HasCount(2, list, "InterfaceF 应有两次注册(ServiceG + ServiceF)");
    }
}
