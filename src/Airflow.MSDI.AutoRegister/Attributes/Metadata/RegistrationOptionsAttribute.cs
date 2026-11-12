using Microsoft.Extensions.DependencyInjection;

/// <summary>
/// 描述 <see cref="BaseServiceAttribute"/> 子类特性的元数据:
/// 说明其构造函数参数中哪个是生命周期、哪个是服务类型、哪个是排序值,以及默认值。
/// </summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = true)]
public class RegistrationOptionsAttribute : Attribute
{
    public int LifetimeIndex { get; set; } = 0;
    public int ServiceTypeIndex { get; set; } = 1;
    public int OrderIndex { get; set; } = 2;

    public ServiceLifetime DefaultLifetime { get; set; } = ServiceLifetime.Singleton;

    public Type? DefaultServiceType { get; set; } = null;

    public double DefaultOrder { get; set; } = 0;

    /// <summary>
    /// 该特性类的默认 key 表达式模板;null/空表示不生成默认 key。
    /// 模板中的占位符会被替换后写入生成代码(运行时求值),源生成器不会执行它。
    /// </summary>
    public string? KeyTemplate { get; set; }
}