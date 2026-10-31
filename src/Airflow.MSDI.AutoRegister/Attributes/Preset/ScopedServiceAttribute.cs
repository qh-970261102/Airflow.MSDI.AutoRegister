using Microsoft.Extensions.DependencyInjection;

[
    RegistrationOptions(
        LifetimeIndex = -1,
        ServiceTypeIndex = 0,
        OrderIndex = 1,
        DefaultLifetime = ServiceLifetime.Scoped,
        DefaultServiceType = null,
        DefaultOrder = 0
    )
]
public sealed class ScopedServiceAttribute : BaseServiceAttribute
{
    public ScopedServiceAttribute(Type? serviceType = null, double order = 0)
        : base(ServiceLifetime.Scoped, serviceType, order)
    {
    }
}

/// <summary>
/// 以泛型参数指定 targettype 的 Scoped 注册特性(等价于 <c>[ScopedService(typeof(TTarget))]</c>)。
/// </summary>
[
    RegistrationOptions(
        LifetimeIndex = -1,
        ServiceTypeIndex = -1,
        OrderIndex = -1,
        DefaultLifetime = ServiceLifetime.Scoped,
        DefaultServiceType = null,
        DefaultOrder = 0
    )
]
public sealed class ScopedServiceAttribute<TTarget> : BaseServiceAttribute where TTarget : class
{
    public ScopedServiceAttribute()
        : base(ServiceLifetime.Scoped, typeof(TTarget))
    {
    }
}
