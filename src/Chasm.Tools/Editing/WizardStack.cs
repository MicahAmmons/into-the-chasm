using Chasm.Tools.ImGuiHost;
using System.Reflection;
using Chasm.Core.Content;
using Chasm.Core.Serialization;
using Chasm.Game.Hosting;
using ImGuiNET;
using Num = System.Numerics;

namespace Chasm.Tools.Editing;

/// <summary>
/// The open "New X" / "Edit X" windows. Wizards can open wizards: creating an item from inside a
/// placement's condition opens the item wizard on top; saving it fills in the field that asked.
/// </summary>
public sealed class WizardStack
{
    private readonly List<DefEditorWindow> _open = new();
    private GameServices _services = null!;
    public Inspector Inspector { get; private set; } = null!;

    public void Initialize(GameServices services)
    {
        _services = services;
        Inspector = new Inspector(services.Defs, services.Textures, this);
    }

    public bool AnyOpen => _open.Count > 0;

    public void OpenNew(Type defType, Action<string>? onSaved = null, Def? template = null)
    {
        var def = template ?? (Def)Activator.CreateInstance(defType)!;
        _open.Add(new DefEditorWindow(_services, this, def, isNew: true, onSaved));
    }

    public void OpenEdit(Def def, Action<string>? onSaved = null)
    {
        if (_open.Any(w => !w.IsNew && w.OriginalId == def.Id && w.DefType == def.GetType())) return;
        _open.Add(new DefEditorWindow(_services, this, ChasmJson.Clone(def), isNew: false, onSaved));
    }

    public void Draw()
    {
        Chasm.Tools.Art.ArtBrowser.Draw(_services);
        for (int i = 0; i < _open.Count; i++)
        {
            if (!_open[i].Draw()) { _open.RemoveAt(i); i--; }
        }
    }
}

/// <summary>
/// Create / edit one definition. Required fields are asked first ("the questions"), the rest is
/// tucked under "More options". Problems (missing references, empty required fields) are listed
/// live; you can still save, so half-finished content is never lost.
/// </summary>
public sealed class DefEditorWindow
{
    private readonly GameServices _s;
    private readonly WizardStack _stack;
    private readonly Def _def;
    private readonly Action<string>? _onSaved;
    private readonly string _windowId = Guid.NewGuid().ToString("N")[..8];
    private bool _idEdited;
    private bool _open = true;
    private bool _dirty;
    private string? _error;

    public bool IsNew { get; }
    public string OriginalId { get; }
    public Type DefType => _def.GetType();

    public DefEditorWindow(GameServices s, WizardStack stack, Def def, bool isNew, Action<string>? onSaved)
    {
        _s = s; _stack = stack; _def = def; IsNew = isNew; _onSaved = onSaved;
        OriginalId = def.Id;
        _idEdited = !isNew || !string.IsNullOrEmpty(def.Id);
    }

    /// <summary>Returns false when the window closed.</summary>
    public bool Draw()
    {
        var folder = DefRegistry.FolderOf(DefType);
        string kind = folder.DisplayName.TrimEnd('s');
        string title = IsNew ? $"New {kind}" : $"Edit {kind}: {_def.DisplayName}";
        ImGui.SetNextWindowSize(Ui.FitSize(620, 680), ImGuiCond.FirstUseEver);
        ImGui.SetNextWindowPos(ImGui.GetIO().DisplaySize / 2, ImGuiCond.FirstUseEver, new Num.Vector2(0.5f, 0.5f));
        if (!ImGui.Begin($"{title}{(_dirty ? " *" : "")}###wiz{_windowId}", ref _open, ImGuiWindowFlags.NoCollapse))
        {
            ImGui.End();
            return _open;
        }

        Ui.KeepOnScreen();
        if (DefType.GetCustomAttribute<TooltipAttribute>() is { } tip) ImGui.TextDisabled(tip.Text);
        ImGui.Separator();

        // Name, then id (auto-suggested from the name for new content).
        string name = _def.Name;
        ImGui.AlignTextToFramePadding(); ImGui.TextColored(new Num.Vector4(1f, 0.8f, 0.4f, 1f), "Name *"); ImGui.SameLine(170);
        ImGui.SetNextItemWidth(-1);
        if (ImGui.InputText("##name", ref name, 200))
        {
            _def.Name = name; _dirty = true;
            if (!_idEdited) _def.Id = _s.Defs.SuggestId(DefType, name);
        }
        string id = _def.Id;
        ImGui.AlignTextToFramePadding(); ImGui.TextUnformatted("Id"); ImGui.SameLine(170);
        ImGui.SetNextItemWidth(-1);
        if (IsNew)
        {
            if (ImGui.InputText("##id", ref id, 120)) { _def.Id = id.Trim(); _idEdited = true; _dirty = true; }
        }
        else Ui.Colored(Ui.Muted, $"{id}   (ids can't change once saved: other content points to them)");

        var inspector = _stack.Inspector;
        static bool IsHeader(PropertyInfo p) => p.Name is nameof(Def.Id) or nameof(Def.Name) or nameof(Def.Notes);
        bool Required(PropertyInfo p) => p.GetCustomAttribute<RequiredAttribute>() is not null;

        ImGui.BeginChild("form", new Num.Vector2(0, -90));
        if (Inspector.EditableProperties(DefType).Any(p => !IsHeader(p) && Required(p)))
        {
            ImGui.SeparatorText("Required");
            _dirty |= inspector.Edit(_def, p => !IsHeader(p) && Required(p));
        }
        ImGui.SeparatorText("Details");
        _dirty |= inspector.Edit(_def, p => !IsHeader(p) && !Required(p));
        ImGui.Spacing();
        _dirty |= inspector.Edit(_def, p => p.Name == nameof(Def.Notes));
        ImGui.EndChild();

        var problems = ContentValidator.Validate(_def, _s.Defs);
        if (IsNew && _s.Defs.Exists(DefType, _def.Id)) problems.Insert(0, $"Id '{_def.Id}' is already used.");
        ImGui.Separator();
        if (problems.Count == 0) ImGui.TextColored(new Num.Vector4(0.5f, 1f, 0.6f, 1), "Looks good.");
        else
        {
            ImGui.TextColored(new Num.Vector4(1f, 0.6f, 0.4f, 1), $"{problems.Count} thing(s) to check:");
            if (ImGui.IsItemHovered()) ImGui.SetTooltip(string.Join("\n", problems));
            ImGui.SameLine();
            ImGui.TextDisabled(problems[0]);
        }
        if (_error is not null) ImGui.TextColored(new Num.Vector4(1, 0.3f, 0.3f, 1), _error);

        bool canSave = !string.IsNullOrWhiteSpace(_def.Id) && !(IsNew && _s.Defs.Exists(DefType, _def.Id));
        if (!canSave) ImGui.BeginDisabled();
        if (ImGui.Button(IsNew ? "Create" : "Save", new Num.Vector2(120, 0))) Save();
        if (!canSave) ImGui.EndDisabled();
        ImGui.SameLine();
        if (ImGui.Button(_dirty ? "Discard" : "Close", new Num.Vector2(120, 0))) _open = false;
        ImGui.End();
        return _open;
    }

    private void Save()
    {
        try
        {
            _s.DefReload.IgnoreNextChange(_s.Defs.PathFor(DefType, _def.Id));
            if (_def is Chasm.Core.World.TileDef t) t.CommitWalkable();
            _s.Defs.Save(ChasmJson.Clone(_def));
            _onSaved?.Invoke(_def.Id);
            _open = false;
        }
        catch (Exception e)
        {
            _error = "Save failed: " + e.Message;
        }
    }
}
