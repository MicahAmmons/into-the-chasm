using Chasm.Core.Content;
using Chasm.Core.Grid;

namespace Chasm.Core.Combat;

/// <summary>Everything needed to start a fight.</summary>
public sealed class CombatSetup
{
    public required DefRegistry Defs { get; init; }
    public required Func<CellCoord, bool> Walkable { get; init; }
    public string PlayerName { get; init; } = "You";
    public int PlayerMaxHp { get; init; } = 20;
    public int PlayerHp { get; init; } = 20;
    public int PlayerAp { get; init; } = 6;
    public int PlayerMp { get; init; } = 3;
    public int PlayerInitiative { get; init; } = 10;
    public required CellCoord PlayerCell { get; init; }
    public List<(string Monster, CellCoord Cell)> Enemies { get; init; } = new();
    public ISummonPolicy Summons { get; init; } = new UnlimitedSummons(Array.Empty<string>());
    public bool CanFlee { get; init; } = true;
    public int Seed { get; init; } = Environment.TickCount;
}

public readonly record struct ActionResult(bool Ok, string Reason, IReadOnlyList<CombatEvent> Events)
{
    public static ActionResult Fail(string reason) => new(false, reason, Array.Empty<CombatEvent>());
}

/// <summary>
/// The rules of a fight, with no drawing. Call <see cref="Apply"/> with the current combatant's
/// action; it validates, resolves instantly and returns what happened as events.
///
/// Turn flow: start of turn (AP/MP refill + status modifiers, start-of-turn ticks, cooldowns) ->
/// actions -> end turn (end-of-turn ticks, statuses count down) -> next in initiative order.
/// Summons act right after the player. Victory: no enemies left. Defeat: the player reaches 0 HP.
/// </summary>
public sealed class CombatEngine
{
    private readonly CombatSetup _setup;
    private readonly List<Combatant> _units = new();
    private readonly List<int> _order = new();
    private readonly Random _rng;
    private int _turn;
    private int _nextId = 1;
    private readonly List<CombatEvent> _log = new();
    private List<CombatEvent> _pending = new();

    public DefRegistry Defs => _setup.Defs;
    public IReadOnlyList<Combatant> Units => _units;
    public IEnumerable<Combatant> Alive => _units.Where(u => !u.IsDead);
    public IReadOnlyList<CombatEvent> Log => _log;
    public int Round { get; private set; } = 1;
    public CombatOutcome? Outcome { get; private set; }
    public bool CanFlee => _setup.CanFlee;
    public ISummonPolicy SummonPolicy => _setup.Summons;
    public Combatant Player { get; }

    /// <summary>Whose turn it is, in initiative order.</summary>
    public IEnumerable<Combatant> TurnOrder => _order.Select(Unit);
    public Combatant Current => Unit(_order[_turn]);

    public CombatEngine(CombatSetup setup)
    {
        _setup = setup;
        _rng = new Random(setup.Seed);
        Player = Add(new Combatant
        {
            Id = _nextId++, Name = setup.PlayerName, Team = Team.Player, Kind = CombatantKind.Player,
            MaxHp = setup.PlayerMaxHp, Hp = Math.Min(setup.PlayerHp, setup.PlayerMaxHp),
            BaseAp = setup.PlayerAp, BaseMp = setup.PlayerMp, Initiative = setup.PlayerInitiative, Cell = setup.PlayerCell,
        });
        foreach (var (monster, cell) in setup.Enemies)
            Add(CreateMonster(monster, Team.Enemy, CombatantKind.Monster, cell, null));
    }

    private Combatant CreateMonster(string monsterId, Team team, CombatantKind kind, CellCoord cell, int? summoner)
    {
        var def = Defs.Get<MonsterDef>(monsterId);
        int count = _units.Count(u => u.Monster?.Id == monsterId) + 1;
        return new Combatant
        {
            Id = _nextId++, Name = count > 1 ? $"{def.DisplayName} {count}" : def.DisplayName, Team = team, Kind = kind,
            Monster = def, SummonerId = summoner, MaxHp = def.Health, Hp = def.Health, BaseAp = def.AP, BaseMp = def.MP,
            Initiative = def.Initiative, Cell = cell, Element = def.Element,
        };
    }

