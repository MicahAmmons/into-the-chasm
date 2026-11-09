using Chasm.Core.Grid;
using Chasm.Core.Serialization;
using Chasm.Core.World;
using ImGuiNET;
using Num = System.Numerics;

namespace Chasm.Tools.MapEditor;

public sealed partial class MapEditorScene
{
    private string? _xyError;
    private float _xyErrorTime;

    private void LeftToolbar()
    {
        var io = ImGui.GetIO();
        var t = _tile!;
        ImGui.SetNextWindowPos(new Num.Vector2(0, MenuH));
        ImGui.SetNextWindowSize(new Num.Vector2(LeftW, io.DisplaySize.Y - MenuH));
        ImGui.Begin("##left", ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoMove);

        // ---- tile
        ImGui.SeparatorText("Tile");
        string name = t.Name;
        ImGui.SetNextItemWidth(-1);
        if (ImGui.InputTextWithHint("##name", "tile name", ref name, 120)) { t.Name = name; Edited(); }
        ImGui.TextDisabled(_isNewTile ? $"id on save: {ProposedId()}" : $"id: {t.Id}");
        if (ImGui.Button("Save", new Num.Vector2(90, 0))) Save();
        ImGui.SameLine();
        if (ImGui.Button("Undo", new Num.Vector2(60, 0))) Undo();
        ImGui.SameLine();
        if (ImGui.Button("Redo", new Num.Vector2(60, 0))) Redo();
        ImGui.SameLine();
        if (ImGui.Button("Open", new Num.Vector2(-1, 0))) Guard(() => _tile = null);
        PanelButton("Tile settings (art layers, exits, notes)", Panel.TileSettings);

        // ---- floor
        ImGui.SeparatorText("Floor");
        FloorPicker();
        bool hasFloor = _floor is not null;
        if (hasFloor) MapPosition();

        if (!hasFloor) ImGui.BeginDisabled();
        if (ImGui.Button("Tiles on this floor", new Num.Vector2(-1, 0))) ImGui.OpenPopup("floormap");
        FloorMapPopup();
        PanelButton("Floor properties", Panel.FloorProperties);
        if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
            ImGui.SetTooltip("Settings and the texture library shared by the whole floor.\n\nWARNING: changes here affect EVERY tile on this floor.");
        PanelButton("Lower clouds", Panel.LowerClouds, half: true);
        ImGui.SameLine();
        PanelButton("Upper clouds", Panel.UpperClouds, half: true);
        if (!hasFloor) ImGui.EndDisabled();
        if (!hasFloor) ImGui.TextDisabled("Pick a floor to unlock these.");

        // ---- tools
        ImGui.SeparatorText("Tools  (Esc = stop)");
        ToolButton(Tool.Select, "Select / move", "Click things on the map to select them; drag to move props and objects. Delete removes.");
        ToolButton(Tool.Walkable, "Walkable cells", "Click or drag: the first cell you press decides (green -> red, red -> green). Right-drag always makes cells red.");
        ToolButton(Tool.PlayerStarts, "Player start cells", "Cells where the player may choose to start a fight. Only walkable cells.");
        ToolButton(Tool.EnemySpawns, "Enemy spawn cells", "Cells where enemies are placed at random when a fight starts. Only walkable cells.");
        ToolButton(Tool.PaintTexture, "Paint texture", "Select cells (click / drag), then paint them with a material from the right panel.");
        ToolButton(Tool.PlaceProp, "Place art (props)", "Pick a PNG on the right, then click to place it anywhere. Drag to move.");
        ToolButton(Tool.Interaction, "Add interaction", "Click a cell, choose a trigger (proximity lets you drag the radius), then fill in what happens.");
        if (PanelButton(_reference is null ? "Reference tile..." : $"Reference: {_reference.DisplayName}", Panel.Reference) && _reference is null)
            ImGui.OpenPopup("pickref");
        ReferencePickerPopup();

        // ---- view
        ImGui.SeparatorText("View");
        ImGui.Checkbox("Art", ref _showArt); ImGui.SameLine();
        if (ImGui.Checkbox("Grid", ref _showGrid)) { _prefs.ShowGrid = _showGrid; _prefs.Save(); }
        ImGui.SameLine();
        if (ColorSwatch("##gridcol", _prefs.GridColor, "Grid line colour")) _prefs.Save();
        ImGui.SameLine();
        bool centre = _prefs.ShowCenterLines;
        if (ImGui.Checkbox("Centre lines", ref centre)) { _prefs.ShowCenterLines = centre; _prefs.Save(); }
        ImGui.SameLine();
        if (ColorSwatch("##centrecol", _prefs.CenterLineColor, "Centre line colour")) _prefs.Save();
        ImGui.Checkbox("Labels", ref _showLabels); ImGui.SameLine();
        ImGui.Checkbox("Markers", ref _showMarkers); ImGui.SameLine();
        ImGui.Checkbox("Cloud lines", ref _showCloudLines);
        if (ImGui.Checkbox("Neighbouring tiles", ref _showNeighbours) && _showNeighbours && Services.Canvas.Zoom > 0.8f)
            Services.Canvas.ZoomAround(Services.Canvas.Area.Center, 0.62f);
        if (ImGui.IsItemHovered()) ImGui.SetTooltip("Shows the tiles around this one (by X/Y) outside the edges. Zoom out to see them.");
        if (ImGui.Button("-", new Num.Vector2(36, 0))) Services.Canvas.ZoomAround(Services.Canvas.Area.Center, Services.Canvas.Zoom / 1.25f);
        ImGui.SameLine();
        if (ImGui.Button("+", new Num.Vector2(36, 0))) Services.Canvas.ZoomAround(Services.Canvas.Area.Center, Services.Canvas.Zoom * 1.25f);
        ImGui.SameLine();
        if (ImGui.Button("Fit (F)", new Num.Vector2(-1, 0))) Services.Canvas.ResetView();
        ImGui.TextDisabled("Trackpad: 2-finger scroll pans,\nCtrl+scroll / pinch zooms. Space+drag pans.");

        // ---- info
        ImGui.SeparatorText("Info");
        if (_hoverCell is { } h) ImGui.Text($"Cell {h}  px ({_hoverPx.X:0},{_hoverPx.Y:0})");
        else ImGui.TextDisabled("Hover the map");
        if (_statusTime > 0 && _status is not null) ImGui.TextWrapped(_status);
        var problems = Chasm.Core.Content.ContentValidator.Validate(t, Services.Defs).Where(p => !p.Contains("Id is empty")).ToList();
        foreach (var p in problems.Take(4)) ImGui.TextColored(new Num.Vector4(1, 0.6f, 0.4f, 1), p);
        ImGui.End();
    }

