using Microsoft.Extensions.DependencyInjection;

/// <summary>
/// 所有注册特性的基类。子类必须使用 <see cref="RegistrationOptionsAttribute"/> 描述其元数据。
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = true, Inherited = true)]
public class BaseServiceAttribute : Attribute
{
    public ServiceLifetime Lifetime{get;private set;}

    public Type? ServiceType {get;private set;}

    public double Order {get;private set;}


    public BaseServiceAttribute(ServiceLifetime lifetime, Type? serviceType = null, double order = 0)
    {
        Lifetime = lifetime;
        Order = order;
        ServiceType = serviceType;
    }
}