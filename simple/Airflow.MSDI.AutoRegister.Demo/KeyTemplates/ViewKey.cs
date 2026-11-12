using System;

namespace Airflow.MSDI.AutoRegister.Demo.KeyTemplates;

/// <summary>
/// View 的注册 key 计算:去掉类型名末尾的 "View"(只去一次)。
/// 由 <see cref="Airflow.MSDI.AutoRegister.Demo.ViewAttribute"/> 的 KeyTemplate 通过生成代码调用。
/// </summary>
public static class ViewKey
{
    public static string GetViewRegisterKey(Type viewType)
    {
        var name = viewType.Name;
        return name.EndsWith("View", StringComparison.Ordinal)
            ? name.Substring(0, name.Length - "View".Length)
            : name;
    }
}
