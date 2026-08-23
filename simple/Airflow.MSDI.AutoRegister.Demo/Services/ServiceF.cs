namespace Airflow.MSDI.AutoRegister.Demo;

/// <summary>
/// 与 <see cref="ServiceG"/> 注册到同一接口 <see cref="InterfaceF"/>,Order=2 后注册胜出。
/// </summary>
[SingletonService(typeof(InterfaceF), 2)]
public class ServiceF : InterfaceF { }
