using Chasm.Core.Combat;
using Chasm.Core.Content;
using Chasm.Core.Grid;
using Chasm.Core.Progression;
using Chasm.Core.Rules;
using Chasm.Core.Session;

namespace Chasm.Core.Tests;

public class ProgressionTests
{
    private static DefRegistry Defs()
    {
        var d = new DefRegistry();
        d.Put(new GameConfigDef { Id = "game", XpForFirstLevel = 10, XpGrowth = 2, HpPerLevel = 2 });
        d.Put(new AbilityDef { Id = "bite", ApCost = 1, Effects = { new DamageEffect { Min = 2, Max = 2 } } });
        d.Put(new AbilityDef { Id = "shell", ApCost = 1, Target = TargetKind.Ally, MinRange = 0, MaxRange = 2 });
        d.Put(new AspectDef { Id = "turtle_aspect", Toggleable = true, CombatAbility = "shell" });
        d.Put(new MonsterDef
        {
            Id = "turtle", Health = 10, AP = 2, MP = 2, Abilities = { "bite" }, SummonApCost = 2,
            Summon = new SummonSettings
            {
                BaseDailyUses = 1, XpPerSummon = 10,
                AffinityLevels =
                {
                    new AffinityLevelDef { XpRequired = 10, ExtraDailyUses = 1, BonusHp = 5 },
                    new AffinityLevelDef { XpRequired = 30, GrantAspect = "turtle_aspect", ExtraOnField = 1 },
                },
            },
        });
        d.Put(new MonsterDef { Id = "ooze", Health = 1, AP = 1, MP = 1, Initiative = 0, Abilities = { "bite" } });
        return d;
    }

    [Fact]
    public void Stat_track_levels_follow_growth_curve()
    {
        var cfg = new GameConfigDef { XpForFirstLevel = 10, XpGrowth = 2 };
        Assert.Equal(0, StatTrack.LevelFor(9, cfg));
        Assert.Equal(1, StatTrack.LevelFor(10, cfg));   // 10
        Assert.Equal(1, StatTrack.LevelFor(29, cfg));
        Assert.Equal(2, StatTrack.LevelFor(30, cfg));   // 10 + 20
        Assert.Equal(3, StatTrack.LevelFor(70, cfg));   // + 40
    }

    [Fact]
    public void Summon_uses_drain_and_rest_refills()
    {
        var defs = Defs();
        var s = new GameSession(defs);
        s.Progress.GrantSummon(defs.Get<MonsterDef>("turtle"));
        var policy = new ProgressSummonPolicy(s.Progress, defs);
        Assert.True(policy.CanSummon("turtle", 0, out _));
        policy.OnSummoned("turtle");
        Assert.False(policy.CanSummon("turtle", 0, out var why));
        Assert.Contains("Rest", why);
        s.Rest();
        Assert.True(policy.CanSummon("turtle", 0, out _));
    }

    [Fact]
    public void Affinity_levels_grant_uses_hp_and_an_aspect()
    {
        var defs = Defs();
        var p = new PlayerProgress();
        var turtle = defs.Get<MonsterDef>("turtle");
        p.GrantSummon(turtle);
        Assert.Single(p.AddAffinity(turtle, 10));
        Assert.Equal(2, p.DailyUses(turtle));
        Assert.Equal(5, new ProgressSummonPolicy(p, defs).BonusFor("turtle").Hp);
        p.AddAffinity(turtle, 20);
        Assert.Contains("turtle_aspect", p.Aspects);
        Assert.Equal(2, p.FieldCap(turtle));
        Assert.Contains("shell", new ProgressSummonPolicy(p, defs).PlayerAbilities);
    }

