using DotnetCoupling.Model;

namespace DotnetCoupling.Scoring;

/// <summary>依存種別ごとの Integration Strength（docs/scoring.md S3）。</summary>
public static class IntegrationStrengthCalculator
{
    /// <summary>
    /// kind 単独から決まる基準 strength。
    /// DI / Reflection / DynamicAccess は付録 A の代表値（解析側がパターンで上書き可能）。
    /// </summary>
    public static double ForKind(DependencyKind kind) => kind switch
    {
        DependencyKind.Inheritance => 1.00,
        DependencyKind.InterfaceImplementation => 0.90,
        DependencyKind.FieldType => 0.85,
        DependencyKind.PropertyType => 0.80,
        DependencyKind.ConstructorParameter => 0.75,
        DependencyKind.ObjectCreation => 0.70,
        DependencyKind.ReturnType => 0.65,
        DependencyKind.MethodParameter => 0.65,
        DependencyKind.StaticAccess => 0.60,
        DependencyKind.MethodCall => 0.50,
        DependencyKind.GenericArgument => 0.45,
        DependencyKind.Attribute => 0.30,
        DependencyKind.DiRegistration => 0.50,   // 付録A: AddScoped<I,Impl> 既定
        DependencyKind.UsingDirective => 0.10,
        DependencyKind.Reflection => 0.20,        // 付録A 代表値
        DependencyKind.DynamicAccess => 0.20,     // 付録A 代表値
        _ => 0.10
    };

    /// <summary>束ねた出現群からエッジ strength を決める = max(kind strength)（docs/scoring.md S3）。</summary>
    public static double ForEdge(IEnumerable<Occurrence> occurrences)
    {
        double max = 0.0;
        foreach (var o in occurrences)
        {
            var s = ForKind(o.Kind);
            if (s > max) max = s;
        }
        return max;
    }
}
