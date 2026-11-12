namespace Airflow.MSDI.AutoRegister.Demo;

/// <summary>
/// 显式传入目标类型,覆盖 <see cref="ViewAttribute"/> 的默认值(不注册为 IView,只注册为 ISpecialView);
/// KeyTemplate 得到的 key 为 "Special"。
/// </summary>
[View(typeof(ISpecialView))]
public class SpecialView : IView, ISpecialView { }
