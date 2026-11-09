using System.Reflection;
using Chasm.Core.Content;
using Chasm.Core.Grid;
using Chasm.Core.Items;
using Chasm.Core.Serialization;
using Chasm.Core.World;
using Chasm.Game.Rendering;
using Chasm.Game.Scenes;
using Chasm.Game.World;
using ImGuiNET;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using Num = System.Numerics;

namespace Chasm.Tools.Scenes;

/// <summary>
/// Paint and furnish Tiles.
///
/// Left panel: which tile, which tool. Middle: the tile at true scale. Right panel: whatever is
/// selected (a cell, a placement, or the tile's own settings), edited with the generic inspector,
/// so new placement kinds and fields need no editor changes.
///
/// Workflow example ("an item that only appears if the player has another item"):
///   Select tool -> click a cell -> "Add: Item pickup" -> Item: "+ New Item..." (wizard) ->
///   Visible When: "+ Add Condition" -> "Player has item" -> pick the item -> Save (Ctrl+S).
/// </summary>
public sealed class MapEditorScene : Scene
{
    private enum Tool { Select, Walkable, Blocked, PlayerStart, EnemyStart, Spawn }

    private const int LeftPanel = 300, RightPanel = 460, MenuHeight = 24;

    private readonly RenderQueue _queue = new();
    private TileDef? _tile;               // working copy
    private string? _requestedTile;
    private bool _dirty;
    private Tool _tool = Tool.Walkable;
    private int _brush = 1;
    private CellCoord? _hoverCell;
    private CellCoord? _selectedCell;
    private Placement? _selected;
    private Placement? _dragging;
    private bool _showArt = true, _showForeground = true, _showGrid = true, _showLabels = true;
    private Action<CellCoord>? _pickCallback;
    private string _tileSearch = "";
    private string? _status;
    private float _statusTime;
    private string? _switchTo;

    public MapEditorScene(string? tileId = null) => _requestedTile = tileId;

    public override void OnEnter()
    {
        ToolContext.EnsureInitialized(Services);
        Services.Canvas.Region = () =>
        {
            var pp = Services.Graphics.PresentationParameters;
            return new Rectangle(LeftPanel, MenuHeight, pp.BackBufferWidth - LeftPanel - RightPanel, pp.BackBufferHeight - MenuHeight);
        };
        ToolContext.Wizards.Inspector.CellPicker = cb => { _pickCallback = cb; Status("Click a cell on the map to pick it (Esc cancels)."); };
        Services.Defs.Changed += OnDefChanged;
        var id = _requestedTile ?? Services.Defs.All<TileDef>().FirstOrDefault()?.Id;
        if (id is not null) Open(id);
    }

    public override void OnExit()
    {
        Services.Canvas.Region = null;
        ToolContext.Wizards.Inspector.CellPicker = null;
        Services.Defs.Changed -= OnDefChanged;
    }

    private void OnDefChanged(Def def)
    {
        // Edited elsewhere (text editor / another tool) and we have no unsaved changes: follow it.
        if (def is TileDef t && _tile is not null && t.Id == _tile.Id && !_dirty && !ReferenceEquals(t, _tile))
            Open(t.Id);
    }

    private void Open(string id)
    {
        if (!Services.Defs.TryGet<TileDef>(id, out var src)) return;
        _tile = ChasmJson.Clone(src);
        _dirty = false;
        _selected = null;
        _selectedCell = null;
    }

    private void Save()
    {
        if (_tile is null) return;
        _tile.CommitWalkable();
        var problems = ContentValidator.Validate(_tile, Services.Defs);
        Services.DefReload.IgnoreNextChange(Services.Defs.PathFor(typeof(TileDef), _tile.Id));
        Services.Defs.Save(ChasmJson.Clone(_tile));
        _dirty = false;
        Status(problems.Count == 0 ? $"Saved {_tile.Id}." : $"Saved {_tile.Id} with {problems.Count} warning(s): {problems[0]}");
    }

    private void Status(string text) { _status = text; _statusTime = 6; }

    private void MarkDirty() => _dirty = true;

    // ------------------------------------------------------------------ update

