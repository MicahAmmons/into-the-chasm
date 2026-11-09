using Chasm.Core.Grid;
using Chasm.Core.World;
using Chasm.Game.Input;
using Chasm.Tools.Scenes;
using Chasm.Tools.ImGuiHost;
using ImGuiNET;
using Num = System.Numerics;

namespace Chasm.Tools.MapEditor;

/// <summary>
/// The material card (Textures page and local materials): name, texture, live rotation / tint /
/// flips / cloud fade, and "Use for cut out" with on-map cell picking.
///
/// Cut-out cells are stored per tile as that tile's paint region for the material (the first
/// region on the tile that uses it), so the same floor material can be cut out differently on
/// every tile.
/// </summary>
public sealed partial class MapEditorScene
{
    private bool _focusMaterialName;

    /// <summary>Material reference ("id" or "local:id") whose cut-out cells the map is currently picking.</summary>
    private string? _cutOutMaterial;

    private void MaterialFields(MaterialDef m, bool isFloor)
    {
        string reference = (isFloor ? "" : "local:") + m.Id;
        const float label = 120;
        bool changed = false;

        // Name
        ImGui.TextUnformatted("Name"); ImGui.SameLine(label); ImGui.SetNextItemWidth(-1);
        if (_focusMaterialName) { ImGui.SetKeyboardFocusHere(); _focusMaterialName = false; }
        string name = m.Name;
        if (ImGui.InputText("##mname", ref name, 80)) { m.Name = name; changed = true; }

        // Texture (dropdown, '...' = library with folders, '+' = add from computer)
        ImGui.TextUnformatted("Texture"); ImGui.SameLine(label); ImGui.SetNextItemWidth(-1);
        string tex = m.Texture;
        ImGui.PushID("tex");
        if (ToolContext.Wizards.Inspector.TexturePicker(ref tex)) { m.Texture = tex; changed = true; }
        ImGui.PopID();
        if (string.IsNullOrEmpty(m.Texture)) Ui.Colored(Ui.Warn, "Pick a texture, or press + to add one from your computer.");

        // Rotation: slider + fine input + quick 90s, applied live.
        ImGui.TextUnformatted("Rotation"); ImGui.SameLine(label);
        float rot = m.Rotation;
        ImGui.SetNextItemWidth(ImGui.GetContentRegionAvail().X - 150);
        if (ImGui.SliderFloat("##rot", ref rot, 0, 360, "%.0f deg")) { m.Rotation = rot; changed = true; }
        if (ImGui.IsItemHovered()) ImGui.SetTooltip("Drag, or Ctrl+click to type an exact angle.");
        foreach (int q in new[] { 0, 90, 180, 270 })
        {
            ImGui.SameLine(0, 3);
            if (ImGui.Button(q + "##q" + q, new Num.Vector2(34, 0))) { m.Rotation = q; changed = true; }
        }

        // Tint: colour wheel picker.
        ImGui.TextUnformatted("Tint"); ImGui.SameLine(label);
        var col = HexToColor(m.Tint);
        ImGui.SetNextItemWidth(ImGui.GetContentRegionAvail().X - 70);
        if (ImGui.ColorEdit3("##tint", ref col, ImGuiColorEditFlags.PickerHueWheel | ImGuiColorEditFlags.DisplayHex | ImGuiColorEditFlags.InputRGB))
        {
            m.Tint = ColorToHex(col); changed = true;
        }
        if (ImGui.IsItemHovered()) ImGui.SetTooltip("Click the colour square for the colour wheel. White = no tint.");
        ImGui.SameLine(0, 4);
        if (ImGui.Button("Reset##tint")) { m.Tint = "#ffffff"; changed = true; }

        // Flips
        ImGui.TextUnformatted("Flip"); ImGui.SameLine(label);
        bool fx = m.FlipX, fy = m.FlipY;
        if (ImGui.Checkbox("Flip X", ref fx)) { m.FlipX = fx; changed = true; }
        ImGui.SameLine(0, 20);
        if (ImGui.Checkbox("Flip Y", ref fy)) { m.FlipY = fy; changed = true; }

        // Cloud fade
        ImGui.TextUnformatted("Cloud fade"); ImGui.SameLine(label); ImGui.SetNextItemWidth(-1);
        string[] fades = { "No fade", "Fades into the lower clouds", "Fades into the upper clouds" };
        int fade = (int)m.Fade;
        if (ImGui.Combo("##fade", ref fade, fades, fades.Length)) { m.Fade = (CloudFade)fade; changed = true; }

        // Cut out
        ImGui.Spacing();
        bool cut = m.UseForCutOut;
        if (ImGui.Checkbox("Use for cut out", ref cut))
        {
            m.UseForCutOut = cut; changed = true;
            if (!cut && _cutOutMaterial == reference) _cutOutMaterial = null;
        }
        if (ImGui.IsItemHovered()) ImGui.SetTooltip("Choose cells on this tile; the texture shows through those whole cells.");
        if (m.UseForCutOut) CutOutControls(m, reference);

        if (changed)
        {
            Edited();
            if (isFloor) Services.TileVisuals.InvalidateAll();
        }
    }