    private Combatant Add(Combatant c) { _units.Add(c); return c; }

    public Combatant Unit(int id) => _units.First(u => u.Id == id);
    public Combatant? At(CellCoord cell) => _units.FirstOrDefault(u => !u.IsDead && u.Cell == cell);
    public bool Walkable(CellCoord c) => IsoGrid.InTile(c) && _setup.Walkable(c);
    public bool Passable(CellCoord c) => Walkable(c) && At(c) is null;

    /// <summary>Begin the fight: build initiative order and start the first turn.</summary>
    public IReadOnlyList<CombatEvent> Start()
    {
        _pending = new();
        // Highest initiative first; ties go to the player's side.
        _order.AddRange(_units.OrderByDescending(u => u.Initiative).ThenBy(u => u.Team).Select(u => u.Id));
        _turn = 0;
        BeginTurn();
        return Flush();
    }

    // ------------------------------------------------------------------ queries

    public Dictionary<CellCoord, int> Reachable(Combatant c) =>
        TacticalGrid.Reachable(c.Cell, c.Rooted ? 0 : c.Mp, Passable);

    public List<CellCoord>? PathTo(Combatant c, CellCoord dest)
    {
        var path = TacticalGrid.Path(c.Cell, dest, Passable);
        return path is not null && path.Count <= (c.Rooted ? 0 : c.Mp) ? path : null;
    }

    /// <summary>
    /// True walking distance (in steps) from every cell to <paramref name="target"/>, going around
    /// walls and other creatures (the mover itself doesn't block). Used by the AI so monsters walk
    /// around obstacles instead of getting stuck behind them.
    /// </summary>
    public Dictionary<CellCoord, int> WalkDistanceTo(CellCoord target, Combatant mover)
    {
        var dist = new Dictionary<CellCoord, int> { [target] = 0 };
        var queue = new Queue<CellCoord>();
        queue.Enqueue(target);
        while (queue.Count > 0)
        {
            var c = queue.Dequeue();
            foreach (var n in c.OrthogonalNeighbours)
            {
                if (dist.ContainsKey(n) || !Walkable(n)) continue;
                var occupant = At(n);
                if (occupant is not null && occupant != mover) continue;
                dist[n] = dist[c] + 1;
                queue.Enqueue(n);
            }
        }
        return dist;
    }

    public IEnumerable<AbilityDef> Abilities(Combatant c) =>
        (c.Monster?.Abilities ?? new List<string>())
        .Select(id => Defs.TryGet<AbilityDef>(id, out var a) ? a : null).Where(a => a is not null)!;

    public bool HasLineOfSight(CellCoord from, CellCoord to)
    {
        foreach (var cell in LineOfSight.CellsAlong(IsoGrid.CellCenter(from), IsoGrid.CellCenter(to)))
            if (cell != from && cell != to && At(cell) is not null) return false;
        return true;
    }

    public IEnumerable<CellCoord> RangeCells(Combatant c, AbilityDef a) =>
        TacticalGrid.InRange(c.Cell, a.MinRange, a.MaxRange)
            .Where(cell => !a.NeedsLineOfSight || HasLineOfSight(c.Cell, cell));

    public IEnumerable<CellCoord> AffectedCells(AbilityDef a, CellCoord center) => a.Area switch
    {
        AreaShape.Cross => IsoGrid.AllCells.Where(c => (c.Q == center.Q || c.R == center.R) && c.ManhattanTo(center) <= a.AreaSize),
        AreaShape.Diamond => IsoGrid.AllCells.Where(c => c.ManhattanTo(center) <= a.AreaSize),
        _ => new[] { center },
    };

