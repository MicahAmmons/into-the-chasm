using Chasm.Tools.MapEditor;
using Chasm.Game.Hosting;
using Chasm.Tools.Editing;
using ImGuiNET;

namespace Chasm.Tools.Scenes;

/// <summary>Shared editor state for all tool scenes (one wizard stack, one inspector).</summary>
public static class ToolContext
{
    public static WizardStack Wizards { get; } = new();
    private static bool _initialized;

    public static void EnsureInitialized(GameServices s)
    {
        if (_initialized) return;
        Wizards.Initialize(s);
        _initialized = true;
        // Dev convenience for automated checks: CHASM_ART=library | import | pick:<key> | move:<key>:<folder>
        switch (Environment.GetEnvironmentVariable("CHASM_ART")?.Split(':'))
        {
            case ["library"]: Chasm.Tools.Art.ArtBrowser.OpenManage(); break;
            case ["pick", var current]: Chasm.Tools.Art.ArtBrowser.OpenPick(current, _ => { }); break;
            case ["import"]: Chasm.Tools.Art.ArtBrowser.OpenPick(null, _ => { }, addNew: true); break;
            case ["move", var key, var folder]:
                Console.WriteLine($"[art] moved to {Chasm.Tools.Art.ArtLibrary.MoveFile(s, key, folder)}");
                break;
        }
    }

    private static void OpenFolder(string path)
    {
        try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo { FileName = path, UseShellExecute = true }); }
        catch (Exception e) { Console.WriteLine($"Couldn't open {path}: {e.Message}"); }
    }

    /// <summary>The top menu bar every tool shows. Returns false if the tool asked to close.</summary>
    public static void MenuBar(GameServices s, string title, Action? extraMenus = null)
    {
        if (!ImGui.BeginMainMenuBar()) return;
        if (ImGui.BeginMenu("Tools"))
        {
            if (ImGui.MenuItem("Launcher")) s.Scenes.ResetTo(new LauncherScene());
            if (ImGui.MenuItem("Content Browser")) s.Scenes.ResetTo(new ContentBrowserScene());
            if (ImGui.MenuItem("Map Editor")) s.Scenes.ResetTo(new MapEditorScene());
            if (ImGui.MenuItem("World Sandbox")) s.Scenes.ResetTo(new WorldSandboxScene(null));
            if (ImGui.MenuItem("Combat Sandbox")) s.Scenes.ResetTo(new CombatSandboxScene(null));
            ImGui.Separator();
            if (ImGui.MenuItem("Art library...")) Chasm.Tools.Art.ArtBrowser.OpenManage();
            ImGui.Separator();
            if (ImGui.MenuItem("Reset display", "F9")) s.Game.ResetDisplay();
            if (ImGui.MenuItem("Open log file folder")) OpenFolder(Path.GetDirectoryName(Chasm.Game.Diagnostics.Log.FilePath)!);
            ImGui.Separator();
            if (ImGui.MenuItem("Quit")) s.Game.Exit();
            ImGui.EndMenu();
        }
        if (ImGui.BeginMenu("New"))
        {
            foreach (var t in Chasm.Core.Content.DefRegistry.DefTypes)
                if (ImGui.MenuItem(Chasm.Core.Content.DefRegistry.FolderOf(t).DisplayName.TrimEnd('s') + "...")) Wizards.OpenNew(t);
            ImGui.EndMenu();
        }
        extraMenus?.Invoke();
        ImGui.SameLine(ImGui.GetWindowWidth() - 360);
        ImGui.TextDisabled(title);
        ImGui.EndMainMenuBar();
    }
}
