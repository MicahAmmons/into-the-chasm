using System.Numerics;

namespace Chasm.Core.World;

/// <summary>
/// Perspective ("corner-pin") maths: maps the unit square onto any four-cornered shape the way a
/// photo tilted away from you looks, so the texture shrinks smoothly toward the narrow end
/// (foreshortening) instead of just being squashed.
/// </summary>
public readonly struct Homography
{
    private readonly float _a, _b, _c, _d, _e, _f, _g, _h;

    private Homography(float a, float b, float c, float d, float e, float f, float g, float h)
    { _a = a; _b = b; _c = c; _d = d; _e = e; _f = f; _g = g; _h = h; }

    /// <summary>Unit square (0,0)-(1,0)-(1,1)-(0,1) onto corners top-left, top-right, bottom-right, bottom-left.</summary>
    public static Homography SquareToQuad(Vector2 p0, Vector2 p1, Vector2 p2, Vector2 p3)
    {
        // Heckbert's closed form.
        float sx = p0.X - p1.X + p2.X - p3.X, sy = p0.Y - p1.Y + p2.Y - p3.Y;
        if (MathF.Abs(sx) < 1e-6f && MathF.Abs(sy) < 1e-6f)
            return new Homography(p1.X - p0.X, p2.X - p1.X, p0.X, p1.Y - p0.Y, p2.Y - p1.Y, p0.Y, 0, 0); // parallelogram
        float dx1 = p1.X - p2.X, dx2 = p3.X - p2.X, dy1 = p1.Y - p2.Y, dy2 = p3.Y - p2.Y;
        float den = dx1 * dy2 - dx2 * dy1;
        if (MathF.Abs(den) < 1e-9f) den = 1e-9f;
        float g = (sx * dy2 - dx2 * sy) / den, h = (dx1 * sy - sx * dy1) / den;
        return new Homography(p1.X - p0.X + g * p1.X, p3.X - p0.X + h * p3.X, p0.X,
                              p1.Y - p0.Y + g * p1.Y, p3.Y - p0.Y + h * p3.Y, p0.Y, g, h);
    }

    /// <summary>Where the point (u, v) of the square lands (u across, v down, both 0..1).</summary>
    public Vector2 Map(float u, float v)
    {
        float w = _g * u + _h * v + 1;
        if (MathF.Abs(w) < 1e-6f) w = 1e-6f;
        return new Vector2((_a * u + _b * v + _c) / w, (_d * u + _e * v + _f) / w);
    }
}

/// <summary>Where a material's texture sheet sits on screen (centre, size, rotation and warp).</summary>
public static class MaterialSheet
{
    /// <summary>The middle of the sheet on screen.</summary>
    public static Vector2 Centre(MaterialDef m) => new(960 + m.OffsetX, 540 + m.OffsetY);

    /// <summary>
    /// The four screen corners (top-left, top-right, bottom-right, bottom-left) of one copy of the
    /// texture: scaled, warped (corner offsets are in the sheet's own unrotated frame), then rotated
    /// around the sheet's centre and moved by its offset.
    /// </summary>
    public static Vector2[] Corners(MaterialDef m, int texW, int texH)
    {
        float hw = texW * m.Scale / 2f, hh = texH * m.Scale / 2f;
        var local = new[] { new Vector2(-hw, -hh), new Vector2(hw, -hh), new Vector2(hw, hh), new Vector2(-hw, hh) };
        if (m.Warp is { } w)
            for (int i = 0; i < 4; i++) local[i] += w.Get(i);
        return local.Select(p => ToScreen(m, p)).ToArray();
    }

    public static Vector2 ToScreen(MaterialDef m, Vector2 local)
    {
        float r = m.Rotation * MathF.PI / 180f, c = MathF.Cos(r), s = MathF.Sin(r);
        return Centre(m) + new Vector2(local.X * c - local.Y * s, local.X * s + local.Y * c);
    }

    /// <summary>A movement on screen expressed in the sheet's own (unrotated) frame.</summary>
    public static Vector2 ToLocalDelta(MaterialDef m, Vector2 screenDelta)
    {
        float r = -m.Rotation * MathF.PI / 180f, c = MathF.Cos(r), s = MathF.Sin(r);
        return new Vector2(screenDelta.X * c - screenDelta.Y * s, screenDelta.X * s + screenDelta.Y * c);
    }
}
