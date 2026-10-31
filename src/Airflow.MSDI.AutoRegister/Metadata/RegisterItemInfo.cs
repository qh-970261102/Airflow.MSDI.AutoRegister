using Microsoft.Extensions.DependencyInjection;

namespace Airflow.MSDI.AutoRegister
{
    /// <summary>
    /// 一条注册的元数据:哪个 key、哪个实现类型、哪个生命周期、排序值是多少。
    /// 源生成器会为每个服务类型注册一份 <see cref="IEnumerable{T}"/>(
    /// <c>IEnumerable&lt;RegisterItemInfo&lt;TService&gt;&gt;</c>),用于在运行时读取注册清单。
    /// </summary>
    /// <typeparam name="TService">服务类型(targettype)。</typeparam>
    public sealed class RegisterItemInfo<TService>
    {
        /// <summary>创建一条注册元数据。</summary>
        /// <param name="key">服务 key;null 表示非 keyed 注册。</param>
        /// <param name="implementationType">实现类型(类注册为实现的类;工厂方法注册为方法的返回类型)。</param>
        /// <param name="lifetime">生命周期。</param>
        /// <param name="order">排序值。</param>
        public RegisterItemInfo(
            string? key,
            Type implementationType,
            ServiceLifetime lifetime,
            double order)
        {
            Key = key;
            ImplementationType = implementationType;
            Lifetime = lifetime;
            Order = order;
        }

        /// <summary>服务 key;null 表示非 keyed 注册。</summary>
        public string? Key { get; }

        /// <summary>实现类型(类注册为实现的类;工厂方法注册为方法的返回类型,即实际产出的类型)。</summary>
        public Type ImplementationType { get; }

        /// <summary>服务类型,恒等于 typeof(TService)。</summary>
        public Type ServiceType => typeof(TService);

        /// <summary>生命周期。</summary>
        public ServiceLifetime Lifetime { get; }

        /// <summary>排序值(与注册时一致)。</summary>
        public double Order { get; }

        /// <inheritdoc />
        public override string ToString() =>
            $"{(Key is null ? "(no key)" : "\"" + Key + "\"")} -> {ImplementationType.Name} ({Lifetime}, order {Order})";
    }
}
