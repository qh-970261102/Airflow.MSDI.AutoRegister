namespace Airflow.MSDI.AutoRegister.Demo;

/// <summary>所有 View 的默认注册目标(由 <see cref="ViewAttribute"/> 的 DefaultServiceType 指定)。</summary>
public interface IView { }

/// <summary>所有 ViewModel 的默认注册目标(由 <see cref="ViewModelAttribute"/> 的 DefaultServiceType 指定)。</summary>
public interface IViewModel { }

/// <summary>用于演示"显式覆盖默认目标"的接口。</summary>
public interface ISpecialView { }
