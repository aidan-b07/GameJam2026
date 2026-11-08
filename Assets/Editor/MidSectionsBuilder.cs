using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

// Builds a lane's mid background from the painted sections exported by
// tools/make_bg_parts.py (Assets/Art/Backgrounds/Sections: a still per look,
// one frame sheet per moving part, bg_sections.json). Sections follow each
// other in a seeded order (shuffled bags, never the same one twice in a row);
// they all start and end with the same viaduct bay and plain railing, so any
// two join. Gives MidSections the parts, a ChimneySmoke and TowerClocks per
// section, and one untiled MidLayerLife for the whole row.
public static class MidSectionsBuilder
{
    public const string Dir = "Assets/Art/Backgrounds/Sections/";
    const string WhitePixel = "Assets/Art/Placeholder/white_16px.png";

#pragma warning disable 0649
    [System.Serializable] class Rgb { public int r, g, b; public Color Color => new Color32((byte)r, (byte)g, (byte)b, 255); }
    [System.Serializable] class Pt { public float x, y; }
    [System.Serializable] class Box { public int x, y, w, h; }
    [System.Serializable] class Smoke { public float x, y, interval, size; }
    [System.Serializable] class Clock { public float x, y, r; }
    [System.Serializable]
    class PartData
    {
        public string id, kind;
        public int x, y, w, h, frames;
        public float rate, travel, period, speed, radius, mouthX, mouthY;
        public int spokes = 4, hoist = -1, track0, track1, ropeX, ropeTop;
        public Rgb[] rope;
    }
    [System.Serializable]
    class SectionData
    {
        public string name;
        public int width, height;
        public PartData[] parts;
        public Smoke[] chimneys;
        public Clock[] clocks;
        public Rgb[] hands;
        public Box[] windows, ivy;
        public Pt[] perches, pennants;
    }
    [System.Serializable] class FileData { public SectionData[] sections; }
#pragma warning restore 0649

    // Section names in seeded order, enough to cover `count`.
    static List<string> Sequence(IList<string> names, int count, int seed)
    {
        var rng = new System.Random(seed);
        var seq = new List<string>();
        while (seq.Count < count)
        {
            var bag = names.OrderBy(_ => rng.Next()).ToList();
            if (seq.Count > 0 && bag[0] == seq[seq.Count - 1]) { bag.Add(bag[0]); bag.RemoveAt(0); }
            seq.AddRange(bag);
        }
        return seq;
    }

    // Everything the build needs, loaded and checked before any object is made.
    class Art
    {
        public FileData file;
        public Dictionary<string, SectionData> byName;
        public Sprite white;
        public readonly Dictionary<string, Sprite> stills = new Dictionary<string, Sprite>();
        public readonly Dictionary<string, Sprite[]> sheets = new Dictionary<string, Sprite[]>();
    }

    static string StillPath(string name, string suffix) => $"{Dir}bgsec_{name}{suffix}.png";
    static string SheetPath(string name, string id) => $"{Dir}bgsec_{name}_{id}.png";
    static readonly string[] LookSuffixes = { "_day", "", "_night" };

