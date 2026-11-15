using Chasm.Core.Serialization;
using Chasm.Core.World;
using Chasm.Tools.ImGuiHost;
using ImGuiNET;
using Num = System.Numerics;

namespace Chasm.Tools.MapEditor;

/// <summary>
/// Cloud prefabs: a saved stack of cloud layers used as one entry on a floor (or swapped in on a
/// tile). Linked: editing a prefab's layers changes it everywhere it's used (saved with Save).
/// </summary>
public sealed partial class MapEditorScene
{
    private readonly HashSet<string> _dirtyCloudPrefabs = new();
    private string? _openPrefabLayer;
    private bool _openAddCloudPrefab;
    private bool _openSaveCloudPrefab;
    private string _cloudPrefabName = "";
    private readonly HashSet<string> _cloudPrefabPick = new();
    private bool _cloudPrefabReplace = true;

    private CloudPrefabDef? PrefabOf(CloudLayer entry) =>
        !string.IsNullOrEmpty(entry.Prefab) && Services.Defs.TryGet<CloudPrefabDef>(entry.Prefab, out var p) ? p : null;

    private void SaveCloudPrefabs()
    {
        foreach (var id in _dirtyCloudPrefabs)
            if (Services.Defs.TryGet<CloudPrefabDef>(id, out var p))
            {
                Services.DefReload.IgnoreNextChange(Services.Defs.PathFor(typeof(CloudPrefabDef), id));
                Services.Defs.Save(p);
            }
        _dirtyCloudPrefabs.Clear();
    }

    /// <summary>Show / opacity / which prefab: what a floor entry or a tile override sets for a prefab.</summary>
    private bool PrefabEntryFields(CloudLayer e)
    {
        const float label = 110;
        bool changed = false;
        ImGui.TextUnformatted("Show"); ImGui.SameLine(label);
        bool en = e.Enabled;
        if (ImGui.Checkbox("##pen", ref en)) { e.Enabled = en; changed = true; }
        ImGui.TextUnformatted("Opacity"); ImGui.SameLine(label); ImGui.SetNextItemWidth(-1);
        float op = e.Opacity * 100;
        if (ImGui.SliderFloat("##pop", ref op, 0, 100, "%.0f%%")) { e.Opacity = op / 100f; changed = true; }
        if (ImGui.IsItemHovered()) ImGui.SetTooltip("Fades the whole prefab (on top of each layer's own opacity).");
        ImGui.TextUnformatted("Prefab"); ImGui.SameLine(label); ImGui.SetNextItemWidth(-1);
        var current = PrefabOf(e);
        if (ImGui.BeginCombo("##ppick", current?.DisplayName ?? "(missing)"))
        {
            foreach (var p in Services.Defs.All<CloudPrefabDef>().OrderBy(p => p.DisplayName))
                if (ImGui.Selectable($"{p.DisplayName}  ({p.Layers.Count} layers)##{p.Id}", p == current)) { e.Prefab = p.Id; changed = true; }
            ImGui.EndCombo();
        }
        return changed;
    }

