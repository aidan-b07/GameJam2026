using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Tilemaps;

// Builds the Level 1 "The Clockworks" map (DESIGN.md, Level 1 sketch) into
// Assets/Scenes/Level1_Clockworks.unity. Map only: obstacles, spawns,
// checkpoints and the door are colour-coded placeholder objects with no logic.
// Run Tick & Tock > Build Level 1 Map; it overwrites the scene each time.
//
// Units: 1 tile = 1 world unit (16px tiles at 16 PPU). Ground surface is y = 0,
// so players stand at y = 0. Tick's lane sits at world y = 0, Tock's at y = -40.
public static class Level1Builder
{
    const string ScenePath = "Assets/Scenes/Level1_Clockworks.unity";
    const string GroundSheet = "ground_16px";
    const string PropsSheet = "props_16px";
    const string WhiteSprite = "Assets/Art/Placeholder/white_16px.png";
    const int Length = 200;
    const float TockLaneY = -40f;
    const float CameraY = 3.5f;        // camera centre above the lane's ground
    const float CameraSize = 5.625f;   // half of the 11.25-unit background height

    static readonly Color TickColor = new Color(1f, 0.55f, 0.15f);
    static readonly Color TockColor = new Color(0.15f, 0.8f, 0.75f);

    [MenuItem("Tick & Tock/Build Level 1 Map")]
    public static void Build()
    {
        PrepareWhiteSprite();
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

        var tick = new Lane("Tick", 0f, TickColor);
        BuildTickLane(tick);
        tick.Finish();

        var tock = new Lane("Tock", TockLaneY, TockColor);
        BuildTockLane(tock);
        tock.Finish();

        CreateCamera("Camera_Tick", 0f, new Rect(0f, 0.5f, 1f, 0.5f), true);
        CreateCamera("Camera_Tock", TockLaneY, new Rect(0f, 0f, 1f, 0.5f), false);

        EditorSceneManager.SaveScene(scene, ScenePath);
        AddToBuildSettings();
        Debug.Log($"[Level1Builder] Saved {ScenePath}");
    }

    // Tick (top lane). Sections follow DESIGN.md "Level 1 sketch".
    static void BuildTickLane(Lane l)
    {
        l.Fill(-8, Length + 8, 0);
        l.Fill(-8, -1, 14);                       // start wall
        l.Spawn(3);
        l.Checkpoint(1);

        // 1. Tutorial A: crusher. Tock must STOP to freeze it.
        l.Crusher(22);

        // 2. Tutorial B: treadmill. Tick runs on it to drop Tock's drawbridge.
        l.Checkpoint(40);
        l.Marker("Treadmill_1", 47, 0, 7, 0.4f, new Color(0.95f, 0.8f, 0.2f));
        l.Fill(58, 63, 1);                        // small step

        // 3. Frozen gears: Tick stops while Tock climbs. Rest pad to wait on.
        l.Marker("RestPad_1", 70, 0, 3, 0.25f, new Color(0.3f, 0.9f, 0.4f));
        l.Ledge(77, 82, 2);
        l.Fill(86, 91, 2);

        // 4. Alternation: crushers offset against Tock's (116, 132, 148).
        l.Checkpoint(98);
        l.Crusher(108);
        l.Crusher(124);
        l.Crusher(140);

        // 5. Finale: vine grows to the high ground (Tick needs fast time).
        l.Checkpoint(152);
        l.Fill(166, Length + 8, 5);
        l.Marker("Vine_1", 165, 0, 1, 5, new Color(0.2f, 0.6f, 0.2f));
        l.Door(186, 5);

        l.Fill(Length - 4, Length + 8, 14);       // end wall
    }

    // Tock (bottom lane).
    static void BuildTockLane(Lane l)
    {
        l.Fill(-8, 49, 0);
        l.Fill(57, 159, 0);
        l.Fill(169, Length + 8, 0);
        l.Fill(-8, -1, 14);                       // start wall
        l.Spawn(3);
        l.Checkpoint(1);

        // 1. Tutorial A: rest pad. "Tock: STOP".
        l.Marker("RestPad_1", 19, 0, 3, 0.25f, new Color(0.3f, 0.9f, 0.4f));

        // 2. Tutorial B: slow drawbridge over a pit (needs Tick on the treadmill).
        l.Checkpoint(40);
        l.Marker("Drawbridge_1", 49, 0, 1, 7, new Color(0.55f, 0.35f, 0.2f)); // raised, hinged at its base
        l.Busy(49, 57);

        // 3. Frozen gears: gears fall from the top; freeze them and climb the wall.
        l.Marker("FallingGears_1", 66, 9, 10, 1, new Color(1f, 0.6f, 0.1f));
        l.Fill(78, 94, 5);
        l.Prop(79, 5, 1, 2);                      // big gear on the wall top
        l.Fill(95, 97, 2);                        // step down

        // 4. Alternation: crushers offset against Tick's (108, 124, 140).
        l.Checkpoint(100);
        l.Crusher(116);
        l.Crusher(132);
        l.Crusher(148);

        // 5. Finale: waterfall over a pit; frozen water is a bridge (Tock needs freeze).
        l.Checkpoint(152);
        l.Fill(157, 170, 10, 8);                  // overhang the water falls from
        l.Marker("Waterfall_1", 160, -4, 9, 12, new Color(0.3f, 0.7f, 1f, 0.5f));
        l.Door(186, 0);

        l.Fill(Length - 4, Length + 8, 14);       // end wall
    }

