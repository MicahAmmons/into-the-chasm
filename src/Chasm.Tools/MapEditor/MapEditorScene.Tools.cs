using Chasm.Core.Grid;
using Chasm.Core.World;
using Chasm.Game.Input;
using Chasm.Tools.ImGuiHost;
using ImGuiNET;
using Microsoft.Xna.Framework.Input;
using Num = System.Numerics;

namespace Chasm.Tools.MapEditor;

public sealed partial class MapEditorScene
{
    // Selection state
    private CellCoord? _selectedCell;
    private readonly HashSet<CellCoord> _selection = new();     // paint-texture cell selection
    private PaintRegion? _selectedRegion;
    private PropDef? _selectedProp;
    private Placement? _selectedPlacement;

    // Drag state
    private bool? _dragMode;                // value being painted by the current drag ("first cell decides")
    private Num.Vector2 _lastDragPx;
    private Num.Vector2 _propGrabOffset;
    private bool _draggingProp, _draggingPlacement;

    // Props
    private string _propTexture = "";
    private bool _snapProps;

    // Interactions
    private CellCoord? _radiusCenter;
    private int _radius = 2;
    private CellCoord? _triggerCell;

    private void ClearSelections()
    {
        _selectedCell = null;
        _selection.Clear();
        _selectedRegion = null;
        _selectedProp = null;
        _selectedPlacement = null;
        _dragMode = null;
        _radiusCenter = null;
    }

    private void UpdateTool(InputState input)
    {
        var t = _tile!;
        if (UpdateStencilPlacing(input)) return;
        if (UpdateCutOutPicking(input)) return;
        if (input.Pressed(InputAction.Cancel) && !input.KeyboardCapturedByUi)
        {
            if (_radiusCenter is not null) { _radiusCenter = null; Status("Cancelled."); }
            else if (_tool != Tool.Select) SetTool(Tool.Select);
            else ClearSelections();
            return;
        }
        if (input.KeyPressed(Keys.Delete) && !input.KeyboardCapturedByUi) DeleteSelected();
        // Number keys pick tools (when not typing in a box).
        if (!input.KeyboardCapturedByUi && !input.KeyHeld(Keys.LeftControl))
        {
            for (int i = 0; i < ToolList.Length; i++)
                if (input.KeyPressed(Keys.D1 + i)) { SetTool(ToolList[i].tool); return; }
            if (input.KeyPressed(Keys.D8)) ToggleReference();
        }
        if (input.LeftReleased || input.RightReleased) { _dragMode = null; _draggingProp = false; _draggingPlacement = false; }

        switch (_tool)
        {
            case Tool.Walkable:
                PaintCells(input, c => t.Walkable[c], (c, v) =>
                {
                    t.Walkable[c] = v;
                    if (!v) { t.PlayerCombatStarts.Remove(c); t.EnemyCombatStarts.Remove(c); }
                }, rightPaintsFalse: true);
                break;
            case Tool.PlayerStarts:
                WalkableOnlyHint();
                PaintCells(input, c => t.PlayerCombatStarts.Contains(c), (c, v) => SetMember(t.PlayerCombatStarts, c, v && t.Walkable[c]), rightPaintsFalse: true);
                break;
            case Tool.EnemySpawns:
                WalkableOnlyHint();
                PaintCells(input, c => t.EnemyCombatStarts.Contains(c), (c, v) => SetMember(t.EnemyCombatStarts, c, v && t.Walkable[c]), rightPaintsFalse: true);
                break;
            case Tool.PaintTexture:
                PaintCells(input, c => _selection.Contains(c), (c, v) => { if (v) _selection.Add(c); else _selection.Remove(c); }, rightPaintsFalse: true, editsTile: false);
                break;
            case Tool.Select:
                UpdateSelect(input);
                break;
            case Tool.PlaceProp:
                UpdatePlaceProp(input);
                break;
            case Tool.Interaction:
                UpdateInteractionTool(input);
                break;
        }
    }

    private void WalkableOnlyHint()
    {
        if (_hoverCell is { } c && !_tile!.Walkable[c]) ImGui.SetTooltip("Only walkable cells can be chosen.\nMake it walkable first (Walkable cells tool).");
    }

    private static void SetMember(List<CellCoord> list, CellCoord c, bool member)
    {
        if (member) { if (!list.Contains(c)) list.Add(c); }
        else list.Remove(c);
    }

    /// <summary>
    /// Shared click/drag cell painting. The first cell pressed decides the value for the whole drag
    /// (pressing an "on" cell paints "off" and vice versa). Right-drag always paints "off".
    /// Fast drags fill the cells in between so there are no gaps.
    /// </summary>
    private void PaintCells(InputState input, Func<CellCoord, bool> get, Action<CellCoord, bool> set, bool rightPaintsFalse, bool editsTile = true)
    {
        if (_hoverCell is not { } cell) return;
        if (input.LeftClicked) { _dragMode = !get(cell); _lastDragPx = _hoverPx; }
        else if (rightPaintsFalse && input.RightClicked) { _dragMode = false; _lastDragPx = _hoverPx; }
        if (_dragMode is not bool value || !(input.LeftHeld || input.RightHeld)) return;
        bool changed = false;
        foreach (var c in LineOfSight.CellsAlong(_lastDragPx, _hoverPx).Append(cell))
        {
            if (!IsoGrid.InTile(c) || get(c) == value) continue;
            set(c, value);
            changed |= get(c) == value;
        }
        _lastDragPx = _hoverPx;
        if (changed && editsTile) Edited();
    }

    // ------------------------------------------------------------------ select / move

