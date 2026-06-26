using DotnetCoupling.Model;

namespace DotnetCoupling.Rules;

/// <summary>
/// レイヤー方向違反を検出する。例: Domain -> Infrastructure, Application -> Web。
/// レイヤー構成が未設定の場合は何もしない。
/// </summary>
public sealed class LayerViolationRule : IArchitectureRule
{
    public string RuleId => "layerViolation";

    public IEnumerable<RuleViolation> Evaluate(CouplingGraph graph, RuleOptions options)
    {
        if (options.Layers is not { } layers)
            yield break;

        var severity = options.SeverityOf(RuleId, RuleSeverity.Error);

        foreach (var edge in graph.Edges)
        {
            var source = graph.FindNode(edge.SourceId);
            var target = graph.FindNode(edge.TargetId);
            if (source is null || target is null)
                continue;

            var fromLayer = layers.LayerOf(source);
            var toLayer = layers.LayerOf(target);
            if (fromLayer is null || toLayer is null || fromLayer == toLayer)
                continue;

            if (layers.MayDepend(fromLayer, toLayer))
                continue;

            var occurrence = edge.Occurrences.Count > 0 ? edge.Occurrences[0] : null;
            yield return new RuleViolation(
                RuleId,
                severity,
                $"Layer '{fromLayer}' must not depend on '{toLayer}': {Short(edge.SourceId)} -> {Short(edge.TargetId)}.",
                edge.SourceId,
                edge.TargetId,
                occurrence?.File,
                occurrence?.Line);
        }
    }

    private static string Short(string id) =>
        id.StartsWith("T:", StringComparison.Ordinal) ? id.Substring(2) : id;
}
