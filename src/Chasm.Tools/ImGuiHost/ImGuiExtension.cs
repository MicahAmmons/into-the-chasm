using Chasm.Game.Hosting;
using ImGuiNET;

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
        Renderer.LoadFont(Path.Combine(services.Paths.Fonts, "DejaVuSans.ttf"), 17);
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

public static class ToolStyle
{
    public static void Apply()
    {
        ImGui.StyleColorsDark();
        var style = ImGui.GetStyle();
        style.WindowRounding = 6;
        style.FrameRounding = 4;
        style.GrabRounding = 4;
        style.WindowPadding = new System.Numerics.Vector2(10, 10);
        style.ItemSpacing = new System.Numerics.Vector2(8, 6);
        var c = style.Colors;
        c[(int)ImGuiCol.WindowBg] = new System.Numerics.Vector4(0.08f, 0.09f, 0.13f, 0.94f);
        c[(int)ImGuiCol.Header] = new System.Numerics.Vector4(0.26f, 0.30f, 0.45f, 0.8f);
        c[(int)ImGuiCol.Button] = new System.Numerics.Vector4(0.22f, 0.26f, 0.40f, 1f);
        c[(int)ImGuiCol.ButtonHovered] = new System.Numerics.Vector4(0.32f, 0.38f, 0.58f, 1f);
        c[(int)ImGuiCol.TitleBgActive] = new System.Numerics.Vector4(0.18f, 0.22f, 0.36f, 1f);
    }
}
