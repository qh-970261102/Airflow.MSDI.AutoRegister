using Microsoft.Extensions.DependencyInjection;

namespace Airflow.MSDI.AutoRegister.Test;

/// <summary>验证 RegisterItemInfo 便捷扩展:按 key 查找、key→实现映射、从容器读取清单。</summary>
[TestClass]
public sealed class RegisterInfoExtensionsTests
{
    public interface IFoo { }

    public sealed class FooA : IFoo { }

    public sealed class FooB : IFoo { }

    public interface IUnregistered { }

    private static RegisterItemInfo<IFoo>[] Items() =>
    [
        new("a", typeof(FooA), ServiceLifetime.Singleton, 0),
        new("b", typeof(FooB), ServiceLifetime.Scoped, 1),
        new(null, typeof(FooA), ServiceLifetime.Transient, 2),
    ];

    [TestMethod]
    public void FindByKey_ReturnsMatchingItem()
    {
        var item = Items().FindByKey("b");

        Assert.IsNotNull(item);
        Assert.AreEqual("b", item!.Key);
        Assert.AreEqual(typeof(FooB), item.ImplementationType);
    }

    [TestMethod]
    public void FindByKey_SupportsNullKey_AndMissingKey()
    {
        var items = Items();

        Assert.IsNotNull(items.FindByKey(null), "无 key 的注册应能用 null 找到");
        Assert.IsNull(items.FindByKey("missing"));
    }

    [TestMethod]
    public void ToKeyMap_ContainsNamedKeysOnly()
    {
        var map = Items().ToKeyMap();

        Assert.HasCount(2, map);
        Assert.AreEqual(typeof(FooA), map["a"]);
        Assert.AreEqual(typeof(FooB), map["b"]);
    }

    [TestMethod]
    public void ToKeyValuePairs_IncludesUnkeyedRegistrations()
    {
        var pairs = Items().ToKeyValuePairs().ToList();

        Assert.HasCount(3, pairs);
        Assert.AreEqual("a", pairs[0].Key);
        Assert.IsNull(pairs[2].Key, "无 key 的注册以 null 呈现");
        Assert.AreEqual(typeof(FooA), pairs[2].Value);
    }

    [TestMethod]
    public void GetRegisterItems_ReadsFromProvider_WithoutCopying()
    {
        var items = Items();
        var services = new ServiceCollection();
        services.AddSingleton<IEnumerable<RegisterItemInfo<IFoo>>>(items);
        using var provider = services.BuildServiceProvider();

        var result = provider.GetRegisterItems<IFoo>();

        Assert.HasCount(3, result);
        Assert.AreSame(items, result, "生成的集合本身就是数组,应直接复用而不复制");
    }

    [TestMethod]
    public void GetRegisterItems_ForUnregisteredServiceType_ReturnsEmpty()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IEnumerable<RegisterItemInfo<IFoo>>>(Items());
        using var provider = services.BuildServiceProvider();

        Assert.HasCount(0, provider.GetRegisterItems<IUnregistered>());
        Assert.IsNull(provider.FindRegisterItem<IUnregistered>("a"));
        Assert.HasCount(0, provider.GetRegisterMap<IUnregistered>());
    }

    [TestMethod]
    public void ProviderHelpers_WorkEndToEnd()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IEnumerable<RegisterItemInfo<IFoo>>>(Items());
        using var provider = services.BuildServiceProvider();

        Assert.AreEqual("b", provider.FindRegisterItem<IFoo>("b")!.Key);
        Assert.IsNull(provider.FindRegisterItem<IFoo>("nope"));
        Assert.AreEqual(typeof(FooB), provider.GetRegisterMap<IFoo>()["b"]);
    }
}
