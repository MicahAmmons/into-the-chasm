using Chasm.Game.Hosting;
using Chasm.Game.Ui;

// Entry point for the shipped game: the title screen with save slots.
var options = GameOptions.FromArgs(args, new GameOptions());
using var game = new ChasmGame(options, _ => new TitleScene());
game.Run();
