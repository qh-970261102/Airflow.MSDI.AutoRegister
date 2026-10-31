using Microsoft.CodeAnalysis;

namespace Airflow.MSDI.AutoRegister.Diagnostics
{
    /// <summary>
    /// All AMAR-prefixed diagnostic descriptors (4-digit ids).
    /// Titles/messages are localized via .resx (English default / zh-Hans) and resolved
    /// through <see cref="LocalizedStrings"/>; error entries are skipped by the generator.
    /// </summary>
    internal static class DiagnosticDescriptors
    {
        private const string Category = "Airflow.MSDI.AutoRegister";

        // ---- 1. 工厂方法相关(核心) ----

        /// <summary>拥有工厂方法的类不是普通类(abstract/static/interface/struct/泛型类)。</summary>
        public static readonly DiagnosticDescriptor AMAR1001 = new(
            "AMAR1001",
            LocalizedStrings.Get("AMAR1001_Title"),
            LocalizedStrings.Get("AMAR1001_Message"),
            Category, DiagnosticSeverity.Error, isEnabledByDefault: true);

        /// <summary>拥有工厂方法的类自身又带有注册特性。</summary>
        public static readonly DiagnosticDescriptor AMAR1002 = new(
            "AMAR1002",
            LocalizedStrings.Get("AMAR1002_Title"),
            LocalizedStrings.Get("AMAR1002_Message"),
            Category, DiagnosticSeverity.Error, isEnabledByDefault: true);

        /// <summary>工厂方法是 static。</summary>
        public static readonly DiagnosticDescriptor AMAR1003 = new(
            "AMAR1003",
            LocalizedStrings.Get("AMAR1003_Title"),
            LocalizedStrings.Get("AMAR1003_Message"),
            Category, DiagnosticSeverity.Error, isEnabledByDefault: true);

        /// <summary>工厂方法不是 public。</summary>
        public static readonly DiagnosticDescriptor AMAR1004 = new(
            "AMAR1004",
            LocalizedStrings.Get("AMAR1004_Title"),
            LocalizedStrings.Get("AMAR1004_Message"),
            Category, DiagnosticSeverity.Error, isEnabledByDefault: true);

        /// <summary>工厂方法是泛型方法。</summary>
        public static readonly DiagnosticDescriptor AMAR1005 = new(
            "AMAR1005",
            LocalizedStrings.Get("AMAR1005_Title"),
            LocalizedStrings.Get("AMAR1005_Message"),
            Category, DiagnosticSeverity.Error, isEnabledByDefault: true);

        /// <summary>工厂方法返回 void。</summary>
        public static readonly DiagnosticDescriptor AMAR1006 = new(
            "AMAR1006",
            LocalizedStrings.Get("AMAR1006_Title"),
            LocalizedStrings.Get("AMAR1006_Message"),
            Category, DiagnosticSeverity.Error, isEnabledByDefault: true);

        /// <summary>工厂方法返回类型不可解析(开放泛型/值类型/数组/指针等)。</summary>
        public static readonly DiagnosticDescriptor AMAR1007 = new(
            "AMAR1007",
            LocalizedStrings.Get("AMAR1007_Title"),
            LocalizedStrings.Get("AMAR1007_Message"),
            Category, DiagnosticSeverity.Error, isEnabledByDefault: true);

        /// <summary>工厂方法参数不是"无参或仅一个 IServiceProvider"。</summary>
        public static readonly DiagnosticDescriptor AMAR1008 = new(
            "AMAR1008",
            LocalizedStrings.Get("AMAR1008_Title"),
            LocalizedStrings.Get("AMAR1008_Message"),
            Category, DiagnosticSeverity.Error, isEnabledByDefault: true);

        /// <summary>工厂方法只有一个参数但不是 IServiceProvider。</summary>
        public static readonly DiagnosticDescriptor AMAR1009 = new(
            "AMAR1009",
            LocalizedStrings.Get("AMAR1009_Title"),
            LocalizedStrings.Get("AMAR1009_Message"),
            Category, DiagnosticSeverity.Error, isEnabledByDefault: true);

