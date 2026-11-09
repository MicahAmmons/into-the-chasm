using Chasm.Game.Hosting;
using Chasm.Tools.Art;
using Chasm.Tools.ImGuiHost;
using ImGuiNET;
using Microsoft.Xna.Framework.Graphics;
using Num = System.Numerics;

namespace Chasm.Tools.MapEditor;

/// <summary>
/// "Add new texture": browse your computer (starts in Downloads, newest first, Explorer-style
/// columns), pick one or more PNGs, name the asset, choose (or create) the library folder, import.
/// Files are copied into content/art/&lt;folder&gt;/. No native dialog needed, so it works everywhere.
/// </summary>
public static class ImportDialog
{
    private const string PopupName = "Add new texture";

    private sealed record Entry(string Path, string Name, bool IsDir, DateTime Added, DateTime Modified, long Bytes);

    private static bool _open, _requestOpen;
    private static string _dir = DefaultDir();
    private static string _dirInput = _dir;
    private static List<Entry>? _entries;
    private static string? _listError;
    private static readonly List<string> _selected = new();
    private static string _assetName = "";
    private static string _folder = "props";
    private static string _newFolder = "";
    private static bool _makingFolder;
    private static string? _error;
    private static Action<List<string>>? _imported;
    private static int _sortColumn = 1;
    private static bool _sortDescending = true;
    private static string _filter = "";

    private static Texture2D? _preview;
    private static string? _previewPath;

    /// <summary>The user's Downloads folder (falls back to their home folder).</summary>
    private static string DefaultDir()
    {
        string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        string downloads = System.IO.Path.Combine(home, "Downloads");
        return Directory.Exists(downloads) ? downloads : home;
    }

    /// <summary>Opens the dialog. <paramref name="folder"/> is the suggested library folder.</summary>
    public static void Open(string? folder, Action<List<string>> imported)
    {
        _requestOpen = true;
        _error = null;
        _selected.Clear();
        _assetName = "";
        _imported = imported;
        if (folder is not null) _folder = folder;
        _entries = null;
    }

    public static bool IsOpen => _open || _requestOpen;

    private static void Go(string dir)
    {
        _dir = dir;
        _dirInput = dir;
        _entries = null;
        _selected.Clear();
        _assetName = "";
    }

    private static void Refresh()
    {
        _listError = null;
        var list = new List<Entry>();
        try
        {
            foreach (var d in Directory.EnumerateDirectories(_dir))
            {
                var di = new DirectoryInfo(d);
                if (di.Name.StartsWith('.') || (di.Attributes & (FileAttributes.Hidden | FileAttributes.System)) != 0) continue;
                list.Add(new Entry(d, di.Name, true, ArtLibrary.Added(di), di.LastWriteTime, 0));
            }
            foreach (var f in Directory.EnumerateFiles(_dir, "*.png"))
            {
                var fi = new FileInfo(f);
                list.Add(new Entry(f, fi.Name, false, ArtLibrary.Added(fi), fi.LastWriteTime, fi.Length));
            }
        }
        catch (Exception e) { _listError = e.Message; }
        _entries = list;
        Sort();
    }

