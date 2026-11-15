"""
Generates the starter ground art in content/art/ground:

  base/    opaque white textures to tint into a ground colour (flat, or with a little grain / mottling
           that survives the tint as lighter and darker variation)
  shapes/  white shapes on transparent, to use as detail layers: tint them darker / lighter / another
           colour and they show only where the shapes are (ovals, blotches, pebbles, streaks...)

Every texture is seamless (wraps left-right and top-bottom), because ground layers repeat across
the screen. Run from the repository root:  python3 tools/generate_ground_art.py
"""
import math
import os
import random

import numpy as np
from PIL import Image, ImageFilter

ROOT = os.path.join("content", "art", "ground")


def save(name, rgb_or_alpha, folder, alpha=True):
    os.makedirs(os.path.join(ROOT, folder), exist_ok=True)
    a = np.clip(rgb_or_alpha, 0, 1)
    if alpha:
        img = np.zeros(a.shape + (4,), np.uint8)
        img[..., :3] = 255
        img[..., 3] = (a * 255 + 0.5).astype(np.uint8)
        Image.fromarray(img, "RGBA").save(os.path.join(ROOT, folder, name + ".png"))
    else:
        v = (a * 255 + 0.5).astype(np.uint8)
        Image.fromarray(np.stack([v, v, v], -1), "RGB").save(os.path.join(ROOT, folder, name + ".png"))
    print("  " + os.path.join(ROOT, folder, name + ".png"))


def blur_wrap(a, radius):
    """Gaussian blur that wraps around the edges (keeps it seamless): done in the frequency domain."""
    if radius <= 0:
        return a
    h, w = a.shape
    fy = np.fft.fftfreq(h)[:, None]
    fx = np.fft.fftfreq(w)[None, :]
    kernel = np.exp(-2 * (math.pi * radius) ** 2 * (fx ** 2 + fy ** 2))
    return np.real(np.fft.ifft2(np.fft.fft2(a) * kernel))


def noise(size, cells, octaves=3, seed=0):
    """Seamless smooth noise 0..1: random grids, upscaled with wrap-around, summed."""
    rng = np.random.default_rng(seed)
    total = np.zeros((size, size))
    amp, norm = 1.0, 0.0
    for o in range(octaves):
        n = cells * (2 ** o)
        grid = rng.random((n, n))
        big = np.tile(grid, (3, 3))
        img = Image.fromarray(big.astype(np.float32), "F").resize((size * 3, size * 3), Image.BICUBIC)
        layer = np.asarray(img, np.float64)[size:2 * size, size:2 * size]
        total += layer * amp
        norm += amp
        amp *= 0.5
    total /= norm
    total -= total.min()
    return total / max(1e-9, total.max())


def smoothstep(a, b, x):
    t = np.clip((x - a) / (b - a), 0, 1)
    return t * t * (3 - 2 * t)


def stamp(canvas, patch, x, y):
    """Max-combine a patch into the canvas at (x, y), wrapping around the edges."""
    s = canvas.shape[0]
    h, w = patch.shape
    rows = (np.arange(h) + y) % s
    cols = (np.arange(w) + x) % s
    canvas[np.ix_(rows, cols)] = np.maximum(canvas[np.ix_(rows, cols)], patch)


def rough_oval(rx, ry, angle, wobble, soft, rng):
    """An irregular oval: a radius that wobbles around the edge, rotated, with a soft (or crisp) edge."""
    r = int(max(rx, ry) * (1 + wobble) + soft * 2 + 4)
    yy, xx = np.mgrid[-r:r + 1, -r:r + 1].astype(np.float64)
    c, s = math.cos(angle), math.sin(angle)
    u, v = xx * c + yy * s, -xx * s + yy * c
    theta = np.arctan2(v / ry, u / rx)
    k = 1.0
    for n in range(2, 6):
        k += wobble * rng.uniform(-1, 1) / n * np.sin(n * theta + rng.uniform(0, 6.28))
    d = np.sqrt((u / rx) ** 2 + (v / ry) ** 2) / k          # 1 at the edge
    edge = max(0.02, soft / max(rx, ry))
    return 1 - smoothstep(1 - edge, 1 + edge * 0.5, d)