    public override void Update(float dt)
    {
        _statusTime -= dt;
        ToolContext.MenuBar(Services, _tile is null ? "Map Editor" : $"Map Editor - {_tile.Id}{(_dirty ? " *" : "")}", Menus);
        LeftPanelGui();
        RightPanelGui();
        ToolContext.Wizards.Draw();
        UnsavedPopup();

        var input = Services.Input;
        if (input.KeyHeld(Keys.LeftControl) && input.KeyPressed(Keys.S)) Save();
        if (_tile is null) return;

        _hoverCell = input.MouseInCanvas && !input.MouseCapturedByUi ? IsoGrid.CellAt(input.MouseCanvas.ToNumerics()) : null;
        if (_hoverCell is { } hc && !IsoGrid.InTile(hc)) _hoverCell = null;

        if (_pickCallback is not null)
        {
            if (input.Pressed(Game.Input.InputAction.Cancel)) { _pickCallback = null; Status("Pick cancelled."); }
            else if (_hoverCell is { } pc && input.LeftClicked) { _pickCallback(pc); _pickCallback = null; MarkDirty(); Status($"Picked {pc}."); }
            return;
        }
        if (input.KeyPressed(Keys.D1)) _tool = Tool.Select;
        if (input.KeyPressed(Keys.D2)) _tool = Tool.Walkable;
        if (input.KeyPressed(Keys.D3)) _tool = Tool.Blocked;
        if (input.KeyPressed(Keys.Delete) && _selected is not null) DeleteSelected();

        if (_hoverCell is not { } cell) { if (input.LeftReleased) _dragging = null; return; }

        switch (_tool)
        {
            case Tool.Select:
                if (input.LeftClicked)
                {
                    _selectedCell = cell;
                    _selected = _tile.Placements.LastOrDefault(p => p.Cell == cell);
                    _dragging = _selected;
                }
                else if (input.LeftHeld && _dragging is not null && _dragging.Cell != cell)
                {
                    _dragging.Cell = cell; _selectedCell = cell; MarkDirty();
                }
                if (input.LeftReleased) _dragging = null;
                break;
            case Tool.Walkable or Tool.Blocked:
                bool paint = _tool == Tool.Walkable;
                if (input.LeftHeld) Paint(cell, paint);
                else if (input.RightHeld) Paint(cell, !paint);
                break;
            case Tool.PlayerStart:
                if (input.LeftClicked) Toggle(_tile.PlayerCombatStarts, cell);
                break;
            case Tool.EnemyStart:
                if (input.LeftClicked) Toggle(_tile.EnemyCombatStarts, cell);
                break;
            case Tool.Spawn:
                if (input.LeftClicked) { _tile.Spawn = cell; MarkDirty(); }
                break;
        }
    }

    private void Paint(CellCoord center, bool walkable)
    {
        int r = _brush - 1;
        for (int dq = -r; dq <= r; dq++)
            for (int dr = -r; dr <= r; dr++)
            {
                var c = new CellCoord(center.Q + dq, center.R + dr);
                if (!IsoGrid.InTile(c) || _tile!.Walkable[c] == walkable) continue;
                _tile.Walkable[c] = walkable;
                MarkDirty();
            }
    }

    private void Toggle(List<CellCoord> list, CellCoord c)
    {
        if (!list.Remove(c)) list.Add(c);
        MarkDirty();
    }

    private void DeleteSelected()
    {
        if (_tile is null || _selected is null) return;
        _tile.Placements.Remove(_selected);
        Status($"Removed {_selected.Id}.");
        _selected = null;
        MarkDirty();
    }

    // ------------------------------------------------------------------ GUI

    private void Menus()
    {
        if (!ImGui.BeginMenu("Tile")) return;
        if (ImGui.MenuItem("Save", "Ctrl+S", false, _tile is not null)) Save();
        if (ImGui.MenuItem("Revert to saved", null, false, _dirty && _tile is not null)) Open(_tile!.Id);
        if (ImGui.MenuItem("New tile...")) ToolContext.Wizards.OpenNew(typeof(TileDef), id => RequestSwitch(id), NewTileTemplate());
        if (ImGui.MenuItem("Play this tile (World Sandbox)", null, false, _tile is not null))
        {
            if (_dirty) Save();
            Services.Scenes.ResetTo(new WorldSandboxScene(_tile!.Id, _selectedCell));
        }
        ImGui.EndMenu();
    }

