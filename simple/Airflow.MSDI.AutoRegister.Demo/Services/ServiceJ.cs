namespace Airflow.MSDI.AutoRegister.Demo;

/// <summary>
/// 由 <see cref="ServiceH.GetServiceJ"/> 工厂方法产出的服务,依赖注入 <see cref="InterfaceF"/>。
/// </summary>
public class ServiceJ
{
    public InterfaceF Dependency { get; }
    public ServiceJ(InterfaceF dependency) => Dependency = dependency;
}
