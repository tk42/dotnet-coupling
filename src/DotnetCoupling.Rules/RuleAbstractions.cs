using System.Text.RegularExpressions;
using DotnetCoupling.Model;

namespace DotnetCoupling.Rules;

/// <summary>設計ルール。グラフを評価して違反を返す。</summary>
public interface IArchitectureRule
{
    string RuleId { get; }

    IEnumerable<RuleViolation> Evaluate(CouplingGraph graph, RuleOptions options);
}

/// <summary>ルール評価の設定（レイヤー定義・重大度マップ・閾値）。</summary>
public sealed class RuleOptions
{
    public LayerArchitecture? Layers { get; init; }

    public IReadOnlyDictionary<string, RuleSeverity> Severities { get; init; } = DefaultSeverities;

    /// <summary>ConcreteDependencyRule で「保持」とみなす最小 strength（field/ctor/property/objectCreation）。</summary>
    public double ConcreteMinStrength { get; init; } = 0.70;

    /// <summary>ConcreteDependencyRule で越境とみなす最小 distance（別プロジェクト以遠）。</summary>
    public double ConcreteMinDistance { get; init; } = 0.65;

    public static readonly IReadOnlyDictionary<string, RuleSeverity> DefaultSeverities =
        new Dictionary<string, RuleSeverity>(StringComparer.OrdinalIgnoreCase)
        {
            ["layerViolation"] = RuleSeverity.Error,
            ["circularDependency"] = RuleSeverity.Error,
            ["concreteDependency"] = RuleSeverity.Warning,
        };

    public RuleSeverity SeverityOf(string ruleId, RuleSeverity fallback) =>
        Severities.TryGetValue(ruleId, out var s) ? s : fallback;
}

/// <summary>1 レイヤーの定義（docs spec §18）。Patterns は project 名 / namespace を glob で照合。</summary>
public sealed record LayerDefinition(
    string Name,
    IReadOnlyList<string> Patterns,
    IReadOnlyList<string> MayDependOn);

/// <summary>レイヤー構成。ノードをレイヤーに対応づけ、依存方向の可否を判定する。</summary>
public sealed class LayerArchitecture
{
    private readonly IReadOnlyList<(LayerDefinition Layer, Regex[] Patterns)> _layers;

    public LayerArchitecture(IReadOnlyList<LayerDefinition> layers)
    {
        _layers = layers
            .Select(l => (l, l.Patterns.Select(ToRegex).ToArray()))
            .ToList();
    }

    /// <summary>ノードが属するレイヤー名（未一致なら null）。project 名優先、無ければ namespace。</summary>
    public string? LayerOf(CouplingNode node)
    {
        foreach (var (layer, patterns) in _layers)
        {
            foreach (var pattern in patterns)
            {
                if (node.ProjectName is { } proj && pattern.IsMatch(proj)) return layer.Name;
                if (node.Namespace is { Length: > 0 } ns && pattern.IsMatch(ns)) return layer.Name;
            }
        }
        return null;
    }

    /// <summary>fromLayer が toLayer に依存してよいか。</summary>
    public bool MayDepend(string fromLayer, string toLayer)
    {
        if (string.Equals(fromLayer, toLayer, StringComparison.OrdinalIgnoreCase)) return true;
        var def = _layers.FirstOrDefault(x => string.Equals(x.Layer.Name, fromLayer, StringComparison.OrdinalIgnoreCase)).Layer;
        return def is not null
            && def.MayDependOn.Any(d => string.Equals(d, toLayer, StringComparison.OrdinalIgnoreCase));
    }

    private static Regex ToRegex(string glob)
    {
        // "*.Web" のような単純 glob を正規表現へ。'*' は任意文字列、それ以外はリテラル。
        var escaped = Regex.Escape(glob).Replace("\\*", ".*");
        return new Regex("^" + escaped + "$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    }
}
