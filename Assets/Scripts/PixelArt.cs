using UnityEngine;

// Small helpers for the background life (SkyLife, MidLayerLife): sprites
// built at runtime from rows of characters, and look weights.
public static class PixelArt
{
    // One sprite from rows of characters, top row first. Each character maps
    // to a colour; '.' and ' ' are clear. Point filtered, never saved.
    public static Sprite Make(string[] rows, System.Func<char, Color32> colour, Vector2 pivot, float pixelsPerUnit = 32f)
    {
        int h = rows.Length, w = 0;
        foreach (var r in rows) w = Mathf.Max(w, r.Length);
        var tex = new Texture2D(w, h, TextureFormat.RGBA32, false)
        {
            filterMode = FilterMode.Point,
            wrapMode = TextureWrapMode.Clamp,
            hideFlags = HideFlags.HideAndDontSave,
        };
        var px = new Color32[w * h];
        for (int y = 0; y < h; y++)
            for (int x = 0; x < rows[y].Length; x++)
            {
                char c = rows[y][x];
                if (c == '.' || c == ' ') continue;
                px[(h - 1 - y) * w + x] = colour(c);
            }
        tex.SetPixels32(px);
        tex.Apply(false);
        var sprite = Sprite.Create(tex, new Rect(0, 0, w, h), pivot, pixelsPerUnit, 0, SpriteMeshType.FullRect);
        sprite.hideFlags = HideFlags.HideAndDontSave;
        return sprite;
    }

    public static Sprite Make(string[] rows, Color32 colour, Vector2 pivot) => Make(rows, c => colour, pivot);

    public static SpriteRenderer Renderer(Transform parent, string name, int sortingOrder)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        var sr = go.AddComponent<SpriteRenderer>();
        sr.sortingOrder = sortingOrder;
        return sr;
    }

    // How much of a look is on screen for LaneSky's blend (from, to, t).
    public static float Weight(LaneSky.Look from, LaneSky.Look to, float t, LaneSky.Look look) =>
        (from == look ? 1f - t : 0f) + (to == look ? t : 0f);

    // Day and night amounts, with dusk counting half to each.
    public static (float day, float night) DayNight(LaneSky.Look from, LaneSky.Look to, float t)
    {
        float dusk = Weight(from, to, t, LaneSky.Look.Dusk);
        return (Weight(from, to, t, LaneSky.Look.Day) + dusk * 0.5f, Weight(from, to, t, LaneSky.Look.Night) + dusk * 0.5f);
    }

    public static Color Alpha(Color c, float a) => new Color(c.r, c.g, c.b, a);

    // Lane seconds passed since the last call (0 while the lane is frozen or
    // outside play mode).
    public static float LaneDelta(int lane, ref float last)
    {
        if (!Application.isPlaying || lane < 0 || GameManager.INSTANCE == null) return 0f;
        float now = GameManager.INSTANCE.GetLaneTime(lane);
        float dt = last < 0f ? 0f : Mathf.Clamp(now - last, 0f, 1f);
        last = now;
        return dt;
    }
}
