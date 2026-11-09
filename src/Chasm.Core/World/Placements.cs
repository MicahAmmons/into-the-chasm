using Chasm.Core.Content;
using Chasm.Core.Grid;
using Chasm.Core.Items;
using Chasm.Core.Rules;

namespace Chasm.Core.World;

/// <summary>
/// Something placed on a Cell of a Tile. Subclasses appear in the Map Editor's "Add..." menu
/// automatically.
/// </summary>
[Polymorphic]
public abstract class Placement
{
    [Tooltip("Unique within the tile. World state (picked up, opened) is remembered under this id; avoid renaming.")]
    [Order(-100)]
    public string Id { get; set; } = "";

    [Order(-99)] public CellCoord Cell { get; set; }

    [Tooltip("Only exists in the world while ALL of these are true. Empty = always.")]
    [Order(-50)]
    public List<Condition> VisibleWhen { get; set; } = new();

    [Tooltip("Can the player walk through this cell while the object is there?")]
    public bool BlocksMovement { get; set; }

    public abstract string Describe(DefRegistry defs);
}

/// <summary>An item lying on the ground. Walk to it and press Interact (or click it) to pick it up.</summary>
[TypeId("ItemPickup"), Label("Item pickup")]
public sealed class ItemPickupPlacement : Placement
{
    [Ref(typeof(ItemDef)), Required] public string Item { get; set; } = "";
    [Range(1, 9999)] public int Count { get; set; } = 1;
    [Tooltip("Gone forever once picked up. Off = respawns every time the tile loads.")]
    public bool OneTime { get; set; } = true;
    [Tooltip("Extra things that happen on pickup (set a flag, show a message...).")]
    public List<Outcome> AlsoOnPickup { get; set; } = new();
    public override string Describe(DefRegistry d) =>
        $"Item: {(d.TryGet<ItemDef>(Item, out var i) ? i.DisplayName : "?")}{(Count > 1 ? " x" + Count : "")}";
}

public enum InteractTrigger
{
    [Label("Player presses Interact / clicks it")] Interact,
    [Label("Player steps onto the cell")] StepOn,
}

/// <summary>
/// Anything you can interact with: a bush, a sign, a hidden ledge, an altar. Runs the first rule
/// whose conditions pass; if none do, shows the fallback message (or nothing).
/// </summary>
[TypeId("Interactable"), Label("Interactable object")]
public sealed class InteractablePlacement : Placement
{
    [Required, Tooltip("Name shown on hover, e.g. 'Berry Bush'. Also the placeholder label.")]
    public string Label { get; set; } = "";
    [Texture, Tooltip("Art. Empty = labelled placeholder box. Leave empty AND tick Invisible for hidden spots.")]
    public string Sprite { get; set; } = "";
    [Tooltip("Not drawn and no hover name: for secret spots.")]
    public bool Invisible { get; set; }
    public int SpriteYOffset { get; set; }
    public InteractTrigger Trigger { get; set; } = InteractTrigger.Interact;
    [Tooltip("First rule whose conditions pass runs.")]
    public List<RuleDef> Rules { get; set; } = new();
    public string FallbackMessage { get; set; } = "";
    public override string Describe(DefRegistry d) => $"{(Invisible ? "Hidden " : "")}Interactable: {Label}";
}
