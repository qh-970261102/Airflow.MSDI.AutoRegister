using Microsoft.CodeAnalysis;

namespace Airflow.MSDI.AutoRegister.Generator;

/// <summary>
/// 1.1 版本的全部 Diagnostic 描述符(AMAR 前缀 + 4 位编号)。
/// 每条给出触发条件、严重级别与中英双语消息;报错条目由生成器跳过,不产出对应代码。
/// </summary>
internal static class DiagnosticDescriptors
{
    private const string Category = "Airflow.MSDI.AutoRegister";

    // ---- 1. 工厂方法相关(核心) ----

    /// <summary>拥有工厂方法的类不是普通类(abstract/static/interface/struct/泛型类)。</summary>
    public static readonly DiagnosticDescriptor AMAR1001 = new(
        "AMAR1001",
        "工厂方法拥有类必须是普通类",
        "工厂方法 '{0}' 的拥有类 '{1}' 必须是可实例化的普通类,不能是 abstract/static/interface/struct 或泛型类。The owner class '{1}' of factory method '{0}' must be a plain instantiable class, not abstract/static/interface/struct or generic.",
        Category, DiagnosticSeverity.Error, isEnabledByDefault: true);

    /// <summary>拥有工厂方法的类自身又带有注册特性。</summary>
    public static readonly DiagnosticDescriptor AMAR1002 = new(
        "AMAR1002",
        "拥有工厂方法的类不能再添加注册特性",
        "拥有工厂方法的类 '{0}' 会被自动注册为单例,请移除类上的注册特性。The class '{0}' containing factory methods is auto-registered as a singleton; remove the registration attribute(s) on the class.",
        Category, DiagnosticSeverity.Error, isEnabledByDefault: true);

    /// <summary>工厂方法是 static。</summary>
    public static readonly DiagnosticDescriptor AMAR1003 = new(
        "AMAR1003",
        "工厂方法不能是 static",
        "工厂方法 '{0}' 不能是 static,必须是实例方法(需要通过拥有类单例解析)。The factory method '{0}' must be an instance method, not static.",
        Category, DiagnosticSeverity.Error, isEnabledByDefault: true);

    /// <summary>工厂方法不是 public。</summary>
    public static readonly DiagnosticDescriptor AMAR1004 = new(
        "AMAR1004",
        "工厂方法必须 public",
        "工厂方法 '{0}' 必须是 public(生成代码需要跨类访问)。The factory method '{0}' must be public so generated code can call it.",
        Category, DiagnosticSeverity.Error, isEnabledByDefault: true);

    /// <summary>工厂方法是泛型方法。</summary>
    public static readonly DiagnosticDescriptor AMAR1005 = new(
        "AMAR1005",
        "工厂方法不能是泛型方法",
        "工厂方法 '{0}' 不能是泛型方法,1.x 不支持泛型工厂方法。Generic factory methods are not supported in 1.x; '{0}' must not be generic.",
        Category, DiagnosticSeverity.Error, isEnabledByDefault: true);

    /// <summary>工厂方法返回 void。</summary>
    public static readonly DiagnosticDescriptor AMAR1006 = new(
        "AMAR1006",
        "工厂方法不能返回 void",
        "工厂方法 '{0}' 的返回类型不能是 void,返回值类型即服务类型。The factory method '{0}' must not return void; its return type is the service type.",
        Category, DiagnosticSeverity.Error, isEnabledByDefault: true);

    /// <summary>工厂方法返回类型是泛型/不可解析类型。</summary>
    public static readonly DiagnosticDescriptor AMAR1007 = new(
        "AMAR1007",
        "工厂方法返回类型必须是可解析的非泛型具名类型",
        "工厂方法 '{0}' 的返回类型必须是可解析的非泛型具名类型。The return type of factory method '{0}' must be a resolvable, non-generic named type.",
        Category, DiagnosticSeverity.Error, isEnabledByDefault: true);

    /// <summary>工厂方法参数不是"无参或仅一个 IServiceProvider"。</summary>
    public static readonly DiagnosticDescriptor AMAR1008 = new(
        "AMAR1008",
        "工厂方法参数个数不合法",
        "工厂方法 '{0}' 只支持无参或单个 IServiceProvider 参数,请删除多余参数。The factory method '{0}' supports only no parameters or a single IServiceProvider parameter.",
        Category, DiagnosticSeverity.Error, isEnabledByDefault: true);

    /// <summary>工厂方法只有一个参数但不是 IServiceProvider。</summary>
    public static readonly DiagnosticDescriptor AMAR1009 = new(
        "AMAR1009",
        "工厂方法单个参数必须是 IServiceProvider",
        "工厂方法 '{0}' 的单个参数必须是 System.IServiceProvider。The single parameter of factory method '{0}' must be System.IServiceProvider.",
        Category, DiagnosticSeverity.Error, isEnabledByDefault: true);

