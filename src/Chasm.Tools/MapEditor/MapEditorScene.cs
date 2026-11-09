using Chasm.Core.Content;
using Chasm.Core.Grid;
using Chasm.Core.Serialization;
using Chasm.Core.World;
using Chasm.Game.Rendering;
using Chasm.Game.Scenes;
using Chasm.Tools.Scenes;
using ImGuiNET;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using Num = System.Numerics;

namespace Chasm.Tools.MapEditor;

/// <summary>
/// The Map Editor: build tiles cell by cell.
///
/// Left toolbar: tile, floor, map position, floor-wide settings, tools, view.
/// Middle: the tile at true scale (trackpad: two-finger scroll pans, Ctrl+scroll / pinch zooms;
/// also Space+drag or middle-drag to pan).
/// Right panel: whatever the current tool or selection needs.
///
/// Works on copies of the tile and its floor; nothing is written until Save (Ctrl+S).
/// Every change can be undone (Ctrl+Z / Ctrl+Y).
/// </summary>
public sealed partial class MapEditorScene : Scene
{
    private enum Tool { Select, Walkable, PlayerStarts, EnemySpawns, PaintTexture, PlaceProp, Interaction }
    private enum Panel { Selection, FloorProperties, LowerClouds, UpperClouds, Reference, TileSettings }

    private const int LeftW = 310, RightW = 480, MenuH = 24;

    private readonly RenderQueue _queue = new();
    private readonly string? _openOnStart;
    private TileDef? _tile;
    private FloorDef? _floor;
    private bool _isNewTile;
    private string _savedTileJson = "", _savedFloorJson = "";
    private Tool _tool = Tool.Select;
    private Panel _panel = Panel.Selection;
    private string? _status;
    private float _statusTime;
    private EditorPrefs _prefs = new();
    private bool _showGrid = true, _showArt = true, _showLabels = true, _showNeighbours, _showMarkers = true, _showCloudLines;
    private CellCoord? _hoverCell;
    private Num.Vector2 _hoverPx;
    private Action? _pendingAfterSave;   // "unsaved changes" prompt continuation
    private bool _askUnsaved;

    public MapEditorScene(string? tileId = null) => _openOnStart = tileId;

    // ------------------------------------------------------------------ lifecycle

    public override void OnEnter()
    {
        ToolContext.EnsureInitialized(Services);
        _prefs = EditorPrefs.Load();
        _showGrid = _prefs.ShowGrid;
        Services.Canvas.Region = () =>
        {
            var pp = Services.Graphics.PresentationParameters;
            return new Rectangle(LeftW, MenuH, Math.Max(100, pp.BackBufferWidth - LeftW - RightW), Math.Max(100, pp.BackBufferHeight - MenuH));
        };
        Services.Canvas.ResetView();
        ToolContext.Wizards.Inspector.CellPicker = cb => { _cellPick = cb; Status("Click a cell on the map (Esc cancels)."); };
        if (_openOnStart is not null && Services.Defs.TryGet<TileDef>(_openOnStart, out var t)) Open(t);
        // Dev convenience for automated screenshots: CHASM_EDITOR=tool[:panel][:neighbours]
        if (_tile is not null && Environment.GetEnvironmentVariable("CHASM_EDITOR") is { } dev)
        {
            var parts = dev.Split(':');
            if (Enum.TryParse<Tool>(parts[0], true, out var tool)) SetTool(tool);
            if (parts.Length > 1 && Enum.TryParse<Panel>(parts[1], true, out var panel)) _panel = panel;
            if (parts.Contains("neighbours")) { _showNeighbours = true; Services.Canvas.Zoom = 0.45f; }
            if (parts.Contains("selectall")) foreach (var c in IsoGrid.AllCells.Where(c => _tile.Walkable[c])) _selection.Add(c);
        }
    }

    public override void OnExit()
    {
        Services.Canvas.Region = null;
        Services.Canvas.ResetView();
        ToolContext.Wizards.Inspector.CellPicker = null;
        if (_tile is not null && _history.Count > 0) WriteRecovery();
        DisposeNeighbours();
    }

    private Action<CellCoord>? _cellPick;

    // ------------------------------------------------------------------ open / new / save