    public bool CanUseAbility(Combatant c, AbilityDef a, CellCoord target, out string reason)
    {
        reason = "";
        if (!Abilities(c).Contains(a)) { reason = "Can't use that."; return false; }
        if (c.Ap < a.ApCost) { reason = $"Needs {a.ApCost} AP."; return false; }
        if (c.Cooldowns.TryGetValue(a.Id, out int cd) && cd > 0) { reason = $"Ready in {cd} turn(s)."; return false; }
        if (a.UsesPerTurn > 0 && c.UsesThisTurn.GetValueOrDefault(a.Id) >= a.UsesPerTurn) { reason = "No uses left this turn."; return false; }
        int dist = c.Cell.ManhattanTo(target);
        if (a.Target == TargetKind.Self) { if (target != c.Cell) { reason = "Targets self."; return false; } return true; }
        if (dist < a.MinRange || dist > a.MaxRange) { reason = "Out of range."; return false; }
        if (a.NeedsLineOfSight && !HasLineOfSight(c.Cell, target)) { reason = "Something is in the way."; return false; }
        var there = At(target);
        switch (a.Target)
        {
            case TargetKind.Enemy when there is null || there.Team == c.Team: reason = "Target an enemy."; return false;
            case TargetKind.Ally when there is null || there.Team != c.Team: reason = "Target an ally."; return false;
            case TargetKind.AnyUnit when there is null: reason = "Target a creature."; return false;
            case TargetKind.EmptyCell when there is not null || !Walkable(target): reason = "Target an empty cell."; return false;
            case TargetKind.AnyCell when !IsoGrid.InTile(target): reason = "Off the map."; return false;
        }
        return true;
    }

    public int SummonsOnField(string monsterId) =>
        Alive.Count(u => u.Kind == CombatantKind.Summon && u.Monster?.Id == monsterId);

    public bool CanSummon(Combatant c, string monsterId, CellCoord cell, out string reason)
    {
        reason = "";
        if (c.Kind != CombatantKind.Player) { reason = "Only you can summon."; return false; }
        if (!Defs.TryGet<MonsterDef>(monsterId, out var m)) { reason = "Unknown creature."; return false; }
        if (!SummonPolicy.CanSummon(monsterId, SummonsOnField(monsterId), out reason)) return false;
        if (c.Ap < m.SummonApCost) { reason = $"Needs {m.SummonApCost} AP."; return false; }
        if (!Passable(cell)) { reason = "That cell isn't free."; return false; }
        if (c.Cell.ManhattanTo(cell) > m.SummonRange) { reason = $"Must be within {m.SummonRange} cells."; return false; }
        return true;
    }

    /// <summary>Checks an action without doing it.</summary>
    public string? Validate(CombatAction action)
    {
        if (Outcome is not null) return "The fight is over.";
        var c = Current;
        switch (action)
        {
            case MoveAction m:
                if (m.Path.Count == 0) return "Nowhere to go.";
                if (c.Rooted) return "Rooted in place.";
                if (m.Path.Count > c.Mp) return "Not enough MP.";
                var prev = c.Cell;
                foreach (var step in m.Path)
                {
                    if (step.ManhattanTo(prev) != 1) return "Path isn't connected.";
                    if (!Passable(step)) return "The way is blocked.";
                    prev = step;
                }
                return null;
            case UseAbilityAction u:
                if (!Defs.TryGet<AbilityDef>(u.Ability, out var a)) return "Unknown ability.";
                return CanUseAbility(c, a, u.Target, out var r) ? null : r;
            case SummonAction s:
                return CanSummon(c, s.Monster, s.Cell, out var why) ? null : why;
            case FleeAction:
                if (c.Kind != CombatantKind.Player) return "Only you can flee.";
                return CanFlee ? null : "You can't flee this fight!";
            case EndTurnAction:
                return null;
        }
        return "Unknown action.";
    }

    // ------------------------------------------------------------------ resolve

    public ActionResult Apply(CombatAction action)
    {
        var problem = Validate(action);
        if (problem is not null) return ActionResult.Fail(problem);
        _pending = new();
        var c = Current;
        switch (action)
        {
            case MoveAction m:
                c.Mp -= m.Path.Count;
                c.Cell = m.Path[^1];
                Emit(new MovedEvent(c.Id, m.Path.ToList()));
                break;
            case UseAbilityAction u:
                UseAbility(c, Defs.Get<AbilityDef>(u.Ability), u.Target);
                break;
            case SummonAction s:
                DoSummon(c, s.Monster, s.Cell);
                break;
            case FleeAction:
                End(CombatOutcome.Fled);
                break;
            case EndTurnAction:
                EndTurn();
                break;
        }
        CheckEnd();
        return new ActionResult(true, "", Flush());
    }