    /// <summary>A prefab entry on the floor, opened: its settings, then the prefab's own layers (linked).</summary>
    private bool PrefabEntry(FloorDef floor, List<CloudLayer> list, int index)
    {
        var e = list[index];
        bool changed = false;
        ImGui.TextUnformatted("Name"); ImGui.SameLine(110); ImGui.SetNextItemWidth(-1);
        string name = e.Name;
        if (ImGui.InputText("##pname", ref name, 80)) { e.Name = name; changed = true; }
        changed |= PrefabEntryFields(e);
        if (PrefabOf(e) is not { } prefab)
        {
            Ui.Colored(Ui.Warn, "This prefab is missing (deleted?). Pick another or delete the entry.");
            return changed;
        }
        Ui.ColoredWrapped(Ui.Blue, $"Linked to '{prefab.DisplayName}': editing its layers below changes it everywhere it's used (saved with Save).");

        // The prefab's layers, back to front.
        Ui.Colored(Ui.Muted, "Prefab layers (top = furthest back):");
        int up = -1, down = -1, remove = -1;
        for (int i = 0; i < prefab.Layers.Count; i++)
        {
            var l = prefab.Layers[i];
            ImGui.PushID("pl" + l.Id);
            if (ImGui.ArrowButton("up", ImGuiDir.Up)) up = i;
            ImGui.SameLine(0, 2);
            if (ImGui.ArrowButton("down", ImGuiDir.Down)) down = i;
            ImGui.SameLine();
            bool on = l.Enabled;
            if (ImGui.Checkbox("##on", ref on)) { l.Enabled = on; MarkPrefab(prefab); }
            ImGui.SameLine();
            Thumb(l.Texture, 22);
            ImGui.SameLine();
            string key = prefab.Id + "/" + l.Id;
            bool open = _openPrefabLayer == key;
            if (ImGui.Selectable(l.Name, open, ImGuiSelectableFlags.None, new Num.Vector2(0, 22))) _openPrefabLayer = open ? null : key;
            if (open)
            {
                ImGui.Indent();
                if (CloudFields(l)) MarkPrefab(prefab);
                if (ImGui.Button("Delete layer")) remove = i;
                ImGui.Unindent();
                ImGui.Spacing();
            }
            ImGui.PopID();
        }
        if (up > 0) { (prefab.Layers[up - 1], prefab.Layers[up]) = (prefab.Layers[up], prefab.Layers[up - 1]); MarkPrefab(prefab); }
        if (down >= 0 && down < prefab.Layers.Count - 1) { (prefab.Layers[down + 1], prefab.Layers[down]) = (prefab.Layers[down], prefab.Layers[down + 1]); MarkPrefab(prefab); }
        if (remove >= 0) { prefab.Layers.RemoveAt(remove); MarkPrefab(prefab); }
        if (ImGui.Button("+ Add layer to prefab"))
        {
            var layer = new CloudLayer { Id = prefab.NewLayerId(), Name = $"Layer {prefab.Layers.Count + 1}", Texture = "" };
            prefab.Layers.Add(layer);
            _openPrefabLayer = prefab.Id + "/" + layer.Id;
            MarkPrefab(prefab);
            Art.ArtBrowser.OpenPick(null, k => { layer.Texture = k; MarkPrefab(prefab); });
        }
        ImGui.SameLine();
        if (ImGui.Button("Unlink (copy its layers here)"))
        {
            // The prefab's layers become this floor's own layers, in its place.
            list.RemoveAt(index);
            int at = index;
            foreach (var l in prefab.Layers)
            {
                var copy = l.Copy();
                copy.Id = CloudStack.NewId(floor);
                copy.Opacity *= e.Opacity;
                if (!e.Enabled) copy.Enabled = false;
                list.Insert(at++, copy);
            }
            _openCloud = null;
            changed = true;
            Status($"Unlinked: '{prefab.DisplayName}' is now {prefab.Layers.Count} layer(s) of this floor. The prefab is unchanged.");
        }
        if (ImGui.IsItemHovered()) ImGui.SetTooltip("Copies the layers onto this floor so you can change them here only.");
        return changed;
    }

    private void MarkPrefab(CloudPrefabDef p) => _dirtyCloudPrefabs.Add(p.Id);

    private void AddCloudPrefabPopup(FloorDef floor, List<CloudLayer> clouds)
    {
        if (_openAddCloudPrefab) { ImGui.OpenPopup("Add a cloud prefab"); _openAddCloudPrefab = false; _cloudPrefabName = ""; }
        CenterNextWindow();
        if (!ImGui.BeginPopupModal("Add a cloud prefab", ImGuiWindowFlags.AlwaysAutoResize)) return;
        var prefabs = Services.Defs.All<CloudPrefabDef>().OrderBy(p => p.Category).ThenBy(p => p.DisplayName).ToList();
        if (prefabs.Count == 0) Ui.Hint("No cloud prefabs yet. Start a new one below, or use 'Save layers as prefab' on layers you've made.");
        else
        {
            Ui.Hint("Pick one to add it to this floor's " + (_cloudsTop ? "top" : "bottom") + " clouds (linked).");
            ImGui.BeginChild("cprefabs", new Num.Vector2(460, Math.Min(280, prefabs.Count * 30 + 12)), ImGuiChildFlags.Borders);
            foreach (var p in prefabs)
            {
                if (p.Layers.Count > 0) { Thumb(p.Layers[0].Texture, 24); ImGui.SameLine(); }
                if (ImGui.Selectable($"{p.DisplayName}    ({p.Layers.Count} layers, {p.Category})##{p.Id}", false, ImGuiSelectableFlags.None, new Num.Vector2(0, 24)))
                {
                    AddPrefabEntry(floor, clouds, p);
                    ImGui.CloseCurrentPopup();
                }
            }
            ImGui.EndChild();
        }
        ImGui.Spacing();
        ImGui.TextUnformatted("New, empty prefab:");
        ImGui.SetNextItemWidth(300);
        bool enter = ImGui.InputTextWithHint("##newcp", "e.g. Stormy sky", ref _cloudPrefabName, 60, ImGuiInputTextFlags.EnterReturnsTrue);
        ImGui.SameLine();
        if ((ImGui.Button("Create") || enter) && _cloudPrefabName.Trim().Length > 0)
        {
            var p = new CloudPrefabDef { Name = _cloudPrefabName.Trim() };
            p.Id = Services.Defs.SuggestId(typeof(CloudPrefabDef), p.Name);
            Services.Defs.Save(p);
            AddPrefabEntry(floor, clouds, p);
            Status($"Created cloud prefab '{p.Name}'. Add layers to it below.");
            ImGui.CloseCurrentPopup();
        }
        ImGui.Spacing();
        if (ImGui.Button("Cancel", new Num.Vector2(100, 0))) ImGui.CloseCurrentPopup();
        ImGui.EndPopup();
    }