def scatter(size, count, make, seed, min_gap=0.0):
    rng = random.Random(seed)
    nrng = np.random.default_rng(seed)
    canvas = np.zeros((size, size))
    for _ in range(count):
        patch = make(rng, nrng)
        stamp(canvas, patch, rng.randrange(size) - patch.shape[1] // 2, rng.randrange(size) - patch.shape[0] // 2)
    return canvas


def bases():
    s = 512
    save("white_flat", np.ones((s, s)), "base", alpha=False)
    # A little fine grain: tints into a ground colour with subtle life in it.
    g = np.random.default_rng(1).random((s, s))
    save("white_grain", 0.93 + 0.07 * blur_wrap(g, 0.8) / max(1e-9, blur_wrap(g, 0.8).max()), "base", alpha=False)
    # Soft cloudy variation, lighter and darker areas.
    save("white_mottled", 0.84 + 0.16 * noise(s, 4, 4, seed=2), "base", alpha=False)
    # Mottled plus grain: the most natural-looking base.
    save("white_mottled_grain", 0.82 + 0.12 * noise(s, 4, 4, seed=3) + 0.06 * blur_wrap(np.random.default_rng(4).random((s, s)), 0.8), "base", alpha=False)


def shapes():
    s = 1024
    out = {}
    out["ovals_soft_large"] = scatter(s, 9, lambda r, n: rough_oval(r.uniform(90, 170), r.uniform(55, 110), r.uniform(0, 3.14), 0.18, 40, n), 10)
    out["ovals_soft_small"] = scatter(s, 40, lambda r, n: rough_oval(r.uniform(22, 50), r.uniform(14, 34), r.uniform(0, 3.14), 0.2, 12, n), 11)
    out["ovals_rough_crisp"] = scatter(s, 18, lambda r, n: rough_oval(r.uniform(40, 110), r.uniform(28, 75), r.uniform(0, 3.14), 0.35, 2, n), 12)
    out["flat_ovals_iso"] = scatter(s, 14, lambda r, n: rough_oval(r.uniform(70, 140), r.uniform(30, 60), 0.0, 0.2, 18, n), 13)   # wide and flat: reads as lying on iso ground
    out["pebbles"] = scatter(s, 260, lambda r, n: rough_oval(r.uniform(3, 9), r.uniform(2.5, 7), r.uniform(0, 3.14), 0.25, 1.2, n), 14)
    out["speckles"] = scatter(s, 900, lambda r, n: rough_oval(r.uniform(1, 2.6), r.uniform(1, 2.6), 0, 0.1, 0.8, n), 15)

    def tuft(r, n):
        p = np.zeros((40, 40))
        for _ in range(r.randint(4, 8)):
            stamp(p, rough_oval(r.uniform(1.2, 2), r.uniform(5, 11), r.uniform(-0.5, 0.5), 0.1, 1, n), r.randint(8, 22), r.randint(4, 16))
        return p
    out["tufts"] = scatter(s, 140, tuft, 16)
    out["streaks"] = scatter(s, 70, lambda r, n: rough_oval(r.uniform(40, 120), r.uniform(2, 5), r.uniform(-0.25, 0.25) + 0.46, 0.15, 3, n), 17)

    n1 = noise(s, 5, 4, seed=20)
    out["blotches_soft"] = smoothstep(0.55, 0.75, n1)
    out["blotches_crisp"] = smoothstep(0.6, 0.63, noise(s, 6, 4, seed=21))
    out["worn_patches"] = smoothstep(0.62, 0.8, noise(s, 3, 5, seed=22))
    out["mottle_cloudy"] = noise(s, 4, 5, seed=23)            # alpha varies everywhere: soft colour variation

    # Thin cracks: random walks.
    cr = np.zeros((s, s))
    rng = random.Random(30)
    for _ in range(26):
        x, y = rng.uniform(0, s), rng.uniform(0, s)
        a = rng.uniform(0, 6.28)
        for _ in range(rng.randint(30, 90)):
            a += rng.uniform(-0.5, 0.5)
            x, y = x + math.cos(a) * 3, y + math.sin(a) * 3
            stamp(cr, rough_oval(2.2, 2.2, 0, 0, 0.8, np.random.default_rng()), int(x) - 3, int(y) - 3)
    out["cracks"] = cr

    for name, a in out.items():
        save(name, a, "shapes")


if __name__ == "__main__":
    print("Ground art:")
    bases()
    shapes()
