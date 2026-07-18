using DotnetCoupling.Analysis;
using DotnetCoupling.Model;

namespace DotnetCoupling.Tests;

/// <summary>
/// Track A で追加した依存種別（GenericArgument / DiRegistration / StaticAccess / Attribute）の
/// 抽出を CouplingWalker 単体（in-memory コンパイル）で検証する。
/// </summary>
public class WalkerDependencyKindTests
{
    private static IReadOnlyList<TypeDependency> Walk(string source) =>
        new CouplingWalker().Walk(TestCompiler.Compile("Test", source));

    private static bool Has(
        IReadOnlyList<TypeDependency> deps, string source, string target, DependencyKind kind) =>
        deps.Any(d => d.Source.Name == source && d.Target.Name == target && d.Kind == kind);

    // ---- GenericArgument（精度バグ修正: List<Order> の Order を失わない） ----

    [Fact]
    public void GenericArgument_FieldOfGenericContainer_CapturesTypeArgument()
    {
        const string src = @"
namespace Shop {
    public class Box<T> { public T Value; }
    public class Order { }
    public class Cart { private Box<Order> _orders; }
}";
        var deps = Walk(src);

        // container（Box）は従来どおり FieldType、型引数 Order は GenericArgument。
        Assert.True(Has(deps, "Cart", "Box", DependencyKind.FieldType));
        Assert.True(Has(deps, "Cart", "Order", DependencyKind.GenericArgument));
    }

    [Fact]
    public void GenericArgument_NestedGeneric_IsRecursivelyCaptured()
    {
        const string src = @"
namespace Shop {
    public class Box<T> { public T Value; }
    public class Order { }
    public class Cart { private Box<Box<Order>> _orders; }
}";
        var deps = Walk(src);

        // ネストしても最内の Order を GenericArgument として拾う。
        Assert.True(Has(deps, "Cart", "Order", DependencyKind.GenericArgument));
    }

    // ---- DiRegistration（合成起点 → 具象実装） ----

    private const string DiInfrastructure = @"
namespace Microsoft.Extensions.DependencyInjection {
    public interface IServiceCollection { }
    public static class ServiceCollectionServiceExtensions {
        public static IServiceCollection AddScoped<TService, TImpl>(this IServiceCollection s) where TImpl : TService => s;
        public static IServiceCollection AddSingleton<TImpl>(this IServiceCollection s) => s;
        public static IServiceCollection AddTransient(this IServiceCollection s, System.Type service, System.Type impl) => s;
    }
}";

    [Fact]
    public void DiRegistration_GenericTwoTypeArgs_RecordsCompositionRootToImplementation()
    {
        var deps = Walk(DiInfrastructure + @"
namespace App {
    using Microsoft.Extensions.DependencyInjection;
    public interface IFoo { }
    public class Foo : IFoo { }
    public class Startup {
        public void Configure(IServiceCollection services) { services.AddScoped<IFoo, Foo>(); }
    }
}");

        // 合成起点 Startup → 具象 Foo のみ（抽象 IFoo は DiRegistration にしない）。
        Assert.True(Has(deps, "Startup", "Foo", DependencyKind.DiRegistration));
        Assert.False(Has(deps, "Startup", "IFoo", DependencyKind.DiRegistration));
    }

    [Fact]
    public void DiRegistration_GenericSelfBinding_RecordsImplementation()
    {
        var deps = Walk(DiInfrastructure + @"
namespace App {
    using Microsoft.Extensions.DependencyInjection;
    public class Bar { }
    public class Startup {
        public void Configure(IServiceCollection services) { services.AddSingleton<Bar>(); }
    }
}");

        Assert.True(Has(deps, "Startup", "Bar", DependencyKind.DiRegistration));
    }

    [Fact]
    public void DiRegistration_TypeofPair_RecordsImplementation()
    {
        var deps = Walk(DiInfrastructure + @"
namespace App {
    using Microsoft.Extensions.DependencyInjection;
    public interface IBaz { }
    public class Baz : IBaz { }
    public class Startup {
        public void Configure(IServiceCollection services) { services.AddTransient(typeof(IBaz), typeof(Baz)); }
    }
}");

        Assert.True(Has(deps, "Startup", "Baz", DependencyKind.DiRegistration));
        Assert.False(Has(deps, "Startup", "IBaz", DependencyKind.DiRegistration));
    }

    // ---- StaticAccess（静的 field/property のみ。静的メソッドは MethodCall のまま） ----

    [Fact]
    public void StaticAccess_StaticFieldAndProperty_AreRecorded()
    {
        const string src = @"
namespace App {
    public class Config { public static string Name; public static int Version { get; set; } }
    public class Reader {
        public void M() { var x = Config.Name; var y = Config.Version; }
    }
}";
        var deps = Walk(src);

        Assert.True(Has(deps, "Reader", "Config", DependencyKind.StaticAccess));
    }

    // ---- Attribute（型 + メンバー） ----

    [Fact]
    public void Attribute_TypeAndMemberLevel_AreRecorded()
    {
        const string src = @"
using System;
namespace App {
    public class MyAttribute : Attribute { }
    public class OtherAttribute : Attribute { }
    [My]
    public class Decorated {
        [Other]
        public void M() { }
    }
}";
        var deps = Walk(src);

        Assert.True(Has(deps, "Decorated", "MyAttribute", DependencyKind.Attribute));    // 型レベル
        Assert.True(Has(deps, "Decorated", "OtherAttribute", DependencyKind.Attribute)); // メンバーレベル
    }
}
