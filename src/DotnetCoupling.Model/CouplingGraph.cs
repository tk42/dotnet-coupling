namespace DotnetCoupling.Model;

/// <summary>ノードとエッジの集合。Id でノードを引ける。</summary>
public sealed class CouplingGraph
{
    private readonly Dictionary<string, CouplingNode> _nodes;

    public CouplingGraph(IEnumerable<CouplingNode> nodes, IEnumerable<CouplingEdge> edges)
    {
        _nodes = nodes.ToDictionary(n => n.Id);
        Edges = edges.ToList();
    }

    public IReadOnlyCollection<CouplingNode> Nodes => _nodes.Values;

    public IReadOnlyList<CouplingEdge> Edges { get; }

    // net472 には Dictionary.GetValueOrDefault が無いため TryGetValue を使う。
    public CouplingNode? FindNode(string id) =>
        _nodes.TryGetValue(id, out var node) ? node : null;
}
