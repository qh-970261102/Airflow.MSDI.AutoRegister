; Unshipped analyzer releases
; https://github.com/dotnet/roslyn-analyzers/blob/main/src/Microsoft.CodeAnalysis.Analyzers/ReleaseTrackingAnalyzers.Help.md

### New Rules

Rule ID | Category | Severity | Notes
--------|----------|----------|-------
AMAR1001 | Airflow.MSDI.AutoRegister | Error | 工厂方法拥有类必须是普通类
AMAR1002 | Airflow.MSDI.AutoRegister | Error | 拥有工厂方法的类不能再添加注册特性
AMAR1003 | Airflow.MSDI.AutoRegister | Error | 工厂方法不能是 static
AMAR1004 | Airflow.MSDI.AutoRegister | Error | 工厂方法必须 public
AMAR1005 | Airflow.MSDI.AutoRegister | Error | 工厂方法不能是泛型方法
AMAR1006 | Airflow.MSDI.AutoRegister | Error | 工厂方法不能返回 void
AMAR1007 | Airflow.MSDI.AutoRegister | Error | 工厂方法返回类型必须是可解析的非泛型具名类型
AMAR1008 | Airflow.MSDI.AutoRegister | Error | 工厂方法参数个数不合法
AMAR1009 | Airflow.MSDI.AutoRegister | Error | 工厂方法单个参数必须是 IServiceProvider
AMAR1010 | Airflow.MSDI.AutoRegister | Error | 单例工厂方法不允许 IServiceProvider 参数
AMAR2001 | Airflow.MSDI.AutoRegister | Error | 注册特性作用于不支持的目标
AMAR2002 | Airflow.MSDI.AutoRegister | Error | 服务类型必须是接口或类
AMAR2003 | Airflow.MSDI.AutoRegister | Error | 泛型类不支持注册
AMAR2005 | Airflow.MSDI.AutoRegister | Warning | 同一 (ServiceType, ImplType) 重复注册
AMAR2006 | Airflow.MSDI.AutoRegister | Warning | 自身注册与接口注册生命周期不一致
AMAR3001 | Airflow.MSDI.AutoRegister | Error | [AutoRegister] 方法必须声明为 partial
AMAR3002 | Airflow.MSDI.AutoRegister | Error | [AutoRegister] 方法所在类必须是 partial
AMAR3003 | Airflow.MSDI.AutoRegister | Error | [AutoRegister] 方法不能有手写实现
AMAR3004 | Airflow.MSDI.AutoRegister | Error | [AutoRegister] 方法必须是 static
AMAR3005 | Airflow.MSDI.AutoRegister | Error | [AutoRegister] 方法签名必须是 (IServiceCollection services)
AMAR3006 | Airflow.MSDI.AutoRegister | Warning | 程序集内没有可注册的服务
AMAR3007 | Airflow.MSDI.AutoRegister | Error | 同一程序集只允许一个 [AutoRegister] 方法
AMAR4001 | Airflow.MSDI.AutoRegister | Error | 缺少 [RegistrationOptions]
AMAR4002 | Airflow.MSDI.AutoRegister | Error | [RegistrationOptions] 索引超出构造函数参数个数
AMAR4003 | Airflow.MSDI.AutoRegister | Error | [RegistrationOptions] 索引对应参数类型不符
AMAR4004 | Airflow.MSDI.AutoRegister | Error | 生命周期枚举值非法
AMAR4005 | Airflow.MSDI.AutoRegister | Warning | 继承链上存在多个 [RegistrationOptions]
