namespace Airflow.MSDI.AutoRegister.Demo;

/// <summary>
/// keyed 注册:自身与接口都使用 key "primary",解析到同一个单例。
/// </summary>
[SingletonService(Key = "primary")]
[SingletonService(typeof(IConnector), Key = "primary")]
public class SqlConnector : IConnector
{
    public string Name => "sql";
}
