using Chasm.Core;
using Chasm.Game.Hosting;
using Chasm.Game.Scenes;
using Chasm.Tools.ImGuiHost;
using Chasm.Tools.Scenes;

// Developer launcher. --mode map|world|content|combat [--arg <tile id>] jumps straight into a tool.
var options = GameOptions.FromArgs(args, new GameOptions { WindowTitle = $"{GameInfo.Title} - Tools", WindowWidth = 1800, WindowHeight = 1000 });
using var game = new ChasmGame(options, s =>
{
    WorldSandboxScene.SandboxDefs = s.Defs;
    return options.StartMode switch
    {
        "map" => new MapEditorScene(options.StartArgument),
        "world" => new WorldSandboxScene(options.StartArgument),
        "content" => new ContentBrowserScene(options.StartArgument),
        "combat" => new CombatSandboxScene(),
        _ => (Scene)new LauncherScene(),
    };
}, new ImGuiExtension());
game.Run();
