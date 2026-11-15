using Chasm.Core.World;
using Chasm.Tools.ImGuiHost;
using Chasm.Tools.Scenes;
using ImGuiNET;
using Num = System.Numerics;

namespace Chasm.Tools.MapEditor;

/// <summary>
/// Clouds page: the floor's animated cloud layers (every tile) and this tile's overrides.
/// Layers draw in list order: the first is furthest back. Everything previews live.
/// </summary>
public sealed partial class MapEditorScene
{
    private string? _openCloud;      // layer id expanded on the Floor tab
    private string? _openTileCloud;  // layer id expanded on the This tile tab

    private void CloudsPanel()
    {
        if (_floor is null) { ImGui.TextDisabled("This tile has no floor, so it has no clouds."); return; }
        Ui.Hint("Animated, always behind the tile art, never baked. Tip: Game view (G) shows them without the grid.");
        if (!ImGui.BeginTabBar("cloudtabs")) return;
        if (ImGui.BeginTabItem("Floor (every tile)"))
        {
            FloorCloudsTab(_floor);
            ImGui.EndTabItem();
        }
        int overrides = _tile!.CloudOverrides.Count(o => _floor.Clouds.Any(c => c.Id == o.Layer));
        if (ImGui.BeginTabItem(overrides > 0 ? $"This tile ({overrides})###tilecl" : "This tile###tilecl"))
        {
            TileCloudsTab(_floor, _tile);
            ImGui.EndTabItem();
        }
        ImGui.EndTabBar();
    }

    private void FloorCloudsTab(FloorDef floor)
    {
        ImGui.PushStyleColor(ImGuiCol.Text, Ui.Warn);
        ImGui.TextWrapped("Floor-wide: every tile on this floor uses these (unless a tile overrides one).");
        ImGui.PopStyleColor();
        if (_openCloud is null && floor.Clouds.Count == 1) _openCloud = floor.Clouds[0].Id;
        Ui.Colored(Ui.Muted, "Top of the list = furthest back.");
        ImGui.BeginChild("floorclouds");
        int up = -1, down = -1, remove = -1, dup = -1;
        for (int i = 0; i < floor.Clouds.Count; i++)
        {
            var c = floor.Clouds[i];
            ImGui.PushID(c.Id);
            if (ImGui.ArrowButton("up", ImGuiDir.Up)) up = i;
            if (ImGui.IsItemHovered()) ImGui.SetTooltip("Move back");
            ImGui.SameLine(0, 2);
            if (ImGui.ArrowButton("down", ImGuiDir.Down)) down = i;
            if (ImGui.IsItemHovered()) ImGui.SetTooltip("Move forward");
            ImGui.SameLine();
            bool on = c.Enabled;
            if (ImGui.Checkbox("##on", ref on)) { c.Enabled = on; Edited(); }
            if (ImGui.IsItemHovered()) ImGui.SetTooltip("Show this layer");
            ImGui.SameLine();
            Thumb(c.Texture, 26);
            ImGui.SameLine();
            bool open = _openCloud == c.Id;
            if (ImGui.Selectable($"{c.Name}  [{c.Id}]", open, ImGuiSelectableFlags.None, new Num.Vector2(0, 26))) _openCloud = open ? null : c.Id;
            if (open)
            {
                ImGui.Indent();
                if (CloudFields(c)) Edited();
                if (ImGui.Button("Duplicate")) dup = i;
                ImGui.SameLine();
                if (ImGui.Button("Delete")) remove = i;
                int tileOverrides = Services.Defs.All<TileDef>().Count(t => t.Floor == floor.Number && t.CloudOverrides.Any(o => o.Layer == c.Id));
                if (tileOverrides > 0) Ui.Hint($"{tileOverrides} tile(s) override this layer.");
                ImGui.Unindent();
                ImGui.Spacing();
            }
            ImGui.PopID();
        }
        if (up > 0) { (floor.Clouds[up - 1], floor.Clouds[up]) = (floor.Clouds[up], floor.Clouds[up - 1]); Edited(); }
        if (down >= 0 && down < floor.Clouds.Count - 1) { (floor.Clouds[down + 1], floor.Clouds[down]) = (floor.Clouds[down], floor.Clouds[down + 1]); Edited(); }
        if (dup >= 0)
        {
            var copy = floor.Clouds[dup].Copy();
            copy.Id = CloudStack.NewId(floor);
            copy.Name += " copy";
            floor.Clouds.Insert(dup + 1, copy);
            _openCloud = copy.Id;
            Edited();
        }
        if (remove >= 0) { floor.Clouds.RemoveAt(remove); _openCloud = null; Edited(); }

        ImGui.Spacing();
        if (Ui.AccentButton("+ Add cloud layer"))
        {
            var layer = new CloudLayer { Id = CloudStack.NewId(floor), Name = $"Clouds {floor.Clouds.Count + 1}", Texture = "" };
            floor.Clouds.Add(layer);
            _openCloud = layer.Id;
            Edited();
            Art.ArtBrowser.OpenPick(null, k => { layer.Texture = k; Edited(); });
        }
        ImGui.EndChild();
    }

