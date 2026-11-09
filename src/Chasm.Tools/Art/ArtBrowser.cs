using Chasm.Game.Hosting;
using Chasm.Tools.ImGuiHost;
using Chasm.Tools.MapEditor;
using ImGuiNET;
using Num = System.Numerics;

namespace Chasm.Tools.Art;

/// <summary>
/// The art library window: content/art shown as folders (left) and thumbnails (right).
/// Drag textures or folders onto a folder to move them; right-click for rename / new folder /
/// delete. Moving or renaming updates every tile, floor and other content that uses the art.
/// Opened for picking (from any texture field) or just for organising (Tools menu).
/// </summary>
public static class ArtBrowser
{
    private const string PopupName = "Art library";
    private const string FilePayload = "ART_FILE", FolderPayload = "ART_FOLDER";

    private static bool _open, _requestOpen, _importOnOpen;
    private static Action<string>? _pick;
    private static string _folder = "";
    private static string? _selected;
    private static string _search = "";
    private static int _sort; // 0 newest, 1 oldest, 2 name
    private static float _thumb = 112;
    private static string? _error;
    private static string? _info;
    private static List<ArtLibrary.FileInfoRow>? _files;
    private static double _filesAge;

    // Drag and drop (ImGui payloads carry no managed data, so the key lives here).
    private static string? _dragFile, _dragFolder;

    // Rename / new folder / delete state.
    private static string? _renamingFolder;
    private static string _renameText = "";
    private static string? _newFolderParent;
    private static string _newFolderName = "";
    private static string? _deleteKey;
    private static List<string> _deleteUses = new();
    private static bool _openDelete;
    private static string _fileName = "";
    private static string? _fileNameFor;
    private static bool _focusName;

    /// <summary>Opens the library to choose a texture. <paramref name="addNew"/> jumps straight to "Add new texture".</summary>
    public static void OpenPick(string? current, Action<string> picked, bool addNew = false)
    {
        _pick = picked;
        _selected = string.IsNullOrEmpty(current) ? null : current;
        if (_selected is not null) _folder = ArtLibrary.ParentOf(_selected);
        _importOnOpen = addNew;
        Begin();
    }

    /// <summary>Opens the library just for organising.</summary>
    public static void OpenManage()
    {
        _pick = null;
        _importOnOpen = false;
        Begin();
    }

    private static void Begin()
    {
        _requestOpen = true;
        _error = null; _info = null;
        _files = null;
        _search = "";
    }

    private static void Invalidate() => _files = null;

    /// <summary>Call once per frame from the tool shell.</summary>
    public static void Draw(GameServices s)
    {
        if (_requestOpen) { ImGui.OpenPopup(PopupName); _requestOpen = false; _open = true; }
        if (!_open) return;
        ImGui.SetNextWindowSize(Ui.FitSize(1240, 780), ImGuiCond.Appearing);
        ImGui.SetNextWindowPos(ImGui.GetIO().DisplaySize / 2, ImGuiCond.Appearing, new Num.Vector2(0.5f, 0.5f));
        if (!ImGui.BeginPopupModal(PopupName, ref _open)) { _dragFile = _dragFolder = null; return; }

        if (_importOnOpen) { _importOnOpen = false; StartImport(); }
        if (!Directory.Exists(ArtLibrary.FullPath(s, _folder))) _folder = "";
        _filesAge += ImGui.GetIO().DeltaTime;
        if (_files is null || _filesAge > 2) { _files = LoadFiles(s); _filesAge = 0; }

        Toolbar(s);
        float bottom = 92;
        ImGui.BeginChild("tree", new Num.Vector2(270, -bottom), ImGuiChildFlags.Borders | ImGuiChildFlags.ResizeX);
        FolderTree(s);
        ImGui.EndChild();
        ImGui.SameLine();
        ImGui.BeginChild("grid", new Num.Vector2(0, -bottom), ImGuiChildFlags.Borders);
        Grid(s);
        ImGui.EndChild();
        Footer(s);

        ImportDialog.Draw(s);
        DeletePopup(s);
        if (!ImGui.IsMouseDown(ImGuiMouseButton.Left)) _dragFile = _dragFolder = null;
        ImGui.EndPopup();
    }

