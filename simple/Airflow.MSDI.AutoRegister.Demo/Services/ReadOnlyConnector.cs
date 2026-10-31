namespace Airflow.MSDI.AutoRegister.Demo;

/// <summary>
/// 泛型特性写法:等价于 <c>[SingletonService(typeof(IConnector), Key = "replica")]</c>。
/// 单条注册且命名 key 唯一,生成的代码是直接 keyed 注册,不产生锚点。
/// </summary>
[SingletonService<IConnector>(Key = "replica")]
public class ReadOnlyConnector : IConnector
{
    public string Name => "readonly";
}
