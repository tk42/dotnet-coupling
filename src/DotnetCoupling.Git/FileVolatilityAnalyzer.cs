using LibGit2Sharp;

namespace DotnetCoupling.Git;

/// <summary>
/// Git 履歴からファイル単位の変更回数を集計する（docs/scoring.md S5、直近窓）。
/// 返すキーは作業ツリー基準の絶対パス。
/// 注: LibGit2Sharp の実行検証は互換マシンの E2E で行う（本機では未実行）。
/// </summary>
public sealed class FileVolatilityAnalyzer
{
    /// <param name="workingDirectory">リポジトリの作業ツリー（<see cref="GitRepositoryDetector.Discover"/> の戻り値）。</param>
    /// <param name="since">この日時以降のコミットを集計対象にする（例: 90 日前）。</param>
    public IReadOnlyDictionary<string, int> ChangeCountsByFile(string workingDirectory, DateTimeOffset since)
    {
        var counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        using var repo = new Repository(workingDirectory);

        foreach (var commit in repo.Commits)
        {
            if (commit.Author.When < since)
                break; // Commits は新しい順。窓の外に出たら打ち切り。

            var parent = commit.Parents.FirstOrDefault();
            var changes = parent is null
                ? repo.Diff.Compare<TreeChanges>(null, commit.Tree)
                : repo.Diff.Compare<TreeChanges>(parent.Tree, commit.Tree);

            foreach (var change in changes)
            {
                var absolute = Path.GetFullPath(Path.Combine(workingDirectory, change.Path));
                counts.TryGetValue(absolute, out var current);
                counts[absolute] = current + 1;
            }
        }

        return counts;
    }
}
