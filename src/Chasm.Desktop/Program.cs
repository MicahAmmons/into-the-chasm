using Chasm.Game.Hosting;

// Entry point for the shipped game.
var options = GameOptions.FromArgs(args, new GameOptions());
using var game = new ChasmGame(options);
game.Run();
