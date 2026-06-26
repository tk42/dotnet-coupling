using DotnetCoupling.Model;

namespace DotnetCoupling.Scoring;

/// <summary>
/// グラフのエッジに volatility を適用して risk を再計算する（docs/scoring.md S5）。
/// volatility は「依存先(target)の変更されやすさ」なので、target ノードのファイルの
/// 変更回数を用いる。Git 非依存の純ロジックなので fake な変更回数辞書でテストできる。
/// </summary>
public sealed class VolatilityEnricher
{
    /// <param name="changeCountsByFile">ファイルパス → 直近窓の変更回数。</param>
    public CouplingGraph Apply(
        CouplingGraph graph,
        IReadOnlyDictionary<string, int> changeCountsByFile,
        int fullScale = VolatilityCalculator.DefaultFullScale)
    {
        // パスの表記ゆれを吸収して正規化キーで引けるようにする。
        var normalized = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var kv in changeCountsByFile)
            normalized[Normalize(kv.Key)] = kv.Value;

        var edges = new List<CouplingEdge>(graph.Edges.Count);
        foreach (var edge in graph.Edges)
        {
            var volatility = VolatilityCalculator.Unknown;
            var targetFile = graph.FindNode(edge.TargetId)?.FilePath;
            if (!string.IsNullOrEmpty(targetFile)
                && normalized.TryGetValue(Normalize(targetFile!), out var count))
            {
                volatility = VolatilityCalculator.FromChangeCount(count, fullScale);
            }

            var risk = RiskScorer.Risk(edge.Strength, edge.Distance, volatility);
            edges.Add(edge with { Volatility = volatility, Risk = risk });
        }

        return new CouplingGraph(graph.Nodes, edges);
    }

    private static string Normalize(string path)
    {
        try
        {
            return Path.GetFullPath(path);
        }
        catch
        {
            return path;
        }
    }
}
