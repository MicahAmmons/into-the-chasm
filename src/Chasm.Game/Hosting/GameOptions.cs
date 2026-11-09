namespace Chasm.Game.Hosting;

/// <summary>
/// How a host (the shipped game or the tools launcher) wants the game configured.
/// </summary>
public sealed record GameOptions
{
    public string WindowTitle { get; init; } = Chasm.Core.GameInfo.Title;

    /// <summary>
    /// When set, the game exits on its own after this many frames. Used by automated smoke tests
    /// to prove the game boots, loads and renders without a person watching.
    /// </summary>
    public int? ExitAfterFrames { get; init; }

    /// <summary>Parses the command-line flags shared by every host.</summary>
    public static GameOptions FromArgs(string[] args, GameOptions defaults)
    {
        var options = defaults;
        for (int i = 0; i < args.Length; i++)
        {
            if (args[i] == "--smoke-test")
            {
                int frames = 120;
                if (i + 1 < args.Length && int.TryParse(args[i + 1], out int parsed)) { frames = parsed; i++; }
                options = options with { ExitAfterFrames = frames };
            }
        }
        return options;
    }
}
