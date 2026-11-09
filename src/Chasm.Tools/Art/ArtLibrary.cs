using System.Text.Json;
using Chasm.Core.Content;
using Chasm.Core.Serialization;
using Chasm.Game.Hosting;

namespace Chasm.Tools.Art;

/// <summary>
/// File operations on content/art (the art library): folders, moving, renaming, importing.
/// Art is referenced by its path ("key") inside content/art, so whenever a file moves or is renamed,
/// every piece of content that used the old path is rewritten to the new one and saved.
/// </summary>
public static class ArtLibrary
{
    /// <summary>Raised after art keys change (old key -> new key), so open editors can patch unsaved work.</summary>
    public static event Action<IReadOnlyDictionary<string, string>>? KeysMoved;

    public sealed record FileInfoRow(string Key, string Name, string Folder, DateTime Added, DateTime Modified, long Bytes);

    public static string Root(GameServices s) => s.Paths.Art;

    public static string FullPath(GameServices s, string key) => Path.Combine(s.Paths.Art, key);

    /// <summary>"backgrounds/clouds" for a folder key, "" for the root.</summary>
    public static string ParentOf(string key) => key.Contains('/') ? key[..key.LastIndexOf('/')] : "";

    public static string NameOf(string key) => key.Contains('/') ? key[(key.LastIndexOf('/') + 1)..] : key;

    public static string Join(string folder, string name) => folder.Length == 0 ? name : folder + "/" + name;

    /// <summary>All folders (keys), sorted, not including the root.</summary>
    public static List<string> Folders(GameServices s) =>
        Directory.Exists(s.Paths.Art)
            ? Directory.EnumerateDirectories(s.Paths.Art, "*", SearchOption.AllDirectories)
                .Select(s.Paths.ArtKey).Where(k => !k.Split('/').Any(p => p.StartsWith('.')))
                .OrderBy(k => k, StringComparer.OrdinalIgnoreCase).ToList()
            : new();

    public static List<string> SubFolders(GameServices s, string folder)
    {
        string dir = FullPath(s, folder);
        if (!Directory.Exists(dir)) return new();
        return Directory.EnumerateDirectories(dir).Select(s.Paths.ArtKey)
            .Where(k => !NameOf(k).StartsWith('.'))
            .OrderBy(k => k, StringComparer.OrdinalIgnoreCase).ToList();
    }

    public static FileInfoRow Row(GameServices s, string path)
    {
        var fi = new FileInfo(path);
        string key = s.Paths.ArtKey(path);
        return new FileInfoRow(key, fi.Name, ParentOf(key), Added(fi), fi.LastWriteTime, fi.Exists ? fi.Length : 0);
    }

    /// <summary>"Date added": when the file appeared here (creation time; falls back to modified).</summary>
    public static DateTime Added(FileSystemInfo fi)
    {
        var c = fi.CreationTime;
        return c.Year < 1990 ? fi.LastWriteTime : c;
    }

    /// <summary>PNGs directly in a folder (or everywhere when recursive).</summary>
    public static List<FileInfoRow> Files(GameServices s, string folder, bool recursive)
    {
        string dir = FullPath(s, folder);
        if (!Directory.Exists(dir)) return new();
        return Directory.EnumerateFiles(dir, "*.png", recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly)
            .Select(f => Row(s, f)).ToList();
    }

    /// <summary>Makes a name safe for a file or folder (no slashes, colons, etc.).</summary>
    public static string Clean(string name)
    {
        name = string.Concat(name.Trim().Select(ch => Path.GetInvalidFileNameChars().Contains(ch) || ch is '/' or '\\' ? '_' : ch));
        return name.Trim('.', ' ');
    }

    /// <summary>"name.png", or "name (2).png" etc. if that's taken in the folder.</summary>
    public static string UniqueFileName(GameServices s, string folder, string fileName)
    {
        string baseName = Path.GetFileNameWithoutExtension(fileName), ext = Path.GetExtension(fileName);
        string candidate = fileName;
        for (int i = 2; File.Exists(FullPath(s, Join(folder, candidate))); i++) candidate = $"{baseName} ({i}){ext}";
        return candidate;
    }

    public static string CreateFolder(GameServices s, string parent, string name)
    {
        name = Clean(name);
        if (name.Length == 0) throw new InvalidOperationException("Folder name is empty.");
        string key = Join(parent, name);
        Directory.CreateDirectory(FullPath(s, key));
        return key;
    }

    /// <summary>Copies a PNG from anywhere on the computer into the library. Returns its new key.</summary>
    public static string Import(GameServices s, string sourcePath, string folder, string assetName)
    {
        string name = Clean(assetName);
        if (name.EndsWith(".png", StringComparison.OrdinalIgnoreCase)) name = name[..^4];
        if (name.Length == 0) name = Path.GetFileNameWithoutExtension(sourcePath);
        Directory.CreateDirectory(FullPath(s, folder));
        string file = UniqueFileName(s, folder, name + ".png");
        string key = Join(folder, file);
        File.Copy(sourcePath, FullPath(s, key));
        File.SetCreationTime(FullPath(s, key), DateTime.Now); // "date added" = now
        return key;
    }

    /// <summary>Renames a file (keeps it in its folder). Returns the new key.</summary>
    public static string RenameFile(GameServices s, string key, string newName)
    {
        string name = Clean(newName);
        if (name.EndsWith(".png", StringComparison.OrdinalIgnoreCase)) name = name[..^4];
        if (name.Length == 0) throw new InvalidOperationException("Name is empty.");
        string newKey = Join(ParentOf(key), name + ".png");
        if (string.Equals(newKey, key, StringComparison.Ordinal)) return key;
        if (!string.Equals(newKey, key, StringComparison.OrdinalIgnoreCase) && File.Exists(FullPath(s, newKey)))
            throw new InvalidOperationException($"There's already a file called {name}.png here.");
        MoveFiles(s, new Dictionary<string, string> { [key] = newKey });
        return newKey;
    }

