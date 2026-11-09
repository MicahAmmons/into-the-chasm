using Chasm.Core.Content;

namespace Chasm.Game.Assets;

/// <summary>
/// Watches content/defs and reloads changed JSON files into the registry on the main thread.
/// Edit a monster file in any text editor and the sandbox picks it up within a frame.
/// </summary>
public sealed class DefHotReload : IDisposable
{
    private readonly DefRegistry _registry;
    private readonly FileSystemWatcher? _watcher;
    private readonly HashSet<string> _dirty = new();
    private readonly Dictionary<string, DateTime> _ignoreUntil = new();

    public DefHotReload(DefRegistry registry, string defsRoot)
    {
        _registry = registry;
        Directory.CreateDirectory(defsRoot);
        _watcher = new FileSystemWatcher(defsRoot, "*.json") { IncludeSubdirectories = true, EnableRaisingEvents = true };
        _watcher.Changed += (_, e) => Mark(e.FullPath);
        _watcher.Created += (_, e) => Mark(e.FullPath);
        _watcher.Renamed += (_, e) => Mark(e.FullPath);
    }

    /// <summary>Editors call this right after saving, so their own write isn't reloaded over in-memory edits.</summary>
    public void IgnoreNextChange(string path)
    {
        lock (_dirty) _ignoreUntil[Path.GetFullPath(path)] = DateTime.UtcNow.AddSeconds(1);
    }

    private void Mark(string path)
    {
        lock (_dirty) _dirty.Add(Path.GetFullPath(path));
    }

    public void Process()
    {
        string[] files;
        lock (_dirty)
        {
            if (_dirty.Count == 0) return;
            files = _dirty.ToArray();
            _dirty.Clear();
            files = files.Where(f => !(_ignoreUntil.TryGetValue(f, out var t) && DateTime.UtcNow < t)).ToArray();
        }
        foreach (var file in files)
        {
            if (!File.Exists(file)) continue;
            var type = _registry.TypeForFile(file);
            if (type is null) continue;
            Thread.Sleep(10); // let the writer finish
            _registry.LoadFile(type, file);
        }
    }

    public void Dispose() => _watcher?.Dispose();
}
