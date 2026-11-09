using Chasm.Core.Combat;
using Chasm.Core.Content;
using Chasm.Core.Progression;

namespace Chasm.Core.Rules;

[TypeId("SummonUnlocked"), Label("Player can summon a creature")]
public sealed class SummonUnlockedCondition : Condition
{
    [Ref(typeof(MonsterDef)), Required] public string Monster { get; set; } = "";
    public override bool Evaluate(RuleContext ctx) => ctx.Progress.HasSummon(Monster);
    public override string Describe(DefRegistry d) => $"Can summon {DescribeHelp.Name<MonsterDef>(d, Monster)}";
}

[TypeId("Affinity"), Label("Affinity with a summon")]
public sealed class AffinityCondition : Condition
{
    [Ref(typeof(MonsterDef)), Required] public string Monster { get; set; } = "";
    public Comparison Compare { get; set; } = Comparison.AtLeast;
    [Tooltip("Affinity level (0 = just bonded).")] public int Level { get; set; } = 1;
    public override bool Evaluate(RuleContext ctx) =>
        ctx.Progress.Summons.TryGetValue(Monster, out var sp) && ctx.Defs.TryGet<MonsterDef>(Monster, out var m)
        && Compare.Test(PlayerProgress.AffinityLevel(m, sp.AffinityXp), Level);
    public override string Describe(DefRegistry d) => $"{DescribeHelp.Name<MonsterDef>(d, Monster)} affinity {Compare.Symbol()} {Level}";
}

[TypeId("AspectKnown"), Label("Player knows an aspect")]
public sealed class AspectKnownCondition : Condition
{
    [Ref(typeof(AspectDef)), Required] public string Aspect { get; set; } = "";
    public override bool Evaluate(RuleContext ctx) => ctx.Progress.Aspects.Contains(Aspect);
    public override string Describe(DefRegistry d) => $"Knows {DescribeHelp.Name<AspectDef>(d, Aspect)}";
}

[TypeId("AspectActive"), Label("Aspect is switched on (out of combat)")]
public sealed class AspectActiveCondition : Condition
{
    [Ref(typeof(AspectDef)), Required] public string Aspect { get; set; } = "";
    public override bool Evaluate(RuleContext ctx) => ctx.Progress.ActiveAspect == Aspect;
    public override string Describe(DefRegistry d) => $"{DescribeHelp.Name<AspectDef>(d, Aspect)} is active";
}

[TypeId("GrantSummon"), Label("Player can now summon a creature")]
public sealed class GrantSummonOutcome : Outcome
{
    [Ref(typeof(MonsterDef)), Required] public string Monster { get; set; } = "";
    public bool Announce { get; set; } = true;
    public override void Execute(RuleContext ctx)
    {
        if (!ctx.Defs.TryGet<MonsterDef>(Monster, out var m) || ctx.Progress.HasSummon(Monster)) return;
        ctx.Progress.GrantSummon(m);
        if (Announce) ctx.Host?.ShowMessage(new[] { $"You can now summon {m.DisplayName}." }, null);
    }
    public override string Describe(DefRegistry d) => $"Unlock summon {DescribeHelp.Name<MonsterDef>(d, Monster)}";
}

[TypeId("GrantAspect"), Label("Player learns an aspect")]
public sealed class GrantAspectOutcome : Outcome
{
    [Ref(typeof(AspectDef)), Required] public string Aspect { get; set; } = "";
    public bool Announce { get; set; } = true;
    public override void Execute(RuleContext ctx)
    {
        if (ctx.Progress.Aspects.Contains(Aspect)) return;
        ctx.Progress.Aspects.Add(Aspect);
        if (Announce) ctx.Host?.ShowMessage(new[] { $"You learned {DescribeHelp.Name<AspectDef>(ctx.Defs, Aspect)}." }, null);
    }
    public override string Describe(DefRegistry d) => $"Learn {DescribeHelp.Name<AspectDef>(d, Aspect)}";
}

[TypeId("AddAffinity"), Label("Add affinity XP to a summon")]
public sealed class AddAffinityOutcome : Outcome
{
    [Ref(typeof(MonsterDef)), Required] public string Monster { get; set; } = "";
    public int Xp { get; set; } = 10;
    public override void Execute(RuleContext ctx)
    {
        if (ctx.Defs.TryGet<MonsterDef>(Monster, out var m)) ctx.Progress.AddAffinity(m, Xp);
    }
    public override string Describe(DefRegistry d) => $"+{Xp} affinity with {DescribeHelp.Name<MonsterDef>(d, Monster)}";
}

public enum PlayerStat { HP, AP, MP }

[TypeId("StatBonus"), Label("Permanent stat bonus (artifact)"), Tooltip("Adds to HP/AP/MP without affecting XP growth.")]
public sealed class StatBonusOutcome : Outcome
{
    public PlayerStat Stat { get; set; }
    public int Amount { get; set; } = 1;
    public override void Execute(RuleContext ctx)
    {
        var track = Stat switch { PlayerStat.HP => ctx.Progress.Hp, PlayerStat.AP => ctx.Progress.Ap, _ => ctx.Progress.Mp };
        track.Bonus += Amount;
        ctx.Host?.ShowMessage(new[] { $"Your {Stat} increased by {Amount}." }, null);
    }
    public override string Describe(DefRegistry d) => $"+{Amount} {Stat} (permanent)";
}

[TypeId("Ask"), Label("Ask the player yes / no"), Tooltip("e.g. 'Jump?' - runs one list on Yes, another on No.")]
public sealed class AskOutcome : Outcome
{
    [Required] public string Question { get; set; } = "";
    public List<Outcome> IfYes { get; set; } = new();
    public List<Outcome> IfNo { get; set; } = new();
    public override void Execute(RuleContext ctx)
    {
        if (ctx.Host is null) return;
        ctx.Host.Confirm(Question, yes =>
        {
            foreach (var o in yes ? IfYes : IfNo) o.Execute(ctx);
        });
    }
    public override string Describe(DefRegistry d) => $"Ask \"{Question}\" (yes: {IfYes.Count}, no: {IfNo.Count})";
}

[TypeId("StartFight"), Label("Start a fight")]
public sealed class StartEncounterOutcome : Outcome
{
    [Ref(typeof(EncounterDef)), Required] public string Encounter { get; set; } = "";
    public override void Execute(RuleContext ctx) => ctx.Host?.StartEncounter(Encounter);
    public override string Describe(DefRegistry d) => $"Fight {DescribeHelp.Name<EncounterDef>(d, Encounter)}";
}
