using Chasm.Core.Content;
using Chasm.Core.Grid;

namespace Chasm.Core.Combat;

public enum TargetPick
{
    [Label("Nearest enemy")] NearestEnemy,
    [Label("The player (if an enemy)")] Player,
    [Label("Enemy with lowest HP")] LowestHpEnemy,
    [Label("Enemy with highest HP")] HighestHpEnemy,
    [Label("Nearest summon")] NearestSummon,
    [Label("Random enemy")] RandomEnemy,
    [Label("Ally with lowest HP")] LowestHpAlly,
    [Label("Itself")] Self,
}

/// <summary>
/// One line in a monster's priority list. Each turn the monster asks its rules, top to bottom,
/// "what would you do now?" and does the first possible answer, then asks again, until no rule has
/// anything to do. Add new behaviours by subclassing; they appear in the editor automatically.
/// </summary>
[Polymorphic]
public abstract class AiRule
{
    public abstract CombatAction? Decide(CombatEngine engine, Combatant self);
    public abstract string Describe(DefRegistry defs);

    protected static IEnumerable<Combatant> Targets(CombatEngine e, Combatant self, TargetPick pick)
    {
        var enemies = e.Alive.Where(u => u.Team != self.Team).ToList();
        var allies = e.Alive.Where(u => u.Team == self.Team).ToList();
        int Dist(Combatant u) => u.Cell.ManhattanTo(self.Cell);
        return pick switch
        {
            TargetPick.NearestEnemy => enemies.OrderBy(Dist),
            TargetPick.Player => enemies.Where(u => u.Kind == CombatantKind.Player),
            TargetPick.LowestHpEnemy => enemies.OrderBy(u => u.Hp).ThenBy(Dist),
            TargetPick.HighestHpEnemy => enemies.OrderByDescending(u => u.Hp).ThenBy(Dist),
            TargetPick.NearestSummon => e.Alive.Where(u => u.Kind == CombatantKind.Summon && u.Team != self.Team).OrderBy(Dist),
            TargetPick.RandomEnemy => enemies.OrderBy(u => (u.Id * 7919 + e.Round * 31 + self.Id) % 97),
            TargetPick.LowestHpAlly => allies.OrderBy(u => u.Hp / (double)u.MaxHp),
            _ => new[] { self },
        };
    }

    protected static string Ability(DefRegistry d, string id) =>
        string.IsNullOrEmpty(id) ? "any ability" : d.TryGet<AbilityDef>(id, out var a) ? a.DisplayName : id;

    /// <summary>Best reachable cell by a score (higher better). Returns a move only if it beats staying put.</summary>
    protected static MoveAction? MoveToBest(CombatEngine e, Combatant self, Func<CellCoord, double> score)
    {
        var reach = e.Reachable(self);
        double here = score(self.Cell);
        CellCoord? best = null;
        double bestScore = here;
        foreach (var (cell, steps) in reach)
        {
            if (cell == self.Cell) continue;
            double s = score(cell) - steps * 0.001; // prefer shorter walks on ties
            if (s > bestScore + 1e-6) { bestScore = s; best = cell; }
        }
        if (best is null) return null;
        var path = e.PathTo(self, best.Value);
        return path is { Count: > 0 } ? new MoveAction(path) : null;
    }
}

[TypeId("UseAbility"), Label("Use an ability on a target")]
public sealed class UseAbilityRule : AiRule
{
    [Ref(typeof(AbilityDef)), Tooltip("Leave empty to try every ability it has, in its listed order.")]
    public string Ability { get; set; } = "";
    public TargetPick Target { get; set; } = TargetPick.NearestEnemy;

    public override CombatAction? Decide(CombatEngine e, Combatant self)
    {
        var abilities = e.Abilities(self).Where(a => string.IsNullOrEmpty(Ability) || a.Id == Ability).ToList();
        foreach (var t in Targets(e, self, Target))
            foreach (var a in abilities)
            {
                var cell = a.Target is TargetKind.Self ? self.Cell : t.Cell;
                if (e.CanUseAbility(self, a, cell, out _)) return new UseAbilityAction(a.Id, cell);
            }
        return null;
    }

    public override string Describe(DefRegistry d) => $"Use {Ability(d, Ability)} on {Inspect(Target)}";
    internal static string Inspect(TargetPick p) => p.ToString();
}

[TypeId("MoveToward"), Label("Move toward a target")]
public sealed class MoveTowardRule : AiRule
{
    public TargetPick Target { get; set; } = TargetPick.NearestEnemy;
    [Tooltip("Stop once this close (1 = adjacent).")]
    [Range(1, 30)] public int StopAtDistance { get; set; } = 1;

