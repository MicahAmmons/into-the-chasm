namespace Chasm.Core.Content;

/// <summary>
/// Base class for every piece of authored content: items, monsters, animations, tiles, aspects...
/// One definition = one JSON file under <c>content/defs/&lt;folder&gt;/&lt;id&gt;.json</c>.
///
/// Definitions describe what something *is*. Runtime state (how many you carry, whether it was
/// picked up) never lives here; that belongs in WorldState / save data.
/// </summary>
public abstract class Def
{
    /// <summary>Unique, stable id. Also the file name. Other content refers to this, so avoid renaming.</summary>
    [System.Text.Json.Serialization.JsonPropertyOrder(-100)]
    [Tooltip("Unique id, also the file name. Other content refers to it by this id, so avoid renaming it later.")]
    [Order(-100)]
    public string Id { get; set; } = "";

    [System.Text.Json.Serialization.JsonPropertyOrder(-99)]
    [Tooltip("Human readable name shown in game and in editor lists.")]
    [Order(-99)]
    public string Name { get; set; } = "";

    /// <summary>Free-form designer notes. Never shown to players.</summary>
    [Multiline, Order(1000)]
    [Tooltip("Your own notes. Never shown in game.")]
    public string Notes { get; set; } = "";

    [System.Text.Json.Serialization.JsonIgnore]
    public string DisplayName => string.IsNullOrWhiteSpace(Name) ? Id : Name;

    /// <summary>
    /// Extra validation beyond what the generic validator checks (missing ids, broken references).
    /// Return human-readable problems.
    /// </summary>
    public virtual IEnumerable<string> Validate(DefRegistry registry) => Array.Empty<string>();

    public override string ToString() => $"{GetType().Name}:{Id}";
}

/// <summary>Marks a <see cref="Def"/> subclass as stored in <c>content/defs/&lt;Folder&gt;</c>.</summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class DefFolderAttribute(string folder, string displayName) : Attribute
{
    public string Folder { get; } = folder;
    public string DisplayName { get; } = displayName;
}
