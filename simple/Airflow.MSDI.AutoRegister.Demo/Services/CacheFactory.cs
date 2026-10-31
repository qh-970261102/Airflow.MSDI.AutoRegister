namespace Airflow.MSDI.AutoRegister.Demo;

/// <summary>
/// keyed 工厂方法:拥有类自动注册为非 keyed 单例,工厂返回值按 key + 生命周期注册。
/// </summary>
public class CacheFactory
{
    /// <summary>单例 keyed 工厂(单例工厂不允许带 IServiceProvider 参数)。</summary>
    [SingletonService<ICache>(Key = "redis")]
    public ICache CreateRedis() => new RedisCache();

    /// <summary>scoped keyed 工厂(可以接收 IServiceProvider)。</summary>
    [ScopedService<ICache>(Key = "memory")]
    public ICache CreateMemory(IServiceProvider sp) => new MemoryCache();
}
