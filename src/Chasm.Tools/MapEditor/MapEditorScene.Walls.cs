using Chasm.Core.Serialization;
using Chasm.Core.World;
using Chasm.Tools.ImGuiHost;
using ImGuiNET;
using Num = System.Numerics;

namespace Chasm.Tools.MapEditor;

/// <summary>
/// Walls page (Wall down / Wall up lists of cliff materials), the Bottom / Top switch shared with
/// the Clouds page, and the "Duplicate to..." dialog for materials and cloud layers.
/// </summary>
public sealed partial class MapEditorScene
{
    private bool _wallsUp;   // Walls page: false = Wall down, true = Wall up
    private bool _cloudsTop; // Clouds page: false = Bottom clouds, true = Top clouds

    private void WallsPanel()
    {
        if (_floor is null) { ImGui.TextDisabled("This tile has no floor."); return; }
        SideSwitch("Wall down", "Wall up", ref _wallsUp,
            "Cliffs going down: they fade into the bottom clouds.", "Walls going up: they fade into the top clouds.");
        Ui.Hint(_wallsUp
            ? "Walls rising up. Fade is measured up from each wall's bottom edge into the top clouds. Shared by every tile; cut-out cells are per tile."
            : "Cliffs dropping away. Fade is measured down from each cliff's top edge into the bottom clouds. Shared by every tile; cut-out cells are per tile.");
        ImGui.BeginChild("walls");
        MaterialsEditor(_floor.Materials, isFloor: true, _wallsUp ? MaterialKind.WallUp : MaterialKind.WallDown);
        ImGui.EndChild();
    }

    /// <summary>Two big side-by-side toggles (e.g. Wall down | Wall up).</summary>
    private static void SideSwitch(string a, string b, ref bool second, string tipA, string tipB)
    {
        float w = (ImGui.GetContentRegionAvail().X - 6) / 2;
        if (Ui.ToggleButton(a, !second, new Num.Vector2(w, 30), tipA)) second = false;
        ImGui.SameLine(0, 6);
        if (Ui.ToggleButton(b, second, new Num.Vector2(w, 30), tipB)) second = true;
        ImGui.Spacing();
    }

    // ------------------------------------------------------------------ duplicate to...

    private enum DupTarget { Same, Other, General }

    private MaterialDef? _dupMaterial;
    private List<MaterialDef>? _dupMaterialList;
    private bool _dupMaterialIsFloor;
    private CloudLayer? _dupCloud;
    private DupTarget _dupTarget;
    private bool _dupFlipX, _dupFlipY, _dupReverse;
    private string _dupName = "";
    private bool _openDup;

    private void AskDuplicateMaterial(MaterialDef m, List<MaterialDef> list, bool isFloor)
    {
        _dupMaterial = m; _dupMaterialList = list; _dupMaterialIsFloor = isFloor; _dupCloud = null;
        SetDupTarget(DupTarget.Same);
        _openDup = true;
    }

    private void AskDuplicateCloud(CloudLayer c)
    {
        _dupCloud = c; _dupMaterial = null;
        SetDupTarget(DupTarget.Same);
        _openDup = true;
    }

    /// <summary>Picks a destination and the matching defaults (same list = mirror left/right; other side = mirror up/down).</summary>
    private void SetDupTarget(DupTarget t)
    {
        _dupTarget = t;
        _dupFlipX = t == DupTarget.Same;
        _dupFlipY = t == DupTarget.Other;
        _dupReverse = false;
        string name = _dupMaterial?.Name ?? _dupCloud?.Name ?? "";
        _dupName = t switch
        {
            DupTarget.Same => SwapWords(name, ("Left", "Right"), ("left", "right")) is var s && s != name ? s : name + " copy",
            DupTarget.Other => SwapWords(name, ("Down", "Up"), ("down", "up"), ("Bottom", "Top"), ("bottom", "top"), ("Lower", "Upper"), ("lower", "upper")) is var o && o != name ? o : name + " copy",
            _ => name + " copy",
        };
    }

    /// <summary>"Cliff Left" -> "Cliff Right" (and back), for suggested names.</summary>
    private static string SwapWords(string text, params (string a, string b)[] pairs)
    {
        var words = text.Split(' ');
        for (int i = 0; i < words.Length; i++)
            foreach (var (a, b) in pairs)
            {
                if (words[i] == a) { words[i] = b; break; }
                if (words[i] == b) { words[i] = a; break; }
            }
        return string.Join(' ', words);
    }

