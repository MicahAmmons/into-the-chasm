using Chasm.Core.Content;
using Chasm.Core.Serialization;
using Chasm.Core.World;
using Chasm.Tools.ImGuiHost;
using ImGuiNET;
using Num = System.Numerics;

namespace Chasm.Tools.MapEditor;

public sealed partial class MapEditorScene
{
    private TileDef? _deleteCandidate;
    private List<string> _deleteReferences = new();
    private bool _openDeletePopup;

    /// <summary>Asks to delete a saved tile (shows what still points at it first).</summary>
    private void AskDelete(TileDef tile)
    {
        _deleteCandidate = tile;
        _deleteReferences = FindReferences(tile.Id);
        _openDeletePopup = true;
    }

    /// <summary>Every other piece of content whose data mentions this tile id (exits, teleports, floors, config...).</summary>
    private List<string> FindReferences(string id)
    {
        var found = new List<string>();
        string needle = $"\"{id}\"";
        foreach (var type in DefRegistry.DefTypes)
            foreach (var def in Services.Defs.All(type))
            {
                if (def is TileDef t && t.Id == id) continue;
                if (ChasmJson.Serialize(def, def.GetType()).Contains(needle, StringComparison.Ordinal))
                    found.Add($"{DefRegistry.FolderOf(type).DisplayName.TrimEnd('s')}: {def.DisplayName} [{def.Id}]");
            }
        return found;
    }

    private void DeletePopup()
    {
        if (_openDeletePopup) { ImGui.OpenPopup("Delete tile?"); _openDeletePopup = false; }
        ImGui.SetNextWindowPos(ImGui.GetIO().DisplaySize / 2, ImGuiCond.Appearing, new Num.Vector2(0.5f, 0.5f));
        ImGui.SetNextWindowSizeConstraints(new Num.Vector2(420, 0), Ui.FitSize(640, 600));
        if (!ImGui.BeginPopupModal("Delete tile?", ImGuiWindowFlags.AlwaysAutoResize)) return;
        var t = _deleteCandidate!;
        Ui.Title($"Delete \"{t.DisplayName}\"?");
        Ui.Colored(Ui.Muted, $"{t.Id}   content/defs/tiles/{t.Id}.json");
        ImGui.Spacing();
        if (_deleteReferences.Count == 0) Ui.Colored(Ui.Good, "Nothing else points to this tile.");
        else
        {
            Ui.Colored(Ui.Warn, $"{_deleteReferences.Count} thing(s) still point to it and will show as missing:");
            foreach (var r in _deleteReferences.Take(12)) ImGui.BulletText(r);
            if (_deleteReferences.Count > 12) Ui.Hint($"...and {_deleteReferences.Count - 12} more.");
        }
        Ui.Hint("Neighbouring tiles (by X/Y) simply stop connecting. A copy is kept under 'Recover recent work' in case you change your mind.");
        ImGui.Spacing();
        ImGui.PushStyleColor(ImGuiCol.Button, new Num.Vector4(0.55f, 0.16f, 0.16f, 1));
        ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new Num.Vector4(0.72f, 0.2f, 0.2f, 1));
        bool delete = ImGui.Button("Delete tile", new Num.Vector2(160, 0));
        ImGui.PopStyleColor(2);
        ImGui.SameLine();
        if (ImGui.Button("Cancel", new Num.Vector2(160, 0))) { _deleteCandidate = null; ImGui.CloseCurrentPopup(); }
        if (delete)
        {
            DeleteTile(t);
            ImGui.CloseCurrentPopup();
        }
        ImGui.EndPopup();
    }

    private void DeleteTile(TileDef t)
    {
        // Safety copy first, so it shows under "Recover recent work".
        var floor = TileLinks.FloorOf(Services.Defs, t);
        EditorRecovery.Write(t.Id, $"{t.DisplayName}  [{t.Id}]  (deleted)",
            ChasmJson.Serialize(new Snapshot { Tile = t, Floor = floor, New = true }));
        Services.Defs.Delete(typeof(TileDef), t.Id);
        if (_reference?.Id == t.Id) _reference = null;
        if (_tile is not null && !_isNewTile && _tile.Id == t.Id) { _tile = null; _history.Clear(); }
        _recoveries = null;
        _deleteCandidate = null;
        Status($"Deleted {t.Id}.");
    }
}
