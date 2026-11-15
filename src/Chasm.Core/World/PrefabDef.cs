using System.Numerics;
using Chasm.Core.Content;

namespace Chasm.Core.World;

/// <summary>
/// A reusable piece of wall / cliff art (or any texture) made once and placed on any tile of any
/// floor: the picture plus how it looks (rotation, tint, flips, zoom, perspective warp) and where
/// its top edge sits in the picture. It deliberately has no fade: each placement (via its floor's
/// default or its own override) decides how it fades into the clouds.
///
/// Placed on a tile it is drawn once (not repeated), anchored so its lip lines up with the top of
/// its cells (bottom, for walls going up), then cut out by those cells.
/// </summary>
[DefFolder("prefabs", "Prefabs")]
[Tooltip("A reusable wall / cliff (or any) art piece placed onto tiles.")]
public sealed class PrefabDef : Def
{
    [Tooltip("Grouping in lists, e.g. 'Cliff walls' or 'Floor 3 rock'.")] public string Category { get; set; } = "Cliff walls";
    [Tooltip("Which Walls list it appears in.")] public MaterialKind Kind { get; set; } = MaterialKind.WallDown;
    [Texture, Required] public string Texture { get; set; } = "";
    [Range(0, 360)] public float Rotation { get; set; }
    public string Tint { get; set; } = "#ffffff";
    public bool FlipX { get; set; }
    public bool FlipY { get; set; }
    [Range(0.1f, 4), Tooltip("Size on screen. 1 = the picture's real size.")] public float Scale { get; set; } = 1f;
    [Range(0, 1), Tooltip("Where the cliff's top edge (lip) is in the picture: 0 = very top, 1 = very bottom. For walls going up it's the wall's foot.")]
    public float LipLine { get; set; }
    /// <summary>Perspective warp in the picture's own frame (null = none).</summary>
    public MaterialWarp? Warp { get; set; }

    /// <summary>The prefab as a material, for drawing with the shared texture code.</summary>
    public MaterialDef AsMaterial(bool extraFlipX = false) => new()
    {
        Id = Id, Name = Name, Texture = Texture, Rotation = Rotation, Tint = Tint,
        FlipX = FlipX ^ extraFlipX, FlipY = FlipY, Scale = Scale, Warp = Warp, Kind = Kind,
    };
}

/// <summary>Per-floor fade defaults for walls: the "cloud level" of a floor.</summary>
public sealed class WallFade
{
    [Range(0, 8), Tooltip("Cells from the edge before the fade begins.")] public float Start { get; set; } = 1f;
    [Range(0.1f, 8), Tooltip("Cells to go from solid to gone.")] public float Length { get; set; } = 1.5f;
    public FadeAngle Angle { get; set; } = FadeAngle.Auto;
}

/// <summary>Where a placed prefab sits and how its region fades.</summary>
public static class PrefabPlacement
{
    /// <summary>
    /// The anchor on screen: centred across the cells, at their top (wall down) or bottom (wall up),
    /// plus the placement's nudge.
    /// </summary>
    public static Vector2 Anchor(PaintRegion region, PrefabDef prefab)
    {
        if (region.Cells.Count == 0) return new Vector2(960, 540) + new Vector2(region.NudgeX, region.NudgeY);
        float minX = float.MaxValue, maxX = float.MinValue, minY = float.MaxValue, maxY = float.MinValue;
        foreach (var c in region.Cells)
        {
            var p = Grid.IsoGrid.CellCenter(c);
            minX = Math.Min(minX, p.X - Grid.IsoGrid.HalfW); maxX = Math.Max(maxX, p.X + Grid.IsoGrid.HalfW);
            minY = Math.Min(minY, p.Y - Grid.IsoGrid.HalfH); maxY = Math.Max(maxY, p.Y + Grid.IsoGrid.HalfH);
        }
        float y = prefab.Kind == MaterialKind.WallUp ? maxY : minY;
        return new Vector2((minX + maxX) / 2f + region.NudgeX, y + region.NudgeY);
    }

    /// <summary>
    /// Screen corners (top-left, top-right, bottom-right, bottom-left) of the placed picture:
    /// zoomed, warped, rotated around the lip point, which sits on the anchor.
    /// </summary>
    public static Vector2[] Corners(PaintRegion region, PrefabDef prefab, int texW, int texH)
    {
        float w = texW * prefab.Scale, h = texH * prefab.Scale;
        float top = -prefab.LipLine * h, bottom = (1 - prefab.LipLine) * h;
        var local = new[] { new Vector2(-w / 2, top), new Vector2(w / 2, top), new Vector2(w / 2, bottom), new Vector2(-w / 2, bottom) };
        if (prefab.Warp is { } warp)
            for (int i = 0; i < 4; i++) local[i] += warp.Get(i);
        if (region.Skew != 0) // slant along a cell edge; verticals stay vertical
            for (int i = 0; i < 4; i++) local[i] = new Vector2(local[i].X, local[i].Y + region.Skew * local[i].X);
        var anchor = Anchor(region, prefab);
        float r = prefab.Rotation * MathF.PI / 180f, cs = MathF.Cos(r), sn = MathF.Sin(r);
        var result = local.Select(p => anchor + new Vector2(p.X * cs - p.Y * sn, p.X * sn + p.Y * cs)).ToArray();
        if (region.MirrorX) // mirror the placed picture around the anchor's vertical line
            result = new[] { Mirror(result[1], anchor), Mirror(result[0], anchor), Mirror(result[3], anchor), Mirror(result[2], anchor) };
        return result;
    }

    private static Vector2 Mirror(Vector2 p, Vector2 a) => new(2 * a.X - p.X, p.Y);

    /// <summary>
    /// How a prefab placement fades: off if it says so, else its own numbers where set, else its
    /// floor's default for walls going down / up. General prefabs don't fade unless given numbers.
    /// </summary>
    public static EdgeFade? FadeFor(PaintRegion region, PrefabDef prefab, FloorDef? floor)
    {
        if (region.FadeEnabled == false) return null;
        bool up = prefab.Kind == MaterialKind.WallUp;
        if (prefab.Kind == MaterialKind.General && region.FadeEnabled != true) return null;
        var d = (up ? floor?.WallUpFade : floor?.WallDownFade) ?? new WallFade();
        return new EdgeFade(!up, region.FadeStart ?? d.Start, region.FadeLength ?? d.Length, region.FadeAngle ?? d.Angle);
    }
}
