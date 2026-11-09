using Chasm.Core.Content;
using Chasm.Core.Grid;
using Chasm.Core.Items;
using Chasm.Core.World;

namespace Chasm.Core.Rules;

[TypeId("HasItem"), Label("Player has item"), Tooltip("True if the player carries at least Count of the item.")]
public sealed class HasItemCondition : Condition
{
    [Ref(typeof(ItemDef)), Required] public string Item { get; set; } = "";
    [Range(1, 9999)] public int Count { get; set; } = 1;
    public override bool Evaluate(RuleContext ctx) => ctx.Inventory.Has(Item, Count);
    public override string Describe(DefRegistry d) => $"Player has {(Count > 1 ? Count + "x " : "")}{DescribeHelp.Name<ItemDef>(d, Item)}";
}

[TypeId("LacksItem"), Label("Player does NOT have item")]
public sealed class LacksItemCondition : Condition
{
    [Ref(typeof(ItemDef)), Required] public string Item { get; set; } = "";
    public override bool Evaluate(RuleContext ctx) => !ctx.Inventory.Has(Item);
    public override string Describe(DefRegistry d) => $"Player lacks {DescribeHelp.Name<ItemDef>(d, Item)}";
}

public enum Comparison
{
    [Label("=")] Equal,
    [Label("!=")] NotEqual,
    [Label(">=")] AtLeast,
    [Label("<=")] AtMost,
    [Label(">")] GreaterThan,
    [Label("<")] LessThan,
}

public static class ComparisonExt
{
    public static bool Test(this Comparison c, double a, double b) => c switch
    {
        Comparison.Equal => Math.Abs(a - b) < 1e-9,
        Comparison.NotEqual => Math.Abs(a - b) >= 1e-9,
        Comparison.AtLeast => a >= b,
        Comparison.AtMost => a <= b,
        Comparison.GreaterThan => a > b,
        _ => a < b,
    };

    public static string Symbol(this Comparison c) => c switch
    {
        Comparison.Equal => "=", Comparison.NotEqual => "!=", Comparison.AtLeast => ">=",
        Comparison.AtMost => "<=", Comparison.GreaterThan => ">", _ => "<",
    };
}

[TypeId("Flag"), Label("World flag"), Tooltip("Compare a world flag. Unset flags are 0. 'is set' = '!= 0'.")]
public sealed class FlagCondition : Condition
{
    [Required, Tooltip("e.g. treant_defeated, vines_fallen, berries_fed")]
    public string Flag { get; set; } = "";
    public Comparison Compare { get; set; } = Comparison.NotEqual;
    public int Value { get; set; }
    public override bool Evaluate(RuleContext ctx) => Compare.Test(ctx.World.Get(Flag), Value);
    public override string Describe(DefRegistry d) =>
        Compare == Comparison.NotEqual && Value == 0 ? $"Flag '{Flag}' is set"
        : Compare == Comparison.Equal && Value == 0 ? $"Flag '{Flag}' is NOT set"
        : $"Flag '{Flag}' {Compare.Symbol()} {Value}";
}

[TypeId("Stat"), Label("Lifetime stat"), Tooltip("Compare a tracked statistic, e.g. monster.defeated.ice_ooze >= 50.")]
public sealed class StatCondition : Condition
{
    [Required] public string StatKey { get; set; } = "";
    public Comparison Compare { get; set; } = Comparison.AtLeast;
    public double Value { get; set; } = 1;
    public override bool Evaluate(RuleContext ctx) => Compare.Test(ctx.Stats.Get(StatKey), Value);
    public override string Describe(DefRegistry d) => $"Stat {StatKey} {Compare.Symbol()} {Value}";
}

[TypeId("Gold"), Label("Gold carried")]
public sealed class GoldCondition : Condition
{
    public Comparison Compare { get; set; } = Comparison.AtLeast;
    public int Amount { get; set; } = 1000;
    public override bool Evaluate(RuleContext ctx) => Compare.Test(ctx.Inventory.Gold, Amount);
    public override string Describe(DefRegistry d) => $"Gold {Compare.Symbol()} {Amount}";
}

[TypeId("Day"), Label("Day number")]
public sealed class DayCondition : Condition
{
    public Comparison Compare { get; set; } = Comparison.AtLeast;
    public int Day { get; set; } = 2;
    public override bool Evaluate(RuleContext ctx) => Compare.Test(ctx.Clock.Day, Day);
    public override string Describe(DefRegistry d) => $"Day {Compare.Symbol()} {Day}";
}

[TypeId("TimeOfDay"), Label("Day or night")]
public sealed class TimeOfDayCondition : Condition
{
    public bool Night { get; set; }
    public override bool Evaluate(RuleContext ctx) => ctx.Clock.IsNight == Night;
    public override string Describe(DefRegistry d) => Night ? "It is night" : "It is day";
}

[TypeId("InTile"), Label("Player is in tile")]
public sealed class InTileCondition : Condition
{
    [Ref(typeof(TileDef)), Required] public string Tile { get; set; } = "";
    public override bool Evaluate(RuleContext ctx) => ctx.CurrentTileId == Tile;
    public override string Describe(DefRegistry d) => $"Player is in {DescribeHelp.Name<TileDef>(d, Tile)}";
}

[TypeId("NearCell"), Label("Player is near a cell"), Tooltip("For hidden spots: 'player stands within Range cells of this one'.")]
public sealed class NearCellCondition : Condition
{
    [Ref(typeof(TileDef)), Required] public string Tile { get; set; } = "";
    public CellCoord Cell { get; set; }
    [Range(0, 30)] public int Range { get; set; }
    public override bool Evaluate(RuleContext ctx) => ctx.CurrentTileId == Tile && ctx.PlayerCell.ManhattanTo(Cell) <= Range;
    public override string Describe(DefRegistry d) => $"Player within {Range} of {Cell} in {DescribeHelp.Name<TileDef>(d, Tile)}";
}

[TypeId("NotInCombat"), Label("Out of combat")]
public sealed class NotInCombatCondition : Condition
{
    public override bool Evaluate(RuleContext ctx) => !ctx.InCombat;
    public override string Describe(DefRegistry d) => "Out of combat";
}

[TypeId("InCombat"), Label("In combat")]
public sealed class InCombatCondition : Condition
{
    public override bool Evaluate(RuleContext ctx) => ctx.InCombat;
    public override string Describe(DefRegistry d) => "In combat";
}

[TypeId("Not"), Label("NOT (inverts another condition)")]
public sealed class NotCondition : Condition
{
    public Condition? Inner { get; set; }
    public override bool Evaluate(RuleContext ctx) => Inner is not null && !Inner.Evaluate(ctx);
    public override string Describe(DefRegistry d) => $"NOT ({Inner?.Describe(d) ?? "nothing"})";
}

[TypeId("AnyOf"), Label("ANY of these (or)")]
public sealed class AnyOfCondition : Condition
{
    public List<Condition> Conditions { get; set; } = new();
    public override bool Evaluate(RuleContext ctx) => Conditions.Any(c => c.Evaluate(ctx));
    public override string Describe(DefRegistry d) => "ANY of: " + string.Join(" / ", Conditions.Select(c => c.Describe(d)));
}

[TypeId("AllOf"), Label("ALL of these (and)")]
public sealed class AllOfCondition : Condition
{
    public List<Condition> Conditions { get; set; } = new();
    public override bool Evaluate(RuleContext ctx) => Conditions.All(c => c.Evaluate(ctx));
    public override string Describe(DefRegistry d) => "ALL of: " + string.Join(" + ", Conditions.Select(c => c.Describe(d)));
}
