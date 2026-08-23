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