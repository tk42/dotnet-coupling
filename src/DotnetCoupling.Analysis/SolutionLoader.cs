using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.MSBuild;

namespace DotnetCoupling.Analysis;

/// <summary>
/// ロード済みソリューション。Roslyn の <see cref="MSBuildWorkspace"/> を保持するため
/// 利用後は Dispose する。
/// </summary>
public sealed class LoadedSolution : IDisposable
{
    private readonly MSBuildWorkspace _workspace;

    internal LoadedSolution(MSBuildWorkspace workspace, Solution solution, IReadOnlyList<string> warnings)
    {
        _workspace = workspace;
        Solution = solution;
        Projects = solution.Projects.ToList();
        Warnings = warnings;
    }

    public Solution Solution { get; }

    public IReadOnlyList<Project> Projects { get; }

    /// <summary>部分的ロード失敗などの警告（§26.3: 可能な範囲で継続）。</summary>
    public IReadOnlyList<string> Warnings { get; }

    public void Dispose() => _workspace.Dispose();
}

/// <summary>
/// MSBuildWorkspace で .sln / .csproj をロードする。呼び出し前に
/// <see cref="MsBuildRegistrar.EnsureRegistered"/> を済ませておくこと。
/// </summary>
public sealed class SolutionLoader
{
    public async Task<LoadedSolution> LoadSolutionAsync(string solutionPath, CancellationToken ct = default)
    {
        var warnings = new List<string>();
        var workspace = MSBuildWorkspace.Create();
        workspace.WorkspaceFailed += (_, e) =>
            warnings.Add($"[{e.Diagnostic.Kind}] {e.Diagnostic.Message}");

        var solution = await workspace
            .OpenSolutionAsync(solutionPath, cancellationToken: ct)
            .ConfigureAwait(false);

        return new LoadedSolution(workspace, solution, warnings);
    }
}
