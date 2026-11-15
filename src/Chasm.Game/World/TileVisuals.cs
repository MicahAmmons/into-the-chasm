using System.Runtime.CompilerServices;
using System.Text;
using Chasm.Core.Content;
using Chasm.Core.Grid;
using Chasm.Core.World;
using Chasm.Game.Assets;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Chasm.Game.World;

/// <summary>A tile's painted regions flattened into two images: behind and in front of characters.</summary>
public sealed class TileBake : IDisposable
{
    public required RenderTarget2D Behind { get; init; }
    public required RenderTarget2D Front { get; init; }
    public bool HasBehind { get; init; }
    public bool HasFront { get; init; }
    public void Dispose() { Behind.Dispose(); Front.Dispose(); }
}

/// <summary>
/// Turns tile + floor data into pixels.
///
/// For each paint region:
///  1. Build a mask (cached): the region's cells, softened by the feather, multiplied by the cloud
///     fade (screen-space: 0 = top, 1 = bottom), so every cliff on a floor fades at the same height.
///  2. Draw the material's texture centred on the screen, rotated/flipped/tinted, repeating to
///     cover any angle.
///  3. Multiply the texture by the mask, then stack it onto the tile's "behind" or "front" image.
///
/// Bakes are cached (a handful of tiles) and rebuilt only when told the tile or floor changed.
/// Masks are cached separately, so recolouring a material rebakes without rebuilding masks.
/// </summary>
public sealed class TileVisuals : IDisposable
{
    private readonly GraphicsDevice _device;
    private readonly TextureStore _textures;
    private readonly DefRegistry _defs;
    private readonly SpriteBatch _batch;
    private RenderTarget2D? _scratch;
    private readonly LinkedList<(string key, TileBake bake)> _bakes = new();
    private readonly Dictionary<string, (Texture2D mask, long used)> _masks = new();
    private readonly HashSet<string> _dirty = new();
    private long _tick;

    /// <summary>How many tile bakes to keep (current + 8 neighbours by default).</summary>
    public int MaxBakes { get; set; } = 9;
    public int MaxMasks { get; set; } = 160;

    private static readonly BlendState MultiplyByAlpha = new()
    {
        ColorSourceBlend = Blend.Zero, ColorDestinationBlend = Blend.SourceAlpha,
        AlphaSourceBlend = Blend.Zero, AlphaDestinationBlend = Blend.SourceAlpha,
    };

    public TileVisuals(GraphicsDevice device, TextureStore textures, DefRegistry defs)
    {
        _device = device;
        _textures = textures;
        _defs = defs;
        _batch = new SpriteBatch(device);
        _textures.Reloaded += key =>
        {
            // A redrawn stencil changes masks that use it: drop its pixels and every cached mask.
            if (_stencils.Remove(key))
            {
                foreach (var (m, _) in _masks.Values) m.Dispose();
                _masks.Clear();
            }
            InvalidateAll();
        };
        _defs.Changed += d => { if (d is FloorDef) InvalidateAll(); else if (d is TileDef t) Invalidate(t); };
    }

    private static string Key(TileDef t) => $"{t.Id}#{RuntimeHelpers.GetHashCode(t)}";

    /// <summary>Call after changing a tile's regions or local materials (editors do this on every edit).</summary>
    public void Invalidate(TileDef tile) => _dirty.Add(Key(tile));

    /// <summary>Call after a floor's materials or clouds change.</summary>
    public void InvalidateAll()
    {
        foreach (var (key, _) in _bakes) _dirty.Add(key);
    }

    public FloorDef? FloorOf(TileDef tile) => TileLinks.FloorOf(_defs, tile);

    /// <summary>The baked images for a tile (built now if missing or out of date). Null if it paints nothing.</summary>
    public TileBake? Get(TileDef tile, FloorDef? floor = null)
    {
        if (tile.PaintRegions.Count == 0) return null;
        floor ??= FloorOf(tile);
        string key = Key(tile);
        var node = _bakes.First;
        while (node is not null && node.Value.key != key) node = node.Next;
        if (node is not null && !_dirty.Contains(key))
        {
            _bakes.Remove(node);
            _bakes.AddFirst(node);
            return node.Value.bake;
        }
        var target = node?.Value.bake ?? NewBake();
        if (node is not null) _bakes.Remove(node);
        var bake = Bake(tile, floor, target);
        _bakes.AddFirst((key, bake));
        _dirty.Remove(key);
        while (_bakes.Count > MaxBakes) { _bakes.Last!.Value.bake.Dispose(); _bakes.RemoveLast(); }
        return bake;
    }

    private TileBake NewBake() => new() { Behind = NewTarget(), Front = NewTarget() };

    private RenderTarget2D NewTarget() =>
        new(_device, 1920, 1080, false, SurfaceFormat.Color, DepthFormat.None, 0, RenderTargetUsage.PreserveContents);