    private void Open(TileDef saved)
    {
        _tile = ChasmJson.Clone(saved);
        _isNewTile = false;
        LoadFloorFor(_tile);
        ResetHistory();
        _tool = Tool.Select;
        _panel = Panel.Selection;
        ClearSelections();
        Status($"Opened {saved.Id}.");
    }

    private void NewTile()
    {
        var t = new TileDef { Id = "", Name = "New tile", Floor = null, BackgroundColor = "#ffffff" };
        foreach (var c in IsoGrid.AllCells) t.Walkable[c] = false;
        t.CommitWalkable();
        _tile = t;
        _isNewTile = true;
        _floor = null;
        ResetHistory();
        _savedTileJson = "";
        _tool = Tool.Walkable;
        _panel = Panel.TileSettings;
        ClearSelections();
        Status("New tile. Pick a floor (optional) and start painting.");
    }

    private void LoadFloorFor(TileDef t)
    {
        var f = t.Floor is int n ? Services.Defs.All<FloorDef>().FirstOrDefault(x => x.Number == n) : null;
        _floor = f is null ? null : ChasmJson.Clone(f);
        _savedFloorJson = _floor is null ? "" : ChasmJson.Serialize(_floor);
    }

    /// <summary>Run <paramref name="then"/> now, or after asking to save if there are unsaved changes.</summary>
    private void Guard(Action then)
    {
        if (!HasUnsaved) { then(); return; }
        _pendingAfterSave = then;
        _askUnsaved = true;
    }

    private bool HasUnsaved =>
        _tile is not null && (_isNewTile ? _history.Count > 1 : TileJson() != _savedTileJson) || FloorDirty;

    private bool FloorDirty => _floor is not null && ChasmJson.Serialize(_floor) != _savedFloorJson;

    private string TileJson() { _tile!.CommitWalkable(); return ChasmJson.Serialize(_tile); }

    private string ProposedId()
    {
        var t = _tile!;
        if (!_isNewTile) return t.Id;
        if (t.OnMap) return TileDef.IdFor(t.Floor!.Value, t.X!.Value, t.Y!.Value);
        return "sketch_" + Services.Defs.SuggestId(typeof(TileDef), string.IsNullOrWhiteSpace(t.Name) ? "tile" : t.Name);
    }

    private bool Save()
    {
        if (_tile is null) return false;
        string id = ProposedId();
        if (_isNewTile && Services.Defs.Exists(typeof(TileDef), id)) { Status($"A tile with id '{id}' already exists. Change the name or X/Y."); return false; }
        if (CoordinateClash(_tile.Floor, _tile.X, _tile.Y) is { } clash) { Status($"{clash.Id} already sits at those coordinates."); return false; }
        _tile.Id = id;
        _tile.CommitWalkable();
        if (_floor is not null && FloorDirty)
        {
            Services.DefReload.IgnoreNextChange(Services.Defs.PathFor(typeof(FloorDef), _floor.Id));
            Services.Defs.Save(ChasmJson.Clone(_floor));
            _savedFloorJson = ChasmJson.Serialize(_floor);
        }
        Services.DefReload.IgnoreNextChange(Services.Defs.PathFor(typeof(TileDef), id));
        Services.Defs.Save(ChasmJson.Clone(_tile));
        _savedTileJson = TileJson();
        _isNewTile = false;
        var problems = ContentValidator.Validate(_tile, Services.Defs);
        Status(problems.Count == 0 ? $"Saved {id}." : $"Saved {id} ({problems.Count} warning(s): {problems[0]})");
        return true;
    }

    private TileDef? CoordinateClash(int? floor, int? x, int? y)
    {
        if (floor is null || x is null || y is null || _tile is null) return null;
        string myId = _isNewTile ? "" : _tile.Id;
        return Services.Defs.All<TileDef>().FirstOrDefault(t => t.Id != myId && t.Floor == floor && t.X == x && t.Y == y);
    }

    private void Status(string text) { _status = text; _statusTime = 7; }

    // ------------------------------------------------------------------ undo / redo

    private sealed class Snapshot { public TileDef Tile { get; set; } = null!; public FloorDef? Floor { get; set; } public bool New { get; set; } }

    private readonly List<string> _history = new();
    private int _historyIndex = -1;
    private int _editVersion, _snapshotVersion;

