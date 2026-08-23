using System.Collections.Immutable;
using Microsoft.CodeAnalysis;

namespace Airflow.MSDI.AutoRegister.Test;

/// <summary>逐条验证 1.1 版本的 AMAR 诊断:触发条件、消息内容与"报错即跳过生成"。</summary>
[TestClass]
public sealed class DiagnosticTests
{
    private const string RegisterSnippet = """
        internal static partial class Register
        {
            [AutoRegister]
            public static partial void AddServices(IServiceCollection services);
        }
        """;

    // ---- 合法源码:零诊断 ----

    [TestMethod]
    public void ValidSource_ReportsNoDiagnostics()
    {
        var (_, _, diagnostics) = GeneratorTestHelper.RunGenerator("""
            using System;
            using Microsoft.Extensions.DependencyInjection;
            namespace Demo;

            [SingletonService]
            public class ServiceA { }

            [ScopedService]
            public class ServiceB { }

            [SingletonService(typeof(InterfaceD))]
            public class ServiceD : InterfaceD { }

            public class ServiceH
            {
                [SingletonService]
                public ServiceI GetServiceI() => new();
                [ScopedService]
                public ServiceJ GetServiceJ(IServiceProvider sp) => new();
            }

            public interface InterfaceD { }
            public class ServiceI { }
            public class ServiceJ { }

            internal static partial class Register
            {
                [AutoRegister]
                public static partial void AddServicesFromCurrentAssembly(IServiceCollection services);
            }
            """);

        Assert.IsFalse(diagnostics.Any(d => d.Id.StartsWith("AMAR", StringComparison.Ordinal)),
            "合法源码不应产生任何 AMAR 诊断:\n" + string.Join("\n", diagnostics.Select(d => d.ToString())));
    }

    // ---- 1. 工厂方法相关 ----

    [TestMethod]
    public void AMAR1001_AbstractOwner_Reports()
    {
        var (code, _, diagnostics) = GeneratorTestHelper.RunGenerator($$"""
            using Microsoft.Extensions.DependencyInjection;
            namespace Demo;
            public abstract class ServiceH
            {
                [SingletonService]
                public ServiceI GetServiceI() => new();
            }
            public class ServiceI { }
            {{RegisterSnippet}}
            """);

        AssertHas(diagnostics, "AMAR1001", "ServiceH");
        Assert.DoesNotContain( "GetServiceI", code,  "非法工厂方法不应生成注册代码");
        Assert.DoesNotContain( "AddSingleton<global::Demo.ServiceH>", code,  "形态非法的工厂方法不应触发拥有类自动注册");
    }

    [TestMethod]
    public void AMAR1002_OwnerWithRegistrationAttribute_Reports()
    {
        var (code, _, diagnostics) = GeneratorTestHelper.RunGenerator($$"""
            using Microsoft.Extensions.DependencyInjection;
            namespace Demo;
            [SingletonService]
            public class ServiceH
            {
                [SingletonService]
                public ServiceI GetServiceI() => new();
            }
            public class ServiceI { }
            {{RegisterSnippet}}
            """);

        AssertHas(diagnostics, "AMAR1002", "ServiceH");
        // 拥有类自身的特性注册仍然生成,但工厂条目被跳过。
        StringAssert.Contains(code, "services.AddSingleton<global::Demo.ServiceH>();");
        Assert.DoesNotContain( "GetServiceI", code,  "AMAR1002 报错后工厂条目应被跳过");
    }

    [TestMethod]
    public void AMAR1003_StaticFactoryMethod_Reports()
    {
        var (_, _, diagnostics) = GeneratorTestHelper.RunGenerator($$"""
            using Microsoft.Extensions.DependencyInjection;
            namespace Demo;
            public class ServiceH
            {
                [SingletonService]
                public static ServiceI GetServiceI() => new();
            }
            public class ServiceI { }
            {{RegisterSnippet}}
            """);

        AssertHas(diagnostics, "AMAR1003", "GetServiceI");
    }

    [TestMethod]
    public void AMAR1004_NonPublicFactoryMethod_Reports()
    {
        var (_, _, diagnostics) = GeneratorTestHelper.RunGenerator($$"""
            using Microsoft.Extensions.DependencyInjection;
            namespace Demo;
            public class ServiceH
            {
                [SingletonService]
                internal ServiceI GetServiceI() => new();
            }
            public class ServiceI { }
            {{RegisterSnippet}}
            """);

        AssertHas(diagnostics, "AMAR1004", "GetServiceI");
    }

