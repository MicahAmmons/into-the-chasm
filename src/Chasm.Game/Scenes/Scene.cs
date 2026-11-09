using Chasm.Game.Hosting;
using Microsoft.Xna.Framework.Graphics;

namespace Chasm.Game.Scenes;

/// <summary>
/// One self-contained screen or overlay: Title, World, Combat, Pause, a sandbox, an editor.
/// Scenes live on a <see cref="SceneStack"/>. Only the top scene receives input.
/// </summary>
public abstract class Scene
{
    /// <summary>Set by the stack before <see cref="OnEnter"/>.</summary>
    public GameServices Services { get; internal set; } = null!;

    /// <summary>True when the scene directly below should still be drawn (this scene is an overlay).</summary>
    public virtual bool DrawsSceneBelow => false;

    /// <summary>True when the scene directly below should keep updating (rare: e.g. a non-pausing toast).</summary>
    public virtual bool UpdatesSceneBelow => false;

    /// <summary>True while this scene is the top of the stack (the one receiving input).</summary>
    public bool IsTop { get; internal set; }

    /// <summary>Called once when pushed (or when it replaces another scene).</summary>
    public virtual void OnEnter() { }

    /// <summary>Called once when popped or replaced. Release per-scene resources here.</summary>
    public virtual void OnExit() { }

    /// <summary>Called when another scene is pushed on top / when it becomes the top again.</summary>
    public virtual void OnCovered() { }
    public virtual void OnUncovered() { }

    public abstract void Update(float dt);

    /// <summary>Draw into the 1920x1080 canvas. The sprite batch is NOT begun; scenes choose their own batches.</summary>
    public abstract void Draw(SpriteBatch batch);

    /// <summary>
    /// Called for the top scene only, after the canvas has been presented, in window space.
    /// Tools use this for ImGui panels. Most scenes leave it empty.
    /// </summary>
    public virtual void DrawOverlayUi() { }
}