    /// <summary>Moves a file into another folder. Returns the new key.</summary>
    public static string MoveFile(GameServices s, string key, string toFolder)
    {
        if (ParentOf(key).Equals(toFolder, StringComparison.OrdinalIgnoreCase)) return key;
        string newKey = Join(toFolder, UniqueFileName(s, toFolder, NameOf(key)));
        MoveFiles(s, new Dictionary<string, string> { [key] = newKey });
        return newKey;
    }

    /// <summary>Moves or renames a folder (everything inside comes along). Returns its new key.</summary>
    public static string MoveFolder(GameServices s, string folder, string toParent, string? newName = null)
    {
        string name = Clean(newName ?? NameOf(folder));
        if (name.Length == 0) throw new InvalidOperationException("Folder name is empty.");
        string newKey = Join(toParent, name);
        if (newKey.Equals(folder, StringComparison.Ordinal)) return folder;
        if ((newKey + "/").StartsWith(folder + "/", StringComparison.OrdinalIgnoreCase) && !newKey.Equals(folder, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Can't move a folder inside itself.");
        if (!newKey.Equals(folder, StringComparison.OrdinalIgnoreCase) && Directory.Exists(FullPath(s, newKey)))
            throw new InvalidOperationException($"There's already a folder called {name} there.");
        var map = Files(s, folder, recursive: true).ToDictionary(f => f.Key, f => newKey + f.Key[folder.Length..]);
        string from = FullPath(s, folder), to = FullPath(s, newKey);
        Directory.CreateDirectory(Path.GetDirectoryName(to)!);
        if (from.Equals(to, StringComparison.OrdinalIgnoreCase)) // case-only rename on Windows
        {
            string tmp = to + ".renaming";
            Directory.Move(from, tmp);
            Directory.Move(tmp, to);
        }
        else Directory.Move(from, to);
        RewriteReferences(s, map);
        return newKey;
    }

    /// <summary>Deletes an empty folder.</summary>
    public static void DeleteFolder(GameServices s, string folder)
    {
        string dir = FullPath(s, folder);
        if (Directory.EnumerateFileSystemEntries(dir).Any()) throw new InvalidOperationException("Folder isn't empty.");
        Directory.Delete(dir);
    }

    public static void DeleteFile(GameServices s, string key) => File.Delete(FullPath(s, key));

    private static void MoveFiles(GameServices s, Dictionary<string, string> map)
    {
        foreach (var (from, to) in map)
        {
            string src = FullPath(s, from), dst = FullPath(s, to);
            Directory.CreateDirectory(Path.GetDirectoryName(dst)!);
            if (src.Equals(dst, StringComparison.OrdinalIgnoreCase))
            {
                File.Move(src, dst + ".renaming");
                File.Move(dst + ".renaming", dst);
            }
            else File.Move(src, dst);
        }
        RewriteReferences(s, map);
    }

    /// <summary>Content that mentions an art key, as "Type: name [id]".</summary>
    public static List<string> UsedBy(GameServices s, string key)
    {
        string needle = JsonSerializer.Serialize(key, ChasmJson.Options);
        var found = new List<string>();
        foreach (var type in DefRegistry.DefTypes)
            foreach (var def in s.Defs.All(type))
                if (ChasmJson.Serialize(def, def.GetType()).Contains(needle, StringComparison.Ordinal))
                    found.Add($"{DefRegistry.FolderOf(type).DisplayName.TrimEnd('s')}: {def.DisplayName} [{def.Id}]");
        return found;
    }

    /// <summary>Rewrites art keys inside a JSON string. Returns the input when nothing matched.</summary>
    public static string Rewrite(string json, IReadOnlyDictionary<string, string> map)
    {
        foreach (var (from, to) in map)
        {
            string a = JsonSerializer.Serialize(from, ChasmJson.Options), b = JsonSerializer.Serialize(to, ChasmJson.Options);
            if (json.Contains(a, StringComparison.Ordinal)) json = json.Replace(a, b, StringComparison.Ordinal);
        }
        return json;
    }

    /// <summary>Updates every saved def that points at a moved file, then tells open editors.</summary>
    public static int RewriteReferences(GameServices s, IReadOnlyDictionary<string, string> map)
    {
        if (map.Count == 0) return 0;
        int changed = 0;
        foreach (var type in DefRegistry.DefTypes)
            foreach (var def in s.Defs.All(type).ToList())
            {
                string json = ChasmJson.Serialize(def, def.GetType());
                string fixedJson = Rewrite(json, map);
                if (ReferenceEquals(json, fixedJson) || json == fixedJson) continue;
                if (ChasmJson.Deserialize(fixedJson, def.GetType()) is Def updated) { s.Defs.Save(updated); changed++; }
            }
        s.TileVisuals.InvalidateAll();
        KeysMoved?.Invoke(map);
        if (changed > 0) Console.WriteLine($"[art] Updated {changed} content file(s) to the new art location.");
        return changed;
    }

    public static string FormatSize(long bytes) =>
        bytes < 1024 ? $"{bytes} B" : bytes < 1024 * 1024 ? $"{bytes / 1024.0:0} KB" : $"{bytes / 1024.0 / 1024.0:0.0} MB";

    public static string FormatDate(DateTime d) =>
        d.Date == DateTime.Today ? "Today " + d.ToString("h:mm tt")
        : d.Date == DateTime.Today.AddDays(-1) ? "Yesterday " + d.ToString("h:mm tt")
        : d.ToString("M/d/yyyy h:mm tt");
}
