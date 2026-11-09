using Chasm.Core.Combat;
using Chasm.Core.Content;
using Chasm.Core.Session;

namespace Chasm.Core.Progression;

/// <summary>One growing stat: level from XP, plus flat bonuses from items/artifacts that don't affect XP.</summary>
public sealed class StatTrack
{
    public int Xp { get; set; }
    public int Bonus { get; set; }
    public int Level(GameConfigDef cfg) => LevelFor(Xp, cfg);

    public static int LevelFor(int xp, GameConfigDef cfg)
    {
        int level = 0;
        double need = cfg.XpForFirstLevel;
        double total = need;
        while (xp >= total && level < 999) { level++; need *= cfg.XpGrowth; total += need; }
        return level;
    }

    /// <summary>XP progress toward the next level as (into, needed).</summary>
    public (int Into, int Needed) Progress(GameConfigDef cfg)
    {
        double need = cfg.XpForFirstLevel, prevTotal = 0, total = need;
        while (Xp >= total) { prevTotal = total; need *= cfg.XpGrowth; total += need; }
        return ((int)(Xp - prevTotal), (int)need);
    }
}

/// <summary>Per-summon state: unlocked, affinity XP, uses left until the next rest.</summary>
public sealed class SummonProgress
{
    public int AffinityXp { get; set; }
    public int UsesLeft { get; set; }
}

/// <summary>
/// Everything the player has grown: stat tracks, summons and their affinity, learned aspects, the
/// active out-of-combat aspect. Saved with the game.
/// </summary>
public sealed class PlayerProgress
{
    public StatTrack Hp { get; set; } = new();
    public StatTrack Ap { get; set; } = new();
    public StatTrack Mp { get; set; } = new();
    public int CurrentHp { get; set; } = -1; // -1 = full
    public Dictionary<string, SummonProgress> Summons { get; set; } = new();
    public List<string> Aspects { get; set; } = new();
    public string? ActiveAspect { get; set; }

    /// <summary>Where you wake after losing a fight (the last Figure you met).</summary>
    public string RespawnTile { get; set; } = "";
    public Grid.CellCoord RespawnCell { get; set; }
    /// <summary>Figures you've met, as "tileId/placementId", for fast travel.</summary>
    public List<string> KnownFigures { get; set; } = new();

    public int MaxHp(GameConfigDef c) => c.PlayerBaseHp + Hp.Level(c) * c.HpPerLevel + Hp.Bonus;
    public int MaxAp(GameConfigDef c) => c.PlayerBaseAp + Ap.Level(c) + Ap.Bonus;
    public int MaxMp(GameConfigDef c) => c.PlayerBaseMp + Mp.Level(c) + Mp.Bonus;
    public int Hp_(GameConfigDef c) => CurrentHp < 0 ? MaxHp(c) : Math.Min(CurrentHp, MaxHp(c));

    public bool HasSummon(string monsterId) => Summons.ContainsKey(monsterId);

    public static int AffinityLevel(MonsterDef m, int xp) => m.Summon.AffinityLevels.Count(l => xp >= l.XpRequired);

    public IEnumerable<AffinityLevelDef> ReachedLevels(MonsterDef m) =>
        Summons.TryGetValue(m.Id, out var sp) ? m.Summon.AffinityLevels.Where(l => sp.AffinityXp >= l.XpRequired) : Enumerable.Empty<AffinityLevelDef>();

    public int DailyUses(MonsterDef m) => m.Summon.BaseDailyUses + ReachedLevels(m).Sum(l => l.ExtraDailyUses);
    public int FieldCap(MonsterDef m) => m.Summon.BaseOnField + ReachedLevels(m).Sum(l => l.ExtraOnField);

    public void GrantSummon(MonsterDef m)
    {
        if (Summons.ContainsKey(m.Id)) return;
        Summons[m.Id] = new SummonProgress { UsesLeft = m.Summon.BaseDailyUses };
    }

    /// <summary>Resting refills every summon's uses.</summary>
    public void Rest(DefRegistry defs)
    {
        foreach (var (id, sp) in Summons)
            if (defs.TryGet<MonsterDef>(id, out var m)) sp.UsesLeft = DailyUses(m);
        CurrentHp = -1;
    }

    /// <summary>Adds affinity XP; returns the levels newly reached (for messages and aspect grants).</summary>
    public List<AffinityLevelDef> AddAffinity(MonsterDef m, int xp)
    {
        if (!Summons.TryGetValue(m.Id, out var sp)) return new();
        var before = m.Summon.AffinityLevels.Where(l => sp.AffinityXp >= l.XpRequired).ToHashSet();
        int oldMax = DailyUses(m);
        sp.AffinityXp += xp;
        var gained = m.Summon.AffinityLevels.Where(l => sp.AffinityXp >= l.XpRequired && !before.Contains(l)).ToList();
        sp.UsesLeft += Math.Max(0, DailyUses(m) - oldMax); // new uses are usable right away
        foreach (var l in gained)
            if (!string.IsNullOrEmpty(l.GrantAspect) && !Aspects.Contains(l.GrantAspect)) Aspects.Add(l.GrantAspect);
        return gained;
    }
}

/// <summary>The real game's summon rules: only unlocked summons, uses per rest, field cap from affinity.</summary>
public sealed class ProgressSummonPolicy(PlayerProgress progress, DefRegistry defs) : ISummonPolicy
{
    public IEnumerable<string> Summonable => progress.Summons.Keys.Where(id => defs.Exists(typeof(MonsterDef), id));

    public bool CanSummon(string id, int onField, out string reason)
    {
        reason = "";
        if (!progress.Summons.TryGetValue(id, out var sp) || !defs.TryGet<MonsterDef>(id, out var m)) { reason = "You haven't bonded with that creature."; return false; }
        if (sp.UsesLeft <= 0) { reason = "No uses left. Rest to recover."; return false; }
        int cap = progress.FieldCap(m);
        if (onField >= cap) { reason = cap == 1 ? "Already on the field." : $"Only {cap} at a time."; return false; }
        return true;
    }

    public void OnSummoned(string id)
    {
        if (progress.Summons.TryGetValue(id, out var sp)) sp.UsesLeft = Math.Max(0, sp.UsesLeft - 1);
    }

    public SummonBonus BonusFor(string id) =>
        defs.TryGet<MonsterDef>(id, out var m)
            ? new SummonBonus(
                progress.ReachedLevels(m).Sum(l => l.BonusHp), progress.ReachedLevels(m).Sum(l => l.BonusAp),
                progress.ReachedLevels(m).Sum(l => l.BonusMp),
                progress.ReachedLevels(m).Select(l => l.GrantAbility).Where(a => !string.IsNullOrEmpty(a)).ToList())
            : SummonBonus.None;

    public IEnumerable<string> PlayerAbilities =>
        progress.Aspects.Select(a => defs.TryGet<AspectDef>(a, out var d) ? d.CombatAbility : "").Where(a => !string.IsNullOrEmpty(a));
}
