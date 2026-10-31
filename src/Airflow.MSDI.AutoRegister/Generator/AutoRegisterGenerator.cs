using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using Airflow.MSDI.AutoRegister.Diagnostics;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace Airflow.MSDI.AutoRegister.Generator;

/// <summary>
/// 2.0 源生成器:
/// - 为程序集内唯一合法的 [AutoRegister] partial 方法生成实现;
/// - 支持 keyed 注册、按 (实现, 生命周期) 共享的锚点、泛型 targettype、注册元数据与实现集合;
/// - 对非法形态输出 AMAR 系列 Diagnostic(Error 阻止编译,Warning 仅提示),报错的注册条目不再生成代码。
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

        var body = analyzer.BuildBody();
        if (string.IsNullOrEmpty(body))
            return;

        var code = Emitter.Emit(method, body);
        var hintName = $"AutoRegister.{method.ContainingType.Name}.{method.Name}.g.cs";
        spc.AddSource(hintName, SourceText.From(code, Encoding.UTF8));
    }

    /// <summary>一条注册(一个特性使用点对应一条)。</summary>
    private sealed class Registration
    {
        /// <summary>服务类型(targettype)。</summary>
        public INamedTypeSymbol ServiceType { get; set; } = null!;

        /// <summary>服务 key;null 表示非 keyed。</summary>
        public string? Key { get; set; }

        /// <summary>生命周期:0=Singleton, 1=Scoped, 2=Transient。</summary>
        public int Lifetime { get; set; }

        public double Order { get; set; }

        /// <summary>锚点注册使用的类型:类注册=实现类型,工厂注册=方法返回类型。</summary>
        public INamedTypeSymbol InstanceType { get; set; } = null!;

        /// <summary>分组与去重使用的生产者:类注册=实现类型,工厂注册=方法。</summary>
        public ISymbol Producer { get; set; } = null!;

        /// <summary>元数据里的 ImplementationType:类注册=实现类型,工厂注册=方法返回类型。</summary>
        public ITypeSymbol ImplDisplay { get; set; } = null!;

        public IMethodSymbol? FactoryMethod { get; set; }

        public Location? Location { get; set; }

        public int Seq { get; set; }

        /// <summary>命中 Error 诊断后被丢弃,不参与生成。</summary>
        public bool Dropped { get; set; }

        public RegistrationGroup Group { get; set; } = null!;
    }

    /// <summary>同一 (生产者, 生命周期) 的注册集合,锚点决策以组为单位。</summary>
    private sealed class RegistrationGroup
    {
        public RegistrationGroup(ISymbol producer, int lifetime)
        {
            Producer = producer;
            Lifetime = lifetime;
        }

        public ISymbol Producer { get; }

        public int Lifetime { get; }

        public List<Registration> Registrations { get; } = new();

        /// <summary>内部 object key 变量名(如 __key1)。每个组都使用锚点注册。</summary>
        public string AnchorKey { get; set; } = null!;

        public INamedTypeSymbol InstanceType => Registrations[0].InstanceType;
    }

    private sealed class AssemblyAnalyzer
    {
        private const string Indent = "            ";

        private readonly Compilation _compilation;
        private readonly CSharpCompilation? _csharpCompilation;
        private readonly INamedTypeSymbol _baseAttributeType;
        private readonly INamedTypeSymbol _autoRegisterAttributeType;
        private readonly INamedTypeSymbol _optionsAttributeType;
        private readonly INamedTypeSymbol? _serviceProviderType;
        private readonly INamedTypeSymbol? _serviceCollectionType;
        private readonly INamedTypeSymbol? _serviceLifetimeType;
        private readonly INamedTypeSymbol? _systemType;

        private readonly List<Registration> _registrations = new();
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
            _csharpCompilation = compilation as CSharpCompilation;
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

            // AMAR2005 / AMAR2009: 完全重复与命名 key 冲突。
            ValidateDuplicateRegistrations(spc);
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

            // AMAR2008: 实现类型必须可实例化。
            if (type.IsAbstract || type.IsStatic)
            {
                ReportOnce(spc, "2008:" + MsgName(type), DiagnosticDescriptors.AMAR2008, LocationOf(type), MsgName(type));
                return;
            }

            foreach (var attr in attrs)
            {
                if (!ValidateAttributeClass(spc, attr.AttributeClass))
                    continue;

                var location = AttrLocation(attr) ?? LocationOf(type) ?? Location.None;

                // 服务类型:泛型目标 > typeof(...) > 自身。
                if (!TryResolveServiceType(spc, attr, location, out var declaredType))
                    continue;

                var (lifetimeMethod, lifetime, order) = ParseLifetimeAndOrder(spc, attr);
                if (lifetimeMethod is null)
                    continue; // AMAR4004 已在 ParseLifetimeAndOrder 内报告

                if (TryReadNamedOrder(attr, out var namedOrder))
                    order = namedOrder;
                TryReadKey(attr, out var key);

                var serviceType = declaredType ?? type;

                // AMAR2007: 实现类型必须能隐式转换为服务类型。
                if (!HasImplicitConversion(type, serviceType))
                {
                    Report(spc, DiagnosticDescriptors.AMAR2007, location, MsgName(type), MsgName(serviceType));
                    continue;
                }

                _registrations.Add(new Registration
                {
                    ServiceType = serviceType,
                    Key = key,
                    Lifetime = lifetime,
                    Order = order,
                    InstanceType = type,
                    Producer = type,
                    ImplDisplay = type,
                    Location = location,
                    Seq = _seq++,
                });
            }
        }

        private void AnalyzeFactoryMethods(SourceProductionContext spc, INamedTypeSymbol owner, List<IMethodSymbol> methods)
        {
            var ownerHasServiceAttributes = owner.GetAttributes().Any(a => IsServiceAttribute(a.AttributeClass));

            foreach (var method in methods)
            {
                var shapeOk = TryAdaptFactoryMethod(spc, method, out var callArgs);
                if (!shapeOk)
                    continue;

                if (ownerHasServiceAttributes)
                {
                    // AMAR1002: 拥有工厂方法的类不能再添加注册特性(拥有类会被自动注册为单例)。
                    ReportOnce(spc, "1002:" + MsgName(owner), DiagnosticDescriptors.AMAR1002, LocationOf(owner), MsgName(owner));
                    continue;
                }

                var returnType = (INamedTypeSymbol)method.ReturnType;

                foreach (var attr in method.GetAttributes().Where(a => IsServiceAttribute(a.AttributeClass)))
                {
                    if (!ValidateAttributeClass(spc, attr.AttributeClass))
                        continue; // 特性类元数据损坏 → 跳过该注册

                    var location = AttrLocation(attr) ?? LocationOf(method) ?? Location.None;

                    if (!TryResolveServiceType(spc, attr, location, out var declaredType))
                        continue;

                    var (lifetimeMethod, lifetime, order) = ParseLifetimeAndOrder(spc, attr);
                    if (lifetimeMethod is null)
                        continue;

                    if (TryReadNamedOrder(attr, out var namedOrder))
                        order = namedOrder;
                    TryReadKey(attr, out var key);

                    var serviceType = declaredType ?? returnType;

                    // AMAR2007: 工厂返回值必须能隐式转换为服务类型。
                    if (!HasImplicitConversion(returnType, serviceType))
                    {
                        Report(spc, DiagnosticDescriptors.AMAR2007, location, MsgName(returnType), MsgName(serviceType));
                        continue;
                    }

                    // AMAR1010: 单例工厂不允许 IServiceProvider 参数。
                    if (lifetime == 0 && callArgs == "sp")
                    {
                        Report(spc, DiagnosticDescriptors.AMAR1010, location, method.Name);
                        continue;
                    }

                    _registrations.Add(new Registration
                    {
                        ServiceType = serviceType,
                        Key = key,
                        Lifetime = lifetime,
                        Order = order,
                        InstanceType = returnType,
                        Producer = method,
                        ImplDisplay = returnType,
                        FactoryMethod = method,
                        Location = location,
                        Seq = _seq++,
                    });
                }
            }
        }

        /// <summary>解析服务类型:泛型目标 > typeof(...) > 无(null 表示自身/返回类型)。</summary>
        private bool TryResolveServiceType(SourceProductionContext spc, AttributeData attr, Location location, out INamedTypeSymbol? serviceType)
        {
            serviceType = null;

            if (!TryGetGenericTarget(spc, attr, location, out var genericTarget))
                return false;

            var (rawServiceType, hasInvalidServiceType) = ReadServiceType(attr);
            if (hasInvalidServiceType || (rawServiceType is not null && rawServiceType is not INamedTypeSymbol))
            {
                Report(spc, DiagnosticDescriptors.AMAR2002, location, MsgName(rawServiceType ?? attr.AttributeClass!));
                return false;
            }

            var fromConstructor = rawServiceType as INamedTypeSymbol;

            // AMAR4007: 泛型目标与构造参数同时提供且不一致(仅自定义泛型特性可能触发)。
            if (genericTarget is not null && fromConstructor is not null &&
                !SymbolEqualityComparer.Default.Equals(genericTarget, fromConstructor))
            {
                Report(spc, DiagnosticDescriptors.AMAR4007, location,
                    MsgName(genericTarget), MsgName(fromConstructor), MsgName(attr.AttributeClass!));
                return false;
            }

            serviceType = genericTarget ?? fromConstructor;
            if (serviceType is not null && !ValidateServiceTypeSymbol(spc, location, serviceType))
                return false;

            return true;
        }

        /// <summary>读取泛型特性的 targettype;带 2 个及以上类型参数时报 AMAR4006。</summary>
        private bool TryGetGenericTarget(SourceProductionContext spc, AttributeData attr, Location location, out INamedTypeSymbol? target)
        {
            target = null;
            var attributeClass = attr.AttributeClass;
            if (attributeClass is null || !attributeClass.IsGenericType)
                return true;

            if (attributeClass.TypeArguments.Length != 1)
            {
                Report(spc, DiagnosticDescriptors.AMAR4006, location,
                    MsgName(attributeClass), attributeClass.TypeArguments.Length);
                return false;
            }

            if (attributeClass.TypeArguments[0] is INamedTypeSymbol namedTarget)
            {
                target = namedTarget;
                return true;
            }

            Report(spc, DiagnosticDescriptors.AMAR2002, location, MsgName(attributeClass.TypeArguments[0]));
            return false;
        }

        private bool ValidateServiceTypeSymbol(SourceProductionContext spc, Location location, INamedTypeSymbol serviceType)
        {
            // AMAR2002: 服务类型必须是接口/类;闭合泛型原样填入,开放/未绑定泛型不支持。
            if (serviceType.TypeKind != TypeKind.Class && serviceType.TypeKind != TypeKind.Interface)
            {
                Report(spc, DiagnosticDescriptors.AMAR2002, location, MsgName(serviceType));
                return false;
            }

            if (serviceType.IsUnboundGenericType || ContainsTypeParameter(serviceType))
            {
                Report(spc, DiagnosticDescriptors.AMAR2002, location, MsgName(serviceType));
                return false;
            }

            return true;
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

            // AMAR1007: 返回类型必须是可解析的引用类型(闭合泛型如 List<TypeA> 支持)。
            if (method.ReturnType is not INamedTypeSymbol returnType ||
                returnType.IsUnboundGenericType ||
                ContainsTypeParameter(returnType) ||
                returnType.IsValueType ||
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

        /// <summary>
        /// 类型(或其任意嵌套类型实参)中是否包含类型参数。
        /// 闭合泛型如 InterfaceA&lt;TypeA&gt; 返回 false(允许原样注册);
        /// 未绑定泛型 InterfaceA&lt;&gt; 与开放构造泛型返回 true(不支持)。
        /// </summary>
        private static bool ContainsTypeParameter(ITypeSymbol type) => type switch
        {
            ITypeParameterSymbol => true,
            IArrayTypeSymbol array => ContainsTypeParameter(array.ElementType),
            INamedTypeSymbol named => named.TypeArguments.Any(ContainsTypeParameter),
            _ => false,
        };

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

        /// <summary>读取 Key 命名参数;null 表示非 keyed。</summary>
        private static void TryReadKey(AttributeData attr, out string? key)
        {
            key = null;
            foreach (var named in attr.NamedArguments)
            {
                if (named.Key != "Key")
                    continue;
                if (named.Value.IsNull)
                    return;
                key = named.Value.Value as string;
                return;
            }
        }

        /// <summary>读取 Order 命名参数(优先于构造参数)。</summary>
        private static bool TryReadNamedOrder(AttributeData attr, out double order)
        {
            order = 0;
            foreach (var named in attr.NamedArguments)
            {
                if (named.Key != "Order" || named.Value.IsNull)
                    continue;
                if (named.Value.Value is double d)
                {
                    order = d;
                    return true;
                }
                try
                {
                    order = Convert.ToDouble(named.Value.Value, CultureInfo.InvariantCulture);
                    return true;
                }
                catch
                {
                    return false;
                }
            }
            return false;
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

        private bool HasImplicitConversion(ITypeSymbol from, ITypeSymbol to)
        {
            if (SymbolEqualityComparer.Default.Equals(from, to))
                return true;
            return _csharpCompilation is null || _csharpCompilation.ClassifyConversion(from, to).IsImplicit;
        }

        /// <summary>AMAR2005(完全重复)/ AMAR2009(同一 targettype 下命名 key 冲突)。</summary>
        private void ValidateDuplicateRegistrations(SourceProductionContext spc)
        {
            var groups = new Dictionary<(INamedTypeSymbol Service, string? Key), List<Registration>>(ServiceKeyComparer.Instance);
            foreach (var r in _registrations)
            {
                var k = (r.ServiceType, r.Key);
                if (!groups.TryGetValue(k, out var list))
                {
                    list = new List<Registration>();
                    groups[k] = list;
                }
                list.Add(r);
            }

            foreach (var kvp in groups)
            {
                var ordered = kvp.Value.OrderBy(r => r.Seq).ToList();
                if (ordered.Count < 2)
                    continue;

                var first = ordered[0];
                var reportedProducers = new HashSet<ISymbol>(SymbolEqualityComparer.Default) { first.Producer };

                foreach (var r in ordered.Skip(1))
                {
                    if (SymbolEqualityComparer.Default.Equals(r.Producer, first.Producer))
                    {
                        // 完全重复:仅告警,不跳过(MSDI 后注册胜出,与 1.x 行为一致)。
                        Report(spc, DiagnosticDescriptors.AMAR2005, r.Location,
                            MsgName(kvp.Key.Service), MsgName(r.ImplDisplay), KeyLiteral(r.Key));
                        continue;
                    }

                    // 无 key 的多实现:允许(1.x 的 Order 竞争)。
                    if (r.Key is null)
                        continue;

                    if (reportedProducers.Add(r.Producer))
                    {
                        Report(spc, DiagnosticDescriptors.AMAR2009, r.Location,
                            MsgName(kvp.Key.Service), KeyLiteral(r.Key), MsgName(first.ImplDisplay));
                    }
                    r.Dropped = true;
                }
            }
        }

        // ------------------------------------------------------------------
        // 生成
        // ------------------------------------------------------------------

        /// <summary>排序 + 渲染注册体(锚点 → 服务注册 → 元数据 → 实现集合)。</summary>
        public string BuildBody()
        {
            var registrations = _registrations.Where(r => !r.Dropped).ToList();
            if (registrations.Count == 0)
                return string.Empty;

            // 1) 按 (生产者, 生命周期) 分组
            var byProducer = new Dictionary<ISymbol, RegistrationGroup?[]>(SymbolEqualityComparer.Default);
            var groups = new List<RegistrationGroup>();
            foreach (var r in registrations)
            {
                if (!byProducer.TryGetValue(r.Producer, out var slots))
                {
                    slots = new RegistrationGroup?[3];
                    byProducer[r.Producer] = slots;
                }

                var group = slots[r.Lifetime];
                if (group is null)
                {
                    group = new RegistrationGroup(r.Producer, r.Lifetime);
                    slots[r.Lifetime] = group;
                    groups.Add(group);
                }

                group.Registrations.Add(r);
                r.Group = group;
            }

            // 2) 锚点:每个 (实现, 生命周期) 组一个内部 object key(单条注册也一视同仁)
            var anchoredGroups = groups
                .OrderBy(g => Fmt(g.InstanceType), StringComparer.Ordinal)
                .ThenBy(g => g.Lifetime)
                .ToList();
            for (var i = 0; i < anchoredGroups.Count; i++)
                anchoredGroups[i].AnchorKey = "__key" + (i + 1).ToString(CultureInfo.InvariantCulture);

            var sb = new StringBuilder();

            // 3) 锚点(每个实现、每个生命周期一个内部 object key)
            foreach (var group in anchoredGroups)
                AppendAnchor(sb, group);
            sb.AppendLine();

            // 6) 工厂拥有类(自动注册为非 keyed 单例,每类一次)
            var owners = registrations
                .Where(r => r.FactoryMethod is not null)
                .Select(r => r.FactoryMethod!.ContainingType)
                .Distinct<INamedTypeSymbol>(SymbolEqualityComparer.Default)
                .OrderBy(Fmt, StringComparer.Ordinal)
                .ToList();
            if (owners.Count > 0)
            {
                foreach (var owner in owners)
                    sb.AppendLine($"{Indent}services.AddSingleton<{Fmt(owner)}>();");
                sb.AppendLine();
            }

            // 4) 服务注册(全部是锚点别名,按 Order → 实现类型名 → key 排序)
            foreach (var r in registrations
                         .OrderBy(r => r.Order)
                         .ThenBy(r => Fmt(r.InstanceType), StringComparer.Ordinal)
                         .ThenBy(r => r.Key, StringComparer.Ordinal)
                         .ThenBy(r => r.Seq))
            {
                sb.AppendLine(EmitRegistration(r));
            }

            // 5) 元数据与实现集合(按服务类型分组)
            var serviceTypes = registrations
                .Select(r => r.ServiceType)
                .Distinct<INamedTypeSymbol>(SymbolEqualityComparer.Default)
                .OrderBy(Fmt, StringComparer.Ordinal)
                .ToList();

            foreach (var serviceType in serviceTypes)
            {
                var items = registrations
                    .Where(r => SymbolEqualityComparer.Default.Equals(r.ServiceType, serviceType))
                    .OrderBy(r => r.Order)
                    .ThenBy(r => Fmt(r.ImplDisplay), StringComparer.Ordinal)
                    .ThenBy(r => r.Key, StringComparer.Ordinal)
                    .ThenBy(r => r.Seq)
                    .ToList();

                sb.AppendLine();
                AppendMetadata(sb, serviceType, items);
                sb.AppendLine();
                AppendCollection(sb, serviceType, items);
            }

            return sb.ToString();
        }

        private void AppendAnchor(StringBuilder sb, RegistrationGroup group)
        {
            var lifetimeMethod = LifetimeMethod(group.Lifetime);
            var keyedMethod = KeyedMethod(lifetimeMethod);
            var instanceType = Fmt(group.InstanceType);
            var sample = group.Registrations[0];
            var suffix = sample.FactoryMethod is null ? string.Empty : " / 工厂方法";

            sb.AppendLine($"{Indent}// 锚点:{instanceType}({LifetimeName(group.Lifetime)}){suffix}");
            sb.AppendLine($"{Indent}object {group.AnchorKey} = new object();");
            if (sample.FactoryMethod is { } method)
            {
                var owner = Fmt(method.ContainingType);
                var args = method.Parameters.Length == 0 ? string.Empty : "sp";
                sb.AppendLine($"{Indent}services.{keyedMethod}<{instanceType}>({group.AnchorKey}, (sp, _) => sp.GetRequiredService<{owner}>().{method.Name}({args}));");
            }
            else
            {
                sb.AppendLine($"{Indent}services.{keyedMethod}<{instanceType}>({group.AnchorKey});");
            }
        }

        private static string EmitRegistration(Registration r)
        {
            var lifetimeMethod = LifetimeMethod(r.Lifetime);
            var keyedMethod = KeyedMethod(lifetimeMethod);
            var serviceType = Fmt(r.ServiceType);
            var resolve = $"sp.GetRequiredKeyedService<{Fmt(r.InstanceType)}>({r.Group.AnchorKey})";

            return r.Key is null
                ? $"{Indent}services.{lifetimeMethod}<{serviceType}>(sp => {resolve});"
                : $"{Indent}services.{keyedMethod}<{serviceType}>({KeyLiteral(r.Key)}, (sp, _) => {resolve});";
        }

        private static void AppendMetadata(StringBuilder sb, INamedTypeSymbol serviceType, List<Registration> items)
        {
            var service = Fmt(serviceType);
            sb.AppendLine($"{Indent}services.AddSingleton<global::System.Collections.Generic.IEnumerable<global::Airflow.MSDI.AutoRegister.RegisterItemInfo<{service}>>>(new global::Airflow.MSDI.AutoRegister.RegisterItemInfo<{service}>[]");
            sb.AppendLine($"{Indent}{{");
            foreach (var r in items)
            {
                sb.AppendLine($"{Indent}    new global::Airflow.MSDI.AutoRegister.RegisterItemInfo<{service}>({KeyLiteral(r.Key)}, typeof({Fmt(r.ImplDisplay)}), {LifetimeLiteral(r.Lifetime)}, {OrderLiteral(r.Order)}),");
            }
            sb.AppendLine($"{Indent}}});");
        }

        private static void AppendCollection(StringBuilder sb, INamedTypeSymbol serviceType, List<Registration> items)
        {
            var service = Fmt(serviceType);
            sb.AppendLine($"{Indent}services.AddTransient<global::System.Collections.Generic.IEnumerable<{service}>>(sp => new {service}[]");
            sb.AppendLine($"{Indent}{{");
            foreach (var r in items)
            {
                sb.AppendLine($"{Indent}    sp.GetRequiredKeyedService<{Fmt(r.InstanceType)}>({r.Group.AnchorKey}),");
            }
            sb.AppendLine($"{Indent}}});");
        }

        private static string LifetimeMethod(int lifetime) => lifetime switch
        {
            0 => "AddSingleton",
            1 => "AddScoped",
            _ => "AddTransient",
        };

        private static string KeyedMethod(string method) => method switch
        {
            "AddSingleton" => "AddKeyedSingleton",
            "AddScoped" => "AddKeyedScoped",
            "AddTransient" => "AddKeyedTransient",
            _ => method,
        };

        private static string LifetimeLiteral(int lifetime) => lifetime switch
        {
            0 => "global::Microsoft.Extensions.DependencyInjection.ServiceLifetime.Singleton",
            1 => "global::Microsoft.Extensions.DependencyInjection.ServiceLifetime.Scoped",
            _ => "global::Microsoft.Extensions.DependencyInjection.ServiceLifetime.Transient",
        };

        private static string KeyLiteral(string? key) =>
            key is null ? "null" : SymbolDisplay.FormatLiteral(key, quote: true);

        private static string OrderLiteral(double order) =>
            order.ToString("R", CultureInfo.InvariantCulture);

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
            _ => lifetime.ToString(CultureInfo.InvariantCulture),
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

    private sealed class ServiceKeyComparer : IEqualityComparer<(INamedTypeSymbol Service, string? Key)>
    {
        public static readonly ServiceKeyComparer Instance = new();

        public bool Equals((INamedTypeSymbol Service, string? Key) x, (INamedTypeSymbol Service, string? Key) y) =>
            SymbolEqualityComparer.Default.Equals(x.Service, y.Service) &&
            string.Equals(x.Key, y.Key, StringComparison.Ordinal);

        public int GetHashCode((INamedTypeSymbol Service, string? Key) obj) =>
            SymbolEqualityComparer.Default.GetHashCode(obj.Service) * 397 ^
            (obj.Key is null ? 0 : StringComparer.Ordinal.GetHashCode(obj.Key));
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