    // Reads bg_sections.json and prepares and loads every still and sheet it
    // names. Returns null with the first problem found.
    static Art Load(out string error)
    {
        error = null;
        var art = new Art();
        string json = Dir + "bg_sections.json";
        if (!File.Exists(json)) { error = $"{json} is missing"; return null; }
        try { art.file = JsonUtility.FromJson<FileData>(File.ReadAllText(json)); }
        catch (System.Exception e) { error = $"{json} does not parse: {e.Message}"; return null; }
        if (art.file?.sections == null || art.file.sections.Length == 0) { error = $"{json} lists no sections"; return null; }
        art.white = AssetDatabase.LoadAssetAtPath<Sprite>(WhitePixel);
        if (art.white == null) { error = $"{WhitePixel} is missing"; return null; }
        art.byName = new Dictionary<string, SectionData>();
        foreach (var d in art.file.sections)
        {
            if (d == null || string.IsNullOrEmpty(d.name) || d.width <= 0 || d.height != MidSections.Height)
            { error = $"{json}: a section without a name or with a bad size"; return null; }
            if (art.byName.ContainsKey(d.name)) { error = $"{json}: section {d.name} twice"; return null; }
            art.byName[d.name] = d;
            foreach (var suffix in LookSuffixes)
            {
                string path = StillPath(d.name, suffix);
                if (!File.Exists(path)) { error = $"{path} is missing"; return null; }
                LevelMap.PrepareSprite(path, true, Vector2.zero);
                var sprite = AssetDatabase.LoadAssetAtPath<Sprite>(path);
                if (sprite == null || (int)sprite.rect.width != d.width || (int)sprite.rect.height != d.height)
                { error = $"{path} is not a {d.width}x{d.height} sprite"; return null; }
                art.stills[path] = sprite;
            }
            foreach (var pd in d.parts ?? new PartData[0])
            {
                if (pd == null || string.IsNullOrEmpty(pd.id) || pd.w <= 0 || pd.h <= 0 || pd.frames <= 0)
                { error = $"{json}: section {d.name} has a part without an id, size or frames"; return null; }
                string path = SheetPath(d.name, pd.id);
                if (!File.Exists(path)) { error = $"{path} is missing"; return null; }
                LevelMap.PrepareStrip(path, pd.w, Vector2.zero);
                var frames = LevelMap.StripFrames(path);
                if (frames.Length != pd.frames * 3 || frames.Any(f => f == null))
                { error = $"{path}: {frames.Length} frames, expected {pd.frames * 3}"; return null; }
                art.sheets[path] = frames;
            }
        }
        return art;
    }

    // Mid layer root (bottom edge at `bottom`, local x 0 = world x 0 at load)
    // with sections from x0 to at least x1 (local units). Returns null after
    // logging one error when the section art or data is missing or broken;
    // the caller then uses the single mid picture.
    public static (Transform root, MidSections sections, MidLayerLife life)? Build(
        Transform parent, int lane, float bottom, float x0, float x1, int seed, float groundY, int order)
    {
        var art = Load(out string error);
        if (art == null)
        {
            Debug.LogError($"[MidSections] {error}; using the single mid picture instead. Rerun tools/make_bg_parts.py and copy its output to {Dir}.");
            return null;
        }
        var file = art.file;
        var byName = art.byName;

        var root = new GameObject("Background_Mid");
        root.transform.SetParent(parent, false);
        root.transform.localPosition = new Vector3(0f, bottom, 0f);
        var ms = root.AddComponent<MidSections>();
        ms.lane = lane;
        ms.stillOrder = order;
        ms.partOrder = order + 1;

        var rng = new System.Random(seed * 7919 + 13);
        int avg = (int)file.sections.Average(s => s.width);
        var seq = Sequence(byName.Keys.OrderBy(n => n).ToList(), Mathf.CeilToInt((x1 - x0) * MidSections.PixelsPerUnit / avg) + 2, seed);
        var built = new List<MidSections.Section>();
        var pigeons = new List<Vector2>();
        var pennants = new List<Vector2>();
        var windows = new List<RectInt>();
        var ivy = new List<RectInt>();
        float x = x0;
        int px = 0;
        foreach (var name in seq)
        {
            if (x >= x1) break;
            var d = byName[name];
            built.Add(Section(root.transform, d, built.Count, x, lane, order, rng, art));
            foreach (var p in d.perches ?? new Pt[0]) pigeons.Add(new Vector2(px + p.x, p.y));
            foreach (var p in d.pennants ?? new Pt[0]) pennants.Add(new Vector2(px + p.x, p.y));
            foreach (var w in d.windows ?? new Box[0]) windows.Add(new RectInt(px + w.x, w.y, w.w, w.h));
            foreach (var w in d.ivy ?? new Box[0]) ivy.Add(new RectInt(px + w.x, w.y, w.w, w.h));
            px += d.width;
            x += d.width / MidSections.PixelsPerUnit;
        }
        ms.sections = built.ToArray();

        var lifeGo = new GameObject("Life_Mid");
        lifeGo.transform.SetParent(root.transform, false);
        lifeGo.transform.localPosition = new Vector3(x0, 0f, 0f);
        var life = lifeGo.AddComponent<MidLayerLife>();
        life.tiled = false;
        life.pictureSize = new Vector2(px, MidSections.Height);
        life.lane = lane;
        life.seed = lane + 1;
        life.groundY = groundY;
        life.pigeons = pigeons.ToArray();
        life.pennants = pennants.ToArray();
        life.windows = windows.ToArray();
        life.ivy = ivy.ToArray();
        Debug.Log($"[MidSections] lane {lane}: {string.Join(", ", built.Select(s => s.name))}");
        return (root.transform, ms, life);
    }

