using Chasm.Core.Grid;

namespace Chasm.Core.Combat;

public enum CombatOutcome { Victory, Defeat, Fled }

/// <summary>
/// Everything that happens in a fight is reported as events, in order. The rules engine resolves
/// instantly; the combat scene replays these with animations; tests just read them.
/// </summary>
public abstract record CombatEvent;

public sealed record TurnStartedEvent(int Unit, int Round) : CombatEvent;
public sealed record TurnEndedEvent(int Unit) : CombatEvent;
public sealed record MovedEvent(int Unit, IReadOnlyList<CellCoord> Path) : CombatEvent;
public sealed record AbilityUsedEvent(int Unit, string Ability, CellCoord Target, IReadOnlyList<int> Hit) : CombatEvent;
public sealed record DamagedEvent(int Unit, int Amount, string Element, int HpAfter, int? Source, string? Ability) : CombatEvent;
public sealed record HealedEvent(int Unit, int Amount, int HpAfter, int? Source) : CombatEvent;
public sealed record StatusAppliedEvent(int Unit, string Status, int Turns) : CombatEvent;
public sealed record StatusExpiredEvent(int Unit, string Status) : CombatEvent;
public sealed record DiedEvent(int Unit, int? Killer) : CombatEvent;
public sealed record SummonedEvent(int Unit, int Summoner, string Monster, CellCoord Cell) : CombatEvent;
public sealed record CombatEndedEvent(CombatOutcome Outcome) : CombatEvent;
public sealed record LogEvent(string Text) : CombatEvent;
