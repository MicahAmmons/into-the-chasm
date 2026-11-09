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
    [Label("Player comes within the radius")] Proximity,
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
    [Ref(typeof(Animation.AnimationDef)), Tooltip("Animated look (plays Idle). Overrides Sprite.")]
    public string Animation { get; set; } = "";
    [Tooltip("Not drawn and no hover name: for secret spots.")]
    public bool Invisible { get; set; }
    public int SpriteYOffset { get; set; }
    public InteractTrigger Trigger { get; set; } = InteractTrigger.Interact;
    [Range(0, 30), Tooltip("For 'Proximity': how many cells away (diamond shape) triggers it.")]
    public int Radius { get; set; } = 2;
    [Tooltip("For 'Proximity' / 'Step on': run again every time the player re-enters (off = only the first time each visit).")]
    public bool Repeat { get; set; } = true;
    [Tooltip("First rule whose conditions pass runs.")]
    public List<RuleDef> Rules { get; set; } = new();
    public string FallbackMessage { get; set; } = "";
    public override string Describe(DefRegistry d) => $"{(Invisible ? "Hidden " : "")}Interactable: {Label}";
}

public enum PatrolMode
{
    [Label("Stand still")] Stay,
    [Label("Loop through the points")] Loop,
    [Label("Walk back and forth")] BackAndForth,
}

/// <summary>
/// A visible monster group in the world. Walks its patrol; hovering shows everything in the group;
/// clicking it (or touching it, if aggressive) starts the fight.
/// </summary>
[TypeId("MonsterGroup"), Label("Monster group (encounter)")]
public sealed class MonsterGroupPlacement : Placement
{
    [Ref(typeof(Combat.EncounterDef)), Required] public string Encounter { get; set; } = "";
    public PatrolMode Patrol { get; set; } = PatrolMode.Loop;
    [Tooltip("Cells to walk between, starting from the placement's own cell. Use 'Pick' to click them on the map.")]
    public List<CellCoord> PatrolPoints { get; set; } = new();
    [Range(10, 600)] public float WalkSpeed { get; set; } = 70;
    [Range(0, 60), Tooltip("Seconds to pause at each point.")] public float PauseSeconds { get; set; } = 2;
    [Tooltip("Fight starts as soon as the player touches it (no prompt).")]
    public bool Aggressive { get; set; }
    public override string Describe(DefRegistry d) =>
        $"Monsters: {(d.TryGet<Combat.EncounterDef>(Encounter, out var e) ? e.DisplayName : "?")}{(Aggressive ? " (aggressive)" : "")}";
}

/// <summary>
/// The Figure: narrator, respawn point and fast-travel network. Coming near one makes it your
/// respawn point and adds it to the travel list. Interacting offers Talk / Rest / Travel.
/// </summary>
[TypeId("Figure"), Label("The Figure (respawn + fast travel)")]
public sealed class FigurePlacement : Placement
{
    public string Name { get; set; } = "Figure";
    [Ref(typeof(Animation.AnimationDef))] public string Animation { get; set; } = "cloud_figure";
    [Tooltip("What the Figure says when you choose Talk (if no rule below passes).")]
    public List<string> Greeting { get; set; } = new() { "..." };
    [Tooltip("Hints / quest lines: first passing rule runs when you choose Talk.")]
    public List<RuleDef> TalkRules { get; set; } = new();
    public bool OffersRest { get; set; } = true;
    public bool OffersFastTravel { get; set; } = true;
    [Range(0, 10), Tooltip("Coming this close counts as 'encountered' (sets respawn point).")]
    public int EncounterRange { get; set; } = 3;
    public override string Describe(DefRegistry d) => $"Figure: {Name}";
}