    /// <summary>Call after any change to the tile or floor.</summary>
    private void Edited()
    {
        _editVersion++;
        if (_tile is not null) Services.TileVisuals.Invalidate(_tile);
    }

    private string SnapshotJson()
    {
        _tile!.CommitWalkable();
        return ChasmJson.Serialize(new Snapshot { Tile = _tile, Floor = _floor, New = _isNewTile });
    }

    private void ResetHistory()
    {
        _history.Clear();
        _history.Add(SnapshotJson());
        _historyIndex = 0;
        _snapshotVersion = _editVersion;
        if (!_isNewTile) _savedTileJson = TileJson();
    }

    /// <summary>Records an undo step once the user has finished an action (mouse released, no text field active).</summary>
    private void MaybeCommitSnapshot()
    {
        if (_tile is null || _editVersion == _snapshotVersion) return;
        var input = Services.Input;
        if (input.RawMouse.LeftButton == ButtonState.Pressed || input.RawMouse.RightButton == ButtonState.Pressed || ImGui.IsAnyItemActive()) return;
        string json = SnapshotJson();
        _snapshotVersion = _editVersion;
        if (_historyIndex >= 0 && _history[_historyIndex] == json) return;
        _history.RemoveRange(_historyIndex + 1, _history.Count - _historyIndex - 1);
        _history.Add(json);
        if (_history.Count > 300) _history.RemoveAt(0);
        _historyIndex = _history.Count - 1;
        _recoveryDue = true;
    }

    // ------------------------------------------------------------------ recovery (crash / close safety net)

    private bool _recoveryDue;
    private float _recoveryTimer;

    private string RecoveryKey => _isNewTile ? "new_" + (string.IsNullOrWhiteSpace(_tile!.Name) ? "tile" : _tile.Name) : _tile!.Id;

    private void UpdateRecovery(float dt)
    {
        _recoveryTimer -= dt;
        if (!_recoveryDue || _recoveryTimer > 0 || _tile is null) return;
        WriteRecovery();
    }

    private void WriteRecovery()
    {
        if (_tile is null || _history.Count == 0) return;
        _recoveryDue = false;
        _recoveryTimer = 2f;
        string title = $"{(string.IsNullOrWhiteSpace(_tile.Name) ? "Untitled" : _tile.Name)}  [{(_isNewTile ? "new" : _tile.Id)}]";
        EditorRecovery.Write(RecoveryKey, title, _history[_historyIndex]);
    }

    private void Recover(EditorRecovery.Entry entry)
    {
        var json = EditorRecovery.Read(entry);
        var snap = json is null ? null : ChasmJson.Deserialize<Snapshot>(json);
        if (snap is null) { Status("That recovery file couldn't be read."); return; }
        _tile = snap.Tile;
        _tile.InvalidateCaches();
        _floor = snap.Floor;
        _isNewTile = snap.New || !Services.Defs.Exists(typeof(TileDef), _tile.Id);
        ResetHistory();
        // Compare against what's saved on disk, so unsaved recovered work shows as unsaved.
        _savedTileJson = !_isNewTile && Services.Defs.TryGet<TileDef>(_tile.Id, out var saved) ? ChasmJson.Serialize(saved) : "";
        var savedFloor = _floor is null ? null : Services.Defs.All<FloorDef>().FirstOrDefault(f => f.Number == _floor.Number);
        _savedFloorJson = savedFloor is null ? "" : ChasmJson.Serialize(savedFloor);
        if (_isNewTile) _history.Insert(0, _history[0]); // counts as unsaved
        _historyIndex = _history.Count - 1;
        _tool = Tool.Select;
        _panel = Panel.Selection;
        ClearSelections();
        Services.TileVisuals.Invalidate(_tile);
        Status($"Recovered work from {EditorRecovery.Ago(entry.SavedLocal)}. Save (Ctrl+S) to keep it.");
    }

    private void Undo() => Restore(_historyIndex - 1);
    private void Redo() => Restore(_historyIndex + 1);

