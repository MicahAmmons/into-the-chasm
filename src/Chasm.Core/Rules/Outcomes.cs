using Chasm.Core.Content;
using Chasm.Core.Grid;
using Chasm.Core.Items;
using Chasm.Core.World;

namespace Chasm.Core.Rules;

[TypeId("GiveItem"), Label("Give item")]
public sealed class GiveItemOutcome : Outcome
{
    [Ref(typeof(ItemDef)), Required] public string Item { get; set; } = "";
    [Range(1, 9999)] public int Count { get; set; } = 1;
    [Tooltip("Show 'Received X' to the player.")] public bool Announce { get; set; } = true;
    public override void Execute(RuleContext ctx)
    {
        ctx.Inventory.Add(Item, Count);
        ctx.Events.Publish(new ItemGainedEvent(Item, Count));
        if (Announce && ctx.Host is not null)
            ctx.Host.ShowMessage(new[] { $"Received {(Count > 1 ? Count + "x " : "")}{DescribeHelp.Name<ItemDef>(ctx.Defs, Item)}." }, null);
    }
    public override string Describe(DefRegistry d) => $"Give {(Count > 1 ? Count + "x " : "")}{DescribeHelp.Name<ItemDef>(d, Item)}";
}

[TypeId("TakeItem"), Label("Take item from player")]
public sealed class TakeItemOutcome : Outcome
{
    [Ref(typeof(ItemDef)), Required] public string Item { get; set; } = "";
    [Range(1, 9999)] public int Count { get; set; } = 1;
    public override void Execute(RuleContext ctx) => ctx.Inventory.Remove(Item, Count);
    public override string Describe(DefRegistry d) => $"Take {(Count > 1 ? Count + "x " : "")}{DescribeHelp.Name<ItemDef>(d, Item)}";
}

[TypeId("Gold"), Label("Give / take gold")]
public sealed class GoldOutcome : Outcome
{
    [Tooltip("Negative takes gold.")] public int Amount { get; set; } = 10;
    public override void Execute(RuleContext ctx) => ctx.Inventory.AddGold(Amount);
    public override string Describe(DefRegistry d) => Amount >= 0 ? $"Give {Amount} gold" : $"Take {-Amount} gold";
}

public enum FlagOperation { Set, Add, Clear }

[TypeId("Flag"), Label("Change world flag")]
public sealed class FlagOutcome : Outcome
{
    [Required] public string Flag { get; set; } = "";
    public FlagOperation Operation { get; set; } = FlagOperation.Set;
    [Tooltip("Set: the new value (1 = on). Add: amount to add.")] public int Value { get; set; } = 1;
    public override void Execute(RuleContext ctx)
    {
        switch (Operation)
        {
            case FlagOperation.Set: ctx.World.Set(Flag, Value); break;
            case FlagOperation.Add: ctx.World.Add(Flag, Value); break;
            default: ctx.World.Set(Flag, 0); break;
        }
        ctx.Events.Publish(new FlagChangedEvent(Flag, ctx.World.Get(Flag)));
    }
    public override string Describe(DefRegistry d) => Operation switch
    {
        FlagOperation.Set => Value == 1 ? $"Set flag '{Flag}'" : $"Set flag '{Flag}' = {Value}",
        FlagOperation.Add => $"Add {Value} to flag '{Flag}'",
        _ => $"Clear flag '{Flag}'",
    };
}

[TypeId("Message"), Label("Show message")]
public sealed class MessageOutcome : Outcome
{
    [Tooltip("Optional name shown above the text (e.g. 'Figure').")] public string Speaker { get; set; } = "";
    [Tooltip("Each entry is one page; the player clicks to advance.")] public List<string> Pages { get; set; } = new() { "" };
    public override void Execute(RuleContext ctx) =>
        ctx.Host?.ShowMessage(Pages, string.IsNullOrWhiteSpace(Speaker) ? null : Speaker);
    public override string Describe(DefRegistry d) =>
        $"Message{(string.IsNullOrWhiteSpace(Speaker) ? "" : $" ({Speaker})")}: \"{Pages.FirstOrDefault() ?? ""}\"{(Pages.Count > 1 ? $" +{Pages.Count - 1}" : "")}";
}

[TypeId("ChangeTile"), Label("Move player to tile"), Tooltip("Teleport / fall / descend to another Tile (any floor).")]
public sealed class ChangeTileOutcome : Outcome
{
    [Ref(typeof(TileDef)), Required] public string Tile { get; set; } = "";
    [Tooltip("Where to land. Leave 'Use Arrival Cell' off to use the tile's player spawn.")]
    public bool UseArrivalCell { get; set; }
    public CellCoord ArrivalCell { get; set; }
    public override void Execute(RuleContext ctx) => ctx.Host?.ChangeTile(Tile, UseArrivalCell ? ArrivalCell : null);
    public override string Describe(DefRegistry d) => $"Move player to {DescribeHelp.Name<TileDef>(d, Tile)}";
}

[TypeId("Rest"), Label("Make the player rest")]
public sealed class RestOutcome : Outcome
{
    public override void Execute(RuleContext ctx) => ctx.Host?.Rest();
    public override string Describe(DefRegistry d) => "Rest";
}

[TypeId("AddStat"), Label("Add to a stat")]
public sealed class AddStatOutcome : Outcome
{
    [Required] public string StatKey { get; set; } = "";
    public double Amount { get; set; } = 1;
    public override void Execute(RuleContext ctx) => ctx.Stats.Add(StatKey, Amount);
    public override string Describe(DefRegistry d) => $"Add {Amount} to stat {StatKey}";
}

[TypeId("HidePlacement"), Label("Remove this object from the world"),
 Tooltip("Permanently removes the placement that ran this rule (picked bush, opened chest...).")]
public sealed class HidePlacementOutcome : Outcome
{
    public override void Execute(RuleContext ctx)
    {
        if (ctx.SourcePlacementId is { } id)
            ctx.World.Set(WorldState.PlacementKey(ctx.CurrentTileId, id, "gone"), 1);
    }
    public override string Describe(DefRegistry d) => "Remove this object";
}
