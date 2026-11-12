using System.Reflection;
using Microsoft.Extensions.DependencyInjection;

namespace Airflow.MSDI.AutoRegister.Test;

/// <summary>
/// 端到端验证 2.1 的两个组合点:AutoRegister Usings 提供 using static,
/// KeyTemplate 在运行时调用用户 helper 计算 key。
/// </summary>
[TestClass]
public sealed class KeyTemplateRuntimeTests
{
    [TestMethod]
    public void KeyTemplate_WithStaticHelper_ResolvesRuntimeKey()
    {
        const string source = """
            using System;
            using Microsoft.Extensions.DependencyInjection;

            namespace Demo.KeyTemplates
            {
                public static class ViewKey
                {
                    public static string GetViewRegisterKey(Type viewType)
                    {
                        var name = viewType.Name;
                        return name.EndsWith("View", StringComparison.Ordinal)
                            ? name.Substring(0, name.Length - "View".Length)
                            : name;
                    }
                }
            }

            namespace Demo
            {
                public interface IView { }

                [RegistrationOptions(
                    LifetimeIndex = -1,
                    ServiceTypeIndex = 0,
                    OrderIndex = 1,
                    DefaultLifetime = ServiceLifetime.Transient,
                    DefaultServiceType = typeof(IView),
                    DefaultOrder = 0,
                    KeyTemplate = "GetViewRegisterKey({Type})")]
                public sealed class ViewAttribute : BaseServiceAttribute
                {
                    public ViewAttribute(Type? serviceType = null, double order = 0)
                        : base(ServiceLifetime.Transient, serviceType, order) { }
                }

                [View]
                public class MainView : IView { }

                internal static partial class Register
                {
                    [AutoRegister(Usings = new[] { "static Demo.KeyTemplates.ViewKey" })]
                    public static partial void AddServices(IServiceCollection services);
                }
            }
            """;

        var (_, outputCompilation, diagnostics) = GeneratorTestHelper.RunGenerator(source);
        Assert.IsEmpty(diagnostics, "生成器不应报告诊断");

        var assembly = GeneratorTestHelper.LoadAssembly(outputCompilation);
        var registerType = assembly.GetType("Demo.Register", throwOnError: true)!;
        var method = registerType.GetMethod("AddServices", BindingFlags.Public | BindingFlags.Static)!;

        var services = new ServiceCollection();
        method.Invoke(null, new object[] { services });
        using var provider = services.BuildServiceProvider();

        var viewType = assembly.GetType("Demo.MainView", throwOnError: true)!;
        var viewServiceType = assembly.GetType("Demo.IView", throwOnError: true)!;
        var resolved = provider.GetRequiredKeyedService(viewServiceType, "Main");
        Assert.AreEqual(viewType, resolved.GetType());

        var metadata = provider.GetRequiredService(
            typeof(IEnumerable<>).MakeGenericType(typeof(RegisterItemInfo<>).MakeGenericType(viewServiceType)));
        var item = ((System.Collections.IEnumerable)metadata).Cast<object>().Single();
        Assert.AreEqual("Main", item.GetType().GetProperty("Key")!.GetValue(item));
    }
}
