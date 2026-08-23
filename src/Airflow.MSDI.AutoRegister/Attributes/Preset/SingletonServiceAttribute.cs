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