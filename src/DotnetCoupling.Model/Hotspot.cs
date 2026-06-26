namespace DotnetCoupling.Model;

/// <summary>優先的に確認・改善すべき依存（docs/scoring.md S6）。</summary>
public sealed record Hotspot(
    string Source,
    string Target,
    DependencyKind Kind,
    double Strength,
    double Distance,
    double Volatility,
    double Risk,
    string Grade,
    string Reason,
    string Suggestion,
    IReadOnlyList<string> RelevantFiles);
