using Chasm.Core.Serialization;

namespace Chasm.Tools.MapEditor;

/// <summary>
/// Your personal Map Editor preferences (line colours, toggles). Saved per computer in
/// AppData/IntoTheChasm/editor-prefs.json, never in the repo.
/// </summary>
public sealed class EditorPrefs
{
    public float[] GridColor { get; set; } = { 1f, 1f, 1f, 0.22f };
    public float[] CenterLineColor { get; set; } = { 0f, 0f, 0f, 0.35f };
    public bool ShowCenterLines { get; set; } = true;
    public bool ShowGrid { get; set; } = true;

    private static string PathOnDisk =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "IntoTheChasm", "editor-prefs.json");

    public static EditorPrefs Load()
    {
        try
        {
            if (File.Exists(PathOnDisk) && ChasmJson.Deserialize<EditorPrefs>(File.ReadAllText(PathOnDisk)) is { } p) return p;
        }
        catch (Exception e) { Console.WriteLine($"[editor] prefs unreadable: {e.Message}"); }
        return new EditorPrefs();
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(PathOnDisk)!);
            File.WriteAllText(PathOnDisk, ChasmJson.Serialize(this));
        }
        catch (Exception e) { Console.WriteLine($"[editor] prefs not saved: {e.Message}"); }
    }

    public static Microsoft.Xna.Framework.Color ToColor(float[] c) =>
        new Microsoft.Xna.Framework.Color(c[0], c[1], c[2]) * c[3];
}
