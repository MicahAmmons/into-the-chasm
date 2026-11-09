using Chasm.Core.Content;

namespace Chasm.Core.Rules;

/// <summary>
/// "When all of these are true, do all of these." The universal building block used by item uses,
/// interactables, tile exits, dialogue choices and unlocks.
/// </summary>
public sealed class RuleDef
{
    [Tooltip("Optional note shown in editor lists, e.g. 'Feed berry to turtle'.")]
    public string Label { get; set; } = "";

    [Tooltip("All must be true. Leave empty for 'always'.")]
    public List<Condition> Conditions { get; set; } = new();

    [Tooltip("Run in order when the conditions pass.")]
    public List<Outcome> Outcomes { get; set; } = new();

    public bool Passes(RuleContext ctx) => Conditions.All(c => c.Evaluate(ctx));

    public void Run(RuleContext ctx)
    {
        foreach (var o in Outcomes) o.Execute(ctx);
    }

    public override string ToString() => string.IsNullOrWhiteSpace(Label)
        ? $"{Conditions.Count} condition(s) -> {Outcomes.Count} outcome(s)"
        : Label;
}

/// <summary>Helpers for "first rule that passes wins" lists.</summary>
public static class Rules
{
    public static bool AllPass(IEnumerable<Condition> conditions, RuleContext ctx) => conditions.All(c => c.Evaluate(ctx));

    /// <summary>Runs the first passing rule. Returns it, or null if none passed.</summary>
    public static RuleDef? RunFirst(IEnumerable<RuleDef> rules, RuleContext ctx)
    {
        foreach (var r in rules)
            if (r.Passes(ctx)) { r.Run(ctx); return r; }
        return null;
    }
}

/// <summary>
/// A yes/no question about the game state. Add a new kind by subclassing; it appears in every
/// editor dropdown automatically.
/// </summary>
[Polymorphic]
public abstract class Condition
{
    public abstract bool Evaluate(RuleContext ctx);

    /// <summary>Plain-English summary shown in editor lists.</summary>
    public abstract string Describe(DefRegistry defs);

    public override string ToString() => GetType().Name;
}

/// <summary>Something that changes the game. Same discovery rules as <see cref="Condition"/>.</summary>
[Polymorphic]
public abstract class Outcome
{
    public abstract void Execute(RuleContext ctx);
    public abstract string Describe(DefRegistry defs);
    public override string ToString() => GetType().Name;
}

internal static class DescribeHelp
{
    public static string Name<T>(DefRegistry defs, string? id) where T : Def =>
        string.IsNullOrEmpty(id) ? "(none)" : defs.TryGet<T>(id, out var d) ? d.DisplayName : $"'{id}' (missing!)";
}
