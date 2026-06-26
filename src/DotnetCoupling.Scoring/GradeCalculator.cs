namespace DotnetCoupling.Scoring;

/// <summary>Score → Grade（docs/scoring.md S2）。</summary>
public static class GradeCalculator
{
    public static string FromScore(int score) =>
        score >= 90 ? "A" :
        score >= 75 ? "B" :
        score >= 60 ? "C" :
        score >= 40 ? "D" : "F";
}
