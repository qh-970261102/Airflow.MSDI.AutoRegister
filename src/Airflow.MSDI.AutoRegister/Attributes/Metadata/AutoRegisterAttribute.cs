/// <summary>
/// 标记在 partial 方法上,由源生成器自动生成该方法体,用于注册当前程序集内所有
/// 带有 <see cref="BaseServiceAttribute"/> 派生特性的服务。
/// </summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = false)]
public sealed class AutoRegisterAttribute : Attribute
{
}