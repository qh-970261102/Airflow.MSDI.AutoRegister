using Microsoft.Extensions.DependencyInjection;

namespace Airflow.MSDI.AutoRegister.Demo;

/// <summary>
/// 自定义 ViewModel 注册特性:默认注册为 <see cref="IViewModel"/>、生命周期 Transient。
/// key 用命名参数指定:<c>[ViewModel(Key = "settings")]</c>。
/// </summary>
[RegistrationOptions(
    LifetimeIndex = -1,
    ServiceTypeIndex = 0,
    OrderIndex = 1,
    DefaultLifetime = ServiceLifetime.Transient,
    DefaultServiceType = typeof(IViewModel),
    DefaultOrder = 0)]
public sealed class ViewModelAttribute : BaseServiceAttribute
{
    public ViewModelAttribute(Type? serviceType = null, double order = 0)
        : base(ServiceLifetime.Transient, serviceType, order)
    {
    }
}
