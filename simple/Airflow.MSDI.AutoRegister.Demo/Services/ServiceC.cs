namespace Airflow.MSDI.AutoRegister.Demo;

/// <summary>
/// 瞬时服务:每次解析都创建新实例。
/// </summary>
[TransientService]
public class ServiceC { }
