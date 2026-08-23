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