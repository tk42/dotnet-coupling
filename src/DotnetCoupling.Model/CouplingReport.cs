namespace DotnetCoupling.Model;

/// <summary>解析結果の中心。Console / Json / Web / AI / SARIF はすべてここから派生する。</summary>
public sealed record CouplingReport(
    AnalysisMetadata Metadata,
    ReportSummary Summary,
    CouplingGraph Graph,
    IReadOnlyList<Hotspot> Hotspots,
    IReadOnlyList<RuleViolation> Violations);

/// <summary>リポジトリ全体の集約値（docs/scoring.md S2）。</summary>
public sealed record ReportSummary(
    int Score,
    string Grade,
    int Projects,
    int Types,
    int Edges,
    int HotspotCount,
    int RuleViolations,
    int CircularDependencies);
