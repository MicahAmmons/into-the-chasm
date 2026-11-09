using ImGuiNET;
using Num = System.Numerics;

namespace Chasm.Tools.ImGuiHost;

/// <summary>Small helpers that give every tool the same clean look.</summary>
public static class Ui
{
    public static ImFontPtr HeaderFont;
    public static ImFontPtr TitleFont;

    public static readonly Num.Vector4 Accent = new(0.98f, 0.72f, 0.30f, 1f);
    public static readonly Num.Vector4 AccentDark = new(0.62f, 0.42f, 0.12f, 1f);
    public static readonly Num.Vector4 Muted = new(0.55f, 0.59f, 0.68f, 1f);
    public static readonly Num.Vector4 Good = new(0.45f, 0.88f, 0.55f, 1f);
    public static readonly Num.Vector4 Warn = new(1f, 0.66f, 0.36f, 1f);
    public static readonly Num.Vector4 Bad = new(1f, 0.45f, 0.42f, 1f);
    public static readonly Num.Vector4 Blue = new(0.45f, 0.65f, 1f, 1f);

    private static bool HasFont(ImFontPtr f) { unsafe { return f.NativePtr != null; } }

    /// <summary>A small caps-style section title with a thin rule under it.</summary>
    public static void Section(string text, bool first = false)
    {
        if (!first) ImGui.Dummy(new Num.Vector2(0, 4));
        if (HasFont(HeaderFont)) ImGui.PushFont(HeaderFont);
        Colored(Muted, text.ToUpperInvariant());
        if (HasFont(HeaderFont)) ImGui.PopFont();
        var dl = ImGui.GetWindowDrawList();
        var p = ImGui.GetCursorScreenPos();
        dl.AddLine(p with { Y = p.Y - 2 }, new Num.Vector2(p.X + ImGui.GetContentRegionAvail().X, p.Y - 2), ImGui.GetColorU32(ImGuiCol.Separator));
        ImGui.Dummy(new Num.Vector2(0, 1));
    }

    public static void Title(string text)
    {
        if (HasFont(TitleFont)) ImGui.PushFont(TitleFont);
        ImGui.TextUnformatted(text);
        if (HasFont(TitleFont)) ImGui.PopFont();
    }

    /// <summary>A text box in the larger title font (tile name).</summary>
    public static bool TitleFontInput(string id, string hint, ref string value)
    {
        if (HasFont(TitleFont)) ImGui.PushFont(TitleFont);
        ImGui.PushStyleColor(ImGuiCol.FrameBg, new Num.Vector4(0, 0, 0, 0));
        bool r = ImGui.InputTextWithHint(id, hint, ref value, 120);
        ImGui.PopStyleColor();
        if (HasFont(TitleFont)) ImGui.PopFont();
        return r;
    }

    /// <summary>Muted helper text that wraps to the panel width.</summary>
    public static void Hint(string text)
    {
        ImGui.PushStyleColor(ImGuiCol.Text, Muted);
        ImGui.TextWrapped(text);
        ImGui.PopStyleColor();
    }

    /// <summary>A button in the accent colour (the main action on a panel, e.g. Save).</summary>
    public static bool AccentButton(string label, Num.Vector2 size = default)
    {
        ImGui.PushStyleColor(ImGuiCol.Button, AccentDark);
        ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new Num.Vector4(0.78f, 0.54f, 0.18f, 1f));
        ImGui.PushStyleColor(ImGuiCol.ButtonActive, Accent);
        ImGui.PushStyleColor(ImGuiCol.Text, new Num.Vector4(1f, 0.97f, 0.9f, 1f));
        bool r = ImGui.Button(label, size);
        ImGui.PopStyleColor(4);
        return r;
    }

    /// <summary>A button that shows an on/off state (tools, panel pages).</summary>
    public static bool ToggleButton(string label, bool active, Num.Vector2 size, string? tooltip = null)
    {
        if (active)
        {
            ImGui.PushStyleColor(ImGuiCol.Button, AccentDark);
            ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new Num.Vector4(0.78f, 0.54f, 0.18f, 1f));
            ImGui.PushStyleColor(ImGuiCol.Text, new Num.Vector4(1f, 0.97f, 0.9f, 1f));
        }
        bool r = ImGui.Button(label, size);
        if (active) ImGui.PopStyleColor(3);
        if (tooltip is not null && ImGui.IsItemHovered(ImGuiHoveredFlags.DelayShort | ImGuiHoveredFlags.AllowWhenDisabled)) ImGui.SetTooltip(tooltip);
        return r;
    }

    /// <summary>A rounded "card" area (child window with a padded background).</summary>
    public static bool BeginCard(string id, float height = 0, Num.Vector4? bg = null)
    {
        ImGui.PushStyleColor(ImGuiCol.ChildBg, bg ?? new Num.Vector4(0.12f, 0.135f, 0.18f, 1f));
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Num.Vector2(10, 8));
        return ImGui.BeginChild(id, new Num.Vector2(0, height), ImGuiChildFlags.AutoResizeY | ImGuiChildFlags.AlwaysUseWindowPadding);
    }

    public static void EndCard()
    {
        ImGui.EndChild();
        ImGui.PopStyleVar();
        ImGui.PopStyleColor();
    }

    /// <summary>Coloured text shown exactly as written (ImGui's TextColored treats '%' as a format code).</summary>
    public static void Colored(Num.Vector4 color, string text)
    {
        ImGui.PushStyleColor(ImGuiCol.Text, color);
        ImGui.TextUnformatted(text);
        ImGui.PopStyleColor();
    }

    /// <summary>A small coloured label like "unsaved" or "sketch".</summary>
    public static void Badge(string text, Num.Vector4 color)
    {
        var dl = ImGui.GetWindowDrawList();
        var p = ImGui.GetCursorScreenPos();
        var size = ImGui.CalcTextSize(text) + new Num.Vector2(12, 4);
        dl.AddRectFilled(p, p + size, ImGui.GetColorU32(color with { W = 0.18f }), 8);
        dl.AddRect(p, p + size, ImGui.GetColorU32(color with { W = 0.6f }), 8);
        ImGui.SetCursorScreenPos(p + new Num.Vector2(6, 2));
        Colored(color, text);
        ImGui.SetCursorScreenPos(p + new Num.Vector2(size.X + 6, 0));
        ImGui.Dummy(new Num.Vector2(0, size.Y));
    }

    /// <summary>Pulls the current window back inside the screen if it was moved or opened partly off it.</summary>
    public static void KeepOnScreen()
    {
        var display = ImGui.GetIO().DisplaySize;
        var pos = ImGui.GetWindowPos();
        var size = ImGui.GetWindowSize();
        var clamped = new Num.Vector2(
            Math.Clamp(pos.X, 0, Math.Max(0, display.X - Math.Min(size.X, display.X))),
            Math.Clamp(pos.Y, 22, Math.Max(22, display.Y - Math.Min(size.Y, display.Y - 22))));
        if (clamped != pos) ImGui.SetWindowPos(clamped);
        if (size.X > display.X || size.Y > display.Y - 22)
            ImGui.SetWindowSize(new Num.Vector2(Math.Min(size.X, display.X), Math.Min(size.Y, display.Y - 22)));
    }

    /// <summary>Size for a new floating window that never exceeds the screen.</summary>
    public static Num.Vector2 FitSize(float w, float h)
    {
        var d = ImGui.GetIO().DisplaySize;
        return new Num.Vector2(Math.Min(w, d.X - 40), Math.Min(h, d.Y - 70));
    }
}
