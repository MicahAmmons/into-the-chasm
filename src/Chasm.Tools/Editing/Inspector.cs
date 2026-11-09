using System.Collections;
using System.Reflection;
using System.Text.Json.Serialization;
using Chasm.Core.Content;
using Chasm.Core.Grid;
using Chasm.Core.Rules;
using Chasm.Core.Serialization;
using Chasm.Core.World;
using Chasm.Game.Assets;
using Chasm.Tools.ImGuiHost;
using ImGuiNET;
using Num = System.Numerics;

namespace Chasm.Tools.Editing;

/// <summary>
/// Builds an editing form for ANY object from its properties and attributes. This is what lets
/// new content be authored without code: add a field to a definition class and it shows up in
/// every editor; add a new Condition subclass and it shows up in every condition dropdown.
///
///   string + [Ref(typeof(X))]  -> picker of all X, plus "+ New X..." (opens X's wizard) and "Edit"
///   string + [Texture]          -> searchable art picker with preview
///   enums                       -> dropdown (uses [Label] names)
///   List&lt;T&gt;                     -> add / remove / reorder; polymorphic T asks which kind to add
///   [Polymorphic] base          -> "kind" dropdown, then its fields
///   CellCoord                   -> Q/R boxes + "pick on map" when a map is open
/// </summary>
public sealed class Inspector
{
    private readonly DefRegistry _defs;
    private readonly TextureStore _textures;
    private readonly WizardStack _wizards;
    private int _depth;

    /// <summary>When set (by the map editor), CellCoord fields show a "Pick" button that calls this.</summary>
    public Action<Action<CellCoord>>? CellPicker { get; set; }

    public Inspector(DefRegistry defs, TextureStore textures, WizardStack wizards)
    {
        _defs = defs;
        _textures = textures;
        _wizards = wizards;
    }

    /// <summary>Draws a form for every editable property of <paramref name="obj"/>. Returns true if anything changed.</summary>
    public bool Edit(object obj, Func<PropertyInfo, bool>? filter = null)
    {
        bool changed = false;
        string? group = null;
        bool groupOpen = true;
        foreach (var prop in EditableProperties(obj.GetType()))
        {
            if (filter is not null && !filter(prop)) continue;
            string? g = prop.GetCustomAttribute<GroupAttribute>()?.Name;
            if (g != group)
            {
                group = g;
                groupOpen = g is null || ImGui.CollapsingHeader(g, ImGuiTreeNodeFlags.DefaultOpen);
            }
            if (!groupOpen) continue;
            ImGui.PushID(prop.Name);
            changed |= EditProperty(obj, prop);
            ImGui.PopID();
        }
        return changed;
    }

