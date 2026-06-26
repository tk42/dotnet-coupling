namespace DotnetCoupling.Model;

/// <summary>
/// 同一 (source,target) に対する依存の 1 出現。
/// 1 本のエッジは複数の出現（field でもあり methodCall でもある等）を束ねる。
/// </summary>
public sealed record Occurrence(
    DependencyKind Kind,
    string? File = null,
    int? Line = null,
    string? Evidence = null);
