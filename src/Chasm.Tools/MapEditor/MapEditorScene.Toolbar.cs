using Chasm.Core.Grid;
using Chasm.Core.Serialization;
using Chasm.Core.World;
using Chasm.Tools.ImGuiHost;
using ImGuiNET;
using Num = System.Numerics;

namespace Chasm.Tools.MapEditor;

public sealed partial class MapEditorScene
{
    private string? _xyError;
    private float _xyErrorTime;

    private static readonly (Tool tool, string label, string key, string help)[] ToolList =
    {
        (Tool.Select, "Select", "1", "Click a cell, prop or object to select it. Drag props and objects to move them. Delete removes the selection."),
        (Tool.Walkable, "Walkable", "2", "Click or drag cells. The first cell you press decides: green turns red, red turns green. Right-drag always blocks."),
        (Tool.PlayerStarts, "Player starts", "3", "Blue cells where the player may choose to start a fight. Only walkable cells. Right-drag removes."),
        (Tool.EnemySpawns, "Enemy spawns", "4", "Orange cells where enemies are placed at random when a fight starts. Only walkable cells."),
        (Tool.PaintTexture, "Paint texture", "5", "Select cells (click / drag), then press Paint next to a material on the right. Each paint becomes a region you can reorder."),
        (Tool.PlaceProp, "Place art", "6", "Pick a PNG on the right (or Import one), then click the map to place it. Drag to move."),
        (Tool.Interaction, "Interaction", "7", "Click a cell and choose a trigger. Proximity lets you drag the radius. Then fill in what happens on the right."),
    };

