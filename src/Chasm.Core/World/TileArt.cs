using Chasm.Core.Content;
using Chasm.Core.Grid;

namespace Chasm.Core.World;

/// <summary>Whether a material fades into the clouds, and which way (measured from its own edge).</summary>
public enum CloudFade
{
    [Label("No fade")] None,
    [Label("Fades into the lower clouds (cliffs going down)")] IntoLowerClouds,
    [Label("Fades into the upper clouds (walls going up)")] IntoUpperClouds,
}

/// <summary>
/// A material's perspective warp: how far each corner of the texture is pulled from where it
/// would be (pixels, in the sheet's own unrotated frame). With a warp on, one copy of the texture
/// is drawn (no repeating), so position and scale it to cover the cells.
/// </summary>
public sealed class MaterialWarp
{
    public float TopLeftX { get; set; }
    public float TopLeftY { get; set; }
    public float TopRightX { get; set; }
    public float TopRightY { get; set; }
    public float BottomRightX { get; set; }
    public float BottomRightY { get; set; }
    public float BottomLeftX { get; set; }
    public float BottomLeftY { get; set; }

    /// <summary>Corner offset by index: 0 top-left, 1 top-right, 2 bottom-right, 3 bottom-left.</summary>
    public System.Numerics.Vector2 Get(int corner) => corner switch
    {
        0 => new(TopLeftX, TopLeftY),
        1 => new(TopRightX, TopRightY),
        2 => new(BottomRightX, BottomRightY),
        _ => new(BottomLeftX, BottomLeftY),
    };

    public void Set(int corner, System.Numerics.Vector2 v)
    {
        switch (corner)
        {
            case 0: TopLeftX = v.X; TopLeftY = v.Y; break;
            case 1: TopRightX = v.X; TopRightY = v.Y; break;
            case 2: BottomRightX = v.X; BottomRightY = v.Y; break;
            default: BottomLeftX = v.X; BottomLeftY = v.Y; break;
        }
    }

    /// <summary>
    /// Quick perspective: pinch the bottom (positive) or top (negative) edge toward the middle,
    /// as a fraction of the width, and pull that edge in by <paramref name="squash"/> of the height.
    /// </summary>
    public static MaterialWarp Taper(float taper, float squash, float width, float height)
    {
        float dx = Math.Abs(taper) * width / 2f, dy = squash * height;
        var w = new MaterialWarp();
        if (taper >= 0) { w.BottomLeftX = dx; w.BottomRightX = -dx; w.BottomLeftY = w.BottomRightY = -dy; }
        else { w.TopLeftX = dx; w.TopRightX = -dx; w.TopLeftY = w.TopRightY = dy; }
        return w;
    }
}

/// <summary>Which floor page a material lives on.</summary>
public enum MaterialKind
{
    [Label("Texture (ground, paths, anything)")] General,
    [Label("Wall down (cliffs into the bottom clouds)")] WallDown,
    [Label("Wall up (walls into the top clouds)")] WallUp,
}

/// <summary>The angle of the fade's lines: along the cell edges (iso angle) or level.</summary>
public enum FadeAngle
{
    [Label("Auto (follow the cliff edge)")] Auto,
    [Label("Down-right  \\  (cell edge angle)")] DownRight,
    [Label("Down-left  /  (cell edge angle)")] DownLeft,
    [Label("Level  -")] Level,
}

/// <summary>
/// Edge fade settings for one region, in cells (1 cell = 64 px down the screen).
/// Measured from the region's own top edge (lower clouds) or bottom edge (upper clouds),
/// so a cliff fades the same way wherever it sits on screen.
/// </summary>
/// <param name="Curve">Shape of the thinning: 1 = even; above 1 stays solid longer then drops away; below 1 thins quickly at first.</param>
/// <param name="Haze">0..1: how much the bottom of the fade takes on <paramref name="HazeColor"/> (distance haze).</param>
public readonly record struct EdgeFade(bool Lower, float StartCells, float LengthCells, FadeAngle Angle, float Curve = 1f, float Haze = 0f, string HazeColor = "#a8acbc")
{
    public const float CellPx = 64f;
}

