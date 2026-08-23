namespace Airflow.MSDI.AutoRegister.Demo;

/// <summary>
/// 作用域服务:每个 Scope 内同一实例,跨 Scope 不同实例。
/// </summary>
[ScopedService]
public class ServiceB { }
