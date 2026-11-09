using Chasm.Core.Grid;
using Chasm.Core.Items;
using Chasm.Core.World;
using Chasm.Game.Rendering;
using Chasm.Game.World;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Chasm.Tools.MapEditor;

public sealed partial class MapEditorScene
{
    private static readonly Color WalkGreen = new(60, 220, 110), BlockRed = new(230, 60, 60);
    private static readonly Color PlayerBlue = new(70, 140, 255), EnemyOrange = new(255, 140, 50);
    private readonly RenderQueue _refQueue = new();

    public override void Draw(SpriteBatch batch)
    {
        if (_tile is null)
        {
            batch.Begin();
            Services.Draw.Rect(batch, new Rectangle(0, 0, 1920, 1080), new Color(24, 26, 38));
            batch.End();
            return;
        }
        var s = Services;
        var t = _tile;
        if (_showNeighbours) RenderNeighbours(batch);

        // Tile art (live, using the working copies of the tile and floor).
        if (_showArt) TileRenderer.QueueLayers(_queue, s, t, _floor, includeProps: false);
        else _queue.Add(RenderLayer.Background, 0, b => s.Draw.Rect(b, new Rectangle(0, 0, 1920, 1080), Color.White));
        TileRenderer.QueueProps(_queue, s, t, 1f, _selectedProp);
        _queue.Flush(batch);

        if (_reference is not null && _referenceOverlay && _referenceOpacity > 0)
        {
            TileRenderer.QueueLayers(_refQueue, s, _reference, null, includeProps: true, opacity: _referenceOpacity);
            _refQueue.Flush(batch);
        }

        batch.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend);
        DrawOverlays(batch);
        batch.End();
    }

    private void DrawOverlays(SpriteBatch b)
    {
        var d = Services.Draw;
        var t = _tile!;
        bool cellMode = _tool is Tool.Walkable or Tool.PlayerStarts or Tool.EnemySpawns;

        foreach (var c in IsoGrid.AllCells)
        {
            var p = IsoGrid.CellCenter(c);
            if (_tool == Tool.Walkable)
                d.DiamondFill(b, p, 118, 59, (t.Walkable[c] ? WalkGreen : BlockRed) * 0.38f);
            else if (cellMode && t.Walkable[c])
                d.DiamondFill(b, p, 118, 59, WalkGreen * 0.15f);
            if (_showGrid) d.DiamondOutline(b, p, IsoGrid.CellWidth, IsoGrid.CellHeight, EditorPrefs.ToColor(_prefs.GridColor));
            var edges = IsoGrid.EdgesOf(c);
            if (edges != Edge.None && t.Walkable[c] && _showMarkers && TileLinks.ExitsFor(Services.Defs, t).Any(e => (edges & e.Edge) != 0))
                d.DiamondOutline(b, p, 100, 50, new Color(120, 200, 255), 2);
        }

        if (_showMarkers || cellMode)
        {
            foreach (var c in t.PlayerCombatStarts) d.DiamondFill(b, IsoGrid.CellCenter(c), 70, 35, PlayerBlue * 0.9f);
            foreach (var c in t.EnemyCombatStarts) d.DiamondFill(b, IsoGrid.CellCenter(c), 70, 35, EnemyOrange * 0.9f);
            d.DiamondOutline(b, IsoGrid.CellCenter(t.Spawn), 90, 45, Color.Yellow, 3);
        }

        // Paint selection + selected region.
        foreach (var c in _selection) d.DiamondFill(b, IsoGrid.CellCenter(c), 110, 55, new Color(255, 220, 60) * 0.45f);
        if (_selectedRegion is { } reg)
            foreach (var c in reg.Cells) d.DiamondOutline(b, IsoGrid.CellCenter(c), 120, 60, Color.Cyan, 2);

        // Placements (objects).
        foreach (var pl in t.Placements) DrawPlacementMarker(b, pl);

        // Props: selection box.
        if (_selectedProp is { } sp) d.RectOutline(b, PropBounds(sp), Color.Yellow, 2);

        // Interaction radius preview.
        if (_radiusCenter is { } rc)
            foreach (var c in IsoGrid.AllCells.Where(c => c.ManhattanTo(rc) <= _radius))
                d.DiamondFill(b, IsoGrid.CellCenter(c), 116, 58, new Color(200, 120, 255) * 0.35f);
        if (_selectedPlacement is InteractablePlacement { Trigger: InteractTrigger.Proximity } ip)
            foreach (var c in IsoGrid.AllCells.Where(c => c.ManhattanTo(ip.Cell) <= ip.Radius))
                d.DiamondOutline(b, IsoGrid.CellCenter(c), 110, 55, new Color(200, 120, 255), 2);

        // Cloud lines.
        if (_showCloudLines && _floor is not null)
        {
            CloudLine(b, _floor.LowerClouds, "lower cloud line", true);
            CloudLine(b, _floor.UpperClouds, "upper cloud line", false);
        }

        if (_prefs.ShowCenterLines)
        {
            var cl = EditorPrefs.ToColor(_prefs.CenterLineColor);
            d.Line(b, new Vector2(960, 0), new Vector2(960, 1080), cl, 2);
            d.Line(b, new Vector2(0, 540), new Vector2(1920, 540), cl, 2);
        }
        if (_selectedCell is { } sc) d.DiamondOutline(b, IsoGrid.CellCenter(sc), IsoGrid.CellWidth, IsoGrid.CellHeight, Color.White, 2);
        if (_hoverCell is { } hc)
            d.DiamondOutline(b, IsoGrid.CellCenter(hc), IsoGrid.CellWidth, IsoGrid.CellHeight, _cellPick is not null ? Color.Cyan : Color.White * 0.9f, 2);
        if (_tool == Tool.PlaceProp && _hoverCell is not null && _propTexture.Length > 0)
        {
            var tex = Services.Textures.Get(_propTexture);
            var pos = _snapProps ? IsoGrid.CellCenter(IsoGrid.CellAt(_hoverPx)) : _hoverPx;
            if (!Services.Textures.IsPlaceholder(tex)) b.Draw(tex, pos, null, Color.White * 0.55f, 0, new Vector2(tex.Width / 2f, tex.Height), 1f, SpriteEffects.None, 0);
        }
        if (_cellPick is not null) d.TextCentered(b, "PICK A CELL", new Vector2(960, 40), Color.Cyan, 32, bold: true);
    }

    private void CloudLine(SpriteBatch b, CloudLayerDef c, string label, bool lower)
    {
        if (!c.Enabled) return;
        var d = Services.Draw;
        float y = c.Line * 1080, y2 = (c.Line + (lower ? c.FadeLength : -c.FadeLength)) * 1080;
        for (int x = 0; x < 1920; x += 40) d.Line(b, new Vector2(x, y), new Vector2(x + 24, y), Color.Cyan, 3);
        for (int x = 0; x < 1920; x += 40) d.Line(b, new Vector2(x, y2), new Vector2(x + 12, y2), Color.Cyan * 0.6f, 2);
        d.TextShadowed(b, label, new Vector2(12, y - 26), Color.Cyan, 18);
        d.TextShadowed(b, "fully faded", new Vector2(12, y2 + (lower ? 4 : -24)), Color.Cyan * 0.7f, 14);
    }

    private void DrawPlacementMarker(SpriteBatch b, Placement pl)
    {
        var d = Services.Draw;
        var center = IsoGrid.CellCenter(pl.Cell);
        string label = pl switch
        {
            ItemPickupPlacement ip => Services.Defs.TryGet<ItemDef>(ip.Item, out var it) ? it.DisplayName : "Item?",
            InteractablePlacement ip => (ip.Invisible ? "(hidden) " : "") + ip.Label + (ip.Trigger == InteractTrigger.Proximity ? $" r{ip.Radius}" : ""),
            FigurePlacement f => f.Name,
            _ => pl.Describe(Services.Defs),
        };
        var rect = new Rectangle((int)center.X - 44, (int)center.Y - 54, 88, 48);
        d.Placeholder(b, rect, label);
        if (pl is MonsterGroupPlacement mg && mg.PatrolPoints.Count > 0)
        {
            var pts = new List<CellCoord> { mg.Cell };
            pts.AddRange(mg.PatrolPoints);
            if (mg.Patrol == PatrolMode.Loop) pts.Add(mg.Cell);
            for (int i = 0; i + 1 < pts.Count; i++)
                d.Line(b, IsoGrid.CellCenter(pts[i]), IsoGrid.CellCenter(pts[i + 1]), new Color(255, 120, 100) * 0.8f, 3);
        }
        if (pl == _selectedPlacement) d.DiamondOutline(b, center, IsoGrid.CellWidth, IsoGrid.CellHeight, Color.Yellow, 3);
    }

    // ------------------------------------------------------------------ neighbours (drawn outside the canvas when zoomed out)

    private readonly Dictionary<Edge, (RenderTarget2D rt, TileDef tile)> _neighbours = new();
    private SpriteBatch? _overlayBatch;
    private readonly RenderQueue _nQueue = new();

    private void RenderNeighbours(SpriteBatch batch)
    {
        var device = Services.Graphics;
        var previous = device.GetRenderTargets();
        foreach (var edge in new[] { Edge.North, Edge.East, Edge.South, Edge.West })
        {
            var n = TileLinks.Neighbour(Services.Defs, _tile!, edge);
            if (n is null) { if (_neighbours.Remove(edge, out var old)) old.rt.Dispose(); continue; }
            if (!_neighbours.TryGetValue(edge, out var entry))
                entry = (new RenderTarget2D(device, 1920, 1080, false, SurfaceFormat.Color, DepthFormat.None, 0, RenderTargetUsage.PreserveContents), n);
            entry.tile = n;
            _neighbours[edge] = entry;
            device.SetRenderTarget(entry.rt);
            device.Clear(Color.Transparent);
            TileRenderer.QueueLayers(_nQueue, Services, n);
            _nQueue.Flush(batch);
        }
        device.SetRenderTargets(previous);
    }

    private void DisposeNeighbours()
    {
        foreach (var (rt, _) in _neighbours.Values) rt.Dispose();
        _neighbours.Clear();
        _overlayBatch?.Dispose();
        _overlayBatch = null;
    }

    /// <summary>After the canvas is shown: neighbour tiles around it (window space).</summary>
    public override void DrawOverlayUi()
    {
        if (!_showNeighbours || _tile is null || _neighbours.Count == 0) return;
        var canvas = Services.Canvas;
        var dest = canvas.Destination;
        _overlayBatch ??= new SpriteBatch(Services.Graphics);
        Services.Graphics.ScissorRectangle = canvas.Area;
        _overlayBatch.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.LinearClamp, null,
            new RasterizerState { ScissorTestEnable = true, CullMode = CullMode.None });
        foreach (var (edge, (rt, tile)) in _neighbours)
        {
            var (dx, dy) = TileLinks.Offset(edge);
            var r = new Rectangle(dest.X + dx * dest.Width, dest.Y + dy * dest.Height, dest.Width, dest.Height);
            _overlayBatch.Draw(rt, r, Color.White * 0.55f);
            Services.Draw.TextShadowed(_overlayBatch, $"{tile.DisplayName} [{tile.Id}]", new Vector2(r.X + 12, r.Y + 8), Color.White, 20, bold: true);
        }
        Services.Draw.RectOutline(_overlayBatch, dest, Color.White * 0.6f, 2);
        _overlayBatch.End();
    }
}
