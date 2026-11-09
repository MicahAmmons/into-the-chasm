using Chasm.Core.Content;
using Chasm.Core.Grid;

namespace Chasm.Core.World;

/// <summary>Where a material fades into the clouds (see FloorDef cloud lines).</summary>
public enum CloudFade
{
    [Label("No fade")] None,
    [Label("Fades into the lower clouds (cliffs going down)")] IntoLowerClouds,
    [Label("Fades into the upper clouds (walls going up)")] IntoUpperClouds,
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
    public CloudFade Fade { get; set; } = CloudFade.None;
    public override string ToString() => string.IsNullOrEmpty(Name) ? Id : Name;
}

public enum PaintLayer
{
    [Label("Behind characters")] BehindCharacters,
    [Label("In front of characters")] InFrontOfCharacters,
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
    public override string ToString() => $"{Material} ({Cells.Count} cells)";
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

public enum CloudCoverage
{
    [Label("Whole screen (texture repeats to fill it)")] FullScreen,
    [Label("Band at the cloud line")] Band,
}

/// <summary>A scrolling cloud layer (the visual floor separators).</summary>
public sealed class CloudLayerDef
{
    public bool Enabled { get; set; } = true;
    [Tooltip("Whole screen: the cloud texture fills everything behind the tile. Band: only near the cloud line.")]
    public CloudCoverage Coverage { get; set; } = CloudCoverage.FullScreen;
    [Range(1, 6), Tooltip("Whole screen: how many copies of the texture stack top to bottom.")]
    public int RowsOnScreen { get; set; } = 2;
    [Range(0, 1), Tooltip("Where the cloud line sits on screen: 0 = top, 1 = bottom.")]
    public float Line { get; set; } = 0.65f;
    [Range(0, 1), Tooltip("How far (fraction of screen height) textures take to fade out past the line.")]
    public float FadeLength { get; set; } = 0.15f;
    [Texture] public string Texture { get; set; } = "Original Art/backgrounds/clouds/Cloud1.png";
    public string Tint { get; set; } = "#ffffff";
    [Range(0, 2000), Tooltip("Pixels per second, drifting right and wrapping around.")]
    public float Speed { get; set; } = 20;
    [Range(0, 1)] public float Opacity { get; set; } = 1f;

    /// <summary>Alpha multiplier for a material at screen fraction y (0 top, 1 bottom).</summary>
    public float FadeAt(float y, bool lower)
    {
        if (!Enabled) return 1f;
        float len = Math.Max(0.0001f, FadeLength);
        return lower
            ? Math.Clamp(1f - (y - Line) / len, 0f, 1f)   // visible above the line, gone FadeLength below it
            : Math.Clamp(1f - (Line - y) / len, 0f, 1f);  // mirrored: gone FadeLength above the line
    }
}
