using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Tilemaps;

// Shared map builder for the Level<N>Builder scripts. A level is two lanes
// (Tick on top, Tock below), each described in code with Fill/Ledge/Marker
// calls. Build() writes a fresh scene: autotiled ground with colliders,
// one-way ledges, decor props, backgrounds, split-screen cameras, and
// colour-coded placeholder objects for everything that will get logic later.
//
// Units: 1 tile = 1 world unit (32px tiles at 32 PPU). Ground surface is y = 0,
// so players stand at y = 0. Tick's lane sits at world y = 0, Tock's at y = -40.
public static class LevelMap
{
    public const float TockLaneY = -40f;
    public const float CameraY = 3.5f;        // camera centre above the lane's ground
    public const float CameraSize = 5.625f;   // half of the 11.25-unit background height

    const string GroundSheet = "ground_32px";
    const string PropsSheet = "props_32px";
    const string WhiteSprite = "Assets/Art/Placeholder/white_16px.png";
    // Layer the player's ground check looks for (PlayerMovement.groundLayer).
    const string GroundLayer = "Ground";

    static readonly Color TickColor = new Color(1f, 0.55f, 0.15f);
    static readonly Color TockColor = new Color(0.15f, 0.8f, 0.75f);

    // Placeholder colours, one per obstacle type, so a level reads at a glance.
    public static readonly Color Stone = new Color(0.55f, 0.5f, 0.45f);
    public static readonly Color Rest = new Color(0.3f, 0.9f, 0.4f);
    public static readonly Color Treadmill = new Color(0.95f, 0.8f, 0.2f);
    public static readonly Color Wood = new Color(0.55f, 0.35f, 0.2f);
    public static readonly Color Gear = new Color(1f, 0.6f, 0.1f);
    public static readonly Color Vine = new Color(0.2f, 0.6f, 0.2f);
    public static readonly Color Water = new Color(0.3f, 0.7f, 1f, 0.5f);
    public static readonly Color Platform = new Color(0.6f, 0.45f, 0.85f);
    public static readonly Color Pendulum = new Color(0.8f, 0.8f, 0.85f);
    public static readonly Color Laser = new Color(1f, 0.15f, 0.2f, 0.7f);
    public static readonly Color Guard = new Color(0.35f, 0.4f, 0.6f);
    public static readonly Color Conveyor = new Color(0.45f, 0.45f, 0.5f);

    public static void Build(string scenePath, int length, Action<Lane> tick, Action<Lane> tock)
    {
        PrepareWhiteSprite();
        TileCache.Clear();
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

        var t = new Lane("Tick", 0f, TickColor, length);
        tick(t);
        t.Finish();

        var b = new Lane("Tock", TockLaneY, TockColor, length);
        tock(b);
        b.Finish();

        CreateCamera("Camera_Tick", 0f, new Rect(0f, 0.5f, 1f, 0.5f), true);
        CreateCamera("Camera_Tock", TockLaneY, new Rect(0f, 0f, 1f, 0.5f), false);

        EditorSceneManager.SaveScene(scene, scenePath);
        AddToBuildSettings(scenePath);
        Debug.Log($"[LevelMap] Saved {scenePath}");
    }

    public class Lane
    {
        public readonly int Length;
        readonly string name;
        readonly Color color;
        readonly GameObject root;
        readonly Transform markers;
        readonly HashSet<Vector2Int> solid = new HashSet<Vector2Int>();
        readonly HashSet<Vector2Int> ledges = new HashSet<Vector2Int>();
        readonly Dictionary<Vector2Int, (int r, int c)> props = new Dictionary<Vector2Int, (int, int)>();
        readonly Dictionary<int, int> surface = new Dictionary<int, int>();
        readonly List<(int x0, int x1)> busy = new List<(int, int)>();
        readonly Dictionary<string, int> counts = new Dictionary<string, int>();

        public Lane(string name, float y, Color color, int length)
        {
            this.name = name;
            this.color = color;
            Length = length;
            root = new GameObject($"Lane_{name}", typeof(Grid));
            root.transform.position = new Vector3(0f, y, 0f);
            markers = new GameObject("Placeholders").transform;
            markers.SetParent(root.transform, false);
        }

