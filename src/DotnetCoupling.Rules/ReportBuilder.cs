using DotnetCoupling.Model;
using DotnetCoupling.Scoring;

namespace DotnetCoupling.Rules;

/// <summary>
/// グラフ + ルール + hotspot + 集約サマリを束ねて <see cref="CouplingReport"/> を作る。
/// Console / JSON / AI / SARIF はすべてこの 1 つの成果物から派生する。
/// </summary>
public sealed class ReportBuilder
{
    private readonly RuleEngine _ruleEngine;
    private readonly HotspotExtractor _hotspotExtractor;

    public ReportBuilder(RuleEngine? ruleEngine = null, HotspotExtractor? hotspotExtractor = null)
    {
        _ruleEngine = ruleEngine ?? RuleEngine.CreateDefault();
        _hotspotExtractor = hotspotExtractor ?? new HotspotExtractor();
    }

    public CouplingReport Build(CouplingGraph graph, AnalysisMetadata metadata, RuleOptions? options = null)
    {
        options ??= new RuleOptions();
        var violations = _ruleEngine.Evaluate(graph, options);
        var hotspots = _hotspotExtractor.Extract(graph);
        var summary = BuildSummary(graph, hotspots, violations);
        return new CouplingReport(metadata, summary, graph, hotspots, violations);
    }

    private static ReportSummary BuildSummary(
        CouplingGraph graph, IReadOnlyList<Hotspot> hotspots, IReadOnlyList<RuleViolation> violations)
    {
        // volatility 未配線のエッジは structuralRisk で代用して集約する（docs/scoring.md S2/S3）。
        var risks = graph.Edges.Select(e => double.IsNaN(e.Volatility) ? e.StructuralRisk : e.Risk);
        var aggregate = RepositoryScore.Aggregate(risks);

        var projects = graph.Nodes
            .Where(n => !string.IsNullOrEmpty(n.ProjectName))
            .Select(n => n.ProjectName!)
            .Distinct(StringComparer.Ordinal)
            .Count();
        var types = graph.Nodes.Count(n => n.Kind == NodeKind.Type);
        var circular = violations.Count(v => v.RuleId == "circularDependency");

        return new ReportSummary(
            aggregate.Score,
            aggregate.Grade,
            projects,
            types,
            graph.Edges.Count,
            hotspots.Count,
            violations.Count,
            circular);
    }
}