    private void TileCloudsTab(FloorDef floor, TileDef tile)
    {
        Ui.Hint("By default this tile shows the floor's clouds. Turn on Override to give one layer different settings here only (or hide it on this tile).");
        ImGui.BeginChild("tileclouds");
        if (floor.Clouds.Count == 0) ImGui.TextDisabled("The floor has no cloud layers yet (Floor tab).");
        foreach (var c in floor.Clouds)
        {
            ImGui.PushID(c.Id);
            var o = tile.CloudOverrides.FirstOrDefault(x => x.Layer == c.Id);
            bool has = o is not null;
            if (ImGui.Checkbox("Override", ref has))
            {
                if (has) { o = new TileCloudOverride { Layer = c.Id, Settings = c.Copy() }; tile.CloudOverrides.Add(o); _openTileCloud = c.Id; }
                else { tile.CloudOverrides.RemoveAll(x => x.Layer == c.Id); o = null; }
                Edited();
            }
            ImGui.SameLine();
            Thumb((o?.Settings ?? c).Texture, 26);
            ImGui.SameLine();
            bool open = _openTileCloud == c.Id && o is not null;
            string state = o is null ? "(floor settings)" : o.Settings.Enabled ? "(overridden here)" : "(hidden on this tile)";
            if (ImGui.Selectable($"{c.Name}  {state}", open, ImGuiSelectableFlags.None, new Num.Vector2(0, 26)) && o is not null)
                _openTileCloud = open ? null : c.Id;
            if (open && o is not null)
            {
                ImGui.Indent();
                o.Settings.Id = c.Id;
                if (CloudFields(o.Settings, nameEditable: false)) Edited();
                if (ImGui.Button("Copy floor settings again")) { o.Settings = c.Copy(); Edited(); }
                ImGui.SameLine();
                if (ImGui.Button("Remove override")) { tile.CloudOverrides.Remove(o); Edited(); }
                ImGui.Unindent();
                ImGui.Spacing();
            }
            ImGui.PopID();
        }
        // Overrides for layers the floor no longer has.
        int stale = tile.CloudOverrides.RemoveAll(x => floor.Clouds.All(c => c.Id != x.Layer));
        if (stale > 0) Edited();
        ImGui.EndChild();
    }

