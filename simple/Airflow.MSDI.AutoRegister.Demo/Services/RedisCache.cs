namespace Airflow.MSDI.AutoRegister.Demo;

/// <summary>keyed 单例缓存。</summary>
public class RedisCache : ICache
{
    public string Name => "redis";
}
