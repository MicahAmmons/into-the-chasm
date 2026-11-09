using FontStashSharp;

namespace Chasm.Game.Assets;

/// <summary>
/// Runtime TTF fonts (FontStashSharp), so text needs no content pipeline step and any size works.
/// Fonts ship in content/fonts (DejaVu, free licence) until custom fonts are chosen.
/// </summary>
public sealed class FontStore
{
    private readonly FontSystem _regular = new();
    private readonly FontSystem _bold = new();
    private readonly FontSystem _mono = new();

    public FontStore(ContentPaths paths)
    {
        _regular.AddFont(File.ReadAllBytes(Path.Combine(paths.Fonts, "DejaVuSans.ttf")));
        _bold.AddFont(File.ReadAllBytes(Path.Combine(paths.Fonts, "DejaVuSans-Bold.ttf")));
        _mono.AddFont(File.ReadAllBytes(Path.Combine(paths.Fonts, "DejaVuSansMono.ttf")));
    }

    public SpriteFontBase Regular(float size = 22) => _regular.GetFont(size);
    public SpriteFontBase Bold(float size = 22) => _bold.GetFont(size);
    public SpriteFontBase Mono(float size = 16) => _mono.GetFont(size);
}