    private static void Sort()
    {
        if (_entries is null) return;
        IEnumerable<Entry> Order(IEnumerable<Entry> e) => _sortColumn switch
        {
            1 => _sortDescending ? e.OrderByDescending(x => x.Added) : e.OrderBy(x => x.Added),
            2 => _sortDescending ? e.OrderByDescending(x => x.Modified) : e.OrderBy(x => x.Modified),
            3 => _sortDescending ? e.OrderByDescending(x => x.Bytes) : e.OrderBy(x => x.Bytes),
            _ => _sortDescending ? e.OrderByDescending(x => x.Name, StringComparer.OrdinalIgnoreCase) : e.OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase),
        };
        // Folders first, like Explorer.
        _entries = Order(_entries.Where(e => e.IsDir)).Concat(Order(_entries.Where(e => !e.IsDir))).ToList();
    }

    public static void Draw(GameServices s)
    {
        if (_requestOpen) { ImGui.OpenPopup(PopupName); _requestOpen = false; _open = true; }
        if (!_open) { DisposePreview(); return; }
        ImGui.SetNextWindowSize(Ui.FitSize(1180, 720), ImGuiCond.Appearing);
        ImGui.SetNextWindowPos(ImGui.GetIO().DisplaySize / 2, ImGuiCond.Appearing, new Num.Vector2(0.5f, 0.5f));
        if (!ImGui.BeginPopupModal(PopupName, ref _open)) { DisposePreview(); return; }
        if (_entries is null) Refresh();

        PlacesBar();
        float bottom = 180;
        float previewW = 280;
        ImGui.BeginChild("files", new Num.Vector2(-previewW - 8, -bottom), ImGuiChildFlags.Borders);
        FileTable();
        ImGui.EndChild();
        ImGui.SameLine();
        ImGui.BeginChild("preview", new Num.Vector2(previewW, -bottom), ImGuiChildFlags.Borders);
        PreviewPane(s);
        ImGui.EndChild();
        Destination(s);
        ImGui.EndPopup();
    }

    private static void PlacesBar()
    {
        void Place(string label, string path)
        {
            if (Directory.Exists(path) && ImGui.Button(label)) Go(path);
            ImGui.SameLine();
        }
        string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        Place("Downloads", System.IO.Path.Combine(home, "Downloads"));
        Place("Desktop", Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory));
        Place("Pictures", Environment.GetFolderPath(Environment.SpecialFolder.MyPictures));
        Place("Documents", Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments));
        Place("Home", home);
        if (OperatingSystem.IsWindows())
            foreach (var d in DriveInfo.GetDrives().Where(d => d.IsReady)) Place(d.Name.TrimEnd('\\'), d.RootDirectory.FullName);
        ImGui.BeginDisabled(Directory.GetParent(_dir) is null);
        if (ImGui.Button("Up")) Go(Directory.GetParent(_dir)!.FullName);
        ImGui.EndDisabled();
        if (ImGui.IsItemHovered()) ImGui.SetTooltip("Parent folder (Backspace)");
        ImGui.SameLine();
        if (ImGui.Button("Refresh")) _entries = null;

        ImGui.SetNextItemWidth(-220);
        if (ImGui.InputText("##dir", ref _dirInput, 500, ImGuiInputTextFlags.EnterReturnsTrue) && Directory.Exists(_dirInput)) Go(_dirInput);
        if (ImGui.IsItemDeactivated() && !Directory.Exists(_dirInput)) _dirInput = _dir;
        ImGui.SameLine();
        ImGui.SetNextItemWidth(-1);
        ImGui.InputTextWithHint("##filter", "filter this folder...", ref _filter, 80);

        if (!ImGui.GetIO().WantTextInput && ImGui.IsKeyPressed(ImGuiKey.Backspace) && Directory.GetParent(_dir) is { } up) Go(up.FullName);
    }

    private static void FileTable()
    {
        if (_listError is not null) { Ui.Colored(Ui.Bad, _listError); return; }
        var flags = ImGuiTableFlags.Sortable | ImGuiTableFlags.RowBg | ImGuiTableFlags.ScrollY | ImGuiTableFlags.Resizable | ImGuiTableFlags.BordersInnerV | ImGuiTableFlags.SizingStretchProp;
        if (!ImGui.BeginTable("files", 5, flags)) return;
        ImGui.TableSetupScrollFreeze(0, 1);
        ImGui.TableSetupColumn("Name", ImGuiTableColumnFlags.WidthStretch, 3.2f);
        ImGui.TableSetupColumn("Date added", ImGuiTableColumnFlags.WidthStretch | ImGuiTableColumnFlags.DefaultSort | ImGuiTableColumnFlags.PreferSortDescending, 1.6f);
        ImGui.TableSetupColumn("Date modified", ImGuiTableColumnFlags.WidthStretch | ImGuiTableColumnFlags.PreferSortDescending, 1.6f);
        ImGui.TableSetupColumn("Size", ImGuiTableColumnFlags.WidthStretch | ImGuiTableColumnFlags.PreferSortDescending, 0.8f);
        ImGui.TableSetupColumn("Type", ImGuiTableColumnFlags.WidthStretch | ImGuiTableColumnFlags.NoSort, 0.9f);
        ImGui.TableHeadersRow();

        var specs = ImGui.TableGetSortSpecs();
        unsafe
        {
            if (specs.NativePtr != null && specs.SpecsDirty && specs.SpecsCount > 0)
            {
                _sortColumn = specs.Specs.ColumnIndex;
                _sortDescending = specs.Specs.SortDirection == ImGuiSortDirection.Descending;
                specs.SpecsDirty = false;
                Sort();
            }
        }

        bool ctrl = ImGui.GetIO().KeyCtrl;
        foreach (var e in _entries!.ToList())
        {
            if (_filter.Length > 0 && !e.Name.Contains(_filter, StringComparison.OrdinalIgnoreCase)) continue;
            ImGui.TableNextRow();
            ImGui.TableNextColumn();
            bool selected = _selected.Contains(e.Path);
            string label = (e.IsDir ? "[folder]  " : "") + e.Name;
            if (e.IsDir) ImGui.PushStyleColor(ImGuiCol.Text, Ui.Accent);
            bool clicked = ImGui.Selectable(label + "##" + e.Path, selected, ImGuiSelectableFlags.SpanAllColumns | ImGuiSelectableFlags.AllowDoubleClick);
            if (e.IsDir) ImGui.PopStyleColor();
            if (clicked)
            {
                bool dbl = ImGui.IsMouseDoubleClicked(ImGuiMouseButton.Left);
                if (e.IsDir) { if (dbl) { Go(e.Path); ImGui.TableNextColumn(); break; } }
                else if (ctrl)
                {
                    if (selected) _selected.Remove(e.Path); else _selected.Add(e.Path);
                }
                else
                {
                    _selected.Clear();
                    _selected.Add(e.Path);
                    _assetName = System.IO.Path.GetFileNameWithoutExtension(e.Path);
                }
            }
            if (e.IsDir && ImGui.IsItemHovered()) ImGui.SetTooltip("Double-click to open");
            ImGui.TableNextColumn(); ImGui.TextUnformatted(ArtLibrary.FormatDate(e.Added));
            ImGui.TableNextColumn(); ImGui.TextUnformatted(ArtLibrary.FormatDate(e.Modified));
            ImGui.TableNextColumn(); ImGui.TextUnformatted(e.IsDir ? "" : ArtLibrary.FormatSize(e.Bytes));
            ImGui.TableNextColumn(); ImGui.TextUnformatted(e.IsDir ? "File folder" : "PNG image");
        }
        if (_entries.Count == 0) { ImGui.TableNextRow(); ImGui.TableNextColumn(); ImGui.TextDisabled("No folders or PNG files here."); }
        ImGui.EndTable();
    }

    private static void PreviewPane(GameServices s)
    {
        if (_selected.Count == 0) { ImGui.TextDisabled("Click a PNG to preview it.\nCtrl+click to pick several."); return; }
        if (_selected.Count > 1)
        {
            Ui.Colored(Ui.Accent, $"{_selected.Count} files selected");
            foreach (var f in _selected) ImGui.BulletText(System.IO.Path.GetFileName(f));
            return;
        }
        string path = _selected[0];
        if (_previewPath != path)
        {
            DisposePreview();
            _previewPath = path;
            try
            {
                using var stream = File.OpenRead(path);
                _preview = Texture2D.FromStream(s.Graphics, stream, DefaultColorProcessors.PremultiplyAlpha);
            }
            catch { _preview = null; }
        }
        if (_preview is null) { Ui.Colored(Ui.Bad, "Couldn't read this image."); return; }
        float w = ImGui.GetContentRegionAvail().X;
        float scale = Math.Min(w / _preview.Width, 240f / _preview.Height);
        var id = ImGuiExtension.Instance!.Renderer.BindTexture(_preview);
        var p = ImGui.GetCursorScreenPos();
        ImGui.GetWindowDrawList().AddRectFilled(p, p + new Num.Vector2(_preview.Width * scale, _preview.Height * scale), ImGui.GetColorU32(ImGuiCol.FrameBg));
        ImGui.Image(id, new Num.Vector2(_preview.Width * scale, _preview.Height * scale));
        ImGui.TextWrapped(System.IO.Path.GetFileName(path));
        Ui.Colored(Ui.Muted, $"{_preview.Width} x {_preview.Height}   {ArtLibrary.FormatSize(new FileInfo(path).Length)}");
        if (_preview.Width > 2048 || _preview.Height > 2048) Ui.Colored(Ui.Warn, "Large image: consider scaling it down.");
    }

    private static void DisposePreview()
    {
        _preview?.Dispose();
        _preview = null;
        _previewPath = null;
    }

    private static void Destination(GameServices s)
    {
        Ui.Section("Add to the art library");
        float labelW = 150;
        // Asset name
        ImGui.TextUnformatted("Asset name"); ImGui.SameLine(labelW);
        ImGui.SetNextItemWidth(380);
        ImGui.BeginDisabled(_selected.Count != 1);
        ImGui.InputTextWithHint("##assetname", _selected.Count > 1 ? "(several files: each keeps its file name)" : "name of the texture", ref _assetName, 100);
        ImGui.EndDisabled();
        ImGui.SameLine(); ImGui.TextDisabled(".png");

        // Folder
        ImGui.TextUnformatted("Library folder"); ImGui.SameLine(labelW);
        ImGui.SetNextItemWidth(380);
        if (ImGui.BeginCombo("##folder", _folder.Length == 0 ? "(art root)" : _folder, ImGuiComboFlags.HeightLarge))
        {
            if (ImGui.Selectable("(art root)", _folder.Length == 0)) _folder = "";
            foreach (var f in ArtLibrary.Folders(s)) if (ImGui.Selectable(f, f == _folder)) _folder = f;
            ImGui.EndCombo();
        }
        ImGui.SameLine();
        if (!_makingFolder)
        {
            if (ImGui.Button("New folder...")) { _makingFolder = true; _newFolder = ""; }
            if (ImGui.IsItemHovered()) ImGui.SetTooltip($"Create a folder inside {(_folder.Length == 0 ? "the art root" : _folder)}");
        }
        else
        {
            ImGui.SetNextItemWidth(200);
            if (ImGui.IsWindowAppearing() || !ImGui.IsAnyItemActive()) ImGui.SetKeyboardFocusHere();
            bool enter = ImGui.InputTextWithHint("##newfolder", "new folder name", ref _newFolder, 60, ImGuiInputTextFlags.EnterReturnsTrue);
            ImGui.SameLine();
            if ((ImGui.Button("Create") || enter) && _newFolder.Trim().Length > 0)
            {
                try { _folder = ArtLibrary.CreateFolder(s, _folder, _newFolder); _makingFolder = false; _error = null; }
                catch (Exception e) { _error = e.Message; }
            }
            ImGui.SameLine();
            if (ImGui.Button("Cancel##nf")) _makingFolder = false;
        }

        if (_selected.Count == 1)
        {
            string name = ArtLibrary.Clean(_assetName.Length == 0 ? System.IO.Path.GetFileNameWithoutExtension(_selected[0]) : _assetName);
            string final = ArtLibrary.UniqueFileName(s, _folder, name + ".png");
            Ui.Colored(Ui.Muted, $"Will be saved as content/art/{ArtLibrary.Join(_folder, final)}");
            if (!final.Equals(name + ".png", StringComparison.OrdinalIgnoreCase)) { ImGui.SameLine(); Ui.Colored(Ui.Warn, "(name taken, numbered)"); }
        }
        else ImGui.NewLine();
        if (_error is not null) Ui.Colored(Ui.Bad, _error);

        ImGui.BeginDisabled(_selected.Count == 0);
        string label = _selected.Count switch { 0 => "Pick a PNG above", 1 => "Add texture", _ => $"Add {_selected.Count} textures" };
        bool go = Ui.AccentButton(label, new Num.Vector2(220, 34));
        ImGui.EndDisabled();
        ImGui.SameLine();
        if (ImGui.Button("Cancel", new Num.Vector2(120, 34))) { _open = false; ImGui.CloseCurrentPopup(); }
        if (go)
        {
            try
            {
                var keys = new List<string>();
                foreach (var f in _selected)
                    keys.Add(ArtLibrary.Import(s, f, _folder, _selected.Count == 1 && _assetName.Trim().Length > 0 ? _assetName : System.IO.Path.GetFileNameWithoutExtension(f)));
                _imported?.Invoke(keys);
                _open = false;
                DisposePreview();
                ImGui.CloseCurrentPopup();
            }
            catch (Exception e) { _error = e.Message; }
        }
    }
}