    private void LeftToolbar()
    {
        var io = ImGui.GetIO();
        var t = _tile!;
        float full = LeftW - 24, half = (full - 8) / 2;
        ImGui.SetNextWindowPos(new Num.Vector2(0, MenuH));
        ImGui.SetNextWindowSize(new Num.Vector2(LeftW, io.DisplaySize.Y - MenuH - StatusH));
        ImGui.Begin("##left", ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoMove);

        // ---- tile card
        Ui.BeginCard("tilecard");
        string name = t.Name;
        ImGui.SetNextItemWidth(-1);
        if (Ui.TitleFontInput("##name", "tile name", ref name)) { t.Name = name; Edited(); }
        ImGui.TextColored(Ui.Muted, _isNewTile ? $"new  ->  {ProposedId()}" : t.Id);
        ImGui.SameLine();
        if (_tile.Floor is null) Ui.Badge("sketch", Ui.Blue);
        else Ui.Badge($"floor {_tile.Floor}", Ui.Blue);
        ImGui.SameLine();
        if (HasUnsaved) Ui.Badge("unsaved", Ui.Warn); else Ui.Badge("saved", Ui.Good);
        if (Ui.AccentButton("Save  (Ctrl+S)", new Num.Vector2(full - 20, 0))) Save();
        ImGui.BeginDisabled(_historyIndex <= 0);
        if (ImGui.Button("Undo", new Num.Vector2((full - 36) / 3, 0))) Undo();
        ImGui.EndDisabled();
        ImGui.SameLine();
        ImGui.BeginDisabled(_historyIndex >= _history.Count - 1);
        if (ImGui.Button("Redo", new Num.Vector2((full - 36) / 3, 0))) Redo();
        ImGui.EndDisabled();
        ImGui.SameLine();
        if (ImGui.Button("Open...", new Num.Vector2((full - 36) / 3, 0))) Guard(() => _tile = null);
        PanelButton("Tile settings", Panel.TileSettings, full - 20, "Hand-painted full-screen art layers, extra exits, local materials and notes.");
        Ui.EndCard();

        // ---- floor
        Ui.Section("Floor");
        FloorPicker();
        bool hasFloor = _floor is not null;
        if (hasFloor) MapPosition();
        else Ui.Hint("Sketch tile: pick a floor to place it on the map and use the floor's texture library.");
        ImGui.BeginDisabled(!hasFloor);
        if (ImGui.Button("Floor map", new Num.Vector2(half, 0))) ImGui.OpenPopup("floormap");
        if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled)) ImGui.SetTooltip("Every tile on this floor by X / Y. Click one to open it.");
        FloorMapPopup();
        ImGui.SameLine();
        PanelButton("Properties", Panel.FloorProperties, half,
            "The floor's texture library and floor-wide settings.\n\nWARNING: changes here affect EVERY tile on this floor.");
        PanelButton("Lower clouds", Panel.LowerClouds, half, "The cloud layer cliffs fade down into (floor-wide).");
        ImGui.SameLine();
        PanelButton("Upper clouds", Panel.UpperClouds, half, "The cloud layer walls fade up into (floor-wide).");
        ImGui.EndDisabled();

        // ---- tools
        Ui.Section("Tools");
        for (int i = 0; i < ToolList.Length; i++)
        {
            var (tool, label, key, help) = ToolList[i];
            if (i % 2 == 1) ImGui.SameLine();
            if (Ui.ToggleButton($"{key}  {label}", _tool == tool, new Num.Vector2(half, 30), help))
                SetTool(_tool == tool && tool != Tool.Select ? Tool.Select : tool);
        }
        ImGui.SameLine();
        if (Ui.ToggleButton(_reference is null ? "8  Reference" : "8  Ref: " + _reference.DisplayName, _panel == Panel.Reference, new Num.Vector2(half, 30),
                "Show another tile on top of this one (read-only) and copy things from it."))
            ToggleReference();
        ReferencePickerPopup();

        // Hint card for the active tool.
        Ui.BeginCard("hint", 0, new Num.Vector4(0.16f, 0.13f, 0.08f, 1f));
        var active = ToolList.First(x => x.tool == _tool);
        ImGui.TextColored(Ui.Accent, active.label);
        ImGui.SameLine();
        ImGui.TextColored(Ui.Muted, "(Esc to stop)");
        Ui.Hint(active.help);
        Ui.EndCard();

        // ---- view
        Ui.Section("View");
        if (ImGui.BeginTable("view", 2))
        {
            ImGui.TableNextColumn(); ImGui.Checkbox("Art", ref _showArt);
            ImGui.TableNextColumn(); ImGui.Checkbox("Labels", ref _showLabels);
            ImGui.TableNextColumn();
            if (ImGui.Checkbox("Grid", ref _showGrid)) { _prefs.ShowGrid = _showGrid; _prefs.Save(); }
            ImGui.SameLine(); if (ColorSwatch("##gridcol", _prefs.GridColor, "Grid line colour")) _prefs.Save();
            ImGui.TableNextColumn();
            bool centre = _prefs.ShowCenterLines;
            if (ImGui.Checkbox("Centre", ref centre)) { _prefs.ShowCenterLines = centre; _prefs.Save(); }
            if (ImGui.IsItemHovered()) ImGui.SetTooltip("Lines through the middle of the screen.");
            ImGui.SameLine(); if (ColorSwatch("##centrecol", _prefs.CenterLineColor, "Centre line colour")) _prefs.Save();
            ImGui.TableNextColumn(); ImGui.Checkbox("Markers", ref _showMarkers);
            if (ImGui.IsItemHovered()) ImGui.SetTooltip("Player starts, enemy spawns, spawn point, exits.");
            ImGui.TableNextColumn(); ImGui.Checkbox("Cloud lines", ref _showCloudLines);
            ImGui.TableNextColumn();
            if (ImGui.Checkbox("Neighbours", ref _showNeighbours) && _showNeighbours && Services.Canvas.Zoom > 0.8f)
                Services.Canvas.ZoomAround(Services.Canvas.Area.Center, 0.62f);
            if (ImGui.IsItemHovered()) ImGui.SetTooltip("Draw the tiles around this one (by X/Y) outside its edges. Zoom out to see them.");
            ImGui.EndTable();
        }
        float third = (full - 16) / 3;
        if (ImGui.Button("-", new Num.Vector2(third, 0))) Services.Canvas.ZoomAround(Services.Canvas.Area.Center, Services.Canvas.Zoom / 1.25f);
        ImGui.SameLine();
        if (ImGui.Button($"{Services.Canvas.Zoom * 100:0}%", new Num.Vector2(third, 0))) Services.Canvas.ResetView();
        if (ImGui.IsItemHovered()) ImGui.SetTooltip("Click (or press F) to fit the tile to the window.");
        ImGui.SameLine();
        if (ImGui.Button("+", new Num.Vector2(third, 0))) Services.Canvas.ZoomAround(Services.Canvas.Area.Center, Services.Canvas.Zoom * 1.25f);
        Ui.Hint("Trackpad: two-finger scroll pans, Ctrl+scroll or pinch zooms, Space+drag pans.");

        // ---- problems
        var problems = Chasm.Core.Content.ContentValidator.Validate(t, Services.Defs).Where(p => !p.Contains("Id is empty")).ToList();
        if (problems.Count > 0)
        {
            Ui.Section("Check");
            foreach (var p in problems.Take(4)) { ImGui.PushStyleColor(ImGuiCol.Text, Ui.Warn); ImGui.TextWrapped(p); ImGui.PopStyleColor(); }
        }
        ImGui.End();
    }

    private void ToggleReference()
    {
        if (_panel == Panel.Reference) { _panel = Panel.Selection; return; }
        _panel = Panel.Reference;
        if (_reference is null) ImGui.OpenPopup("pickref");
    }

    /// <summary>A button that opens a right-panel page; clicking it again closes the page.</summary>
    private void PanelButton(string label, Panel panel, float width, string tooltip)
    {
        bool active = _panel == panel;
        if (Ui.ToggleButton(label, active, new Num.Vector2(width, 0), tooltip))
            _panel = active ? Panel.Selection : panel;
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
            float w = (LeftW - 24 - 40) / 2f;
            ImGui.AlignTextToFramePadding(); ImGui.TextUnformatted("X"); ImGui.SameLine();
            ImGui.SetNextItemWidth(w); bool cx = ImGui.InputInt("##x", ref x);
            ImGui.SameLine();
            ImGui.TextUnformatted("Y"); ImGui.SameLine();
            ImGui.SetNextItemWidth(w); bool cy = ImGui.InputInt("##y", ref y);
            if (cx || cy)
            {
                if (CoordinateClash(t.Floor, x, y) is { } clash) { _xyError = $"({x},{y}) is taken by {clash.DisplayName} [{clash.Id}]."; _xyErrorTime = 4; }
                else { t.X = x; t.Y = y; Edited(); }
            }
            if (!_isNewTile && t.Id != TileDef.IdFor(t.Floor!.Value, x0, y0))
                Ui.Colored(Ui.Muted, $"(id stays '{t.Id}')");
        }
        _xyErrorTime -= ImGui.GetIO().DeltaTime;
        if (_xyErrorTime > 0 && _xyError is not null) ImGui.TextColored(new Num.Vector4(1, 0.45f, 0.4f, 1), _xyError);
    }

    private void FloorMapPopup()
    {
        if (!ImGui.BeginPopup("floormap")) return;
        var tiles = Services.Defs.All<TileDef>().Where(x => x.Floor == _floor?.Number).ToList();
        var placed = tiles.Where(x => x.X is not null && x.Y is not null).ToList();
        ImGui.TextUnformatted($"Floor {_floor?.Number}: {placed.Count} tile(s) on the map, {tiles.Count - placed.Count} off-map");
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
