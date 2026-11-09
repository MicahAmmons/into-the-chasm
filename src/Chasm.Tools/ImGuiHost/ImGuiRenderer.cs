using System.Runtime.InteropServices;
using ImGuiNET;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using Num = System.Numerics;

namespace Chasm.Tools.ImGuiHost;

/// <summary>
/// Renders Dear ImGui with MonoGame (adapted from the ImGui.NET sample renderer). Feeds mouse,
/// keyboard and text input to ImGui and draws its vertex lists with a BasicEffect.
/// </summary>
public sealed class ImGuiRenderer : IDisposable
{
    private readonly Microsoft.Xna.Framework.Game _game;
    private readonly GraphicsDevice _device;
    private readonly BasicEffect _effect;
    private readonly RasterizerState _raster;
    private byte[] _vertexData = Array.Empty<byte>();
    private VertexBuffer? _vertexBuffer;
    private int _vertexBufferSize;
    private byte[] _indexData = Array.Empty<byte>();
    private IndexBuffer? _indexBuffer;
    private int _indexBufferSize;
    private readonly Dictionary<IntPtr, Texture2D> _textures = new();
    private int _nextTextureId = 1;
    private IntPtr? _fontTextureId;
    private int _scrollWheel;
    private readonly IntPtr _iniPath; // must stay alive for ImGui's lifetime
    private readonly List<int> _keys = new();

