using Chasm.Core.Combat;
using Chasm.Core.Content;
using Chasm.Core.Grid;

namespace Chasm.Core.Tests;

public class CombatTests
{
    private static DefRegistry Defs()
    {
        var d = new DefRegistry();
        d.Put(new ElementDef { Id = "fire" });
        d.Put(new ElementDef { Id = "ice", DamageTakenFrom = { new ElementModifier { Element = "fire", Percent = 40 } } });
        d.Put(new StatusDef { Id = "burn", DefaultDuration = 2, TickDamage = 2, TickElement = "fire", Stacks = false });
        d.Put(new StatusDef { Id = "shell", DamageTakenPercent = -50, DefaultDuration = 1, IsBuff = true });
        d.Put(new AbilityDef { Id = "slam", ApCost = 2, MinRange = 1, MaxRange = 1, Effects = { new DamageEffect { Min = 3, Max = 3 } } });
        d.Put(new AbilityDef { Id = "fireball", ApCost = 3, MinRange = 2, MaxRange = 6, Effects = { new DamageEffect { Min = 5, Max = 5, Element = "fire" }, new ApplyStatusEffect { Status = "burn" } } });
        d.Put(new AbilityDef { Id = "harden", ApCost = 1, Target = TargetKind.Self, MinRange = 0, MaxRange = 0,
            Effects = { new ApplyStatusEffect { Status = "shell", AffectsAllies = true, AffectsEnemies = false } } });
        d.Put(new MonsterDef { Id = "frost_ooze", Name = "Frost Ooze", Element = "ice", Health = 10, AP = 4, MP = 3, Initiative = 5, Abilities = { "slam" } });
        d.Put(new MonsterDef { Id = "turtle", Name = "Turtle", Health = 12, AP = 4, MP = 2, Initiative = 1, Abilities = { "slam", "harden" }, SummonApCost = 3, SummonRange = 2 });
        d.Put(new MonsterDef { Id = "fire_ooze", Name = "Fire Ooze", Element = "fire", Health = 8, AP = 6, MP = 3, Initiative = 8, Abilities = { "fireball" },
            AiPriorities = { new UseAbilityRule(), new KeepAtRangeRule { UseAbilityRange = "fireball" }, new UseAbilityRule() } });
        return d;
    }

    private static CombatEngine Fight(DefRegistry d, CellCoord player, params (string, CellCoord)[] enemies) =>
        new(new CombatSetup
        {
            Defs = d, Walkable = _ => true, PlayerCell = player, PlayerHp = 20, PlayerMaxHp = 20, PlayerAp = 6, PlayerMp = 3,
            PlayerInitiative = 10, Enemies = enemies.ToList(), Summons = new UnlimitedSummons(new[] { "turtle" }), Seed = 1,
        });

    [Fact]
    public void Initiative_order_and_player_moves_with_mp()
    {
        var e = Fight(Defs(), new CellCoord(10, 2), ("frost_ooze", new CellCoord(16, 2)));
        e.Start();
        Assert.Equal(CombatantKind.Player, e.Current.Kind); // initiative 10 > 5
        Assert.False(e.Apply(new MoveAction(new[] { new CellCoord(11, 2), new CellCoord(12, 2), new CellCoord(13, 2), new CellCoord(14, 2) })).Ok);
        var ok = e.Apply(new MoveAction(new[] { new CellCoord(11, 2), new CellCoord(12, 2) }));
        Assert.True(ok.Ok);
        Assert.Equal(1, e.Player.Mp);
    }

    [Fact]
    public void Element_weakness_and_status_ticks()
    {
        var d = Defs();
        var e = Fight(d, new CellCoord(10, 2), ("frost_ooze", new CellCoord(14, 2)));
        var ooze = e.Units.Single(u => u.Kind == CombatantKind.Monster);
        Assert.Equal(7, e.CalculateDamage(ooze, 5, "fire", 0)); // 5 * 1.4
        Assert.Equal(5, e.CalculateDamage(ooze, 5, "", 0));
    }

    [Fact]
    public void Summon_acts_right_after_player_and_costs_ap()
    {
        var e = Fight(Defs(), new CellCoord(10, 2), ("frost_ooze", new CellCoord(18, 2)));
        e.Start();
        var r = e.Apply(new SummonAction("turtle", new CellCoord(11, 2)));
        Assert.True(r.Ok, r.Reason);
        Assert.Equal(3, e.Player.Ap);
        Assert.False(e.Apply(new SummonAction("turtle", new CellCoord(10, 3))).Ok); // max 1 on field
        var order = e.TurnOrder.Select(u => u.DefId).ToList();
        Assert.Equal(new[] { "player", "turtle", "frost_ooze" }, order);
        e.Apply(new EndTurnAction());
        Assert.Equal("turtle", e.Current.DefId);
    }

