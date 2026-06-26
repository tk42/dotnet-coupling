using DotnetCoupling.Model;

namespace DotnetCoupling.Rules;

/// <summary>
/// 型レベルの循環依存を検出する。Tarjan の強連結成分(SCC)で、サイズ 2 以上の成分を循環とみなす。
/// </summary>
public sealed class CircularDependencyRule : IArchitectureRule
{
    public string RuleId => "circularDependency";

    public IEnumerable<RuleViolation> Evaluate(CouplingGraph graph, RuleOptions options)
    {
        var severity = options.SeverityOf(RuleId, RuleSeverity.Error);

        var adjacency = BuildAdjacency(graph);
        foreach (var scc in StronglyConnectedComponents(adjacency))
        {
            if (scc.Count < 2)
                continue;

            var cycle = scc.Select(Short).ToList();
            yield return new RuleViolation(
                RuleId,
                severity,
                "Circular dependency among: " + string.Join(" -> ", cycle) + ".",
                SourceId: scc[0],
                TargetId: scc[scc.Count - 1],
                FilePath: null,
                Line: null,
                Cycle: scc);
        }
    }

    private static Dictionary<string, List<string>> BuildAdjacency(CouplingGraph graph)
    {
        var adjacency = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        foreach (var edge in graph.Edges)
        {
            if (edge.SourceId == edge.TargetId)
                continue;
            if (!adjacency.TryGetValue(edge.SourceId, out var list))
                adjacency[edge.SourceId] = list = new List<string>();
            if (!list.Contains(edge.TargetId))
                list.Add(edge.TargetId);
        }
        return adjacency;
    }

    // 反復版 Tarjan（深い再帰でのスタックオーバーフローを避ける）。
    private static List<List<string>> StronglyConnectedComponents(Dictionary<string, List<string>> adjacency)
    {
        var index = new Dictionary<string, int>(StringComparer.Ordinal);
        var lowlink = new Dictionary<string, int>(StringComparer.Ordinal);
        var onStack = new HashSet<string>(StringComparer.Ordinal);
        var stack = new Stack<string>();
        var result = new List<List<string>>();
        var next = 0;

        foreach (var start in adjacency.Keys)
        {
            if (index.ContainsKey(start))
                continue;

            var work = new Stack<(string Node, int ChildIndex)>();
            work.Push((start, 0));

            while (work.Count > 0)
            {
                var (node, childIndex) = work.Pop();

                if (childIndex == 0)
                {
                    index[node] = lowlink[node] = next++;
                    stack.Push(node);
                    onStack.Add(node);
                }

                var children = adjacency.TryGetValue(node, out var c) ? c : null;
                var advanced = false;
                if (children is not null)
                {
                    for (var i = childIndex; i < children.Count; i++)
                    {
                        var child = children[i];
                        if (!index.ContainsKey(child))
                        {
                            work.Push((node, i + 1));
                            work.Push((child, 0));
                            advanced = true;
                            break;
                        }
                        if (onStack.Contains(child))
                            lowlink[node] = Math.Min(lowlink[node], index[child]);
                    }
                }

                if (advanced)
                    continue;

                // ノードの子を処理し終えた: 親の lowlink を更新し、root なら SCC を確定。
                if (work.Count > 0)
                {
                    var parent = work.Peek().Node;
                    lowlink[parent] = Math.Min(lowlink[parent], lowlink[node]);
                }

                if (lowlink[node] == index[node])
                {
                    var component = new List<string>();
                    string popped;
                    do
                    {
                        popped = stack.Pop();
                        onStack.Remove(popped);
                        component.Add(popped);
                    }
                    while (popped != node);
                    result.Add(component);
                }
            }
        }

        return result;
    }

    private static string Short(string id) =>
        id.StartsWith("T:", StringComparison.Ordinal) ? id.Substring(2) : id;
}
