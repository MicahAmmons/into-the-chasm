using System.Reflection;

namespace Chasm.Game.Hosting;

/// <summary>How a host (the shipped game or the tools launcher) wants the game configured.</summary>
public sealed record GameOptions
{
    public string WindowTitle { get; init; } = Chasm.Core.GameInfo.Title;
    public int WindowWidth { get; init; } = 1600;
    public int WindowHeight { get; init; } = 900;

    /// <summary>Exit on its own after this many frames (automated smoke tests).</summary>
    public int? ExitAfterFrames { get; init; }

    /// <summary>With <see cref="ExitAfterFrames"/>: save a screenshot of the last frame here.</summary>
    public string? ScreenshotPath { get; init; }

    /// <summary>Optional start mode for the tools launcher (e.g. "map", "world", "combat").</summary>
    public string? StartMode { get; init; }
    public string? StartArgument { get; init; }

    /// <summary>Extra assemblies containing content types / polymorphic subclasses.</summary>
    public IReadOnlyList<Assembly> ContentAssemblies { get; init; } = Array.Empty<Assembly>();

    /// <summary>Parses the command-line flags shared by every host.</summary>
    public static GameOptions FromArgs(string[] args, GameOptions defaults)
    {
        var o = defaults;
        for (int i = 0; i < args.Length; i++)
        {
            string? Next() => i + 1 < args.Length && !args[i + 1].StartsWith("--") ? args[++i] : null;
            switch (args[i])
            {
                case "--smoke-test":
                    o = o with { ExitAfterFrames = int.TryParse(Next(), out int f) ? f : 120 };
                    break;
                case "--screenshot":
                    o = o with { ScreenshotPath = Next() ?? "screenshot.png" };
                    break;
                case "--mode":
                    o = o with { StartMode = Next() };
                    break;
                case "--arg":
                    o = o with { StartArgument = Next() };
                    break;
            }
        }
        return o;
    }
}
