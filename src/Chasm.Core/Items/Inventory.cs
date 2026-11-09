using Chasm.Core.Events;

namespace Chasm.Core.Items;

/// <summary>What the player carries: item counts plus gold.</summary>
public sealed class Inventory
{
    private readonly SortedDictionary<string, int> _items = new(StringComparer.Ordinal);

    public IReadOnlyDictionary<string, int> Items => _items;
    public int Gold { get; private set; }

    public event Action? Changed;

    public int Count(string itemId) => _items.TryGetValue(itemId, out var n) ? n : 0;
    public bool Has(string itemId, int count = 1) => Count(itemId) >= count;

    public void Add(string itemId, int count = 1)
    {
        if (count <= 0) return;
        _items[itemId] = Count(itemId) + count;
        Changed?.Invoke();
    }

    public bool Remove(string itemId, int count = 1)
    {
        if (!Has(itemId, count)) return false;
        int left = Count(itemId) - count;
        if (left == 0) _items.Remove(itemId); else _items[itemId] = left;
        Changed?.Invoke();
        return true;
    }

    public void AddGold(int amount) { Gold = Math.Max(0, Gold + amount); Changed?.Invoke(); }

    public bool SpendGold(int amount)
    {
        if (Gold < amount) return false;
        Gold -= amount;
        Changed?.Invoke();
        return true;
    }

    public void Load(IDictionary<string, int> items, int gold)
    {
        _items.Clear();
        foreach (var kv in items) if (kv.Value > 0) _items[kv.Key] = kv.Value;
        Gold = gold;
        Changed?.Invoke();
    }

    public Dictionary<string, int> Snapshot() => new(_items);
}

public sealed record ItemGainedEvent(string ItemId, int Count) : Stats.IStatEvent
{
    public IEnumerable<(string Key, double Amount)> StatChanges => new[] { ($"item.gained.{ItemId}", (double)Count) };
}

public sealed record ItemUsedEvent(string ItemId) : Stats.IStatEvent
{
    public IEnumerable<(string Key, double Amount)> StatChanges => new[] { (Stats.StatKeys.ItemUsed(ItemId), 1.0) };
}
