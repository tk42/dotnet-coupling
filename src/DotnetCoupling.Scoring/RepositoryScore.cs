namespace DotnetCoupling.Scoring;

/// <summary>
/// リポジトリ全体スコアの集約（docs/scoring.md S2）。
/// 単純平均は大量の自然な弱依存に薄められて鈍感化するため、risk 上位 20% の平均（CVaR 的）を採る。
/// </summary>
public static class RepositoryScore
{
    public sealed record Result(double MeanRisk, int Score, string Grade);

    public const double DefaultTailFraction = 0.20;
    public const int MinEdgesForTail = 5;

    public static Result Aggregate(IEnumerable<double> risks, double tailFraction = DefaultTailFraction)
    {
        var all = risks.Where(r => !double.IsNaN(r)).ToList();
        if (all.Count == 0)
            return new Result(0.0, 100, "A");

        double meanRisk;
        if (all.Count < MinEdgesForTail)
        {
            meanRisk = all.Average();
        }
        else
        {
            var k = Math.Max(1, (int)Math.Ceiling(all.Count * tailFraction));
            meanRisk = all.OrderByDescending(r => r).Take(k).Average();
        }

        var score = RiskScorer.ScoreFromRisk(meanRisk);
        return new Result(meanRisk, score, GradeCalculator.FromScore(score));
    }
}