        // Solid cells for x in [x0, x1], y in [bottom, top - 1]. Players stand at y = top.
        // Fills that reach the bottom (-5) count as ground; higher ones are ceilings.
        public void Fill(int x0, int x1, int top, int bottom = -5)
        {
            for (int x = x0; x <= x1; x++)
            {
                for (int y = bottom; y < top; y++) solid.Add(new Vector2Int(x, y));
                if (bottom <= -5) surface[x] = surface.TryGetValue(x, out var s) ? Mathf.Max(s, top) : top;
            }
        }

        // Ground from x0 to x1 at height 0, with the usual start and end walls.
        public void Floor(params (int x0, int x1)[] spans)
        {
            foreach (var (x0, x1) in spans) Fill(x0, x1, 0);
            Fill(-8, -1, 14);                   // start wall
            Fill(Length - 4, Length + 8, 14);   // end wall
        }

        // One-way ledge in row y (stand on it at about y + 1).
        public void Ledge(int x0, int x1, int y)
        {
            for (int x = x0; x <= x1; x++) ledges.Add(new Vector2Int(x, y));
        }

        public void Prop(int x, int y, int r, int c) => props[new Vector2Int(x, y)] = (r, c);

        public void Busy(int x0, int x1) => busy.Add((x0, x1));

        // Placeholder object, auto-numbered: Marker("Crusher", ...) -> Crusher_1, Crusher_2...
        public GameObject Marker(string type, float x, float y, float w, float h, Color c)
        {
            counts[type] = counts.TryGetValue(type, out var n) ? n + 1 : 1;
            var go = new GameObject($"{type}_{counts[type]}");
            go.transform.SetParent(markers, false);
            go.transform.localPosition = new Vector3(x + w / 2f, y + h / 2f, 0f);
            go.transform.localScale = new Vector3(w, h, 1f);
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = AssetDatabase.LoadAssetAtPath<Sprite>(WhiteSprite);
            sr.color = c;
            sr.sortingOrder = 5;
            Busy(Mathf.FloorToInt(x) - 1, Mathf.CeilToInt(x + w));
            return go;
        }

        public void Spawn(int x) => Marker($"Spawn_{name}", x, 0, 1, 1.5f, color);

        // Drawbridge over the pit between x0 and x1 (ground ends at x0 and starts
        // again at x1). The leaf is as long as the pit is wide and hangs down the
        // near wall from a hinge at the pit's left edge. BridgeMovement swings it
        // 270 degrees counterclockwise: it passes flat across the pit at 90 degrees
        // without stopping and lands flat on the ground to the left. Players must
        // stop lane time while it spans the pit to cross.
        public GameObject Drawbridge(int x0, int x1)
        {
            int length = x1 - x0;
            var go = Marker("Drawbridge", x0, -length, 1, length, Wood);
            int layer = LayerMask.NameToLayer(GroundLayer);
            if (layer >= 0) go.layer = layer;

            var rb = go.AddComponent<Rigidbody2D>();
            rb.bodyType = RigidbodyType2D.Kinematic;
            rb.gravityScale = 0f;
            rb.interpolation = RigidbodyInterpolation2D.Interpolate;
            go.AddComponent<BoxCollider2D>();

            // Hinge at the centre of the leaf's top tile, at ground level.
            var bridge = new SerializedObject(go.AddComponent<BridgeMovement>());
            bridge.FindProperty("hingeAnchor").vector2Value = new Vector2(0f, 0.5f - 0.5f / length);
            bridge.ApplyModifiedPropertiesWithoutUndo();
            return go;
        }

        public void Checkpoint(int x)
        {
            Marker("Checkpoint", x, Surface(x), 0.5f, 0.5f, new Color(1f, 0.4f, 0.7f));
            Prop(x, Surface(x), 3, 4);          // lit lantern
        }

        public void RestPad(int x) => Marker("RestPad", x, Surface(x), 3, 0.25f, Rest);

        // Crusher hanging from a stone housing, drawn in its raised position.
        public void Crusher(int x)
        {
            Fill(x - 1, x + 3, 10, 8);
            Marker("Crusher", x, 5, 3, 3, Stone);
            Prop(x - 2, 8, 1, 1);
            Prop(x + 4, 8, 1, 1);
            Busy(x - 3, x + 5);
        }