/// <summary>
/// A reusable texture recipe: one PNG plus how to show it (rotation, tint, flips, cloud fade).
/// Lives in a Floor's library (shared by every tile on the floor) or in a tile's local list.
///
/// Screen textures are mapped to the screen 1:1: assigning cells shows the part of the texture
/// that sits under those cells, so neighbouring regions line up seamlessly. Paint them as
/// 2208x2208 squares to cover the screen at any rotation; smaller textures repeat.
/// </summary>
public sealed class MaterialDef
{
    [Order(-10), Tooltip("Stable id used by paint regions. Don't rename once used.")]
    public string Id { get; set; } = "";
    [Order(-9), Required] public string Name { get; set; } = "";
    [Texture, Required] public string Texture { get; set; } = "";
    [Range(0, 360), Tooltip("Degrees, rotated around the centre of the screen.")]
    public float Rotation { get; set; }
    [Tooltip("Multiplied over the texture, #RRGGBB. #ffffff = unchanged.")]
    public string Tint { get; set; } = "#ffffff";
    public bool FlipX { get; set; }
    public bool FlipY { get; set; }
    [Hidden, Tooltip("Moves the texture sheet (pixels from the middle of the screen).")] public float OffsetX { get; set; }
    [Hidden] public float OffsetY { get; set; }
    [Hidden, Range(0.1f, 4), Tooltip("Size of the texture sheet. 1 = real size.")] public float Scale { get; set; } = 1f;
    /// <summary>Perspective warp (corner pin). Null = off: the texture repeats to cover the screen.</summary>
    [Hidden] public MaterialWarp? Warp { get; set; }
    public CloudFade Fade { get; set; } = CloudFade.None;
    [Range(0, 8), Tooltip("Cells below the cliff's top edge where the fade begins (1 cell = 64 px).")]
    public float FadeStart { get; set; } = 1f;
    [Range(0.1f, 8), Tooltip("Cells it takes to go from solid to fully faded.")]
    public float FadeLength { get; set; } = 1f;
    [Tooltip("Angle of the fade lines. Auto picks the cell-edge angle that matches the cliff's top edge.")]
    public FadeAngle FadeAngle { get; set; } = FadeAngle.Auto;
    [Hidden, Tooltip("Which page it lives on. Empty = worked out from its cloud fade (older files).")]
    public MaterialKind? Kind { get; set; }

    /// <summary>Its page: the stored kind, else walls by fade direction, else a general texture.</summary>
    public MaterialKind EffectiveKind => Kind ?? Fade switch
    {
        CloudFade.IntoLowerClouds => MaterialKind.WallDown,
        CloudFade.IntoUpperClouds => MaterialKind.WallUp,
        _ => MaterialKind.General,
    };

    [Label("Use for cut out"), Tooltip("Pick cells on a tile and this texture shows through them (one region per tile).")]
    public bool UseForCutOut { get; set; }
    public override string ToString() => string.IsNullOrEmpty(Name) ? Id : Name;
}

public enum PaintLayer
{
    [Label("Behind characters")] BehindCharacters,
    [Label("In front of characters")] InFrontOfCharacters,
}

/// <summary>
/// A stencil on one cell of a region. The stencil PNG is stretched over the cell's 128x64 box:
/// opaque pixels keep the texture, transparent pixels cut it away (partly transparent = partly).
/// </summary>
public sealed class CellStencil
{
    public CellCoord Cell { get; set; }
    [Texture] public string Stencil { get; set; } = "";
    public bool FlipX { get; set; }
    public bool FlipY { get; set; }
    /// <summary>Turns the mask around the cell's middle (degrees).</summary>
    public float Rotation { get; set; }
    /// <summary>Moves the mask within the cell (pixels).</summary>
    public float OffsetX { get; set; }
    public float OffsetY { get; set; }
    public float Scale { get; set; } = 1f;
    /// <summary>Cut the whole cell (draw nothing there).</summary>
    public bool Hide { get; set; }

