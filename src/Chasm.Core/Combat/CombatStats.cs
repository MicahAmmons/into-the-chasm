using Chasm.Core.Stats;

namespace Chasm.Core.Combat;

/// <summary>Turns combat events into lifetime statistics (damage taken, kills per monster, summon damage...).</summary>
public static class CombatStats
{
    public static void Record(CombatEngine e, CombatEvent ev, StatsTracker stats)
    {
        switch (ev)
        {
            case DamagedEvent d:
                var target = e.Unit(d.Unit);
                if (target.Kind == CombatantKind.Player) stats.Add(StatKeys.DamageTaken, d.Amount);
                if (d.Source is int src && e.Unit(src) is { Kind: CombatantKind.Summon } s)
                    stats.Add(StatKeys.SummonDamage(s.DefId), d.Amount);
                break;
            case DiedEvent died:
                var u = e.Unit(died.Unit);
                if (u.Kind == CombatantKind.Monster) stats.Add(StatKeys.MonsterDefeated(u.DefId));
                if (u.Kind == CombatantKind.Summon) stats.Add(StatKeys.SummonDeaths(u.DefId));
                if (u.Kind == CombatantKind.Player) stats.Add(StatKeys.Deaths);
                break;
            case SummonedEvent sum:
                stats.Add(StatKeys.SummonUsed(sum.Monster));
                break;
            case CombatEndedEvent end:
                stats.Add(end.Outcome switch
                {
                    CombatOutcome.Victory => StatKeys.CombatsWon,
                    CombatOutcome.Defeat => StatKeys.CombatsLost,
                    _ => StatKeys.Flees,
                });
                break;
        }
    }
}
