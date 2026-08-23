namespace Airflow.MSDI.AutoRegister.Demo;

/// <summary>
/// 同时注册类自身和接口 <see cref="InterfaceE"/>,共享同一单例。
/// </summary>
[SingletonService]
[SingletonService(typeof(InterfaceE))]
public class ServiceE : InterfaceE { }
