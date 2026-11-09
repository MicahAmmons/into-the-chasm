using Chasm.Game.Hosting;
using ImGuiNET;
using Num = System.Numerics;

namespace Chasm.Tools.MapEditor;

/// <summary>
/// "Import PNG...": browse your computer, pick a PNG, choose a category (folder), and it is copied
/// into content/art/&lt;category&gt;/. Works on Windows, macOS and Linux (no native dialog needed).
/// </summary>
public static class ImportDialog
{
    private static bool _open, _requestOpen;
    private static string _dir = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
    private static string? _file;
    private static string _category = "props";
    private static string? _error;

    public static void Open(GameServices s) { _requestOpen = true; _error = null; _file = null; }

    public static void Draw(GameServices s, Action<string> imported)
    {
        if (_requestOpen) { ImGui.OpenPopup("Import PNG"); _requestOpen = false; _open = true; }
        if (!_open) return;
        ImGui.SetNextWindowSize(new Num.Vector2(720, 560), ImGuiCond.Appearing);
        if (!ImGui.BeginPopupModal("Import PNG", ref _open)) return;

        // Drives / quick places
        if (OperatingSystem.IsWindows())
        {
            foreach (var d in DriveInfo.GetDrives().Where(d => d.IsReady))
            {
                if (ImGui.Button(d.Name)) _dir = d.RootDirectory.FullName;
                ImGui.SameLine();
            }
        }
        if (ImGui.Button("Home")) _dir = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        ImGui.SameLine();
        if (ImGui.Button("Desktop")) _dir = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
        ImGui.SameLine();
        if (ImGui.Button("Pictures")) _dir = Environment.GetFolderPath(Environment.SpecialFolder.MyPictures);
        ImGui.SameLine();
        if (ImGui.Button("Up") && Directory.GetParent(_dir) is { } parent) _dir = parent.FullName;
        ImGui.SetNextItemWidth(-1);
        ImGui.InputText("##dir", ref _dir, 400);

        ImGui.BeginChild("files", new Num.Vector2(0, -110), ImGuiChildFlags.Borders);
        try
        {
            foreach (var d in Directory.EnumerateDirectories(_dir).OrderBy(x => x))
            {
                var name = Path.GetFileName(d);
                if (name.StartsWith('.')) continue;
                if (ImGui.Selectable("[" + name + "]")) { _dir = d; _file = null; }
            }
            foreach (var f in Directory.EnumerateFiles(_dir, "*.png").OrderBy(x => x))
                if (ImGui.Selectable(Path.GetFileName(f), f == _file, ImGuiSelectableFlags.AllowDoubleClick)) _file = f;
        }
        catch (Exception e) { ImGui.TextColored(new Num.Vector4(1, 0.5f, 0.4f, 1), e.Message); }
        ImGui.EndChild();

        var categories = Directory.Exists(s.Paths.Art)
            ? Directory.GetDirectories(s.Paths.Art).Select(Path.GetFileName).Where(n => n is not null).Cast<string>().OrderBy(n => n).ToList()
            : new List<string>();
        ImGui.Text("Category (folder in content/art):");
        ImGui.SameLine();
        ImGui.SetNextItemWidth(180);
        if (ImGui.BeginCombo("##cat", _category))
        {
            foreach (var c in categories) if (ImGui.Selectable(c, c == _category)) _category = c;
            ImGui.EndCombo();
        }
        ImGui.SameLine();
        ImGui.SetNextItemWidth(160);
        ImGui.InputTextWithHint("##newcat", "or type a new one", ref _category, 60);
        if (_error is not null) ImGui.TextColored(new Num.Vector4(1, 0.5f, 0.4f, 1), _error);

        ImGui.BeginDisabled(_file is null || _category.Trim().Length == 0);
        if (ImGui.Button(_file is null ? "Pick a PNG" : $"Import {Path.GetFileName(_file)}", new Num.Vector2(-1, 36)))
        {
            try
            {
                string cat = string.Concat(_category.Trim().Select(ch => Path.GetInvalidFileNameChars().Contains(ch) ? '_' : ch));
                string destDir = Path.Combine(s.Paths.Art, cat);
                Directory.CreateDirectory(destDir);
                string dest = Path.Combine(destDir, Path.GetFileName(_file!));
                File.Copy(_file!, dest, overwrite: true);
                imported(s.Paths.ArtKey(dest));
                _open = false;
                ImGui.CloseCurrentPopup();
            }
            catch (Exception e) { _error = e.Message; }
        }
        ImGui.EndDisabled();
        ImGui.EndPopup();
    }
}