    private static List<ArtLibrary.FileInfoRow> LoadFiles(GameServices s)
    {
        var list = ArtLibrary.Files(s, _search.Length > 0 ? "" : _folder, recursive: _search.Length > 0);
        if (_search.Length > 0) list = list.Where(f => f.Key.Contains(_search, StringComparison.OrdinalIgnoreCase)).ToList();
        return _sort switch
        {
            1 => list.OrderBy(f => f.Added).ToList(),
            2 => list.OrderBy(f => f.Name, StringComparer.OrdinalIgnoreCase).ToList(),
            _ => list.OrderByDescending(f => f.Added).ToList(),
        };
    }

    private static void StartImport() =>
        ImportDialog.Open(_folder, keys =>
        {
            Invalidate();
            if (keys.Count == 0) return;
            _selected = keys[0];
            _folder = ArtLibrary.ParentOf(keys[0]);
            _info = keys.Count == 1 ? $"Added {keys[0]}." : $"Added {keys.Count} textures to {(_folder.Length == 0 ? "the art root" : _folder)}.";
        });

    // ------------------------------------------------------------------ toolbar

    private static void Toolbar(GameServices s)
    {
        if (Ui.AccentButton("+ Add new texture...")) StartImport();
        if (ImGui.IsItemHovered()) ImGui.SetTooltip("Pick a PNG from your computer, name it, choose its folder.");
        ImGui.SameLine();
        if (ImGui.Button("New folder")) BeginNewFolder(_folder);
        if (ImGui.IsItemHovered()) ImGui.SetTooltip($"Create a folder inside {(_folder.Length == 0 ? "the art root" : _folder)}");
        ImGui.SameLine();
        if (ImGui.Button("Open in Explorer")) OpenInShell(ArtLibrary.FullPath(s, _folder));
        ImGui.SameLine();
        ImGui.SetNextItemWidth(240);
        if (ImGui.InputTextWithHint("##search", "search all art...", ref _search, 80)) Invalidate();
        ImGui.SameLine();
        ImGui.SetNextItemWidth(170);
        string[] sorts = { "Newest first", "Oldest first", "Name (A-Z)" };
        if (ImGui.Combo("##sort", ref _sort, sorts, sorts.Length)) Invalidate();
        ImGui.SameLine();
        ImGui.SetNextItemWidth(140);
        ImGui.SliderFloat("##size", ref _thumb, 64, 220, "%.0f px");
        if (_newFolderParent is not null)
        {
            ImGui.TextUnformatted($"New folder in {(_newFolderParent.Length == 0 ? "art root" : _newFolderParent)}:");
            ImGui.SameLine();
            ImGui.SetNextItemWidth(240);
            if (ImGui.IsWindowAppearing() || !ImGui.IsAnyItemActive()) ImGui.SetKeyboardFocusHere();
            bool enter = ImGui.InputTextWithHint("##nf", "folder name", ref _newFolderName, 60, ImGuiInputTextFlags.EnterReturnsTrue);
            ImGui.SameLine();
            if ((ImGui.Button("Create") || enter) && _newFolderName.Trim().Length > 0)
                Try(() => { _folder = ArtLibrary.CreateFolder(s, _newFolderParent, _newFolderName); _newFolderParent = null; Invalidate(); });
            ImGui.SameLine();
            if (ImGui.Button("Cancel##nf") || ImGui.IsKeyPressed(ImGuiKey.Escape)) _newFolderParent = null;
        }
    }

    private static void BeginNewFolder(string parent) { _newFolderParent = parent; _newFolderName = ""; }

    // ------------------------------------------------------------------ folder tree

    private static void FolderTree(GameServices s)
    {
        FolderNode(s, "", "art (all)");
    }

