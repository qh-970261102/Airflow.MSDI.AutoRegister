namespace Airflow.MSDI.AutoRegister.Demo;

/// <summary>
/// 由多个 keyed 实现共同提供的连接器接口,用于演示 keyed 注册与集合聚合。
/// </summary>
public interface IConnector
{
    /// <summary>连接器名称(便于 Demo 输出)。</summary>
    string Name { get; }
}
