namespace Chasm.Game.Diagnostics;

/// <summary>
/// A small text log at AppData/IntoTheChasm/log.txt (rewritten each run). Records window focus,
/// resizes, display resets and crashes, so odd behaviour on another machine can be diagnosed.
/// </summary>
public static class Log
{
    private static readonly object Gate = new();
    private static StreamWriter? _writer;

    public static string FilePath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "IntoTheChasm", "log.txt");

    public static void Start(string app)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            _writer = new StreamWriter(FilePath, append: false) { AutoFlush = true };
            Write($"{app} started. OS {Environment.OSVersion}, .NET {Environment.Version}");
            AppDomain.CurrentDomain.UnhandledException += (_, e) => Write($"CRASH: {e.ExceptionObject}");
        }
        catch { _writer = null; }
    }

    public static void Write(string message)
    {
        string line = $"{DateTime.Now:HH:mm:ss.fff}  {message}";
        Console.WriteLine("[log] " + message);
        lock (Gate) { try { _writer?.WriteLine(line); } catch { } }
    }
}