        // Laser gate between a floor emitter and a ceiling emitter, drawn "on".
        public void LaserGate(int x)
        {
            Fill(x - 1, x + 1, 10, 8);
            Marker("LaserGate", x + 0.3f, 0, 0.4f, 8, Laser);
            Prop(x, 0, 2, 7);                   // emitter machine
            Busy(x - 2, x + 2);
        }

        // Clock door with the big clock face above it.
        public void Door(int x)
        {
            int y = Surface(x);
            Marker("ClockDoor", x, y, 3, 3.5f, new Color(1f, 0.85f, 0.3f));
            Prop(x, y + 5, 6, 6); Prop(x + 1, y + 5, 6, 7);
            Prop(x, y + 4, 7, 6); Prop(x + 1, y + 4, 7, 7);
            Busy(x - 3, x + 6);
        }

        public int Surface(int x) => surface.TryGetValue(x, out var s) ? s : 0;

        bool IsBusy(int x) => busy.Any(b => x >= b.x0 && x <= b.x1);

        public void Finish()
        {
            Decorate();

            var ground = CreateTilemap("Ground", 0);
            foreach (var p in solid) ground.SetTile((Vector3Int)p, GroundTile(p));
            var groundCol = ground.gameObject.AddComponent<TilemapCollider2D>();
            groundCol.compositeOperation = Collider2D.CompositeOperation.Merge;
            ground.gameObject.AddComponent<Rigidbody2D>().bodyType = RigidbodyType2D.Static;
            ground.gameObject.AddComponent<CompositeCollider2D>();
            SetGroundLayer(ground.gameObject);

            var platforms = CreateTilemap("Platforms", 0);
            foreach (var p in ledges)
            {
                bool l = ledges.Contains(p + Vector2Int.left), r = ledges.Contains(p + Vector2Int.right);
                platforms.SetTile((Vector3Int)p, Tile(GroundSheet, 4, !l ? 0 : !r ? 2 : 1));
            }
            var platCol = platforms.gameObject.AddComponent<TilemapCollider2D>();
            platCol.usedByEffector = true;
            platforms.gameObject.AddComponent<PlatformEffector2D>();
            SetGroundLayer(platforms.gameObject);

            var decor = CreateTilemap("Decor", -5);
            foreach (var kv in props) decor.SetTile((Vector3Int)kv.Key, Tile(PropsSheet, kv.Value.r, kv.Value.c));

            CreateBackground("Background_Far", "Assets/Art/Backgrounds/bg_far_360h.png", -20, 0.85f);
            CreateBackground("Background_Mid", "Assets/Art/Backgrounds/bg_mid_360h.png", -10, 1f);
        }

        // Lamp posts, crates, barrels and leaf piles on free stretches of ground.
        void Decorate()
        {
            for (int x = 4; x < Length - 6; x++)
            {
                if (IsBusy(x) || !surface.ContainsKey(x) || props.ContainsKey(new Vector2Int(x, Surface(x)))) continue;
                int y = Surface(x);
                // Only on flat ground with nothing solid directly above.
                if (solid.Contains(new Vector2Int(x, y)) || Surface(x - 1) != y || Surface(x + 1) != y) continue;
                int h = Hash(x, name.Length) % 100;
                if (x % 12 == 6) Prop(x, y, 0, 1);            // lamp post
                else if (h < 4) Prop(x, y, 1, 5);             // crate
                else if (h < 7) Prop(x, y, 1, 6);             // barrel
                else if (h < 14) Prop(x, y, 0, 7);            // leaf pile
            }
        }

