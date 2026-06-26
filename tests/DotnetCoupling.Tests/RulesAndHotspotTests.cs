using DotnetCoupling.Model;
using DotnetCoupling.Rules;
using DotnetCoupling.Scoring;

namespace DotnetCoupling.Tests;

public class RulesAndHotspotTests
{
    private const string DataSource = @"
namespace Shop.Data
{
    public interface IUserRepository { User GetById(int id); }
    public class User { public string Name { get; set; } }
    public class SqlUserRepository : IUserRepository
    {
        public User GetById(int id) { return new User(); }
    }
}";

    private const string AppSource = @"
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

    private static CouplingGraph CanonicalGraph()
    {
        var data = TestCompiler.Compile("Shop.Data", DataSource);
        var app = TestCompiler.Compile("Shop.App", AppSource, data.ToMetadataReference());
        return TestCompiler.BuildGraph(("Shop.Data", data), ("Shop.App", app));
    }

    [Fact]
    public void ConcreteDependency_OnImplementedType_IsFlagged()
    {
        var violations = RuleEngine.CreateDefault().Evaluate(CanonicalGraph(), new RuleOptions());

        var concrete = violations.Where(v => v.RuleId == "concreteDependency").ToList();
        Assert.Contains(concrete, v =>
            v.SourceId == "T:Shop.App.UserController" && v.TargetId == "T:Shop.Data.SqlUserRepository");
        Assert.All(concrete, v => Assert.Equal(RuleSeverity.Warning, v.Severity));
    }

    [Fact]
    public void Hotspot_CanonicalCrossProjectField_IsSurfaced()
    {
        var hotspots = new HotspotExtractor().Extract(CanonicalGraph());

        var top = hotspots.FirstOrDefault();
        Assert.NotNull(top);
        Assert.Equal("Shop.App.UserController", top!.Source);
        Assert.Equal("Shop.Data.SqlUserRepository", top.Target);
        Assert.Equal(0.85, top.Strength, 3);
        Assert.True(top.Risk >= 0.60); // structural risk (volatility 未配線) でも閾値を満たす
    }

    [Fact]
    public void LayerViolation_DomainToInfrastructure_IsError()
    {
        const string source = @"
namespace MyApp.Infrastructure { public class Db { } }
namespace MyApp.Domain { public class Entity { private MyApp.Infrastructure.Db _db; } }";
        var graph = TestCompiler.BuildGraph(("MyApp", TestCompiler.Compile("MyApp", source)));

        var options = new RuleOptions
        {
            Layers = new LayerArchitecture(new[]
            {
                new LayerDefinition("Domain", new[] { "*.Domain" }, Array.Empty<string>()),
                new LayerDefinition("Infrastructure", new[] { "*.Infrastructure" }, new[] { "Domain" }),
            }),
        };

        var violations = new LayerViolationRule().Evaluate(graph, options).ToList();
        Assert.Contains(violations, v =>
            v.SourceId == "T:MyApp.Domain.Entity" && v.TargetId == "T:MyApp.Infrastructure.Db");
        Assert.All(violations, v => Assert.Equal(RuleSeverity.Error, v.Severity));
    }

    [Fact]
    public void CircularDependency_BetweenTwoTypes_IsDetected()
    {
        const string source = @"
namespace Cyc
{
    public class A { private B _b; }
    public class B { private A _a; }
}";
        var graph = TestCompiler.BuildGraph(("Cyc", TestCompiler.Compile("Cyc", source)));

        var violations = new CircularDependencyRule().Evaluate(graph, new RuleOptions()).ToList();
        var cycle = Assert.Single(violations);
        Assert.Equal("circularDependency", cycle.RuleId);
        Assert.NotNull(cycle.Cycle);
        Assert.Equal(2, cycle.Cycle!.Count);
        Assert.Contains("T:Cyc.A", cycle.Cycle);
        Assert.Contains("T:Cyc.B", cycle.Cycle);
    }

    [Fact]
    public void NoCycle_ProducesNoCircularViolation()
    {
        var violations = new CircularDependencyRule().Evaluate(CanonicalGraph(), new RuleOptions());
        Assert.Empty(violations);
    }
}
