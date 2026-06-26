using LibGit2Sharp;

namespace DotnetCoupling.Git;

/// <summary>開始パスから Git リポジトリのルートを探す。無ければ null。</summary>
public static class GitRepositoryDetector
{
    public static string? Discover(string startPath)
    {
        var probe = File.Exists(startPath) ? Path.GetDirectoryName(startPath) : startPath;
        if (string.IsNullOrEmpty(probe))
            return null;

        var gitDir = Repository.Discover(probe!);
        if (string.IsNullOrEmpty(gitDir))
            return null;

        using var repo = new Repository(gitDir);
        // bare リポジトリは作業ツリーが無いので対象外。
        return repo.Info.WorkingDirectory;
    }
}
