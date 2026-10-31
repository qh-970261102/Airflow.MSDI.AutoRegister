using Microsoft.Extensions.DependencyInjection;

[
    RegistrationOptions(
        LifetimeIndex = -1,
        ServiceTypeIndex = 0,
        OrderIndex = 1,
        DefaultLifetime = ServiceLifetime.Transient,
        DefaultServiceType = null,
        DefaultOrder = 0
    )
]
public sealed class TransientServiceAttribute : BaseServiceAttribute
{
    public TransientServiceAttribute(Type? serviceType = null, double order = 0)
        : base(ServiceLifetime.Transient, serviceType, order)
    {
    }
}

/// <summary>
/// 以泛型参数指定 targettype 的 Transient 注册特性(等价于 <c>[TransientService(typeof(TTarget))]</c>)。
/// </summary>
[
    RegistrationOptions(
        LifetimeIndex = -1,
        ServiceTypeIndex = -1,
        OrderIndex = -1,
        DefaultLifetime = ServiceLifetime.Transient,
        DefaultServiceType = null,
        DefaultOrder = 0
    )
]
public sealed class TransientServiceAttribute<TTarget> : BaseServiceAttribute where TTarget : class
{
    public TransientServiceAttribute()
        : base(ServiceLifetime.Transient, typeof(TTarget))
    {
    }
}
