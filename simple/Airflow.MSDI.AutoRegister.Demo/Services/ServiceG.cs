namespace Airflow.MSDI.AutoRegister.Demo;

/// <summary>
/// 与 <see cref="ServiceF"/> 注册到同一接口 <see cref="InterfaceF"/>,Order=1。
/// </summary>
[SingletonService(typeof(InterfaceF), 1)]
public class ServiceG : InterfaceF { }
