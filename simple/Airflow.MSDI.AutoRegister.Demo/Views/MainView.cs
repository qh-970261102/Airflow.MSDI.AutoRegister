namespace Airflow.MSDI.AutoRegister.Demo;

/// <summary>
/// 不写参数,走 <see cref="ViewAttribute"/> 的默认值 → 注册为 <see cref="IView"/>(Transient, Order 0);
/// KeyTemplate 把类型名去掉末尾 View,key 为 "Main"。
/// </summary>
[View]
public class MainView : IView { }