    public ImGuiRenderer(Microsoft.Xna.Framework.Game game)
    {
        _game = game;
        _device = game.GraphicsDevice;
        ImGui.SetCurrentContext(ImGui.CreateContext());
        // ImGui remembers window sizes/positions in an .ini file. Keep it in AppData (per computer),
        // never in the repo folder, so it can't block git pulls.
        var iniDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "IntoTheChasm");
        Directory.CreateDirectory(iniDir);
        _iniPath = Marshal.StringToHGlobalAnsi(Path.Combine(iniDir, "imgui.ini"));
        unsafe { ImGui.GetIO().NativePtr->IniFilename = (byte*)_iniPath; }
        _effect = new BasicEffect(_device) { TextureEnabled = true, VertexColorEnabled = true };
        _raster = new RasterizerState { CullMode = CullMode.None, DepthBias = 0, FillMode = FillMode.Solid, MultiSampleAntiAlias = false, ScissorTestEnable = true };
        game.Window.TextInput += (_, a) => { if (a.Character != '\t') ImGui.GetIO().AddInputCharacter(a.Character); };
        var io = ImGui.GetIO();
        io.ConfigFlags |= ImGuiConfigFlags.NavEnableKeyboard;
        io.ConfigFlags |= ImGuiConfigFlags.DockingEnable;
    }

    /// <summary>Adds a font without rebuilding (call <see cref="RebuildFontAtlas"/> after adding all fonts).</summary>
    public ImFontPtr AddFont(string ttfPath, float size) => ImGui.GetIO().Fonts.AddFontFromFileTTF(ttfPath, size);

    public void LoadFont(string ttfPath, float size)
    {
        var io = ImGui.GetIO();
        io.Fonts.AddFontFromFileTTF(ttfPath, size);
        RebuildFontAtlas();
    }

    public unsafe void RebuildFontAtlas()
    {
        var io = ImGui.GetIO();
        io.Fonts.GetTexDataAsRGBA32(out byte* pixelData, out int width, out int height, out int bpp);
        var pixels = new byte[width * height * bpp];
        Marshal.Copy(new IntPtr(pixelData), pixels, 0, pixels.Length);
        var tex = new Texture2D(_device, width, height, false, SurfaceFormat.Color);
        tex.SetData(pixels);
        if (_fontTextureId.HasValue) UnbindTexture(_fontTextureId.Value);
        _fontTextureId = BindTexture(tex);
        io.Fonts.SetTexID(_fontTextureId.Value);
        io.Fonts.ClearTexData();
    }

    public IntPtr BindTexture(Texture2D texture)
    {
        foreach (var kv in _textures) if (kv.Value == texture) return kv.Key;
        var id = new IntPtr(_nextTextureId++);
        _textures[id] = texture;
        return id;
    }

    public void UnbindTexture(IntPtr id) => _textures.Remove(id);

    public void BeforeLayout(float dt)
    {
        var io = ImGui.GetIO();
        io.DeltaTime = Math.Max(dt, 1f / 1000f);
        UpdateInput();
        ImGui.NewFrame();
    }

    public void AfterLayout()
    {
        ImGui.Render();
        RenderDrawData(ImGui.GetDrawData());
    }

    private void UpdateInput()
    {
        var io = ImGui.GetIO();
        bool focused = _game is Chasm.Game.Hosting.ChasmGame cg ? cg.HasFocus : _game.IsActive;
        if (!focused) return;
        var mouse = Mouse.GetState();
        var kb = Keyboard.GetState();
        io.AddMousePosEvent(mouse.X, mouse.Y);
        io.AddMouseButtonEvent(0, mouse.LeftButton == ButtonState.Pressed);
        io.AddMouseButtonEvent(1, mouse.RightButton == ButtonState.Pressed);
        io.AddMouseButtonEvent(2, mouse.MiddleButton == ButtonState.Pressed);
        int scroll = mouse.ScrollWheelValue - _scrollWheel;
        io.AddMouseWheelEvent(0, scroll > 0 ? 1 : scroll < 0 ? -1 : 0);
        _scrollWheel = mouse.ScrollWheelValue;

        foreach (Keys key in Enum.GetValues<Keys>())
            if (TryMapKey(key, out var imKey)) io.AddKeyEvent(imKey, kb.IsKeyDown(key));
        io.AddKeyEvent(ImGuiKey.ModShift, kb.IsKeyDown(Keys.LeftShift) || kb.IsKeyDown(Keys.RightShift));
        io.AddKeyEvent(ImGuiKey.ModCtrl, kb.IsKeyDown(Keys.LeftControl) || kb.IsKeyDown(Keys.RightControl));
        io.AddKeyEvent(ImGuiKey.ModAlt, kb.IsKeyDown(Keys.LeftAlt) || kb.IsKeyDown(Keys.RightAlt));

        var pp = _device.PresentationParameters;
        io.DisplaySize = new Num.Vector2(pp.BackBufferWidth, pp.BackBufferHeight);
        io.DisplayFramebufferScale = Num.Vector2.One;
    }

    private static bool TryMapKey(Keys key, out ImGuiKey imKey)
    {
        imKey = key switch
        {
            Keys.Back => ImGuiKey.Backspace, Keys.Tab => ImGuiKey.Tab, Keys.Enter => ImGuiKey.Enter,
            Keys.Escape => ImGuiKey.Escape, Keys.Space => ImGuiKey.Space, Keys.PageUp => ImGuiKey.PageUp,
            Keys.PageDown => ImGuiKey.PageDown, Keys.End => ImGuiKey.End, Keys.Home => ImGuiKey.Home,
            Keys.Left => ImGuiKey.LeftArrow, Keys.Right => ImGuiKey.RightArrow, Keys.Up => ImGuiKey.UpArrow,
            Keys.Down => ImGuiKey.DownArrow, Keys.Insert => ImGuiKey.Insert, Keys.Delete => ImGuiKey.Delete,
            >= Keys.D0 and <= Keys.D9 => ImGuiKey._0 + (key - Keys.D0),
            >= Keys.A and <= Keys.Z => ImGuiKey.A + (key - Keys.A),
            >= Keys.NumPad0 and <= Keys.NumPad9 => ImGuiKey.Keypad0 + (key - Keys.NumPad0),
            >= Keys.F1 and <= Keys.F12 => ImGuiKey.F1 + (key - Keys.F1),
            Keys.LeftControl => ImGuiKey.LeftCtrl, Keys.RightControl => ImGuiKey.RightCtrl,
            Keys.LeftShift => ImGuiKey.LeftShift, Keys.RightShift => ImGuiKey.RightShift,
            Keys.LeftAlt => ImGuiKey.LeftAlt, Keys.RightAlt => ImGuiKey.RightAlt,
            Keys.OemMinus => ImGuiKey.Minus, Keys.OemPlus => ImGuiKey.Equal, Keys.OemPeriod => ImGuiKey.Period,
            Keys.OemComma => ImGuiKey.Comma, Keys.OemQuestion => ImGuiKey.Slash,
            _ => ImGuiKey.None,
        };
        return imKey != ImGuiKey.None;
    }

    private void RenderDrawData(ImDrawDataPtr drawData)
    {
        var lastViewport = _device.Viewport;
        var lastScissor = _device.ScissorRectangle;
        var lastRaster = _device.RasterizerState;
        var lastDepth = _device.DepthStencilState;
        var lastBlend = _device.BlendState;
        var lastSampler = _device.SamplerStates[0];

        _device.BlendFactor = Color.White;
        _device.BlendState = BlendState.NonPremultiplied;
        _device.RasterizerState = _raster;
        _device.DepthStencilState = DepthStencilState.DepthRead;
        _device.SamplerStates[0] = SamplerState.LinearClamp;

        drawData.ScaleClipRects(ImGui.GetIO().DisplayFramebufferScale);
        _device.Viewport = new Viewport(0, 0, _device.PresentationParameters.BackBufferWidth, _device.PresentationParameters.BackBufferHeight);
        UpdateBuffers(drawData);
        RenderCommandLists(drawData);

        _device.Viewport = lastViewport;
        _device.ScissorRectangle = lastScissor;
        _device.RasterizerState = lastRaster;
        _device.DepthStencilState = lastDepth;
        _device.BlendState = lastBlend;
        _device.SamplerStates[0] = lastSampler;
    }

    private const int VertexSize = 20; // pos(8) + uv(8) + col(4)
    private static readonly VertexDeclaration Declaration = new(VertexSize,
        new VertexElement(0, VertexElementFormat.Vector2, VertexElementUsage.Position, 0),
        new VertexElement(8, VertexElementFormat.Vector2, VertexElementUsage.TextureCoordinate, 0),
        new VertexElement(16, VertexElementFormat.Color, VertexElementUsage.Color, 0));

    private unsafe void UpdateBuffers(ImDrawDataPtr drawData)
    {
        if (drawData.TotalVtxCount == 0) return;
        if (drawData.TotalVtxCount > _vertexBufferSize)
        {
            _vertexBuffer?.Dispose();
            _vertexBufferSize = (int)(drawData.TotalVtxCount * 1.5f);
            _vertexBuffer = new VertexBuffer(_device, Declaration, _vertexBufferSize, BufferUsage.None);
            _vertexData = new byte[_vertexBufferSize * VertexSize];
        }
        if (drawData.TotalIdxCount > _indexBufferSize)
        {
            _indexBuffer?.Dispose();
            _indexBufferSize = (int)(drawData.TotalIdxCount * 1.5f);
            _indexBuffer = new IndexBuffer(_device, IndexElementSize.SixteenBits, _indexBufferSize, BufferUsage.None);
            _indexData = new byte[_indexBufferSize * sizeof(ushort)];
        }
        int vtxOffset = 0, idxOffset = 0;
        for (int n = 0; n < drawData.CmdListsCount; n++)
        {
            var cmdList = drawData.CmdLists[n];
            fixed (void* vtxDst = &_vertexData[vtxOffset * VertexSize])
            fixed (void* idxDst = &_indexData[idxOffset * sizeof(ushort)])
            {
                Buffer.MemoryCopy((void*)cmdList.VtxBuffer.Data, vtxDst, _vertexData.Length - vtxOffset * VertexSize, cmdList.VtxBuffer.Size * VertexSize);
                Buffer.MemoryCopy((void*)cmdList.IdxBuffer.Data, idxDst, _indexData.Length - idxOffset * sizeof(ushort), cmdList.IdxBuffer.Size * sizeof(ushort));
            }
            vtxOffset += cmdList.VtxBuffer.Size;
            idxOffset += cmdList.IdxBuffer.Size;
        }
        _vertexBuffer!.SetData(_vertexData, 0, drawData.TotalVtxCount * VertexSize);
        _indexBuffer!.SetData(_indexData, 0, drawData.TotalIdxCount * sizeof(ushort));
    }

    private void RenderCommandLists(ImDrawDataPtr drawData)
    {
        if (drawData.TotalVtxCount == 0) return;
        _device.SetVertexBuffer(_vertexBuffer);
        _device.Indices = _indexBuffer;
        var io = ImGui.GetIO();
        _effect.World = Matrix.Identity;
        _effect.View = Matrix.Identity;
        _effect.Projection = Matrix.CreateOrthographicOffCenter(0, io.DisplaySize.X, io.DisplaySize.Y, 0, -1, 1);
        int vtxOffset = 0, idxOffset = 0;
        for (int n = 0; n < drawData.CmdListsCount; n++)
        {
            var cmdList = drawData.CmdLists[n];
            for (int i = 0; i < cmdList.CmdBuffer.Size; i++)
            {
                var cmd = cmdList.CmdBuffer[i];
                if (cmd.ElemCount == 0) continue;
                if (!_textures.TryGetValue(cmd.TextureId, out var tex)) continue;
                _device.ScissorRectangle = new Rectangle((int)cmd.ClipRect.X, (int)cmd.ClipRect.Y,
                    (int)(cmd.ClipRect.Z - cmd.ClipRect.X), (int)(cmd.ClipRect.W - cmd.ClipRect.Y));
                _effect.Texture = tex;
                foreach (var pass in _effect.CurrentTechnique.Passes)
                {
                    pass.Apply();
#pragma warning disable CS0618
                    _device.DrawIndexedPrimitives(PrimitiveType.TriangleList, (int)cmd.VtxOffset + vtxOffset,
                        (int)cmd.IdxOffset + idxOffset, (int)cmd.ElemCount / 3);
#pragma warning restore CS0618
                }
            }
            vtxOffset += cmdList.VtxBuffer.Size;
            idxOffset += cmdList.IdxBuffer.Size;
        }
    }

    public void Dispose()
    {
        _vertexBuffer?.Dispose();
        _indexBuffer?.Dispose();
        _effect.Dispose();
    }
}
