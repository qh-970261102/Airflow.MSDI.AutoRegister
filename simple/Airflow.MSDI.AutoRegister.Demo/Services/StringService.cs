namespace Airflow.MSDI.AutoRegister.Demo;

/// <summary>
/// 闭合泛型服务类型注册:生成
/// <c>services.AddSingleton&lt;IGenericService&lt;string&gt;, StringService&gt;();</c>
/// 特性里的类型(IGenericService&lt;string&gt;)原样填入,实际类型(StringService)为具体类。
/// </summary>
[SingletonService(typeof(IGenericService<string>))]
public class StringService : GenericServiceBase<string> { }