    public string Key => $"{Cell.Q},{Cell.R}:{Stencil}{(FlipX ? "x" : "")}{(FlipY ? "y" : "")}{(Hide ? "H" : "")}{(Rotation != 0 || OffsetX != 0 || OffsetY != 0 || Scale != 1 ? $"/{Rotation:0.#},{OffsetX:0.#},{OffsetY:0.#},{Scale:0.##}" : "")}";
}

/// <summary>A stencil's alpha channel, 0..1, row by row.</summary>
public sealed record StencilImage(int Width, int Height, float[] Alpha)
{
    private float[]? _reach;

    /// <summary>
    /// How far down the shape reaches at u (0..1 across): the lowest opaque point of that column,
    /// 0 = top, 1 = bottom. Used when a bottom shape steers the fade instead of cutting it.
    /// </summary>
    public float ReachAt(float u)
    {
        if (_reach is null)
        {
            var r = new float[Width];
            for (int x = 0; x < Width; x++)
            {
                int low = -1;
                for (int y = Height - 1; y >= 0; y--) if (Alpha[y * Width + x] > 0.5f) { low = y; break; }
                r[x] = (low + 1) / (float)Height;
            }
            _reach = r;
        }
        float fx = Math.Clamp(u * Width - 0.5f, 0, Width - 1);
        int x0 = (int)fx, x1 = Math.Min(Width - 1, x0 + 1);
        return _reach[x0] + (_reach[x1] - _reach[x0]) * (fx - x0);
    }

    /// <summary>Bilinear sample at u, v in 0..1.</summary>
    public float Sample(float u, float v)
    {
        float fx = Math.Clamp(u * Width - 0.5f, 0, Width - 1), fy = Math.Clamp(v * Height - 0.5f, 0, Height - 1);
        int x0 = (int)fx, y0 = (int)fy, x1 = Math.Min(Width - 1, x0 + 1), y1 = Math.Min(Height - 1, y0 + 1);
        float tx = fx - x0, ty = fy - y0;
        float a = Alpha[y0 * Width + x0] * (1 - tx) + Alpha[y0 * Width + x1] * tx;
        float b = Alpha[y1 * Width + x0] * (1 - tx) + Alpha[y1 * Width + x1] * tx;
        return a * (1 - ty) + b * ty;
    }
}

/// <summary>
/// "This material covers these cells." The region list order on a tile is the stacking order.
/// </summary>
public sealed class PaintRegion
{
    [Order(-10)] public string Id { get; set; } = "";
    [Order(-9), Tooltip("A floor material id, or 'local:<id>' for one of this tile's own materials.")]
    public string Material { get; set; } = "";
    public PaintLayer Layer { get; set; } = PaintLayer.BehindCharacters;
    [Tooltip("Soft edge in pixels. Empty = the floor's default.")]
    public int? Feather { get; set; }
    [Range(0, 1)] public float Opacity { get; set; } = 1f;
    [Hidden] public List<CellCoord> Cells { get; set; } = new();
    /// <summary>Per-tile fade overrides (null = use the material's).</summary>
    [Hidden] public float? FadeStart { get; set; }
    [Hidden] public float? FadeLength { get; set; }
    [Hidden] public FadeAngle? FadeAngle { get; set; }
    /// <summary>A prefab placement: the prefab this region shows (instead of a material).</summary>
    [Hidden] public string? Prefab { get; set; }
    /// <summary>Prefab placement: nudge from the automatic anchor, in pixels.</summary>
    [Hidden] public float NudgeX { get; set; }
    [Hidden] public float NudgeY { get; set; }
    /// <summary>Prefab placement: mirror left/right on this tile.</summary>
    [Hidden] public bool MirrorX { get; set; }
    /// <summary>Prefab placement: vertical slant (y += Skew * x) so a face follows a cell edge while its verticals stay vertical.</summary>
    [Hidden] public float Skew { get; set; }
    /// <summary>Made by auto cliffs ("down" / "up"); regenerating replaces these. Null = placed by hand.</summary>
    [Hidden] public string? Auto { get; set; }
    /// <summary>Fade on/off for this region (null = the usual: walls fade, general textures don't).</summary>
    [Hidden] public bool? FadeEnabled { get; set; }

