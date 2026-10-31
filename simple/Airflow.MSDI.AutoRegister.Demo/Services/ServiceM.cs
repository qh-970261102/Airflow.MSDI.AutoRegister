namespace Airflow.MSDI.AutoRegister.Demo;

/// <summary>
/// 同一实现、同一生命周期注册到两个接口:2.0 用一个锚点让两个接口解析到同一个单例。
/// </summary>
[SingletonService(typeof(ISharedLeft))]
[SingletonService(typeof(ISharedRight))]
public class ServiceM : ISharedLeft, ISharedRight { }