    private TileDef NewTileTemplate()
    {
        var t = new TileDef { Floor = _tile?.Floor ?? 1 };
        foreach (var c in IsoGrid.AllCells) t.Walkable[c] = false;
        t.CommitWalkable();
        return t;
    }

    private void RequestSwitch(string id)
    {
        if (_dirty) { _switchTo = id; ImGui.OpenPopup("Unsaved changes"); }
        else Open(id);
    }

    private void UnsavedPopup()
    {
        if (_switchTo is not null && !ImGui.IsPopupOpen("Unsaved changes")) ImGui.OpenPopup("Unsaved changes");
        if (!ImGui.BeginPopupModal("Unsaved changes", ImGuiWindowFlags.AlwaysAutoResize)) return;
        ImGui.Text($"{_tile?.Id} has unsaved changes.");
        if (ImGui.Button("Save", new Num.Vector2(100, 0))) { Save(); Open(_switchTo!); _switchTo = null; ImGui.CloseCurrentPopup(); }
        ImGui.SameLine();
        if (ImGui.Button("Discard", new Num.Vector2(100, 0))) { Open(_switchTo!); _switchTo = null; ImGui.CloseCurrentPopup(); }
        ImGui.SameLine();
        if (ImGui.Button("Cancel", new Num.Vector2(100, 0))) { _switchTo = null; ImGui.CloseCurrentPopup(); }
        ImGui.EndPopup();
    }

    private void LeftPanelGui()
    {
        var io = ImGui.GetIO();
        ImGui.SetNextWindowPos(new Num.Vector2(0, MenuHeight));
        ImGui.SetNextWindowSize(new Num.Vector2(LeftPanel, io.DisplaySize.Y - MenuHeight));
        ImGui.Begin("##left", ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoMove);

        ImGui.SeparatorText("Tiles");
        ImGui.SetNextItemWidth(-1);
        ImGui.InputTextWithHint("##ts", "search tiles...", ref _tileSearch, 60);
        ImGui.BeginChild("tiles", new Num.Vector2(0, 200), ImGuiChildFlags.Borders);
        foreach (var floor in Services.Defs.All<TileDef>().GroupBy(t => t.Floor).OrderBy(g => g.Key))
        {
            ImGui.TextDisabled($"Floor {floor.Key}");
            foreach (var t in floor)
            {
                if (_tileSearch.Length > 0 && !t.Id.Contains(_tileSearch, StringComparison.OrdinalIgnoreCase) &&
                    !t.DisplayName.Contains(_tileSearch, StringComparison.OrdinalIgnoreCase)) continue;
                if (ImGui.Selectable($"  {t.DisplayName}##{t.Id}", t.Id == _tile?.Id) && t.Id != _tile?.Id) RequestSwitch(t.Id);
            }
        }
        ImGui.EndChild();
        if (ImGui.Button("+ New tile", new Num.Vector2(-1, 0)))
            ToolContext.Wizards.OpenNew(typeof(TileDef), id => RequestSwitch(id), NewTileTemplate());

        ImGui.SeparatorText("Tool");
        ToolButton(Tool.Select, "Select / place (1)", "Click a cell to see and add things on it. Drag objects to move them. Del removes.");
        ToolButton(Tool.Walkable, "Paint walkable (2)", "Left-drag paints walkable cells. Right-drag erases.");
        ToolButton(Tool.Blocked, "Paint blocked (3)", "Left-drag blocks cells. Right-drag makes them walkable.");
        ToolButton(Tool.PlayerStart, "Player combat starts", "Click to toggle a cell where the player may start a fight (blue).");
        ToolButton(Tool.EnemyStart, "Enemy combat starts", "Click to toggle a cell where enemies may start a fight (red).");
        ToolButton(Tool.Spawn, "Spawn point", "Where the player lands when teleported here (yellow).");
        if (_tool is Tool.Walkable or Tool.Blocked)
        {
            ImGui.SetNextItemWidth(-1);
            ImGui.SliderInt("##brush", ref _brush, 1, 4, "Brush size %d");
            if (ImGui.Button("Fill all walkable")) { foreach (var c in IsoGrid.AllCells) _tile!.Walkable[c] = true; MarkDirty(); }
            ImGui.SameLine();
            if (ImGui.Button("Clear all")) { foreach (var c in IsoGrid.AllCells) _tile!.Walkable[c] = false; MarkDirty(); }
        }

        ImGui.SeparatorText("View");
        ImGui.Checkbox("Art", ref _showArt); ImGui.SameLine();
        ImGui.Checkbox("Foreground", ref _showForeground);
        ImGui.Checkbox("Grid", ref _showGrid); ImGui.SameLine();
        ImGui.Checkbox("Labels", ref _showLabels);

        ImGui.SeparatorText("Info");
        if (_hoverCell is { } h) ImGui.Text($"Cell {h}  {IsoGrid.EdgesOf(h)}");
        else ImGui.TextDisabled("Hover the map");
        if (_tile is not null)
        {
            var problems = ContentValidator.Validate(_tile, Services.Defs);
            if (problems.Count == 0) ImGui.TextColored(new Num.Vector4(0.5f, 1, 0.6f, 1), "No problems.");
            else foreach (var p in problems) ImGui.TextColored(new Num.Vector4(1, 0.6f, 0.4f, 1), p);
        }
        if (_statusTime > 0 && _status is not null) { ImGui.Spacing(); ImGui.TextWrapped(_status); }
        ImGui.End();
    }

