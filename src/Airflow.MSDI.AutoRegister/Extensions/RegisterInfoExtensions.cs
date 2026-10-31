using System;
using System.Collections.Generic;
using System.Linq;

namespace Airflow.MSDI.AutoRegister
{
    /// <summary>
    /// <see cref="RegisterItemInfo{TService}"/> 注册清单的便捷扩展:按 key 查找、转 key→实现映射、遍历键值对。
    /// </summary>
    public static class RegisterInfoExtensions
    {
        /// <summary>
        /// 按 key 查找一条注册;找不到返回 <c>null</c>。无 key 的注册用 <c>null</c> 查找。
        /// </summary>
        public static RegisterItemInfo<TService>? FindByKey<TService>(
            this IEnumerable<RegisterItemInfo<TService>> items,
            string? key)
        {
            if (items is null)
                throw new ArgumentNullException(nameof(items));

            foreach (var item in items)
            {
                if (string.Equals(item.Key, key, StringComparison.Ordinal))
                    return item;
            }

            return null;
        }

        /// <summary>
        /// 转成 <c>key → 实现类型</c> 映射(仅包含命名 key;无 key 的注册不参与)。
        /// 重复 key 时后出现的覆盖先出现的(同一 targettype 下命名 key 唯一,正常不会发生)。
        /// </summary>
        public static IReadOnlyDictionary<string, Type> ToKeyMap<TService>(
            this IEnumerable<RegisterItemInfo<TService>> items)
        {
            if (items is null)
                throw new ArgumentNullException(nameof(items));

            var map = new Dictionary<string, Type>(StringComparer.Ordinal);
            foreach (var item in items)
            {
                if (item.Key is null)
                    continue;

                map[item.Key] = item.ImplementationType;
            }

            return map;
        }

        /// <summary>
        /// 遍历 <c>key → 实现类型</c> 键值对(包含无 key 的注册,其 key 为 <c>null</c>)。
        /// </summary>
        public static IEnumerable<KeyValuePair<string?, Type>> ToKeyValuePairs<TService>(
            this IEnumerable<RegisterItemInfo<TService>> items)
        {
            if (items is null)
                throw new ArgumentNullException(nameof(items));

            foreach (var item in items)
                yield return new KeyValuePair<string?, Type>(item.Key, item.ImplementationType);
        }
    }

    /// <summary>
    /// 从 <see cref="IServiceProvider"/> 读取注册清单的便捷扩展,避免使用者手写
    /// <c>GetRequiredService&lt;IEnumerable&lt;RegisterItemInfo&lt;T&gt;&gt;&gt;()</c>。
    /// </summary>
    public static class ServiceProviderRegisterInfoExtensions
    {
        /// <summary>
        /// 取某个服务类型的注册清单;该类型没有任何注册时返回空列表(不抛异常)。
        /// </summary>
        public static IReadOnlyList<RegisterItemInfo<TService>> GetRegisterItems<TService>(this IServiceProvider provider)
        {
            if (provider is null)
                throw new ArgumentNullException(nameof(provider));

            var items = provider.GetService(typeof(IEnumerable<RegisterItemInfo<TService>>))
                        as IEnumerable<RegisterItemInfo<TService>>;

            if (items is null)
                return Array.Empty<RegisterItemInfo<TService>>();

            return items as IReadOnlyList<RegisterItemInfo<TService>> ?? items.ToList();
        }

        /// <summary>按 key 查找某个服务类型的一条注册;找不到返回 <c>null</c>。</summary>
        public static RegisterItemInfo<TService>? FindRegisterItem<TService>(this IServiceProvider provider, string? key)
            => provider.GetRegisterItems<TService>().FindByKey(key);

        /// <summary>取某个服务类型的 <c>key → 实现类型</c> 映射(仅命名 key)。</summary>
        public static IReadOnlyDictionary<string, Type> GetRegisterMap<TService>(this IServiceProvider provider)
            => provider.GetRegisterItems<TService>().ToKeyMap();
    }
}