    /// <summary>Hand-drawn carve-outs: a stencil image cuts away part of a cell's texture.</summary>
    [Hidden] public List<CellStencil> Stencils { get; set; } = new();

    /// <summary>The fade this region bakes with, or null when its material doesn't fade.</summary>
    public EdgeFade? FadeFor(MaterialDef mat) => mat.Fade == CloudFade.None ? null
        : new EdgeFade(mat.Fade == CloudFade.IntoLowerClouds, FadeStart ?? mat.FadeStart, FadeLength ?? mat.FadeLength, FadeAngle ?? mat.FadeAngle);
    public override string ToString() => $"{Prefab ?? Material} ({Cells.Count} cells)";
}

public enum PropLayer
{
    [Label("Behind everything (ground decal)")] Ground,
    [Label("Sorted with characters (by its base)")] SortWithCharacters,
    [Label("In front of everything")] InFront,
}

/// <summary>A freely positioned piece of art on a tile (rock, vine, glow).</summary>
public sealed class PropDef
{
    [Order(-10)] public string Id { get; set; } = "";
    [Texture, Required] public string Texture { get; set; } = "";
    [Tooltip("Centre of the prop's base, in screen pixels.")] public float X { get; set; } = 960;
    public float Y { get; set; } = 540;
    [Range(0, 360)] public float Rotation { get; set; }
    [Range(0.05, 10)] public float Scale { get; set; } = 1f;
    public string Tint { get; set; } = "#ffffff";
    public bool FlipX { get; set; }
    public bool FlipY { get; set; }
    [Range(0, 1)] public float Opacity { get; set; } = 1f;
    public PropLayer Layer { get; set; } = PropLayer.SortWithCharacters;
    [Tooltip("Optional: makes the prop something the player can interact with.")]
    public InteractablePlacement? Interaction { get; set; }
    public override string ToString() => $"{Id}: {Texture}";
}

/// <summary>How a cloud layer combines with what's under it.</summary>
public enum CloudBlend
{
    [Label("Normal (uses the PNG's transparency)")] Normal,
    [Label("Screen (black = see-through)")] Screen,
    [Label("Add (glowing)")] Add,
}

/// <summary>Extra motion on top of a cloud layer's scroll (set Speed to 0 for motion in place).</summary>
public enum CloudPattern
{
    [Label("Scroll (straight drift)")] Scroll,
    [Label("Wave (snakes side to side)")] Wave,
    [Label("Swirl (loops in circles)")] Swirl,
    [Label("Gusts (surges and eases)")] Gusts,
    [Label("Ripple (wavy bands, like haze)")] Ripple,
    [Label("Breathe (swells and shrinks)")] Breathe,
    [Label("Shimmer (fades in and out)")] Shimmer,
}

/// <summary>Where a cloud layer is at a moment: scroll offset, size multiplier, opacity multiplier.</summary>
public readonly record struct CloudMotion(System.Numerics.Vector2 Offset, float ScaleMul, float OpacityMul);

/// <summary>
/// One animated cloud layer. A floor has a list of them (first = furthest back); tiles use the
/// floor's list and can override any layer. Always drawn behind the tile art, never baked.
///
/// The texture repeats in every direction (paint it seamless, e.g. 2048x2048) and scrolls
/// <see cref="Speed"/> pixels per second toward <see cref="Direction"/>.
/// </summary>
public sealed class CloudLayer
{
    [Order(-10), Tooltip("Stable id that tile overrides point at.")] public string Id { get; set; } = "";
    [Order(-9)] public string Name { get; set; } = "Clouds";
    public bool Enabled { get; set; } = true;
    /// <summary>
    /// A saved cloud prefab: this entry then shows all the prefab's layers (stays linked) and only
    /// its Show / Opacity apply (opacity multiplies the prefab's).
    /// </summary>
    [Ref(typeof(CloudPrefabDef)), Tooltip("Use a saved cloud prefab (all its layers) instead of this layer's own settings.")]
    public string Prefab { get; set; } = "";
    [Texture] public string Texture { get; set; } = "";
    [Tooltip("Multiplied over the clouds, #RRGGBB. White = unchanged.")] public string Tint { get; set; } = "#ffffff";
    [Range(0, 1), Tooltip("0 = invisible, 1 = solid.")] public float Opacity { get; set; } = 1f;
    public CloudBlend Blend { get; set; } = CloudBlend.Normal;
    [Range(0, 600), Tooltip("Pixels per second.")] public float Speed { get; set; } = 20f;
    [Range(0, 360), Tooltip("Which way it drifts, in degrees: 0 = right, 90 = down, 180 = left, 270 = up.")]
    public float Direction { get; set; }
    [Range(0.1f, 4), Tooltip("1 = the texture at its real size. Bigger = larger, slower-looking clouds.")]
    public float Scale { get; set; } = 1f;
    [Tooltip("Flip every other copy so edges always match. Use for art that isn't quite seamless.")]
    public bool Mirror { get; set; }