    [TestMethod]
    public void AMAR1005_GenericFactoryMethod_Reports()
    {
        var (_, _, diagnostics) = GeneratorTestHelper.RunGenerator($$"""
            using Microsoft.Extensions.DependencyInjection;
            namespace Demo;
            public class ServiceH
            {
                [SingletonService]
                public ServiceI GetServiceI<T>() => new();
            }
            public class ServiceI { }
            {{RegisterSnippet}}
            """);

        AssertHas(diagnostics, "AMAR1005", "GetServiceI");
    }

    [TestMethod]
    public void AMAR1006_VoidFactoryMethod_Reports()
    {
        var (_, _, diagnostics) = GeneratorTestHelper.RunGenerator($$"""
            using Microsoft.Extensions.DependencyInjection;
            namespace Demo;
            public class ServiceH
            {
                [SingletonService]
                public void GetServiceI() { }
            }
            {{RegisterSnippet}}
            """);

        AssertHas(diagnostics, "AMAR1006", "GetServiceI");
    }

    [TestMethod]
    public void AMAR1007_GenericReturnType_Reports()
    {
        var (_, _, diagnostics) = GeneratorTestHelper.RunGenerator($$"""
            using System.Collections.Generic;
            using Microsoft.Extensions.DependencyInjection;
            namespace Demo;
            public class ServiceH
            {
                [SingletonService]
                public List<ServiceI> GetServiceI() => new();
            }
            public class ServiceI { }
            {{RegisterSnippet}}
            """);

        AssertHas(diagnostics, "AMAR1007", "GetServiceI");
    }

    [TestMethod]
    public void AMAR1008_TwoParameters_Reports()
    {
        var (_, _, diagnostics) = GeneratorTestHelper.RunGenerator($$"""
            using Microsoft.Extensions.DependencyInjection;
            namespace Demo;
            public class ServiceH
            {
                [SingletonService]
                public ServiceI GetServiceI(int a, int b) => new();
            }
            public class ServiceI { }
            {{RegisterSnippet}}
            """);

        AssertHas(diagnostics, "AMAR1008", "GetServiceI");
        AssertNotHas(diagnostics, "AMAR1009");
    }

    [TestMethod]
    public void AMAR1009_SingleNonServiceProviderParameter_Reports()
    {
        var (_, _, diagnostics) = GeneratorTestHelper.RunGenerator($$"""
            using Microsoft.Extensions.DependencyInjection;
            namespace Demo;
            public class ServiceH
            {
                [SingletonService]
                public ServiceI GetServiceI(int a) => new();
            }
            public class ServiceI { }
            {{RegisterSnippet}}
            """);

        // 单参数但非 IServiceProvider:只报 1009,不叠加 1008。
        AssertHas(diagnostics, "AMAR1009", "GetServiceI", "IServiceProvider");
        AssertNotHas(diagnostics, "AMAR1008");
    }

    [TestMethod]
    public void AMAR1010_SingletonFactoryWithServiceProvider_Reports()
    {
        var (code, _, diagnostics) = GeneratorTestHelper.RunGenerator($$"""
            using System;
            using Microsoft.Extensions.DependencyInjection;
            namespace Demo;
            public class ServiceH
            {
                [SingletonService]
                public ServiceI GetServiceI(IServiceProvider sp) => new();
            }
            public class ServiceI { }
            {{RegisterSnippet}}
            """);

        AssertHas(diagnostics, "AMAR1010", "GetServiceI");
        Assert.DoesNotContain( "GetServiceI", code,  "单例工厂带 sp 参数应跳过生成");
        Assert.DoesNotContain( "AddSingleton<global::Demo.ServiceH>", code,  "工厂条目全部跳过时拥有类不应自动注册");
    }

    [TestMethod]
    public void AMAR1010_NotReported_ForScopedFactoryWithServiceProvider()
    {
        var (code, _, diagnostics) = GeneratorTestHelper.RunGenerator($$"""
            using System;
            using Microsoft.Extensions.DependencyInjection;
            namespace Demo;
            public class ServiceH
            {
                [ScopedService]
                public ServiceJ GetServiceJ(IServiceProvider sp) => new();
            }
            public class ServiceJ { }
            {{RegisterSnippet}}
            """);

        AssertNotHas(diagnostics, "AMAR1010");
        StringAssert.Contains(code, "services.AddScoped<global::Demo.ServiceJ>(sp => sp.GetRequiredService<global::Demo.ServiceH>().GetServiceJ(sp));");
        StringAssert.Contains(code, "services.AddSingleton<global::Demo.ServiceH>();");
    }

