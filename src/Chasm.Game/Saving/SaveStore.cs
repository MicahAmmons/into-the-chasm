using Chasm.Core.Serialization;
using Chasm.Core.Session;

namespace Chasm.Game.Saving;

/// <summary>
/// Save slots on disk. There is no "save" button and no loading an older state: each slot is one
/// living file the game rewrites at commit points (see WorldScene autosave). Writes are atomic
/// (temp file, then swap) and the previous version is kept as .bak, used only if the main file is
/// corrupt.
///
/// Location: %AppData%/IntoTheChasm/saves (Windows) or ~/.config/IntoTheChasm/saves (Linux/macOS).
/// </summary>
public sealed class SaveStore
{
    public const int SlotCount = 3;
    public string Folder { get; }

    public SaveStore(string? folder = null)
    {
        Folder = folder ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "IntoTheChasm", "saves");
        Directory.CreateDirectory(Folder);
    }

    public string PathFor(int slot) => Path.Combine(Folder, $"slot{slot}.json");

    public SaveData? Load(int slot)
    {
        foreach (var file in new[] { PathFor(slot), PathFor(slot) + ".bak" })
        {
            try
            {
                if (File.Exists(file) && ChasmJson.Deserialize<SaveData>(File.ReadAllText(file)) is { } data) return data;
            }
            catch (Exception e) { Console.WriteLine($"[save] {file} unreadable: {e.Message}"); }
        }
        return null;
    }

    public void Write(int slot, SaveData data)
    {
        data.SavedAtUtc = DateTime.UtcNow;
        string path = PathFor(slot), tmp = path + ".tmp";
        File.WriteAllText(tmp, ChasmJson.Serialize(data));
        if (File.Exists(path)) File.Copy(path, path + ".bak", overwrite: true);
        File.Move(tmp, path, overwrite: true);
    }

    public void Delete(int slot)
    {
        foreach (var f in new[] { PathFor(slot), PathFor(slot) + ".bak" }) if (File.Exists(f)) File.Delete(f);
    }
}