    public override CombatAction? Decide(CombatEngine e, Combatant self)
    {
        var t = Targets(e, self, Target).FirstOrDefault();
        if (t is null || self.Cell.ManhattanTo(t.Cell) <= StopAtDistance) return null;
        // Walking distance (around walls), not straight-line distance.
        var walk = e.WalkDistanceTo(t.Cell, self);
        return MoveToBest(e, self, c =>
            c.ManhattanTo(t.Cell) <= StopAtDistance ? -StopAtDistance
            : walk.TryGetValue(c, out int d) ? -d : -1000 - c.ManhattanTo(t.Cell));
    }

    public override string Describe(DefRegistry d) => $"Move toward {Target} (stop at {StopAtDistance})";
}

[TypeId("KeepAtRange"), Label("Keep at a distance from a target")]
public sealed class KeepAtRangeRule : AiRule
{
    public TargetPick Target { get; set; } = TargetPick.NearestEnemy;
    [Ref(typeof(AbilityDef)), Tooltip("Stay at this ability's max range. Empty = use Distance.")]
    public string UseAbilityRange { get; set; } = "";
    [Range(1, 30)] public int Distance { get; set; } = 4;

    public override CombatAction? Decide(CombatEngine e, Combatant self)
    {
        var t = Targets(e, self, Target).FirstOrDefault();
        if (t is null) return null;
        int want = Distance;
        if (!string.IsNullOrEmpty(UseAbilityRange) && e.Defs.TryGet<AbilityDef>(UseAbilityRange, out var a)) want = a.MaxRange;
        // Best: exactly at the wanted distance (with line of sight), otherwise as close to it as possible.
        return MoveToBest(e, self, c =>
        {
            int d = c.ManhattanTo(t.Cell);
            double s = -Math.Abs(d - want);
            if (d <= want && e.HasLineOfSight(c, t.Cell)) s += 0.5;
            return s;
        });
    }

    public override string Describe(DefRegistry d) =>
        $"Keep {(string.IsNullOrEmpty(UseAbilityRange) ? Distance.ToString() : Ability(d, UseAbilityRange) + " range")} from {Target}";
}

[TypeId("MoveAway"), Label("Run away from enemies")]
public sealed class MoveAwayRule : AiRule
{
    [Tooltip("Only run when HP is at or below this percent (100 = always).")]
    [Range(1, 100)] public int WhenHpBelowPercent { get; set; } = 100;

    public override CombatAction? Decide(CombatEngine e, Combatant self)
    {
        if (self.Hp * 100 > self.MaxHp * WhenHpBelowPercent) return null;
        var enemies = e.Alive.Where(u => u.Team != self.Team).ToList();
        if (enemies.Count == 0) return null;
        return MoveToBest(e, self, c => enemies.Min(u => u.Cell.ManhattanTo(c)));
    }

    public override string Describe(DefRegistry d) => WhenHpBelowPercent >= 100 ? "Run away" : $"Run away when HP <= {WhenHpBelowPercent}%";
}

[TypeId("EndTurn"), Label("End turn (stop here)")]
public sealed class EndTurnRule : AiRule
{
    public override CombatAction? Decide(CombatEngine e, Combatant self) => new EndTurnAction();
    public override string Describe(DefRegistry d) => "End turn";
}

/// <summary>Runs a combatant's priority list.</summary>
public static class CombatAi
{
    /// <summary>Used when a monster has no rules: hit the nearest enemy, walk to it, hit again.</summary>
    public static readonly IReadOnlyList<AiRule> Default = new AiRule[]
    {
        new UseAbilityRule(), new MoveTowardRule(), new UseAbilityRule(),
    };

    public static CombatAction NextAction(CombatEngine e, Combatant self)
    {
        var rules = self.Monster?.AiPriorities is { Count: > 0 } r ? (IEnumerable<AiRule>)r : Default;
        foreach (var rule in rules)
        {
            var action = rule.Decide(e, self);
            if (action is not null && e.Validate(action) is null) return action;
        }
        return new EndTurnAction();
    }

    /// <summary>Plays a whole AI turn instantly (tests, AI-vs-AI fast mode). Returns events.</summary>
    public static List<CombatEvent> PlayTurn(CombatEngine e, int maxActions = 30)
    {
        var events = new List<CombatEvent>();
        var self = e.Current;
        for (int i = 0; i < maxActions && e.Outcome is null && e.Current == self; i++)
        {
            var action = NextAction(e, self);
            var result = e.Apply(action);
            events.AddRange(result.Events);
            if (action is EndTurnAction) return events;
        }
        if (e.Outcome is null && e.Current == self) events.AddRange(e.Apply(new EndTurnAction()).Events);
        return events;
    }
}
