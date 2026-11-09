namespace Chasm.Core.Events;

/// <summary>
/// Marker for anything published on the <see cref="EventBus"/>. Events are small immutable facts
/// ("TurtleDealtDamage 3"), never commands.
/// </summary>
public interface IGameEvent;

/// <summary>
/// Simple synchronous publish/subscribe. Gameplay announces facts; listeners (statistics, audio,
/// achievements, unlock rules, UI) react without gameplay knowing they exist.
/// </summary>
public sealed class EventBus
{
    private readonly Dictionary<Type, List<Delegate>> _handlers = new();
    private readonly List<Action<IGameEvent>> _any = new();

    public IDisposable Subscribe<T>(Action<T> handler) where T : IGameEvent
    {
        if (!_handlers.TryGetValue(typeof(T), out var list)) _handlers[typeof(T)] = list = new();
        list.Add(handler);
        return new Unsubscriber(() => list.Remove(handler));
    }

    /// <summary>Receives every event (used by the statistics tracker and debug log).</summary>
    public IDisposable SubscribeAll(Action<IGameEvent> handler)
    {
        _any.Add(handler);
        return new Unsubscriber(() => _any.Remove(handler));
    }

    public void Publish<T>(T evt) where T : IGameEvent
    {
        if (_handlers.TryGetValue(evt.GetType(), out var list))
            foreach (var h in list.ToArray()) ((Action<T>)h)(evt);
        foreach (var h in _any.ToArray()) h(evt);
    }

    private sealed class Unsubscriber(Action dispose) : IDisposable
    {
        private Action? _dispose = dispose;
        public void Dispose() { _dispose?.Invoke(); _dispose = null; }
    }
}
