namespace Airflow.MSDI.AutoRegister.Demo;

/// <summary>
/// 只注册到接口 <see cref="InterfaceD"/>。
/// </summary>
[SingletonService(typeof(InterfaceD))]
public class ServiceD : InterfaceD { }