    // ---- 2. 类注册相关 ----

    [TestMethod]
    public void AMAR2001_AttributeOnOperator_Reports()
    {
        var (_, _, diagnostics) = GeneratorTestHelper.RunGenerator($$"""
            using Microsoft.Extensions.DependencyInjection;
            namespace Demo;
            [SingletonService]
            public class ServiceA
            {
                [SingletonService]
                public static ServiceA operator +(ServiceA a, ServiceA b) => a;
            }
            {{RegisterSnippet}}
            """);

        AssertHas(diagnostics, "AMAR2001");
    }

    [TestMethod]
    public void AMAR2002_ValueTypeServiceType_Reports()
    {
        var (_, _, diagnostics) = GeneratorTestHelper.RunGenerator($$"""
            using Microsoft.Extensions.DependencyInjection;
            namespace Demo;
            [SingletonService(typeof(int))]
            public class ServiceA { }
            {{RegisterSnippet}}
            """);

        AssertHas(diagnostics, "AMAR2002", "int");
        Assert.IsFalse(diagnostics.Any(d => d.Id == "AMAR2001"), "值类型服务类型不应误报为目标错误");
    }

    [TestMethod]
    public void AMAR2002_ArrayServiceType_Reports()
    {
        var (_, _, diagnostics) = GeneratorTestHelper.RunGenerator($$"""
            using Microsoft.Extensions.DependencyInjection;
            namespace Demo;
            [SingletonService(typeof(int[]))]
            public class ServiceA { }
            {{RegisterSnippet}}
            """);

        AssertHas(diagnostics, "AMAR2002", "int[]");
    }

    [TestMethod]
    public void AMAR2003_GenericClass_Reports()
    {
        var (code, _, diagnostics) = GeneratorTestHelper.RunGenerator($$"""
            using Microsoft.Extensions.DependencyInjection;
            namespace Demo;
            [SingletonService]
            public class ServiceA<T> { }
            {{RegisterSnippet}}
            """);

        AssertHas(diagnostics, "AMAR2003", "ServiceA");
        Assert.DoesNotContain( "ServiceA", code,  "泛型类注册应被跳过,不生成代码");
    }

    [TestMethod]
    public void AMAR2005_DuplicateSelfRegistration_ReportsWarning()
    {
        var (_, _, diagnostics) = GeneratorTestHelper.RunGenerator($$"""
            using Microsoft.Extensions.DependencyInjection;
            namespace Demo;
            [SingletonService]
            [SingletonService]
            public class ServiceA { }
            {{RegisterSnippet}}
            """);

        AssertHas(diagnostics, "AMAR2005", "ServiceA", "ServiceA");
        Assert.AreEqual(DiagnosticSeverity.Warning, Find(diagnostics, "AMAR2005")!.Severity);
    }

    [TestMethod]
    public void AMAR2006_InconsistentLifetimes_ReportsWarning()
    {
        var (_, _, diagnostics) = GeneratorTestHelper.RunGenerator($$"""
            using Microsoft.Extensions.DependencyInjection;
            namespace Demo;
            [SingletonService]
            [ScopedService(typeof(InterfaceA))]
            public class ServiceA : InterfaceA { }
            public interface InterfaceA { }
            {{RegisterSnippet}}
            """);

        AssertHas(diagnostics, "AMAR2006", "ServiceA", "Singleton", "Scoped");
        Assert.AreEqual(DiagnosticSeverity.Warning, Find(diagnostics, "AMAR2006")!.Severity);
    }

    // ---- 3. AutoRegister 方法相关 ----

    [TestMethod]
    public void AMAR3001_NotPartialMethod_Reports()
    {
        var (code, _, diagnostics) = GeneratorTestHelper.RunGenerator("""
            using Microsoft.Extensions.DependencyInjection;
            namespace Demo;
            [SingletonService]
            public class ServiceA { }

            internal static partial class Register
            {
                [AutoRegister]
                public static void AddServices(IServiceCollection services) { }
            }
            """);

        AssertHas(diagnostics, "AMAR3001", "AddServices");
        AssertHas(diagnostics, "AMAR3003", "AddServices"); // 非 partial + 带方法体:两条都报
        Assert.AreEqual(string.Empty, code, "非法 [AutoRegister] 方法不应生成实现");
    }

