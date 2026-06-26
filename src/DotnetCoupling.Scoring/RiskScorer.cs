namespace DotnetCoupling.Scoring;

/// <summary>
/// Edge Risk（docs/scoring.md S1）。distance / volatility は softening して適用する
/// （0 でも係数 0.5 を残す）。看板例 S=0.85, D=0.65, V=0.72 → 0.603。
/// </summary>
public static class RiskScorer
{
    /// <summary>risk = strength × (0.5 + 0.5·distance) × (0.5 + 0.5·volatility)。</summary>
    public static double Risk(double strength, double distance, double volatility)
    {
        var v = double.IsNaN(volatility) ? 0.0 : volatility;   // unknown は 0 相当
        return strength * (0.5 + 0.5 * distance) * (0.5 + 0.5 * v);
    }

    /// <summary>volatility を含まない構造 risk。CI ゲートの一次判定に使う（docs/scoring.md S1/S3）。</summary>
    public static double StructuralRisk(double strength, double distance)
        => strength * (0.5 + 0.5 * distance);

    /// <summary>risk → 0〜100 スコア（docs/scoring.md S2）。</summary>
    public static int ScoreFromRisk(double risk)
        => (int)Math.Round(100.0 * (1.0 - risk), MidpointRounding.AwayFromZero);
}
