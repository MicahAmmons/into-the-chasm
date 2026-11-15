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
    private readonly LinkedList<(string key, TileBake bake)> _bakes = new();
    private readonly Dictionary<string, (Texture2D mask, long used)> _masks = new();
    /// <summary>Distance haze per mask slot: how much each pixel takes on the haze colour (premultiplied white x amount).</summary>
    private readonly Dictionary<string, Texture2D> _hazes = new();
    private readonly HashSet<string> _dirty = new();
    private long _tick;

    /// <summary>Editors set this while a slider or drag is active: rebakes are spaced out so it stays smooth.</summary>
    public bool LiveEditing { get; set; }
    private readonly System.Diagnostics.Stopwatch _bakeClock = System.Diagnostics.Stopwatch.StartNew();
    private readonly Dictionary<string, long> _lastBake = new();
    private long _lastBakeMs;
    /// <summary>How long the last tile bake took (ms), for the editor's status bar.</summary>
    public long LastBakeMs => _lastBakeMs;

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
            // A redrawn picture: cached pieces may show it, so redraw them all.
            foreach (var (rt, _, _) in _pieces.Values) rt.Dispose();
            _pieces.Clear();
            // A redrawn stencil changes masks that use it: drop its pixels and every cached mask.
            if (_stencils.Remove(key))
            {
                foreach (var (m, _) in _masks.Values) m.Dispose();
                _masks.Clear();
                foreach (var h in _hazes.Values) h.Dispose();
                _hazes.Clear();
                _maskFade.Clear();
                _maskArea.Clear();
                _maskBases.Clear();
                _maskShapes.Clear();
                _edges.Clear();
            }
            InvalidateAll();
        };
        _defs.Changed += d => { if (d is FloorDef or PrefabDef or WallDef or GroundPrefabDef) InvalidateAll(); else if (d is TileDef t) Invalidate(t); };
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
        floor ??= FloorOf(tile);
        if (tile.PaintRegions.Count == 0 && (floor is null || floor.WallsDown.Count + floor.WallsUp.Count + floor.GroundWalkable.Count + floor.GroundUnwalkable.Count == 0)) return null;
        string key = Key(tile);
        var node = _bakes.First;
        while (node is not null && node.Value.key != key) node = node.Next;
        // While an editor slider is being dragged, rebake at most ~12 times a second.
        // While dragging: quick bakes run every frame; only slow ones are spaced out.
        bool throttled = node is not null && LiveEditing && _lastBakeMs > 12
            && _bakeClock.ElapsedMilliseconds - _lastBake.GetValueOrDefault(key) < Math.Clamp(_lastBakeMs * 2, 33, 120);
        if (node is not null && (!_dirty.Contains(key) || throttled))
        {
            _bakes.Remove(node);
            _bakes.AddFirst(node);
            return node.Value.bake;
        }
        _lastBake[key] = _bakeClock.ElapsedMilliseconds;
        var target = node?.Value.bake ?? NewBake();
        if (node is not null) _bakes.Remove(node);
        long t0 = _bakeClock.ElapsedMilliseconds;
        var bake = Bake(tile, floor, target);
        _lastBakeMs = _bakeClock.ElapsedMilliseconds - t0;
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
        _tileKey = Key(tile);
        RendersThisBake = 0;
        bool any0 = DrawLayer(tile, floor, PaintLayer.BehindCharacters, into.Behind);
        bool any1 = DrawLayer(tile, floor, PaintLayer.InFrontOfCharacters, into.Front);
        EvictPieces();
        _device.SetRenderTargets(previous);
        return new TileBake { Behind = into.Behind, Front = into.Front, HasBehind = any0, HasFront = any1 };
    }

    /// <summary>Walls are looked up here; editors can slip in an unsaved wall for previews.</summary>
    public Func<string, WallDef?>? WallOverride { get; set; }

    private WallDef? FindWall(string id) =>
        WallOverride?.Invoke(id) ?? (_defs.TryGet<WallDef>(id, out var w) ? w : null);

    /// <summary>The tile's live walls (down and up), worked out from its walkable cells and its floor's wall lists.</summary>
    public List<WallPiece> WallsFor(TileDef tile, FloorDef? floor, List<WallJunction>? junctions = null) =>
        AutoWalls.Build(tile, floor, up: false, FindWall, junctions, FindCombo).Concat(AutoWalls.Build(tile, floor, up: true, FindWall, junctions, FindCombo)).ToList();

    /// <summary>Saved end-cap combos; editors can slip in an unsaved one.</summary>
    public Func<string, EndCapComboDef?>? ComboOverride { get; set; }
    private EndCapComboDef? FindCombo(string id) =>
        ComboOverride?.Invoke(id) ?? (_defs.TryGet<EndCapComboDef>(id, out var c) ? c : null);

    private bool DrawLayer(TileDef tile, FloorDef? floor, PaintLayer layer, RenderTarget2D target)
    {
        _device.SetRenderTarget(target);
        _device.Clear(Color.Transparent);
        bool any = false;
        // Walls: faces behind everything painted; rims over the walkway after it.
        long w0 = _bakeClock.ElapsedTicks;
        var walls = layer == PaintLayer.BehindCharacters ? WallsFor(tile, floor) : new List<WallPiece>();
        WallsTicks += _bakeClock.ElapsedTicks - w0;
        int feather = floor?.DefaultFeather ?? 6;
        // Ground on unwalkable cells sits under the cliffs; walkable ground over the cliff tops,
        // under painted textures.
        if (layer == PaintLayer.BehindCharacters) any |= DrawGround(tile, floor, walkable: false, target);
        foreach (var piece in walls.Where(p => !p.Rim)) { DrawWallPiece(piece, feather, target); any = true; }
        if (layer == PaintLayer.BehindCharacters) any |= DrawGround(tile, floor, walkable: true, target);
        foreach (var region in tile.PaintRegions.Where(r => r.Layer == layer && r.Cells.Count > 0))
        {
            // A prefab placement draws its prefab once at its anchor; otherwise the region's material.
            PrefabDef? prefab = region.Prefab is { } pid && _defs.TryGet<PrefabDef>(pid, out var pf) ? pf : null;
            var mat = prefab is not null ? prefab.AsMaterial() : tile.FindMaterial(region.Material, floor);
            if (mat is null || (region.Prefab is not null && prefab is null)) continue;
            // The mask first: its rectangle is all that gets drawn.
            var fade = prefab is not null ? PrefabPlacement.FadeFor(region, prefab, floor) : region.FadeFor(mat);
            var (mask, rect, maskKey) = Mask(region, fade, floor);
            if (mask is null) continue;
            any = true;
            System.Numerics.Vector2[]? corners = null;
            Texture2D? ptex = null;
            if (prefab is not null) { ptex = _textures.Get(prefab.Texture); corners = PrefabPlacement.Corners(region, prefab, ptex.Width, ptex.Height); }
            else if (mat.Warp is not null) { var t = _textures.Get(mat.Texture); corners = MaterialSheet.Corners(mat, t.Width, t.Height); }
            string content = maskKey + "#" + LookKey(mat) + (corners is null ? "" : CornersKey(corners)) + $"|o{region.Opacity:0.###}";
            Piece($"region:{region.Id}:{layer}", content, mask, rect, target, r =>
            {
                if (prefab is not null) DrawQuad(ptex!, corners!, mat.Tint, prefab.FlipX, prefab.FlipY, region.Opacity, r);
                else if (mat.Warp is not null) DrawWarped(mat, region.Opacity, r);
                else
                {
                    _batch.Begin(SpriteSortMode.Immediate, BlendState.AlphaBlend, SamplerState.LinearWrap, null, null, null, Matrix.CreateTranslation(-r.X, -r.Y, 0));
                    DrawMaterial(_batch, mat, region.Opacity);
                    _batch.End();
                }
            });
        }
        foreach (var piece in walls.Where(p => p.Rim)) { DrawWallPiece(piece, 0, target); any = true; }
        return any;
    }

    /// <summary>One wall piece: its picture (pinned, slanted, maybe mirrored) cut to its cells, seams, caps and fade.</summary>
    private void DrawWallPiece(WallPiece piece, int feather, RenderTarget2D target)
    {
        var (mask, rect, maskKey) = Mask(piece.Cells, feather, piece.Stencils, piece.Clips, piece.Fade, piece.Bottom, piece.Ends);
        if (mask is null) return;
        Texture2D tex = piece.IsOverlay ? _textures.Pixel : _textures.Get(piece.Texture);
        // The picture, repeated along long walls (copies fixed to the world, so they never slide).
        var copies = piece.IsOverlay
            ? new() { new[] { new System.Numerics.Vector2(rect.Left, rect.Top), new System.Numerics.Vector2(rect.Right, rect.Top), new System.Numerics.Vector2(rect.Right, rect.Bottom), new System.Numerics.Vector2(rect.Left, rect.Bottom) } }
            : piece.Copies(tex.Width, tex.Height, rect.Left, rect.Right);
        string content = $"{maskKey}#{piece.Kind}|{(piece.IsOverlay ? "" : piece.Texture)}|{piece.Tint}|{piece.Opacity:0.###}|{piece.Wall.FlipX}{piece.Wall.FlipY}{string.Concat(copies.Select(CornersKey))}";
        string identity = $"wall:{piece.Kind}:{piece.Up}:{piece.RightFace}:{piece.Lip.X:0.#},{piece.Lip.Y:0.#}";
        bool flipX = !piece.IsOverlay && piece.Wall.FlipX, flipY = !piece.IsOverlay && piece.Wall.FlipY;
        // Distance haze: the haze colour laid over the picture where the fade says (before the mask cuts it).
        var hazeTex = !piece.IsOverlay && piece.Fade is { Haze: > 0 } hf ? HazeFor(maskKey) : null;
        var hazeCol = hazeTex is null ? Color.Transparent : TileRenderer.ParseColor(piece.Fade!.Value.HazeColor, Color.LightGray);
        Piece(identity, content, mask, rect, target, r =>
        {
            foreach (var c in copies) DrawQuad(tex, c, piece.Tint, flipX, flipY, piece.IsOverlay ? piece.Opacity : 1f, r);
            if (hazeTex is null) return;
            _batch.Begin(SpriteSortMode.Immediate, BlendState.AlphaBlend, SamplerState.LinearClamp);
            _batch.Draw(hazeTex, new Rectangle(0, 0, r.Width, r.Height), hazeCol);
            _batch.End();
        });
    }

    /// <summary>The haze texture made with a mask (looked up by the key Mask returned).</summary>
    private Texture2D? HazeFor(string maskKey)
    {
        int bar = maskKey.LastIndexOf("|f:", StringComparison.Ordinal);
        string slot = bar < 0 ? maskKey : maskKey[..bar];
        return _hazes.TryGetValue(slot, out var t) ? t : null;
    }

    // ------------------------------------------------------------------ ground

    /// <summary>Ground prefabs; editors can slip in an unsaved one.</summary>
    public Func<string, GroundPrefabDef?>? GroundOverride { get; set; }
    private GroundPrefabDef? FindGround(string id) =>
        GroundOverride?.Invoke(id) ?? (_defs.TryGet<GroundPrefabDef>(id, out var g) ? g : null);

    private readonly Dictionary<string, (float[] shape, long used)> _groundShapes = new();
    private readonly Dictionary<string, (Texture2D tex, MaskArea area, long used)> _groundMasks = new();

    private static string CellsKey(IEnumerable<CellCoord> cells)
    {
        var sb = new StringBuilder();
        foreach (var c in cells.OrderBy(c => c.Q).ThenBy(c => c.R)) sb.Append(c.Q).Append(',').Append(c.R).Append(' ');
        return sb.Length + ":" + sb.ToString().GetHashCode();
    }

    /// <summary>Cells as a soft-edged shape over the whole screen (half resolution), cached.</summary>
    private float[] GroundShape(IReadOnlyCollection<CellCoord> cells, int feather)
    {
        string key = CellsKey(cells) + "|" + feather;
        if (_groundShapes.TryGetValue(key, out var hit)) { _groundShapes[key] = (hit.shape, ++_tick); return hit.shape; }
        var (_, shape) = RegionMask.BuildBaseIn(MaskArea.Full, cells, feather);
        if (_groundShapes.Count >= 32) _groundShapes.Remove(_groundShapes.OrderBy(kv => kv.Value.used).First().Key);
        _groundShapes[key] = (shape, ++_tick);
        return shape;
    }

    /// <summary>A full-screen mask made by <paramref name="build"/>, cropped to where it shows, as a texture (cached by key).</summary>
    private (Texture2D? tex, Rectangle rect) GroundMask(string key, Func<float[]> build)
    {
        if (_groundMasks.TryGetValue(key, out var hit)) { _groundMasks[key] = (hit.tex, hit.area, ++_tick); return (hit.tex, ToScreen(hit.area)); }
        var a = build();
        int W = RegionMask.Width, H = RegionMask.Height, x0 = W, y0 = H, x1 = -1, y1 = -1;
        for (int y = 0; y < H; y++)
            for (int x = 0; x < W; x++)
                if (a[y * W + x] > 0.002f) { if (x < x0) x0 = x; if (x > x1) x1 = x; if (y < y0) y0 = y; if (y > y1) y1 = y; }
        if (x1 < 0) return (null, Rectangle.Empty);
        var area = new MaskArea(x0, y0, x1 - x0 + 1, y1 - y0 + 1);
        var colors = new Color[area.W * area.H];
        for (int y = 0; y < area.H; y++)
            for (int x = 0; x < area.W; x++)
            {
                byte v = (byte)Math.Clamp((int)(a[(y + y0) * W + x + x0] * 255f + 0.5f), 0, 255);
                colors[y * area.W + x] = new Color(v, v, v, v);
            }
        var tex = new Texture2D(_device, area.W, area.H, false, SurfaceFormat.Color);
        tex.SetData(colors);
        if (_groundMasks.Count >= 96)
            foreach (var old in _groundMasks.OrderBy(kv => kv.Value.used).Take(16).ToList()) { old.Value.tex.Dispose(); _groundMasks.Remove(old.Key); }
        _groundMasks[key] = (tex, area, ++_tick);
        return (tex, ToScreen(area));
    }

    private RenderTarget2D? _groundScratch;

    /// <summary>A full-screen mask (not cropped) as a texture, cached by key.</summary>
    private Texture2D FullMask(string key, Func<float[]> build)
    {
        if (_groundMasks.TryGetValue(key, out var hit)) { _groundMasks[key] = (hit.tex, hit.area, ++_tick); return hit.tex; }
        var a = build();
        var colors = new Color[a.Length];
        for (int i = 0; i < a.Length; i++) { byte v = (byte)Math.Clamp((int)(a[i] * 255f + 0.5f), 0, 255); colors[i] = new Color(v, v, v, v); }
        var tex = new Texture2D(_device, RegionMask.Width, RegionMask.Height, false, SurfaceFormat.Color);
        tex.SetData(colors);
        _groundMasks[key] = (tex, MaskArea.Full, ++_tick);
        return tex;
    }

    /// <summary>
    /// The tile's ground. Unwalkable: each painted region, layers back to front, soft at its edge.
    /// Walkable: every region is put together first (the automatic ground everywhere, painted grounds
    /// blended over it), then the whole lot is cut to the walkable cells with one soft edge, so no
    /// ground shows through another at the cliff edge. Then the darkened rim.
    /// </summary>
    private bool DrawGround(TileDef tile, FloorDef? floor, bool walkable, RenderTarget2D target)
    {
        var regions = GroundMap.Build(tile, floor).Where(r => r.Walkable == walkable).ToList();
        if (regions.Count == 0) return false;
        var allWalk = walkable ? IsoGrid.AllCells.Where(tile.IsWalkable).ToList() : new List<CellCoord>();
        string walkKey = walkable ? CellsKey(allWalk) : "";
        var baseGround = walkable ? FindGround(regions.FirstOrDefault(r => r.Base)?.Prefab ?? "") : null;
        int edgeSoft = baseGround?.EdgeSoftness ?? 6;
        float[] WalkEdge() => GroundShape(allWalk, edgeSoft);

        var into = target;
        if (walkable)
        {
            _groundScratch ??= NewTarget();
            into = _groundScratch;
            _device.SetRenderTarget(into);
            _device.Clear(Color.Transparent);
        }
        bool any = false;
        foreach (var region in regions)
        {
            if (FindGround(region.Prefab) is not { } g) continue;
            string cellsKey = CellsKey(region.Cells);
            // Where this ground goes before any edge: walkable base = everywhere; painted walkable =
            // its cells blended across its transition (spilling into the void beside them, which the
            // walkable edge cuts later); unwalkable = its cells, soft at the edge.
            string regionKey = !walkable ? $"v|{cellsKey}|e{g.EdgeSoftness}" : region.Base ? "base" : $"w|{cellsKey}|t{g.Transition}|{walkKey}";
            float[]? regionMask = null;
            float[] Region()
            {
                if (regionMask is not null) return regionMask;
                if (!walkable) return regionMask = GroundShape(region.Cells, g.EdgeSoftness);
                if (region.Base) { var ones = new float[RegionMask.Width * RegionMask.Height]; Array.Fill(ones, 1f); return regionMask = ones; }
                var mine = region.Cells.ToHashSet();
                var withVoid = region.Cells.Concat(region.Cells.SelectMany(Neighbours).Where(c => IsoGrid.InTile(c) && !tile.IsWalkable(c))).Distinct().ToList();
                return regionMask = GroundShape(withVoid, g.Transition);
            }
            for (int li = 0; li < g.Layers.Count; li++)
            {
                var layer = g.Layers[li];
                if (!layer.Enabled || layer.Opacity <= 0 || string.IsNullOrEmpty(layer.Texture)) continue;
                var (tex, rect) = GroundMask($"g|{regionKey}|{layer.PatchKey}", () =>
                {
                    var a = (float[])Region().Clone();
                    if (layer.Patches)
                        for (int y = 0; y < RegionMask.Height; y++)
                            for (int x = 0; x < RegionMask.Width; x++)
                            {
                                int i = y * RegionMask.Width + x;
                                if (a[i] > 0) a[i] *= GroundNoise.Patch(x * 2 + 1, y * 2 + 1, layer.PatchSize, layer.PatchAmount, layer.PatchSoftness, layer.PatchSeed);
                            }
                    return a;
                });
                if (tex is null) continue;
                var mat = layer.AsMaterial();
                float op = layer.Opacity;
                Piece($"ground:{walkable}:{region.Prefab}:{region.Base}:{li}", $"{regionKey}|{layer.PatchKey}#{LookKey(mat)}|o{op:0.###}", tex, rect, into, r =>
                {
                    _batch.Begin(SpriteSortMode.Immediate, BlendState.AlphaBlend, SamplerState.LinearWrap, null, null, null, Matrix.CreateTranslation(-r.X, -r.Y, 0));
                    DrawMaterial(_batch, mat, op);
                    _batch.End();
                });
                any = true;
            }
        }
        if (walkable && any)
        {
            // One soft edge for all the walkable ground, then onto the tile.
            var edge = FullMask($"we|{walkKey}|{edgeSoft}", () => (float[])WalkEdge().Clone());
            _device.SetRenderTarget(into);
            _batch.Begin(SpriteSortMode.Immediate, MultiplyByAlpha, SamplerState.LinearClamp);
            _batch.Draw(edge, new Rectangle(0, 0, 1920, 1080), Color.White);
            _batch.End();
            _device.SetRenderTarget(target);
            _batch.Begin(SpriteSortMode.Immediate, BlendState.AlphaBlend, SamplerState.PointClamp);
            _batch.Draw(into, Vector2.Zero, Color.White);
            _batch.End();
        }
        // Darker toward the edge where the ground ends (the walkable edge, for walkable ground).
        foreach (var region in regions)
        {
            if (FindGround(region.Prefab) is not { EdgeShade: > 0 } g) continue;
            var cells = walkable ? allWalk : region.Cells.ToList();
            int width = (int)Math.Clamp(g.EdgeShadeWidth, 4, 160);
            string cellsKey = CellsKey(region.Cells);
            string key = $"gs|{walkable}|{region.Base}|{cellsKey}|{walkKey}|{width}|{g.EdgeShade:0.###}|{g.EdgeSoftness}|{g.Transition}|{edgeSoft}";
            var (tex, rect) = GroundMask(key, () =>
            {
                var inner = GroundShape(cells, width * 2);
                var a = walkable
                    ? (region.Base ? (float[])WalkEdge().Clone() : Mul(GroundShape(region.Cells.Concat(region.Cells.SelectMany(Neighbours).Where(c => IsoGrid.InTile(c) && !tile.IsWalkable(c))).Distinct().ToList(), g.Transition), WalkEdge()))
                    : (float[])GroundShape(region.Cells, g.EdgeSoftness).Clone();
                for (int i = 0; i < a.Length; i++) a[i] *= g.EdgeShade * Math.Clamp((1f - inner[i]) * 2f, 0f, 1f);
                return a;
            });
            if (tex is null) continue;
            Piece($"groundshade:{walkable}:{region.Prefab}:{region.Base}", key, tex, rect, target, r =>
            {
                _batch.Begin(SpriteSortMode.Immediate, BlendState.AlphaBlend);
                _batch.Draw(_textures.Pixel, new Rectangle(0, 0, r.Width, r.Height), Color.Black);
                _batch.End();
            });
        }
        return any;
    }

    private static float[] Mul(float[] a, float[] b)
    {
        var m = new float[a.Length];
        for (int i = 0; i < m.Length; i++) m[i] = a[i] * b[i];
        return m;
    }

    private static IEnumerable<CellCoord> Neighbours(CellCoord c)
    {
        for (int dq = -1; dq <= 1; dq++)
            for (int dr = -1; dr <= 1; dr++)
                if (dq != 0 || dr != 0) yield return new CellCoord(c.Q + dq, c.R + dr);
    }

    // ------------------------------------------------------------------ piece cache
    //
    // Every wall piece and painted region keeps its finished picture (texture x mask) in a small
    // render target the size of its rectangle. A rebake re-renders only pieces whose inputs
    // changed and just pastes the others - so dragging a slider redraws the few pieces it affects.

    private readonly Dictionary<string, (RenderTarget2D rt, string content, long used)> _pieces = new();
    private string _tileKey = "";
    private const int MaxPieces = 600;

    private void Piece(string identity, string content, Texture2D mask, Rectangle rect, RenderTarget2D target, Action<Rectangle> drawPicture)
    {
        string key = _tileKey + "/" + identity;
        _tick++;
        if (!_pieces.TryGetValue(key, out var cached) || cached.content != content || cached.rt.Width != rect.Width || cached.rt.Height != rect.Height)
        {
            var rt = cached.rt;
            if (rt is null || rt.IsDisposed || rt.Width != rect.Width || rt.Height != rect.Height)
            {
                rt?.Dispose();
                rt = new RenderTarget2D(_device, rect.Width, rect.Height, false, SurfaceFormat.Color, DepthFormat.None, 0, RenderTargetUsage.PreserveContents);
            }
            _device.SetRenderTarget(rt);
            _device.Clear(Color.Transparent);
            drawPicture(rect);
            _batch.Begin(SpriteSortMode.Immediate, MultiplyByAlpha, SamplerState.LinearClamp);
            _batch.Draw(mask, new Rectangle(0, 0, rect.Width, rect.Height), Color.White);
            _batch.End();
            cached = (rt, content, _tick);
            RendersThisBake++;
        }
        _pieces[key] = (cached.rt, cached.content, _tick);
        _device.SetRenderTarget(target);
        _batch.Begin(SpriteSortMode.Immediate, BlendState.AlphaBlend, SamplerState.PointClamp);
        _batch.Draw(cached.rt, rect, Color.White);
        _batch.End();
    }

    /// <summary>Pieces re-rendered in the last bake (the rest were reused).</summary>
    public int RendersThisBake { get; private set; }

    private void EvictPieces()
    {
        if (_pieces.Count <= MaxPieces) return;
        foreach (var old in _pieces.OrderBy(kv => kv.Value.used).Take(_pieces.Count - MaxPieces).ToList())
        { old.Value.rt.Dispose(); _pieces.Remove(old.Key); }
    }

    private static string CornersKey(System.Numerics.Vector2[] c) =>
        string.Concat(c.Select(p => $"|{p.X:0.#},{p.Y:0.#}"));

    private static string LookKey(MaterialDef m) =>
        $"{m.Texture}|{m.Rotation:0.##}|{m.Tint}|{m.FlipX}{m.FlipY}|{m.OffsetX:0.#},{m.OffsetY:0.#}|{m.Scale:0.###}";

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
    public void DrawWarped(MaterialDef mat, float opacity = 1f, Rectangle? into = null)
    {
        var tex = _textures.Get(mat.Texture);
        DrawQuad(tex, MaterialSheet.Corners(mat, tex.Width, tex.Height), mat.Tint, mat.FlipX, mat.FlipY, opacity, into);
    }

    /// <summary>One copy of a texture pinned to four screen corners (TL, TR, BR, BL) with true perspective.</summary>
    /// <remarks><paramref name="into"/>: the render target is just this screen rectangle (a piece's own image).</remarks>
    public void DrawQuad(Texture2D tex, System.Numerics.Vector2[] c, string tintHex, bool flipX, bool flipY, float opacity = 1f, Rectangle? into = null)
    {
        var h = Homography.SquareToQuad(c[0], c[1], c[2], c[3]);
        var tint = TileRenderer.ParseColor(tintHex, Color.White) * opacity;
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
                float tu = flipX ? 1 - u : u, tv = flipY ? 1 - v : v;
                _warpVerts[y * (n + 1) + x] = new VertexPositionColorTexture(new Vector3(p.X, p.Y, 0), tint, new Vector2(tu, tv));
            }
        _warpEffect ??= new BasicEffect(_device) { TextureEnabled = true, VertexColorEnabled = true, LightingEnabled = false };
        _warpEffect.Texture = tex;
        _warpEffect.World = Matrix.Identity;
        _warpEffect.View = Matrix.Identity;
        _warpEffect.Projection = into is { } r
            ? Matrix.CreateOrthographicOffCenter(r.Left, r.Right, r.Bottom, r.Top, 0, 1)
            : Matrix.CreateOrthographicOffCenter(0, 1920, 1080, 0, 0, 1);
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

    // Masks are built for each piece's own rectangle (cells + room for the soft edge), in steps:
    //   base (cells, stencils, soft edge, clips) - cached by its shape
    //   edge line for a fade direction          - cached by shape + direction + angle
    //   final texture                           - one per shape + direction, refilled when only the fade numbers change
    private readonly Dictionary<string, (MaskArea area, float[] inside, float[] shape, long used)> _maskBases = new();
    private readonly Dictionary<string, float[]> _edges = new();
    private readonly Dictionary<string, (MaskArea area, float[] inside, float[] shape)> _maskShapes = new();
    private readonly Dictionary<string, string> _maskFade = new();
    private readonly Dictionary<string, MaskArea> _maskArea = new();
    private const int MaxBases = 160;

    private (Texture2D? mask, Rectangle rect, string key) Mask(PaintRegion region, EdgeFade? fade, FloorDef? floor) =>
        Mask(region.Cells, region.Feather ?? floor?.DefaultFeather ?? 6, region.Stencils, null, fade);

    /// <summary>Profiling (dev): time spent working out walls and masks since last reset.</summary>
    public long WallsTicks, MaskTicks;
    public static double TicksToMs(long t) => t * 1000.0 / System.Diagnostics.Stopwatch.Frequency;

    private (Texture2D? mask, Rectangle rect, string key) Mask(IReadOnlyCollection<CellCoord> cells, int feather, IReadOnlyList<CellStencil> stencils, IReadOnlyList<ClipZone>? clips, EdgeFade? fade,
        IReadOnlyList<BottomSegment>? bottom = null, IReadOnlyList<EndStencil>? ends = null)
    {
        long m0 = _bakeClock.ElapsedTicks;
        try { return MaskCore(cells, feather, stencils, clips, fade, bottom, ends); }
        finally { MaskTicks += _bakeClock.ElapsedTicks - m0; }
    }

    private (Texture2D? mask, Rectangle rect, string key) MaskCore(IReadOnlyCollection<CellCoord> cells, int feather, IReadOnlyList<CellStencil> stencils, IReadOnlyList<ClipZone>? clips, EdgeFade? fade,
        IReadOnlyList<BottomSegment>? bottom, IReadOnlyList<EndStencil>? ends)
    {
        var sb = new StringBuilder();
        foreach (var c in cells.OrderBy(c => c.Q).ThenBy(c => c.R)) sb.Append(c.Q).Append(',').Append(c.R).Append(' ');
        sb.Append('|').Append(feather);
        foreach (var st in stencils) sb.Append("|s").Append(st.Key);
        string shapeKey = sb.ToString();   // cells + soft edge: the expensive part
        if (clips is not null) foreach (var c in clips) sb.Append("|c").Append(c.Key);
        if (ends is not null) foreach (var e in ends) sb.Append("|e").Append(e.Key);
        string baseKey = sb.ToString();    // + cut lines (corners, trims, caps): cheap to redo
        string slot = baseKey + "|" + (fade is { } f0 ? $"{f0.Lower}/{f0.Angle}" : "none");
        string fadeKey = (fade?.ToString() ?? "none") + (bottom is { Count: > 0 } && fade is not null ? "|b" + string.Join(";", bottom.Select(x => x.Key)) : "");
        _tick++;
        if (_masks.TryGetValue(slot, out var hit) && _maskFade.TryGetValue(slot, out var had) && had == fadeKey && _maskArea.TryGetValue(slot, out var hitArea))
        {
            _masks[slot] = (hit.mask, _tick);
            return (hit.mask, ToScreen(hitArea), slot + "|f:" + fadeKey);
        }

        // Base shape (cells, carving, blur, clips) - reused while only the fade changes. The blurred
        // shape before the cut lines is cached on its own, so corner / cap sliders only redo the cuts.
        if (!_maskBases.TryGetValue(baseKey, out var b))
        {
            if (!_maskShapes.TryGetValue(shapeKey, out var shaped))
            {
                var area = RegionMask.AreaFor(cells, feather);
                if (area.IsEmpty) return (null, Rectangle.Empty, "");
                var (ins, blurred) = RegionMask.BuildBaseIn(area, cells, feather, stencils, LoadStencil, null);
                shaped = (area, ins, blurred);
                if (_maskShapes.Count >= MaxBases) _maskShapes.Remove(_maskShapes.Keys.First());
                _maskShapes[shapeKey] = shaped;
            }
            var shape = (float[])shaped.shape.Clone();
            if (clips is { Count: > 0 }) RegionMask.ApplyClipsIn(shaped.area, shape, clips);
            if (ends is { Count: > 0 }) RegionMask.ApplyEndsIn(shaped.area, shape, ends, LoadStencil);
            b = (shaped.area, shaped.inside, shape, _tick);
            if (_maskBases.Count >= MaxBases)
                foreach (var old in _maskBases.OrderBy(kv => kv.Value.used).Take(_maskBases.Count - MaxBases + 1).ToList())
                {
                    _maskBases.Remove(old.Key);
                    foreach (var k in _edges.Keys.Where(k => k.StartsWith(old.Key + "|", StringComparison.Ordinal)).ToList()) _edges.Remove(k);
                }
        }
        _maskBases[baseKey] = (b.area, b.inside, b.shape, _tick);

        var a = (float[])b.shape.Clone();
        float[]? haze = null;
        if (fade is { } f)
        {
            // The fade's edge only depends on the cells and direction, not on the cut lines.
            string edgeKey = shapeKey + "|" + f.Lower + "/" + f.Angle;
            if (!_edges.TryGetValue(edgeKey, out var edge)) _edges[edgeKey] = edge = RegionMask.EdgeIn(b.area, b.inside, f);
            haze = f.Haze > 0 ? new float[a.Length] : null;
            RegionMask.ApplyFadeIn(b.area, a, edge, f, bottom, LoadStencil, haze);
        }
        // Distance haze: its own little texture next to the mask (white x amount, premultiplied).
        if (haze is not null)
        {
            var hc = new Color[haze.Length];
            for (int i = 0; i < haze.Length; i++) { byte v = (byte)Math.Clamp((int)(haze[i] * 255f + 0.5f), 0, 255); hc[i] = new Color(v, v, v, v); }
            if (!_hazes.TryGetValue(slot, out var ht) || ht.Width != b.area.W || ht.Height != b.area.H)
            {
                ht?.Dispose();
                _hazes[slot] = ht = new Texture2D(_device, b.area.W, b.area.H, false, SurfaceFormat.Color);
            }
            ht.SetData(hc);
        }
        else if (_hazes.Remove(slot, out var gone)) gone.Dispose();
        var colors = new Color[a.Length];
        for (int i = 0; i < a.Length; i++)
        {
            byte v = (byte)Math.Clamp((int)(a[i] * 255f + 0.5f), 0, 255);
            colors[i] = new Color(v, v, v, v);
        }
        // Refill the texture for this shape + direction (same size) instead of making a new one.
        if (!_masks.TryGetValue(slot, out var existing) || existing.mask.Width != b.area.W || existing.mask.Height != b.area.H)
        {
            existing.mask?.Dispose();
            existing = (new Texture2D(_device, b.area.W, b.area.H, false, SurfaceFormat.Color), _tick);
            if (_masks.Count >= MaxMasks)
                foreach (var old in _masks.OrderBy(kv => kv.Value.used).Take(_masks.Count - MaxMasks + 1).ToList())
                { old.Value.mask.Dispose(); _masks.Remove(old.Key); _maskFade.Remove(old.Key); _maskArea.Remove(old.Key); if (_hazes.Remove(old.Key, out var oh)) oh.Dispose(); }
        }
        existing.mask.SetData(colors);
        _masks[slot] = (existing.mask, _tick);
        _maskFade[slot] = fadeKey;
        _maskArea[slot] = b.area;
        return (existing.mask, ToScreen(b.area), slot + "|f:" + fadeKey);
    }

    /// <summary>A half-resolution mask area as a full-resolution screen rectangle.</summary>
    private static Rectangle ToScreen(MaskArea a) => new(a.X * 2, a.Y * 2, a.W * 2, a.H * 2);

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
        foreach (var (rt, _, _) in _pieces.Values) rt.Dispose();
        _warpEffect?.Dispose();
        _batch.Dispose();
    }
}
