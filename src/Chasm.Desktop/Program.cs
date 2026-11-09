using Chasm.Core.Session;
using Chasm.Game.Hosting;
using Chasm.Game.World;

// Entry point for the shipped game. (Title screen + save slots come later; this starts a fresh session.)
var options = GameOptions.FromArgs(args, new GameOptions());
using var game = new ChasmGame(options, s => new WorldScene(new GameSession(s.Defs)));
game.Run();