    private void UseAbility(Combatant user, AbilityDef a, CellCoord target)
    {
        user.Ap -= a.ApCost;
        if (a.Cooldown > 0) user.Cooldowns[a.Id] = a.Cooldown;
        user.UsesThisTurn[a.Id] = user.UsesThisTurn.GetValueOrDefault(a.Id) + 1;
        var hit = AffectedCells(a, target).Select(At).Where(u => u is not null).Cast<Combatant>().ToList();
        Emit(new AbilityUsedEvent(user.Id, a.Id, target, hit.Select(h => h.Id).ToList()));
        foreach (var effect in a.Effects)
            foreach (var t in hit)
            {
                if (t.IsDead) continue;
                bool ally = t.Team == user.Team;
                if (ally ? !effect.AffectsAllies : !effect.AffectsEnemies) continue;
                ApplyEffect(user, t, effect, a.Id);
            }
    }

    private void ApplyEffect(Combatant user, Combatant target, AbilityEffect effect, string abilityId)
    {
        switch (effect)
        {
            case DamageEffect d:
                int roll = _rng.Next(Math.Min(d.Min, d.Max), Math.Max(d.Min, d.Max) + 1);
                DealDamage(target, roll, d.Element, user, abilityId, user.DamageDealtPercent);
                break;
            case HealEffect h:
                Heal(target, _rng.Next(Math.Min(h.Min, h.Max), Math.Max(h.Min, h.Max) + 1), user.Id);
                break;
            case ApplyStatusEffect s:
                if (_rng.Next(100) < s.Chance && Defs.TryGet<StatusDef>(s.Status, out var sd))
                    AddStatus(target, sd, s.Duration > 0 ? s.Duration : sd.DefaultDuration, user.Id);
                break;
        }
    }

    /// <summary>Final damage = roll x (attacker bonus) x (element + resistance + status modifiers on the target).</summary>
    public int CalculateDamage(Combatant target, int roll, string element, int attackerDealtPercent)
    {
        int takenPercent = target.DamageTakenPercent;
        if (!string.IsNullOrEmpty(element))
        {
            if (Defs.TryGet<ElementDef>(target.Element, out var el)) takenPercent += el.PercentFrom(element);
            if (target.Monster is not null)
                takenPercent += target.Monster.Resistances.Where(r => r.Element == element).Sum(r => r.Percent);
        }
        double value = roll * (100 + attackerDealtPercent) / 100.0 * (100 + takenPercent) / 100.0;
        return Math.Max(0, (int)Math.Round(value, MidpointRounding.AwayFromZero));
    }

    private void DealDamage(Combatant target, int roll, string element, Combatant? source, string? abilityId, int dealtPercent)
    {
        int amount = CalculateDamage(target, roll, element, dealtPercent);
        target.Hp = Math.Max(0, target.Hp - amount);
        Emit(new DamagedEvent(target.Id, amount, element, target.Hp, source?.Id, abilityId));
        if (target.IsDead) Kill(target, source?.Id);
    }

    private void Heal(Combatant target, int amount, int? source)
    {
        int before = target.Hp;
        target.Hp = Math.Min(target.MaxHp, target.Hp + amount);
        Emit(new HealedEvent(target.Id, target.Hp - before, target.Hp, source));
    }

    private void AddStatus(Combatant target, StatusDef def, int turns, int source)
    {
        var existing = target.Statuses.FirstOrDefault(s => s.Def == def);
        if (existing is not null && !def.Stacks) existing.TurnsLeft = Math.Max(existing.TurnsLeft, turns);
        else target.Statuses.Add(new StatusInstance { Def = def, TurnsLeft = turns, SourceId = source });
        Emit(new StatusAppliedEvent(target.Id, def.Id, turns));
    }

