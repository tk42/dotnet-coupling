using System.Reflection;
using DotnetCoupling.Analysis;
using DotnetCoupling.Git;
using DotnetCoupling.Model;
using DotnetCoupling.Output;
using DotnetCoupling.Rules;
using DotnetCoupling.Scoring;

namespace DotnetCoupling.Cli;

internal sealed class CliOptions
{
    public string SolutionPath = string.Empty;
    public bool Summary;
    public bool Hotspots;
    public bool Violations;
    public bool Verbose;
    public int? Top;
    public string? JsonPath;
    public string? MarkdownPath;
    public bool Check;
    public string MinGrade = "B";
}

/// <summary>引数の簡易パーサ。System.CommandLine への置き換えは後続。</summary>
internal static class ArgParser
{
    public const string Usage =
        "usage: dotnet-coupling <solution-or-project> [options]\n" +
        "  --summary            総合サマリ（既定）\n" +
        "  --hotspots           ホットスポット一覧\n" +
        "  --violations         ルール違反一覧（レイヤー/循環/具象）\n" +
        "  --verbose            サマリ＋ホットスポット＋違反を一括表示\n" +
        "  --top <N>            ホットスポットの表示件数を N 件に制限\n" +
        "  --json <path>        全情報を JSON 出力（機械可読）\n" +
        "  --markdown <path>    人間向け Markdown レポートを出力\n" +
        "  --check              品質ゲート判定（終了コードで合否）\n" +
        "  --min-grade <A..F>   --check の合格ライン（既定 B）";

    public static CliOptions? Parse(string[] args, out string? error)
    {
        error = null;
        if (args.Length == 0)
            return null;

        var options = new CliOptions();
        for (var i = 0; i < args.Length; i++)
        {
            var arg = args[i];
            switch (arg)
            {
                case "--summary": options.Summary = true; break;
                case "--hotspots": options.Hotspots = true; break;
                case "--violations": options.Violations = true; break;
                case "--verbose": options.Verbose = true; break;
                case "--check": options.Check = true; break;
                case "--json":
                    if (++i >= args.Length) { error = "--json requires a path."; return options; }
                    options.JsonPath = args[i];
                    break;
                case "--markdown":
                    if (++i >= args.Length) { error = "--markdown requires a path."; return options; }
                    options.MarkdownPath = args[i];
                    break;
                case "--top":
                    if (++i >= args.Length || !int.TryParse(args[i], out var top))
                    {
                        error = "--top requires an integer.";
                        return options;
                    }
                    options.Top = top;
                    break;
                case "--min-grade":
                    if (++i >= args.Length) { error = "--min-grade requires a value."; return options; }
                    options.MinGrade = args[i];
                    break;
                default:
                    if (arg.StartsWith("--", StringComparison.Ordinal))
                    {
                        error = $"unknown option: {arg}";
                        return options;
                    }
                    options.SolutionPath = arg;
                    break;
            }
        }

        if (string.IsNullOrEmpty(options.SolutionPath))
            error = "no solution or project path specified.";
        else if (!File.Exists(options.SolutionPath))
            error = $"path not found: {options.SolutionPath}";

        return options;
    }
}

internal static class CliRunner
{
    public static async Task<int> RunAsync(CliOptions opt)
    {
        using var loaded = await new SolutionLoader().LoadSolutionAsync(opt.SolutionPath).ConfigureAwait(false);

        var projects = new List<ProjectCompilation>();
        foreach (var project in loaded.Projects)
        {
            var compilation = await project.GetCompilationAsync().ConfigureAwait(false);
            if (compilation is not null)
                projects.Add(new ProjectCompilation(project.Name, compilation));
        }

        var graph = new SemanticGraphBuilder().Build(projects);

        // Git volatility を適用（無い/失敗時は unknown のまま継続: docs spec §26.2）。
        string? volatilityNote = null;
        try
        {
            var repoRoot = GitRepositoryDetector.Discover(opt.SolutionPath);
            if (repoRoot is not null)
            {
                var counts = new FileVolatilityAnalyzer()
                    .ChangeCountsByFile(repoRoot, DateTimeOffset.UtcNow.AddDays(-90));
                graph = new VolatilityEnricher().Apply(graph, counts);
            }
            else
            {
                volatilityNote = "No Git repository found; volatility is unknown.";
            }
        }
        catch (Exception ex)
        {
            volatilityNote = "Git history unavailable; volatility is unknown. (" + ex.Message + ")";
        }

        var metadata = new AnalysisMetadata(
            AnalysisMode.Semantic,
            loaded.Warnings.Count > 0 ? ConfidenceLevel.Medium : ConfidenceLevel.High,
            Path.GetFileName(opt.SolutionPath),
            ToolVersion(),
            DateTimeOffset.UtcNow,
            Reason: volatilityNote,
            Warnings: loaded.Warnings.Count > 0 ? loaded.Warnings : null);

        var report = new ReportBuilder().Build(graph, metadata);

        if (opt.Verbose)
        {
            opt.Summary = true;
            opt.Hotspots = true;
            opt.Violations = true;
        }

        var console = new ConsoleReporter();
        var anyConsole = opt.Summary || opt.Hotspots || opt.Violations;
        var anyOutput = anyConsole || opt.JsonPath is not null || opt.MarkdownPath is not null || opt.Check;
        if (!anyOutput)
            opt.Summary = true; // 既定はサマリ

        if (opt.Summary)
            console.WriteSummary(report, Console.Out);

        if (opt.Hotspots)
        {
            Console.Out.WriteLine();
            Console.Out.WriteLine($"== Hotspots ({report.Hotspots.Count}) ==");
            console.WriteHotspots(report, Console.Out, opt.Top);
        }

        if (opt.Violations)
        {
            Console.Out.WriteLine();
            Console.Out.WriteLine($"== Rule violations ({report.Violations.Count}) ==");
            console.WriteViolations(report, Console.Out);
        }

        if (opt.JsonPath is not null)
        {
            new JsonReporter().Write(report, opt.JsonPath);
            Console.Out.WriteLine($"Wrote {opt.JsonPath}");
        }

        if (opt.MarkdownPath is not null)
        {
            new MarkdownReporter().Write(report, opt.MarkdownPath);
            Console.Out.WriteLine($"Wrote {opt.MarkdownPath}");
        }

        return opt.Check ? GateExitCode(report, opt.MinGrade) : 0;
    }

    private static int GateExitCode(CouplingReport report, string minGrade)
    {
        var gradeOk = GradeRank(report.Summary.Grade) <= GradeRank(minGrade);
        var errorCount = report.Violations.Count(v => v.Severity == RuleSeverity.Error);
        if (!gradeOk || errorCount > 0)
        {
            Console.Error.WriteLine(
                $"Quality gate failed: grade {report.Summary.Grade} (min {minGrade}), {errorCount} error-level violations.");
            return 1;
        }
        return 0;
    }

    private static int GradeRank(string grade) => grade.ToUpperInvariant() switch
    {
        "A" => 0,
        "B" => 1,
        "C" => 2,
        "D" => 3,
        _ => 4,
    };

    private static string ToolVersion() =>
        Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "0.0.0";
}