    /// <summary>[SingletonService] 工厂方法带 IServiceProvider 参数。</summary>
    public static readonly DiagnosticDescriptor AMAR1010 = new(
        "AMAR1010",
        "单例工厂方法不允许 IServiceProvider 参数",
        "单例工厂方法 '{0}' 只允许无参;依赖应来自拥有类构造注入,不需要 IServiceProvider。若确实需要解析 scoped 依赖,说明生命周期用错了。A singleton factory method '{0}' must have no parameters; dependencies should come from the constructor injection of its owning class.",
        Category, DiagnosticSeverity.Error, isEnabledByDefault: true);

    // ---- 2. 类注册相关 ----

    /// <summary>注册特性作用于不支持的目标。</summary>
    public static readonly DiagnosticDescriptor AMAR2001 = new(
        "AMAR2001",
        "注册特性作用于不支持的目标",
        "注册特性不能作用于成员 '{0}',只能用于类或普通方法(字段、属性、事件、构造函数、运算符等均不支持)。The registration attribute on member '{0}' is invalid; registration attributes are only valid on classes and ordinary methods.",
        Category, DiagnosticSeverity.Error, isEnabledByDefault: true);

    /// <summary>注册特性的服务类型参数不是接口/类。</summary>
    public static readonly DiagnosticDescriptor AMAR2002 = new(
        "AMAR2002",
        "服务类型必须是接口或类",
        "注册特性的服务类型 '{0}' 必须是接口或类,不能是泛型类型、数组、指针或值类型。The service type '{0}' of a registration attribute must be an interface or class, not a generic, array, pointer, or value type.",
        Category, DiagnosticSeverity.Error, isEnabledByDefault: true);

    /// <summary>类本身是泛型却带注册特性。</summary>
    public static readonly DiagnosticDescriptor AMAR2003 = new(
        "AMAR2003",
        "泛型类不支持注册",
        "泛型类 '{0}' 不支持注册,1.x 不支持开放泛型注册;请闭合泛型或使用手动注册。Generic class '{0}' is not supported in 1.x; close the generic arguments or register manually.",
        Category, DiagnosticSeverity.Error, isEnabledByDefault: true);

    /// <summary>同一 (ServiceType, ImplType) 重复注册。</summary>
    public static readonly DiagnosticDescriptor AMAR2005 = new(
        "AMAR2005",
        "重复注册",
        "服务类型 '{0}' 与实现类型 '{1}' 存在重复注册,后注册者胜出,请确认是否有意。Duplicate registration of service '{0}' with implementation '{1}'; the last registration wins, please confirm this is intended.",
        Category, DiagnosticSeverity.Warning, isEnabledByDefault: true);

    /// <summary>自身注册与接口注册生命周期不一致。</summary>
    public static readonly DiagnosticDescriptor AMAR2006 = new(
        "AMAR2006",
        "自身注册与接口注册生命周期不一致",
        "类 '{0}' 的自身注册(生命周期 {1})与接口注册(生命周期 {2})生命周期不一致,同实例规则下建议保持一致。The lifetimes of the self-registration ({1}) and interface registration ({2}) of '{0}' are inconsistent; consider making them consistent.",
        Category, DiagnosticSeverity.Warning, isEnabledByDefault: true);

    // ---- 3. AutoRegister 方法相关 ----

    /// <summary>[AutoRegister] 方法不是 partial 方法。</summary>
    public static readonly DiagnosticDescriptor AMAR3001 = new(
        "AMAR3001",
        "[AutoRegister] 方法必须声明为 partial",
        "标记 [AutoRegister] 的方法 '{0}' 必须声明为 partial,生成器才能补全实现。The [AutoRegister] method '{0}' must be declared partial so the generator can provide the implementation.",
        Category, DiagnosticSeverity.Error, isEnabledByDefault: true);

    /// <summary>[AutoRegister] 方法所在类不是 partial 类。</summary>
    public static readonly DiagnosticDescriptor AMAR3002 = new(
        "AMAR3002",
        "[AutoRegister] 方法所在类必须是 partial",
        "标记 [AutoRegister] 的方法 '{0}' 所在类 '{1}' 必须是 partial 类。The class '{1}' containing the [AutoRegister] method '{0}' must be partial.",
        Category, DiagnosticSeverity.Error, isEnabledByDefault: true);

    /// <summary>[AutoRegister] 方法已存在实现部分。</summary>
    public static readonly DiagnosticDescriptor AMAR3003 = new(
        "AMAR3003",
        "[AutoRegister] 方法不能有手写实现",
        "标记 [AutoRegister] 的方法 '{0}' 已有手写实现,请移除,由生成器生成。The [AutoRegister] method '{0}' already has an implementation; remove it and let the generator provide it.",
        Category, DiagnosticSeverity.Error, isEnabledByDefault: true);

