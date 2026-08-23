namespace Airflow.MSDI.AutoRegister.Demo;

/// <summary>
/// 由 <see cref="ServiceH.GetServiceI"/> 工厂方法产出的服务,依赖注入 <see cref="InterfaceF"/>。
/// </summary>
public class ServiceI
{
    public InterfaceF Dependency { get; }
    public ServiceI(InterfaceF dependency) => Dependency = dependency;
}
