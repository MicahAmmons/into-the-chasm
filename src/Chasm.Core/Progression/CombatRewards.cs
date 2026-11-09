using Chasm.Core.Combat;
using Chasm.Core.Session;

namespace Chasm.Core.Progression;

/// <summary>
/// After a fight: turns what happened into growth.
///  - Victory: HP XP from damage taken, AP XP from AP spent, MP XP from cells walked, affinity for every summon used.
///  - Defeat: same, except no HP XP.
///  - Fled: nothing (summon uses already spent stay spent).
/// Returns messages to show the player.
/// </summary>
public static class CombatRewards
{
    public static List<string> Apply(GameSession session, CombatEngine engine, CombatOutcome outcome)
    {
        var msgs = new List<string>();
        var cfg = session.Config;
        var prog = session.Progress;
        var player = engine.Player;
        prog.CurrentHp = outcome == CombatOutcome.Defeat ? -1 : player.Hp;
        if (outcome == CombatOutcome.Fled) return msgs;

        int damageTaken = 0, apSpent = 0, cellsWalked = 0;
        var summonsUsed = new Dictionary<string, int>();
        foreach (var e in engine.Log)
        {
            switch (e)
            {
                case DamagedEvent d when d.Unit == player.Id: damageTaken += d.Amount; break;
                case MovedEvent m when m.Unit == player.Id: cellsWalked += m.Path.Count; break;
                case AbilityUsedEvent a when a.Unit == player.Id && engine.Defs.TryGet<AbilityDef>(a.Ability, out var ab): apSpent += ab.ApCost; break;
                case SummonedEvent s:
                    summonsUsed[s.Monster] = summonsUsed.GetValueOrDefault(s.Monster) + 1;
                    if (engine.Defs.TryGet<MonsterDef>(s.Monster, out var sm)) apSpent += sm.SummonApCost;
                    break;
            }
        }

        void Grow(string name, StatTrack track, float xp)
        {
            int amount = (int)MathF.Round(xp);
            if (amount <= 0) return;
            int before = track.Level(cfg);
            track.Xp += amount;
            int after = track.Level(cfg);
            if (after > before) msgs.Add($"Your {name} grows stronger! ({name} +{(name == "HP" ? (after - before) * cfg.HpPerLevel : after - before)})");
        }
        if (outcome == CombatOutcome.Victory) Grow("HP", prog.Hp, damageTaken * cfg.HpXpPerDamage);
        Grow("AP", prog.Ap, apSpent * cfg.ApXpPerPoint);
        Grow("MP", prog.Mp, cellsWalked * cfg.MpXpPerCell);

        foreach (var (id, count) in summonsUsed)
        {
            if (!engine.Defs.TryGet<MonsterDef>(id, out var m)) continue;
            foreach (var level in prog.AddAffinity(m, m.Summon.XpPerSummon * count))
            {
                msgs.Add(string.IsNullOrWhiteSpace(level.Message) ? $"Your bond with {m.DisplayName} deepens." : level.Message);
                if (!string.IsNullOrEmpty(level.GrantAspect) && engine.Defs.TryGet<AspectDef>(level.GrantAspect, out var asp))
                    msgs.Add($"You learned {asp.DisplayName}!");
            }
        }
        return msgs;
    }
}
