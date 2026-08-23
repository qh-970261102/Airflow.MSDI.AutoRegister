namespace Airflow.MSDI.AutoRegister.Demo;

/// <summary>
/// 宿主类:通过工厂方法注册派生服务。
/// </summary>
public class ServiceH
{
    private readonly InterfaceF _serviceG;
    public ServiceH(InterfaceF serviceG)
    {
        this._serviceG = serviceG;
    }

    /// <summary>
    /// 工厂方法:注册 <see cref="ServiceI"/>(Singleton)。
    /// </summary>
    [SingletonService]
    public ServiceI GetServiceI()
    {
        return new ServiceI(_serviceG);
    }

    /// <summary>
    /// 工厂方法:注册 <see cref="ServiceJ"/>(Scoped),可注入 <see cref="IServiceProvider"/>。
    /// </summary>
    [ScopedService]
    public ServiceJ GetServiceJ(IServiceProvider sp)
    {
        return new ServiceJ(_serviceG);
    }
}
