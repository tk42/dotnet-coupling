using System.Text;
using DotnetCoupling.Model;

namespace DotnetCoupling.Output;

/// <summary>人間が読みやすい Markdown レポートを生成する。</summary>
public sealed class MarkdownReporter
{
    public string Serialize(CouplingReport report)
    {
        var sb = new StringBuilder();
        var m = report.Metadata;
        var s = report.Summary;

        sb.AppendLine("# dotnet-coupling Report");
        sb.AppendLine();
        sb.AppendLine($"- Solution: `{m.Solution}`");
        sb.AppendLine($"- Mode: {m.Mode} / Confidence: {m.Confidence}");
        sb.AppendLine($"- Generated: {m.GeneratedAt:yyyy-MM-dd HH:mm:ss zzz}");
        if (!string.IsNullOrEmpty(m.Reason))
            sb.AppendLine($"- Note: {m.Reason}");
        sb.AppendLine();

        sb.AppendLine("## Summary");
        sb.AppendLine();
        sb.AppendLine("| Metric | Value |");
        sb.AppendLine("|---|---|");
        sb.AppendLine($"| Grade | **{s.Grade}** |");
        sb.AppendLine($"| Score | {s.Score} |");
        sb.AppendLine($"| Projects | {s.Projects} |");
        sb.AppendLine($"| Types | {s.Types} |");
        sb.AppendLine($"| Edges | {s.Edges} |");
        sb.AppendLine($"| Hotspots | {s.HotspotCount} |");
        sb.AppendLine($"| Rule violations | {s.RuleViolations} |");
        sb.AppendLine($"| Circular dependencies | {s.CircularDependencies} |");
        sb.AppendLine();

        sb.AppendLine($"## Hotspots ({report.Hotspots.Count})");
        sb.AppendLine();
        if (report.Hotspots.Count == 0)
        {
            sb.AppendLine("_None._");
        }
        else
        {
            var i = 1;
            foreach (var h in report.Hotspots)
            {
                sb.AppendLine($"### {i}. Grade {h.Grade} — {h.Source} → {h.Target}");
                sb.AppendLine();
                sb.AppendLine($"- Kind: {h.Kind}, Strength {h.Strength:0.00}, Distance {h.Distance:0.00}, " +
                              $"Volatility {Volatility(h.Volatility)}, Risk {h.Risk:0.00}");
                sb.AppendLine($"- Reason: {h.Reason}");
                sb.AppendLine($"- Suggestion: {h.Suggestion}");
                if (h.RelevantFiles.Count > 0)
                {
                    sb.AppendLine("- Files:");
                    foreach (var f in h.RelevantFiles)
                        sb.AppendLine($"  - `{f}`");
                }
                sb.AppendLine();
                i++;
            }
        }

        sb.AppendLine($"## Rule Violations ({report.Violations.Count})");
        sb.AppendLine();
        if (report.Violations.Count == 0)
        {
            sb.AppendLine("_None._");
        }
        else
        {
            foreach (var group in report.Violations.GroupBy(v => v.RuleId).OrderBy(g => g.Key))
            {
                sb.AppendLine($"### {group.Key} ({group.Count()})");
                sb.AppendLine();
                foreach (var v in group)
                {
                    var loc = string.IsNullOrEmpty(v.FilePath) ? "" : $" ({v.FilePath}:{v.Line})";
                    sb.AppendLine($"- **[{v.Severity}]** {v.Message}{loc}");
                }
                sb.AppendLine();
            }
        }

        return sb.ToString();
    }

    public void Write(CouplingReport report, string path) =>
        File.WriteAllText(path, Serialize(report));

    private static string Volatility(double value) =>
        double.IsNaN(value) ? "unknown" : value.ToString("0.00");
}