        // 9-slice autotile from the ground sheet: rows 0-2 are top, middle and bottom,
        // column 0 and 7 are the left and right edges, columns 1-6 are fill variants.
        TileBase GroundTile(Vector2Int p)
        {
            bool up = solid.Contains(p + Vector2Int.up), down = solid.Contains(p + Vector2Int.down);
            bool left = solid.Contains(p + Vector2Int.left), right = solid.Contains(p + Vector2Int.right);
            int row = !up ? 0 : !down ? 2 : 1;
            int col;
            if (!left) col = 0;
            else if (!right) col = 7;
            else
            {
                int h = Hash(p.x, p.y) % 100;
                if (row == 0) col = h < 12 ? 2 + h % 3 : new[] { 1, 5, 6 }[h % 3];   // 2-4 have ivy
                else col = 1 + h % 6;
            }
            return Tile(GroundSheet, row, col);
        }

        static void SetGroundLayer(GameObject go)
        {
            int layer = LayerMask.NameToLayer(GroundLayer);
            if (layer >= 0) go.layer = layer;
        }

        Tilemap CreateTilemap(string id, int order)
        {
            var go = new GameObject(id, typeof(Tilemap), typeof(TilemapRenderer));
            go.transform.SetParent(root.transform, false);
            go.GetComponent<TilemapRenderer>().sortingOrder = order;
            return go.GetComponent<Tilemap>();
        }

        void CreateBackground(string id, string path, int order, float tint)
        {
            var go = new GameObject(id);
            go.transform.SetParent(root.transform, false);
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = AssetDatabase.LoadAssetAtPath<Sprite>(path);
            sr.drawMode = SpriteDrawMode.Tiled;
            var size = sr.sprite.bounds.size;
            float width = Length + 60;
            sr.size = new Vector2(width, size.y);
            sr.sortingOrder = order;
            sr.color = new Color(tint, tint, tint, 1f);
            // Top edge just above the view; the bottom hides behind the ground.
            float bottom = CameraY + CameraSize + 0.25f - size.y;
            go.transform.localPosition = new Vector3(width / 2f - 30f, bottom + size.y / 2f, 0f);
        }
    }

    static int Hash(int x, int y) => ((x * 73856093) ^ (y * 19349663)) & 0x7fffffff;

    static readonly Dictionary<string, Tile> TileCache = new Dictionary<string, Tile>();

    static Tile Tile(string sheet, int r, int c)
    {
        string path = $"Assets/Art/Tiles/Generated/{sheet}/{sheet}_r{r}c{c}.asset";
        if (!TileCache.TryGetValue(path, out var t) || t == null)
        {
            t = AssetDatabase.LoadAssetAtPath<Tile>(path);
            if (t == null) Debug.LogError($"[LevelMap] Missing tile {path}; run Tick & Tock > Import Art first.");
            TileCache[path] = t;
        }
        return t;
    }

    static void CreateCamera(string id, float laneY, Rect viewport, bool listener)
    {
        var go = new GameObject(id);
        go.tag = listener ? "MainCamera" : "Untagged";
        go.transform.position = new Vector3(18f, laneY + CameraY, -10f);
        var cam = go.AddComponent<Camera>();
        cam.orthographic = true;
        cam.orthographicSize = CameraSize;
        cam.rect = viewport;
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = new Color(0.1f, 0.06f, 0.15f);
        if (listener) go.AddComponent<AudioListener>();
    }

    static void PrepareWhiteSprite()
    {
        var ti = (TextureImporter)AssetImporter.GetAtPath(WhiteSprite);
        if (ti.textureType == TextureImporterType.Sprite && ti.spritePixelsPerUnit == 16) return;
        ti.textureType = TextureImporterType.Sprite;
        ti.spriteImportMode = SpriteImportMode.Single;
        ti.spritePixelsPerUnit = 16;
        ti.filterMode = FilterMode.Point;
        ti.textureCompression = TextureImporterCompression.Uncompressed;
        ti.SaveAndReimport();
    }

    // Keeps level scenes in Build Settings sorted by name (Level1_, Level2_, ...).
    static void AddToBuildSettings(string scenePath)
    {
        var scenes = EditorBuildSettings.scenes.Where(s => s.path != scenePath).ToList();
        scenes.Add(new EditorBuildSettingsScene(scenePath, true));
        var levels = scenes.Where(s => s.path.Contains("/Level")).OrderBy(s => s.path).ToList();
        var others = scenes.Where(s => !s.path.Contains("/Level"));
        EditorBuildSettings.scenes = levels.Concat(others).ToArray();
    }
}
