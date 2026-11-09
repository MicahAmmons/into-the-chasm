using Chasm.Core.Combat;
using Chasm.Core.Content;
using Chasm.Core.Rules;

namespace Chasm.Core.Progression;

/// <summary>
/// An Aspect: a power the player learns (usually through summon affinity). Can work in combat
/// (an ability the player casts with AP), out of combat (a toggle on the quick bar, e.g. "heavy"),
/// or both.
/// </summary>
[DefFolder("aspects", "Aspects")]
[Tooltip("A learnable power: in combat, out of combat, or both.")]
public sealed class AspectDef : Def
{
    [Multiline] public string Description { get; set; } = "";
    [Texture] public string Icon { get; set; } = "";

    [Group("In combat"), Ref(typeof(AbilityDef)), Tooltip("Ability the player can cast in fights. Empty = no in-combat use.")]
    public string CombatAbility { get; set; } = "";

    [Group("Out of combat"), Tooltip("Can be switched on/off from the quick bar while exploring.")]
    public bool Toggleable { get; set; }
    [Group("Out of combat"), Tooltip("Shown when switched on, e.g. 'You feel heavy as stone.'")]
    public string OnMessage { get; set; } = "";
    [Group("Out of combat")] public string OffMessage { get; set; } = "";
    [Group("Out of combat"), Tooltip("Rules run when it's switched on (first passing rule runs). Use for 'only works here' effects.")]
    public List<RuleDef> OnActivate { get; set; } = new();
}

/// <summary>One step on a summon's affinity track: what the player gains when reaching it.</summary>
public sealed class AffinityLevelDef
{
    [Range(1, 1000000), Tooltip("Total affinity XP needed to reach this level.")]
    public int XpRequired { get; set; } = 100;
    [Tooltip("Extra uses per rest.")] public int ExtraDailyUses { get; set; }
    [Tooltip("Extra copies allowed on the field at once.")] public int ExtraOnField { get; set; }
    public int BonusHp { get; set; }
    public int BonusAp { get; set; }
    public int BonusMp { get; set; }
    [Ref(typeof(AbilityDef)), Tooltip("Ability the summon gains.")] public string GrantAbility { get; set; } = "";
    [Ref(typeof(AspectDef)), Tooltip("Aspect the player learns.")] public string GrantAspect { get; set; } = "";
    [Tooltip("Message shown when this level is reached.")] public string Message { get; set; } = "";
    public override string ToString() => $"{XpRequired} xp";
}

/// <summary>How a creature behaves as the player's summon: uses, field cap, affinity track.</summary>
public sealed class SummonSettings
{
    [Range(0, 99), Tooltip("Uses per rest when first acquired.")] public int BaseDailyUses { get; set; } = 1;
    [Range(1, 20), Tooltip("Copies allowed on the field at once when first acquired.")] public int BaseOnField { get; set; } = 1;
    [Tooltip("Affinity XP each time it's summoned in a fight you don't flee.")] public int XpPerSummon { get; set; } = 10;
    public List<AffinityLevelDef> AffinityLevels { get; set; } = new();
}
