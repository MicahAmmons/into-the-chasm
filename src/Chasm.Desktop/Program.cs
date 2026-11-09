using Chasm.Core;
using Chasm.Game.Hosting;
using Chasm.Game.Scenes;

// Entry point for the shipped game.
var options = GameOptions.FromArgs(args, new GameOptions());
using var game = new ChasmGame(options, _ => new MessageScene(GameInfo.Title, "Foundation build"));
game.Run();
