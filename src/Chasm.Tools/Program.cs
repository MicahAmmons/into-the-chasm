using Chasm.Tools.MapEditor;
using Chasm.Core;
using Chasm.Game.Hosting;
using Chasm.Game.Scenes;
using Chasm.Tools.ImGuiHost;
using Chasm.Tools.Scenes;

// Developer launcher. --mode map|world|content|combat [--arg <tile id>] jumps straight into a tool.
// --resave: load every content file and write it back in the current format (after format changes).
if (args.Contains("--resave"))
{
    Chasm.Core.Serialization.ChasmJson.RegisterAssembly(typeof(ChasmGame).Assembly);
    var paths = Chasm.Game.Assets.ContentPaths.Resolve();
    var reg = new Chasm.Core.Content.DefRegistry();
    reg.LoadAll(paths.Defs);
    foreach (var e in reg.LoadErrors) Console.WriteLine($"[load error] {e}");
    int n = 0;
    foreach (var t in Chasm.Core.Content.DefRegistry.DefTypes)
        foreach (var d in reg.All(t).ToList()) { if (d is Chasm.Core.World.TileDef td) td.CommitWalkable(); reg.Save(d); n++; }
    Console.WriteLine($"Re-saved {n} content files.");
    return;
}

var options = GameOptions.FromArgs(args, new GameOptions { WindowTitle = $"{GameInfo.Title} - Tools" });
using var game = new ChasmGame(options, s =>
{
    WorldSandboxScene.SandboxDefs = s.Defs;
    return options.StartMode switch
    {
        "map" => new MapEditorScene(options.StartArgument),
        "world" => new WorldSandboxScene(options.StartArgument),
        "content" => new ContentBrowserScene(options.StartArgument),
        "combat" => new CombatSandboxScene(options.StartArgument),
        _ => (Scene)new LauncherScene(),
    };
}, new ImGuiExtension());
game.Run();