    [Tooltip("Extra motion on top of the scroll.")] public CloudPattern Pattern { get; set; } = CloudPattern.Scroll;
    [Range(0, 600), Tooltip("How strong the pattern is: pixels for movement, percent for Breathe / Shimmer.")]
    public float PatternAmount { get; set; } = 40f;
    [Range(0.5f, 120), Tooltip("Seconds for one full cycle of the pattern.")]
    public float PatternCycle { get; set; } = 8f;
    [Range(40, 2000), Tooltip("Ripple only: distance between wave crests, in pixels.")]
    public float RippleLength { get; set; } = 320f;

    public CloudLayer Copy() => (CloudLayer)MemberwiseClone();

    /// <summary>The layer's scroll + pattern at a time (seconds). Ripple's banding is applied by the renderer.</summary>
    public CloudMotion MotionAt(double seconds)
    {
        var scroll = OffsetAt(seconds);
        double rad = Direction * Math.PI / 180.0;
        var along = new System.Numerics.Vector2((float)Math.Cos(rad), (float)Math.Sin(rad));
        var across = new System.Numerics.Vector2(-along.Y, along.X);
        double phase = 2 * Math.PI * seconds / Math.Max(0.1, PatternCycle);
        float sin = (float)Math.Sin(phase), cos = (float)Math.Cos(phase), a = PatternAmount;
        return Pattern switch
        {
            CloudPattern.Wave => new(scroll + across * (a * sin), 1, 1),
            CloudPattern.Swirl => new(scroll + new System.Numerics.Vector2(cos, sin) * a, 1, 1),
            CloudPattern.Gusts => new(scroll + along * (a * (float)Math.Sin(phase - Math.Sin(phase) * 0.6)), 1, 1),
            CloudPattern.Breathe => new(scroll, 1 + a / 100f * 0.5f * (1 + sin), 1),
            CloudPattern.Shimmer => new(scroll, 1, Math.Clamp(1 - a / 100f * 0.5f * (1 + sin), 0, 1)),
            _ => new(scroll, 1, 1),
        };
    }

    /// <summary>Ripple: sideways shift of the horizontal band at screen y.</summary>
    public float RippleShiftAt(double seconds, float y) =>
        Pattern != CloudPattern.Ripple ? 0f
        : PatternAmount * (float)Math.Sin(2 * Math.PI * (seconds / Math.Max(0.1, PatternCycle) + y / Math.Max(1, RippleLength)));

