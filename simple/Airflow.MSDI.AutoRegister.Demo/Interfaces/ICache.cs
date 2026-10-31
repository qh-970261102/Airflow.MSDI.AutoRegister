namespace Airflow.MSDI.AutoRegister.Demo;

/// <summary>
/// 由 keyed 工厂方法提供的缓存接口。
/// </summary>
public interface ICache
{
    /// <summary>缓存名称(便于 Demo 输出)。</summary>
    string Name { get; }
}
