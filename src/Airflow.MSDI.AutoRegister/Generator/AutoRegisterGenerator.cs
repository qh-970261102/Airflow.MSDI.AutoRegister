using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace Airflow.MSDI.AutoRegister.Generator;

/// <summary>
/// 1.0 源生成器:
/// 为所有标注 [AutoRegister] 的 partial 方法生成实现,把当前程序集内带
/// BaseServiceAttribute 派生特性的类与工厂方法注册进 IServiceCollection。
/// </summary>
[Generator(LanguageNames.CSharp)]
public sealed class AutoRegisterGenerator : IIncrementalGenerator
{
    private const string BaseServiceAttributeMetadataName = "BaseServiceAttribute";
    private const string AutoRegisterAttributeMetadataName = "AutoRegisterAttribute";
    private const string RegistrationOptionsAttributeMetadataName = "RegistrationOptionsAttribute";
    private const string IServiceProviderMetadataName = "System.IServiceProvider";

    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        context.RegisterSourceOutput(context.CompilationProvider, Execute);
    }

    private static void Execute(SourceProductionContext spc, Compilation compilation)
    {
        var baseAttributeType = compilation.GetTypeByMetadataName(BaseServiceAttributeMetadataName);
        var autoRegisterAttributeType = compilation.GetTypeByMetadataName(AutoRegisterAttributeMetadataName);
        var optionsAttributeType = compilation.GetTypeByMetadataName(RegistrationOptionsAttributeMetadataName);
        var serviceProviderType = compilation.GetTypeByMetadataName(IServiceProviderMetadataName);

        if (baseAttributeType is null || autoRegisterAttributeType is null || optionsAttributeType is null)
            return;

        var analyzer = new AssemblyAnalyzer(compilation, baseAttributeType, autoRegisterAttributeType, optionsAttributeType, serviceProviderType);
        analyzer.Analyze();

        if (analyzer.AutoRegisterMethods.Count == 0)
            return;

        var body = analyzer.BuildRegistrationBody();
        if (string.IsNullOrEmpty(body))
            return;

        var index = 0;
        foreach (var method in analyzer.AutoRegisterMethods)
        {
            var code = Emitter.Emit(method, body);
            var hintName = $"AutoRegister.{method.ContainingType.Name}.{method.Name}.{index++}.g.cs";
            spc.AddSource(hintName, SourceText.From(code, Encoding.UTF8));
        }
    }

    /// <summary>解析结果模型:一条注册。</summary>
    private enum EntryKind
    {
        /// <summary>类自身注册:AddLifetime&lt;T&gt;()</summary>
        ClassSelf,
        /// <summary>类接口注册(类有自身注册):AddLifetime&lt;S&gt;(sp =&gt; sp.GetRequiredService&lt;T&gt;())</summary>
        ClassInterfaceViaSelf,
        /// <summary>类接口注册(类无自身注册):AddLifetime&lt;S, T&gt;()</summary>
        ClassInterfaceDirect,
        /// <summary>工厂方法拥有类的自动单例注册:AddSingleton&lt;Owner&gt;()</summary>
        FactoryOwner,
        /// <summary>工厂方法注册:AddLifetime&lt;R&gt;(sp =&gt; sp.GetRequiredService&lt;Owner&gt;().Method(...))</summary>
        FactoryMethod,
    }

    private sealed class Entry
    {
        public EntryKind Kind { get; set; }
        public double Order { get; set; }
        /// <summary>排序键:实现类型全名(工厂条目用拥有类全名,保证同组相邻)。</summary>
        public string SortName { get; set; } = null!;
        public int Seq { get; set; }

        /// <summary>生命周期方法名:AddSingleton / AddScoped / AddTransient。</summary>
        public string? LifetimeMethod { get; set; }
        /// <summary>类注册的实现类型(自身注册时也是服务类型)。</summary>
        public INamedTypeSymbol? ImplType { get; set; }
        /// <summary>类接口注册的服务类型。</summary>
        public INamedTypeSymbol? ServiceType { get; set; }
        /// <summary>工厂拥有类。</summary>
        public INamedTypeSymbol? FactoryOwner { get; set; }
        /// <summary>工厂方法。</summary>
        public IMethodSymbol? FactoryMethod { get; set; }
    }

    private sealed class AssemblyAnalyzer
    {
        private readonly Compilation _compilation;
        private readonly INamedTypeSymbol _baseAttributeType;
        private readonly INamedTypeSymbol _autoRegisterAttributeType;
        private readonly INamedTypeSymbol _optionsAttributeType;
        private readonly INamedTypeSymbol? _serviceProviderType;

        private readonly List<Entry> _entries = new();
        private readonly List<IMethodSymbol> _autoRegisterMethods = new();
        private int _seq;

        public AssemblyAnalyzer(
            Compilation compilation,
            INamedTypeSymbol baseAttributeType,
            INamedTypeSymbol autoRegisterAttributeType,
            INamedTypeSymbol optionsAttributeType,
            INamedTypeSymbol? serviceProviderType)
        {
            _compilation = compilation;
            _baseAttributeType = baseAttributeType;
            _autoRegisterAttributeType = autoRegisterAttributeType;
            _optionsAttributeType = optionsAttributeType;
            _serviceProviderType = serviceProviderType;
        }

        public List<IMethodSymbol> AutoRegisterMethods => _autoRegisterMethods;

        public void Analyze()
        {
            foreach (var type in AllTypes(_compilation.Assembly.GlobalNamespace))
            {
                AnalyzeType(type);
            }
        }

        private void AnalyzeType(INamedTypeSymbol type)
        {
            var classAttrs = type.GetAttributes().Where(a => IsServiceAttribute(a.AttributeClass)).ToList();
            if (classAttrs.Count > 0)
            {
                AnalyzeClassRegistrations(type, classAttrs);
            }

            // 先收集该类型下所有合法工厂方法,拥有类单例只注册一次。
            var factoryMethods = new List<(IMethodSymbol Method, List<AttributeData> Attrs)>();
            foreach (var method in type.GetMembers().OfType<IMethodSymbol>())
            {
                if (method.MethodKind != MethodKind.Ordinary)
                    continue;

                if (method.GetAttributes().Any(a => SymbolEqualityComparer.Default.Equals(a.AttributeClass, _autoRegisterAttributeType)))
                {
                    _autoRegisterMethods.Add(method);
                }

                var methodAttrs = method.GetAttributes().Where(a => IsServiceAttribute(a.AttributeClass)).ToList();
                if (methodAttrs.Count > 0 && TryAdaptFactoryMethod(method, out _))
                {
                    factoryMethods.Add((method, methodAttrs));
                }
            }

            if (factoryMethods.Count > 0)
            {
                RegisterFactoryOwner(type);
                foreach (var (method, attrs) in factoryMethods)
                {
                    AnalyzeFactoryRegistrations(method, attrs);
                }
            }
        }

        private void AnalyzeClassRegistrations(INamedTypeSymbol type, List<AttributeData> attrs)
        {
            var hasSelf = attrs.Any(a => GetServiceType(a) is null);

            foreach (var attr in attrs)
            {
                var (lifetimeMethod, order) = ParseLifetimeAndOrder(attr);
                if (lifetimeMethod is null)
                    continue;

                var serviceType = GetServiceType(attr);
                if (serviceType is null)
                {
                    // 自身注册
                    _entries.Add(new Entry
                    {
                        Kind = EntryKind.ClassSelf,
                        Order = order,
                        SortName = type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                        Seq = _seq++,
                        LifetimeMethod = lifetimeMethod,
                        ImplType = type,
                    });
                }
                else
                {
                    _entries.Add(new Entry
                    {
                        Kind = hasSelf ? EntryKind.ClassInterfaceViaSelf : EntryKind.ClassInterfaceDirect,
                        Order = order,
                        SortName = type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                        Seq = _seq++,
                        LifetimeMethod = lifetimeMethod,
                        ImplType = type,
                        ServiceType = serviceType,
                    });
                }
            }
        }

        private void AnalyzeFactoryRegistrations(IMethodSymbol method, List<AttributeData> attrs)
        {
            var owner = method.ContainingType;

            foreach (var attr in attrs)
            {
                var (lifetimeMethod, order) = ParseLifetimeAndOrder(attr);
                if (lifetimeMethod is null)
                    continue;

                _entries.Add(new Entry
                {
                    Kind = EntryKind.FactoryMethod,
                    Order = order,
                    SortName = owner.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                    Seq = _seq++,
                    LifetimeMethod = lifetimeMethod,
                    FactoryOwner = owner,
                    FactoryMethod = method,
                });
            }
        }

        /// <summary>拥有类自动注册为单例(若自身未通过特性注册)。</summary>
        private void RegisterFactoryOwner(INamedTypeSymbol owner)
        {
            var ownerHasAttributeRegistration = owner.GetAttributes().Any(a => IsServiceAttribute(a.AttributeClass));
            if (ownerHasAttributeRegistration)
                return;

            _entries.Add(new Entry
            {
                Kind = EntryKind.FactoryOwner,
                Order = 0,
                SortName = owner.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                Seq = _seq++,
                LifetimeMethod = "AddSingleton",
                ImplType = owner,
            });
        }

        /// <summary>校验工厂方法形态(1.0 静默跳过非法形态,1.1 起输出 Diagnostic)。</summary>
        private bool TryAdaptFactoryMethod(IMethodSymbol method, out string? callArgs)
        {
            callArgs = null;

            // 1.0 只支持普通类上的 public 实例方法(1.1 起对非法形态输出 Diagnostic)。
            var owner = method.ContainingType;
            if (owner.TypeKind != TypeKind.Class || owner.IsStatic || owner.IsAbstract || owner.IsGenericType)
                return false;
            if (method.IsStatic || method.IsGenericMethod || method.DeclaredAccessibility != Accessibility.Public)
                return false;
            if (method.ReturnsVoid || method.ReturnType is not INamedTypeSymbol returnType || returnType.IsGenericType)
                return false;

            // 参数适配:无参,或仅一个 IServiceProvider 参数。
            callArgs = AdaptCallArguments(method);
            return callArgs is not null;
        }

        /// <summary>返回方法调用参数文本;不满足"无参或单 IServiceProvider 参数"返回 null。</summary>
        private string? AdaptCallArguments(IMethodSymbol method)
        {
            switch (method.Parameters.Length)
            {
                case 0:
                    return "";
                case 1:
                    var p = method.Parameters[0];
                    if (_serviceProviderType is not null &&
                        SymbolEqualityComparer.Default.Equals(p.Type, _serviceProviderType))
                    {
                        return "sp";
                    }
                    return null;
                default:
                    return null;
            }
        }

        private bool IsServiceAttribute(INamedTypeSymbol? attrClass)
        {
            for (var c = attrClass; c is not null; c = c.BaseType)
            {
                if (SymbolEqualityComparer.Default.Equals(c, _baseAttributeType))
                    return true;
            }
            return false;
        }

        private INamedTypeSymbol? GetServiceType(AttributeData attr)
        {
            var options = ReadOptions(attr.AttributeClass);
            var serviceType = ReadConstructorArg(attr, options.ServiceTypeIndex, out var value)
                ? value as ITypeSymbol
                : options.DefaultServiceType;
            return serviceType as INamedTypeSymbol;
        }

        private (string? LifetimeMethod, double Order) ParseLifetimeAndOrder(AttributeData attr)
        {
            var options = ReadOptions(attr.AttributeClass);

            int lifetimeInt;
            if (ReadConstructorArg(attr, options.LifetimeIndex, out var lifetimeValue) && lifetimeValue is int i)
            {
                lifetimeInt = i;
            }
            else
            {
                lifetimeInt = (int)options.DefaultLifetime;
            }

            var lifetimeMethod = lifetimeInt switch
            {
                0 => "AddSingleton",
                1 => "AddScoped",
                2 => "AddTransient",
                _ => null,
            };

            double order = options.DefaultOrder;
            if (ReadConstructorArg(attr, options.OrderIndex, out var orderValue))
            {
                try
                {
                    order = Convert.ToDouble(orderValue);
                }
                catch
                {
                    order = options.DefaultOrder;
                }
            }

            return (lifetimeMethod, order);
        }

        private static bool ReadConstructorArg(AttributeData attr, int index, out object? value)
        {
            value = null;
            if (index < 0 || attr.ConstructorArguments.Length <= index)
                return false;
            var arg = attr.ConstructorArguments[index];
            if (arg.IsNull)
                return false;
            value = arg.Value;
            return true;
        }

        /// <summary>读取特性类上的 RegistrationOptionsAttribute(沿继承链)。</summary>
        private RegistrationOptionsData ReadOptions(INamedTypeSymbol? attrClass)
        {
            for (var c = attrClass; c is not null; c = c.BaseType)
            {
                foreach (var attr in c.GetAttributes())
                {
                    if (!SymbolEqualityComparer.Default.Equals(attr.AttributeClass, _optionsAttributeType))
                        continue;

                    var options = new RegistrationOptionsData();
                    foreach (var named in attr.NamedArguments)
                    {
                        switch (named.Key)
                        {
                            case nameof(RegistrationOptionsData.LifetimeIndex):
                                options.LifetimeIndex = (int)(named.Value.Value ?? -1);
                                break;
                            case nameof(RegistrationOptionsData.ServiceTypeIndex):
                                options.ServiceTypeIndex = (int)(named.Value.Value ?? -1);
                                break;
                            case nameof(RegistrationOptionsData.OrderIndex):
                                options.OrderIndex = (int)(named.Value.Value ?? -1);
                                break;
                            case nameof(RegistrationOptionsData.DefaultLifetime):
                                options.DefaultLifetime = named.Value.Value is int li ? li : 0; // 0 = Singleton
                                break;
                            case nameof(RegistrationOptionsData.DefaultServiceType):
                                options.DefaultServiceType = named.Value.Value as ITypeSymbol;
                                break;
                            case nameof(RegistrationOptionsData.DefaultOrder):
                                options.DefaultOrder = named.Value.Value is null ? 0 : Convert.ToDouble(named.Value.Value);
                                break;
                        }
                    }
                    return options;
                }
            }
            return new RegistrationOptionsData();
        }

        private IEnumerable<INamedTypeSymbol> AllTypes(INamespaceSymbol ns)
        {
            foreach (var member in ns.GetTypeMembers())
            {
                foreach (var nested in AllNested(member))
                    yield return nested;
            }
            foreach (var child in ns.GetNamespaceMembers())
            {
                foreach (var t in AllTypes(child))
                    yield return t;
            }
        }

        private static IEnumerable<INamedTypeSymbol> AllNested(INamedTypeSymbol type)
        {
            yield return type;
            foreach (var nested in type.GetTypeMembers())
            {
                foreach (var t in AllNested(nested))
                    yield return t;
            }
        }

        /// <summary>排序 + 渲染注册体。</summary>
        public string BuildRegistrationBody()
        {
            var sorted = _entries
                .OrderBy(e => e.Order)
                .ThenBy(e => e.SortName, StringComparer.Ordinal)
                .ThenBy(e => e.Seq)
                .ToList();

            var sb = new StringBuilder();
            foreach (var e in sorted)
            {
                switch (e.Kind)
                {
                    case EntryKind.ClassSelf:
                        sb.AppendLine($"            services.{e.LifetimeMethod}<{Fmt(e.ImplType!)}>();");
                        break;
                    case EntryKind.ClassInterfaceViaSelf:
                        sb.AppendLine($"            services.{e.LifetimeMethod}<{Fmt(e.ServiceType!)}>(sp => sp.GetRequiredService<{Fmt(e.ImplType!)}>());");
                        break;
                    case EntryKind.ClassInterfaceDirect:
                        sb.AppendLine($"            services.{e.LifetimeMethod}<{Fmt(e.ServiceType!)}, {Fmt(e.ImplType!)}>();");
                        break;
                    case EntryKind.FactoryOwner:
                        sb.AppendLine($"            services.AddSingleton<{Fmt(e.ImplType!)}>();");
                        break;
                    case EntryKind.FactoryMethod:
                        var owner = Fmt(e.FactoryOwner!);
                        var method = e.FactoryMethod!;
                        var args = method.Parameters.Length == 0 ? "" : "sp";
                        sb.AppendLine($"            services.{e.LifetimeMethod}<{Fmt(method.ReturnType)}>(sp => sp.GetRequiredService<{owner}>().{method.Name}({args}));");
                        break;
                }
            }
            return sb.ToString();
        }

        private static string Fmt(ITypeSymbol type) =>
            type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
    }

    private sealed class RegistrationOptionsData
    {
        public int LifetimeIndex { get; set; } = -1;
        public int ServiceTypeIndex { get; set; } = -1;
        public int OrderIndex { get; set; } = -1;
        public int DefaultLifetime { get; set; } = 0; // 0 = Singleton, 1 = Scoped, 2 = Transient
        public ITypeSymbol? DefaultServiceType { get; set; }
        public double DefaultOrder { get; set; }
    }

    /// <summary>把注册体渲染成 partial 类文件。</summary>
    private static class Emitter
    {
        public static string Emit(IMethodSymbol method, string body)
        {
            var declaringSyntax = method.DeclaringSyntaxReferences.FirstOrDefault()?.GetSyntax() as MethodDeclarationSyntax;
            var namespaceName = method.ContainingNamespace.IsGlobalNamespace ? null : method.ContainingNamespace.ToDisplayString();

            var sb = new StringBuilder();
            sb.AppendLine("// <auto-generated/>");
            sb.AppendLine("#nullable enable");
            sb.AppendLine("using Microsoft.Extensions.DependencyInjection;");
            sb.AppendLine();

            void OpenNs()
            {
                if (namespaceName is not null)
                {
                    sb.AppendLine($"namespace {namespaceName}");
                    sb.AppendLine("{");
                }
            }

            void CloseNs()
            {
                if (namespaceName is not null)
                {
                    sb.AppendLine("}");
                }
            }

            // 嵌套类型:从外到内生成 partial class。
            var containingChain = new List<INamedTypeSymbol>();
            for (var t = method.ContainingType; t is not null; t = t.ContainingType)
                containingChain.Add(t);
            containingChain.Reverse();

            OpenNs();
            foreach (var type in containingChain)
            {
                var modifiers = GetTypeModifiers(type);
                sb.AppendLine($"    {modifiers} partial class {type.Name}");
                sb.AppendLine("    {");
            }

            // 方法实现部分:必须保留与定义部分一致的修饰符(含 partial)与签名。
            var methodModifiers = declaringSyntax?.Modifiers.ToString() ?? "public static partial";
            var returnType = declaringSyntax?.ReturnType.ToString() ?? "void";
            var typeParams = declaringSyntax?.TypeParameterList?.ToString() ?? "";
            var paramList = declaringSyntax?.ParameterList.ToString() ?? "(IServiceCollection services)";

            sb.AppendLine($"        {methodModifiers} {returnType} {method.Name}{typeParams}{paramList}");
            sb.AppendLine("        {");
            sb.Append(body.TrimEnd());
            sb.AppendLine();
            sb.AppendLine("        }");

            for (var i = 0; i < containingChain.Count; i++)
                sb.AppendLine("    }");
            CloseNs();

            return sb.ToString();
        }

        private static string GetTypeModifiers(INamedTypeSymbol type)
        {
            var mods = new List<string>();
            switch (type.DeclaredAccessibility)
            {
                case Accessibility.Public: mods.Add("public"); break;
                case Accessibility.Internal: mods.Add("internal"); break;
                case Accessibility.Private: mods.Add("private"); break;
                case Accessibility.Protected: mods.Add("protected"); break;
                case Accessibility.ProtectedOrInternal: mods.Add("protected internal"); break;
                case Accessibility.ProtectedAndInternal: mods.Add("private protected"); break;
            }
            if (type.IsStatic) mods.Add("static");
            return string.Join(" ", mods);
        }
    }
}