    private TileBake Bake(TileDef tile, FloorDef? floor, TileBake into)
    {
        var previous = _device.GetRenderTargets();
        _scratch ??= NewTarget();
        bool any0 = DrawLayer(tile, floor, PaintLayer.BehindCharacters, into.Behind);
        bool any1 = DrawLayer(tile, floor, PaintLayer.InFrontOfCharacters, into.Front);
        _device.SetRenderTargets(previous);
        return new TileBake { Behind = into.Behind, Front = into.Front, HasBehind = any0, HasFront = any1 };
    }

    private bool DrawLayer(TileDef tile, FloorDef? floor, PaintLayer layer, RenderTarget2D target)
    {
        _device.SetRenderTarget(target);
        _device.Clear(Color.Transparent);
        bool any = false;
        foreach (var region in tile.PaintRegions.Where(r => r.Layer == layer && r.Cells.Count > 0))
        {
            var mat = tile.FindMaterial(region.Material, floor);
            if (mat is null) continue;
            any = true;
            // 1. texture into scratch
            _device.SetRenderTarget(_scratch);
            _device.Clear(Color.Transparent);
            if (mat.Warp is not null) DrawWarped(mat, region.Opacity);
            else
            {
                _batch.Begin(SpriteSortMode.Immediate, BlendState.AlphaBlend, SamplerState.LinearWrap);
                DrawMaterial(_batch, mat, region.Opacity);
                _batch.End();
            }
            // 2. multiply by mask
            var mask = Mask(region, mat, floor);
            _batch.Begin(SpriteSortMode.Immediate, MultiplyByAlpha, SamplerState.LinearClamp);
            _batch.Draw(mask, new Rectangle(0, 0, 1920, 1080), Color.White);
            _batch.End();
            // 3. stack onto the layer
            _device.SetRenderTarget(target);
            _batch.Begin(SpriteSortMode.Immediate, BlendState.AlphaBlend, SamplerState.PointClamp);
            _batch.Draw(_scratch, Vector2.Zero, Color.White);
            _batch.End();
        }
        return any;
    }

    /// <summary>
    /// Draws a material (no warp) as a full-screen layer: centred on the screen plus its offset,
    /// zoomed by its scale, rotated around its centre, flipped, tinted, and repeating so any
    /// rotation covers the screen. The batch must use SamplerState.LinearWrap.
    /// </summary>
    public void DrawMaterial(SpriteBatch batch, MaterialDef mat, float opacity = 1f)
    {
        var tex = _textures.Get(mat.Texture);
        float scale = Math.Clamp(mat.Scale, 0.05f, 16f);
        // Enough texture (in texture pixels) to cover the screen at any rotation and offset.
        float coverPx = 2210 + 2 * (Math.Abs(mat.OffsetX) + Math.Abs(mat.OffsetY));
        int cover = (int)MathF.Ceiling(coverPx / scale);
        int w = Math.Max(cover, tex.Width), h = Math.Max(cover, tex.Height);
        var src = new Rectangle(tex.Width / 2 - w / 2, tex.Height / 2 - h / 2, w, h);
        var fx = (mat.FlipX ? SpriteEffects.FlipHorizontally : 0) | (mat.FlipY ? SpriteEffects.FlipVertically : 0);
        var tint = TileRenderer.ParseColor(mat.Tint, Color.White) * opacity;
        var centre = MaterialSheet.Centre(mat);
        batch.Draw(tex, new Vector2(centre.X, centre.Y), src, tint, MathHelper.ToRadians(mat.Rotation), new Vector2(w / 2f, h / 2f), scale, fx, 0);
    }

    // ------------------------------------------------------------------ perspective warp

    private BasicEffect? _warpEffect;
    private const int WarpGrid = 32; // the warped sheet is drawn as a 32x32 mesh, so perspective stays smooth
    private VertexPositionColorTexture[]? _warpVerts;
    private short[]? _warpIndices;

