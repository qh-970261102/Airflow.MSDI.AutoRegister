using Microsoft.Extensions.DependencyInjection;

namespace Airflow.MSDI.AutoRegister.Demo;

/// <summary>
/// 自定义 View 注册特性:
/// - 默认把 View 注册为 <see cref="IView"/>、生命周期 Transient、Order 0;
/// - 也可以显式传入目标类型覆盖默认值:<c>[View(typeof(ISpecialView))]</c>;
/// - Key / Order 用命名参数指定:<c>[View(Order = 1)]</c>。
/// </summary>
[RegistrationOptions(
    LifetimeIndex = -1,                      // 生命周期由特性自身决定,不占用构造参数
    ServiceTypeIndex = 0,                    // 构造参数 #0 = 目标类型
    OrderIndex = 1,                          // 构造参数 #1 = Order
    DefaultLifetime = ServiceLifetime.Transient,
    DefaultServiceType = typeof(IView),      // 默认目标类型
    DefaultOrder = 0)]
public sealed class ViewAttribute : BaseServiceAttribute
{
    public ViewAttribute(Type? serviceType = null, double order = 0)
        : base(ServiceLifetime.Transient, serviceType, order)
    {
    }
}