    private void AddPrefabEntry(FloorDef floor, List<CloudLayer> clouds, CloudPrefabDef p)
    {
        var entry = new CloudLayer { Id = CloudStack.NewId(floor), Name = p.DisplayName, Prefab = p.Id, Texture = "" };
        clouds.Add(entry);
        _openCloud = entry.Id;
        Edited();
    }

    private void OpenSaveCloudPrefab(List<CloudLayer> clouds)
    {
        _openSaveCloudPrefab = true;
        _cloudPrefabName = "";
        _cloudPrefabReplace = true;
        _cloudPrefabPick.Clear();
        foreach (var c in clouds.Where(c => string.IsNullOrEmpty(c.Prefab))) _cloudPrefabPick.Add(c.Id);
    }

    private void SaveCloudPrefabPopup(FloorDef floor, List<CloudLayer> clouds)
    {
        if (_openSaveCloudPrefab) { ImGui.OpenPopup("Save layers as a cloud prefab"); _openSaveCloudPrefab = false; }
        CenterNextWindow();
        if (!ImGui.BeginPopupModal("Save layers as a cloud prefab", ImGuiWindowFlags.AlwaysAutoResize)) return;
        Ui.Hint("Which layers go in it (kept in this order, back to front):");
        foreach (var c in clouds.Where(c => string.IsNullOrEmpty(c.Prefab)))
        {
            bool on = _cloudPrefabPick.Contains(c.Id);
            if (ImGui.Checkbox($"##pk{c.Id}", ref on)) { if (on) _cloudPrefabPick.Add(c.Id); else _cloudPrefabPick.Remove(c.Id); }
            ImGui.SameLine();
            Thumb(c.Texture, 22);
            ImGui.SameLine();
            ImGui.TextUnformatted(c.Name);
        }
        ImGui.Spacing();
        ImGui.SetNextItemWidth(300);
        if (ImGui.IsWindowAppearing()) ImGui.SetKeyboardFocusHere();
        bool enter = ImGui.InputTextWithHint("##cpname", "Prefab name, e.g. Stormy sky", ref _cloudPrefabName, 60, ImGuiInputTextFlags.EnterReturnsTrue);
        ImGui.Checkbox("Replace them here with the prefab (linked)", ref _cloudPrefabReplace);
        if (ImGui.IsItemHovered()) ImGui.SetTooltip("On: this floor then uses the prefab, so improving it later updates here too.\nOff: the layers stay as they are; the prefab is just saved for other floors.");
        bool ok = _cloudPrefabName.Trim().Length > 0 && _cloudPrefabPick.Count > 0;
        ImGui.BeginDisabled(!ok);
        if (Ui.AccentButton("Save prefab", new Num.Vector2(140, 0)) || (enter && ok))
        {
            var picked = clouds.Where(c => _cloudPrefabPick.Contains(c.Id) && string.IsNullOrEmpty(c.Prefab)).ToList();
            var p = new CloudPrefabDef { Name = _cloudPrefabName.Trim() };
            p.Id = Services.Defs.SuggestId(typeof(CloudPrefabDef), p.Name);
            foreach (var c in picked)
            {
                var copy = ChasmJson.Clone(c);
                copy.Id = p.NewLayerId();
                p.Layers.Add(copy);
            }
            Services.Defs.Save(p);
            if (_cloudPrefabReplace)
            {
                int at = clouds.IndexOf(picked[0]);
                var entry = new CloudLayer { Id = CloudStack.NewId(floor), Name = p.Name, Prefab = p.Id, Texture = "" };
                clouds.Insert(at, entry);
                foreach (var c in picked) clouds.Remove(c);
                _openCloud = entry.Id;
                Edited();
            }
            Status($"Saved cloud prefab '{p.Name}' ({picked.Count} layers).");
            ImGui.CloseCurrentPopup();
        }
        ImGui.EndDisabled();
        ImGui.SameLine();
        if (ImGui.Button("Cancel", new Num.Vector2(100, 0))) ImGui.CloseCurrentPopup();
        ImGui.EndPopup();
    }
}
