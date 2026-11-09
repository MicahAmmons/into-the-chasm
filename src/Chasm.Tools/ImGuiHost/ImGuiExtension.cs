using Chasm.Game.Hosting;
using ImGuiNET;
using Num = System.Numerics;

namespace Chasm.Tools.ImGuiHost;

/// <summary>Hooks ImGui into the game host: starts the frame, reports mouse/keyboard capture, renders.</summary>
public sealed class ImGuiExtension : IHostExtension
{
    private ChasmGame? _game;
    public ImGuiRenderer Renderer { get; private set; } = null!;
    public static ImGuiExtension? Instance { get; private set; }

    public void Initialize(GameServices services)
    {
        Instance = this;
        _game = services.Game;
        Renderer = new ImGuiRenderer(services.Game);
        // First font added is the default; the bold ones are for section headers and titles.
        Renderer.AddFont(Path.Combine(services.Paths.Fonts, "DejaVuSans.ttf"), 16.5f);
        Ui.HeaderFont = Renderer.AddFont(Path.Combine(services.Paths.Fonts, "DejaVuSans-Bold.ttf"), 15f);
        Ui.TitleFont = Renderer.AddFont(Path.Combine(services.Paths.Fonts, "DejaVuSans-Bold.ttf"), 21f);
        Renderer.RebuildFontAtlas();
        ToolStyle.Apply();
    }

    private bool _frameOpen;

    public void BeginFrame(float dt)
    {
        // MonoGame may run several Updates before one Draw (catching up). Close the unrendered
        // ImGui frame so ImGui never sees two NewFrame calls in a row.
        if (_frameOpen) ImGui.EndFrame();
        Renderer.BeforeLayout(dt);
        _frameOpen = true;
        var io = ImGui.GetIO();
        var input = _game!.Svc.Input;
        input.MouseCapturedByUi = io.WantCaptureMouse;
        input.KeyboardCapturedByUi = io.WantCaptureKeyboard || io.WantTextInput;
    }

    public void EndFrame()
    {
        if (!_frameOpen) return;
        Renderer.AfterLayout();
        _frameOpen = false;
    }

    public void Dispose() => Renderer.Dispose();
}

/// <summary>The tools' look: dark slate panels, soft rounding, a warm amber accent for "active".</summary>
public static class ToolStyle
{
    public static void Apply()
    {
        ImGui.StyleColorsDark();
        var style = ImGui.GetStyle();
        style.WindowRounding = 8;
        style.ChildRounding = 6;
        style.FrameRounding = 5;
        style.PopupRounding = 6;
        style.GrabRounding = 5;
        style.TabRounding = 5;
        style.ScrollbarRounding = 6;
        style.WindowBorderSize = 1;
        style.FrameBorderSize = 0;
        style.WindowPadding = new Num.Vector2(12, 12);
        style.FramePadding = new Num.Vector2(8, 5);
        style.ItemSpacing = new Num.Vector2(8, 7);
        style.ItemInnerSpacing = new Num.Vector2(6, 5);
        style.IndentSpacing = 16;
        style.ScrollbarSize = 12;
        style.SeparatorTextBorderSize = 1;
        style.SeparatorTextPadding = new Num.Vector2(0, 4);
        style.WindowTitleAlign = new Num.Vector2(0.02f, 0.5f);
        // Keep windows from being dragged off screen.
        style.DisplayWindowPadding = new Num.Vector2(60, 60);
        style.DisplaySafeAreaPadding = new Num.Vector2(6, 6);

        var c = style.Colors;
        void Set(ImGuiCol col, float r, float g, float b, float a = 1f) => c[(int)col] = new Num.Vector4(r, g, b, a);
        Set(ImGuiCol.Text, 0.90f, 0.92f, 0.96f);
        Set(ImGuiCol.TextDisabled, 0.52f, 0.56f, 0.64f);
        Set(ImGuiCol.WindowBg, 0.085f, 0.095f, 0.125f, 0.98f);
        Set(ImGuiCol.ChildBg, 0.11f, 0.12f, 0.16f, 1f);
        Set(ImGuiCol.PopupBg, 0.10f, 0.11f, 0.15f, 0.98f);
        Set(ImGuiCol.Border, 0.20f, 0.22f, 0.29f);
        Set(ImGuiCol.FrameBg, 0.15f, 0.165f, 0.215f);
        Set(ImGuiCol.FrameBgHovered, 0.20f, 0.22f, 0.29f);
        Set(ImGuiCol.FrameBgActive, 0.24f, 0.27f, 0.36f);
        Set(ImGuiCol.TitleBg, 0.10f, 0.11f, 0.15f);
        Set(ImGuiCol.TitleBgActive, 0.14f, 0.16f, 0.23f);
        Set(ImGuiCol.MenuBarBg, 0.07f, 0.08f, 0.105f);
        Set(ImGuiCol.Button, 0.18f, 0.20f, 0.27f);
        Set(ImGuiCol.ButtonHovered, 0.25f, 0.29f, 0.40f);
        Set(ImGuiCol.ButtonActive, 0.30f, 0.35f, 0.50f);
        Set(ImGuiCol.Header, 0.20f, 0.23f, 0.32f);
        Set(ImGuiCol.HeaderHovered, 0.26f, 0.30f, 0.42f);
        Set(ImGuiCol.HeaderActive, 0.30f, 0.35f, 0.50f);
        Set(ImGuiCol.Separator, 0.22f, 0.25f, 0.33f);
        Set(ImGuiCol.CheckMark, 0.98f, 0.72f, 0.30f);
        Set(ImGuiCol.SliderGrab, 0.98f, 0.72f, 0.30f);
        Set(ImGuiCol.SliderGrabActive, 1f, 0.80f, 0.42f);
        Set(ImGuiCol.Tab, 0.15f, 0.17f, 0.23f);
        Set(ImGuiCol.TabHovered, 0.26f, 0.30f, 0.42f);
        Set(ImGuiCol.TabSelected, 0.22f, 0.26f, 0.37f);
        Set(ImGuiCol.TextSelectedBg, 0.98f, 0.72f, 0.30f, 0.35f);
        Set(ImGuiCol.ModalWindowDimBg, 0.02f, 0.02f, 0.04f, 0.55f);
    }
}
