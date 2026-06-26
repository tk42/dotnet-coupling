using DotnetCoupling.Model;
using DotnetCoupling.Scoring;
using Microsoft.CodeAnalysis;

namespace DotnetCoupling.Analysis;

/// <summary>解析対象の 1 プロジェクト = 1 コンパイル。</summary>
public sealed record ProjectCompilation(string ProjectName, Compilation Compilation);

/// <summary>
/// 複数プロジェクトのコンパイルから型レベルの依存グラフを構築する。
/// strength/distance/risk は docs/scoring.md に従って付与する。volatility は未配線のため
/// 現状 unknown(NaN)。Git 連携で後から埋める。
/// </summary>
public sealed class SemanticGraphBuilder
{
    private static readonly SymbolDisplayFormat IdFormat = new(
        typeQualificationStyle: SymbolDisplayTypeQualificationStyle.NameAndContainingTypesAndNamespaces,
        globalNamespaceStyle: SymbolDisplayGlobalNamespaceStyle.Omitted);

    private const double SemanticConfidence = 1.0;

    public CouplingGraph Build(IReadOnlyList<ProjectCompilation> projects, CancellationToken ct = default)
    {
        var assemblyToProject = projects
            .GroupBy(p => p.Compilation.Assembly.Name)
            .ToDictionary(g => g.Key, g => g.First().ProjectName);
        var knownAssemblies = new HashSet<string>(assemblyToProject.Keys);

        var nodes = new Dictionary<string, CouplingNode>();
        var pairs = new Dictionary<(string, string), EdgeAccumulator>();

        var walker = new CouplingWalker();
        foreach (var project in projects)
        {
            ct.ThrowIfCancellationRequested();
            foreach (var dep in walker.Walk(project.Compilation, ct))
            {
                var sourceId = TypeId(dep.Source);
                var targetId = TypeId(dep.Target);

                nodes[sourceId] = NodeFor(dep.Source, sourceId, assemblyToProject);
                nodes[targetId] = NodeFor(dep.Target, targetId, assemblyToProject);

                if (!pairs.TryGetValue((sourceId, targetId), out var acc))
                {
                    acc = new EdgeAccumulator(dep.Source, dep.Target);
                    pairs[(sourceId, targetId)] = acc;
                }
                acc.Occurrences.Add(new Occurrence(dep.Kind, dep.FilePath, dep.Line));
            }
        }

        var edges = new List<CouplingEdge>(pairs.Count);
        foreach (var pair in pairs)
        {
            var (sourceId, targetId) = pair.Key;
            var acc = pair.Value;
            var strength = IntegrationStrengthCalculator.ForEdge(acc.Occurrences);
            var distance = DistanceCalculator.Normalize(ResolveBucket(acc.Source, acc.Target, knownAssemblies));
            var volatility = VolatilityCalculator.Unknown;
            var risk = RiskScorer.Risk(strength, distance, volatility);
            var structuralRisk = RiskScorer.StructuralRisk(strength, distance);

            // 代表 kind（最も強い出現）を先頭に並べておく。
            var ordered = acc.Occurrences
                .OrderByDescending(o => IntegrationStrengthCalculator.ForKind(o.Kind))
                .ToList();

            edges.Add(new CouplingEdge(
                sourceId, targetId, strength, distance, volatility, risk, structuralRisk,
                SemanticConfidence, ordered));
        }

        return new CouplingGraph(nodes.Values, edges);
    }

    private static DistanceBucket ResolveBucket(
        INamedTypeSymbol source, INamedTypeSymbol target, ISet<string> knownAssemblies)
    {
        if (SymbolEqualityComparer.Default.Equals(source, target))
            return DistanceBucket.SameType;

        if (SymbolEqualityComparer.Default.Equals(source.ContainingAssembly, target.ContainingAssembly))
        {
            var sameNamespace = NamespaceOf(source) == NamespaceOf(target);
            return sameNamespace ? DistanceBucket.SameNamespace : DistanceBucket.SameProject;
        }

        var targetAssembly = target.ContainingAssembly?.Name ?? string.Empty;
        return knownAssemblies.Contains(targetAssembly)
            ? DistanceBucket.DifferentProjectSameSolution
            : DistanceBucket.ExternalAssembly;
    }

    private static CouplingNode NodeFor(
        INamedTypeSymbol type, string id, IReadOnlyDictionary<string, string> assemblyToProject)
    {
        var assembly = type.ContainingAssembly?.Name;
        var projectName = assembly is not null && assemblyToProject.TryGetValue(assembly, out var p) ? p : null;
        return new CouplingNode(
            id,
            type.Name,
            NodeKind.Type,
            projectName,
            NamespaceOf(type),
            assembly,
            FilePathOf(type));
    }

    private static string NamespaceOf(INamedTypeSymbol type) =>
        type.ContainingNamespace is { IsGlobalNamespace: false } ns ? ns.ToDisplayString() : string.Empty;

    private static string? FilePathOf(INamedTypeSymbol type) =>
        type.Locations.FirstOrDefault(l => l.IsInSource)?.GetLineSpan().Path;

    private static string TypeId(INamedTypeSymbol type) => "T:" + type.ToDisplayString(IdFormat);

    private sealed class EdgeAccumulator
    {
        public EdgeAccumulator(INamedTypeSymbol source, INamedTypeSymbol target)
        {
            Source = source;
            Target = target;
        }

        public INamedTypeSymbol Source { get; }
        public INamedTypeSymbol Target { get; }
        public List<Occurrence> Occurrences { get; } = new();
    }
}
