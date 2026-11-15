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
            if (layer.Pattern == CloudPattern.Ripple && layer.PatternAmount > 0.5f) { DrawRipple(b, s, layer, blend); continue; }
            b.Begin(SpriteSortMode.Deferred, blend, SamplerState.LinearClamp);
            DrawLayer(b, s, layer, Vector2.Zero);
            b.End();
        }
        b.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.LinearClamp);
    }

    /// <summary>Draws one layer's repeating texture, moved by its scroll + pattern, shifted by <paramref name="shift"/>.</summary>
    public static void DrawLayer(SpriteBatch b, GameServices s, CloudLayer layer, Vector2 shift, int screenW = 1920)
    {
        var tex = s.Textures.Get(layer.Texture);
        var motion = layer.MotionAt(s.TotalTime);
        float scale = Math.Clamp(layer.Scale * motion.ScaleMul, 0.05f, 16f);
        float w = tex.Width * scale, h = tex.Height * scale;
        if (w < 4 || h < 4) return;
        // Breathe scales around the middle of the screen, so the clouds swell in place.
        float k = motion.ScaleMul;
        var off = new Vector2(motion.Offset.X * k + 960 * (1 - k), motion.Offset.Y * k + 540 * (1 - k)) + shift;
        // Wrap the offset into two copies (so mirrored pairs stay in step) and keep the numbers small.
        float ox = Mod(off.X, 2 * w), oy = Mod(off.Y, 2 * h);
        var color = TileRenderer.ParseColor(layer.Tint, Color.White) * Math.Clamp(layer.Opacity * motion.OpacityMul, 0f, 1f);
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
            for (float x = ox - 2 * w; x < screenW; x += w, col++)
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

    // ---------------------------------------------------------------- ripple

    private static RenderTarget2D? _rippleTarget;
    private const int RippleBand = 4; // strip height in pixels

    /// <summary>
    /// Ripple: draw the layer into a slightly wider off-screen image, then copy it back in thin
    /// horizontal strips, each shifted sideways by a travelling sine wave.
    /// </summary>
    private static void DrawRipple(SpriteBatch b, GameServices s, CloudLayer layer, BlendState blend)
    {
        int pad = (int)MathF.Ceiling(Math.Abs(layer.PatternAmount)) + 2;
        int width = 1920 + pad * 2;
        if (_rippleTarget is null || _rippleTarget.Width != width || _rippleTarget.IsDisposed)
        {
            _rippleTarget?.Dispose();
            _rippleTarget = new RenderTarget2D(s.Graphics, width, 1080, false, SurfaceFormat.Color, DepthFormat.None);
        }
        var previous = s.Graphics.GetRenderTargets();
        s.Graphics.SetRenderTarget(_rippleTarget);
        s.Graphics.Clear(Color.Transparent);
        b.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.LinearClamp);
        DrawLayer(b, s, layer, new Vector2(pad, 0), width);
        b.End();
        s.Graphics.SetRenderTargets(previous);

        b.Begin(SpriteSortMode.Deferred, blend, SamplerState.LinearClamp);
        for (int y = 0; y < 1080; y += RippleBand)
        {
            float dx = layer.RippleShiftAt(s.TotalTime, y + RippleBand * 0.5f);
            int sx = Math.Clamp((int)MathF.Round(pad - dx), 0, pad * 2);
            b.Draw(_rippleTarget, new Rectangle(0, y, 1920, RippleBand), new Rectangle(sx, y, 1920, RippleBand), Color.White);
        }
        b.End();
    }

    private static float Mod(float v, float m) { float r = v % m; return r < 0 ? r + m : r; }
}
