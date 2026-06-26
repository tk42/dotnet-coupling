namespace DotnetCoupling.Model;

/// <summary>
/// 依存関係。Strength は束ねた Occurrences の最大 kind strength（docs/scoring.md S3）。
/// Volatility が不明な場合は <see cref="double.NaN"/>。
/// </summary>
public sealed record CouplingEdge(
    string SourceId,
    string TargetId,
    double Strength,
    double Distance,
    double Volatility,
    double Risk,
    double StructuralRisk,
    double Confidence,
    IReadOnlyList<Occurrence> Occurrences)
{
    /// <summary>このエッジを代表する依存種別（最も強い出現）。</summary>
    public DependencyKind PrimaryKind =>
        Occurrences.Count == 0 ? DependencyKind.UsingDirective : Occurrences[0].Kind;

    public bool HasVolatility => !double.IsNaN(Volatility);
}
