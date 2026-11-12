/// <summary>
/// 标记在 partial 方法上,由源生成器自动生成该方法体,用于注册当前程序集内所有
/// 带有 <see cref="BaseServiceAttribute"/> 派生特性的服务。
/// </summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = false)]
public sealed class AutoRegisterAttribute : Attribute
{
    /// <summary>
    /// 追加到生成文件顶部的 using 指令(不含 using 关键字和结尾分号)。
    /// 支持命名空间、static 类型与 using 别名;不支持 global using。
    /// </summary>
    public string[] Usings { get; set; } = Array.Empty<string>();
}