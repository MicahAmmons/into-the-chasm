using Chasm.Core;
using Chasm.Game.Hosting;
using Chasm.Game.Scenes;

var options = GameOptions.FromArgs(args, new GameOptions { WindowTitle = $"{GameInfo.Title} - Tools" });
using var game = new ChasmGame(options, _ => new MessageScene("Tools", "Launcher coming next"));
game.Run();
