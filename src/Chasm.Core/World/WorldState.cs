using Chasm.Core.Events;
using Chasm.Core.Stats;

namespace Chasm.Core.World;

/// <summary>
/// Everything the world remembers, always in memory and tiny: named flags plus per-placement state.
/// "Floors unlock floors" is built entirely on this: any rule can read a flag, any rule can set one.
///
/// Flags are integers (0 = off). Bool flags use 0/1; counters ("berries_fed") just count.
/// Placement state uses keys like "f1_start/bush_03.taken" so it survives leaving and re-entering a Tile.
/// </summary>
public sealed class WorldState
{
    private readonly SortedDictionary<string, int> _flags = new(StringComparer.Ordinal);

    public IReadOnlyDictionary<string, int> Flags => _flags;

    public event Action<string, int>? FlagChanged;

    public int Get(string flag) => _flags.TryGetValue(flag, out var v) ? v : 0;
    public bool IsSet(string flag) => Get(flag) != 0;

    public void Set(string flag, int value)
    {
        if (value == 0) { if (!_flags.Remove(flag)) return; }
        else { if (_flags.TryGetValue(flag, out var old) && old == value) return; _flags[flag] = value; }
        FlagChanged?.Invoke(flag, value);
    }

    public void Set(string flag, bool on) => Set(flag, on ? 1 : 0);
    public void Add(string flag, int amount) => Set(flag, Get(flag) + amount);

    public static string PlacementKey(string tileId, string placementId, string what) => $"{tileId}/{placementId}.{what}";

    public void Load(IDictionary<string, int> flags)
    {
        _flags.Clear();
        foreach (var kv in flags) if (kv.Value != 0) _flags[kv.Key] = kv.Value;
    }

    public Dictionary<string, int> Snapshot() => new(_flags);
}

/// <summary>Published whenever a flag changes (debug log, quest UI later).</summary>
public sealed record FlagChangedEvent(string Flag, int Value) : IGameEvent;