    private void ToolButton(Tool tool, string label, string tip)
    {
        if (ImGui.RadioButton(label, _tool == tool)) _tool = tool;
        if (ImGui.IsItemHovered()) ImGui.SetTooltip(tip);
    }

    private int _rightTab;

    private void RightPanelGui()
    {
        var io = ImGui.GetIO();
        ImGui.SetNextWindowPos(new Num.Vector2(io.DisplaySize.X - RightPanel, MenuHeight));
        ImGui.SetNextWindowSize(new Num.Vector2(RightPanel, io.DisplaySize.Y - MenuHeight));
        ImGui.Begin("##right", ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoMove);
        if (_tile is null) { ImGui.TextDisabled("No tile open."); ImGui.End(); return; }

        if (ImGui.BeginTabBar("tabs"))
        {
            if (ImGui.BeginTabItem("Selection")) { _rightTab = 0; SelectionGui(); ImGui.EndTabItem(); }
            if (ImGui.BeginTabItem("Tile settings")) { _rightTab = 1; TileSettingsGui(); ImGui.EndTabItem(); }
            if (ImGui.BeginTabItem($"All objects ({_tile.Placements.Count})")) { _rightTab = 2; AllObjectsGui(); ImGui.EndTabItem(); }
            ImGui.EndTabBar();
        }
        ImGui.End();
    }

    private void SelectionGui()
    {
        var tile = _tile!;
        if (_selectedCell is not { } cell)
        {
            ImGui.TextWrapped("Use the Select tool (1) and click a cell to inspect it and place objects on it.");
            return;
        }
        ImGui.SeparatorText($"Cell {cell}");
        bool walk = tile.Walkable[cell];
        if (ImGui.Checkbox("Walkable", ref walk)) { tile.Walkable[cell] = walk; MarkDirty(); }
        bool ps = tile.PlayerCombatStarts.Contains(cell), es = tile.EnemyCombatStarts.Contains(cell);
        if (ImGui.Checkbox("Player combat start", ref ps)) Toggle(tile.PlayerCombatStarts, cell);
        if (ImGui.Checkbox("Enemy combat start", ref es)) Toggle(tile.EnemyCombatStarts, cell);
        if (ImGui.Button("Set as spawn point")) { tile.Spawn = cell; MarkDirty(); }

        ImGui.SeparatorText("Add to this cell");
        foreach (var t in PolymorphicTypes.ConcreteSubtypes(typeof(Placement)))
        {
            if (ImGui.Button(Editing.Inspector.LabelFor(t)))
            {
                var p = (Placement)Activator.CreateInstance(t)!;
                p.Id = tile.NewPlacementId(PlacementPrefix(t));
                p.Cell = cell;
                tile.Placements.Add(p);
                _selected = p;
                MarkDirty();
            }
            if (t.GetCustomAttribute<TooltipAttribute>() is { } tt && ImGui.IsItemHovered()) ImGui.SetTooltip(tt.Text);
            ImGui.SameLine();
        }
        ImGui.NewLine();

        var here = tile.Placements.Where(p => p.Cell == cell).ToList();
        if (here.Count > 0)
        {
            ImGui.SeparatorText("On this cell");
            foreach (var p in here)
                if (ImGui.Selectable($"{p.Id}: {p.Describe(Services.Defs)}", p == _selected)) _selected = p;
        }

        if (_selected is not null && tile.Placements.Contains(_selected))
        {
            ImGui.SeparatorText($"Edit {_selected.Id}");
            if (ImGui.Button("Delete object")) { DeleteSelected(); return; }
            ImGui.SameLine();
            if (ImGui.Button("Duplicate"))
            {
                var copy = ChasmJson.Clone(_selected);
                copy.Id = tile.NewPlacementId(PlacementPrefix(copy.GetType()));
                tile.Placements.Add(copy);
                _selected = copy;
                MarkDirty();
            }
            ImGui.BeginChild("placement");
            if (ToolContext.Wizards.Inspector.Edit(_selected)) MarkDirty();
            ImGui.EndChild();
        }
    }

