using Chasm.Game.Hosting;
using Microsoft.Xna.Framework.Graphics;

namespace Chasm.Game.Scenes;

/// <summary>
/// A stack of scenes. Push = put on top (Pause over World). Pop = return to the one below.
/// Replace = swap the top (Title -> World).
///
/// Changes requested during Update/Draw are queued and applied at the start of the next frame,
/// so a scene can safely pop itself from inside its own Update.
/// </summary>
public sealed class SceneStack
{
    private readonly List<Scene> _scenes = new();
    private readonly Queue<Action> _pending = new();
    private readonly GameServices _services;

    public SceneStack(GameServices services) => _services = services;

    public Scene? Top => _scenes.Count > 0 ? _scenes[^1] : null;
    public int Count => _scenes.Count;
    public IReadOnlyList<Scene> Scenes => _scenes;

    public void Push(Scene scene) => _pending.Enqueue(() => DoPush(scene));
    public void Pop() => _pending.Enqueue(DoPop);
    public void Replace(Scene scene) => _pending.Enqueue(() => { DoPop(); DoPush(scene); });

    /// <summary>Clears the whole stack and starts fresh with one scene (e.g. back to title).</summary>
    public void ResetTo(Scene scene) => _pending.Enqueue(() =>
    {
        while (_scenes.Count > 0) DoPop();
        DoPush(scene);
    });

    /// <summary>
    /// The app is closing: give every scene its OnExit (top first) so they can save / write
    /// recovery files. Errors are logged, never thrown, so closing always works.
    /// </summary>
    public void ExitAll()
    {
        for (int i = _scenes.Count - 1; i >= 0; i--)
        {
            try { _scenes[i].OnExit(); }
            catch (Exception e) { Console.WriteLine($"[scenes] {_scenes[i].GetType().Name}.OnExit failed: {e.Message}"); }
        }
        _scenes.Clear();
    }

    /// <summary>Applies queued changes. Called by the host once per frame before Update.</summary>
    public void ApplyPending()
    {
        while (_pending.Count > 0) _pending.Dequeue()();
    }

    public void Update(float dt)
    {
        // Walk down from the top while scenes allow the one beneath to update.
        int start = _scenes.Count - 1;
        while (start > 0 && _scenes[start].UpdatesSceneBelow) start--;
        for (int i = _scenes.Count - 1; i >= start && i >= 0; i--) _scenes[i].Update(dt);
    }

    public void Draw(SpriteBatch batch)
    {
        // Find the lowest scene that must be drawn, then draw bottom-up so overlays land on top.
        int start = _scenes.Count - 1;
        while (start > 0 && _scenes[start].DrawsSceneBelow) start--;
        for (int i = Math.Max(start, 0); i < _scenes.Count; i++) _scenes[i].Draw(batch);
    }

    private void DoPush(Scene scene)
    {
        if (Top is { } covered) { covered.IsTop = false; covered.OnCovered(); }
        scene.Services = _services;
        _scenes.Add(scene);
        scene.IsTop = true;
        scene.OnEnter();
    }

    private void DoPop()
    {
        if (_scenes.Count == 0) return;
        var top = _scenes[^1];
        _scenes.RemoveAt(_scenes.Count - 1);
        top.IsTop = false;
        top.OnExit();
        if (Top is { } uncovered) { uncovered.IsTop = true; uncovered.OnUncovered(); }
    }
}
