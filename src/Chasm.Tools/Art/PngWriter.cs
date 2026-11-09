using System.IO.Compression;

namespace Chasm.Tools.Art;

/// <summary>Minimal PNG encoder (RGB or RGBA, 8-bit) so art can be generated without any graphics device.</summary>
public static class PngWriter
{
    /// <param name="pixels">Row-major, 3 or 4 bytes per pixel.</param>
    public static void Write(string path, int width, int height, byte[] pixels, bool alpha)
    {
        int bpp = alpha ? 4 : 3;
        using var raw = new MemoryStream();
        using (var z = new ZLibStream(raw, CompressionLevel.SmallestSize, leaveOpen: true))
        {
            var row = new byte[width * bpp + 1];
            var prev = new byte[width * bpp];
            for (int y = 0; y < height; y++)
            {
                // "Up" filter (difference from the row above) compresses smooth noise much better.
                row[0] = 2;
                int o = y * width * bpp;
                for (int i = 0; i < width * bpp; i++) row[i + 1] = (byte)(pixels[o + i] - prev[i]);
                Buffer.BlockCopy(pixels, o, prev, 0, width * bpp);
                z.Write(row, 0, row.Length);
            }
        }
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        using var fs = File.Create(path);
        fs.Write(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 });
        var ihdr = new byte[13];
        WriteBE(ihdr, 0, width); WriteBE(ihdr, 4, height);
        ihdr[8] = 8; ihdr[9] = (byte)(alpha ? 6 : 2); // bit depth 8, colour type RGBA / RGB
        Chunk(fs, "IHDR", ihdr);
        Chunk(fs, "IDAT", raw.ToArray());
        Chunk(fs, "IEND", Array.Empty<byte>());
    }

    private static void Chunk(Stream s, string type, byte[] data)
    {
        var len = new byte[4]; WriteBE(len, 0, data.Length); s.Write(len);
        var t = System.Text.Encoding.ASCII.GetBytes(type);
        s.Write(t); s.Write(data);
        uint crc = Crc(t, 0xFFFFFFFFu); crc = Crc(data, crc) ^ 0xFFFFFFFFu;
        var c = new byte[4]; WriteBE(c, 0, (int)crc); s.Write(c);
    }

    private static void WriteBE(byte[] b, int o, int v) { b[o] = (byte)(v >> 24); b[o + 1] = (byte)(v >> 16); b[o + 2] = (byte)(v >> 8); b[o + 3] = (byte)v; }

    private static readonly uint[] Table = Enumerable.Range(0, 256).Select(n =>
    {
        uint c = (uint)n;
        for (int k = 0; k < 8; k++) c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1;
        return c;
    }).ToArray();

    private static uint Crc(byte[] data, uint crc)
    {
        foreach (var b in data) crc = Table[(crc ^ b) & 0xFF] ^ (crc >> 8);
        return crc;
    }
}
