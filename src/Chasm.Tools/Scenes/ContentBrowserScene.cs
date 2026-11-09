using Chasm.Tools.MapEditor;
using Chasm.Core.Content;
using Chasm.Core.Serialization;
using Chasm.Game.Scenes;
using ImGuiNET;
using Microsoft.Xna.Framework.Graphics;
using Num = System.Numerics;

namespace Chasm.Tools.Scenes;

/// <summary>Browse every definition by type; create, duplicate, edit, delete.</summary>
public sealed class ContentBrowserScene : Scene
{
    private Type _type = DefRegistry.DefTypes.First();
    private string _search = "";
    private string? _confirmDelete;

    private readonly string? _openOnStart;
    public ContentBrowserScene(string? openOnStart = null) => _openOnStart = openOnStart;

    public override void OnEnter()
    {
        ToolContext.EnsureInitialized(Services);
        // "--mode content --arg tiles/f1_0_0" opens that def's editor; "--arg new:items" starts a wizard.
        if (_openOnStart is { } arg)
        {
            var parts = arg.Split(new[] { '/', ':' }, 2);
            var type = DefRegistry.DefTypes.FirstOrDefault(t => DefRegistry.FolderOf(t).Folder == parts[^1] || DefRegistry.FolderOf(t).Folder == parts[0]);
            if (parts[0] == "new" && type is not null) { _type = type; ToolContext.Wizards.OpenNew(type); }
            else if (type is not null && parts.Length == 2 && Services.Defs.Find(type, parts[1]) is { } d) { _type = type; ToolContext.Wizards.OpenEdit(d); }
        }
    }

    public override void Update(float dt)
    {
        ToolContext.MenuBar(Services, "Content Browser");
        var io = ImGui.GetIO();
        ImGui.SetNextWindowPos(new Num.Vector2(0, 24));
        ImGui.SetNextWindowSize(new Num.Vector2(io.DisplaySize.X, io.DisplaySize.Y - 24));
        ImGui.Begin("Content", ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoMove | ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.NoTitleBar);

        ImGui.BeginChild("types", new Num.Vector2(220, 0), ImGuiChildFlags.Borders);
        foreach (var t in DefRegistry.DefTypes)
        {
            var f = DefRegistry.FolderOf(t);
            if (ImGui.Selectable($"{f.DisplayName} ({Services.Defs.All(t).Count()})", t == _type)) _type = t;
        }
        ImGui.EndChild();
        ImGui.SameLine();

        ImGui.BeginChild("list", new Num.Vector2(0, 0), ImGuiChildFlags.Borders);
        var folder = DefRegistry.FolderOf(_type);
        if (ImGui.Button($"+ New {folder.DisplayName.TrimEnd('s')}")) ToolContext.Wizards.OpenNew(_type);
        ImGui.SameLine();
        ImGui.SetNextItemWidth(300);
        ImGui.InputTextWithHint("##search", "search...", ref _search, 100);
        ImGui.SameLine();
        ImGui.TextDisabled($"content/defs/{folder.Folder}/");
        ImGui.Separator();

        if (ImGui.BeginTable("defs", 4, ImGuiTableFlags.RowBg | ImGuiTableFlags.BordersInnerH | ImGuiTableFlags.ScrollY))
        {
            ImGui.TableSetupColumn("Name", ImGuiTableColumnFlags.WidthStretch, 2);
            ImGui.TableSetupColumn("Id", ImGuiTableColumnFlags.WidthStretch, 2);
            ImGui.TableSetupColumn("Status", ImGuiTableColumnFlags.WidthStretch, 3);
            ImGui.TableSetupColumn("", ImGuiTableColumnFlags.WidthFixed, 230);
            ImGui.TableHeadersRow();
            foreach (var def in Services.Defs.All(_type).ToList())
            {
                if (_search.Length > 0 && !def.DisplayName.Contains(_search, StringComparison.OrdinalIgnoreCase) &&
                    !def.Id.Contains(_search, StringComparison.OrdinalIgnoreCase)) continue;
                ImGui.PushID(def.Id);
                ImGui.TableNextRow();
                ImGui.TableNextColumn();
                if (ImGui.Selectable(def.DisplayName, false, ImGuiSelectableFlags.AllowDoubleClick) && ImGui.IsMouseDoubleClicked(0))
                    ToolContext.Wizards.OpenEdit(def);
                ImGui.TableNextColumn(); ImGui.TextDisabled(def.Id);
                ImGui.TableNextColumn();
                var problems = ContentValidator.Validate(def, Services.Defs);
                if (problems.Count == 0) ImGui.TextColored(new Num.Vector4(0.5f, 1, 0.6f, 1), "ok");
                else
                {
                    ImGui.TextColored(new Num.Vector4(1, 0.6f, 0.4f, 1), problems[0]);
                    if (ImGui.IsItemHovered()) ImGui.SetTooltip(string.Join("\n", problems));
                }
                ImGui.TableNextColumn();
                if (ImGui.SmallButton("Edit")) ToolContext.Wizards.OpenEdit(def);
                ImGui.SameLine();
                if (ImGui.SmallButton("Duplicate"))
                {
                    var copy = ChasmJson.Clone(def);
                    copy.Id = Services.Defs.SuggestId(_type, def.Id);
                    copy.Name = def.Name + " (copy)";
                    ToolContext.Wizards.OpenNew(_type, null, copy);
                }
                if (def is Chasm.Core.World.TileDef tile)
                {
                    ImGui.SameLine();
                    if (ImGui.SmallButton("Map")) Services.Scenes.ResetTo(new MapEditorScene(tile.Id));
                }
                ImGui.SameLine();
                if (ImGui.SmallButton("Delete")) { _confirmDelete = def.Id; ImGui.OpenPopup("Delete?"); }
                if (ImGui.BeginPopupModal("Delete?", ImGuiWindowFlags.AlwaysAutoResize))
                {
                    ImGui.Text($"Delete '{_confirmDelete}'? Its JSON file is removed. Anything pointing to it will show as missing.");
                    if (ImGui.Button("Delete", new Num.Vector2(120, 0))) { Services.Defs.Delete(_type, _confirmDelete!); ImGui.CloseCurrentPopup(); }
                    ImGui.SameLine();
                    if (ImGui.Button("Cancel", new Num.Vector2(120, 0))) ImGui.CloseCurrentPopup();
                    ImGui.EndPopup();
                }
                ImGui.PopID();
            }
            ImGui.EndTable();
        }
        ImGui.EndChild();
        ImGui.End();
        ToolContext.Wizards.Draw();
    }

    public override void Draw(SpriteBatch batch) { }
}
