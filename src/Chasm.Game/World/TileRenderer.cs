using Chasm.Core.Grid;
using Chasm.Core.World;
using Chasm.Game.Hosting;
using Chasm.Game.Rendering;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Chasm.Game.World;

/// <summary>Draws a Tile's art layers and cell overlays. Shared by the world, combat and the map editor.</summary>
public static class TileRenderer
{
    public static Color ParseColor(string? hex, Color fallback)
    {
        if (string.IsNullOrWhiteSpace(hex)) return fallback;
        hex = hex.TrimStart('#');
        try
        {
            if (hex.Length == 6)
                return new Color(Convert.ToByte(hex[..2], 16), Convert.ToByte(hex[2..4], 16), Convert.ToByte(hex[4..6], 16));
        }
        catch (FormatException) { }
        return fallback;
    }

    /// <summary>
    /// Queues everything static about a tile, back to front:
    /// background colour, cloud bands, hand-painted full-screen layers, painted regions (behind),
    /// props, painted regions (in front), foreground / fog layers.
    /// </summary>
    public static void QueueLayers(RenderQueue q, GameServices s, TileDef tile, FloorDef? floor = null, bool includeProps = true, float opacity = 1f)
    {
        floor ??= s.TileVisuals.FloorOf(tile);
        var bg = ParseColor(floor?.BackgroundColor ?? tile.BackgroundColor, new Color(26, 29, 46));
        if (opacity >= 1f) q.Add(RenderLayer.Background, 0, b => s.Draw.Rect(b, new Rectangle(0, 0, 1920, 1080), bg));
        if (floor is not null && opacity >= 1f)
        {
            q.Add(RenderLayer.Background, 0, b => CloudRenderer.Draw(b, s, floor.UpperClouds, lower: false));
            q.Add(RenderLayer.Background, 0, b => CloudRenderer.Draw(b, s, floor.LowerClouds, lower: true));
        }
        QueueImageLayers(q, s, tile, TileLayerKind.Background, opacity);
        var bake = s.TileVisuals.Get(tile, floor);
        if (bake is { HasBehind: true }) q.Add(RenderLayer.Background, 0, b => b.Draw(bake.Behind, Vector2.Zero, Color.White * opacity));
        if (includeProps) QueueProps(q, s, tile, opacity);
        if (bake is { HasFront: true }) q.Add(RenderLayer.Foreground, 0, b => b.Draw(bake.Front, Vector2.Zero, Color.White * opacity));
        QueueImageLayers(q, s, tile, TileLayerKind.Foreground, opacity);
        QueueImageLayers(q, s, tile, TileLayerKind.Fog, opacity);
    }

    public static void QueueProps(RenderQueue q, GameServices s, TileDef tile, float opacity = 1f, PropDef? highlight = null)
    {
        foreach (var p in tile.Props)
        {
            var tex = s.Textures.Get(p.Texture);
            var tint = ParseColor(p.Tint, Color.White) * (p.Opacity * opacity);
            var fx = (p.FlipX ? SpriteEffects.FlipHorizontally : 0) | (p.FlipY ? SpriteEffects.FlipVertically : 0);
            var pos = new Vector2(p.X, p.Y);
            bool placeholder = s.Textures.IsPlaceholder(tex);
            bool hl = p == highlight;
            void DrawProp(SpriteBatch b)
            {
                if (placeholder)
                {
                    int w = (int)(80 * p.Scale), h = (int)(80 * p.Scale);
                    s.Draw.Placeholder(b, new Rectangle((int)p.X - w / 2, (int)p.Y - h, w, h), string.IsNullOrEmpty(p.Texture) ? "Prop" : Path.GetFileNameWithoutExtension(p.Texture));
                    return;
                }
                b.Draw(tex, pos, null, hl ? Color.Lerp(tint, Color.Yellow, 0.35f) : tint, MathHelper.ToRadians(p.Rotation),
                    new Vector2(tex.Width / 2f, tex.Height), p.Scale, fx, 0);
            }
            switch (p.Layer)
            {
                case PropLayer.Ground: q.Add(RenderLayer.Background, 0, DrawProp); break;
                case PropLayer.InFront: q.Add(RenderLayer.Foreground, 0, DrawProp); break;
                default: q.Add(RenderLayer.Entities, p.Y, DrawProp); break;
            }
        }
    }

    private static void QueueImageLayers(RenderQueue q, GameServices s, TileDef tile, TileLayerKind kind, float opacity)
    {
        foreach (var layer in tile.Layers.Where(l => l.Kind == kind))
        {
            var tex = s.Textures.Get(layer.Texture);
            if (s.Textures.IsPlaceholder(tex)) continue; // missing tile art: just show the colour + grid
            var target = layer.Kind switch
            {
                TileLayerKind.Foreground => RenderLayer.Foreground,
                TileLayerKind.Fog => RenderLayer.Fog,
                _ => RenderLayer.Background,
            };
            float a = layer.Opacity * opacity;
            q.Add(target, 0, b => b.Draw(tex, new Rectangle(0, 0, 1920, 1080), Color.White * a));
        }
    }

    /// <summary>Outlines every cell; walkable ones tinted. Used by the F4 debug view and the editor.</summary>
    public static void QueueWalkGrid(RenderQueue q, GameServices s, TileDef tile, float alpha = 0.5f)
    {
        q.Add(RenderLayer.Ground, 0, b =>
        {
            foreach (var c in IsoGrid.AllCells)
            {
                var p = IsoGrid.CellCenter(c);
                if (tile.IsWalkable(c)) s.Draw.DiamondFill(b, p, IsoGrid.CellWidth - 6, IsoGrid.CellHeight - 4, new Color(80, 220, 120) * (alpha * 0.45f));
                s.Draw.DiamondOutline(b, p, IsoGrid.CellWidth, IsoGrid.CellHeight, Color.White * (alpha * 0.35f));
            }
        });
    }

    public static void QueueCell(RenderQueue q, GameServices s, CellCoord c, Color color, RenderLayer layer = RenderLayer.Ground, float shrink = 6)
    {
        var p = IsoGrid.CellCenter(c);
        q.Add(layer, p.Y, b => s.Draw.DiamondFill(b, p, IsoGrid.CellWidth - shrink, IsoGrid.CellHeight - shrink / 2, color));
    }
}
