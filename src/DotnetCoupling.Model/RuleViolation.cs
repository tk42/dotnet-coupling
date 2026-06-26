namespace DotnetCoupling.Model;

public enum RuleSeverity
{
    Info,
    Warning,
    Error
}

/// <summary>Rule Engine が検出した設計ルール違反。</summary>
public sealed record RuleViolation(
    string RuleId,
    RuleSeverity Severity,
    string Message,
    string? SourceId = null,
    string? TargetId = null,
    string? FilePath = null,
    int? Line = null,
    IReadOnlyList<string>? Cycle = null);
