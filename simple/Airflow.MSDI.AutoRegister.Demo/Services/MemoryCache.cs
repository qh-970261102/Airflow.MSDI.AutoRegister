namespace Airflow.MSDI.AutoRegister.Demo;

/// <summary>keyed scoped 缓存。</summary>
public class MemoryCache : ICache
{
    public string Name => "memory";
}
