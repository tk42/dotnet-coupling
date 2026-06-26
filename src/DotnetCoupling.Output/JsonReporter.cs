using System.Text.Json;
using DotnetCoupling.Model;

namespace DotnetCoupling.Output;

/// <summary>
/// <see cref="CouplingReport"/> を JSON で出力する（docs spec §24 のスキーマ概要に準拠）。
/// volatility が unknown(NaN) のエッジは JSON では null にする。
/// </summary>
public sealed class JsonReporter
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    public const string ToolName = "dotnet-coupling";

    public string Serialize(CouplingReport report)
    {
        var dto = new
        {
            metadata = new
            {
                tool = ToolName,
                version = report.Metadata.Version,
                analysisMode = report.Metadata.Mode.ToString(),
                confidence = report.Metadata.Confidence.ToString(),
                solution = report.Metadata.Solution,
                generatedAt = report.Metadata.GeneratedAt,
                reason = report.Metadata.Reason,
                warnings = report.Metadata.Warnings,
            },
            summary = new
            {
                score = report.Summary.Score,
                grade = report.Summary.Grade,
                projects = report.Summary.Projects,
                types = report.Summary.Types,
                edges = report.Summary.Edges,
                hotspots = report.Summary.HotspotCount,
                ruleViolations = report.Summary.RuleViolations,
                circularDependencies = report.Summary.CircularDependencies,
            },
            nodes = report.Graph.Nodes.Select(n => new
            {
                id = n.Id,
                name = n.Name,
                kind = n.Kind.ToString(),
                project = n.ProjectName,
                @namespace = n.Namespace,
                assembly = n.AssemblyName,
                filePath = n.FilePath,
            }),
            edges = report.Graph.Edges.Select(e => new
            {
                source = e.SourceId,
                target = e.TargetId,
                strength = e.Strength,
                distance = e.Distance,
                volatility = NullIfNaN(e.Volatility),
                risk = e.Risk,
                structuralRisk = e.StructuralRisk,
                confidence = e.Confidence,
                kinds = e.Occurrences.Select(o => o.Kind.ToString()).Distinct(),
            }),
            hotspots = report.Hotspots.Select(h => new
            {
                source = h.Source,
                target = h.Target,
                kind = h.Kind.ToString(),
                strength = h.Strength,
                distance = h.Distance,
                volatility = NullIfNaN(h.Volatility),
                risk = h.Risk,
                grade = h.Grade,
                reason = h.Reason,
                suggestion = h.Suggestion,
                filesToRead = h.RelevantFiles,
            }),
            rules = report.Violations.Select(v => new
            {
                ruleId = v.RuleId,
                severity = v.Severity.ToString(),
                message = v.Message,
                source = Short(v.SourceId),
                target = Short(v.TargetId),
                filePath = v.FilePath,
                line = v.Line,
                cycle = v.Cycle?.Select(Short),
            }),
        };

        return JsonSerializer.Serialize(dto, Options);
    }

    public void Write(CouplingReport report, string path) =>
        File.WriteAllText(path, Serialize(report));

    private static double? NullIfNaN(double value) => double.IsNaN(value) ? null : value;

    private static string? Short(string? id) =>
        id is not null && id.StartsWith("T:", StringComparison.Ordinal) ? id.Substring(2) : id;
}