        /// <summary>[SingletonService] 工厂方法带 IServiceProvider 参数。</summary>
        public static readonly DiagnosticDescriptor AMAR1010 = new(
            "AMAR1010",
            LocalizedStrings.Get("AMAR1010_Title"),
            LocalizedStrings.Get("AMAR1010_Message"),
            Category, DiagnosticSeverity.Error, isEnabledByDefault: true);

        // ---- 2. 类注册相关 ----

        /// <summary>注册特性作用于不支持的目标。</summary>
        public static readonly DiagnosticDescriptor AMAR2001 = new(
            "AMAR2001",
            LocalizedStrings.Get("AMAR2001_Title"),
            LocalizedStrings.Get("AMAR2001_Message"),
            Category, DiagnosticSeverity.Error, isEnabledByDefault: true);

        /// <summary>注册特性的服务类型参数不是接口/类(或为开放/未绑定泛型/值类型)。</summary>
        public static readonly DiagnosticDescriptor AMAR2002 = new(
            "AMAR2002",
            LocalizedStrings.Get("AMAR2002_Title"),
            LocalizedStrings.Get("AMAR2002_Message"),
            Category, DiagnosticSeverity.Error, isEnabledByDefault: true);

        /// <summary>类本身是泛型却带注册特性。</summary>
        public static readonly DiagnosticDescriptor AMAR2003 = new(
            "AMAR2003",
            LocalizedStrings.Get("AMAR2003_Title"),
            LocalizedStrings.Get("AMAR2003_Message"),
            Category, DiagnosticSeverity.Error, isEnabledByDefault: true);

        /// <summary>完全重复的注册(同一 targettype + key + 实现)。</summary>
        public static readonly DiagnosticDescriptor AMAR2005 = new(
            "AMAR2005",
            LocalizedStrings.Get("AMAR2005_Title"),
            LocalizedStrings.Get("AMAR2005_Message"),
            Category, DiagnosticSeverity.Warning, isEnabledByDefault: true);

        /// <summary>实现类型与服务类型不兼容(无法隐式转换)。</summary>
        public static readonly DiagnosticDescriptor AMAR2007 = new(
            "AMAR2007",
            LocalizedStrings.Get("AMAR2007_Title"),
            LocalizedStrings.Get("AMAR2007_Message"),
            Category, DiagnosticSeverity.Error, isEnabledByDefault: true);

        /// <summary>实现类型不可实例化(abstract / static)。</summary>
        public static readonly DiagnosticDescriptor AMAR2008 = new(
            "AMAR2008",
            LocalizedStrings.Get("AMAR2008_Title"),
            LocalizedStrings.Get("AMAR2008_Message"),
            Category, DiagnosticSeverity.Error, isEnabledByDefault: true);

        /// <summary>同一 targettype 下命名 key 重复(不同实现)。</summary>
        public static readonly DiagnosticDescriptor AMAR2009 = new(
            "AMAR2009",
            LocalizedStrings.Get("AMAR2009_Title"),
            LocalizedStrings.Get("AMAR2009_Message"),
            Category, DiagnosticSeverity.Error, isEnabledByDefault: true);

        // ---- 3. AutoRegister 方法相关 ----

        /// <summary>[AutoRegister] 方法不是 partial 方法。</summary>
        public static readonly DiagnosticDescriptor AMAR3001 = new(
            "AMAR3001",
            LocalizedStrings.Get("AMAR3001_Title"),
            LocalizedStrings.Get("AMAR3001_Message"),
            Category, DiagnosticSeverity.Error, isEnabledByDefault: true);

        /// <summary>[AutoRegister] 方法所在类不是 partial 类。</summary>
        public static readonly DiagnosticDescriptor AMAR3002 = new(
            "AMAR3002",
            LocalizedStrings.Get("AMAR3002_Title"),
            LocalizedStrings.Get("AMAR3002_Message"),
            Category, DiagnosticSeverity.Error, isEnabledByDefault: true);

        /// <summary>[AutoRegister] 方法已存在实现部分。</summary>
        public static readonly DiagnosticDescriptor AMAR3003 = new(
            "AMAR3003",
            LocalizedStrings.Get("AMAR3003_Title"),
            LocalizedStrings.Get("AMAR3003_Message"),
            Category, DiagnosticSeverity.Error, isEnabledByDefault: true);