    /// <summary>A button that opens a right-panel page; clicking it again closes the page. Returns true when it opened.</summary>
    private bool PanelButton(string label, Panel panel, bool half = false)
    {
        bool active = _panel == panel;
        if (active) ImGui.PushStyleColor(ImGuiCol.Button, new Num.Vector4(0.35f, 0.42f, 0.7f, 1));
        bool clicked = ImGui.Button(label, new Num.Vector2(half ? (LeftW - 28) / 2f : -1, 0));
        if (active) ImGui.PopStyleColor();
        if (!clicked) return false;
        _panel = active ? Panel.Selection : panel;
        return !active;
    }

    private void ToolButton(Tool tool, string label, string tip)
    {
        bool active = _tool == tool;
        if (active) ImGui.PushStyleColor(ImGuiCol.Button, new Num.Vector4(0.85f, 0.6f, 0.2f, 1));
        if (ImGui.Button(label, new Num.Vector2(-1, 0)))
        {
            SetTool(active ? Tool.Select : tool); // clicking the active tool again turns it off
        }
        if (active) ImGui.PopStyleColor();
        if (ImGui.IsItemHovered()) ImGui.SetTooltip(tip);
    }

    private void SetTool(Tool tool)
    {
        _tool = tool;
        _dragMode = null;
        _radiusCenter = null;
        if (tool is Tool.Select or Tool.Walkable or Tool.PlayerStarts or Tool.EnemySpawns or Tool.PaintTexture or Tool.PlaceProp or Tool.Interaction)
            _panel = Panel.Selection;
    }

