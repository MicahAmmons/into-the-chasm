namespace Chasm.Tools.MapEditor;

/// <summary>
/// Crash / close safety net for the Map Editor. While you edit, the current tile (and its floor)
/// is written to AppData/IntoTheChasm/editor-recovery/ every couple of seconds, saved or not.
/// The start dialog lists these so you can always grab your most recent work again.
///
/// One file per tile (new, unsaved tiles are keyed by their name). The newest 12 are kept.
/// </summary>
public static class EditorRecovery
{
    public const int Keep = 12;

    public static string Folder =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "IntoTheChasm", "editor-recovery");

    public sealed record Entry(string Path, string Title, DateTime SavedLocal);

    /// <summary>Writes a recovery file. <paramref name="key"/> identifies the tile (its id, or "new_name").</summary>
    public static void Write(string key, string title, string snapshotJson)
    {
        try
        {
            Directory.CreateDirectory(Folder);
            string safe = string.Concat(key.Select(c => System.IO.Path.GetInvalidFileNameChars().Contains(c) ? '_' : c));
            string path = System.IO.Path.Combine(Folder, safe + ".json");
            string tmp = path + ".tmp";
            // First line: the title shown in the list. Rest: the snapshot.
            File.WriteAllText(tmp, title.Replace('\n', ' ') + "\n" + snapshotJson);
            File.Move(tmp, path, overwrite: true);
            Prune();
        }
        catch (Exception e) { Console.WriteLine($"[recovery] not written: {e.Message}"); }
    }

    public static List<Entry> List()
    {
        if (!Directory.Exists(Folder)) return new();
        var list = new List<Entry>();
        foreach (var f in Directory.EnumerateFiles(Folder, "*.json"))
        {
            try
            {
                using var reader = new StreamReader(f);
                string title = reader.ReadLine() ?? System.IO.Path.GetFileNameWithoutExtension(f);
                list.Add(new Entry(f, title, File.GetLastWriteTime(f)));
            }
            catch (IOException) { }
        }
        return list.OrderByDescending(e => e.SavedLocal).ToList();
    }

    /// <summary>The snapshot JSON stored in a recovery file.</summary>
    public static string? Read(Entry entry)
    {
        try
        {
            string text = File.ReadAllText(entry.Path);
            int nl = text.IndexOf('\n');
            return nl < 0 ? null : text[(nl + 1)..];
        }
        catch (IOException) { return null; }
    }

    public static void Delete(Entry entry)
    {
        try { File.Delete(entry.Path); } catch (IOException) { }
    }

    private static void Prune()
    {
        foreach (var old in List().Skip(Keep)) Delete(old);
    }

    public static string Ago(DateTime local)
    {
        var span = DateTime.Now - local;
        if (span.TotalMinutes < 1) return "just now";
        if (span.TotalHours < 1) return $"{(int)span.TotalMinutes} min ago";
        if (span.TotalDays < 1) return $"{(int)span.TotalHours} h ago";
        return local.ToString("MMM d, HH:mm");
    }
}
