using Chasm.Core.World;
using Chasm.Game.Hosting;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Chasm.Game.World;

/// <summary>
/// Draws animated cloud layers (always behind the tile art, never baked). Each layer's texture
/// repeats endlessly in every direction, scaled, tinted and faded, scrolling at its speed toward
/// its direction. The texture should be seamless on all four sides.
/// </summary>
public static class CloudRenderer
{
    /// <summary>"Screen": black adds nothing, white brightens - so clouds painted on black need no transparency.</summary>
    private static readonly BlendState Screen = new()
    {
        ColorSourceBlend = Blend.One, ColorDestinationBlend = Blend.InverseSourceColor,
        AlphaSourceBlend = Blend.One, AlphaDestinationBlend = Blend.InverseSourceAlpha,
    };

    private static readonly BlendState Add = new()
    {
        ColorSourceBlend = Blend.One, ColorDestinationBlend = Blend.One,
        AlphaSourceBlend = Blend.One, AlphaDestinationBlend = Blend.One,
    };

    /// <summary>Draws the layers in order (first = furthest back). Leaves the batch begun as the render queue expects.</summary>
    public static void DrawLayers(SpriteBatch b, GameServices s, IReadOnlyList<CloudLayer> layers)
    {
        if (layers.Count == 0) return;
        b.End();
        foreach (var layer in layers)
        {
            var blend = layer.Blend switch { CloudBlend.Screen => Screen, CloudBlend.Add => Add, _ => BlendState.AlphaBlend };
            b.Begin(SpriteSortMode.Deferred, blend, SamplerState.LinearClamp);
            DrawLayer(b, s, layer);
            b.End();
        }
        b.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.LinearClamp);
    }

    public static void DrawLayer(SpriteBatch b, GameServices s, CloudLayer layer)
    {
        var tex = s.Textures.Get(layer.Texture);
        float scale = Math.Clamp(layer.Scale, 0.05f, 16f);
        float w = tex.Width * scale, h = tex.Height * scale;
        if (w < 4 || h < 4) return;
        var off = layer.OffsetAt(s.TotalTime);
        // Wrap the offset into two copies (so mirrored pairs stay in step) and keep the numbers small.
        float ox = Mod(off.X, 2 * w), oy = Mod(off.Y, 2 * h);
        var color = TileRenderer.ParseColor(layer.Tint, Color.White) * Math.Clamp(layer.Opacity, 0f, 1f);
        // Mirrored (not-quite-seamless) art: skip the outermost pixels, which are often a different shade.
        Rectangle? src = null;
        if (layer.Mirror)
        {
            int trim = Math.Max(1, Math.Min(tex.Width, tex.Height) / 200);
            src = new Rectangle(trim, trim, tex.Width - trim * 2, tex.Height - trim * 2);
        }
        int row = 0;
        for (float y = oy - 2 * h; y < 1080; y += h, row++)
        {
            int col = 0;
            for (float x = ox - 2 * w; x < 1920; x += w, col++)
            {
                if (x + w < 0 || y + h < 0) continue;
                var fx = layer.Mirror
                    ? (col % 2 == 1 ? SpriteEffects.FlipHorizontally : 0) | (row % 2 == 1 ? SpriteEffects.FlipVertically : 0)
                    : SpriteEffects.None;
                // Whole pixels; each copy ends exactly where the next begins (no gap, no doubled line).
                int x0 = (int)MathF.Floor(x), y0 = (int)MathF.Floor(y);
                var dest = new Rectangle(x0, y0, (int)MathF.Floor(x + w) - x0, (int)MathF.Floor(y + h) - y0);
                b.Draw(tex, dest, src, color, 0, Vector2.Zero, fx, 0);
            }
        }
    }

    private static float Mod(float v, float m) { float r = v % m; return r < 0 ? r + m : r; }
}
