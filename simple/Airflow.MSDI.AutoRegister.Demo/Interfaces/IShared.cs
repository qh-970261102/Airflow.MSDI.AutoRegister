namespace Airflow.MSDI.AutoRegister.Demo;

/// <summary>与 <see cref="ISharedRight"/> 一起由 ServiceM 实现,用于演示"同一实现多接口共享实例"。</summary>
public interface ISharedLeft { }

/// <summary>与 <see cref="ISharedLeft"/> 一起由 ServiceM 实现。</summary>
public interface ISharedRight { }
