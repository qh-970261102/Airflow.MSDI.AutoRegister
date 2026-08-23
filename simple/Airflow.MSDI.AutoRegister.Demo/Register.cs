using Microsoft.Extensions.DependencyInjection;

namespace Airflow.MSDI.AutoRegister.Demo;

/// <summary>
/// AutoRegister 入口:由源生成器生成 <see cref="AddServicesFromCurrentAssembly"/> 的实现,
/// 一次性注册当前程序集内所有标记了注册特性的服务。
/// </summary>
internal static partial class Register
{
    [AutoRegister]
    public static partial void AddServicesFromCurrentAssembly(IServiceCollection services);

    public static IServiceCollection AddServices(this IServiceCollection services)
    {
        AddServicesFromCurrentAssembly(services);
        return services;
    }
}
