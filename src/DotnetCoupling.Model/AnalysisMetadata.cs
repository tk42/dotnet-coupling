namespace DotnetCoupling.Model;

/// <summary>解析の素性。出力に必ず含める（docs/implementation-plan.md 2 節）。</summary>
public sealed record AnalysisMetadata(
    AnalysisMode Mode,
    ConfidenceLevel Confidence,
    string Solution,
    string Version,
    DateTimeOffset GeneratedAt,
    string? Reason = null,
    IReadOnlyList<string>? Warnings = null);
