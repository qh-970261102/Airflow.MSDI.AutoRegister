using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Airflow.MSDI.AutoRegister.Test;

/// <summary>临时实验:值类型作为工厂返回类型在不同 MSDI 版本下的表现。</summary>
[TestClass]
public sealed class ScratchValueTypeTests
{
    private static readonly string[] Tpa = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!)
        .Split(Path.PathSeparator);

    private static string CompileWith(string msdiAbstractionsPath, string source)
    {
        // 排除 TPA 里已有的 MSDI,避免同名程序集版本冲突
        var refs = Tpa
            .Where(p => !Path.GetFileNameWithoutExtension(p).StartsWith("Microsoft.Extensions.DependencyInjection", StringComparison.Ordinal))
            .Select(p => (MetadataReference)MetadataReference.CreateFromFile(p))
            .ToList();
        refs.Add(MetadataReference.CreateFromFile(msdiAbstractionsPath));

        var tree = CSharpSyntaxTree.ParseText(source);
        var compilation = CSharpCompilation.Create(
            "ScratchValueType",
            new[] { tree },
            refs,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        var errors = compilation.GetDiagnostics()
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .Select(d => d.ToString())
            .ToList();
        return errors.Count == 0 ? "COMPILES" : string.Join("\n", errors);
    }

    private const string Source = """
        using Microsoft.Extensions.DependencyInjection;
        namespace Demo
        {
            public class Owner
            {
                public int GetInt() => 42;
            }
            public static class Test
            {
                public static void Run(IServiceCollection services)
                {
                    services.AddSingleton(sp => 42);
                    services.AddSingleton(sp => sp.GetRequiredService<Owner>().GetInt());
                }
            }
        }
        """;

    [TestMethod]
    public void ValueTypeFactory_MSDI9()
    {
        var result = CompileWith(
            Path.Combine(Environment.CurrentDirectory, "..", "..", "..", "..", "..", ".nuget",
                "microsoft.extensions.dependencyinjection.abstractions", "9.0.0",
                "lib", "net8.0", "Microsoft.Extensions.DependencyInjection.Abstractions.dll"),
            Source);
        System.Console.WriteLine("=== MSDI 9.0.0 值类型工厂:\n" + result);
    }

    [TestMethod]
    public void ValueTypeFactory_MSDI1()
    {
        var result = CompileWith(
            Path.Combine(Environment.CurrentDirectory, "..", "..", "..", "..", "..", ".nuget",
                "microsoft.extensions.dependencyinjection.abstractions", "1.0.0",
                "lib", "netstandard1.0", "Microsoft.Extensions.DependencyInjection.Abstractions.dll"),
            Source);
        System.Console.WriteLine("=== MSDI 1.0.0 值类型工厂:\n" + result);
    }
}