    /// <summary>Every cloud setting, applied live. Returns true when something changed.</summary>
    private bool CloudFields(CloudLayer c, bool nameEditable = true)
    {
        const float label = 110;
        bool changed = false;
        if (nameEditable)
        {
            ImGui.TextUnformatted("Name"); ImGui.SameLine(label); ImGui.SetNextItemWidth(-1);
            string name = c.Name;
            if (ImGui.InputText("##cname", ref name, 80)) { c.Name = name; changed = true; }
        }
        ImGui.TextUnformatted("Show"); ImGui.SameLine(label);
        bool en = c.Enabled;
        if (ImGui.Checkbox("##cen", ref en)) { c.Enabled = en; changed = true; }

        ImGui.TextUnformatted("Texture"); ImGui.SameLine(label); ImGui.SetNextItemWidth(-1);
        string tex = c.Texture;
        ImGui.PushID("ctex");
        if (ToolContext.Wizards.Inspector.TexturePicker(ref tex)) { c.Texture = tex; changed = true; }
        ImGui.PopID();
        if (string.IsNullOrEmpty(c.Texture)) Ui.Colored(Ui.Warn, "Pick a cloud texture (seamless, e.g. 2048x2048).");

        ImGui.TextUnformatted("Tint"); ImGui.SameLine(label);
        var col = HexToColor(c.Tint);
        ImGui.SetNextItemWidth(ImGui.GetContentRegionAvail().X - 64);
        if (ImGui.ColorEdit3("##ctint", ref col, ImGuiColorEditFlags.PickerHueWheel | ImGuiColorEditFlags.DisplayHex)) { c.Tint = ColorToHex(col); changed = true; }
        ImGui.SameLine(0, 4);
        if (ImGui.Button("Reset##ct")) { c.Tint = "#ffffff"; changed = true; }

        ImGui.TextUnformatted("Opacity"); ImGui.SameLine(label); ImGui.SetNextItemWidth(-1);
        float op = c.Opacity * 100;
        if (ImGui.SliderFloat("##cop", ref op, 0, 100, "%.0f%%")) { c.Opacity = op / 100f; changed = true; }

        ImGui.TextUnformatted("Blend"); ImGui.SameLine(label); ImGui.SetNextItemWidth(-1);
        string[] blends = { "Normal (uses the PNG's transparency)", "Screen (black = see-through)", "Add (glowing)" };
        int bl = (int)c.Blend;
        if (ImGui.Combo("##cbl", ref bl, blends, blends.Length)) { c.Blend = (CloudBlend)bl; changed = true; }

        ImGui.TextUnformatted("Speed"); ImGui.SameLine(label); ImGui.SetNextItemWidth(-1);
        float sp = c.Speed;
        if (ImGui.SliderFloat("##csp", ref sp, 0, 600, "%.0f px/s", ImGuiSliderFlags.Logarithmic)) { c.Speed = sp; changed = true; }

        // Direction: dial + slider + quick buttons.
        ImGui.TextUnformatted("Direction"); ImGui.SameLine(label);
        float dir = c.Direction;
        if (DirectionDial("##cdial", ref dir)) { c.Direction = dir; changed = true; }
        ImGui.SameLine();
        ImGui.BeginGroup();
        ImGui.SetNextItemWidth(-1);
        if (ImGui.SliderFloat("##cdir", ref dir, 0, 360, "%.0f deg")) { c.Direction = dir; changed = true; }
        foreach (var (lbl, deg, tip) in new[] { ("->", 0f, "Right"), ("v", 90f, "Down"), ("<-", 180f, "Left"), ("^", 270f, "Up") })
        {
            if (ImGui.Button(lbl + "##d" + deg, new Num.Vector2(34, 0))) { c.Direction = deg; changed = true; }
            if (ImGui.IsItemHovered()) ImGui.SetTooltip(tip);
            ImGui.SameLine(0, 3);
        }
        if (ImGui.Button("Reverse")) { c.Direction = (c.Direction + 180) % 360; changed = true; }
        if (ImGui.IsItemHovered()) ImGui.SetTooltip("Flow the opposite way (+180 deg)");
        ImGui.EndGroup();

        ImGui.TextUnformatted("Scale"); ImGui.SameLine(label); ImGui.SetNextItemWidth(-1);
        float sc = c.Scale;
        if (ImGui.SliderFloat("##csc", ref sc, 0.1f, 4f, "%.2fx", ImGuiSliderFlags.Logarithmic)) { c.Scale = sc; changed = true; }

        // Pattern: extra motion on top of the scroll.
        ImGui.TextUnformatted("Pattern"); ImGui.SameLine(label); ImGui.SetNextItemWidth(-1);
        string[] patterns = { "Scroll (straight drift)", "Wave (snakes side to side)", "Swirl (loops in circles)", "Gusts (surges and eases)",
            "Ripple (wavy bands, like haze)", "Breathe (swells and shrinks)", "Shimmer (fades in and out)" };
        int pat = (int)c.Pattern;
        if (ImGui.Combo("##cpat", ref pat, patterns, patterns.Length)) { c.Pattern = (CloudPattern)pat; changed = true; }
        if (ImGui.IsItemHovered()) ImGui.SetTooltip("Plays on top of the scroll. Set Speed to 0 for motion in place.");
        if (c.Pattern != CloudPattern.Scroll)
        {
            bool percent = c.Pattern is CloudPattern.Breathe or CloudPattern.Shimmer;
            ImGui.TextUnformatted("Amount"); ImGui.SameLine(label); ImGui.SetNextItemWidth(-1);
            float amt = c.PatternAmount;
            if (ImGui.SliderFloat("##camt", ref amt, 0, percent ? 100 : 400, percent ? "%.0f%%" : "%.0f px")) { c.PatternAmount = amt; changed = true; }
            if (ImGui.IsItemHovered()) ImGui.SetTooltip(c.Pattern switch
            {
                CloudPattern.Wave => "How far it snakes to each side.",
                CloudPattern.Swirl => "Radius of the loops.",
                CloudPattern.Gusts => "How far each surge pushes ahead.",
                CloudPattern.Ripple => "How far the bands bend sideways.",
                CloudPattern.Breathe => "How much bigger it swells.",
                _ => "How much it fades at the dimmest point.",
            });
            ImGui.TextUnformatted("Cycle"); ImGui.SameLine(label); ImGui.SetNextItemWidth(-1);
            float cyc = c.PatternCycle;
            if (ImGui.SliderFloat("##ccyc", ref cyc, 0.5f, 120f, "%.1f s", ImGuiSliderFlags.Logarithmic)) { c.PatternCycle = cyc; changed = true; }
            if (ImGui.IsItemHovered()) ImGui.SetTooltip("Seconds for one full cycle. Lower = faster.");
            if (c.Pattern == CloudPattern.Ripple)
            {
                ImGui.TextUnformatted("Wave length"); ImGui.SameLine(label); ImGui.SetNextItemWidth(-1);
                float rl = c.RippleLength;
                if (ImGui.SliderFloat("##crl", ref rl, 40, 2000, "%.0f px", ImGuiSliderFlags.Logarithmic)) { c.RippleLength = rl; changed = true; }
                if (ImGui.IsItemHovered()) ImGui.SetTooltip("Distance between wave crests, top to bottom.");
            }
        }

        ImGui.TextUnformatted("Mirror"); ImGui.SameLine(label);
        bool mi = c.Mirror;
        if (ImGui.Checkbox("Flip every other copy##cmi", ref mi)) { c.Mirror = mi; changed = true; }
        if (ImGui.IsItemHovered()) ImGui.SetTooltip("Hides seams on art that isn't perfectly seamless. Leave off for truly seamless textures.");
        return changed;
    }

