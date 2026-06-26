namespace DotnetCoupling.Scoring;

/// <summary>
/// 構造的距離の分類（docs/scoring.md S4）。
/// レイヤー越境は distance に固定値を入れず Rule（LayerViolationRule）で扱う（レビュー C4）。
/// </summary>
public enum DistanceBucket
{
    SameType,
    SameFile,
    SameNamespace,
    SameProject,
    DifferentProjectSameSolution,
    SharedLibrary,
    ExternalAssembly
}

/// <summary>距離の正規化（docs/scoring.md S4）。</summary>
public static class DistanceCalculator
{
    public static double Normalize(DistanceBucket bucket) => bucket switch
    {
        DistanceBucket.SameType => 0.00,
        DistanceBucket.SameFile => 0.10,
        DistanceBucket.SameNamespace => 0.25,
        DistanceBucket.SameProject => 0.40,
        DistanceBucket.DifferentProjectSameSolution => 0.65,
        DistanceBucket.SharedLibrary => 0.75,
        DistanceBucket.ExternalAssembly => 0.85,
        _ => 0.65
    };

    /// <summary>
    /// レイヤー方向の論理補正。最大 +0.10 までで越境固定はしない（docs/scoring.md S4）。
    /// </summary>
    public static double WithLayerAdjustment(double distance, double layerAdjustment)
        => Math.Min(1.0, distance + Math.Max(0.0, Math.Min(0.10, layerAdjustment)));
}
