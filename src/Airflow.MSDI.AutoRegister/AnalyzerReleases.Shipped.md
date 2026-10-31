; Shipped analyzer releases
; https://github.com/dotnet/roslyn-analyzers/blob/main/src/Microsoft.CodeAnalysis.Analyzers/ReleaseTrackingAnalyzers.Help.md

## Release 1.1.0

### New Rules

Rule ID | Category | Severity | Notes
--------|----------|----------|-------
AMAR1001 | Airflow.MSDI.AutoRegister | Error | Factory method owner must be a plain class
AMAR1002 | Airflow.MSDI.AutoRegister | Error | A class containing factory methods cannot have registration attributes
AMAR1003 | Airflow.MSDI.AutoRegister | Error | Factory method cannot be static
AMAR1004 | Airflow.MSDI.AutoRegister | Error | Factory method must be public
AMAR1005 | Airflow.MSDI.AutoRegister | Error | Factory method cannot be generic
AMAR1006 | Airflow.MSDI.AutoRegister | Error | Factory method cannot return void
AMAR1007 | Airflow.MSDI.AutoRegister | Error | Factory method return type must be a resolvable named type
AMAR1008 | Airflow.MSDI.AutoRegister | Error | Invalid number of factory method parameters
AMAR1009 | Airflow.MSDI.AutoRegister | Error | Single parameter must be IServiceProvider
AMAR1010 | Airflow.MSDI.AutoRegister | Error | Singleton factory method cannot take IServiceProvider
AMAR2001 | Airflow.MSDI.AutoRegister | Error | Registration attribute on an unsupported target
AMAR2002 | Airflow.MSDI.AutoRegister | Error | Service type must be an interface or class
AMAR2003 | Airflow.MSDI.AutoRegister | Error | Generic classes cannot be registered
AMAR2005 | Airflow.MSDI.AutoRegister | Warning | Duplicate registration of the same (ServiceType, ImplType)
AMAR2006 | Airflow.MSDI.AutoRegister | Warning | Inconsistent lifetimes between self and interface registration
AMAR3001 | Airflow.MSDI.AutoRegister | Error | [AutoRegister] method must be declared partial
AMAR3002 | Airflow.MSDI.AutoRegister | Error | [AutoRegister] method's containing class must be partial
AMAR3003 | Airflow.MSDI.AutoRegister | Error | [AutoRegister] method cannot have a manual implementation
AMAR3004 | Airflow.MSDI.AutoRegister | Error | [AutoRegister] method must be static
AMAR3005 | Airflow.MSDI.AutoRegister | Error | [AutoRegister] method signature must be (IServiceCollection services)
AMAR3006 | Airflow.MSDI.AutoRegister | Warning | No registerable services found in the assembly
AMAR3007 | Airflow.MSDI.AutoRegister | Error | Only one [AutoRegister] method is allowed per assembly
AMAR4001 | Airflow.MSDI.AutoRegister | Error | Missing [RegistrationOptions]
AMAR4002 | Airflow.MSDI.AutoRegister | Error | [RegistrationOptions] index exceeds constructor parameter count
AMAR4003 | Airflow.MSDI.AutoRegister | Error | [RegistrationOptions] index points to a parameter of the wrong type
AMAR4004 | Airflow.MSDI.AutoRegister | Error | Invalid lifetime value
AMAR4005 | Airflow.MSDI.AutoRegister | Warning | Multiple [RegistrationOptions] on the inheritance chain

## Release 2.0.0

### New Rules

Rule ID | Category | Severity | Notes
--------|----------|----------|-------
AMAR2007 | Airflow.MSDI.AutoRegister | Error | Implementation type is not compatible with the service type
AMAR2008 | Airflow.MSDI.AutoRegister | Error | Implementation type cannot be instantiated
AMAR2009 | Airflow.MSDI.AutoRegister | Error | Duplicate service key for the same target type
AMAR4006 | Airflow.MSDI.AutoRegister | Error | Generic registration attribute has too many type parameters
AMAR4007 | Airflow.MSDI.AutoRegister | Error | Conflicting service types in the registration attribute

### Removed Rules

Rule ID | Category | Severity | Notes
--------|----------|----------|-------
AMAR2006 | Airflow.MSDI.AutoRegister | Warning | Removed: instances are grouped per (implementation, lifetime), so self/interface lifetime mismatches no longer exist