    private void FloorPicker()
    {
        var t = _tile!;
        var floors = Services.Defs.All<FloorDef>().OrderBy(f => f.Number).ToList();
        string preview = _floor is null ? "(no floor - sketch)" : $"Floor {_floor.Number}: {_floor.DisplayName}";
        ImGui.SetNextItemWidth(-1);
        if (!ImGui.BeginCombo("##floor", preview)) return;
        if (ImGui.Selectable("(no floor - sketch)", _floor is null)) Guard(() => SetFloor(null));
        foreach (var f in floors)
            if (ImGui.Selectable($"Floor {f.Number}: {f.DisplayName}", _floor?.Number == f.Number) && _floor?.Number != f.Number)
            {
                var chosen = f;
                if (FloorDirty) Guard(() => SetFloor(chosen)); else SetFloor(chosen);
            }
        if (ImGui.Selectable("+ New floor..."))
        {
            _newFloorNumber = floors.Count == 0 ? 1 : floors.Max(f => f.Number) + 1;
            _newFloorName = "";
            _openNewFloor = true;
        }
        ImGui.EndCombo();
    }

    private bool _openNewFloor;
    private int _newFloorNumber = 1;
    private string _newFloorName = "";

    /// <summary>Asks which floor number (and a name) for a new floor.</summary>
    private void NewFloorPopup()
    {
        if (_openNewFloor) { ImGui.OpenPopup("New floor"); _openNewFloor = false; }
        CenterNextWindow();
        if (!ImGui.BeginPopupModal("New floor", ImGuiWindowFlags.AlwaysAutoResize)) return;
        ImGui.Text("Floor number:");
        ImGui.SetNextItemWidth(160);
        ImGui.InputInt("##num", ref _newFloorNumber);
        ImGui.Text("Name (optional):");
        ImGui.SetNextItemWidth(300);
        ImGui.InputTextWithHint("##fname", $"Floor {_newFloorNumber}", ref _newFloorName, 80);
        var taken = Services.Defs.All<FloorDef>().FirstOrDefault(f => f.Number == _newFloorNumber);
        if (taken is not null) ImGui.TextColored(new Num.Vector4(1, 0.45f, 0.4f, 1), $"Floor {_newFloorNumber} already exists ({taken.DisplayName}).");
        if (_newFloorNumber < 0) ImGui.TextColored(new Num.Vector4(1, 0.45f, 0.4f, 1), "Floor numbers can't be negative.");
        ImGui.BeginDisabled(taken is not null || _newFloorNumber < 0);
        if (ImGui.Button("Create", new Num.Vector2(140, 0)))
        {
            int n = _newFloorNumber;
            var nf = new FloorDef { Id = $"floor_{n}", Number = n, Name = string.IsNullOrWhiteSpace(_newFloorName) ? $"Floor {n}" : _newFloorName.Trim() };
            Services.Defs.Save(nf);
            Guard(() => SetFloor(nf));
            Status($"Created floor {n}.");
            ImGui.CloseCurrentPopup();
        }
        ImGui.EndDisabled();
        ImGui.SameLine();
        if (ImGui.Button("Cancel", new Num.Vector2(140, 0))) ImGui.CloseCurrentPopup();
        ImGui.EndPopup();
    }

    private static bool ColorSwatch(string id, float[] rgba, string tooltip)
    {
        var v = new Num.Vector4(rgba[0], rgba[1], rgba[2], rgba[3]);
        bool changed = ImGui.ColorEdit4(id, ref v, ImGuiColorEditFlags.NoInputs | ImGuiColorEditFlags.AlphaBar | ImGuiColorEditFlags.AlphaPreviewHalf);
        if (ImGui.IsItemHovered()) ImGui.SetTooltip(tooltip + " (click to change; includes transparency)");
        if (changed) { rgba[0] = v.X; rgba[1] = v.Y; rgba[2] = v.Z; rgba[3] = v.W; }
        return changed;
    }

    /// <summary>Next popup/window appears centred on screen (so it never opens off the edge).</summary>
    private static void CenterNextWindow() =>
        ImGui.SetNextWindowPos(ImGui.GetIO().DisplaySize / 2, ImGuiCond.Appearing, new Num.Vector2(0.5f, 0.5f));

    private void SetFloor(FloorDef? f)
    {
        var t = _tile!;
        t.Floor = f?.Number;
        if (f is null) { t.X = null; t.Y = null; }
        else if (CoordinateClash(t.Floor, t.X, t.Y) is not null) { t.X = null; t.Y = null; }
        _floor = f is null ? null : ChasmJson.Clone(f);
        _savedFloorJson = _floor is null ? "" : ChasmJson.Serialize(_floor);
        Edited();
    }

