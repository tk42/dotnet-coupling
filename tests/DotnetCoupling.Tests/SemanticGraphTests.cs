using DotnetCoupling.Analysis;
using DotnetCoupling.Model;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace DotnetCoupling.Tests;

public class SemanticGraphTests
{
    private static readonly MetadataReference[] CoreReferences =
    {
        MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
        MetadataReference.CreateFromFile(typeof(Console).Assembly.Location),
        MetadataReference.CreateFromFile(typeof(Enumerable).Assembly.Location),
    };

    private static CSharpCompilation Compile(string assemblyName, string source, params MetadataReference[] extra)
    {
        var tree = CSharpSyntaxTree.ParseText(source, path: assemblyName + ".cs");
        return CSharpCompilation.Create(
            assemblyName,
            new[] { tree },
            CoreReferences.Concat(extra),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
    }

    private static CouplingEdge? Edge(CouplingGraph graph, string source, string target) =>
        graph.Edges.FirstOrDefault(e => e.SourceId == "T:" + source && e.TargetId == "T:" + target);

    // 看板例の構造（FieldType 0.85 / 別project 0.65）を、Git 無しの in-memory で再現する。
    [Fact]
    public void CanonicalCrossProjectFieldDependency_HasExpectedStrengthAndDistance()
    {
        const string dataSource = @"
namespace Shop.Data
{
    public interface IUserRepository { User GetById(int id); }
    public class User { public string Name { get; set; } }
    public class SqlUserRepository : IUserRepository
    {
        public User GetById(int id) { return new User(); }
    }
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
        var data = Compile("Shop.Data", dataSource);
        var app = Compile("Shop.App", appSource, data.ToMetadataReference());

        var graph = new SemanticGraphBuilder().Build(new[]
        {
            new ProjectCompilation("Shop.Data", data),
            new ProjectCompilation("Shop.App", app),
        });

        var edge = Edge(graph, "Shop.App.UserController", "Shop.Data.SqlUserRepository");
        Assert.NotNull(edge);
        Assert.Equal(0.85, edge!.Strength, 3);                 // FieldType が最強
        Assert.Equal(0.65, edge.Distance, 3);                  // 別プロジェクト
        Assert.Equal(0.85 * 0.825, edge.StructuralRisk, 6);    // volatility 非依存 = 0.70125
        Assert.True(double.IsNaN(edge.Volatility));            // Git 未配線 → unknown

        // 同一エッジに field / objectCreation / methodCall が束ねられている。
        var kinds = edge.Occurrences.Select(o => o.Kind).ToHashSet();
        Assert.Contains(DependencyKind.FieldType, kinds);
        Assert.Contains(DependencyKind.ObjectCreation, kinds);
        Assert.Contains(DependencyKind.MethodCall, kinds);
    }

    [Fact]
    public void InterfaceImplementation_SameNamespace_IsDetected()
    {
        const string source = @"
namespace Shop.Data
{
    public interface IUserRepository { }
    public class SqlUserRepository : IUserRepository { }
}";
        var graph = new SemanticGraphBuilder().Build(new[]
        {
            new ProjectCompilation("Shop.Data", Compile("Shop.Data", source)),
        });

        var edge = Edge(graph, "Shop.Data.SqlUserRepository", "Shop.Data.IUserRepository");
        Assert.NotNull(edge);
        Assert.Equal(DependencyKind.InterfaceImplementation, edge!.Occurrences[0].Kind);
        Assert.Equal(0.90, edge.Strength, 3);
        Assert.Equal(0.25, edge.Distance, 3);  // 同一 namespace
    }

    [Fact]
    public void Inheritance_DifferentNamespaceSameAssembly_IsSameProjectDistance()
    {
        const string source = @"
namespace Shop.Domain { public class EntityBase { } }
namespace Shop.Model { public class Order : Shop.Domain.EntityBase { } }";
        var graph = new SemanticGraphBuilder().Build(new[]
        {
            new ProjectCompilation("Shop", Compile("Shop", source)),
        });

        var edge = Edge(graph, "Shop.Model.Order", "Shop.Domain.EntityBase");
        Assert.NotNull(edge);
        Assert.Equal(DependencyKind.Inheritance, edge!.Occurrences[0].Kind);
        Assert.Equal(1.00, edge.Strength, 3);
        Assert.Equal(0.40, edge.Distance, 3);  // 同一 assembly・別 namespace → SameProject
    }

    [Fact]
    public void PrimitiveAndSelfReferences_AreNotEdges()
    {
        const string source = @"
namespace Shop { public class A { public string Name; public int Count; public A Next; } }";
        var graph = new SemanticGraphBuilder().Build(new[]
        {
            new ProjectCompilation("Shop", Compile("Shop", source)),
        });

        // string/int(プリミティブ) と自己参照(A->A) はエッジにしない。
        Assert.DoesNotContain(graph.Edges, e => e.SourceId == "T:Shop.A");
    }
}