    private static void FolderNode(GameServices s, string folder, string label)
    {
        var subs = ArtLibrary.SubFolders(s, folder);
        var flags = ImGuiTreeNodeFlags.OpenOnArrow | ImGuiTreeNodeFlags.OpenOnDoubleClick | ImGuiTreeNodeFlags.SpanAvailWidth;
        if (subs.Count == 0) flags |= ImGuiTreeNodeFlags.Leaf;
        if (folder.Length == 0 || (_folder + "/").StartsWith(folder + "/", StringComparison.OrdinalIgnoreCase)) ImGui.SetNextItemOpen(true, ImGuiCond.Once);
        if (_search.Length == 0 && _folder == folder) flags |= ImGuiTreeNodeFlags.Selected;

        bool renaming = _renamingFolder == folder && folder.Length > 0;
        bool open;
        if (renaming)
        {
            open = ImGui.TreeNodeEx("##" + folder, flags);
            ImGui.SameLine();
            ImGui.SetNextItemWidth(-1);
            if (!ImGui.IsAnyItemActive()) ImGui.SetKeyboardFocusHere();
            if (ImGui.InputText("##rename" + folder, ref _renameText, 60, ImGuiInputTextFlags.EnterReturnsTrue))
            {
                string f = folder;
                Try(() =>
                {
                    string moved = ArtLibrary.MoveFolder(s, f, ArtLibrary.ParentOf(f), _renameText);
                    if ((_folder + "/").StartsWith(f + "/", StringComparison.OrdinalIgnoreCase)) _folder = moved + _folder[f.Length..];
                    _info = $"Renamed to {moved}. Content using it was updated.";
                });
                _renamingFolder = null; Invalidate();
            }
            if (ImGui.IsKeyPressed(ImGuiKey.Escape)) _renamingFolder = null;
        }
        else
        {
            open = ImGui.TreeNodeEx(label + "##" + folder, flags);
            if (ImGui.IsItemClicked() && !ImGui.IsItemToggledOpen()) { _folder = folder; _search = ""; Invalidate(); }
            if (folder.Length > 0 && ImGui.BeginDragDropSource())
            {
                _dragFolder = folder; _dragFile = null;
                ImGui.SetDragDropPayload(FolderPayload, IntPtr.Zero, 0);
                ImGui.TextUnformatted("Move folder " + folder);
                ImGui.EndDragDropSource();
            }
            DropTarget(s, folder);
            if (ImGui.BeginPopupContextItem("ctx" + folder))
            {
                if (ImGui.MenuItem("New folder inside...")) BeginNewFolder(folder);
                if (folder.Length > 0)
                {
                    if (ImGui.MenuItem("Rename...")) { _renamingFolder = folder; _renameText = ArtLibrary.NameOf(folder); }
                    if (ImGui.MenuItem("Move to the art root", null, false, ArtLibrary.ParentOf(folder).Length > 0))
                        Try(() => { _folder = ArtLibrary.MoveFolder(s, folder, ""); Invalidate(); });
                    bool empty = !Directory.EnumerateFileSystemEntries(ArtLibrary.FullPath(s, folder)).Any();
                    if (ImGui.MenuItem("Delete (empty folders only)", null, false, empty))
                        Try(() => { ArtLibrary.DeleteFolder(s, folder); if (_folder == folder) _folder = ArtLibrary.ParentOf(folder); Invalidate(); });
                }
                if (ImGui.MenuItem("Open in Explorer")) OpenInShell(ArtLibrary.FullPath(s, folder));
                ImGui.EndPopup();
            }
        }
        if (!open) return;
        foreach (var sub in subs) FolderNode(s, sub, ArtLibrary.NameOf(sub));
        ImGui.TreePop();
    }

