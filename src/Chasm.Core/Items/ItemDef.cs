using Chasm.Core.Content;
using Chasm.Core.Rules;

namespace Chasm.Core.Items;

public enum ItemCategory
{
    [Label("Key item")] KeyItem,
    Usable,
    [Label("Consumed for an unlock")] Consumable,
    [Label("Summon upgrade")] SummonUpgrade,
    Treasure,
    Quest,
}

/// <summary>
/// An item type. Never a stat potion: items do things (use rules), unlock things (other rules check
/// "player has X"), get consumed for unlocks, or permanently upgrade summons.
/// </summary>
[DefFolder("items", "Items")]
[Tooltip("Anything the player can carry.")]
public sealed class ItemDef : Def
{
    [Multiline, Tooltip("Shown when hovering the item in the inventory or quick bar.")]
    public string Description { get; set; } = "";

    public ItemCategory Category { get; set; } = ItemCategory.KeyItem;

    [Texture, Tooltip("Inventory / quick bar icon.")]
    public string Icon { get; set; } = "";

    [Texture, Tooltip("How it looks lying in the world. Empty = use the icon.")]
    public string WorldSprite { get; set; } = "";

    [Tooltip("Can you carry more than one?")]
    public bool Stackable { get; set; }

    [Tooltip("Shown on the quick bar so it can be clicked to use.")]
    public bool ShowInQuickBar { get; set; } = true;

    [Tooltip("What happens when the player uses it. The first rule whose conditions pass runs. " +
             "If none pass, the player sees the 'no reason' message.")]
    public List<RuleDef> UseRules { get; set; } = new();

    public string NoUseMessage { get; set; } = "No reason to use that here...";
}