    /// <summary>[AutoRegister] 方法不是 static。</summary>
    public static readonly DiagnosticDescriptor AMAR3004 = new(
        "AMAR3004",
        "[AutoRegister] 方法必须是 static",
        "标记 [AutoRegister] 的方法 '{0}' 必须是 static 方法。The [AutoRegister] method '{0}' must be static.",
        Category, DiagnosticSeverity.Error, isEnabledByDefault: true);

    /// <summary>[AutoRegister] 方法参数不是单个 IServiceCollection。</summary>
    public static readonly DiagnosticDescriptor AMAR3005 = new(
        "AMAR3005",
        "[AutoRegister] 方法签名必须是 (IServiceCollection services)",
        "标记 [AutoRegister] 的方法 '{0}' 的签名必须是 (IServiceCollection services)。The [AutoRegister] method '{0}' must have the signature (IServiceCollection services).",
        Category, DiagnosticSeverity.Error, isEnabledByDefault: true);

    /// <summary>程序集内没有任何带注册特性的类/方法。</summary>
    public static readonly DiagnosticDescriptor AMAR3006 = new(
        "AMAR3006",
        "程序集内没有可注册的服务",
        "程序集内没有任何带注册特性的类或方法,请检查特性是否拼写错误。No classes or methods with registration attributes were found in the assembly; check for typos in attribute names.",
        Category, DiagnosticSeverity.Warning, isEnabledByDefault: true);

    /// <summary>同一程序集不允许存在多个 [AutoRegister] 方法。</summary>
    public static readonly DiagnosticDescriptor AMAR3007 = new(
        "AMAR3007",
        "同一程序集只允许一个 [AutoRegister] 方法",
        "同一程序集内只允许一个 [AutoRegister] 方法,已存在 '{0}',请删除多余的方法 '{1}'。Only one [AutoRegister] method is allowed per assembly; '{0}' already exists, remove the extra method '{1}'.",
        Category, DiagnosticSeverity.Error, isEnabledByDefault: true);

    // ---- 4. 特性元数据相关 ----

    /// <summary>BaseServiceAttribute 子类缺少 [RegistrationOptions]。</summary>
    public static readonly DiagnosticDescriptor AMAR4001 = new(
        "AMAR4001",
        "缺少 [RegistrationOptions]",
        "BaseServiceAttribute 的子类 '{0}' 必须使用 [RegistrationOptions] 描述其元数据。The BaseServiceAttribute subclass '{0}' must be decorated with [RegistrationOptions].",
        Category, DiagnosticSeverity.Error, isEnabledByDefault: true);

    /// <summary>RegistrationOptions 的索引超出构造函数参数个数。</summary>
    public static readonly DiagnosticDescriptor AMAR4002 = new(
        "AMAR4002",
        "[RegistrationOptions] 索引超出构造函数参数个数",
        "[RegistrationOptions] 的 '{0}' 索引({1})超出特性 '{2}' 的构造函数参数个数。The RegistrationOptions '{0}' index ({1}) exceeds the number of constructor parameters of attribute '{2}'.",
        Category, DiagnosticSeverity.Error, isEnabledByDefault: true);

    /// <summary>RegistrationOptions 索引对应的构造参数类型不符。</summary>
    public static readonly DiagnosticDescriptor AMAR4003 = new(
        "AMAR4003",
        "[RegistrationOptions] 索引对应参数类型不符",
        "[RegistrationOptions] 的 '{0}' 索引指向的构造函数参数类型不符(期望 {1}),请检查特性 '{2}' 的元数据。The RegistrationOptions '{0}' index of attribute '{2}' points to a constructor parameter of the wrong type (expected {1}).",
        Category, DiagnosticSeverity.Error, isEnabledByDefault: true);

    /// <summary>生命周期枚举值非法。</summary>
    public static readonly DiagnosticDescriptor AMAR4004 = new(
        "AMAR4004",
        "生命周期枚举值非法",
        "注册特性 '{0}' 的生命周期值非法,只支持 Singleton/Scoped/Transient。The lifetime value of registration attribute '{0}' is invalid; only Singleton/Scoped/Transient are supported.",
        Category, DiagnosticSeverity.Error, isEnabledByDefault: true);

    /// <summary>继承链上存在多个 [RegistrationOptions]。</summary>
    public static readonly DiagnosticDescriptor AMAR4005 = new(
        "AMAR4005",
        "继承链上存在多个 [RegistrationOptions]",
        "特性 '{0}' 的继承链上存在多个 [RegistrationOptions],只取最近的,请移除冗余项。Multiple [RegistrationOptions] were found on the inheritance chain of '{0}'; only the nearest one is used, remove the redundant ones.",
        Category, DiagnosticSeverity.Warning, isEnabledByDefault: true);
}