    [Fact]
    public void Winning_grows_hp_from_damage_taken_and_affinity_from_summons()
    {
        var defs = Defs();
        var s = new GameSession(defs);
        s.Progress.GrantSummon(defs.Get<MonsterDef>("turtle"));
        var e = new CombatEngine(new CombatSetup
        {
            Defs = defs, Walkable = _ => true, PlayerCell = new CellCoord(10, 2), PlayerAp = 6, PlayerMaxHp = 20, PlayerHp = 20,
            Enemies = { ("ooze", new CellCoord(12, 2)) }, Summons = new ProgressSummonPolicy(s.Progress, defs), Seed = 1,
        });
        e.Start();
        Assert.True(e.Apply(new SummonAction("turtle", new CellCoord(11, 2))).Ok);
        e.Apply(new EndTurnAction());
        Assert.True(e.Apply(new UseAbilityAction("bite", new CellCoord(12, 2))).Ok);
        Assert.Equal(CombatOutcome.Victory, e.Outcome);
        CombatRewards.Apply(s, e, CombatOutcome.Victory);
        Assert.Equal(2, s.Progress.Ap.Xp);                         // summon cost
        Assert.Equal(10, s.Progress.Summons["turtle"].AffinityXp); // one summon
        Assert.Equal(20, s.Progress.MaxHp(s.Config)); // took no damage: no HP growth
    }

    [Fact]
    public void Ask_outcome_runs_yes_branch_and_aspect_condition()
    {
        var defs = Defs();
        var s = new GameSession(defs);
        var host = new AskHost();
        var ctx = s.CreateContext(host);
        s.Progress.Aspects.Add("turtle_aspect");
        s.Progress.ActiveAspect = "turtle_aspect";
        var jump = new RuleDef
        {
            Conditions = { new AspectActiveCondition { Aspect = "turtle_aspect" } },
            Outcomes = { new AskOutcome { Question = "Jump?", IfYes = { new FlagOutcome { Flag = "jumped" } } } },
        };
        Assert.True(jump.Passes(ctx));
        jump.Run(ctx);
        Assert.True(s.World.IsSet("jumped"));
    }

    private sealed class AskHost : IRuleHost
    {
        public void ShowMessage(IReadOnlyList<string> lines, string? speaker) { }
        public void ChangeTile(string tileId, CellCoord? arrivalCell) { }
        public void StartEncounter(string encounterId) { }
        public void StartDialogue(string dialogueId) { }
        public void Rest() { }
        public void Confirm(string question, Action<bool> answered) => answered(true);
    }
}

public class CombatItemAndTimeTests
{
    [Fact]
    public void Items_with_a_combat_ability_are_used_up_and_removed_after_the_fight()
    {
        var d = new DefRegistry();
        d.Put(new GameConfigDef { Id = "game", MinutesPerFight = 1, DayLengthMinutes = 10 });
        d.Put(new AbilityDef { Id = "throw_rock", ApCost = 1, MinRange = 1, MaxRange = 5, Effects = { new DamageEffect { Min = 1, Max = 1 } } });
        d.Put(new Chasm.Core.Items.ItemDef { Id = "rock", CombatAbility = "throw_rock", ConsumedInCombat = true });
        d.Put(new MonsterDef { Id = "blob", Health = 5, AP = 1, MP = 1, Initiative = 0 });
        var s = new GameSession(d);
        s.Inventory.Add("rock", 2);
        var e = new CombatEngine(new CombatSetup
        {
            Defs = d, Walkable = _ => true, PlayerCell = new CellCoord(10, 2), PlayerAp = 6, Seed = 1,
            Enemies = { ("blob", new CellCoord(13, 2)) }, Items = { new CombatItem("rock", "throw_rock", 2, true) },
        });
        e.Start();
        Assert.True(e.Apply(new UseAbilityAction("throw_rock", new CellCoord(13, 2))).Ok);
        Assert.True(e.Apply(new UseAbilityAction("throw_rock", new CellCoord(13, 2))).Ok);
        Assert.False(e.Apply(new UseAbilityAction("throw_rock", new CellCoord(13, 2))).Ok); // none left
        e.Apply(new FleeAction());
        float before = s.Clock.Time;
        CombatRewards.Apply(s, e, CombatOutcome.Fled);
        Assert.False(s.Inventory.Has("rock"));
        Assert.Equal(0.1f, s.Clock.Time - before, 3); // 1 of 10 minutes
    }
}
