using DotnetCoupling.Model;

namespace DotnetCoupling.Rules;

/// <summary>
/// 上位層が具象クラスに直接依存している箇所を検出する。
/// 「具象」はグラフ位相から判定する: 対象型が InterfaceImplementation エッジの始点である
/// （= 何らかの interface を実装している）なら、その interface に依存し直せるはず、とみなす。
/// 保持系の強い依存（field/ctor/property/objectCreation）かつ越境のものを対象にする。
/// </summary>
public sealed class ConcreteDependencyRule : IArchitectureRule
{
    public string RuleId => "concreteDependency";

    public IEnumerable<RuleViolation> Evaluate(CouplingGraph graph, RuleOptions options)
    {
        var severity = options.SeverityOf(RuleId, RuleSeverity.Warning);

        // 型 -> 実装している interface 群。
        var implementedInterfaces = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        foreach (var edge in graph.Edges)
        {
            if (edge.Occurrences.Any(o => o.Kind == DependencyKind.InterfaceImplementation))
            {
                if (!implementedInterfaces.TryGetValue(edge.SourceId, out var list))
                    implementedInterfaces[edge.SourceId] = list = new List<string>();
                list.Add(edge.TargetId);
            }
        }

        foreach (var edge in graph.Edges)
        {
            if (edge.Strength < options.ConcreteMinStrength || edge.Distance < options.ConcreteMinDistance)
                continue;
            if (!implementedInterfaces.TryGetValue(edge.TargetId, out var interfaces) || interfaces.Count == 0)
                continue;

            var occurrence = edge.Occurrences.Count > 0 ? edge.Occurrences[0] : null;
            var abstractions = string.Join(", ", interfaces.Select(Short));
            yield return new RuleViolation(
                RuleId,
                severity,
                $"{Short(edge.SourceId)} depends on concrete {Short(edge.TargetId)}; depend on its abstraction instead ({abstractions}).",
                edge.SourceId,
                edge.TargetId,
                occurrence?.File,
                occurrence?.Line);
        }
    }

    private static string Short(string id) =>
        id.StartsWith("T:", StringComparison.Ordinal) ? id.Substring(2) : id;
}