    /// <summary>Scroll offset in screen pixels at a time (seconds).</summary>
    public System.Numerics.Vector2 OffsetAt(double seconds)
    {
        double rad = Direction * Math.PI / 180.0, dist = Speed * seconds;
        return new System.Numerics.Vector2((float)(Math.Cos(rad) * dist), (float)(Math.Sin(rad) * dist));
    }

    public override string ToString() => string.IsNullOrEmpty(Name) ? Id : Name;
}

/// <summary>A tile's own settings for one of its floor's cloud layers.</summary>
public sealed class TileCloudOverride
{
    [Tooltip("The floor cloud layer id this replaces on this tile.")] public string Layer { get; set; } = "";
    public CloudLayer Settings { get; set; } = new();
}

/// <summary>
/// A reusable stack of cloud layers (e.g. three at different speeds, directions and colours) that
/// floors and tiles can use as one entry. Layers draw in list order: first = furthest back.
/// </summary>
[DefFolder("cloudprefabs", "Cloud prefabs")]
[Tooltip("A reusable stack of cloud layers.")]
public sealed class CloudPrefabDef : Def
{
    public string Category { get; set; } = "Clouds";
    public List<CloudLayer> Layers { get; set; } = new();

    /// <summary>A fresh layer id inside this prefab.</summary>
    public string NewLayerId()
    {
        for (int n = 1; ; n++)
            if (Layers.All(l => l.Id != "layer_" + n)) return "layer_" + n;
    }
}

/// <summary>Works out the clouds a tile actually shows: the floor's layers with the tile's overrides applied.</summary>
public static class CloudStack
{
    public const string DefaultTexture = "Archive/Original Art/backgrounds/clouds/Cloud1.png";

    /// <summary>The tile's clouds, back to front: the bottom clouds, then the top clouds (each with overrides).</summary>
    public static List<CloudLayer> Resolve(FloorDef? floor, TileDef tile, Func<string, CloudPrefabDef?>? prefabs = null)
    {
        var result = new List<CloudLayer>();
        if (floor is null) return result;
        foreach (var layer in floor.Clouds.Concat(floor.TopClouds))
        {
            var o = tile.CloudOverrides.FirstOrDefault(x => x.Layer == layer.Id);
            var effective = o?.Settings ?? layer;
            if (!effective.Enabled || effective.Opacity <= 0) continue;
            if (!string.IsNullOrEmpty(effective.Prefab))
            {
                // A prefab: all its layers, back to front, faded by this entry's opacity.
                if (prefabs?.Invoke(effective.Prefab) is not { } prefab) continue;
                foreach (var inner in prefab.Layers)
                {
                    if (!inner.Enabled || inner.Opacity <= 0 || string.IsNullOrEmpty(inner.Texture) || !string.IsNullOrEmpty(inner.Prefab)) continue;
                    var c = inner.Copy();
                    c.Id = effective.Id + "/" + inner.Id;
                    c.Opacity *= effective.Opacity;
                    result.Add(c);
                }
                continue;
            }
            if (!string.IsNullOrEmpty(effective.Texture)) result.Add(effective);
        }
        return result;
    }

    /// <summary>A fresh layer id not used on the floor.</summary>
    public static string NewId(FloorDef floor)
    {
        for (int n = 1; ; n++)
        {
            string id = "cloud_" + n;
            if (floor.Clouds.All(c => c.Id != id) && floor.TopClouds.All(c => c.Id != id)) return id;
        }
    }
}

/// <summary>Old two-band cloud settings (before cloud layers). Only read, to convert old floor files.</summary>
public sealed class CloudLayerDef
{
    public bool Enabled { get; set; } = true;
    public string Texture { get; set; } = CloudStack.DefaultTexture;
    public string Tint { get; set; } = "#ffffff";
    public float Speed { get; set; } = 20;
    public float Opacity { get; set; } = 1f;
    // Ignored now (fades are measured from each cliff's own edge).
    public string? Coverage { get; set; }
    public int RowsOnScreen { get; set; } = 2;
    public float Line { get; set; }
    public float FadeLength { get; set; }
}
