namespace Airflow.MSDI.AutoRegister.Demo;

/// <summary>
/// 泛型实现基类。本身是泛型类,不支持直接注册(AMAR2003 会拦截);
/// 由具体类闭合类型参数后注册——"多包一层"即可。
/// </summary>
public class GenericServiceBase<T> : IGenericService<T> { }