    /// <summary>Lets textures and folders be dropped on the last item (a folder).</summary>
    private static void DropTarget(GameServices s, string folder)
    {
        if (!ImGui.BeginDragDropTarget()) return;
        unsafe
        {
            if (_dragFile is { } file && ImGui.AcceptDragDropPayload(FilePayload).NativePtr != null)
            {
                Try(() =>
                {
                    string moved = ArtLibrary.MoveFile(s, file, folder);
                    if (_selected == file) _selected = moved;
                    _info = $"Moved to {moved}.";
                });
                _dragFile = null; Invalidate();
            }
            else if (_dragFolder is { } dragged && ImGui.AcceptDragDropPayload(FolderPayload).NativePtr != null)
            {
                Try(() =>
                {
                    string moved = ArtLibrary.MoveFolder(s, dragged, folder);
                    if ((_folder + "/").StartsWith(dragged + "/", StringComparison.OrdinalIgnoreCase)) _folder = moved + _folder[dragged.Length..];
                    _info = $"Moved folder to {moved}.";
                });
                _dragFolder = null; Invalidate();
            }
        }
        ImGui.EndDragDropTarget();
    }

    // ------------------------------------------------------------------ thumbnails

    private static void Grid(GameServices s)
    {
        if (_search.Length > 0) Ui.Colored(Ui.Muted, $"Search results for \"{_search}\" in all folders ({_files!.Count})");
        else
        {
            // Breadcrumb: each part is clickable and a drop target.
            if (ImGui.SmallButton("art")) { _folder = ""; Invalidate(); }
            DropTarget(s, "");
            string acc = "";
            foreach (var part in _folder.Split('/', StringSplitOptions.RemoveEmptyEntries))
            {
                acc = ArtLibrary.Join(acc, part);
                ImGui.SameLine(); ImGui.TextDisabled("/"); ImGui.SameLine();
                string target = acc;
                if (ImGui.SmallButton(part + "##bc" + acc)) { _folder = target; Invalidate(); }
                DropTarget(s, target);
            }
            ImGui.SameLine();
            Ui.Colored(Ui.Muted, $"   {_files!.Count} texture(s)   drag onto a folder to move");
        }
        ImGui.Separator();

        // Sub-folders of the current folder as big drop targets.
        if (_search.Length == 0)
        {
            var subs = ArtLibrary.SubFolders(s, _folder);
            foreach (var sub in subs)
            {
                if (ImGui.Button("[ ] " + ArtLibrary.NameOf(sub) + "##open" + sub)) { _folder = sub; Invalidate(); }
                DropTarget(s, sub);
                ImGui.SameLine();
                if (ImGui.GetContentRegionAvail().X < 160) ImGui.NewLine();
            }
            if (subs.Count > 0) { ImGui.NewLine(); ImGui.Separator(); }
        }

        if (_files!.Count == 0)
        {
            ImGui.TextDisabled(_search.Length > 0 ? "No art matches." : "No textures in this folder. Use '+ Add new texture...' or drag some in from another folder.");
            return;
        }

        float cell = _thumb + 12;
        int cols = Math.Max(1, (int)((ImGui.GetContentRegionAvail().X + 6) / (cell + 6)));
        var dl = ImGui.GetWindowDrawList();
        int i = 0;
        foreach (var f in _files)
        {
            if (i % cols != 0) ImGui.SameLine();
            i++;
            ImGui.PushID(f.Key);
            var p = ImGui.GetCursorScreenPos();
            bool sel = _selected == f.Key;
            if (ImGui.Selectable("##cell", sel, ImGuiSelectableFlags.AllowDoubleClick, new Num.Vector2(cell, cell + 20)))
            {
                _selected = f.Key;
                if (ImGui.IsMouseDoubleClicked(ImGuiMouseButton.Left) && _pick is not null) { Choose(f.Key); ImGui.PopID(); return; }
            }
            bool hovered = ImGui.IsItemHovered();
            if (ImGui.BeginDragDropSource())
            {
                _dragFile = f.Key; _dragFolder = null;
                ImGui.SetDragDropPayload(FilePayload, IntPtr.Zero, 0);
                DrawImage(s, f.Key, 96);
                ImGui.TextUnformatted("Move " + f.Name);
                ImGui.EndDragDropSource();
            }
            if (ImGui.BeginPopupContextItem("ctx"))
            {
                _selected = f.Key;
                if (_pick is not null && ImGui.MenuItem("Use this texture")) Choose(f.Key);
                if (ImGui.MenuItem("Rename...")) { _fileNameFor = null; _focusName = true; }
                if (ImGui.BeginMenu("Move to"))
                {
                    if (ImGui.MenuItem("(art root)", null, false, ArtLibrary.ParentOf(f.Key).Length > 0)) MoveSelected(s, f.Key, "");
                    foreach (var folder in ArtLibrary.Folders(s))
                        if (ImGui.MenuItem(folder, null, false, folder != ArtLibrary.ParentOf(f.Key))) MoveSelected(s, f.Key, folder);
                    ImGui.EndMenu();
                }
                if (ImGui.MenuItem("Show in Explorer")) OpenInShell(ArtLibrary.FullPath(s, ArtLibrary.ParentOf(f.Key)));
                ImGui.Separator();
                if (ImGui.MenuItem("Delete...")) AskDelete(s, f.Key);
                ImGui.EndPopup();
            }
            // Thumbnail + name drawn over the selectable.
            var tex = s.Textures.Get(f.Key);
            float scale = _thumb / Math.Max(tex.Width, tex.Height);
            var size = new Num.Vector2(tex.Width * scale, tex.Height * scale);
            var tl = p + new Num.Vector2(6 + (_thumb - size.X) / 2, 6 + (_thumb - size.Y) / 2);
            dl.AddRectFilled(p + new Num.Vector2(6, 6), p + new Num.Vector2(6 + _thumb, 6 + _thumb), ImGui.GetColorU32(ImGuiCol.FrameBg), 4);
            dl.AddImage(ImGuiExtension.Instance!.Renderer.BindTexture(tex), tl, tl + size);
            string name = Path.GetFileNameWithoutExtension(f.Name);
            while (name.Length > 3 && ImGui.CalcTextSize(name).X > cell - 6) name = name[..^2];
            if (name.Length < Path.GetFileNameWithoutExtension(f.Name).Length) name += "..";
            dl.AddText(p + new Num.Vector2((cell - ImGui.CalcTextSize(name).X) / 2, cell + 1), ImGui.GetColorU32(sel ? Ui.Accent : new Num.Vector4(0.85f, 0.87f, 0.92f, 1)), name);
            if (hovered && _dragFile is null)
            {
                ImGui.BeginTooltip();
                ImGui.TextUnformatted(f.Key);
                Ui.Colored(Ui.Muted, $"{tex.Width} x {tex.Height}   {ArtLibrary.FormatSize(f.Bytes)}   added {ArtLibrary.FormatDate(f.Added)}");
                ImGui.EndTooltip();
            }
            ImGui.PopID();
        }
    }

