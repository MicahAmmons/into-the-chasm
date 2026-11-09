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
        if (m.Fade != CloudFade.None)
        {
            float start = m.FadeStart, length = m.FadeLength;
            var angle = m.FadeAngle;
            if (FadeControls(label, ref start, ref length, ref angle, m.Fade == CloudFade.IntoLowerClouds))
            { m.FadeStart = start; m.FadeLength = length; m.FadeAngle = angle; changed = true; }
        }

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
        if (region is not null && m.Fade != CloudFade.None) RegionFadeOverride(region, m);
        if (region is not null)
        {
            if (ImGui.Button("Clear cells")) { region.Cells.Clear(); t.PaintRegions.Remove(region); Edited(); }
            ImGui.SameLine();
            if (ImGui.Button("Region settings (layer, feather)")) { _selectedRegion = region; SetTool(Tool.PaintTexture); }
        }
        ImGui.Unindent();
    }

    /// <summary>Fade start / length (cells) and angle. Returns true when changed.</summary>
    private static bool FadeControls(float label, ref float start, ref float length, ref FadeAngle angle, bool lower)
    {
        bool changed = false;
        string edge = lower ? "top edge" : "bottom edge";
        ImGui.TextUnformatted("Fade starts"); ImGui.SameLine(label); ImGui.SetNextItemWidth(-1);
        if (ImGui.SliderFloat("##fstart", ref start, 0, 8, "%.2f cells")) changed = true;
        if (ImGui.IsItemHovered()) ImGui.SetTooltip($"How far from the {edge} the fade begins (1 cell = 64 px). Ctrl+click to type.");
        ImGui.TextUnformatted("Fade length"); ImGui.SameLine(label); ImGui.SetNextItemWidth(-1);
        if (ImGui.SliderFloat("##flen", ref length, 0.1f, 8, "%.2f cells")) changed = true;
        if (ImGui.IsItemHovered()) ImGui.SetTooltip("How far it takes to go from solid to fully faded.");
        ImGui.TextUnformatted("Fade angle"); ImGui.SameLine(label); ImGui.SetNextItemWidth(-1);
        string[] angles = { "Auto (follow the cliff edge)", "Down-right  \\  (cell edge)", "Down-left  /  (cell edge)", "Level  -" };
        int a = (int)angle;
        if (ImGui.Combo("##fangle", ref a, angles, angles.Length)) { angle = (FadeAngle)a; changed = true; }
        return changed;
    }

    /// <summary>"Own fade on this tile": per-region override of the material's fade numbers.</summary>
    private void RegionFadeOverride(PaintRegion region, MaterialDef m)
    {
        bool own = region.FadeStart is not null || region.FadeLength is not null || region.FadeAngle is not null;
        if (ImGui.Checkbox("Own fade on this tile", ref own))
        {
            if (own) { region.FadeStart = m.FadeStart; region.FadeLength = m.FadeLength; region.FadeAngle = m.FadeAngle; }
            else { region.FadeStart = null; region.FadeLength = null; region.FadeAngle = null; }
            Edited();
        }
        if (ImGui.IsItemHovered()) ImGui.SetTooltip("Off: uses the material's fade. On: this tile's cells get their own fade numbers.");
        if (!own) return;
        ImGui.PushID("regionfade");
        float start = region.FadeStart ?? m.FadeStart, length = region.FadeLength ?? m.FadeLength;
        var angle = region.FadeAngle ?? m.FadeAngle;
        if (FadeControls(120, ref start, ref length, ref angle, m.Fade == CloudFade.IntoLowerClouds))
        { region.FadeStart = start; region.FadeLength = length; region.FadeAngle = angle; Edited(); }
        ImGui.PopID();
    }

    // ------------------------------------------------------------------ fade lines on the map

    private string? _fadeLinesKey;
    private float[]? _fadeEdge;

    /// <summary>
    /// The region whose fade lines to show: the one being cut out, else the selected region.
    /// Draws the detected edge (white dashes), where the fade starts (orange) and where it's gone (faint).
    /// </summary>
    private void DrawFadeLines(Microsoft.Xna.Framework.Graphics.SpriteBatch b)
    {
        var t = _tile!;
        PaintRegion? region = _cutOutMaterial is { } cut ? t.PaintRegions.FirstOrDefault(r => r.Material == cut) : _selectedRegion;
        if (region is null || region.Cells.Count == 0 || !t.PaintRegions.Contains(region)) return;
        var mat = t.FindMaterial(region.Material, _floor);
        if (mat is null || region.FadeFor(mat) is not { } fade) return;
        string key = string.Join(' ', region.Cells.OrderBy(c => c.Q).ThenBy(c => c.R)) + "|" + fade;
        if (key != _fadeLinesKey)
        {
            _fadeLinesKey = key;
            _fadeEdge = RegionMask.Edge(RegionMask.Inside(region.Cells), fade);
        }
        var edge = _fadeEdge!;
        float dir = fade.Lower ? 1 : -1;
        float start = fade.StartCells * EdgeFade.CellPx, end = start + fade.LengthCells * EdgeFade.CellPx;
        var d = Services.Draw;
        var white = Microsoft.Xna.Framework.Color.White;
        var orange = new Microsoft.Xna.Framework.Color(255, 170, 60);
        const int step = 4; // half-res columns per segment
        for (int x = 0; x + step < RegionMask.Width; x += step)
        {
            if (float.IsNaN(edge[x]) || float.IsNaN(edge[x + step])) continue;
            var p0 = new Microsoft.Xna.Framework.Vector2(x * 2, edge[x] * 2);
            var p1 = new Microsoft.Xna.Framework.Vector2((x + step) * 2, edge[x + step] * 2);
            if ((x / step) % 3 != 2) d.Line(b, p0, p1, white * 0.9f, 2);
            d.Line(b, p0 + new Microsoft.Xna.Framework.Vector2(0, dir * start), p1 + new Microsoft.Xna.Framework.Vector2(0, dir * start), orange, 2);
            if ((x / step) % 2 == 0) d.Line(b, p0 + new Microsoft.Xna.Framework.Vector2(0, dir * end), p1 + new Microsoft.Xna.Framework.Vector2(0, dir * end), orange * 0.55f, 2);
        }
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
