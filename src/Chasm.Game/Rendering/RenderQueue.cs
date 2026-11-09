using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Chasm.Game.Rendering;

/// <summary>Draw layers, back to front. Within <see cref="Entities"/>, items are sorted by their foot Y.</summary>
public enum RenderLayer
{
    Background = 0,
    Ground = 100,       // cell highlights, ground decals
    Entities = 200,     // player, monsters, NPCs, interactables: Y-sorted for isometric depth
    Foreground = 300,   // tile art drawn in front of characters (overhangs, foliage)
    Fog = 400,          // floor fog / cloud layers
    WorldUi = 500,      // floating names, damage numbers
}

/// <summary>
/// Collects draw calls for a frame, then draws them sorted by layer and (for entities) by foot
/// position, so a character standing lower on screen is drawn in front. Each entry may also carry
/// a shader (Effect); consecutive entries with the same effect share one batch.
/// </summary>
public sealed class RenderQueue
{
    private readonly List<Entry> _entries = new();
    private int _sequence;

    private struct Entry
    {
        public int Layer;
        public float SortY;
        public int Sequence;
        public Effect? Effect;
        public Action<SpriteBatch> DrawAction;
    }

    public void Add(RenderLayer layer, float sortY, Action<SpriteBatch> draw, Effect? effect = null) =>
        _entries.Add(new Entry { Layer = (int)layer, SortY = sortY, Sequence = _sequence++, Effect = effect, DrawAction = draw });

    /// <summary>Convenience for a plain sprite.</summary>
    public void Sprite(RenderLayer layer, float sortY, Texture2D tex, Rectangle dest, Rectangle? src, Color tint,
        SpriteEffects fx = SpriteEffects.None, Effect? effect = null) =>
        Add(layer, sortY, b => b.Draw(tex, dest, src, tint, 0, Vector2.Zero, fx, 0), effect);

    public void Flush(SpriteBatch batch, Matrix? transform = null)
    {
        _entries.Sort(static (a, b) =>
        {
            int c = a.Layer.CompareTo(b.Layer);
            if (c != 0) return c;
            if (a.Layer == (int)RenderLayer.Entities) { c = a.SortY.CompareTo(b.SortY); if (c != 0) return c; }
            return a.Sequence.CompareTo(b.Sequence);
        });

        Effect? current = null;
        bool open = false;
        foreach (var e in _entries)
        {
            if (!open || e.Effect != current)
            {
                if (open) batch.End();
                current = e.Effect;
                batch.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.LinearClamp, null, null, current, transform);
                open = true;
            }
            e.DrawAction(batch);
        }
        if (open) batch.End();
        _entries.Clear();
        _sequence = 0;
    }
}
