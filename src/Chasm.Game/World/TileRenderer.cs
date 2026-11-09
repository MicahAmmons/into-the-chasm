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

    public static void QueueLayers(RenderQueue q, GameServices s, TileDef tile)
    {
        var bg = ParseColor(tile.BackgroundColor, new Color(26, 29, 46));
        q.Add(RenderLayer.Background, 0, b => s.Draw.Rect(b, new Rectangle(0, 0, 1920, 1080), bg));
        foreach (var layer in tile.Layers)
        {
            var tex = s.Textures.Get(layer.Texture);
            if (s.Textures.IsPlaceholder(tex)) continue; // missing tile art: just show the colour + grid
            var target = layer.Kind switch
            {
                TileLayerKind.Foreground => RenderLayer.Foreground,
                TileLayerKind.Fog => RenderLayer.Fog,
                _ => RenderLayer.Background,
            };
            float opacity = layer.Opacity;
            q.Add(target, 0, b => b.Draw(tex, new Rectangle(0, 0, 1920, 1080), Color.White * opacity));
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
