using Chasm.Core.Grid;

namespace Chasm.Core.Combat;

/// <summary>Something the current combatant wants to do. Validated, then resolved by the engine.</summary>
public abstract record CombatAction;
public sealed record MoveAction(IReadOnlyList<CellCoord> Path) : CombatAction;
public sealed record UseAbilityAction(string Ability, CellCoord Target) : CombatAction;
public sealed record SummonAction(string Monster, CellCoord Cell) : CombatAction;
public sealed record EndTurnAction : CombatAction;
public sealed record FleeAction : CombatAction;

/// <summary>
/// Decides whether the player may summon something right now. The real game checks daily uses and
/// affinity-based field caps; sandboxes allow everything.
/// </summary>
public interface ISummonPolicy
{
    /// <summary>Monsters the player can summon at all (for the action bar).</summary>
    IEnumerable<string> Summonable { get; }
    bool CanSummon(string monsterId, int alreadyOnField, out string reason);
    void OnSummoned(string monsterId);

    /// <summary>Extra stats / abilities a summon gets from the player's affinity with it.</summary>
    SummonBonus BonusFor(string monsterId) => SummonBonus.None;

    /// <summary>Abilities the player can cast (from learned aspects).</summary>
    IEnumerable<string> PlayerAbilities => Array.Empty<string>();
}

/// <summary>An item the player brings into a fight: which ability it grants and how many.</summary>
public sealed record CombatItem(string ItemId, string AbilityId, int Count, bool Consumed);

public sealed record SummonBonus(int Hp, int Ap, int Mp, IReadOnlyList<string> Abilities)
{
    public static readonly SummonBonus None = new(0, 0, 0, Array.Empty<string>());
}

public sealed class UnlimitedSummons(IEnumerable<string> monsters, int maxPerType = 1, IEnumerable<string>? playerAbilities = null) : ISummonPolicy
{
    public IEnumerable<string> PlayerAbilities { get; } = playerAbilities?.ToList() ?? new List<string>();
    private readonly List<string> _monsters = monsters.ToList();
    public IEnumerable<string> Summonable => _monsters;
    public bool CanSummon(string id, int onField, out string reason)
    {
        reason = onField >= maxPerType ? $"Only {maxPerType} of that summon at a time." : "";
        return onField < maxPerType;
    }
    public void OnSummoned(string id) { }
}
