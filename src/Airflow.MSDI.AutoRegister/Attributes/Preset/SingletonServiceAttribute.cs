using Microsoft.Extensions.DependencyInjection;

[
    RegistrationOptions(
        LifetimeIndex = -1,
        ServiceTypeIndex = 0,
        OrderIndex = 1,
        DefaultLifetime = ServiceLifetime.Singleton,
        DefaultServiceType = null,
        DefaultOrder = 0
    )
]
public sealed class SingletonServiceAttribute : BaseServiceAttribute
{
    public SingletonServiceAttribute(Type? serviceType = null, double order = 0)
        : base(ServiceLifetime.Singleton, serviceType, order)
    {
    }
}

/// <summary>
/// 以泛型参数指定 targettype 的 Singleton 注册特性(等价于 <c>[SingletonService(typeof(TTarget))]</c>)。
/// key 与 order 通过命名参数指定:<c>[SingletonService&lt;IFoo&gt;(Key = "primary", Order = 1)]</c>。
/// </summary>
[
    RegistrationOptions(
        LifetimeIndex = -1,
        ServiceTypeIndex = -1,
        OrderIndex = -1,
        DefaultLifetime = ServiceLifetime.Singleton,
        DefaultServiceType = null,
        DefaultOrder = 0
    )
]
public sealed class SingletonServiceAttribute<TTarget> : BaseServiceAttribute where TTarget : class
{
    public SingletonServiceAttribute()
        : base(ServiceLifetime.Singleton, typeof(TTarget))
    {
    }
}