    [Fact]
    public void Self_buff_halves_damage_then_expires()
    {
        var e = Fight(Defs(), new CellCoord(10, 2), ("frost_ooze", new CellCoord(18, 2)));
        e.Start();
        e.Apply(new SummonAction("turtle", new CellCoord(11, 2)));
        e.Apply(new EndTurnAction());
        var turtle = e.Current;
        Assert.True(e.Apply(new UseAbilityAction("harden", turtle.Cell)).Ok);
        Assert.Equal(2, e.CalculateDamage(turtle, 3, "", 0)); // 3 * 0.5 rounded
        e.Apply(new EndTurnAction());
        Assert.Empty(turtle.Statuses); // 1-turn buff expired at end of its turn
    }

    [Fact]
    public void Ranged_ai_keeps_distance_and_burns()
    {
        var e = Fight(Defs(), new CellCoord(10, 2), ("fire_ooze", new CellCoord(12, 2)));
        e.Start();
        e.Apply(new EndTurnAction()); // player passes
        var events = CombatAi.PlayTurn(e);
        Assert.True(events.Any(ev => ev is AbilityUsedEvent), string.Join("\n", e.Log) + "\n" + string.Join("\n", e.Units));
        Assert.Contains(events, ev => ev is AbilityUsedEvent { Ability: "fireball" });
        Assert.Contains(events, ev => ev is StatusAppliedEvent { Status: "burn" });
        // AP 6 / fireball 3 = two casts of 5; burn doesn't stack, ticks 2 when the player's turn starts.
        Assert.Equal(2, events.Count(ev => ev is AbilityUsedEvent));
        Assert.Equal(8, e.Player.Hp);
        Assert.True(e.Units.Single(u => u.DefId == "fire_ooze").Cell.ManhattanTo(e.Player.Cell) > 2, "should back off to range");
    }

    [Fact]
    public void Ai_vs_ai_fight_always_finishes()
    {
        var d = Defs();
        for (int seed = 0; seed < 20; seed++)
        {
            var e = new CombatEngine(new CombatSetup
            {
                Defs = d, Walkable = c => !(c.Q == 15 && c.R is > -2 and < 5), PlayerCell = new CellCoord(10, 2), Seed = seed,
                Enemies = { ("frost_ooze", new CellCoord(20, 2)), ("fire_ooze", new CellCoord(20, 4)) },
                Summons = new UnlimitedSummons(new[] { "turtle" }),
            });
            e.Start();
            for (int step = 0; step < 400 && e.Outcome is null; step++)
            {
                if (e.Current.Kind == CombatantKind.Player)
                {
                    var free = e.Reachable(e.Player).Keys.FirstOrDefault(c => c != e.Player.Cell && c.ManhattanTo(e.Player.Cell) == 1);
                    if (e.SummonsOnField("turtle") == 0 && e.Apply(new SummonAction("turtle", free)).Ok) continue;
                    e.Apply(new EndTurnAction());
                }
                else CombatAi.PlayTurn(e);
            }
            Assert.True(e.Outcome is not null, $"seed {seed} round {e.Round}\n" + string.Join("\n", e.Units) + "\n" + string.Join("\n", e.Log.TakeLast(15)));
        }
    }

    [Fact]
    public void Flee_ends_fight_unless_forbidden()
    {
        var e = Fight(Defs(), new CellCoord(10, 2), ("frost_ooze", new CellCoord(16, 2)));
        e.Start();
        Assert.True(e.Apply(new FleeAction()).Ok);
        Assert.Equal(CombatOutcome.Fled, e.Outcome);

        var boss = new CombatEngine(new CombatSetup { Defs = Defs(), Walkable = _ => true, PlayerCell = new CellCoord(10, 2), CanFlee = false, Enemies = { ("frost_ooze", new CellCoord(16, 2)) } });
        boss.Start();
        Assert.False(boss.Apply(new FleeAction()).Ok);
    }

    [Fact]
    public void Killing_last_enemy_is_victory()
    {
        var e = Fight(Defs(), new CellCoord(10, 2), ("frost_ooze", new CellCoord(12, 2)));
        e.Start();
        e.Apply(new SummonAction("turtle", new CellCoord(11, 2)));
        e.Apply(new EndTurnAction());
        var ooze = e.Units.Single(u => u.Kind == CombatantKind.Monster);
        ooze.Hp = 3;
        Assert.True(e.Apply(new UseAbilityAction("slam", ooze.Cell)).Ok);
        Assert.Equal(CombatOutcome.Victory, e.Outcome);
    }
}