    private static string PlacementPrefix(Type t) => PolymorphicTypes.TypeId(t).ToLowerInvariant() switch
    {
        "itempickup" => "item",
        "interactable" => "obj",
        "monstergroup" => "monsters",
        "figure" => "figure",
        var s => s,
    };

    private void TileSettingsGui()
    {
        ImGui.BeginChild("settings");
        ImGui.TextDisabled($"content/defs/tiles/{_tile!.Id}.json");
        string name = _tile.Name;
        ImGui.AlignTextToFramePadding(); ImGui.Text("Name"); ImGui.SameLine(170); ImGui.SetNextItemWidth(-1);
        if (ImGui.InputText("##n", ref name, 200)) { _tile.Name = name; MarkDirty(); }
        string[] painted = { nameof(TileDef.Placements), nameof(TileDef.PlayerCombatStarts), nameof(TileDef.EnemyCombatStarts), nameof(Def.Id), nameof(Def.Name) };
        if (ToolContext.Wizards.Inspector.Edit(_tile, p => !painted.Contains(p.Name))) MarkDirty();
        ImGui.EndChild();
    }

    private void AllObjectsGui()
    {
        foreach (var p in _tile!.Placements.ToList())
            if (ImGui.Selectable($"{p.Id}  {p.Cell}  {p.Describe(Services.Defs)}", p == _selected))
            {
                _selected = p; _selectedCell = p.Cell; _tool = Tool.Select;
            }
    }

    // ------------------------------------------------------------------ draw

