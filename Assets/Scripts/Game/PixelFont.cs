using System.Collections.Generic;
using UnityEngine;

// A 5x7 pixel font for the ending's titles: text is rendered once into a small
// point-filtered texture with a one-pixel outline, then shown scaled up whole
// (RawImage), so it stays crisp pixel art at any size.
public static class PixelFont
{
    public const int GlyphWidth = 5, GlyphHeight = 7;

    // Top row first; '#' is ink.
    static readonly Dictionary<char, string[]> Glyphs = new Dictionary<char, string[]>
    {
        ['A'] = new[] { ".###.", "#...#", "#...#", "#####", "#...#", "#...#", "#...#" },
        ['C'] = new[] { ".###.", "#...#", "#....", "#....", "#....", "#...#", ".###." },
        ['D'] = new[] { "####.", "#...#", "#...#", "#...#", "#...#", "#...#", "####." },
        ['E'] = new[] { "#####", "#....", "#....", "####.", "#....", "#....", "#####" },
        ['H'] = new[] { "#...#", "#...#", "#...#", "#####", "#...#", "#...#", "#...#" },
        ['I'] = new[] { "#####", "..#..", "..#..", "..#..", "..#..", "..#..", "#####" },
        ['K'] = new[] { "#...#", "#..#.", "#.#..", "##...", "#.#..", "#..#.", "#...#" },
        ['N'] = new[] { "#...#", "##..#", "#.#.#", "#.#.#", "#..##", "#...#", "#...#" },
        ['O'] = new[] { ".###.", "#...#", "#...#", "#...#", "#...#", "#...#", ".###." },
        ['P'] = new[] { "####.", "#...#", "#...#", "####.", "#....", "#....", "#...." },
        ['R'] = new[] { "####.", "#...#", "#...#", "####.", "#.#..", "#..#.", "#...#" },
        ['S'] = new[] { ".####", "#....", "#....", ".###.", "....#", "....#", "####." },
        ['T'] = new[] { "#####", "..#..", "..#..", "..#..", "..#..", "..#..", "..#.." },
        ['Y'] = new[] { "#...#", "#...#", ".#.#.", "..#..", "..#..", "..#..", "..#.." },
        ['&'] = new[] { ".##..", "#..#.", "#.#..", ".#...", "#.#.#", "#..#.", ".##.#" },
        [' '] = new[] { ".....", ".....", ".....", ".....", ".....", ".....", "....." },
    };

    // Texture of the text: glyphs one pixel apart, a one-pixel outline round
    // the ink (8-neighbour), transparent elsewhere.
    public static Texture2D Render(string text, Color32 fill, Color32 outline)
    {
        text = text.ToUpperInvariant();
        int w = text.Length * (GlyphWidth + 1) - 1 + 2, h = GlyphHeight + 2;
        var ink = new bool[w, h];   // y from the top
        for (int i = 0; i < text.Length; i++)
        {
            if (!Glyphs.TryGetValue(text[i], out var rows)) continue;
            for (int y = 0; y < GlyphHeight; y++)
                for (int x = 0; x < GlyphWidth; x++)
                    if (rows[y][x] == '#') ink[1 + i * (GlyphWidth + 1) + x, 1 + y] = true;
        }
        var px = new Color32[w * h];
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                Color32 c = default;
                if (ink[x, y]) c = fill;
                else
                {
                    for (int dy = -1; dy <= 1 && c.a == 0; dy++)
                        for (int dx = -1; dx <= 1; dx++)
                        {
                            int nx = x + dx, ny = y + dy;
                            if (nx >= 0 && ny >= 0 && nx < w && ny < h && ink[nx, ny]) { c = outline; break; }
                        }
                }
                px[(h - 1 - y) * w + x] = c;
            }
        var tex = new Texture2D(w, h, TextureFormat.RGBA32, false)
        {
            filterMode = FilterMode.Point,
            wrapMode = TextureWrapMode.Clamp,
            hideFlags = HideFlags.HideAndDontSave,
            name = "PixelText " + text,
        };
        tex.SetPixels32(px);
        tex.Apply(false);
        return tex;
    }
}
