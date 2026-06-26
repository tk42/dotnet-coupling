using DotnetCoupling.Model;

namespace DotnetCoupling.Output;

/// <summary>人間向けのコンソール出力（docs spec §19.2 / §19.3 / §16）。</summary>
public sealed class ConsoleReporter
{
    public void WriteSummary(CouplingReport report, TextWriter writer)
    {
        var s = report.Summary;
        writer.WriteLine($"Overall Grade: {s.Grade}");
        writer.WriteLine($"Score: {s.Score}");
        writer.WriteLine($"Projects: {s.Projects}");
        writer.WriteLine($"Types: {s.Types}");
        writer.WriteLine($"Edges: {s.Edges}");
        writer.WriteLine($"Hotspots: {s.HotspotCount}");
        writer.WriteLine($"Rule violations: {s.RuleViolations}");
        writer.WriteLine($"Circular dependencies: {s.CircularDependencies}");

        if (report.Metadata.Mode == AnalysisMode.SyntaxOnly)
        {
            writer.WriteLine();
            writer.WriteLine($"Mode: syntax-only");
            writer.WriteLine($"Confidence: {report.Metadata.Confidence.ToString().ToLowerInvariant()}");
            if (!string.IsNullOrEmpty(report.Metadata.Reason))
                writer.WriteLine($"Reason: {report.Metadata.Reason}");
        }
    }

    public void WriteHotspots(CouplingReport report, TextWriter writer, int? top = null)
    {
        if (report.Hotspots.Count == 0)
        {
            writer.WriteLine("No hotspots found.");
            return;
        }

        var shown = top is > 0 ? report.Hotspots.Take(top.Value) : report.Hotspots;
        foreach (var h in shown)
        {
            writer.WriteLine($"Grade {h.Grade}  {h.Source}");
            writer.WriteLine($"  depends on {h.Target}");
            writer.WriteLine($"  Strength: {h.Strength:0.00}  Distance: {h.Distance:0.00}  " +
                             $"Volatility: {Volatility(h.Volatility)}  Risk: {h.Risk:0.00}");
            writer.WriteLine($"  Suggestion: {h.Suggestion}");
            writer.WriteLine();
        }
    }

    public void WriteViolations(CouplingReport report, TextWriter writer)
    {
        if (report.Violations.Count == 0)
        {
            writer.WriteLine("No rule violations.");
            return;
        }

        foreach (var group in report.Violations.GroupBy(v => v.RuleId).OrderBy(g => g.Key))
        {
            writer.WriteLine($"[{group.Key}] ({group.Count()})");
            foreach (var v in group)
            {
                var loc = string.IsNullOrEmpty(v.FilePath) ? string.Empty : $"  ({v.FilePath}:{v.Line})";
                writer.WriteLine($"  {v.Severity,-7} {v.Message}{loc}");
            }
            writer.WriteLine();
        }
    }

    private static string Volatility(double value) =>
        double.IsNaN(value) ? "unknown" : value.ToString("0.00");
}