    /// <summary>A small round dial: drag to point the direction (screen angles: 0 = right, 90 = down). Shift snaps to 15 deg.</summary>
    private static bool DirectionDial(string id, ref float degrees)
    {
        const float r = 24;
        var p = ImGui.GetCursorScreenPos();
        var centre = p + new Num.Vector2(r, r);
        ImGui.InvisibleButton(id, new Num.Vector2(r * 2, r * 2));
        bool changed = false;
        if (ImGui.IsItemActive())
        {
            var d = ImGui.GetIO().MousePos - centre;
            if (d.LengthSquared() > 4)
            {
                float a = MathF.Atan2(d.Y, d.X) * 180f / MathF.PI;
                if (a < 0) a += 360;
                if (ImGui.GetIO().KeyShift) a = MathF.Round(a / 15f) * 15f % 360;
                if (MathF.Abs(a - degrees) > 0.01f) { degrees = a; changed = true; }
            }
        }
        if (ImGui.IsItemHovered()) ImGui.SetTooltip("Drag to set the direction (Shift snaps to 15 deg).");
        var dl = ImGui.GetWindowDrawList();
        uint bg = ImGui.GetColorU32(ImGuiCol.FrameBg), rim = ImGui.GetColorU32(ImGuiCol.Border), accent = ImGui.GetColorU32(Ui.Accent);
        dl.AddCircleFilled(centre, r, bg, 32);
        dl.AddCircle(centre, r, rim, 32, 1.5f);
        float rad = degrees * MathF.PI / 180f;
        var tip = centre + new Num.Vector2(MathF.Cos(rad), MathF.Sin(rad)) * (r - 4);
        dl.AddLine(centre, tip, accent, 2.5f);
        // Arrow head.
        var back = new Num.Vector2(MathF.Cos(rad), MathF.Sin(rad)) * 7;
        var side = new Num.Vector2(-MathF.Sin(rad), MathF.Cos(rad)) * 4;
        dl.AddTriangleFilled(tip, tip - back + side, tip - back - side, accent);
        dl.AddCircleFilled(centre, 2.5f, accent);
        return changed;
    }
}
