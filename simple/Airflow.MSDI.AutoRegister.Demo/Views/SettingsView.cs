namespace Airflow.MSDI.AutoRegister.Demo;

/// <summary>
/// 默认目标仍是 <see cref="IView"/>,用命名参数指定 Order(集合中排在 MainView 之后);
/// KeyTemplate 得到的 key 为 "Settings"。
/// </summary>
[View(Order = 1)]
public class SettingsView : IView { }