    private void Restore(int index)
    {
        if (index < 0 || index >= _history.Count) return;
        _historyIndex = index;
        var snap = ChasmJson.Deserialize<Snapshot>(_history[index])!;
        _tile = snap.Tile;
        _floor = snap.Floor;
        _isNewTile = snap.New;
        _tile.InvalidateCaches();
        _snapshotVersion = ++_editVersion;
        Services.TileVisuals.Invalidate(_tile);
        _selectedRegion = _tile.PaintRegions.FirstOrDefault(r => r.Id == _selectedRegion?.Id);
        _selectedProp = _tile.Props.FirstOrDefault(p => p.Id == _selectedProp?.Id);
        _selectedPlacement = _tile.Placements.FirstOrDefault(p => p.Id == _selectedPlacement?.Id);
        Status(index < _history.Count - 1 ? "Undone." : "Redone.");
    }

    // ------------------------------------------------------------------ update

    public override void Update(float dt)
    {
        _statusTime -= dt;
        ToolContext.MenuBar(Services, _tile is null ? "Map Editor" : $"Map Editor - {(_isNewTile ? "(new)" : _tile.Id)}{(HasUnsaved ? " *" : "")}", Menus);

        if (_tile is null) { StartDialog(); ToolContext.Wizards.Draw(); return; }

        LeftToolbar();
        RightPanelGui();
        ToolContext.Wizards.Draw();
        Popups();

        var input = Services.Input;
        bool ctrl = input.KeyHeld(Keys.LeftControl) || input.KeyHeld(Keys.RightControl);
        if (ctrl && input.KeyPressed(Keys.S)) Save();
        if (ctrl && input.KeyPressed(Keys.Z)) Undo();
        if (ctrl && (input.KeyPressed(Keys.Y) || (input.KeyHeld(Keys.LeftShift) && input.KeyPressed(Keys.Z)))) Redo();

        UpdateView(input, ctrl);
        UpdateHover(input);
        if (_cellPick is not null) UpdateCellPick(input);
        else if (!_panning) UpdateTool(input);

        MaybeCommitSnapshot();
        UpdateRecovery(dt);
    }

    private void UpdateHover(Game.Input.InputState input)
    {
        _hoverPx = input.MouseCanvas.ToNumerics();
        bool overCanvas = input.MouseInCanvas && !input.MouseCapturedByUi && Services.Canvas.Area.Contains(input.MouseWindow);
        var c = IsoGrid.CellAt(_hoverPx);
        _hoverCell = overCanvas && IsoGrid.InTile(c) ? c : null;
    }

    private void UpdateCellPick(Game.Input.InputState input)
    {
        if (input.Pressed(Game.Input.InputAction.Cancel)) { _cellPick = null; Status("Pick cancelled."); return; }
        if (_hoverCell is { } c && input.LeftClicked) { _cellPick!(c); _cellPick = null; Edited(); Status($"Picked {c}."); }
    }

    // ------------------------------------------------------------------ view: zoom + pan (trackpad friendly)

    private bool _panning;

    private void UpdateView(Game.Input.InputState input, bool ctrl)
    {
        var canvas = Services.Canvas;
        bool overCanvas = canvas.Area.Contains(input.MouseWindow) && !input.MouseCapturedByUi;
        if (overCanvas)
        {
            int wheel = input.ScrollDelta, hwheel = input.HorizontalScrollDelta;
            if (ctrl && wheel != 0) canvas.ZoomAround(input.MouseWindow, canvas.Zoom * MathF.Pow(1.0015f, wheel));
            else if (wheel != 0 || hwheel != 0) canvas.Pan += new Vector2(-hwheel * 0.5f, wheel * 0.5f);
        }
        bool spaceDrag = input.KeyHeld(Keys.Space) && input.LeftHeld;
        _panning = (spaceDrag || input.MiddleHeld) && (overCanvas || _panning);
        if (_panning) canvas.Pan += input.MouseDelta.ToVector2();
        if (!input.KeyboardCapturedByUi)
        {
            if (input.KeyPressed(Keys.OemPlus) || input.KeyPressed(Keys.Add)) canvas.ZoomAround(canvas.Area.Center, canvas.Zoom * 1.25f);
            if (input.KeyPressed(Keys.OemMinus) || input.KeyPressed(Keys.Subtract)) canvas.ZoomAround(canvas.Area.Center, canvas.Zoom / 1.25f);
            if (input.KeyPressed(Keys.F)) canvas.ResetView();
        }
    }