    /// <summary>
    /// Draws one copy of a material's texture pinned to its four warped corners, with true
    /// perspective (the far end shrinks smoothly). Outside the shape stays empty.
    /// Draws into whatever render target is set.
    /// </summary>
    public void DrawWarped(MaterialDef mat, float opacity = 1f)
    {
        var tex = _textures.Get(mat.Texture);
        var c = MaterialSheet.Corners(mat, tex.Width, tex.Height);
        var h = Homography.SquareToQuad(c[0], c[1], c[2], c[3]);
        var tint = TileRenderer.ParseColor(mat.Tint, Color.White) * opacity;
        int n = WarpGrid;
        _warpVerts ??= new VertexPositionColorTexture[(n + 1) * (n + 1)];
        if (_warpIndices is null)
        {
            _warpIndices = new short[n * n * 6];
            int k = 0;
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    short i0 = (short)(y * (n + 1) + x), i1 = (short)(i0 + 1), i2 = (short)(i0 + n + 1), i3 = (short)(i2 + 1);
                    _warpIndices[k++] = i0; _warpIndices[k++] = i1; _warpIndices[k++] = i2;
                    _warpIndices[k++] = i1; _warpIndices[k++] = i3; _warpIndices[k++] = i2;
                }
        }
        for (int y = 0; y <= n; y++)
            for (int x = 0; x <= n; x++)
            {
                float u = x / (float)n, v = y / (float)n;
                var p = h.Map(u, v);
                float tu = mat.FlipX ? 1 - u : u, tv = mat.FlipY ? 1 - v : v;
                _warpVerts[y * (n + 1) + x] = new VertexPositionColorTexture(new Vector3(p.X, p.Y, 0), tint, new Vector2(tu, tv));
            }
        _warpEffect ??= new BasicEffect(_device) { TextureEnabled = true, VertexColorEnabled = true, LightingEnabled = false };
        _warpEffect.Texture = tex;
        _warpEffect.World = Matrix.Identity;
        _warpEffect.View = Matrix.Identity;
        _warpEffect.Projection = Matrix.CreateOrthographicOffCenter(0, 1920, 1080, 0, 0, 1);
        _device.BlendState = BlendState.AlphaBlend;
        _device.SamplerStates[0] = SamplerState.LinearClamp;
        _device.RasterizerState = RasterizerState.CullNone;
        _device.DepthStencilState = DepthStencilState.None;
        foreach (var pass in _warpEffect.CurrentTechnique.Passes)
        {
            pass.Apply();
            _device.DrawUserIndexedPrimitives(PrimitiveType.TriangleList, _warpVerts, 0, _warpVerts.Length, _warpIndices, 0, _warpIndices.Length / 3);
        }
    }

    // ------------------------------------------------------------------ masks

    private Texture2D Mask(PaintRegion region, MaterialDef mat, FloorDef? floor)
    {
        int feather = region.Feather ?? floor?.DefaultFeather ?? 6;
        var fade = region.FadeFor(mat);
        var sb = new StringBuilder();
        foreach (var c in region.Cells.OrderBy(c => c.Q).ThenBy(c => c.R)) sb.Append(c.Q).Append(',').Append(c.R).Append(' ');
        sb.Append('|').Append(feather).Append('|').Append(fade?.ToString() ?? "nofade");
        foreach (var st in region.Stencils) sb.Append("|s").Append(st.Cell.Q).Append(',').Append(st.Cell.R).Append(':').Append(st.Stencil).Append(st.FlipX ? "x" : "").Append(st.FlipY ? "y" : "");
        string key = sb.ToString();
        _tick++;
        if (_masks.TryGetValue(key, out var hit)) { _masks[key] = (hit.mask, _tick); return hit.mask; }

        var tex = new Texture2D(_device, RegionMask.Width, RegionMask.Height, false, SurfaceFormat.Color);
        tex.SetData(BuildMask(region.Cells, feather, fade, region.Stencils, LoadStencil));
        _masks[key] = (tex, _tick);
        if (_masks.Count > MaxMasks)
            foreach (var old in _masks.OrderBy(kv => kv.Value.used).Take(_masks.Count - MaxMasks).ToList())
            { old.Value.mask.Dispose(); _masks.Remove(old.Key); }
        return tex;
    }

    /// <summary>Region mask as texture data (alpha in every channel: white * alpha).</summary>
    public static Color[] BuildMask(IReadOnlyCollection<CellCoord> cells, int featherPx, EdgeFade? fade,
        IReadOnlyList<CellStencil>? stencils = null, Func<string, StencilImage?>? loadStencil = null)
    {
        var a = RegionMask.Build(cells, featherPx, fade, stencils, loadStencil);
        var colors = new Color[a.Length];
        for (int i = 0; i < a.Length; i++)
        {
            byte v = (byte)Math.Clamp((int)(a[i] * 255f + 0.5f), 0, 255);
            colors[i] = new Color(v, v, v, v);
        }
        return colors;
    }

    // ------------------------------------------------------------------ stencils

    private readonly Dictionary<string, StencilImage?> _stencils = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>A stencil PNG's alpha, read back from the loaded texture (cached; cleared on hot reload).</summary>
    public StencilImage? LoadStencil(string key)
    {
        if (string.IsNullOrWhiteSpace(key)) return null;
        if (_stencils.TryGetValue(key, out var hit)) return hit;
        StencilImage? img = null;
        if (_textures.Exists(key))
        {
            var tex = _textures.Get(key);
            var px = new Color[tex.Width * tex.Height];
            tex.GetData(px);
            var alpha = new float[px.Length];
            for (int i = 0; i < px.Length; i++) alpha[i] = px[i].A / 255f;
            img = new StencilImage(tex.Width, tex.Height, alpha);
        }
        _stencils[key] = img;
        return img;
    }

    public void Dispose()
    {
        foreach (var (_, b) in _bakes) b.Dispose();
        foreach (var (m, _) in _masks.Values) m.Dispose();
        _scratch?.Dispose();
        _warpEffect?.Dispose();
        _batch.Dispose();
    }
}
