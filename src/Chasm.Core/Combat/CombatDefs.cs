using Chasm.Core.Animation;
using Chasm.Core.Content;
using Chasm.Core.Rules;

namespace Chasm.Core.Combat;

/// <summary>A damage element (Fire, Ice...). How much extra or less damage things of this element take from others.</summary>
[DefFolder("elements", "Elements")]
[Tooltip("A damage element and its strengths / weaknesses.")]
public sealed class ElementDef : Def
{
    [Tooltip("Colour for damage numbers and icons, #RRGGBB.")]
    public string Color { get; set; } = "#ffffff";

    [Tooltip("Creatures of THIS element take this much more (+) or less (-) damage from the listed elements. +40 = 40% more.")]
    public List<ElementModifier> DamageTakenFrom { get; set; } = new();

    public int PercentFrom(string? element) =>
        DamageTakenFrom.Where(m => m.Element == element).Sum(m => m.Percent);
}

public sealed class ElementModifier
{
    [Ref(typeof(ElementDef)), Required] public string Element { get; set; } = "";
    [Range(-100, 500)] public int Percent { get; set; }
    public override string ToString() => $"{Element} {(Percent >= 0 ? "+" : "")}{Percent}%";
}

public enum StatusTick { StartOfTurn, EndOfTurn }

/// <summary>
/// A buff or debuff on a combatant: Burn, Turtle Shell, Rooted... In-combat aspects are statuses.
/// </summary>
[DefFolder("statuses", "Status Effects")]
[Tooltip("A buff or debuff that lasts a number of turns.")]
public sealed class StatusDef : Def
{
    [Multiline] public string Description { get; set; } = "";
    [Texture] public string Icon { get; set; } = "";
    public bool IsBuff { get; set; }

    [Range(1, 99)] public int DefaultDuration { get; set; } = 2;
    [Tooltip("Can several copies be on the same target at once? Off = reapplying refreshes the duration.")]
    public bool Stacks { get; set; }

    [Group("Modifiers"), Tooltip("Negative reduces damage taken: -30 = takes 30% less.")]
    [Range(-100, 500)] public int DamageTakenPercent { get; set; }
    [Group("Modifiers"), Range(-100, 500)] public int DamageDealtPercent { get; set; }
    [Group("Modifiers"), Tooltip("Change to AP each turn while active.")] public int ApChange { get; set; }
    [Group("Modifiers"), Tooltip("Change to MP each turn while active.")] public int MpChange { get; set; }
    [Group("Modifiers"), Tooltip("Can't move at all (rooted).")] public bool Rooted { get; set; }

    [Group("Over time")] public StatusTick TickAt { get; set; } = StatusTick.StartOfTurn;
    [Group("Over time"), Tooltip("Damage each tick (0 = none).")] public int TickDamage { get; set; }
    [Group("Over time"), Ref(typeof(ElementDef))] public string TickElement { get; set; } = "";
    [Group("Over time")] public int TickHeal { get; set; }
}

public enum TargetKind
{
    Enemy, Ally, Self,
    [Label("Any creature")] AnyUnit,
    [Label("Empty cell")] EmptyCell,
    [Label("Any cell")] AnyCell,
}

public enum AreaShape
{
    [Label("Single cell")] Single,
    [Label("Cross (+)")] Cross,
    [Label("Diamond (all within size)")] Diamond,
}

/// <summary>Something an ability does to each creature it hits.</summary>
[Polymorphic]
public abstract class AbilityEffect
{
    [Tooltip("Who in the area this effect touches.")]
    public bool AffectsEnemies { get; set; } = true;
    public bool AffectsAllies { get; set; }
    public abstract string Describe(DefRegistry defs);
}

[TypeId("Damage"), Label("Deal damage")]
public sealed class DamageEffect : AbilityEffect
{
    [Range(0, 9999)] public int Min { get; set; } = 1;
    [Range(0, 9999)] public int Max { get; set; } = 3;
    [Ref(typeof(ElementDef))] public string Element { get; set; } = "";
    public override string Describe(DefRegistry d) => $"{Min}-{Max} {(string.IsNullOrEmpty(Element) ? "" : Element + " ")}damage";
}

[TypeId("Heal"), Label("Heal")]
public sealed class HealEffect : AbilityEffect
{
    public HealEffect() { AffectsEnemies = false; AffectsAllies = true; }
    [Range(0, 9999)] public int Min { get; set; } = 1;
    [Range(0, 9999)] public int Max { get; set; } = 3;
    public override string Describe(DefRegistry d) => $"Heal {Min}-{Max}";
}

[TypeId("ApplyStatus"), Label("Apply status effect")]
public sealed class ApplyStatusEffect : AbilityEffect
{
    [Ref(typeof(StatusDef)), Required] public string Status { get; set; } = "";
    [Tooltip("0 = the status's default duration.")] public int Duration { get; set; }
    [Range(0, 100), Tooltip("Chance in percent.")] public int Chance { get; set; } = 100;
    public override string Describe(DefRegistry d) => $"Apply {DescribeHelpCombat.Name<StatusDef>(d, Status)}{(Chance < 100 ? $" ({Chance}%)" : "")}";
}