    private static void MoveSelected(GameServices s, string key, string folder) =>
        Try(() =>
        {
            string moved = ArtLibrary.MoveFile(s, key, folder);
            _selected = moved;
            _info = $"Moved to {moved}. Content using it was updated.";
            Invalidate();
        });

    private static void DrawImage(GameServices s, string key, float max)
    {
        var tex = s.Textures.Get(key);
        float scale = Math.Min(1f, max / Math.Max(tex.Width, tex.Height));
        ImGui.Image(ImGuiExtension.Instance!.Renderer.BindTexture(tex), new Num.Vector2(tex.Width * scale, tex.Height * scale));
    }

    // ------------------------------------------------------------------ footer: selected texture details

    private static void Footer(GameServices s)
    {
        ImGui.BeginChild("##footer", new Num.Vector2(0, 0));
        if (_selected is { } key && File.Exists(ArtLibrary.FullPath(s, key)))
        {
            if (_fileNameFor != key) { _fileNameFor = key; _fileName = Path.GetFileNameWithoutExtension(key); }
            DrawImage(s, key, 56);
            ImGui.SameLine();
            ImGui.BeginGroup();
            ImGui.TextUnformatted("Name");
            ImGui.SameLine();
            ImGui.SetNextItemWidth(260);
            if (_focusName) { ImGui.SetKeyboardFocusHere(); _focusName = false; }
            bool enter = ImGui.InputText("##filename", ref _fileName, 100, ImGuiInputTextFlags.EnterReturnsTrue);
            ImGui.SameLine();
            ImGui.TextDisabled(".png");
            ImGui.SameLine();
            bool changedName = _fileName.Trim() != Path.GetFileNameWithoutExtension(key);
            ImGui.BeginDisabled(!changedName);
            if (ImGui.Button("Rename") || (enter && changedName))
                Try(() =>
                {
                    string renamed = ArtLibrary.RenameFile(s, key, _fileName);
                    _selected = renamed; _fileNameFor = null;
                    _info = $"Renamed to {renamed}. Content using it was updated.";
                    Invalidate();
                });
            ImGui.EndDisabled();
            var row = ArtLibrary.Row(s, ArtLibrary.FullPath(s, key));
            var tex = s.Textures.Get(key);
            Ui.Colored(Ui.Muted, $"{key}    {tex.Width} x {tex.Height}    {ArtLibrary.FormatSize(row.Bytes)}    added {ArtLibrary.FormatDate(row.Added)}");
            ImGui.EndGroup();
        }
        else ImGui.TextDisabled("Select a texture to see its details. Right-click textures and folders for more.");

        if (_error is not null) Ui.Colored(Ui.Bad, _error);
        else if (_info is not null) Ui.Colored(Ui.Good, _info);

        float right = ImGui.GetWindowWidth() - (_pick is not null ? 330 : 130);
        ImGui.SetCursorPos(new Num.Vector2(right, 8));
        if (_pick is not null)
        {
            ImGui.BeginDisabled(_selected is null);
            if (Ui.AccentButton("Use this texture", new Num.Vector2(190, 36))) Choose(_selected!);
            ImGui.EndDisabled();
            ImGui.SameLine();
        }
        if (ImGui.Button(_pick is null ? "Close" : "Cancel", new Num.Vector2(120, 36))) Close();
        ImGui.EndChild();
    }