    private void UpdateSelect(InputState input)
    {
        var t = _tile!;
        if (input.LeftClicked && _hoverCell is { } cell)
        {
            _selectedProp = PropAt(_hoverPx);
            _selectedPlacement = _selectedProp is null ? t.Placements.LastOrDefault(p => p.Cell == cell) : null;
            _selectedCell = cell;
            _selectedRegion = null;
            if (_selectedProp is { } prop) { _draggingProp = true; _propGrabOffset = new Num.Vector2(prop.X, prop.Y) - _hoverPx; }
            else if (_selectedPlacement is not null) _draggingPlacement = true;
            _panel = Panel.Selection;
        }
        else if (input.LeftHeld && _draggingProp && _selectedProp is { } p)
        {
            var pos = _hoverPx + _propGrabOffset;
            if (_snapProps) pos = IsoGrid.CellCenter(IsoGrid.CellAt(pos));
            if (pos.X != p.X || pos.Y != p.Y) { p.X = pos.X; p.Y = pos.Y; Edited(); }
        }
        else if (input.LeftHeld && _draggingPlacement && _selectedPlacement is { } pl && _hoverCell is { } to && pl.Cell != to)
        {
            pl.Cell = to; _selectedCell = to; Edited();
        }
    }

    /// <summary>The top-most prop whose image covers a canvas point.</summary>
    private PropDef? PropAt(Num.Vector2 px)
    {
        foreach (var p in _tile!.Props.OrderByDescending(p => p.Layer).ThenByDescending(p => p.Y))
            if (PropBounds(p).Contains((int)px.X, (int)px.Y)) return p;
        return null;
    }

    private Microsoft.Xna.Framework.Rectangle PropBounds(PropDef p)
    {
        var tex = Services.Textures.Get(p.Texture);
        bool ph = Services.Textures.IsPlaceholder(tex);
        float w = (ph ? 80 : tex.Width) * p.Scale, h = (ph ? 80 : tex.Height) * p.Scale;
        return new Microsoft.Xna.Framework.Rectangle((int)(p.X - w / 2), (int)(p.Y - h), (int)w, (int)h);
    }

    private void DeleteSelected()
    {
        var t = _tile!;
        if (_selectedProp is not null) { t.Props.Remove(_selectedProp); _selectedProp = null; Edited(); Status("Prop removed."); }
        else if (_selectedPlacement is not null) { t.Placements.Remove(_selectedPlacement); _selectedPlacement = null; Edited(); Status("Object removed."); }
        else if (_selectedRegion is not null && _tool == Tool.PaintTexture) { t.PaintRegions.Remove(_selectedRegion); _selectedRegion = null; Edited(); Status("Region removed."); }
    }

    // ------------------------------------------------------------------ props

    private void UpdatePlaceProp(InputState input)
    {
        if (_hoverCell is null) return;
        if (string.IsNullOrEmpty(_propTexture)) { ImGui.SetTooltip("Pick a PNG in the panel on the right first."); return; }
        if (!input.LeftClicked) return;
        // Clicking an existing prop selects/moves it instead of placing a new one.
        if (PropAt(_hoverPx) is { } existing)
        {
            _selectedProp = existing; _draggingProp = true;
            _propGrabOffset = new Num.Vector2(existing.X, existing.Y) - _hoverPx;
            _tool = Tool.Select;
            return;
        }
        var pos = _snapProps ? IsoGrid.CellCenter(_hoverCell.Value) : _hoverPx;
        var prop = new PropDef { Id = NextId("prop", _tile!.Props.Select(p => p.Id)), Texture = _propTexture, X = pos.X, Y = pos.Y };
        _tile.Props.Add(prop);
        _selectedProp = prop;
        Edited();
        Status($"Placed {prop.Id}. Click again to place another; Esc to stop.");
    }

    private static string NextId(string prefix, IEnumerable<string> used)
    {
        var set = used.ToHashSet();
        for (int n = 1; ; n++) if (!set.Contains($"{prefix}_{n}")) return $"{prefix}_{n}";
    }

    // ------------------------------------------------------------------ interactions

    private void UpdateInteractionTool(InputState input)
    {
        if (_radiusCenter is { } center)
        {
            if (_hoverCell is { } h) _radius = Math.Min(30, h.ManhattanTo(center));
            ImGui.SetTooltip($"Radius {_radius} - move to resize, click to confirm, Esc to cancel");
            if (input.LeftClicked) { CreateInteraction(center, InteractTrigger.Proximity, _radius); _radiusCenter = null; }
            return;
        }
        if (_hoverCell is { } cell && input.LeftClicked)
        {
            _triggerCell = cell;
            ImGui.OpenPopup("trigger?");
        }
        CenterNextWindow();
        if (ImGui.BeginPopup("trigger?"))
        {
            Ui.Colored(Ui.Muted, $"Interaction at {_triggerCell}. What triggers it?");
            if (ImGui.Selectable("Player presses Interact / clicks it")) CreateInteraction(_triggerCell!.Value, InteractTrigger.Interact, 0);
            if (ImGui.Selectable("Player steps onto the cell")) CreateInteraction(_triggerCell!.Value, InteractTrigger.StepOn, 0);
            if (ImGui.Selectable("Player comes within a radius...")) { _radiusCenter = _triggerCell; _radius = 2; }
            ImGui.EndPopup();
        }
    }

    private void CreateInteraction(CellCoord cell, InteractTrigger trigger, int radius)
    {
        var t = _tile!;
        var p = new InteractablePlacement
        {
            Id = NextId("interaction", t.Placements.Select(x => x.Id)), Cell = cell, Trigger = trigger, Radius = radius,
            Label = "Interaction", Invisible = trigger != InteractTrigger.Interact,
        };
        t.Placements.Add(p);
        _selectedPlacement = p;
        _selectedCell = cell;
        _panel = Panel.Selection;
        Edited();
        Status($"Added {p.Id}. Fill in what it does on the right (Rules).");
    }
}