    private void DuplicatePopup()
    {
        if (_openDup) { ImGui.OpenPopup("Duplicate to"); _openDup = false; }
        CenterNextWindow();
        if (!ImGui.BeginPopupModal("Duplicate to", ImGuiWindowFlags.AlwaysAutoResize)) return;
        bool isCloud = _dupCloud is not null;
        Ui.Title($"Duplicate \"{_dupMaterial?.Name ?? _dupCloud?.Name}\"");
        ImGui.Spacing();
        ImGui.TextUnformatted("Where to?");

        if (isCloud)
        {
            string here = _cloudsTop ? "Top clouds" : "Bottom clouds", there = _cloudsTop ? "Bottom clouds" : "Top clouds";
            if (ImGui.RadioButton($"{here} (this list)", _dupTarget == DupTarget.Same)) SetDupTarget(DupTarget.Same);
            if (ImGui.RadioButton($"{there} (the other side)", _dupTarget == DupTarget.Other)) SetDupTarget(DupTarget.Other);
            ImGui.Spacing();
            ImGui.Checkbox("Reverse its direction (+180 deg)", ref _dupReverse);
        }
        else
        {
            var kind = _dupMaterial!.EffectiveKind;
            bool floorWall = _dupMaterialIsFloor && kind != MaterialKind.General;
            string here = !_dupMaterialIsFloor ? "This tile's local materials" : PageName(kind);
            if (ImGui.RadioButton($"{here} (this list)", _dupTarget == DupTarget.Same)) SetDupTarget(DupTarget.Same);
            if (floorWall)
            {
                var other = kind == MaterialKind.WallDown ? MaterialKind.WallUp : MaterialKind.WallDown;
                if (ImGui.RadioButton($"{PageName(other)} (the other side)", _dupTarget == DupTarget.Other)) SetDupTarget(DupTarget.Other);
                if (ImGui.RadioButton("Textures (general)", _dupTarget == DupTarget.General)) SetDupTarget(DupTarget.General);
            }
            else if (_dupMaterialIsFloor)
            {
                if (ImGui.RadioButton("Wall down", _dupTarget == DupTarget.Other)) SetDupTarget(DupTarget.Other);
            }
            ImGui.Spacing();
            ImGui.TextUnformatted("Mirror the copy:");
            ImGui.Checkbox("Flip X (left <-> right)", ref _dupFlipX);
            ImGui.SameLine(0, 20);
            ImGui.Checkbox("Flip Y (up <-> down)", ref _dupFlipY);
        }
        ImGui.Spacing();
        ImGui.TextUnformatted("Name"); ImGui.SameLine(70);
        ImGui.SetNextItemWidth(280);
        if (ImGui.IsWindowAppearing()) ImGui.SetKeyboardFocusHere();
        bool enter = ImGui.InputText("##dupname", ref _dupName, 80, ImGuiInputTextFlags.EnterReturnsTrue);
        ImGui.Spacing();
        bool go = Ui.AccentButton("Duplicate", new Num.Vector2(150, 0)) || enter;
        ImGui.SameLine();
        if (ImGui.Button("Cancel", new Num.Vector2(120, 0))) { _dupMaterial = null; _dupCloud = null; ImGui.CloseCurrentPopup(); }
        if (go && _dupName.Trim().Length > 0)
        {
            if (isCloud) DoDuplicateCloud(); else DoDuplicateMaterial();
            ImGui.CloseCurrentPopup();
        }
        ImGui.EndPopup();
    }

    private static string PageName(MaterialKind k) => k switch
    {
        MaterialKind.WallDown => "Wall down",
        MaterialKind.WallUp => "Wall up",
        _ => "Textures",
    };

    private void DoDuplicateMaterial()
    {
        var src = _dupMaterial!;
        var list = _dupMaterialList!;
        var copy = ChasmJson.Clone(src);
        copy.Name = _dupName.Trim();
        copy.Id = UniqueMaterialId(list, copy.Name);
        if (_dupFlipX) copy.FlipX = !copy.FlipX;
        if (_dupFlipY) copy.FlipY = !copy.FlipY;
        var kind = src.EffectiveKind;
        if (_dupMaterialIsFloor)
        {
            var target = _dupTarget switch
            {
                DupTarget.Other => kind switch { MaterialKind.WallDown => MaterialKind.WallUp, MaterialKind.WallUp => MaterialKind.WallDown, _ => MaterialKind.WallDown },
                DupTarget.General => MaterialKind.General,
                _ => kind,
            };
            bool keepCutOut = copy.UseForCutOut;
            ApplyKind(copy, target);
            if (target == MaterialKind.General) copy.UseForCutOut = keepCutOut;
            // Show the copy where it went.
            switch (target)
            {
                case MaterialKind.WallDown: _panel = Panel.Walls; _wallsUp = false; break;
                case MaterialKind.WallUp: _panel = Panel.Walls; _wallsUp = true; break;
                default: if (_panel == Panel.Walls) _panel = Panel.FloorTextures; break;
            }
        }
        list.Insert(list.IndexOf(src) + 1, copy);
        _editingMaterial = (_dupMaterialIsFloor ? "" : "local:") + copy.Id;
        _focusMaterialName = true;
        _cutOutMaterial = null;
        _dupMaterial = null;
        Edited();
        Status($"Duplicated as '{copy.Name}'. Tweak it, pick its cut-out cells, then Save (Ctrl+S).");
    }

    private void DoDuplicateCloud()
    {
        var floor = _floor!;
        var src = _dupCloud!;
        var copy = src.Copy();
        copy.Id = CloudStack.NewId(floor);
        copy.Name = _dupName.Trim();
        if (_dupReverse) copy.Direction = (copy.Direction + 180) % 360;
        bool srcTop = floor.TopClouds.Contains(src);
        bool toTop = _dupTarget == DupTarget.Other ? !srcTop : srcTop;
        var list = toTop ? floor.TopClouds : floor.Clouds;
        int at = list.IndexOf(src);
        list.Insert(at >= 0 ? at + 1 : list.Count, copy);
        _cloudsTop = toTop;
        _openCloud = copy.Id;
        _dupCloud = null;
        Edited();
        Status($"Duplicated to {(toTop ? "Top" : "Bottom")} clouds as '{copy.Name}'.");
    }
}
