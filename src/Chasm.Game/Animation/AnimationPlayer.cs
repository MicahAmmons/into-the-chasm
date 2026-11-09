using Chasm.Core.Animation;
using Chasm.Game.Assets;
using Chasm.Game.Rendering;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Chasm.Game.Animation;

/// <summary>
/// Plays an <see cref="AnimationDef"/> for one character: tracks the current state, advances each
/// layer's clock, and queues the frames for drawing at the character's feet.
///
/// With no AnimationDef (or missing art) it draws a labelled placeholder box, so a brand-new monster
/// is visible and testable before it has art.
/// </summary>
public sealed class AnimationPlayer
{
    private AnimationStateDef? _state;
    private LayerClock[] _clocks = Array.Empty<LayerClock>();

    public AnimationDef? Def { get; private set; }
    public AnimState Requested { get; private set; } = AnimState.Idle;
    public AnimState? Playing => _state?.State;
    public float Speed { get; set; } = 1f;

    /// <summary>Size of the placeholder box when there is no art.</summary>
    public Point PlaceholderSize { get; set; } = new(70, 90);

    public AnimationPlayer(AnimationDef? def) { SetDef(def); }

    public void SetDef(AnimationDef? def)
    {
        Def = def;
        _state = null;
        Play(Requested, restart: true);
    }

    /// <summary>Request a state. Re-requesting the current state does nothing unless <paramref name="restart"/>.</summary>
    public void Play(AnimState state, bool restart = false)
    {
        Requested = state;
        if (Def is null) return;
        var resolved = AnimationRules.Resolve(Def, state);
        var next = resolved is null ? null : Def.Find(resolved.Value);
        if (next == _state && !restart) return;
        _state = next;
        _clocks = _state?.Layers.Select(l => new LayerClock(l, Speed)).ToArray() ?? Array.Empty<LayerClock>();
    }

    /// <summary>True when every non-looping layer has played through (attack finished, etc.).</summary>
    public bool Finished => _clocks.Length == 0 || _clocks.All(c => c.Finished);

    /// <summary>Current position in the first layer's frame sequence (attack timing, "hit on frame 34").</summary>
    public int Position => _clocks.Length > 0 ? _clocks[0].Position : 0;

    public void Update(float dt)
    {
        foreach (var c in _clocks) c.Update(dt);
    }

    public void Draw(RenderQueue queue, TextureStore textures, Draw draw, Vector2 feet, Facing facing,
        Color tint, string label, RenderLayer layer = RenderLayer.Entities, float? sortY = null)
    {
        float sort = sortY ?? feet.Y;
        if (_state is null || _state.Layers.Count == 0)
        {
            var size = PlaceholderSize;
            var box = new Rectangle((int)feet.X - size.X / 2, (int)feet.Y - size.Y + 8, size.X, size.Y);
            queue.Add(layer, sort, b => draw.Placeholder(b, box, label));
            return;
        }

        for (int i = 0; i < _state.Layers.Count; i++)
        {
            var def = _state.Layers[i];
            var clock = _clocks[i];
            int frame = clock.Frame;
            if (frame < 0) continue;
            var tex = textures.Get(def.Texture);
            int w = (int)(def.FrameWidth * def.Scale), h = (int)(def.FrameHeight * def.Scale);
            var dest = new Rectangle((int)(feet.X - w / 2f), (int)(feet.Y - h / 2f - def.YOffset), w, h);
            var fx = facing == def.ArtFaces ? SpriteEffects.None : SpriteEffects.FlipHorizontally;
            float layerSort = sort + i * 0.001f;

            if (textures.IsPlaceholder(tex))
            {
                queue.Add(layer, layerSort, b => draw.Placeholder(b, dest, $"{label}\n{_state.State}"));
                continue;
            }
            var src = SourceRect(tex, def, frame);
            if (def.Crossfade)
            {
                float t = clock.FrameProgress;
                var next = SourceRect(tex, def, Math.Max(0, clock.NextFrame));
                queue.Sprite(layer, layerSort, tex, dest, src, tint * (1 - t), fx);
                queue.Sprite(layer, layerSort, tex, dest, next, tint * t, fx);
            }
            else
            {
                queue.Sprite(layer, layerSort, tex, dest, src, tint, fx);
            }
        }
    }

    /// <summary>Frames read across a row and continue onto the next row, like the original.</summary>
    public static Rectangle SourceRect(Texture2D tex, AnimationLayerDef def, int frame)
    {
        int perRow = Math.Max(1, tex.Width / Math.Max(1, def.FrameWidth));
        int col = frame % perRow;
        int row = def.Row - 1 + frame / perRow;
        return new Rectangle(col * def.FrameWidth, row * def.FrameHeight, def.FrameWidth, def.FrameHeight);
    }
}