        /// <summary>[AutoRegister] 方法不是 static。</summary>
        public static readonly DiagnosticDescriptor AMAR3004 = new(
            "AMAR3004",
            LocalizedStrings.Get("AMAR3004_Title"),
            LocalizedStrings.Get("AMAR3004_Message"),
            Category, DiagnosticSeverity.Error, isEnabledByDefault: true);

        /// <summary>[AutoRegister] 方法参数不是单个 IServiceCollection。</summary>
        public static readonly DiagnosticDescriptor AMAR3005 = new(
            "AMAR3005",
            LocalizedStrings.Get("AMAR3005_Title"),
            LocalizedStrings.Get("AMAR3005_Message"),
            Category, DiagnosticSeverity.Error, isEnabledByDefault: true);

        /// <summary>程序集内没有任何带注册特性的类/方法。</summary>
        public static readonly DiagnosticDescriptor AMAR3006 = new(
            "AMAR3006",
            LocalizedStrings.Get("AMAR3006_Title"),
            LocalizedStrings.Get("AMAR3006_Message"),
            Category, DiagnosticSeverity.Warning, isEnabledByDefault: true);

        /// <summary>同一程序集不允许存在多个 [AutoRegister] 方法。</summary>
        public static readonly DiagnosticDescriptor AMAR3007 = new(
            "AMAR3007",
            LocalizedStrings.Get("AMAR3007_Title"),
            LocalizedStrings.Get("AMAR3007_Message"),
            Category, DiagnosticSeverity.Error, isEnabledByDefault: true);

        // ---- 4. 特性元数据相关 ----

        /// <summary>BaseServiceAttribute 子类缺少 [RegistrationOptions]。</summary>
        public static readonly DiagnosticDescriptor AMAR4001 = new(
            "AMAR4001",
            LocalizedStrings.Get("AMAR4001_Title"),
            LocalizedStrings.Get("AMAR4001_Message"),
            Category, DiagnosticSeverity.Error, isEnabledByDefault: true);

        /// <summary>RegistrationOptions 的索引超出构造函数参数个数。</summary>
        public static readonly DiagnosticDescriptor AMAR4002 = new(
            "AMAR4002",
            LocalizedStrings.Get("AMAR4002_Title"),
            LocalizedStrings.Get("AMAR4002_Message"),
            Category, DiagnosticSeverity.Error, isEnabledByDefault: true);

        /// <summary>RegistrationOptions 索引对应的构造参数类型不符。</summary>
        public static readonly DiagnosticDescriptor AMAR4003 = new(
            "AMAR4003",
            LocalizedStrings.Get("AMAR4003_Title"),
            LocalizedStrings.Get("AMAR4003_Message"),
            Category, DiagnosticSeverity.Error, isEnabledByDefault: true);

        /// <summary>生命周期枚举值非法。</summary>
        public static readonly DiagnosticDescriptor AMAR4004 = new(
            "AMAR4004",
            LocalizedStrings.Get("AMAR4004_Title"),
            LocalizedStrings.Get("AMAR4004_Message"),
            Category, DiagnosticSeverity.Error, isEnabledByDefault: true);

        /// <summary>继承链上存在多个 [RegistrationOptions]。</summary>
        public static readonly DiagnosticDescriptor AMAR4005 = new(
            "AMAR4005",
            LocalizedStrings.Get("AMAR4005_Title"),
            LocalizedStrings.Get("AMAR4005_Message"),
            Category, DiagnosticSeverity.Warning, isEnabledByDefault: true);

        /// <summary>泛型注册特性的类型参数超过 1 个。</summary>
        public static readonly DiagnosticDescriptor AMAR4006 = new(
            "AMAR4006",
            LocalizedStrings.Get("AMAR4006_Title"),
            LocalizedStrings.Get("AMAR4006_Message"),
            Category, DiagnosticSeverity.Error, isEnabledByDefault: true);

        /// <summary>泛型目标类型与 typeof(...) 服务类型冲突。</summary>
        public static readonly DiagnosticDescriptor AMAR4007 = new(
            "AMAR4007",
            LocalizedStrings.Get("AMAR4007_Title"),
            LocalizedStrings.Get("AMAR4007_Message"),
            Category, DiagnosticSeverity.Error, isEnabledByDefault: true);
    }
}