/// <summary>An attack, spell or skill used in combat (monsters, summons, aspects, items).</summary>
[DefFolder("abilities", "Abilities")]
[Tooltip("An attack or skill usable in combat.")]
public sealed class AbilityDef : Def
{
    [Multiline] public string Description { get; set; } = "";
    [Texture] public string Icon { get; set; } = "";

    [Range(0, 99)] public int ApCost { get; set; } = 1;
    [Range(0, 30)] public int MinRange { get; set; } = 1;
    [Range(0, 30)] public int MaxRange { get; set; } = 1;
    [Tooltip("Other creatures in the way block it.")] public bool NeedsLineOfSight { get; set; } = true;
    public TargetKind Target { get; set; } = TargetKind.Enemy;
    public AreaShape Area { get; set; } = AreaShape.Single;
    [Range(0, 10), Tooltip("For Cross / Diamond areas.")] public int AreaSize { get; set; }

    [Range(0, 20), Tooltip("Turns before it can be used again (0 = none).")] public int Cooldown { get; set; }
    [Range(0, 20), Tooltip("Max uses per turn (0 = unlimited).")] public int UsesPerTurn { get; set; }

    public List<AbilityEffect> Effects { get; set; } = new();

    [Group("Presentation")] public AnimState Animation { get; set; } = AnimState.Attack;
    [Group("Presentation"), Tooltip("Milliseconds into the animation when the hit lands.")]
    public int HitDelayMs { get; set; } = 400;
}

/// <summary>Per-monster override of how much damage it takes from an element.</summary>
public sealed class ResistanceOverride
{
    [Ref(typeof(ElementDef)), Required] public string Element { get; set; } = "";
    [Range(-100, 500), Tooltip("Damage taken change, + more, - less.")] public int Percent { get; set; }
}

/// <summary>A creature: its stats, abilities, AI priorities and how it looks.</summary>
[DefFolder("monsters", "Monsters")]
[Tooltip("A creature that can be fought and (maybe) summoned.")]
public sealed class MonsterDef : Def
{
    [Multiline] public string Description { get; set; } = "";
    [Ref(typeof(AnimationDef))] public string Animation { get; set; } = "";
    [Texture, Tooltip("Small portrait for turn order and buttons.")] public string Icon { get; set; } = "";
    [Ref(typeof(ElementDef))] public string Element { get; set; } = "";

    [Group("Stats"), Range(1, 99999)] public int Health { get; set; } = 10;
    [Group("Stats"), Range(0, 99), Tooltip("Ability Power: spent on abilities each turn.")] public int AP { get; set; } = 3;
    [Group("Stats"), Range(0, 99), Tooltip("Movement Points: cells it can walk each turn.")] public int MP { get; set; } = 3;
    [Group("Stats"), Range(0, 999), Tooltip("Higher acts earlier.")] public int Initiative { get; set; } = 10;
    [Group("Stats")] public List<ResistanceOverride> Resistances { get; set; } = new();

    [Ref(typeof(AbilityDef))] public List<string> Abilities { get; set; } = new();

    [Tooltip("What it does on its turn, top rule first. It keeps taking the first possible rule until none is possible, then ends its turn.")]
    public List<AiRule> AiPriorities { get; set; } = new();

    [Group("As a summon"), Tooltip("AP the player spends to summon it.")] public int SummonApCost { get; set; } = 2;
    [Group("As a summon"), Tooltip("Max cells away from the player it can be summoned.")] public int SummonRange { get; set; } = 2;
    [Group("As a summon")] public Progression.SummonSettings Summon { get; set; } = new();
}

public enum RespawnRule
{
    [Label("Respawns (normal monsters)")] Respawns,
    [Label("Never comes back once beaten")] Never,
    [Label("Only comes back through a rule (e.g. revive item)")] OnlyByRule,
}

/// <summary>A group of monsters fought together. Shown in the world by a monster group placement.</summary>
[DefFolder("encounters", "Encounters")]
[Tooltip("A monster group you fight together.")]
public sealed class EncounterDef : Def
{
    public List<EncounterSlot> Monsters { get; set; } = new();
    [Tooltip("Can the player run away from this fight?")] public bool CanFlee { get; set; } = true;
    public RespawnRule Respawn { get; set; } = RespawnRule.Respawns;
    [Tooltip("Runs when the player wins (give items, set flags...).")]
    public List<Outcome> OnVictory { get; set; } = new();
    [Tooltip("Runs when the player loses.")]
    public List<Outcome> OnDefeat { get; set; } = new();
}

public sealed class EncounterSlot
{
    [Ref(typeof(MonsterDef)), Required] public string Monster { get; set; } = "";
    [Range(1, 20)] public int Count { get; set; } = 1;
    public override string ToString() => $"{Count}x {Monster}";
}

internal static class DescribeHelpCombat
{
    public static string Name<T>(DefRegistry defs, string? id) where T : Def =>
        string.IsNullOrEmpty(id) ? "(none)" : defs.TryGet<T>(id, out var d) ? d.DisplayName : $"'{id}' (missing!)";
}
