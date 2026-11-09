using Chasm.Core.World;
using Chasm.Game.Hosting;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Chasm.Game.World;

/// <summary>
/// Placeholder cloud bands until the cloud FX are reworked: the floor's cloud texture tiled
/// horizontally (every other copy mirrored to hide seams), drifting right and wrapping, tinted.
/// The lower band fills from just above the lower cloud line to the bottom; the upper band from the
/// top to just below the upper line. The edge facing the playfield fades in softly. Drawn beneath
/// painted regions so cliffs and walls fade into them.
/// </summary>
public static class CloudRenderer
{
    private const int EdgeStrips = 24;

    public static void Draw(SpriteBatch b, GameServices s, CloudLayerDef cloud, bool lower)
    {
        if (!cloud.Enabled || cloud.Opacity <= 0) return;
        var tex = s.Textures.Get(cloud.Texture);
        float lineY = cloud.Line * 1080, pad = Math.Max(40, cloud.FadeLength * 1080 * 0.5f);
        int top = lower ? (int)(lineY - pad) : 0;
        int bottom = lower ? 1080 : (int)(lineY + pad);
        if (bottom <= top) return;
        int h = bottom - top;
        float scale = h / (float)tex.Height;
        int w = Math.Max(1, (int)(tex.Width * scale));
        float offset = (float)(s.TotalTime * cloud.Speed % (2 * w));
        var color = TileRenderer.ParseColor(cloud.Tint, Color.White) * cloud.Opacity;
        int soft = (int)Math.Min(h * 0.5f, pad * 1.6f); // height of the soft edge

        int i = 0;
        for (float x = offset - 2 * w; x < 1920; x += w, i++)
        {
            var fx = (i % 2 == 1 ? SpriteEffects.FlipHorizontally : 0) | (lower ? 0 : SpriteEffects.FlipVertically);
            // Solid part.
            int solidTop = lower ? top + soft : top, solidBottom = lower ? bottom : bottom - soft;
            DrawSlice(b, tex, x, w, top, h, solidTop, solidBottom, color, fx);
            // Soft edge: thin strips with rising opacity.
            for (int k = 0; k < EdgeStrips; k++)
            {
                float a = (k + 0.5f) / EdgeStrips;
                int y0 = lower ? top + k * soft / EdgeStrips : bottom - (k + 1) * soft / EdgeStrips;
                int y1 = lower ? top + (k + 1) * soft / EdgeStrips : bottom - k * soft / EdgeStrips;
                DrawSlice(b, tex, x, w, top, h, y0, y1, color * a, fx);
            }
        }
    }

    /// <summary>Draws rows [y0, y1) of a band that spans [top, top+h), keeping the texture aligned.</summary>
    private static void DrawSlice(SpriteBatch b, Texture2D tex, float x, int w, int top, int h, int y0, int y1, Color c, SpriteEffects fx)
    {
        if (y1 <= y0) return;
        float v0 = (y0 - top) / (float)h, v1 = (y1 - top) / (float)h;
        if ((fx & SpriteEffects.FlipVertically) != 0) { (v0, v1) = (1 - v1, 1 - v0); }
        var src = new Rectangle(0, (int)(v0 * tex.Height), tex.Width, Math.Max(1, (int)((v1 - v0) * tex.Height)));
        b.Draw(tex, new Rectangle((int)x, y0, w, y1 - y0), src, c, 0, Vector2.Zero, fx, 0);
    }
}