    class Lane
    {
        readonly string name;
        readonly Color color;
        readonly GameObject root;
        readonly Transform markers;
        readonly HashSet<Vector2Int> solid = new HashSet<Vector2Int>();
        readonly HashSet<Vector2Int> ledges = new HashSet<Vector2Int>();
        readonly Dictionary<Vector2Int, (int r, int c)> props = new Dictionary<Vector2Int, (int, int)>();
        readonly Dictionary<int, int> surface = new Dictionary<int, int>();
        readonly List<(int x0, int x1)> busy = new List<(int, int)>();

        public Lane(string name, float y, Color color)
        {
            this.name = name;
            this.color = color;
            root = new GameObject($"Lane_{name}", typeof(Grid));
            root.transform.position = new Vector3(0f, y, 0f);
            markers = new GameObject("Placeholders").transform;
            markers.SetParent(root.transform, false);
        }

        // Solid cells for x in [x0, x1], y in [bottom, top - 1]. Players stand at y = top.
        public void Fill(int x0, int x1, int top, int bottom = -5)
        {
            for (int x = x0; x <= x1; x++)
            {
                for (int y = bottom; y < top; y++) solid.Add(new Vector2Int(x, y));
                if (bottom <= -5) surface[x] = surface.TryGetValue(x, out var s) ? Mathf.Max(s, top) : top;
            }
        }

        // One-way ledge in row y (stand on it at about y + 1).
        public void Ledge(int x0, int x1, int y)
        {
            for (int x = x0; x <= x1; x++) ledges.Add(new Vector2Int(x, y));
        }

        public void Prop(int x, int y, int r, int c) => props[new Vector2Int(x, y)] = (r, c);

        public void Busy(int x0, int x1) => busy.Add((x0, x1));

        public GameObject Marker(string id, float x, float y, float w, float h, Color c)
        {
            var go = new GameObject(id);
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

        int checkpoints;
        public void Checkpoint(int x)
        {
            Marker($"Checkpoint_{checkpoints++}", x, 0, 0.5f, 0.5f, new Color(1f, 0.4f, 0.7f));
            Prop(x, Surface(x), 3, 4);            // lit lantern
        }

        int crushers;
        // Crusher hanging from a stone housing, drawn in its raised position.
        public void Crusher(int x)
        {
            Fill(x - 1, x + 3, 10, 8);
            Marker($"Crusher_{++crushers}", x, 5, 3, 3, new Color(0.55f, 0.5f, 0.45f));
            Prop(x - 2, 8, 1, 1);
            Prop(x + 4, 8, 1, 1);
            Busy(x - 3, x + 5);
        }

        // Clock door with the big clock face above it.
        public void Door(int x, int y)
        {
            Marker("ClockDoor", x, y, 3, 3.5f, new Color(1f, 0.85f, 0.3f));
            Prop(x, y + 5, 6, 6); Prop(x + 1, y + 5, 6, 7);
            Prop(x, y + 4, 7, 6); Prop(x + 1, y + 4, 7, 7);
            Busy(x - 3, x + 6);
        }

        int Surface(int x) => surface.TryGetValue(x, out var s) ? s : 0;

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

            var platforms = CreateTilemap("Platforms", 0);
            foreach (var p in ledges)
            {
                bool l = ledges.Contains(p + Vector2Int.left), r = ledges.Contains(p + Vector2Int.right);
                platforms.SetTile((Vector3Int)p, Tile(GroundSheet, 4, !l ? 0 : !r ? 2 : 1));
            }
            var platCol = platforms.gameObject.AddComponent<TilemapCollider2D>();
            platCol.usedByEffector = true;
            platforms.gameObject.AddComponent<PlatformEffector2D>();

            var decor = CreateTilemap("Decor", -5);
            foreach (var kv in props) decor.SetTile((Vector3Int)kv.Key, Tile(PropsSheet, kv.Value.r, kv.Value.c));

            CreateBackground("Background_Far", "Assets/Art/Backgrounds/bg_far_180h.png", -20, 0.85f);
            CreateBackground("Background_Mid", "Assets/Art/Backgrounds/bg_mid_180h.png", -10, 1f);
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
            if (t == null) Debug.LogError($"[Level1Builder] Missing tile {path}; run Tick & Tock > Import Art first.");
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

    static void AddToBuildSettings()
    {
        var scenes = EditorBuildSettings.scenes.Where(s => s.path != ScenePath).ToList();
        scenes.Insert(0, new EditorBuildSettingsScene(ScenePath, true));
        EditorBuildSettings.scenes = scenes.ToArray();
    }
}
