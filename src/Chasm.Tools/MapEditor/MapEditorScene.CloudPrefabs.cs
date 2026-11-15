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
    private string? _openPrefabLayer;
    private bool _openAddCloudPrefab;
    private bool _openSaveCloudPrefab;
    private string _cloudPrefabName = "";
    private readonly HashSet<string> _cloudPrefabPick = new();
    private bool _cloudPrefabReplace = true;
    private bool _openSaveAsCloudPrefab;
    private CloudLayer? _saveAsEntry;
    /// <summary>A change to the floor's cloud list that has to wait until the list is drawn.</summary>
    private Action? _afterCloudList;

    private CloudPrefabDef? PrefabOf(CloudLayer entry) =>
        !string.IsNullOrEmpty(entry.Prefab) && Services.Defs.TryGet<CloudPrefabDef>(entry.Prefab, out var p) ? p : null;

    /// <summary>The layers a prefab entry shows: its unsaved edits, else the prefab's.</summary>
    private List<CloudLayer>? PrefabLayersOf(CloudLayer entry) => entry.Layers ?? PrefabOf(entry)?.Layers;

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
                if (ImGui.Selectable($"{p.DisplayName}  ({p.Layers.Count} layers)##{p.Id}", p == current) && p != current)
                {
                    e.Prefab = p.Id;
                    e.Layers = null;   // edits were for the other prefab
                    changed = true;
                }
            ImGui.EndCombo();
        }
        return changed;
    }

    /// <summary>
    /// A prefab entry on the floor, opened: its settings, then its layers. Editing a layer doesn't
    /// touch the saved prefab: the edits stay on this floor ("edited here") until you Save them over
    /// the prefab, Save them as a new prefab, or Revert.
    /// </summary>
    private bool PrefabEntry(FloorDef floor, List<CloudLayer> list, int index)
    {
        var e = list[index];
        bool changed = false;
        ImGui.TextUnformatted("Name"); ImGui.SameLine(110); ImGui.SetNextItemWidth(-1);
        string name = e.Name;
        if (ImGui.InputText("##pname", ref name, 80)) { e.Name = name; changed = true; }
        changed |= PrefabEntryFields(e);
        var prefab = PrefabOf(e);
        if (prefab is null && e.Layers is null)
        {
            Ui.Colored(Ui.Warn, "This prefab is missing (deleted?). Pick another or remove the entry.");
            return changed;
        }

        // Saved, or edited here?
        if (e.Layers is not null)
        {
            Ui.ColoredWrapped(Ui.Warn, prefab is null
                ? "Edited here; the prefab it came from is gone. Save as a new prefab to keep it."
                : $"Edited here: these changes are only on this floor. '{prefab.DisplayName}' itself is unchanged until you save over it.");
            if (prefab is not null)
            {
                if (Ui.AccentButton("Save to prefab"))
                {
                    prefab.Layers = e.Layers.Select(l => l.Copy()).ToList();
                    Services.DefReload.IgnoreNextChange(Services.Defs.PathFor(typeof(CloudPrefabDef), prefab.Id));
                    Services.Defs.Save(prefab);
                    e.Layers = null;
                    changed = true;
                    Status($"Saved over '{prefab.DisplayName}'. Every floor and tile using it now shows this.");
                }
                if (ImGui.IsItemHovered()) ImGui.SetTooltip($"Overwrite '{prefab.DisplayName}' with these layers (every floor using it updates).");
                ImGui.SameLine();
            }
            if (ImGui.Button("Save as new prefab...")) { _saveAsEntry = e; _cloudPrefabName = (prefab?.DisplayName ?? e.Name) + " 2"; _openSaveAsCloudPrefab = true; }
            if (prefab is not null)
            {
                ImGui.SameLine();
                if (ImGui.Button("Revert")) { e.Layers = null; changed = true; }
                if (ImGui.IsItemHovered()) ImGui.SetTooltip("Throw away the changes made here: back to the prefab as saved.");
            }
        }
        else Ui.Hint($"Using '{prefab!.DisplayName}' as saved. Change a layer below and it stays on this floor only, until you choose to save it.");
        SaveAsCloudPrefabPopup();

        // The layers, back to front, each collapsible. Edits go to this entry's own copy.
        var layers = e.Layers ?? prefab!.Layers;
        List<CloudLayer> Own() => e.Layers ??= prefab!.Layers.Select(l => l.Copy()).ToList();
        int up = -1, down = -1, remove = -1;
        for (int i = 0; i < layers.Count; i++)
        {
            var l = layers[i];
            // Not edited yet: edit a copy, and only make this entry's own copy if something changes.
            var target = e.Layers is not null ? l : l.Copy();
            bool lc = false;
            ImGui.PushID("pl" + i);
            if (ImGui.ArrowButton("up", ImGuiDir.Up)) up = i;
            ImGui.SameLine(0, 2);
            if (ImGui.ArrowButton("down", ImGuiDir.Down)) down = i;
            ImGui.SameLine();
            bool on = target.Enabled;
            if (ImGui.Checkbox("##on", ref on)) { target.Enabled = on; lc = true; }
            ImGui.SameLine();
            Thumb(target.Texture, 22);
            ImGui.SameLine();
            bool open = _openPrefabLayer == l.Id;
            ImGui.SetNextItemOpen(open);
            bool node = ImGui.TreeNodeEx($"{target.Name}###layer", ImGuiTreeNodeFlags.SpanAvailWidth);
            if (node != open) _openPrefabLayer = node ? l.Id : null;
            if (node)
            {
                lc |= CloudFields(target);
                if (ImGui.Button("Delete layer")) remove = i;
                ImGui.TreePop();
                ImGui.Spacing();
            }
            if (lc) { if (e.Layers is null) Own()[i] = target; changed = true; }
            ImGui.PopID();
        }
        if (up > 0) { var o = Own(); (o[up - 1], o[up]) = (o[up], o[up - 1]); changed = true; }
        if (down >= 0 && down < layers.Count - 1) { var o = Own(); (o[down + 1], o[down]) = (o[down], o[down + 1]); changed = true; }
        if (remove >= 0) { Own().RemoveAt(remove); changed = true; }
        if (ImGui.Button("+ Add layer"))
        {
            var o = Own();
            var layer = new CloudLayer { Id = NewLayerId(o), Name = $"Layer {o.Count + 1}", Texture = "" };
            o.Add(layer);
            _openPrefabLayer = layer.Id;
            changed = true;
            Art.ArtBrowser.OpenPick(null, k => { layer.Texture = k; Edited(); });
        }
        ImGui.SameLine();
        if (ImGui.Button("Unlink (copy its layers here)"))
        {
            var copy = layers.Select(l => l.Copy()).ToList();
            _afterCloudList = () =>
            {
                // The layers become this floor's own plain layers, in the prefab's place.
                int at = list.IndexOf(e);
                if (at < 0) return;
                list.RemoveAt(at);
                foreach (var l in copy)
                {
                    l.Id = CloudStack.NewId(floor);
                    l.Opacity *= e.Opacity;
                    if (!e.Enabled) l.Enabled = false;
                    list.Insert(at++, l);
                }
                _openCloud = null;
                Edited();
                Status($"Unlinked: now {copy.Count} layer(s) of this floor. The prefab is unchanged.");
            };
        }
        if (ImGui.IsItemHovered()) ImGui.SetTooltip("Turns it into this floor's own plain layers (no longer a prefab).");
        return changed;
    }

    private static string NewLayerId(List<CloudLayer> layers)
    {
        for (int n = 1; ; n++)
            if (layers.All(l => l.Id != "layer_" + n)) return "layer_" + n;
    }

    private void SaveAsCloudPrefabPopup()
    {
        if (_openSaveAsCloudPrefab) { ImGui.OpenPopup("Save as a new cloud prefab"); _openSaveAsCloudPrefab = false; }
        CenterNextWindow();
        if (!ImGui.BeginPopupModal("Save as a new cloud prefab", ImGuiWindowFlags.AlwaysAutoResize)) return;
        Ui.Hint("Saves these layers as a new prefab (the original stays as it was). This floor then uses the new one.");
        ImGui.SetNextItemWidth(300);
        if (ImGui.IsWindowAppearing()) ImGui.SetKeyboardFocusHere();
        bool enter = ImGui.InputTextWithHint("##sacp", "Prefab name", ref _cloudPrefabName, 60, ImGuiInputTextFlags.EnterReturnsTrue);
        bool ok = _cloudPrefabName.Trim().Length > 0 && _saveAsEntry is not null;
        ImGui.BeginDisabled(!ok);
        if (Ui.AccentButton("Save", new Num.Vector2(120, 0)) || (enter && ok))
        {
            var e = _saveAsEntry!;
            var p = new CloudPrefabDef { Name = _cloudPrefabName.Trim(), Layers = (PrefabLayersOf(e) ?? new()).Select(l => l.Copy()).ToList() };
            p.Id = Services.Defs.SuggestId(typeof(CloudPrefabDef), p.Name);
            Services.Defs.Save(p);
            e.Prefab = p.Id;
            e.Name = p.Name;
            e.Layers = null;
            Edited();
            Status($"Saved new cloud prefab '{p.Name}'. This floor now uses it.");
            ImGui.CloseCurrentPopup();
        }
        ImGui.EndDisabled();
        ImGui.SameLine();
        if (ImGui.Button("Cancel", new Num.Vector2(100, 0))) ImGui.CloseCurrentPopup();
        ImGui.EndPopup();
    }

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
