using System.Resources;
using Microsoft.CodeAnalysis;

namespace Airflow.MSDI.AutoRegister.Diagnostics
{
    /// <summary>
    /// 诊断文本的本地化入口。
    /// 资源默认语言为英文(Resources.resx),简体中文位于卫星资源 Resources.zh-Hans.resx;
    /// IDE/编译器按当前 UI 语言自动解析,无需手工切换。
    /// </summary>
    internal static class LocalizedStrings
    {
        private const string BaseName = "Airflow.MSDI.AutoRegister.Resources.Resources";

        private static readonly ResourceManager Manager =
            new ResourceManager(BaseName, typeof(LocalizedStrings).Assembly);

        /// <summary>按资源名取本地化字符串,并包装为 Roslyn 可延迟解析的 <see cref="LocalizableResourceString"/>。</summary>
        public static LocalizableResourceString Get(string name) =>
            new LocalizableResourceString(name, Manager, typeof(LocalizedStrings));
    }
}
