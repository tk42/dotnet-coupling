namespace DotnetCoupling.Model;

/// <summary>依存グラフの頂点。Id はグラフ内で一意（例: "T:MyApp.Domain.User"）。</summary>
public sealed record CouplingNode(
    string Id,
    string Name,
    NodeKind Kind,
    string? ProjectName = null,
    string? Namespace = null,
    string? AssemblyName = null,
    string? FilePath = null);
