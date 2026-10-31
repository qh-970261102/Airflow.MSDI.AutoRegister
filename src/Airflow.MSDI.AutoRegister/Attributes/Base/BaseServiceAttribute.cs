using Microsoft.Extensions.DependencyInjection;

/// <summary>
/// 所有注册特性的基类。子类必须使用 <see cref="RegistrationOptionsAttribute"/> 描述其元数据。
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = true, Inherited = true)]
public class BaseServiceAttribute : Attribute
{
    public ServiceLifetime Lifetime { get; private set; }

    public Type? ServiceType { get; private set; }

    /// <summary>
    /// 排序值:数值越大越后注册(同一 targettype + key 下后注册者胜出),同时决定集合与元数据的排序。
    /// 可直接用命名参数覆盖构造函数传入的值。
    /// </summary>
    public double Order { get; set; }

    /// <summary>
    /// 服务 key;null 表示非 keyed 注册。只能在使用处通过命名参数指定(<c>[SingletonService(Key = "primary")]</c>)。
    /// 同一 targettype 下命名 key 必须唯一;无 key 的注册不参与该判断。
    /// </summary>
    public string? Key { get; set; }

    public BaseServiceAttribute(ServiceLifetime lifetime, Type? serviceType = null, double order = 0)
    {
        Lifetime = lifetime;
        Order = order;
        ServiceType = serviceType;
    }
}