    [TestMethod]
    public void AMAR3002_ClassNotPartial_Reports()
    {
        var (_, _, diagnostics) = GeneratorTestHelper.RunGenerator("""
            using Microsoft.Extensions.DependencyInjection;
            namespace Demo;
            internal static class Register
            {
                [AutoRegister]
                public static partial void AddServices(IServiceCollection services);
            }
            """);

        AssertHas(diagnostics, "AMAR3002", "AddServices", "Register");
    }

    [TestMethod]
    public void AMAR3003_PartialMethodWithBody_Reports()
    {
        var (_, _, diagnostics) = GeneratorTestHelper.RunGenerator("""
            using Microsoft.Extensions.DependencyInjection;
            namespace Demo;
            internal static partial class Register
            {
                [AutoRegister]
                public static partial void AddServices(IServiceCollection services) { }
            }
            """);

        AssertHas(diagnostics, "AMAR3003", "AddServices");
        AssertNotHas(diagnostics, "AMAR3001"); // 已声明 partial,只报实现冲突
    }

    [TestMethod]
    public void AMAR3004_NotStatic_Reports()
    {
        var (_, _, diagnostics) = GeneratorTestHelper.RunGenerator("""
            using Microsoft.Extensions.DependencyInjection;
            namespace Demo;
            internal partial class Register
            {
                [AutoRegister]
                public partial void AddServices(IServiceCollection services);
            }
            """);

        AssertHas(diagnostics, "AMAR3004", "AddServices");
    }

    [TestMethod]
    public void AMAR3005_WrongParameterList_Reports()
    {
        var (_, _, diagnostics) = GeneratorTestHelper.RunGenerator("""
            using Microsoft.Extensions.DependencyInjection;
            namespace Demo;
            internal static partial class Register
            {
                [AutoRegister]
                public static partial void AddServices();
            }
            """);

        AssertHas(diagnostics, "AMAR3005", "AddServices");
    }

    [TestMethod]
    public void AMAR3006_NoRegistrableServices_ReportsWarning()
    {
        var (code, _, diagnostics) = GeneratorTestHelper.RunGenerator("""
            using Microsoft.Extensions.DependencyInjection;
            namespace Demo;
            internal static partial class Register
            {
                [AutoRegister]
                public static partial void AddServices(IServiceCollection services);
            }
            """);

        AssertHas(diagnostics, "AMAR3006");
        Assert.AreEqual(DiagnosticSeverity.Warning, Find(diagnostics, "AMAR3006")!.Severity);
        Assert.AreEqual(string.Empty, code);
    }

    // ---- 4. 特性元数据相关 ----

    [TestMethod]
    public void AMAR4001_MissingRegistrationOptions_Reports_AndSkipsUsages()
    {
        var (code, _, diagnostics) = GeneratorTestHelper.RunGenerator($$"""
            using System;
            using Microsoft.Extensions.DependencyInjection;
            namespace Demo;
            public sealed class MyServiceAttribute : BaseServiceAttribute
            {
                public MyServiceAttribute(Type? serviceType = null, double order = 0)
                    : base(ServiceLifetime.Transient, serviceType, order) { }
            }

            [MyService]
            public class ServiceA { }
            {{RegisterSnippet}}
            """);

        AssertHas(diagnostics, "AMAR4001", "MyServiceAttribute");
        Assert.DoesNotContain( "ServiceA", code,  "缺少 [RegistrationOptions] 的特性类的使用点应被跳过");
    }

    [TestMethod]
    public void AMAR4002_IndexOutOfRange_Reports()
    {
        var (_, _, diagnostics) = GeneratorTestHelper.RunGenerator($$"""
            using Microsoft.Extensions.DependencyInjection;
            namespace Demo;
            [RegistrationOptions(LifetimeIndex = 5, ServiceTypeIndex = -1, OrderIndex = -1, DefaultLifetime = ServiceLifetime.Singleton)]
            public sealed class MyServiceAttribute : BaseServiceAttribute
            {
                public MyServiceAttribute(int unused) : base(ServiceLifetime.Singleton) { }
            }

            [MyService(1)]
            public class ServiceA { }
            {{RegisterSnippet}}
            """);

        AssertHas(diagnostics, "AMAR4002", "LifetimeIndex", "5", "MyServiceAttribute");
    }

