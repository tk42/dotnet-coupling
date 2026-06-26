using DotnetCoupling.Model;
using DotnetCoupling.Scoring;

namespace DotnetCoupling.Tests;

// Git 非依存。fake な変更回数辞書で volatility 適用と risk 再計算のみ検証する
// （実 Git 読み取り = FileVolatilityAnalyzer は互換マシンの E2E で確認する）。
public class VolatilityEnricherTests
{
    private static CouplingGraph CanonicalGraph()
    {
        const string dataSource = @"
namespace Shop.Data
{
    public interface IUserRepository { User GetById(int id); }
    public class User { public string Name { get; set; } }
    public class SqlUserRepository : IUserRepository { public User GetById(int id) { return new User(); } }
}";
        const string appSource = @"
using Shop.Data;
namespace Shop.App
{
    public class UserController
    {
        private readonly SqlUserRepository _repository;
        public UserController() { _repository = new SqlUserRepository(); }
        public string GetUserName(int id) { return _repository.GetById(id).Name; }
    }
}";
        var data = TestCompiler.Compile("Shop.Data", dataSource);
        var app = TestCompiler.Compile("Shop.App", appSource, data.ToMetadataReference());
        return TestCompiler.BuildGraph(("Shop.Data", data), ("Shop.App", app));
    }

    private static CouplingEdge CanonicalEdge(CouplingGraph graph) =>
        graph.Edges.First(e =>
            e.SourceId == "T:Shop.App.UserController" && e.TargetId == "T:Shop.Data.SqlUserRepository");

    [Fact]
    public void Apply_SetsTargetVolatilityAndRecomputesRisk()
    {
        var graph = CanonicalGraph();

        // SqlUserRepository は Shop.Data.cs（target のファイル）。変更回数 10 → volatility 1.0。
        var counts = new Dictionary<string, int> { ["Shop.Data.cs"] = 10 };
        var enriched = new VolatilityEnricher().Apply(graph, counts);

        var edge = CanonicalEdge(enriched);
        Assert.True(edge.HasVolatility);
        Assert.Equal(1.0, edge.Volatility, 6);
        // risk = 0.85 × (0.5+0.5·0.65) × (0.5+0.5·1.0) = 0.85 × 0.825 × 1.0
        Assert.Equal(0.85 * 0.825, edge.Risk, 6);
        Assert.True(edge.Risk >= 0.60);
    }

    [Fact]
    public void Apply_UnknownFile_LeavesVolatilityUnknown()
    {
        var graph = CanonicalGraph();
        var before = CanonicalEdge(graph);

        var enriched = new VolatilityEnricher().Apply(graph, new Dictionary<string, int>());
        var after = CanonicalEdge(enriched);

        Assert.True(double.IsNaN(after.Volatility));
        Assert.Equal(before.Risk, after.Risk, 6); // unknown → risk 変化なし
    }

    [Fact]
    public void Apply_StructuralRiskIsPreserved()
    {
        var graph = CanonicalGraph();
        var counts = new Dictionary<string, int> { ["Shop.Data.cs"] = 3 };
        var enriched = new VolatilityEnricher().Apply(graph, counts);

        // structuralRisk は volatility 非依存なので不変。
        Assert.Equal(0.85 * 0.825, CanonicalEdge(enriched).StructuralRisk, 6);
    }
}