    public override void Draw(SpriteBatch batch)
    {
        if (_tile is null) return;
        var s = Services;
        if (_showArt)
        {
            var copy = new TileDef { BackgroundColor = _tile.BackgroundColor, Layers = _tile.Layers.Where(l => _showForeground || l.Kind == TileLayerKind.Background).ToList() };
            TileRenderer.QueueLayers(_queue, s, copy);
        }
        else _queue.Add(RenderLayer.Background, 0, b => s.Draw.Rect(b, new Rectangle(0, 0, 1920, 1080), new Color(30, 32, 44)));

        _queue.Add(RenderLayer.WorldUi, 0, b =>
        {
            foreach (var c in IsoGrid.AllCells)
            {
                var p = IsoGrid.CellCenter(c);
                if (_tile.Walkable[c]) s.Draw.DiamondFill(b, p, 120, 60, new Color(60, 220, 110) * 0.28f);
                else if (!_showArt) s.Draw.DiamondFill(b, p, 120, 60, new Color(10, 10, 14) * 0.6f);
                if (_showGrid) s.Draw.DiamondOutline(b, p, IsoGrid.CellWidth, IsoGrid.CellHeight, Color.White * 0.18f);
                var edges = IsoGrid.EdgesOf(c);
                if (edges != Edge.None && _tile.Exits.Any(e => (edges & e.Edge) != 0) && _tile.Walkable[c])
                    s.Draw.DiamondOutline(b, p, 100, 50, new Color(120, 200, 255), 2);
            }
            foreach (var c in _tile.PlayerCombatStarts) s.Draw.DiamondFill(b, IsoGrid.CellCenter(c), 70, 35, new Color(70, 140, 255) * 0.85f);
            foreach (var c in _tile.EnemyCombatStarts) s.Draw.DiamondFill(b, IsoGrid.CellCenter(c), 70, 35, new Color(255, 80, 80) * 0.85f);
            s.Draw.DiamondOutline(b, IsoGrid.CellCenter(_tile.Spawn), 90, 45, Color.Yellow, 3);

            foreach (var pl in _tile.Placements)
            {
                var center = IsoGrid.CellCenter(pl.Cell);
                var rect = new Rectangle((int)center.X - 40, (int)center.Y - 60, 80, 56);
                string key = pl switch
                {
                    ItemPickupPlacement ip when s.Defs.TryGet<ItemDef>(ip.Item, out var it) => string.IsNullOrEmpty(it.WorldSprite) ? it.Icon : it.WorldSprite,
                    InteractablePlacement ip => ip.Sprite,
                    _ => "",
                };
                var tex = s.Textures.Get(key);
                string label = pl switch
                {
                    ItemPickupPlacement ip => s.Defs.TryGet<ItemDef>(ip.Item, out var it2) ? it2.DisplayName : "Item?",
                    InteractablePlacement ip => (ip.Invisible ? "(hidden) " : "") + ip.Label,
                    FigurePlacement f => f.Name,
                    _ => pl.Describe(s.Defs),
                };
                if (pl is MonsterGroupPlacement mg && mg.Patrol != PatrolMode.Stay && mg.PatrolPoints.Count > 0)
                {
                    var pts = new List<CellCoord> { mg.Cell };
                    pts.AddRange(mg.PatrolPoints);
                    if (mg.Patrol == PatrolMode.Loop) pts.Add(mg.Cell);
                    for (int i = 0; i + 1 < pts.Count; i++)
                        s.Draw.Line(b, IsoGrid.CellCenter(pts[i]), IsoGrid.CellCenter(pts[i + 1]), new Color(255, 120, 100) * 0.8f, 3);
                    foreach (var pt in mg.PatrolPoints) s.Draw.DiamondOutline(b, IsoGrid.CellCenter(pt), 60, 30, new Color(255, 120, 100), 2);
                }
                if (s.Textures.IsPlaceholder(tex)) s.Draw.Placeholder(b, rect, label);
                else
                {
                    float sc = Math.Min(1f, 120f / Math.Max(tex.Width, tex.Height));
                    var r = new Rectangle((int)(center.X - tex.Width * sc / 2), (int)(center.Y - tex.Height * sc + 10), (int)(tex.Width * sc), (int)(tex.Height * sc));
                    b.Draw(tex, r, Color.White);
                    if (_showLabels) s.Draw.TextCentered(b, label, new Vector2(center.X, r.Y - 10), Color.White, 16);
                }
                if (pl.VisibleWhen.Count > 0 && _showLabels) s.Draw.TextCentered(b, "conditional", new Vector2(center.X, center.Y + 14), new Color(255, 220, 120), 14);
                if (pl == _selected) s.Draw.DiamondOutline(b, center, IsoGrid.CellWidth, IsoGrid.CellHeight, new Color(255, 230, 80), 3);
            }

            if (_selectedCell is { } sc2) s.Draw.DiamondOutline(b, IsoGrid.CellCenter(sc2), IsoGrid.CellWidth, IsoGrid.CellHeight, Color.White, 2);
            if (_hoverCell is { } hc)
            {
                int r = _tool is Tool.Walkable or Tool.Blocked ? _brush - 1 : 0;
                for (int dq = -r; dq <= r; dq++)
                    for (int dr = -r; dr <= r; dr++)
                        s.Draw.DiamondOutline(b, IsoGrid.CellCenter(new CellCoord(hc.Q + dq, hc.R + dr)), IsoGrid.CellWidth, IsoGrid.CellHeight,
                            _pickCallback is not null ? Color.Cyan : Color.White * 0.9f, 2);
            }
            if (_pickCallback is not null) s.Draw.TextCentered(b, "PICK A CELL", new Vector2(960, 40), Color.Cyan, 32, bold: true);
        });
        _queue.Flush(batch);
    }
}