    private void Kill(Combatant c, int? killer)
    {
        Emit(new DiedEvent(c.Id, killer));
        c.Statuses.Clear();
        int idx = _order.IndexOf(c.Id);
        if (idx >= 0)
        {
            _order.RemoveAt(idx);
            if (idx < _turn) _turn--;
            if (_turn >= _order.Count) _turn = 0;
        }
        // Summons vanish with their summoner.
        foreach (var s in _units.Where(u => u.SummonerId == c.Id && !u.IsDead).ToList()) { s.Hp = 0; Kill(s, null); }
    }

    private void DoSummon(Combatant summoner, string monsterId, CellCoord cell)
    {
        var def = Defs.Get<MonsterDef>(monsterId);
        summoner.Ap -= def.SummonApCost;
        var unit = Add(CreateMonster(monsterId, summoner.Team, CombatantKind.Summon, cell, summoner.Id));
        unit.Ap = unit.BaseAp;
        unit.Mp = unit.BaseMp;
        // Acts right after its summoner (newest first).
        _order.Insert(_order.IndexOf(summoner.Id) + 1, unit.Id);
        SummonPolicy.OnSummoned(monsterId);
        Emit(new SummonedEvent(unit.Id, summoner.Id, monsterId, cell));
    }

    private void EndTurn()
    {
        var c = Current;
        Tick(c, StatusTick.EndOfTurn);
        if (!c.IsDead)
        {
            foreach (var s in c.Statuses.ToList())
                if (--s.TurnsLeft <= 0) { c.Statuses.Remove(s); Emit(new StatusExpiredEvent(c.Id, s.Def.Id)); }
            c.UsesThisTurn.Clear();
            Emit(new TurnEndedEvent(c.Id));
        }
        if (CheckEnd() || _order.Count == 0) return;
        // If the current unit died during its own end-of-turn tick, _turn already points at the next one.
        if (!c.IsDead) _turn++;
        if (_turn >= _order.Count) { _turn = 0; Round++; }
        BeginTurn();
    }

    private void BeginTurn()
    {
        for (int guard = 0; guard < 64 && Outcome is null; guard++)
        {
            var c = Current;
            c.Ap = Math.Max(0, c.BaseAp + c.Statuses.Sum(s => s.Def.ApChange));
            c.Mp = Math.Max(0, c.BaseMp + c.Statuses.Sum(s => s.Def.MpChange));
            foreach (var key in c.Cooldowns.Keys.ToList()) if (c.Cooldowns[key] > 0) c.Cooldowns[key]--;
            Emit(new TurnStartedEvent(c.Id, Round));
            Tick(c, StatusTick.StartOfTurn);
            if (CheckEnd()) return;
            if (!c.IsDead) return;
            // Died to a start-of-turn tick: Kill() already moved _turn to the next unit.
            if (_turn == 0) Round++;
        }
    }

    private void Tick(Combatant c, StatusTick when)
    {
        foreach (var s in c.Statuses.Where(s => s.Def.TickAt == when).ToList())
        {
            if (c.IsDead) return;
            if (s.Def.TickDamage > 0) DealDamage(c, s.Def.TickDamage, s.Def.TickElement, _units.FirstOrDefault(u => u.Id == s.SourceId), null, 0);
            if (s.Def.TickHeal > 0 && !c.IsDead) Heal(c, s.Def.TickHeal, s.SourceId);
        }
    }

    private bool CheckEnd()
    {
        if (Outcome is not null) return true;
        if (Player.IsDead) { End(CombatOutcome.Defeat); return true; }
        if (!Alive.Any(u => u.Team == Team.Enemy)) { End(CombatOutcome.Victory); return true; }
        return false;
    }

    private void End(CombatOutcome outcome)
    {
        if (Outcome is not null) return;
        Outcome = outcome;
        Emit(new CombatEndedEvent(outcome));
    }

    private void Emit(CombatEvent e) { _pending.Add(e); _log.Add(e); }

    private IReadOnlyList<CombatEvent> Flush()
    {
        var list = _pending;
        _pending = new();
        return list;
    }
}