    private void MapPosition()
    {
        var t = _tile!;
        bool onMap = t.X is not null;
        if (ImGui.Checkbox("On the floor map at X / Y", ref onMap))
        {
            if (onMap)
            {
                // First free spot near 0,0.
                for (int r = 0; r < 50 && t.X is null; r++)
                    for (int y = -r; y <= r && t.X is null; y++)
                        for (int x = -r; x <= r && t.X is null; x++)
                            if (CoordinateClash(t.Floor, x, y) is null) { t.X = x; t.Y = y; }
            }
            else { t.X = null; t.Y = null; }
            Edited();
        }
        if (ImGui.IsItemHovered()) ImGui.SetTooltip("Tiles with X/Y connect to their neighbours automatically (east = X+1, south = Y+1).");
        if (t.X is int x0 && t.Y is int y0)
        {
            int x = x0, y = y0;
            ImGui.SetNextItemWidth(130); bool cx = ImGui.InputInt("X", ref x);
            ImGui.SameLine();
            ImGui.SetNextItemWidth(130); bool cy = ImGui.InputInt("Y", ref y);
            if (cx || cy)
            {
                if (CoordinateClash(t.Floor, x, y) is { } clash) { _xyError = $"({x},{y}) is taken by {clash.DisplayName} [{clash.Id}]."; _xyErrorTime = 4; }
                else { t.X = x; t.Y = y; Edited(); }
            }
            if (!_isNewTile && t.Id != TileDef.IdFor(t.Floor!.Value, x0, y0))
                ImGui.TextDisabled($"(id stays '{t.Id}')");
        }
        _xyErrorTime -= ImGui.GetIO().DeltaTime;
        if (_xyErrorTime > 0 && _xyError is not null) ImGui.TextColored(new Num.Vector4(1, 0.45f, 0.4f, 1), _xyError);
    }

    private void FloorMapPopup()
    {
        if (!ImGui.BeginPopup("floormap")) return;
        var tiles = Services.Defs.All<TileDef>().Where(x => x.Floor == _floor?.Number).ToList();
        var placed = tiles.Where(x => x.X is not null && x.Y is not null).ToList();
        ImGui.Text($"Floor {_floor?.Number}: {placed.Count} tile(s) on the map, {tiles.Count - placed.Count} off-map");
        if (placed.Count > 0)
        {
            int minX = placed.Min(p => p.X!.Value), maxX = placed.Max(p => p.X!.Value);
            int minY = placed.Min(p => p.Y!.Value), maxY = placed.Max(p => p.Y!.Value);
            const float cell = 58;
            for (int y = minY; y <= maxY; y++)
            {
                for (int x = minX; x <= maxX; x++)
                {
                    if (x > minX) ImGui.SameLine();
                    var tile = placed.FirstOrDefault(p => p.X == x && p.Y == y);
                    bool current = _tile!.Id == tile?.Id || (_tile.X == x && _tile.Y == y && _tile.Floor == _floor?.Number);
                    if (current) ImGui.PushStyleColor(ImGuiCol.Button, new Num.Vector4(0.85f, 0.6f, 0.2f, 1));
                    if (tile is null) ImGui.BeginDisabled();
                    if (ImGui.Button(tile is null ? $"##{x}_{y}" : $"{x},{y}##{tile.Id}", new Num.Vector2(cell, cell * 0.6f)) && tile is not null && tile.Id != _tile.Id)
                    {
                        var chosen = tile;
                        ImGui.CloseCurrentPopup();
                        Guard(() => Open(chosen));
                    }
                    if (tile is null) ImGui.EndDisabled();
                    else if (ImGui.IsItemHovered()) ImGui.SetTooltip($"{tile.DisplayName}\n{tile.Id}");
                    if (current) ImGui.PopStyleColor();
                }
            }
        }
        var off = tiles.Where(x => x.X is null).ToList();
        if (off.Count > 0)
        {
            ImGui.SeparatorText("Not on the map");
            foreach (var o in off)
                if (ImGui.Selectable($"{o.DisplayName}##{o.Id}")) { var chosen = o; ImGui.CloseCurrentPopup(); Guard(() => Open(chosen)); }
        }
        ImGui.EndPopup();
    }
}
