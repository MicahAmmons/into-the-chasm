using Chasm.Core.Events;

namespace Chasm.Core.Stats;

/// <summary>
/// An event that contributes to lifetime statistics. Each event says which counters it bumps, so
/// adding a new tracked stat never requires touching the tracker.
/// Keys are dotted, lowercase: "damage.taken", "monster.defeated.frost_ooze", "summon.damage.turtle".
/// </summary>
public interface IStatEvent : IGameEvent
{
    IEnumerable<(string Key, double Amount)> StatChanges { get; }
}

/// <summary>
/// Lifetime counters for the player: deaths, damage taken, kills per monster, damage per summon,
/// distance walked, flees... Saved with the game; shown on the hidden debug overlay; readable by
/// unlock conditions ("StatAtLeast monster.defeated.ice_ooze 50").
/// </summary>
public sealed class StatsTracker
{
    private readonly SortedDictionary<string, double> _values = new(StringComparer.Ordinal);
    private IDisposable? _subscription;

    public IReadOnlyDictionary<string, double> Values => _values;

    public event Action<string, double>? Changed;

    public void Attach(EventBus bus)
    {
        _subscription?.Dispose();
        _subscription = bus.SubscribeAll(e =>
        {
            if (e is IStatEvent s) foreach (var (k, a) in s.StatChanges) Add(k, a);
        });
    }

    public void Detach() { _subscription?.Dispose(); _subscription = null; }

    public double Get(string key) => _values.TryGetValue(key, out var v) ? v : 0;

    public void Add(string key, double amount = 1)
    {
        _values[key] = Get(key) + amount;
        Changed?.Invoke(key, _values[key]);
    }

    public void Set(string key, double value)
    {
        _values[key] = value;
        Changed?.Invoke(key, value);
    }

    public void Load(IDictionary<string, double> saved)
    {
        _values.Clear();
        foreach (var kv in saved) _values[kv.Key] = kv.Value;
    }

    public Dictionary<string, double> Snapshot() => new(_values);
}

/// <summary>Standard stat keys, collected in one place so they're consistent.</summary>
public static class StatKeys
{
    public const string Deaths = "player.deaths";
    public const string DamageTaken = "player.damage_taken";
    public const string DistanceWalked = "player.distance_walked";
    public const string Flees = "combat.flees";
    public const string CombatsWon = "combat.won";
    public const string CombatsLost = "combat.lost";
    public const string Rests = "player.rests";
    public static string MonsterDefeated(string monsterId) => $"monster.defeated.{monsterId}";
    public static string SummonUsed(string summonId) => $"summon.used.{summonId}";
    public static string SummonDamage(string summonId) => $"summon.damage.{summonId}";
    public static string SummonDeaths(string summonId) => $"summon.deaths.{summonId}";
    public static string ItemUsed(string itemId) => $"item.used.{itemId}";
}