    // ------------------------------------------------------------------ start dialog + menus

    private string _openSearch = "";

    private void StartDialog()
    {
        var io = ImGui.GetIO();
        ImGui.SetNextWindowPos(io.DisplaySize / 2, ImGuiCond.Always, new Num.Vector2(0.5f, 0.5f));
        ImGui.SetNextWindowSize(new Num.Vector2(560, 600));
        ImGui.Begin("Map Editor##start", ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoMove | ImGuiWindowFlags.NoCollapse);
        if (ImGui.Button("New tile", new Num.Vector2(-1, 50))) NewTile();
        ImGui.Spacing();
        ImGui.SeparatorText("Or open an existing tile");
        ImGui.SetNextItemWidth(-1);
        ImGui.InputTextWithHint("##search", "search...", ref _openSearch, 80);
        ImGui.BeginChild("list");
        TileList(t => Open(t), _openSearch);
        ImGui.EndChild();
        ImGui.End();
    }

    /// <summary>All tiles grouped by floor (sketches last).</summary>
    private void TileList(Action<TileDef> chosen, string search, string? exclude = null)
    {
        foreach (var group in Services.Defs.All<TileDef>().GroupBy(t => t.Floor).OrderBy(g => g.Key is null ? int.MaxValue : g.Key))
        {
            ImGui.TextDisabled(group.Key is int f ? $"Floor {f}" : "Sketches (no floor)");
            foreach (var t in group.OrderBy(t => t.Y).ThenBy(t => t.X))
            {
                if (t.Id == exclude) continue;
                if (search.Length > 0 && !t.Id.Contains(search, StringComparison.OrdinalIgnoreCase) && !t.DisplayName.Contains(search, StringComparison.OrdinalIgnoreCase)) continue;
                string pos = t.X is int x && t.Y is int y ? $"({x},{y})" : "";
                if (ImGui.Selectable($"   {t.DisplayName}  {pos}##{t.Id}")) chosen(t);
                if (ImGui.IsItemHovered()) ImGui.SetTooltip(t.Id);
            }
        }
    }

    private void Menus()
    {
        if (!ImGui.BeginMenu("Tile")) return;
        if (ImGui.MenuItem("New tile")) Guard(NewTile);
        if (ImGui.MenuItem("Open...")) Guard(() => { _tile = null; });
        if (ImGui.MenuItem("Save", "Ctrl+S", false, _tile is not null)) Save();
        if (ImGui.MenuItem("Undo", "Ctrl+Z", false, _historyIndex > 0)) Undo();
        if (ImGui.MenuItem("Redo", "Ctrl+Y", false, _historyIndex < _history.Count - 1)) Redo();
        if (ImGui.MenuItem("Revert to saved", null, false, _tile is not null && !_isNewTile && HasUnsaved))
            Open(Services.Defs.Get<TileDef>(_tile!.Id));
        ImGui.Separator();
        if (ImGui.MenuItem("Play this tile (World Sandbox)", null, false, _tile is not null && !_isNewTile))
        {
            if (HasUnsaved) Save();
            Services.Scenes.ResetTo(new WorldSandboxScene(_tile!.Id, _selectedCell));
        }
        ImGui.EndMenu();
    }

    private void Popups()
    {
        NewFloorPopup();
        if (_askUnsaved) { ImGui.OpenPopup("Unsaved changes"); _askUnsaved = false; }
        if (!ImGui.BeginPopupModal("Unsaved changes", ImGuiWindowFlags.AlwaysAutoResize)) return;
        ImGui.Text("This tile (or its floor) has unsaved changes.");
        if (ImGui.Button("Save", new Num.Vector2(110, 0))) { if (Save()) { _pendingAfterSave?.Invoke(); _pendingAfterSave = null; } ImGui.CloseCurrentPopup(); }
        ImGui.SameLine();
        if (ImGui.Button("Discard", new Num.Vector2(110, 0))) { _pendingAfterSave?.Invoke(); _pendingAfterSave = null; ImGui.CloseCurrentPopup(); }
        ImGui.SameLine();
        if (ImGui.Button("Cancel", new Num.Vector2(110, 0))) { _pendingAfterSave = null; ImGui.CloseCurrentPopup(); }
        ImGui.EndPopup();
    }
}
