using DotnetCoupling.Model;

namespace DotnetCoupling.Rules;

/// <summary>登録ルールを順に評価して違反を集約する。</summary>
public sealed class RuleEngine
{
    private readonly IReadOnlyList<IArchitectureRule> _rules;

    public RuleEngine(IEnumerable<IArchitectureRule> rules) => _rules = rules.ToList();

    /// <summary>MVP の既定ルール一式。</summary>
    public static RuleEngine CreateDefault() => new(new IArchitectureRule[]
    {
        new LayerViolationRule(),
        new CircularDependencyRule(),
        new ConcreteDependencyRule(),
    });

    public IReadOnlyList<RuleViolation> Evaluate(CouplingGraph graph, RuleOptions options) =>
        _rules.SelectMany(r => r.Evaluate(graph, options)).ToList();
}
