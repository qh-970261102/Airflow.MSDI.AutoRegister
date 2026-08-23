using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace Airflow.MSDI.AutoRegister.Generator;

/// <summary>
/// 1.1 源生成器:
/// - 为程序集内唯一合法的 [AutoRegister] partial 方法生成实现;
/// - 对非法形态输出 AMAR 系列 Diagnostic(Error 阻止编译,Warning 仅提示);
/// - 报错的注册条目不再生成对应代码,避免在诊断之上叠加迷惑的编译器错误。
/// </summary>
[Generator(LanguageNames.CSharp)]
public sealed class AutoRegisterGenerator : IIncrementalGenerator
{
    private const string BaseServiceAttributeMetadataName = "BaseServiceAttribute";
    private const string AutoRegisterAttributeMetadataName = "AutoRegisterAttribute";
    private const string RegistrationOptionsAttributeMetadataName = "RegistrationOptionsAttribute";
    private const string IServiceProviderMetadataName = "System.IServiceProvider";
    private const string IServiceCollectionMetadataName = "Microsoft.Extensions.DependencyInjection.IServiceCollection";
    private const string ServiceLifetimeMetadataName = "Microsoft.Extensions.DependencyInjection.ServiceLifetime";
    private const string SystemTypeMetadataName = "System.Type";

    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        context.RegisterSourceOutput(context.CompilationProvider, Execute);
    }

    private static void Execute(SourceProductionContext spc, Compilation compilation)
    {
        var baseAttributeType = compilation.GetTypeByMetadataName(BaseServiceAttributeMetadataName);
        var autoRegisterAttributeType = compilation.GetTypeByMetadataName(AutoRegisterAttributeMetadataName);
        var optionsAttributeType = compilation.GetTypeByMetadataName(RegistrationOptionsAttributeMetadataName);

        if (baseAttributeType is null || autoRegisterAttributeType is null || optionsAttributeType is null)
            return;

        var analyzer = new AssemblyAnalyzer(
            compilation,
            baseAttributeType,
            autoRegisterAttributeType,
            optionsAttributeType,
            compilation.GetTypeByMetadataName(IServiceProviderMetadataName),
            compilation.GetTypeByMetadataName(IServiceCollectionMetadataName),
            compilation.GetTypeByMetadataName(ServiceLifetimeMetadataName),
            compilation.GetTypeByMetadataName(SystemTypeMetadataName));

        analyzer.Analyze(spc);

        // AMAR3006: 存在 [AutoRegister] 方法但程序集内没有任何带注册特性的类/方法。
        if (analyzer.PrimaryAutoRegisterMethod is not null && !analyzer.FoundAnyServiceAttribute)
        {
            spc.ReportDiagnostic(Diagnostic.Create(
                DiagnosticDescriptors.AMAR3006,
                analyzer.PrimaryAutoRegisterMethod.Locations.FirstOrDefault() ?? Location.None));
        }

        var method = analyzer.PrimaryAutoRegisterMethod;
        if (method is null || !analyzer.IsPrimaryAutoRegisterValid)
            return;

        var body = analyzer.BuildRegistrationBody();
        if (string.IsNullOrEmpty(body))
            return;

        var code = Emitter.Emit(method, body);
        var hintName = $"AutoRegister.{method.ContainingType.Name}.{method.Name}.g.cs";
        spc.AddSource(hintName, SourceText.From(code, Encoding.UTF8));
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
        /// <summary>生命周期值:0=Singleton, 1=Scoped, 2=Transient。</summary>
        public int Lifetime { get; set; } = -1;
        /// <summary>该注册对应的特性位置(用于重复注册等诊断)。</summary>
        public Location? Location { get; set; }
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
        private readonly INamedTypeSymbol? _serviceCollectionType;
        private readonly INamedTypeSymbol? _serviceLifetimeType;
        private readonly INamedTypeSymbol? _systemType;

        private readonly List<Entry> _entries = new();
        private readonly List<IMethodSymbol> _autoRegisterMethods = new();
        private readonly Dictionary<IMethodSymbol, bool> _autoRegisterValidity = new(SymbolEqualityComparer.Default);
        private readonly HashSet<INamedTypeSymbol> _validatedAttributeClasses = new(SymbolEqualityComparer.Default);
        private readonly HashSet<INamedTypeSymbol> _brokenAttributeClasses = new(SymbolEqualityComparer.Default);
        private readonly Dictionary<INamedTypeSymbol, RegistrationOptionsData?> _optionsByAttrClass = new(SymbolEqualityComparer.Default);
        private readonly HashSet<string> _reported = new(StringComparer.Ordinal);
        private int _seq;
        private bool _foundAnyServiceAttribute;

        public AssemblyAnalyzer(
            Compilation compilation,
            INamedTypeSymbol baseAttributeType,
            INamedTypeSymbol autoRegisterAttributeType,
            INamedTypeSymbol optionsAttributeType,
            INamedTypeSymbol? serviceProviderType,
            INamedTypeSymbol? serviceCollectionType,
            INamedTypeSymbol? serviceLifetimeType,
            INamedTypeSymbol? systemType)
        {
            _compilation = compilation;
            _baseAttributeType = baseAttributeType;
            _autoRegisterAttributeType = autoRegisterAttributeType;
            _optionsAttributeType = optionsAttributeType;
            _serviceProviderType = serviceProviderType;
            _serviceCollectionType = serviceCollectionType;
            _serviceLifetimeType = serviceLifetimeType;
            _systemType = systemType;
        }

        public IMethodSymbol? PrimaryAutoRegisterMethod => _autoRegisterMethods.Count > 0 ? _autoRegisterMethods[0] : null;

        public bool IsPrimaryAutoRegisterValid =>
            PrimaryAutoRegisterMethod is not null &&
            _autoRegisterValidity.TryGetValue(PrimaryAutoRegisterMethod, out var v) && v;

        public bool FoundAnyServiceAttribute => _foundAnyServiceAttribute;

        public void Analyze(SourceProductionContext spc)
        {
            foreach (var type in AllTypes(_compilation.Assembly.GlobalNamespace))
            {
                AnalyzeType(spc, type);
            }

            // AMAR3007: 同一程序集只允许一个 [AutoRegister] 方法。
            if (_autoRegisterMethods.Count > 1)
            {
                var first = _autoRegisterMethods[0];
                for (var i = 1; i < _autoRegisterMethods.Count; i++)
                {
                    Report(spc, DiagnosticDescriptors.AMAR3007,
                        LocationOf(_autoRegisterMethods[i]),
                        first.Name, _autoRegisterMethods[i].Name);
                }
            }

            // AMAR2005: 重复注册(跨类、跨方法)。
            ReportDuplicates(spc);
        }

        private void AnalyzeType(SourceProductionContext spc, INamedTypeSymbol type)
        {
            // AMAR2001: 注册特性作用于不支持的目标(字段、属性、事件、构造函数、运算符等)。
            foreach (var member in type.GetMembers())
            {
                if (member is INamedTypeSymbol)
                    continue; // 嵌套类型单独遍历
                if (member is IMethodSymbol { MethodKind: MethodKind.Ordinary })
                    continue; // 普通方法按工厂方法处理

                var attrs = member.GetAttributes().Where(a => IsServiceAttribute(a.AttributeClass)).ToList();
                if (attrs.Count == 0)
                    continue;

                _foundAnyServiceAttribute = true;
                foreach (var attr in attrs)
                {
                    Report(spc, DiagnosticDescriptors.AMAR2001, AttrLocation(attr) ?? LocationOf(member), member.Name);
                }
            }

            var classAttrs = type.GetAttributes().Where(a => IsServiceAttribute(a.AttributeClass)).ToList();
            if (classAttrs.Count > 0)
            {
                _foundAnyServiceAttribute = true;
                AnalyzeClassRegistrations(spc, type, classAttrs);
            }

            var factoryCandidates = new List<IMethodSymbol>();
            foreach (var member in type.GetMembers())
            {
                if (member is not IMethodSymbol method || method.MethodKind != MethodKind.Ordinary)
                    continue;

                if (method.GetAttributes().Any(a => SymbolEqualityComparer.Default.Equals(a.AttributeClass, _autoRegisterAttributeType)))
                {
                    _autoRegisterMethods.Add(method);
                    _autoRegisterValidity[method] = ValidateAutoRegisterMethod(spc, method);
                }

                var methodAttrs = method.GetAttributes().Where(a => IsServiceAttribute(a.AttributeClass)).ToList();
                if (methodAttrs.Count > 0)
                {
                    _foundAnyServiceAttribute = true;
                    factoryCandidates.Add(method);
                }
            }

            if (factoryCandidates.Count > 0)
            {
                AnalyzeFactoryMethods(spc, type, factoryCandidates);
            }
        }

        private void AnalyzeClassRegistrations(SourceProductionContext spc, INamedTypeSymbol type, List<AttributeData> attrs)
        {
            // AMAR2003: 泛型类不支持注册。
            if (type.IsGenericType)
            {
                Report(spc, DiagnosticDescriptors.AMAR2003, LocationOf(type), MsgName(type));
                return;
            }

            // 第一遍:过滤元数据损坏的特性类,并确定是否存在自身注册。
            var validAttrs = new List<AttributeData>();
            var hasSelf = false;
            foreach (var attr in attrs)
            {
                if (!ValidateAttributeClass(spc, attr.AttributeClass))
                    continue;
                validAttrs.Add(attr);
                var (rawServiceType, hasInvalidServiceType) = ReadServiceType(attr);
                if (!hasInvalidServiceType && rawServiceType is null)
                    hasSelf = true;
            }

            var classEntries = new List<Entry>();
            var selfLifetime = -1;

            foreach (var attr in validAttrs)
            {
                var attrLocation = AttrLocation(attr) ?? LocationOf(type);

                // AMAR2002: 服务类型必须是非泛型的接口/类。
                var (rawServiceType, hasInvalidServiceType) = ReadServiceType(attr);
                var serviceType = rawServiceType as INamedTypeSymbol;
                if (hasInvalidServiceType)
                {
                    Report(spc, DiagnosticDescriptors.AMAR2002, attrLocation, MsgName(rawServiceType ?? attr.AttributeClass!));
                    continue;
                }
                if (serviceType is not null &&
                    (serviceType.IsGenericType ||
                     (serviceType.TypeKind != TypeKind.Class && serviceType.TypeKind != TypeKind.Interface)))
                {
                    Report(spc, DiagnosticDescriptors.AMAR2002, attrLocation, MsgName(serviceType));
                    continue;
                }

                var (lifetimeMethod, lifetime, order) = ParseLifetimeAndOrder(spc, attr);
                if (lifetimeMethod is null)
                    continue; // AMAR4004 已在 ParseLifetimeAndOrder 内报告

                Entry entry;
                if (serviceType is null)
                {
                    entry = new Entry
                    {
                        Kind = EntryKind.ClassSelf,
                        Order = order,
                        SortName = Fmt(type),
                        Seq = _seq++,
                        Lifetime = lifetime,
                        LifetimeMethod = lifetimeMethod,
                        ImplType = type,
                        Location = attrLocation,
                    };
                    selfLifetime = lifetime;
                }
                else
                {
                    entry = new Entry
                    {
                        Kind = hasSelf ? EntryKind.ClassInterfaceViaSelf : EntryKind.ClassInterfaceDirect,
                        Order = order,
                        SortName = Fmt(type),
                        Seq = _seq++,
                        Lifetime = lifetime,
                        LifetimeMethod = lifetimeMethod,
                        ImplType = type,
                        ServiceType = serviceType,
                        Location = attrLocation,
                    };
                }
                _entries.Add(entry);
                classEntries.Add(entry);
            }

            // AMAR2006: 自身注册与接口注册生命周期不一致。
            if (hasSelf && selfLifetime >= 0)
            {
                foreach (var e in classEntries)
                {
                    if (e.Kind == EntryKind.ClassInterfaceViaSelf && e.Lifetime != selfLifetime)
                    {
                        ReportOnce(spc, "2006:" + MsgName(type), DiagnosticDescriptors.AMAR2006,
                            LocationOf(type), MsgName(type), LifetimeName(selfLifetime), LifetimeName(e.Lifetime));
                        break;
                    }
                }
            }
        }

        private void AnalyzeFactoryMethods(SourceProductionContext spc, INamedTypeSymbol owner, List<IMethodSymbol> methods)
        {
            var ownerHasServiceAttributes = owner.GetAttributes().Any(a => IsServiceAttribute(a.AttributeClass));
            var ownerAutoRegisterNeeded = false;

            foreach (var method in methods)
            {
                var shapeOk = TryAdaptFactoryMethod(spc, method, out var callArgs);
                if (!shapeOk)
                    continue;

                if (ownerHasServiceAttributes)
                {
                    // AMAR1002: 拥有工厂方法的类不能再添加注册特性(拥有类会被自动注册为单例)。
                    ReportOnce(spc, "1002:" + MsgName(owner), DiagnosticDescriptors.AMAR1002,
                        LocationOf(owner), MsgName(owner));
                    continue;
                }

                var added = false;
                foreach (var attr in method.GetAttributes().Where(a => IsServiceAttribute(a.AttributeClass)))
                {
                    if (!ValidateAttributeClass(spc, attr.AttributeClass))
                        continue; // 特性类元数据损坏 → 跳过该注册

                    var (lifetimeMethod, lifetime, order) = ParseLifetimeAndOrder(spc, attr);
                    if (lifetimeMethod is null)
                        continue; // AMAR4004 已在 ParseLifetimeAndOrder 内报告

                    // AMAR1010: 单例工厂不允许 IServiceProvider 参数。
                    if (lifetime == 0 && callArgs == "sp")
                    {
                        Report(spc, DiagnosticDescriptors.AMAR1010, AttrLocation(attr) ?? LocationOf(method), method.Name);
                        continue;
                    }

                    _entries.Add(new Entry
                    {
                        Kind = EntryKind.FactoryMethod,
                        Order = order,
                        SortName = Fmt(owner),
                        Seq = _seq++,
                        Lifetime = lifetime,
                        LifetimeMethod = lifetimeMethod,
                        FactoryOwner = owner,
                        FactoryMethod = method,
                        Location = AttrLocation(attr),
                    });
                    added = true;
                }

                if (added)
                    ownerAutoRegisterNeeded = true;
            }

            if (ownerAutoRegisterNeeded)
                RegisterFactoryOwner(owner);
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
                SortName = Fmt(owner),
                Seq = _seq++,
                Lifetime = 0,
                LifetimeMethod = "AddSingleton",
                ImplType = owner,
                Location = LocationOf(owner),
            });
        }

        /// <summary>校验工厂方法形态,输出所有适用诊断;返回形态是否合法。</summary>
        private bool TryAdaptFactoryMethod(SourceProductionContext spc, IMethodSymbol method, out string? callArgs)
        {
            callArgs = null;
            var owner = method.ContainingType;
            var ok = true;

            // AMAR1001: 拥有类必须是可实例化的普通类。
            if (owner.TypeKind != TypeKind.Class || owner.IsStatic || owner.IsAbstract || owner.IsGenericType)
            {
                Report(spc, DiagnosticDescriptors.AMAR1001, LocationOf(method), method.Name, MsgName(owner));
                ok = false;
            }

            // AMAR1003: 工厂方法必须是实例方法。
            if (method.IsStatic)
            {
                Report(spc, DiagnosticDescriptors.AMAR1003, LocationOf(method), method.Name);
                ok = false;
            }

            // AMAR1004: 工厂方法必须 public。
            if (method.DeclaredAccessibility != Accessibility.Public)
            {
                Report(spc, DiagnosticDescriptors.AMAR1004, LocationOf(method), method.Name);
                ok = false;
            }

            // AMAR1005: 不支持泛型工厂方法。
            if (method.IsGenericMethod)
            {
                Report(spc, DiagnosticDescriptors.AMAR1005, LocationOf(method), method.Name);
                ok = false;
            }

            // AMAR1006: 返回类型不能是 void。
            if (method.ReturnsVoid)
            {
                Report(spc, DiagnosticDescriptors.AMAR1006, LocationOf(method), method.Name);
                ok = false;
            }

            // AMAR1007: 返回类型必须是可解析的非泛型具名类型。
            if (method.ReturnType is not INamedTypeSymbol returnType ||
                returnType.IsGenericType ||
                returnType is IErrorTypeSymbol)
            {
                Report(spc, DiagnosticDescriptors.AMAR1007, LocationOf(method), method.Name);
                ok = false;
            }

            // 参数:无参,或仅一个 IServiceProvider。
            switch (method.Parameters.Length)
            {
                case 0:
                    callArgs = "";
                    break;
                case 1:
                    var p = method.Parameters[0];
                    if (_serviceProviderType is not null &&
                        SymbolEqualityComparer.Default.Equals(p.Type, _serviceProviderType))
                    {
                        callArgs = "sp";
                    }
                    else
                    {
                        // AMAR1009: 单参数但不是 IServiceProvider(优先于 1008,避免重复报告)。
                        Report(spc, DiagnosticDescriptors.AMAR1009, LocationOf(method), method.Name);
                        ok = false;
                    }
                    break;
                default:
                    // AMAR1008: 参数个数不合法(≥ 2)。
                    Report(spc, DiagnosticDescriptors.AMAR1008, LocationOf(method), method.Name);
                    ok = false;
                    break;
            }

            return ok;
        }

        /// <summary>校验 [AutoRegister] 方法形态,输出所有适用诊断;返回是否可生成实现。</summary>
        private bool ValidateAutoRegisterMethod(SourceProductionContext spc, IMethodSymbol method)
        {
            var valid = true;

            // AMAR3002: 所在类(含嵌套链)必须是 partial。
            foreach (var t in ContainingChain(method.ContainingType))
            {
                if (IsPartialType(t))
                    continue;
                Report(spc, DiagnosticDescriptors.AMAR3002, LocationOf(t), method.Name, t.Name);
                valid = false;
                break;
            }

            var hasBody = HasMethodBody(method);
            var isPartial = IsPartialMethod(method);

            // AMAR3001: 必须声明为 partial(定义部分或实现部分均可识别)。
            if (!isPartial)
            {
                Report(spc, DiagnosticDescriptors.AMAR3001, LocationOf(method), method.Name);
                valid = false;
            }

            // AMAR3003: 已有手写实现。
            if (hasBody)
            {
                Report(spc, DiagnosticDescriptors.AMAR3003, LocationOf(method), method.Name);
                valid = false;
            }

            // AMAR3004: 注册方法必须是静态方法。
            if (!method.IsStatic)
            {
                Report(spc, DiagnosticDescriptors.AMAR3004, LocationOf(method), method.Name);
                valid = false;
            }

            // AMAR3005: 签名必须是单个 IServiceCollection 参数。
            var paramOk = method.Parameters.Length == 1 &&
                          _serviceCollectionType is not null &&
                          SymbolEqualityComparer.Default.Equals(method.Parameters[0].Type, _serviceCollectionType);
            if (!paramOk)
            {
                Report(spc, DiagnosticDescriptors.AMAR3005, LocationOf(method), method.Name);
                valid = false;
            }

            return valid;
        }

        /// <summary>
        /// 校验特性类上的 RegistrationOptions 元数据(4001/4002/4003/4004/4005)。
        /// 元数据损坏的特性类被标记为 broken,其所有使用点的注册将被跳过。
        /// </summary>
        private bool ValidateAttributeClass(SourceProductionContext spc, INamedTypeSymbol? attrClass)
        {
            if (attrClass is null)
                return false;

            if (_validatedAttributeClasses.Contains(attrClass))
                return !_brokenAttributeClasses.Contains(attrClass);

            _validatedAttributeClasses.Add(attrClass);

            // 直接使用基类特性本身:按默认元数据处理(索引 -1,默认生命周期),不报 4001。
            if (SymbolEqualityComparer.Default.Equals(attrClass, _baseAttributeType))
            {
                _optionsByAttrClass[attrClass] = new RegistrationOptionsData();
                return true;
            }

            var classLocation = LocationOf(attrClass) ?? Location.None;

            // 沿继承链收集 [RegistrationOptions](Roslyn 的 GetAttributes 不返回继承特性,须手动遍历)。
            var optionsFound = new List<AttributeData>();
            for (var c = attrClass; c is not null && !SymbolEqualityComparer.Default.Equals(c, _baseAttributeType); c = c.BaseType)
            {
                foreach (var attr in c.GetAttributes())
                {
                    if (SymbolEqualityComparer.Default.Equals(attr.AttributeClass, _optionsAttributeType))
                        optionsFound.Add(attr);
                }
            }

            if (optionsFound.Count == 0)
            {
                // AMAR4001: 缺少 [RegistrationOptions]。
                Report(spc, DiagnosticDescriptors.AMAR4001, classLocation, MsgName(attrClass));
                _brokenAttributeClasses.Add(attrClass);
                return false;
            }

            if (optionsFound.Count > 1)
            {
                // AMAR4005: 继承链上存在多个 [RegistrationOptions],只取最近的。
                Report(spc, DiagnosticDescriptors.AMAR4005, classLocation, MsgName(attrClass));
            }

            var options = ParseOptions(optionsFound[0]); // 最近的
            _optionsByAttrClass[attrClass] = options;

            // AMAR4004: 默认生命周期值非法。
            if (options.DefaultLifetime is not (0 or 1 or 2))
            {
                Report(spc, DiagnosticDescriptors.AMAR4004, classLocation, MsgName(attrClass));
            }

            var ctors = attrClass.InstanceConstructors.ToList();
            CheckIndex(spc, attrClass, classLocation, "LifetimeIndex", options.LifetimeIndex,
                "ServiceLifetime 或 int", IsLifetimeParamType, ctors);
            CheckIndex(spc, attrClass, classLocation, "ServiceTypeIndex", options.ServiceTypeIndex,
                "System.Type", IsServiceTypeParamType, ctors);
            CheckIndex(spc, attrClass, classLocation, "OrderIndex", options.OrderIndex,
                "数值类型(如 double/int)", IsOrderParamType, ctors);

            return !_brokenAttributeClasses.Contains(attrClass);
        }

        private void CheckIndex(
            SourceProductionContext spc,
            INamedTypeSymbol attrClass,
            Location classLocation,
            string indexName,
            int index,
            string expectedTypeName,
            Func<ITypeSymbol, bool> isExpected,
            List<IMethodSymbol> ctors)
        {
            if (index < 0)
                return; // -1 表示"不在构造函数中",跳过

            var outOfRange = false;
            var typeMismatch = false;
            foreach (var ctor in ctors)
            {
                if (index >= ctor.Parameters.Length)
                {
                    outOfRange = true;
                    continue;
                }
                if (!isExpected(ctor.Parameters[index].Type))
                    typeMismatch = true;
            }

            if (outOfRange)
            {
                // AMAR4002: 索引超出构造函数参数个数。
                ReportOnce(spc, $"4002:{MsgName(attrClass)}:{indexName}", DiagnosticDescriptors.AMAR4002,
                    classLocation, indexName, index, MsgName(attrClass));
                _brokenAttributeClasses.Add(attrClass);
            }
            else if (typeMismatch)
            {
                // AMAR4003: 索引对应参数类型不符。
                ReportOnce(spc, $"4003:{MsgName(attrClass)}:{indexName}", DiagnosticDescriptors.AMAR4003,
                    classLocation, indexName, expectedTypeName, MsgName(attrClass));
                _brokenAttributeClasses.Add(attrClass);
            }
        }

        private bool IsLifetimeParamType(ITypeSymbol t) =>
            (_serviceLifetimeType is not null && SymbolEqualityComparer.Default.Equals(t, _serviceLifetimeType)) ||
            t.SpecialType == SpecialType.System_Int32;

        private bool IsServiceTypeParamType(ITypeSymbol t) =>
            _systemType is not null && SymbolEqualityComparer.Default.Equals(t, _systemType);

        private bool IsOrderParamType(ITypeSymbol t) =>
            t.SpecialType is SpecialType.System_Double or SpecialType.System_Single or SpecialType.System_Decimal
                or SpecialType.System_Int32 or SpecialType.System_Int64 or SpecialType.System_Int16
                or SpecialType.System_Byte or SpecialType.System_UInt32 or SpecialType.System_UInt64
                or SpecialType.System_UInt16 or SpecialType.System_SByte;

        private static RegistrationOptionsData ParseOptions(AttributeData attr)
        {
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

        /// <summary>读取特性类已解析的元数据(须先调用 ValidateAttributeClass)。</summary>
        private RegistrationOptionsData ReadOptions(INamedTypeSymbol attrClass)
            => _optionsByAttrClass.TryGetValue(attrClass, out var o) && o is not null
                ? o
                : new RegistrationOptionsData();

        /// <summary>读取服务类型参数;返回(原始类型, 是否有"传了值但不是可解析具名类型")。</summary>
        private (ITypeSymbol? RawServiceType, bool HasInvalidServiceType) ReadServiceType(AttributeData attr)
        {
            var options = ReadOptions(attr.AttributeClass!);
            if (ReadConstructorArg(attr, options.ServiceTypeIndex, out var value))
            {
                return value is ITypeSymbol ts
                    ? (ts, ts is not INamedTypeSymbol)
                    : (null, true);
            }
            return (options.DefaultServiceType, false);
        }

        /// <summary>解析生命周期与排序;非法生命周期在内部报告 AMAR4004 并返回 null 方法名。</summary>
        private (string? LifetimeMethod, int Lifetime, double Order) ParseLifetimeAndOrder(SourceProductionContext spc, AttributeData attr)
        {
            var options = ReadOptions(attr.AttributeClass!);

            int lifetimeInt;
            var argPresent = ReadConstructorArg(attr, options.LifetimeIndex, out var lifetimeValue);
            if (argPresent && lifetimeValue is int i)
            {
                lifetimeInt = i;
            }
            else if (argPresent)
            {
                // 参数存在但不是整数:元数据损坏的防御路径(通常已被 4003 拦截)。
                Report(spc, DiagnosticDescriptors.AMAR4004, AttrLocation(attr) ?? Location.None, MsgName(attr.AttributeClass!));
                return (null, -1, options.DefaultOrder);
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

            // AMAR4004: 使用点传入非法生命周期值。
            if (lifetimeMethod is null && argPresent)
            {
                Report(spc, DiagnosticDescriptors.AMAR4004, AttrLocation(attr) ?? Location.None, MsgName(attr.AttributeClass!));
            }

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

            return (lifetimeMethod, lifetimeInt, order);
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

        private bool IsServiceAttribute(INamedTypeSymbol? attrClass)
        {
            for (var c = attrClass; c is not null; c = c.BaseType)
            {
                if (SymbolEqualityComparer.Default.Equals(c, _baseAttributeType))
                    return true;
            }
            return false;
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

        private void ReportDuplicates(SourceProductionContext spc)
        {
            var groups = new Dictionary<(ITypeSymbol Service, ITypeSymbol Impl), List<Entry>>(TypePairComparer.Instance);
            foreach (var e in _entries)
            {
                switch (e.Kind)
                {
                    case EntryKind.ClassSelf:
                    case EntryKind.FactoryOwner:
                        AddToGroup(groups, e, e.ImplType!, e.ImplType!);
                        break;
                    case EntryKind.ClassInterfaceViaSelf:
                    case EntryKind.ClassInterfaceDirect:
                        AddToGroup(groups, e, e.ServiceType!, e.ImplType!);
                        break;
                    case EntryKind.FactoryMethod:
                        AddToGroup(groups, e, e.FactoryMethod!.ReturnType, e.FactoryOwner!);
                        break;
                }
            }

            foreach (var kvp in groups)
            {
                if (kvp.Value.Count < 2)
                    continue;
                foreach (var e in kvp.Value.Skip(1))
                {
                    // AMAR2005: 重复注册,后注册者胜出。
                    Report(spc, DiagnosticDescriptors.AMAR2005, e.Location, MsgName(kvp.Key.Service), MsgName(kvp.Key.Impl));
                }
            }
        }

        private static void AddToGroup(
            Dictionary<(ITypeSymbol Service, ITypeSymbol Impl), List<Entry>> groups,
            Entry entry,
            ITypeSymbol service,
            ITypeSymbol impl)
        {
            var key = (service, impl);
            if (!groups.TryGetValue(key, out var list))
            {
                list = new List<Entry>();
                groups[key] = list;
            }
            list.Add(entry);
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

        private static IEnumerable<INamedTypeSymbol> ContainingChain(INamedTypeSymbol type)
        {
            var chain = new List<INamedTypeSymbol>();
            for (var t = type; t is not null; t = t.ContainingType)
                chain.Add(t);
            chain.Reverse();
            return chain;
        }

        private static bool IsPartialType(INamedTypeSymbol type) =>
            type.DeclaringSyntaxReferences
                .Select(r => r.GetSyntax())
                .OfType<TypeDeclarationSyntax>()
                .Any(s => s.Modifiers.Any(m => m.IsKind(SyntaxKind.PartialKeyword)));

        private static bool IsPartialMethod(IMethodSymbol method) =>
            method.DeclaringSyntaxReferences
                .Select(r => r.GetSyntax())
                .OfType<MethodDeclarationSyntax>()
                .Any(m => m.Modifiers.Any(mod => mod.IsKind(SyntaxKind.PartialKeyword)));

        private static bool HasMethodBody(IMethodSymbol method) =>
            method.DeclaringSyntaxReferences
                .Select(r => r.GetSyntax())
                .OfType<MethodDeclarationSyntax>()
                .Any(m => m.Body is not null || m.ExpressionBody is not null);

        private static string Fmt(ITypeSymbol type) =>
            type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);

        private static string MsgName(ITypeSymbol type) => type.ToDisplayString();

        private static string LifetimeName(int lifetime) => lifetime switch
        {
            0 => "Singleton",
            1 => "Scoped",
            2 => "Transient",
            _ => lifetime.ToString(),
        };

        private static Location? LocationOf(ISymbol symbol) => symbol.Locations.FirstOrDefault();

        private static Location? AttrLocation(AttributeData attr) =>
            attr.ApplicationSyntaxReference?.GetSyntax().GetLocation();

        private void Report(SourceProductionContext spc, DiagnosticDescriptor descriptor, Location? location, params object[] args)
            => spc.ReportDiagnostic(Diagnostic.Create(descriptor, location ?? Location.None, args));

        private void ReportOnce(SourceProductionContext spc, string key, DiagnosticDescriptor descriptor, Location? location, params object[] args)
        {
            if (!_reported.Add(key))
                return;
            Report(spc, descriptor, location, args);
        }
    }

    private sealed class TypePairComparer : IEqualityComparer<(ITypeSymbol Service, ITypeSymbol Impl)>
    {
        public static readonly TypePairComparer Instance = new();

        public bool Equals((ITypeSymbol Service, ITypeSymbol Impl) x, (ITypeSymbol Service, ITypeSymbol Impl) y) =>
            SymbolEqualityComparer.Default.Equals(x.Service, y.Service) &&
            SymbolEqualityComparer.Default.Equals(x.Impl, y.Impl);

        public int GetHashCode((ITypeSymbol Service, ITypeSymbol Impl) obj) =>
            SymbolEqualityComparer.Default.GetHashCode(obj.Service) * 397 ^
            SymbolEqualityComparer.Default.GetHashCode(obj.Impl);
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
