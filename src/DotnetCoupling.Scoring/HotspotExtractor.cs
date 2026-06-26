using DotnetCoupling.Model;

namespace DotnetCoupling.Scoring;

/// <summary>
/// グラフから hotspot を抽出する（docs/scoring.md S6）。
/// volatility が不明(NaN)なエッジは、risk が 0 に潰れて拾えなくなるため、
/// 閾値判定には structuralRisk を代用する（Git 連携後は risk を使う）。
/// </summary>
public sealed class HotspotExtractor
{
    public const double RiskThreshold = 0.60;
    public const double StrengthThreshold = 0.50;
    public const double DistanceThreshold = 0.50;

    public IReadOnlyList<Hotspot> Extract(CouplingGraph graph)
    {
        var hotspots = new List<Hotspot>();
        foreach (var edge in graph.Edges)
        {
            var effectiveRisk = double.IsNaN(edge.Volatility) ? edge.StructuralRisk : edge.Risk;
            if (effectiveRisk < RiskThreshold
                || edge.Strength < StrengthThreshold
                || edge.Distance < DistanceThreshold)
            {
                continue;
            }

            var source = graph.FindNode(edge.SourceId);
            var target = graph.FindNode(edge.TargetId);
            var kind = edge.Occurrences.Count > 0 ? edge.Occurrences[0].Kind : DependencyKind.UsingDirective;
            var grade = GradeCalculator.FromScore(RiskScorer.ScoreFromRisk(effectiveRisk));

            hotspots.Add(new Hotspot(
                DisplayName(edge.SourceId, source),
                DisplayName(edge.TargetId, target),
                kind,
                edge.Strength,
                edge.Distance,
                edge.Volatility,
                effectiveRisk,
                grade,
                BuildReason(edge, kind),
                BuildSuggestion(kind),
                RelevantFiles(source, target)));
        }

        return hotspots
            .OrderByDescending(h => h.Risk)
            .ThenBy(h => h.Source, StringComparer.Ordinal)
            .ToList();
    }

    private static string DisplayName(string id, CouplingNode? node)
    {
        if (node is not null)
            return string.IsNullOrEmpty(node.Namespace) ? node.Name : node.Namespace + "." + node.Name;
        return id.StartsWith("T:", StringComparison.Ordinal) ? id.Substring(2) : id;
    }

    private static string BuildReason(CouplingEdge edge, DependencyKind kind)
    {
        var distance = DistanceLabel(edge.Distance);
        var volatility = double.IsNaN(edge.Volatility)
            ? "volatility unknown (structural risk used)"
            : $"volatility {edge.Volatility:0.00}";
        return $"{kind} dependency across {distance}; strength {edge.Strength:0.00}; {volatility}.";
    }

    private static string BuildSuggestion(DependencyKind kind) => kind switch
    {
        DependencyKind.FieldType or DependencyKind.PropertyType or DependencyKind.ConstructorParameter =>
            "Depend on an abstraction (interface) instead of the concrete type, and inject it.",
        DependencyKind.ObjectCreation =>
            "Avoid creating the concrete type directly; resolve it via DI or a factory.",
        DependencyKind.Inheritance =>
            "Reconsider the inheritance relationship; prefer composition across boundaries.",
        _ => "Reduce coupling to this target or move it closer to its consumer.",
    };

    private static string DistanceLabel(double distance) => distance switch
    {
        >= 0.85 => "an external assembly",
        >= 0.75 => "a shared library",
        >= 0.65 => "a different project",
        >= 0.40 => "the same project",
        >= 0.25 => "the same namespace",
        _ => "a close boundary",
    };

    private static IReadOnlyList<string> RelevantFiles(CouplingNode? source, CouplingNode? target)
    {
        var files = new List<string>();
        if (!string.IsNullOrEmpty(source?.FilePath)) files.Add(source!.FilePath!);
        if (!string.IsNullOrEmpty(target?.FilePath) && target!.FilePath != source?.FilePath) files.Add(target.FilePath!);
        return files;
    }
}