    static MidSections.Kind KindOf(string kind) => kind switch
    {
        "spin" => MidSections.Kind.Spin,
        "swing" => MidSections.Kind.Swing,
        "flicker" => MidSections.Kind.Flicker,
        "pulley" => MidSections.Kind.Pulley,
        "crate" => MidSections.Kind.Crate,
        "train" => MidSections.Kind.Train,
        _ => MidSections.Kind.Front,
    };

    static SpriteRenderer Renderer(Transform parent, string name, int order, float z)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.transform.localPosition = new Vector3(0f, 0f, z);
        var sr = go.AddComponent<SpriteRenderer>();
        sr.sortingOrder = order;
        return sr;
    }

    static MidSections.Section Section(Transform parent, SectionData d, int index, float x, int lane, int order,
        System.Random rng, Art art)
    {
        var white = art.white;
        var go = new GameObject($"Section_{index}_{d.name}");
        go.transform.SetParent(parent, false);
        go.transform.localPosition = new Vector3(x, 0f, 0f);
        var s = new MidSections.Section { name = d.name, root = go.transform, width = d.width };
        SpriteRenderer Still(string suffix, string label)
        {
            var sr = Renderer(go.transform, $"Still_{label}", order, 0f);
            sr.sprite = art.stills[StillPath(d.name, suffix)];
            return sr;
        }
        s.day = Still("_day", "Day");
        s.dusk = Still("", "Dusk");
        s.night = Still("_night", "Night");

        var chimneys = (d.chimneys ?? new Smoke[0]).Select(c => new ChimneySmoke.Chimney
        {
            mouth = new Vector2(c.x, c.y), interval = c.interval, size = c.size,
        }).ToList();
        int cropLeft = chimneys.Count > 0 ? (int)chimneys.Min(c => c.mouth.x) - 20 : int.MaxValue;
        int cropRight = chimneys.Count > 0 ? (int)chimneys.Max(c => c.mouth.x) + 110 : int.MinValue;

        var parts = new List<MidSections.Part>();
        var data = d.parts ?? new PartData[0];
        for (int j = 0; j < data.Length; j++)
        {
            var pd = data[j];
            var frames = art.sheets[SheetPath(d.name, pd.id)];
            var holder = new GameObject($"Part_{pd.id}").transform;
            holder.SetParent(go.transform, false);
            float z = -0.002f * (j + 1);
            var p = new MidSections.Part
            {
                id = pd.id,
                kind = KindOf(pd.kind),
                a = Renderer(holder, "From", order + 1, z),
                b = Renderer(holder, "To", order + 1, z - 0.001f),
                frames = frames,
                box = new RectInt(pd.x, pd.y, pd.w, pd.h),
                rate = pd.rate,
                phase = (float)rng.NextDouble(),
                travel = pd.travel,
                period = pd.period,
                ropeX = pd.ropeX,
                ropeTop = pd.ropeTop,
                radius = pd.radius,
                spokes = pd.spokes,
                track0 = pd.track0,
                track1 = pd.track1,
                speed = pd.speed,
                mouth = new Vector2(pd.mouthX, pd.mouthY),
            };
            if (p.kind == MidSections.Kind.Pulley)
            {
                // The JSON counts hoists; the part list holds them as crates.
                var crates = Enumerable.Range(0, data.Length).Where(i => data[i].kind == "crate").ToList();
                p.hoist = pd.hoist >= 0 && pd.hoist < crates.Count ? crates[pd.hoist] : -1;
            }
            if (p.kind == MidSections.Kind.Crate)
            {
                p.rope = Renderer(holder, "Rope", order + 1, z + 0.0005f);
                p.rope.sprite = white;
                p.ropeColours = (pd.rope ?? new Rgb[0]).Select(c => c.Color).ToArray();
            }
            if (p.kind == MidSections.Kind.Train)
            {
                // Hidden where the track runs behind the buildings at both ends.
                var mask = new GameObject("TrackMask").AddComponent<SpriteMask>();
                mask.transform.SetParent(holder, false);
                mask.sprite = white;
                var size = white.bounds.size;
                float w = (pd.track1 - pd.track0) / MidSections.PixelsPerUnit, h = (pd.h + 8) / MidSections.PixelsPerUnit;
                mask.transform.localPosition = new Vector3((pd.track0 + pd.track1) / 2f / MidSections.PixelsPerUnit,
                    (MidSections.Height - pd.y - pd.h / 2f) / MidSections.PixelsPerUnit, 0f);
                mask.transform.localScale = new Vector3(w / size.x, h / size.y, 1f);
                p.a.maskInteraction = SpriteMaskInteraction.VisibleInsideMask;
                p.b.maskInteraction = SpriteMaskInteraction.VisibleInsideMask;
                p.smokeIndex = chimneys.Count;
                chimneys.Add(new ChimneySmoke.Chimney { mouth = new Vector2(pd.track0 + p.mouth.x, p.mouth.y), interval = 0.3f, size = 0.75f, moving = true });
                cropLeft = Mathf.Min(cropLeft, pd.track0 - 10);
                cropRight = Mathf.Max(cropRight, pd.track1 + 110);
            }
            parts.Add(p);
        }
        s.parts = parts.ToArray();

        if (chimneys.Count > 0)
        {
            var smokeGo = new GameObject("Smoke");
            smokeGo.transform.SetParent(go.transform, false);
            smokeGo.AddComponent<SpriteRenderer>().sortingOrder = order + 3;
            var smoke = smokeGo.AddComponent<ChimneySmoke>();
            smoke.background = s.dusk;
            smoke.chimneys = chimneys.ToArray();
            smoke.lane = lane;
            smoke.seed = lane * 31 + index + 1;
            smoke.cropLeft = Mathf.Max(0, cropLeft);
            smoke.cropRight = cropRight;
            s.smoke = smoke;
        }
        if (d.clocks != null && d.clocks.Length > 0)
        {
            var clockGo = new GameObject("Clocks");
            clockGo.transform.SetParent(go.transform, false);
            clockGo.AddComponent<SpriteRenderer>().sortingOrder = order + 2;
            var clocks = clockGo.AddComponent<TowerClocks>();
            clocks.crop = true;
            clocks.background = s.dusk;
            clocks.faces = d.clocks.Select(c => new TowerClocks.Face { centre = new Vector2(c.x, c.y), radius = c.r }).ToArray();
            if (d.hands != null && d.hands.Length == 3)
                (clocks.dayHands, clocks.duskHands, clocks.nightHands) = (d.hands[0].Color, d.hands[1].Color, d.hands[2].Color);
            s.clocks = clocks;
        }
        return s;
    }
}
