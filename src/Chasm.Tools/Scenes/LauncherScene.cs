using Chasm.Core;
using Chasm.Core.Content;
using Chasm.Game.Scenes;
using ImGuiNET;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Num = System.Numerics;

namespace Chasm.Tools.Scenes;

/// <summary>The developer hub: pick a tool.</summary>
public sealed class LauncherScene : Scene
{
    public override void OnEnter() => ToolContext.EnsureInitialized(Services);

    private static readonly (string name, string description, Func<Scene> open)[] Tools =
    {
        ("Content Browser", "Every item, monster, animation, tile, floor... Create, edit, duplicate.", () => new ContentBrowserScene()),
        ("Map Editor", "Paint walkable cells, exits, combat starts. Place items, interactables, monsters.", () => new MapEditorScene()),
        ("World Sandbox", "Walk any tile with live panels: give items, set flags, change the clock.", () => new WorldSandboxScene(null)),
        ("Combat Sandbox", "Pick monsters and a team, then fight on any tile.", () => new CombatSandboxScene()),
    };

    public override void Update(float dt)
    {
        ToolContext.MenuBar(Services, "Launcher");
        var size = new Num.Vector2(520, 0);
        ImGui.SetNextWindowPos(ImGui.GetIO().DisplaySize / 2, ImGuiCond.Always, new Num.Vector2(0.5f, 0.5f));
        ImGui.SetNextWindowSize(size);
        ImGui.Begin("Into the Chasm - Tools", ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoMove | ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.AlwaysAutoResize);
        foreach (var (name, description, open) in Tools)
        {
            if (ImGui.Button(name, new Num.Vector2(-1, 46))) Services.Scenes.ResetTo(open());
            ImGui.TextDisabled(description);
            ImGui.Spacing();
        }
        ImGui.Separator();
        var errors = Services.Defs.LoadErrors;
        var problems = ContentValidator.ValidateAll(Services.Defs);
        ImGui.TextDisabled($"Content: {Services.Paths.Root}");
        if (errors.Count + problems.Count == 0) ImGui.TextColored(new Num.Vector4(0.5f, 1, 0.6f, 1), "All content valid.");
        else if (ImGui.CollapsingHeader($"{errors.Count + problems.Count} content problem(s)"))
            foreach (var p in errors.Concat(problems)) ImGui.TextWrapped(p);
        ImGui.End();
        ToolContext.Wizards.Draw();
    }

    public override void Draw(SpriteBatch batch)
    {
        batch.Begin();
        Services.Draw.TextCentered(batch, GameInfo.Title, new Vector2(960, 140), new Color(200, 210, 240), 72, bold: true);
        batch.End();
    }
}