    public static IEnumerable<PropertyInfo> EditableProperties(Type t) =>
        t.GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.CanRead && p.GetIndexParameters().Length == 0)
            .Where(p => p.GetCustomAttribute<HiddenAttribute>() is null && p.GetCustomAttribute<JsonIgnoreAttribute>() is null)
            .Where(p => p.CanWrite || IsEditableInPlace(p.PropertyType))
            .Select((p, i) => (p, i, order: p.GetCustomAttribute<OrderAttribute>()?.Order ?? 0, depth: Depth(p.DeclaringType!)))
            .OrderBy(x => x.order).ThenBy(x => x.depth).ThenBy(x => x.i)
            .Select(x => x.p);

    private static int Depth(Type t) { int d = 0; while (t.BaseType is not null) { d++; t = t.BaseType; } return d; }

    private static bool IsEditableInPlace(Type t) => typeof(IList).IsAssignableFrom(t) || (t.IsClass && t != typeof(string));

    public static string LabelFor(MemberInfo m) =>
        m.GetCustomAttribute<LabelAttribute>()?.Text ?? SplitWords(m.Name);

    public static string SplitWords(string s)
    {
        var sb = new System.Text.StringBuilder();
        for (int i = 0; i < s.Length; i++)
        {
            if (i > 0 && char.IsUpper(s[i]) && !char.IsUpper(s[i - 1])) sb.Append(' ');
            sb.Append(s[i]);
        }
        return sb.ToString();
    }

    private static void Label(string text, string? tooltip, bool required)
    {
        ImGui.AlignTextToFramePadding();
        if (required) ImGui.TextColored(new Num.Vector4(1f, 0.8f, 0.4f, 1f), text + " *");
        else ImGui.TextUnformatted(text);
        if (tooltip is not null && ImGui.IsItemHovered()) ImGui.SetTooltip(tooltip);
        ImGui.SameLine(170);
        ImGui.SetNextItemWidth(-1);
    }

    private bool EditProperty(object owner, PropertyInfo prop)
    {
        var type = prop.PropertyType;
        var value = prop.GetValue(owner);
        string label = LabelFor(prop);
        string? tip = prop.GetCustomAttribute<TooltipAttribute>()?.Text;
        bool required = prop.GetCustomAttribute<RequiredAttribute>() is not null;
        var range = prop.GetCustomAttribute<Chasm.Core.Content.RangeAttribute>();

        // Lists and nested objects get their own tree node.
        if (typeof(IList).IsAssignableFrom(type) && type != typeof(string))
        {
            var list = (IList?)value;
            if (list is null && prop.CanWrite) { list = (IList)Activator.CreateInstance(type)!; prop.SetValue(owner, list); }
            return list is not null && EditList(label, tip, list, ElementType(type), prop);
        }

        if (type == typeof(string))
        {
            string s = (string?)value ?? "";
            if (prop.GetCustomAttribute<RefAttribute>() is { } r)
            {
                Label(label, tip, required);
                if (RefPicker(r.Target, ref s)) { prop.SetValue(owner, s); return true; }
                return false;
            }
            if (prop.GetCustomAttribute<TextureAttribute>() is not null)
            {
                Label(label, tip, required);
                if (TexturePicker(ref s)) { prop.SetValue(owner, s); return true; }
                return false;
            }
            if (prop.GetCustomAttribute<MultilineAttribute>() is not null)
            {
                ImGui.TextUnformatted(label);
                if (tip is not null && ImGui.IsItemHovered()) ImGui.SetTooltip(tip);
                if (ImGui.InputTextMultiline("##v", ref s, 4000, new Num.Vector2(-1, 70))) { prop.SetValue(owner, s); return true; }
                return false;
            }
            Label(label, tip, required);
            if (ImGui.InputText("##v", ref s, 400)) { prop.SetValue(owner, s); return true; }
            return false;
        }

        if (!prop.CanWrite && value is not null && type.IsClass)
            return EditNested(label, tip, value);

        Label(label, tip, required);
        var result = EditValue(type, value, range, out bool changed);
        if (changed) prop.SetValue(owner, result);
        return changed;
    }

    /// <summary>Edits a single value (no label). Used for properties and list elements.</summary>
    private object? EditValue(Type type, object? value, Chasm.Core.Content.RangeAttribute? range, out bool changed)
    {
        changed = false;
        if (type == typeof(int))
        {
            int v = (int)value!;
            if (ImGui.InputInt("##v", ref v)) { if (range is not null) v = (int)Math.Clamp(v, range.Min, range.Max); changed = true; }
            return v;
        }
        if (type == typeof(float))
        {
            float v = (float)value!;
            if (ImGui.InputFloat("##v", ref v, 0.1f, 1f, "%.3g")) { if (range is not null) v = (float)Math.Clamp(v, range.Min, range.Max); changed = true; }
            return v;
        }
        if (type == typeof(double))
        {
            double v = (double)value!;
            if (ImGui.InputDouble("##v", ref v, 1, 10, "%.4g")) { if (range is not null) v = Math.Clamp(v, range.Min, range.Max); changed = true; }
            return v;
        }
        if (type == typeof(bool))
        {
            bool v = (bool)value!;
            changed = ImGui.Checkbox("##v", ref v);
            return v;
        }
        if (type == typeof(string))
        {
            string v = (string?)value ?? "";
            changed = ImGui.InputText("##v", ref v, 400);
            return v;
        }
        if (type.IsEnum) return EnumCombo(type, value!, out changed);
        if (type == typeof(CellCoord)) return CellField((CellCoord)value!, out changed);
        if (Nullable.GetUnderlyingType(type) is { } inner)
        {
            bool has = value is not null;
            if (ImGui.Checkbox("##has", ref has)) { changed = true; return has ? Activator.CreateInstance(inner) : null; }
            if (!has) return null;
            ImGui.SameLine();
            var v = EditValue(inner, value, range, out changed);
            return v;
        }
        if (IsPolymorphic(type))
        {
            var v = PolymorphicPicker(type, value, out changed);
            if (v is not null) { ImGui.Indent(); changed |= EditNested(null, null, v); ImGui.Unindent(); }
            return v;
        }
        if (type.IsClass)
        {
            value ??= Activator.CreateInstance(type);
            ImGui.NewLine();
            ImGui.Indent();
            changed = Edit(value!);
            ImGui.Unindent();
            return value;
        }
        Ui.Colored(Ui.Muted, $"({type.Name} not editable)");
        return value;
    }

    private bool EditNested(string? label, string? tip, object value)
    {
        if (_depth > 12) { ImGui.TextDisabled("(too deep)"); return false; }
        if (label is null)
        {
            _depth++; bool c = Edit(value); _depth--; return c;
        }
        bool open = ImGui.TreeNodeEx(label, ImGuiTreeNodeFlags.DefaultOpen);
        if (tip is not null && ImGui.IsItemHovered()) ImGui.SetTooltip(tip);
        if (!open) return false;
        _depth++;
        bool changed = Edit(value);
        _depth--;
        ImGui.TreePop();
        return changed;
    }

    // ---------------------------------------------------------------- lists

    private static Type ElementType(Type listType) =>
        listType.IsArray ? listType.GetElementType()! : listType.GetGenericArguments().FirstOrDefault() ?? typeof(object);

    private bool EditList(string label, string? tip, IList list, Type elem, PropertyInfo? prop)
    {
        bool changed = false;
        bool open = ImGui.TreeNodeEx($"{label} ({list.Count})###list", list.Count > 0 && list.Count < 6 ? ImGuiTreeNodeFlags.DefaultOpen : 0);
        if (tip is not null && ImGui.IsItemHovered()) ImGui.SetTooltip(tip);
        if (!open) return false;
        var refAttr = prop?.GetCustomAttribute<RefAttribute>();
        var texAttr = prop?.GetCustomAttribute<TextureAttribute>();

        int remove = -1, moveUp = -1;
        for (int i = 0; i < list.Count; i++)
        {
            ImGui.PushID(i);
            var item = list[i];
            if (ImGui.SmallButton("x")) remove = i;
            if (ImGui.IsItemHovered()) ImGui.SetTooltip("Remove");
            ImGui.SameLine();
            if (i > 0 && ImGui.SmallButton("^")) moveUp = i;
            ImGui.SameLine();

            if (elem == typeof(string) && refAttr is not null)
            {
                string s = (string?)item ?? "";
                ImGui.SetNextItemWidth(-1);
                if (RefPicker(refAttr.Target, ref s)) { list[i] = s; changed = true; }
            }
            else if (elem == typeof(string) && texAttr is not null)
            {
                string s = (string?)item ?? "";
                ImGui.SetNextItemWidth(-1);
                if (TexturePicker(ref s)) { list[i] = s; changed = true; }
            }
            else if (IsSimple(elem))
            {
                ImGui.SetNextItemWidth(-1);
                var v = EditValue(elem, item, null, out bool c);
                if (c) { list[i] = v; changed = true; }
            }
            else if (item is not null)
            {
                string summary = Summary(item);
                bool nodeOpen = ImGui.TreeNodeEx($"{summary}###item", list.Count <= 3 ? ImGuiTreeNodeFlags.DefaultOpen : 0);
                if (nodeOpen)
                {
                    if (IsPolymorphic(elem))
                    {
                        Label("Kind", "What kind of thing this is.", false);
                        var swapped = PolymorphicPicker(elem, item, out bool c);
                        if (c && swapped is not null) { list[i] = swapped; item = swapped; changed = true; }
                    }
                    _depth++;
                    changed |= Edit(item);
                    _depth--;
                    ImGui.TreePop();
                }
            }
            ImGui.PopID();
        }
        if (remove >= 0) { list.RemoveAt(remove); changed = true; }
        if (moveUp > 0) { var t = list[moveUp]; list.RemoveAt(moveUp); list.Insert(moveUp - 1, t); changed = true; }

        if (IsPolymorphic(elem))
        {
            if (ImGui.Button($"+ Add {SplitWords(elem.Name)}")) ImGui.OpenPopup("addKind");
            if (ImGui.BeginPopup("addKind"))
            {
                foreach (var t in PolymorphicTypes.ConcreteSubtypes(elem))
                {
                    if (ImGui.Selectable(LabelFor(t))) { list.Add(Activator.CreateInstance(t)); changed = true; }
                    if (t.GetCustomAttribute<TooltipAttribute>() is { } tt && ImGui.IsItemHovered()) ImGui.SetTooltip(tt.Text);
                }
                ImGui.EndPopup();
            }
        }
        else if (ImGui.Button("+ Add"))
        {
            list.Add(elem == typeof(string) ? "" : Activator.CreateInstance(elem));
            changed = true;
        }
        ImGui.TreePop();
        return changed;
    }

    private static bool IsSimple(Type t) =>
        t.IsPrimitive || t.IsEnum || t == typeof(string) || t == typeof(CellCoord) || Nullable.GetUnderlyingType(t) is not null;

    public string Summary(object item) => item switch
    {
        Condition c => c.Describe(_defs),
        Outcome o => o.Describe(_defs),
        Placement p => $"{p.Id}: {p.Describe(_defs)}",
        _ => item.ToString() ?? item.GetType().Name,
    };

    // ---------------------------------------------------------------- polymorphic

    public static bool IsPolymorphic(Type t) => t.IsAbstract && t.GetCustomAttribute<PolymorphicAttribute>() is not null;

    private object? PolymorphicPicker(Type baseType, object? current, out bool changed)
    {
        changed = false;
        string preview = current is null ? "(choose...)" : LabelFor(current.GetType());
        ImGui.SetNextItemWidth(-1);
        if (ImGui.BeginCombo("##kind", preview))
        {
            if (ImGui.Selectable("(none)", current is null)) { current = null; changed = true; }
            foreach (var t in PolymorphicTypes.ConcreteSubtypes(baseType))
            {
                if (ImGui.Selectable(LabelFor(t), current?.GetType() == t) && current?.GetType() != t)
                {
                    current = Activator.CreateInstance(t);
                    changed = true;
                }
                if (t.GetCustomAttribute<TooltipAttribute>() is { } tt && ImGui.IsItemHovered()) ImGui.SetTooltip(tt.Text);
            }
            ImGui.EndCombo();
        }
        return current;
    }

    // ---------------------------------------------------------------- pickers

    private string _search = "";

    /// <summary>Dropdown of every def of <paramref name="defType"/>, plus "+ New..." and an Edit button.</summary>
    public bool RefPicker(Type defType, ref string id)
    {
        bool changed = false;
        uint fieldId = ImGui.GetID("##ref");
        var current = _defs.Find(defType, id);
        string preview = string.IsNullOrEmpty(id) ? "(none)" : current is null ? $"'{id}' (missing!)" : $"{current.DisplayName}  [{id}]";
        float editWidth = string.IsNullOrEmpty(id) || current is null ? 0 : 50;
        ImGui.SetNextItemWidth(ImGui.GetContentRegionAvail().X - editWidth);
        if (ImGui.BeginCombo("##ref", preview, ImGuiComboFlags.HeightLarge))
        {
            if (ImGui.IsWindowAppearing()) { _search = ""; ImGui.SetKeyboardFocusHere(); }
            ImGui.InputTextWithHint("##search", "search...", ref _search, 100);
            string typeName = DefRegistry.FolderOf(defType).DisplayName.TrimEnd('s');
            if (ImGui.Selectable($"+ New {typeName}..."))
            {
                // The wizard finishes later; remember which field asked so the new id lands there.
                var pending = new PendingAssign(fieldId);
                _pending.Add(pending);
                _wizards.OpenNew(defType, newId => pending.Result = newId);
            }
            if (ImGui.Selectable("(none)", string.IsNullOrEmpty(id))) { id = ""; changed = true; }
            foreach (var d in _defs.All(defType))
            {
                if (_search.Length > 0 && !d.DisplayName.Contains(_search, StringComparison.OrdinalIgnoreCase) &&
                    !d.Id.Contains(_search, StringComparison.OrdinalIgnoreCase)) continue;
                if (ImGui.Selectable($"{d.DisplayName}  [{d.Id}]", d.Id == id)) { id = d.Id; changed = true; }
            }
            ImGui.EndCombo();
        }
        // Assign the id from a just-finished "+ New" wizard to the field that opened it.
        var done = _pending.FirstOrDefault(p => p.FieldId == fieldId && p.Result is not null);
        if (done is not null) { id = done.Result!; changed = true; _pending.Remove(done); }
        if (editWidth > 0)
        {
            ImGui.SameLine();
            if (ImGui.Button("Edit")) _wizards.OpenEdit(current!);
        }
        return changed;
    }

    private sealed class PendingAssign(uint fieldId) { public uint FieldId { get; } = fieldId; public string? Result { get; set; } }
    private readonly List<PendingAssign> _pending = new();

    private string _texSearch = "";

    public bool TexturePicker(ref string key)
    {
        bool changed = false;
        string preview = string.IsNullOrEmpty(key) ? "(none)" : key;
        bool missing = !string.IsNullOrEmpty(key) && !_textures.Exists(key);
        if (missing) ImGui.PushStyleColor(ImGuiCol.Text, new Num.Vector4(1, 0.5f, 0.5f, 1));
        if (ImGui.BeginCombo("##tex", preview, ImGuiComboFlags.HeightLarge))
        {
            if (ImGui.IsWindowAppearing()) { _texSearch = ""; ImGui.SetKeyboardFocusHere(); }
            ImGui.InputTextWithHint("##search", "search art...", ref _texSearch, 100);
            if (ImGui.Selectable("(none)", string.IsNullOrEmpty(key))) { key = ""; changed = true; }
            foreach (var k in _textures.AllKeys())
            {
                if (_texSearch.Length > 0 && !k.Contains(_texSearch, StringComparison.OrdinalIgnoreCase)) continue;
                if (ImGui.Selectable(k, k == key)) { key = k; changed = true; }
                if (ImGui.IsItemHovered()) PreviewTooltip(k);
            }
            ImGui.EndCombo();
        }
        if (missing) { ImGui.PopStyleColor(); if (ImGui.IsItemHovered()) ImGui.SetTooltip("File not found in content/art"); }
        else if (!string.IsNullOrEmpty(key) && ImGui.IsItemHovered()) PreviewTooltip(key);
        return changed;
    }

    private void PreviewTooltip(string key)
    {
        var tex = _textures.Get(key);
        var id = ImGuiExtension.Instance!.Renderer.BindTexture(tex);
        float scale = Math.Min(1f, 256f / Math.Max(tex.Width, tex.Height));
        ImGui.BeginTooltip();
        ImGui.Image(id, new Num.Vector2(tex.Width * scale, tex.Height * scale));
        Ui.Colored(Ui.Muted, $"{tex.Width} x {tex.Height}");
        ImGui.EndTooltip();
    }

    private object EnumCombo(Type type, object value, out bool changed)
    {
        changed = false;
        string Name(object v) => type.GetField(v.ToString()!)?.GetCustomAttribute<LabelAttribute>()?.Text ?? SplitWords(v.ToString()!);
        bool flags = type.GetCustomAttribute<FlagsAttribute>() is not null;
        if (ImGui.BeginCombo("##enum", Name(value)))
        {
            foreach (var v in Enum.GetValues(type))
            {
                if (flags && Convert.ToInt64(v) == 0) continue;
                if (ImGui.Selectable(Name(v), v.Equals(value))) { value = v; changed = true; }
            }
            ImGui.EndCombo();
        }
        return value;
    }

    private CellCoord? _pickedCell;
    private object? _pickTarget;

    private CellCoord CellField(CellCoord c, out bool changed)
    {
        changed = false;
        int q = c.Q, r = c.R;
        float w = (ImGui.GetContentRegionAvail().X - (CellPicker is null ? 0 : 60)) / 2 - 4;
        ImGui.SetNextItemWidth(w);
        if (ImGui.InputInt("##q", ref q, 0)) changed = true;
        ImGui.SameLine();
        ImGui.SetNextItemWidth(w);
        if (ImGui.InputInt("##r", ref r, 0)) changed = true;
        if (CellPicker is not null)
        {
            ImGui.SameLine();
            uint myId = ImGui.GetID("pick");
            if (ImGui.Button("Pick"))
            {
                _pickTarget = myId;
                _pickedCell = null;
                CellPicker(cell => _pickedCell = cell);
            }
            if (ImGui.IsItemHovered()) ImGui.SetTooltip("Click a cell on the map to choose it.");
            if (_pickTarget is uint t && t == myId && _pickedCell is { } picked)
            {
                _pickTarget = null; _pickedCell = null;
                changed = true;
                return picked;
            }
        }
        return changed ? new CellCoord(q, r) : c;
    }
}
