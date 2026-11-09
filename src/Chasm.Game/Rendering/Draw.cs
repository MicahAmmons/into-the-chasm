using Chasm.Game.Assets;
using FontStashSharp;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Chasm.Game.Rendering;

/// <summary>
/// Drawing helpers on top of SpriteBatch: shapes, diamonds, text, and placeholder-aware sprites.
/// Call inside a Begin/End.
/// </summary>
public sealed class Draw
{
    private readonly TextureStore _textures;
    private readonly FontStore _fonts;
    public Texture2D Pixel => _textures.Pixel;

    /// <summary>A white filled 2:1 diamond, tinted when drawn (cell highlights).</summary>
    public Texture2D Diamond { get; }

    public Draw(GraphicsDevice device, TextureStore textures, FontStore fonts)
    {
        _textures = textures;
        _fonts = fonts;
        Diamond = MakeDiamond(device, 128, 64);
    }

    public void Rect(SpriteBatch b, Rectangle r, Color c) => b.Draw(Pixel, r, c);

    public void RectOutline(SpriteBatch b, Rectangle r, Color c, int thickness = 1)
    {
        b.Draw(Pixel, new Rectangle(r.X, r.Y, r.Width, thickness), c);
        b.Draw(Pixel, new Rectangle(r.X, r.Bottom - thickness, r.Width, thickness), c);
        b.Draw(Pixel, new Rectangle(r.X, r.Y, thickness, r.Height), c);
        b.Draw(Pixel, new Rectangle(r.Right - thickness, r.Y, thickness, r.Height), c);
    }

    public void Line(SpriteBatch b, Vector2 from, Vector2 to, Color c, float thickness = 1f)
    {
        var d = to - from;
        float len = d.Length();
        if (len < 0.01f) return;
        b.Draw(Pixel, from, null, c, MathF.Atan2(d.Y, d.X), new Vector2(0, 0.5f), new Vector2(len, thickness), SpriteEffects.None, 0);
    }

    /// <summary>Filled diamond centred on <paramref name="center"/>.</summary>
    public void DiamondFill(SpriteBatch b, Vector2 center, float width, float height, Color c) =>
        b.Draw(Diamond, new Rectangle((int)(center.X - width / 2), (int)(center.Y - height / 2), (int)width, (int)height), c);

    public void DiamondOutline(SpriteBatch b, Vector2 center, float width, float height, Color c, float thickness = 1f)
    {
        var top = center + new Vector2(0, -height / 2);
        var right = center + new Vector2(width / 2, 0);
        var bottom = center + new Vector2(0, height / 2);
        var left = center + new Vector2(-width / 2, 0);
        Line(b, top, right, c, thickness); Line(b, right, bottom, c, thickness);
        Line(b, bottom, left, c, thickness); Line(b, left, top, c, thickness);
    }

    public void Text(SpriteBatch b, string text, Vector2 pos, Color c, float size = 22, bool bold = false)
    {
        var font = bold ? _fonts.Bold(size) : _fonts.Regular(size);
        font.DrawText(b, text, pos, c);
    }

    public void TextShadowed(SpriteBatch b, string text, Vector2 pos, Color c, float size = 22, bool bold = false)
    {
        Text(b, text, pos + new Vector2(2, 2), Color.Black * 0.8f, size, bold);
        Text(b, text, pos, c, size, bold);
    }

    public Vector2 Measure(string text, float size = 22, bool bold = false) =>
        (bold ? _fonts.Bold(size) : _fonts.Regular(size)).MeasureString(text);

    public void TextCentered(SpriteBatch b, string text, Vector2 center, Color c, float size = 22, bool bold = false)
    {
        var m = Measure(text, size, bold);
        TextShadowed(b, text, center - m / 2, c, size, bold);
    }

    /// <summary>
    /// Draws a texture into <paramref name="dest"/>. If it's the missing-art placeholder, draws a labelled
    /// box instead so you can see what belongs there.
    /// </summary>
    public void Sprite(SpriteBatch b, Texture2D tex, Rectangle dest, Rectangle? src, Color tint, string label,
        SpriteEffects fx = SpriteEffects.None)
    {
        if (_textures.IsPlaceholder(tex))
        {
            Placeholder(b, dest, label);
            return;
        }
        b.Draw(tex, dest, src, tint, 0, Vector2.Zero, fx, 0);
    }

    /// <summary>A labelled stand-in box: the "rectangle with a word" for art that doesn't exist yet.</summary>
    public void Placeholder(SpriteBatch b, Rectangle dest, string label)
    {
        Rect(b, dest, new Color(90, 40, 110) * 0.85f);
        RectOutline(b, dest, new Color(230, 160, 255), 2);
        float size = Math.Clamp(dest.Height / 4f, 10, 22);
        var m = Measure(label, size);
        if (m.X > dest.Width - 4) size = Math.Max(8, size * (dest.Width - 4) / m.X);
        TextCentered(b, label, dest.Center.ToVector2(), Color.White, size);
    }

    private static Texture2D MakeDiamond(GraphicsDevice device, int w, int h)
    {
        var data = new Color[w * h];
        float cx = (w - 1) / 2f, cy = (h - 1) / 2f;
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                // Distance outside the diamond edge, in pixels, for a 1px anti-aliased border.
                float d = (MathF.Abs(x - cx) / (w / 2f) + MathF.Abs(y - cy) / (h / 2f) - 1f) * (h / 2f);
                float a = Math.Clamp(0.5f - d, 0f, 1f);
                data[y * w + x] = Color.White * a;
            }
        var tex = new Texture2D(device, w, h);
        tex.SetData(data);
        return tex;
    }
}
