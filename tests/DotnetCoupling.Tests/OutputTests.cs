using System.Text.Json;
using DotnetCoupling.Model;
using DotnetCoupling.Output;
using DotnetCoupling.Rules;

namespace DotnetCoupling.Tests;

public class OutputTests
{
    private static CouplingReport CanonicalReport()
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
        var graph = TestCompiler.BuildGraph(("Shop.Data", data), ("Shop.App", app));

        var metadata = new AnalysisMetadata(
            AnalysisMode.Semantic, ConfidenceLevel.High, "Shop.sln", "0.1.0",
            new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));

        return new ReportBuilder().Build(graph, metadata);
    }

    [Fact]
    public void ReportSummary_HasGradeAndProjectsAndHotspots()
    {
        var report = CanonicalReport();
        Assert.Equal(2, report.Summary.Projects);
        Assert.True(report.Summary.HotspotCount >= 1);
        Assert.Equal(0, report.Summary.CircularDependencies);
        Assert.False(string.IsNullOrEmpty(report.Summary.Grade));
    }

    [Fact]
    public void Json_IsValid_AndConvertsUnknownVolatilityToNull()
    {
        var json = new JsonReporter().Serialize(CanonicalReport());

        using var doc = JsonDocument.Parse(json); // 妥当な JSON であること
        var root = doc.RootElement;

        Assert.Equal("dotnet-coupling", root.GetProperty("metadata").GetProperty("tool").GetString());
        Assert.True(root.GetProperty("hotspots").GetArrayLength() >= 1);

        // volatility 未配線 → JSON では null。
        var firstEdge = root.GetProperty("edges")[0];
        Assert.Equal(JsonValueKind.Null, firstEdge.GetProperty("volatility").ValueKind);
    }

    [Fact]
    public void Console_Summary_ContainsGradeLine()
    {
        using var sw = new StringWriter();
        new ConsoleReporter().WriteSummary(CanonicalReport(), sw);
        var text = sw.ToString();

        Assert.Contains("Overall Grade:", text);
        Assert.Contains("Hotspots:", text);
    }

    [Fact]
    public void Console_Hotspots_ListsCanonicalDependency()
    {
        using var sw = new StringWriter();
        new ConsoleReporter().WriteHotspots(CanonicalReport(), sw);
        var text = sw.ToString();

        Assert.Contains("Shop.App.UserController", text);
        Assert.Contains("Shop.Data.SqlUserRepository", text);
    }

    [Fact]
    public void Markdown_ContainsSummaryAndHotspot()
    {
        var md = new MarkdownReporter().Serialize(CanonicalReport());
        Assert.Contains("# dotnet-coupling Report", md);
        Assert.Contains("## Summary", md);
        Assert.Contains("## Hotspots", md);
        Assert.Contains("Shop.App.UserController", md);
    }
}
