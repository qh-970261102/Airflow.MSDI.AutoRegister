namespace Airflow.MSDI.AutoRegister.Demo;

/// <summary>
/// 演示闭合泛型工厂方法:返回类型 <see cref="List{T}"/> 按原样注册,
/// 生成 <c>services.AddSingleton(sp =&gt; sp.GetRequiredService&lt;GenericListFactory&gt;().GetStringList());</c>
/// —— 服务类型 List&lt;string&gt; 由 lambda 返回值自动推断,无需重复写出。
/// </summary>
public class GenericListFactory
{
    [SingletonService]
    public List<string> GetStringList() => new();
}