    [TestMethod]
    public void AMAR4003_WrongParameterType_Reports()
    {
        var (_, _, diagnostics) = GeneratorTestHelper.RunGenerator($$"""
            using Microsoft.Extensions.DependencyInjection;
            namespace Demo;
            [RegistrationOptions(LifetimeIndex = 0, ServiceTypeIndex = -1, OrderIndex = -1, DefaultLifetime = ServiceLifetime.Singleton)]
            public sealed class MyServiceAttribute : BaseServiceAttribute
            {
                public MyServiceAttribute(string name) : base(ServiceLifetime.Singleton) { }
            }

            [MyService("x")]
            public class ServiceA { }
            {{RegisterSnippet}}
            """);

        AssertHas(diagnostics, "AMAR4003", "LifetimeIndex", "MyServiceAttribute");
    }

    [TestMethod]
    public void AMAR4004_InvalidLifetimeAtUsage_Reports()
    {
        var (code, _, diagnostics) = GeneratorTestHelper.RunGenerator($$"""
            using Microsoft.Extensions.DependencyInjection;
            namespace Demo;
            [RegistrationOptions(LifetimeIndex = 0, ServiceTypeIndex = -1, OrderIndex = -1, DefaultLifetime = ServiceLifetime.Singleton)]
            public sealed class MyServiceAttribute : BaseServiceAttribute
            {
                public MyServiceAttribute(ServiceLifetime lifetime) : base(lifetime) { }
            }

            [MyService((ServiceLifetime)42)]
            public class ServiceA { }
            {{RegisterSnippet}}
            """);

        AssertHas(diagnostics, "AMAR4004", "MyServiceAttribute");
        Assert.DoesNotContain( "ServiceA", code,  "生命周期非法的注册应被跳过");
    }

    [TestMethod]
    public void AMAR4004_InvalidDefaultLifetime_ReportsOnce_AtClassDefinition()
    {
        var (_, _, diagnostics) = GeneratorTestHelper.RunGenerator($$"""
            using Microsoft.Extensions.DependencyInjection;
            namespace Demo;
            [RegistrationOptions(DefaultLifetime = (ServiceLifetime)42)]
            public sealed class MyServiceAttribute : BaseServiceAttribute
            {
                public MyServiceAttribute() : base(ServiceLifetime.Singleton) { }
            }

            [MyService]
            public class ServiceA { }
            {{RegisterSnippet}}
            """);

        var found = diagnostics.Where(d => d.Id == "AMAR4004").ToList();
        Assert.HasCount(1, found, "默认生命周期非法只应在特性类定义处报一次");
    }

    [TestMethod]
    public void AMAR4005_MultipleRegistrationOptionsOnChain_ReportsWarning()
    {
        var (code, _, diagnostics) = GeneratorTestHelper.RunGenerator($$"""
            using Microsoft.Extensions.DependencyInjection;
            namespace Demo;
            [RegistrationOptions(DefaultLifetime = ServiceLifetime.Singleton)]
            public class BaseAttr : BaseServiceAttribute
            {
                public BaseAttr() : base(ServiceLifetime.Singleton) { }
            }

            [RegistrationOptions(DefaultLifetime = ServiceLifetime.Scoped)]
            public sealed class DerivedAttr : BaseAttr
            {
                public DerivedAttr() : base(ServiceLifetime.Scoped) { }
            }

            [DerivedAttr]
            public class ServiceA { }
            {{RegisterSnippet}}
            """);

        AssertHas(diagnostics, "AMAR4005", "DerivedAttr");
        Assert.AreEqual(DiagnosticSeverity.Warning, Find(diagnostics, "AMAR4005")!.Severity);
        // 只取最近的 [RegistrationOptions](Scoped) 生成注册。
        StringAssert.Contains(code, "services.AddScoped<global::Demo.ServiceA>();");
    }

    // ---- 辅助 ----

    private static Diagnostic? Find(ImmutableArray<Diagnostic> diagnostics, string id) =>
        diagnostics.FirstOrDefault(d => d.Id == id);

    private static void AssertHas(ImmutableArray<Diagnostic> diagnostics, string id, params string[] messageParts)
    {
        var diagnostic = Find(diagnostics, id);
        Assert.IsNotNull(diagnostic, $"应报告 {id}。实际诊断:\n" + string.Join("\n", diagnostics.Select(d => d.ToString())));
        var message = diagnostic!.GetMessage();
        foreach (var part in messageParts)
        {
            StringAssert.Contains(message, part, $"{id} 消息应包含 '{part}'。实际消息:{message}");
        }
    }

    private static void AssertNotHas(ImmutableArray<Diagnostic> diagnostics, string id)
    {
        Assert.IsNull(Find(diagnostics, id), $"不应报告 {id}");
    }
}