    private static void Choose(string key)
    {
        _pick?.Invoke(key);
        Close();
    }

    private static void Close()
    {
        _open = false;
        _pick = null;
        ImGui.CloseCurrentPopup();
    }

    // ------------------------------------------------------------------ delete

    private static void AskDelete(GameServices s, string key)
    {
        _deleteKey = key;
        _deleteUses = ArtLibrary.UsedBy(s, key);
        _openDelete = true;
    }

    private static void DeletePopup(GameServices s)
    {
        if (_openDelete) { ImGui.OpenPopup("Delete texture?"); _openDelete = false; }
        if (!ImGui.BeginPopupModal("Delete texture?", ImGuiWindowFlags.AlwaysAutoResize)) return;
        Ui.Title($"Delete {Path.GetFileName(_deleteKey)}?");
        Ui.Colored(Ui.Muted, $"content/art/{_deleteKey}");
        if (_deleteUses.Count == 0) Ui.Colored(Ui.Good, "Nothing uses this texture.");
        else
        {
            Ui.Colored(Ui.Warn, $"Used by {_deleteUses.Count} thing(s); they'll show the 'missing art' checkerboard:");
            foreach (var u in _deleteUses.Take(10)) ImGui.BulletText(u);
        }
        ImGui.PushStyleColor(ImGuiCol.Button, new Num.Vector4(0.55f, 0.16f, 0.16f, 1));
        bool del = ImGui.Button("Delete file", new Num.Vector2(150, 0));
        ImGui.PopStyleColor();
        ImGui.SameLine();
        if (ImGui.Button("Cancel", new Num.Vector2(150, 0))) ImGui.CloseCurrentPopup();
        if (del)
        {
            string key = _deleteKey!;
            Try(() => { ArtLibrary.DeleteFile(s, key); if (_selected == key) _selected = null; _info = $"Deleted {key}."; Invalidate(); });
            ImGui.CloseCurrentPopup();
        }
        ImGui.EndPopup();
    }

    // ------------------------------------------------------------------ helpers

    private static void Try(Action a)
    {
        try { a(); _error = null; }
        catch (Exception e) { _error = e.Message; _info = null; }
    }

    private static void OpenInShell(string path)
    {
        try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo { FileName = path, UseShellExecute = true }); }
        catch (Exception e) { _error = $"Couldn't open {path}: {e.Message}"; }
    }
}
