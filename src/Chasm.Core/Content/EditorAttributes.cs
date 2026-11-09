namespace Chasm.Core.Content;

// Attributes that describe how a field should be edited. Core owns them so content classes can be
// annotated without referencing any editor code; the Tools project reads them to build forms.

/// <summary>A string field holding the id of another definition of type <see cref="Target"/>.
/// Editors show a picker of all existing ones plus "+ New".</summary>
[AttributeUsage(AttributeTargets.Property)]
public sealed class RefAttribute(Type target) : Attribute
{
    public Type Target { get; } = target;
}

/// <summary>A string field holding a texture path relative to <c>content/art</c>.</summary>
[AttributeUsage(AttributeTargets.Property)]
public sealed class TextureAttribute : Attribute;

/// <summary>Help text shown when hovering the field in editors.</summary>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Class)]
public sealed class TooltipAttribute(string text) : Attribute
{
    public string Text { get; } = text;
}

/// <summary>Display label in editors (defaults to the property name split into words).</summary>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Class | AttributeTargets.Field)]
public sealed class LabelAttribute(string text) : Attribute
{
    public string Text { get; } = text;
}

/// <summary>Multi-line text box.</summary>
[AttributeUsage(AttributeTargets.Property)]
public sealed class MultilineAttribute : Attribute;

/// <summary>Numeric limits for sliders / validation.</summary>
[AttributeUsage(AttributeTargets.Property)]
public sealed class RangeAttribute(double min, double max) : Attribute
{
    public double Min { get; } = min;
    public double Max { get; } = max;
}

/// <summary>Field must be filled in; the creation wizard will ask for it.</summary>
[AttributeUsage(AttributeTargets.Property)]
public sealed class RequiredAttribute : Attribute;

/// <summary>Never shown in editors.</summary>
[AttributeUsage(AttributeTargets.Property)]
public sealed class HiddenAttribute : Attribute;

/// <summary>Sort order in editor forms (lower first). Default 0, ties keep declaration order.</summary>
[AttributeUsage(AttributeTargets.Property)]
public sealed class OrderAttribute(int order) : Attribute
{
    public int Order { get; } = order;
}

/// <summary>Groups fields under a collapsible header in editor forms.</summary>
[AttributeUsage(AttributeTargets.Property)]
public sealed class GroupAttribute(string name) : Attribute
{
    public string Name { get; } = name;
}

/// <summary>
/// Marks an abstract base type whose concrete subclasses are chosen from a list in editors and
/// saved with a <c>"$type"</c> field in JSON. Conditions, outcomes and placements use this.
/// New subclasses are discovered automatically; no registration needed.
/// </summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class PolymorphicAttribute : Attribute;

/// <summary>The <c>"$type"</c> name written to JSON for a polymorphic subclass. Defaults to the class name.
/// Never change it once content uses it.</summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class TypeIdAttribute(string id) : Attribute
{
    public string Id { get; } = id;
}
