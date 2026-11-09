using Chasm.Core.Grid;

namespace Chasm.Core.Combat;

public enum Team { Player, Enemy }
public enum CombatantKind { Player, Summon, Monster }

public sealed class StatusInstance
{
    public required StatusDef Def { get; init; }
    public int TurnsLeft { get; set; }
    public int SourceId { get; init; }
}

/// <summary>One creature in a fight (the player, a summon, or a monster). Pure data + small helpers.</summary>
public sealed class Combatant
{
    public required int Id { get; init; }
    public required string Name { get; init; }
    public required Team Team { get; init; }
    public required CombatantKind Kind { get; init; }
    /// <summary>The monster definition (null for the player).</summary>
    public MonsterDef? Monster { get; init; }
    public int? SummonerId { get; init; }

    public int MaxHp { get; set; }
    public int Hp { get; set; }
    public int BaseAp { get; set; }
    public int BaseMp { get; set; }
    public int Ap { get; set; }
    public int Mp { get; set; }
    public int Initiative { get; set; }
    public CellCoord Cell { get; set; }
    public string Element { get; set; } = "";

    public List<StatusInstance> Statuses { get; } = new();
    public Dictionary<string, int> Cooldowns { get; } = new();
    public Dictionary<string, int> UsesThisTurn { get; } = new();

    public bool IsDead => Hp <= 0;
    public bool PlayerControlled => Team == Team.Player;
    public string DefId => Monster?.Id ?? "player";

    public int DamageTakenPercent => Statuses.Sum(s => s.Def.DamageTakenPercent);
    public int DamageDealtPercent => Statuses.Sum(s => s.Def.DamageDealtPercent);
    public bool Rooted => Statuses.Any(s => s.Def.Rooted);

    public override string ToString() => $"{Name}#{Id} {Hp}/{MaxHp} AP{Ap} MP{Mp} @{Cell}";
}
