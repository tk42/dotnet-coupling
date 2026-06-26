using DotnetCoupling.Model;
using DotnetCoupling.Scoring;

namespace DotnetCoupling.Tests;

public class ScoringTests
{
    // docs/scoring.md S1 の看板例: FieldType(0.85) × 別project(0.65) × volatility 0.72 ≈ 0.603。
    // レビュー C1 の回帰: 看板例が hotspot 閾値 0.60 を満たすことを固定する。
    [Fact]
    public void CanonicalExample_RiskMeetsHotspotThreshold()
    {
        var strength = IntegrationStrengthCalculator.ForKind(DependencyKind.FieldType);
        var distance = DistanceCalculator.Normalize(DistanceBucket.DifferentProjectSameSolution);
        var risk = RiskScorer.Risk(strength, distance, 0.72);

        Assert.Equal(0.85, strength, 3);
        Assert.Equal(0.65, distance, 3);
        Assert.Equal(0.603, risk, 3);
        Assert.True(risk >= 0.60, $"看板例 risk={risk} は hotspot 閾値 0.60 を満たすべき");
    }

    [Theory]
    [InlineData(DependencyKind.Inheritance, 1.00)]
    [InlineData(DependencyKind.InterfaceImplementation, 0.90)]
    [InlineData(DependencyKind.FieldType, 0.85)]
    [InlineData(DependencyKind.MethodCall, 0.50)]
    [InlineData(DependencyKind.Attribute, 0.30)]
    [InlineData(DependencyKind.UsingDirective, 0.10)]
    public void Strength_MatchesTable(DependencyKind kind, double expected)
        => Assert.Equal(expected, IntegrationStrengthCalculator.ForKind(kind), 3);

    [Fact]
    public void EdgeStrength_IsMaxOfOccurrences()
    {
        var occ = new[]
        {
            new Occurrence(DependencyKind.MethodCall),
            new Occurrence(DependencyKind.FieldType),
            new Occurrence(DependencyKind.UsingDirective),
        };
        Assert.Equal(0.85, IntegrationStrengthCalculator.ForEdge(occ), 3);
    }

    [Fact]
    public void StructuralRisk_IgnoresVolatility()
        => Assert.Equal(0.85 * 0.825, RiskScorer.StructuralRisk(0.85, 0.65), 6);

    [Theory]
    [InlineData(0, 0.0)]
    [InlineData(10, 1.0)]
    [InlineData(100, 1.0)]
    public void Volatility_AbsoluteScale(int count, double expected)
        => Assert.Equal(expected, VolatilityCalculator.FromChangeCount(count), 6);

    [Fact]
    public void Volatility_Unknown_TreatedAsZeroInRisk()
    {
        var withUnknown = RiskScorer.Risk(0.85, 0.65, VolatilityCalculator.Unknown);
        var withZero = RiskScorer.Risk(0.85, 0.65, 0.0);
        Assert.Equal(withZero, withUnknown, 6);
    }

    [Theory]
    [InlineData(95, "A")]
    [InlineData(90, "A")]
    [InlineData(89, "B")]
    [InlineData(75, "B")]
    [InlineData(74, "C")]
    [InlineData(60, "C")]
    [InlineData(59, "D")]
    [InlineData(40, "D")]
    [InlineData(39, "F")]
    [InlineData(0, "F")]
    public void Grade_Boundaries(int score, string grade)
        => Assert.Equal(grade, GradeCalculator.FromScore(score));

    [Fact]
    public void RepoScore_UsesTopTailMean_WhenEnoughEdges()
    {
        // 10 件中 8 件 0.0, 2 件 0.8。上位 20% = 2 件 → 平均 0.8 → score 20 → F。
        var risks = new List<double> { 0, 0, 0, 0, 0, 0, 0, 0, 0.8, 0.8 };
        var r = RepositoryScore.Aggregate(risks);
        Assert.Equal(0.8, r.MeanRisk, 6);
        Assert.Equal(20, r.Score);
        Assert.Equal("F", r.Grade);
    }

    [Fact]
    public void RepoScore_UsesAllMean_WhenFewEdges()
    {
        // 2 件 (<5) → 全平均 0.25 → score 75 → B。
        var risks = new List<double> { 0.0, 0.5 };
        var r = RepositoryScore.Aggregate(risks);
        Assert.Equal(0.25, r.MeanRisk, 6);
        Assert.Equal(75, r.Score);
        Assert.Equal("B", r.Grade);
    }

    [Fact]
    public void RepoScore_Empty_IsPerfect()
    {
        var r = RepositoryScore.Aggregate(Array.Empty<double>());
        Assert.Equal(100, r.Score);
        Assert.Equal("A", r.Grade);
    }
}