    private void CutOutControls(MaterialDef m, string reference)
    {
        var t = _tile!;
        var region = t.PaintRegions.FirstOrDefault(r => r.Material == reference);
        int count = region?.Cells.Count ?? 0;
        ImGui.Indent();
        bool picking = _cutOutMaterial == reference;
        if (Ui.ToggleButton(picking ? "Done selecting (Esc)" : "Select cells", picking, new Num.Vector2(190, 0),
                "Click or drag on the map to add cells; click a selected cell (or right-drag) to remove.\nThe texture shows through live."))
        {
            _cutOutMaterial = picking ? null : reference;
            if (!picking) Status($"Selecting cut-out cells for '{m.Name}'. Click/drag cells; Esc when done.");
        }
        ImGui.SameLine();
        Ui.Colored(count > 0 ? Ui.Good : Ui.Muted, $"{count} cell(s) on this tile");
        if (region is not null)
        {
            if (ImGui.Button("Clear cells")) { region.Cells.Clear(); t.PaintRegions.Remove(region); Edited(); }
            ImGui.SameLine();
            if (ImGui.Button("Region settings (layer, feather)")) { _selectedRegion = region; SetTool(Tool.PaintTexture); }
        }
        ImGui.Unindent();
    }

    /// <summary>Map clicks while picking cut-out cells. Returns true if it handled the input.</summary>
    private bool UpdateCutOutPicking(InputState input)
    {
        if (_cutOutMaterial is not { } reference) return false;
        var t = _tile!;
        if (t.FindMaterial(reference, _floor) is null) { _cutOutMaterial = null; return false; }
        if (input.Pressed(InputAction.Cancel) && !input.KeyboardCapturedByUi) { _cutOutMaterial = null; Status("Done selecting cut-out cells."); return true; }
        if (input.LeftReleased || input.RightReleased) _dragMode = null;
        PaintCells(input,
            c => t.PaintRegions.FirstOrDefault(r => r.Material == reference)?.Cells.Contains(c) == true,
            (c, v) =>
            {
                var region = t.PaintRegions.FirstOrDefault(r => r.Material == reference);
                if (region is null)
                {
                    if (!v) return;
                    region = new PaintRegion { Id = NextId("r", t.PaintRegions.Select(x => x.Id)), Material = reference };
                    t.PaintRegions.Add(region);
                }
                if (v) { if (!region.Cells.Contains(c)) region.Cells.Add(c); }
                else
                {
                    region.Cells.Remove(c);
                    if (region.Cells.Count == 0) t.PaintRegions.Remove(region);
                }
            }, rightPaintsFalse: true);
        return true;
    }

    private static Num.Vector3 HexToColor(string? hex)
    {
        if (hex is null) return Num.Vector3.One;
        hex = hex.TrimStart('#');
        if (hex.Length < 6 || !uint.TryParse(hex[..6], System.Globalization.NumberStyles.HexNumber, null, out uint v)) return Num.Vector3.One;
        return new Num.Vector3(((v >> 16) & 255) / 255f, ((v >> 8) & 255) / 255f, (v & 255) / 255f);
    }

    private static string ColorToHex(Num.Vector3 c) =>
        $"#{(int)MathF.Round(c.X * 255):x2}{(int)MathF.Round(c.Y * 255):x2}{(int)MathF.Round(c.Z * 255):x2}";
}
