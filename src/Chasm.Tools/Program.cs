using Chasm.Core;
using Chasm.Game.Hosting;

// Entry point for the developer launcher. Sandbox and editor modes are added in later steps;
// for now it boots the same game host with a tools-specific window title.
var options = GameOptions.FromArgs(args, new GameOptions { WindowTitle = $"{GameInfo.Title} - Tools" });
using var game = new ChasmGame(options);
game.Run();
