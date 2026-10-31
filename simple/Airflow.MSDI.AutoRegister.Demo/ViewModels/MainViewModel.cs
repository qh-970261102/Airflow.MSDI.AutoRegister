namespace Airflow.MSDI.AutoRegister.Demo;

/// <summary>走 <see cref="ViewModelAttribute"/> 的默认值 → 注册为 <see cref="IViewModel"/>(Transient, 无 key)。</summary>
[ViewModel]
public class MainViewModel : IViewModel { }
