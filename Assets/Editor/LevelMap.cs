using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Tilemaps;
using UnityEngine.UI;

// Shared map builder for the Level<N>Builder scripts. A level is two lanes
// (Tick on top, Tock below), each described in code with Fill/Ledge/Marker
// calls. Build() writes a fresh scene: autotiled ground with colliders,
// one-way ledges, decor props, backgrounds, split-screen cameras, and
// colour-coded placeholder objects for everything that will get logic later.
// Each lane gets a LaneSky with day, dusk and night backgrounds, sun and moon;
// Tick's lane starts at 10:00 (day) and Tock's at 02:00 (night). A screen
// overlay draws the Unraveling crack on the seam between the two lanes.
//
// Units: 1 tile = 1 world unit (32px tiles at 32 PPU). Ground surface is y = 0,
// so players stand at y = 0. Tick's lane sits at world y = 0, Tock's at y = -40.
public static class LevelMap
{
    public const float TockLaneY = -40f;
    public const float CameraY = 3.5f;        // camera centre above the lane's ground
    public const float CameraSize = 5.625f;   // half of the 11.25-unit background height
    public const float TickStartHour = 10f;
    public const float TockStartHour = 2f;

    const string GroundSheet = "ground_32px";
    const string PropsSheet = "props_32px";
    const string WhiteSprite = "Assets/Art/Placeholder/white_16px.png";
    // Drawbridge leaf from tools/make_drawbridge.py: 1x7 tiles, hinge at the top.
    const string DrawbridgeSprite = "Assets/Art/Obstacles/drawbridge_32px.png";
    // Back wall of the slot the drawbridge lands in, from tools/make_recess.py.
    const string RecessBack = "recess_back";
    const string RecessBackLeft = "recess_back_left";
    const string RecessBackRight = "recess_back_right";
    // The wall tiles are 32x38 (6 rows hang behind the slot floor); the left one
    // is 2 px wider on the left. Pivots put the top 32x32 on the cell.
    static readonly Vector2 RecessPivot = new Vector2(0.5f, 1f - 16f / 38f);
    static readonly Vector2 RecessLeftPivot = new Vector2(18f / 34f, 1f - 16f / 38f);
    // Layer the player's ground check looks for (PlayerMovement.groundLayer).
    const string GroundLayer = "Ground";
    // Player prefabs placed at each lane's spawn point (Tock's is a variant).
    const string TickPrefab = "Assets/Prefabs/Player.prefab";
    const string TockPrefab = "Assets/Prefabs/Player_Tock.prefab";
    // Parallax depth of the background layers: 0 moves with the ground, 1 with the camera.
    const float FarDepth = 0.85f;
    const float MidDepth = 0.6f;

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

    public static void Build(string scenePath, int length, Action<Lane> tick, Action<Lane> tock,
        float tickHour = TickStartHour, float tockHour = TockStartHour)
    {
        PrepareWhiteSprite();
        PrepareSprite(DrawbridgeSprite, true, new Vector2(0.5f, 0.5f));
        PrepareSprite(ObstaclePng(RecessBack), false, RecessPivot);
        PrepareSprite(ObstaclePng(RecessBackLeft), false, RecessLeftPivot);
        PrepareSprite(ObstaclePng(RecessBackRight), false, RecessPivot);
        TileCache.Clear();
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

        var t = new Lane("Tick", 0f, TickColor, length, tickHour);
        tick(t);
        t.Finish();

        var b = new Lane("Tock", TockLaneY, TockColor, length, tockHour);
        tock(b);
        b.Finish();

        t.Sky.skyAnchor = CreateCamera("Camera_Tick", 0f, new Rect(0f, 0.5f, 1f, 0.5f), true);
        b.Sky.skyAnchor = CreateCamera("Camera_Tock", TockLaneY, new Rect(0f, 0f, 1f, 0.5f), false);
        t.AddParallax(t.Sky.skyAnchor);
        b.AddParallax(b.Sky.skyAnchor);
        t.Sky.Apply();
        b.Sky.Apply();
        CreateDivider(t.Sky.skyAnchor, b.Sky.skyAnchor);
        var players = new[] { t.PlacePlayer(TickPrefab), b.PlacePlayer(TockPrefab) };
        var rig = CreateCameraRig(length, new[] { t.Sky.skyAnchor, b.Sky.skyAnchor }, players);
        CreateGameFlow(players, rig);
        new GameObject("GameManager").AddComponent<GameManager>();

        EditorSceneManager.SaveScene(scene, scenePath);
        AddToBuildSettings(scenePath);
        Debug.Log($"[LevelMap] Saved {scenePath}");
    }

    public class Lane
    {
        public readonly int Length;
        public LaneSky Sky { get; private set; }
        readonly float startHour;
        readonly string name;
        readonly Color color;
        readonly GameObject root;
        readonly Transform markers;
        readonly HashSet<Vector2Int> solid = new HashSet<Vector2Int>();
        readonly HashSet<Vector2Int> ledges = new HashSet<Vector2Int>();
        readonly HashSet<Vector2Int> recess = new HashSet<Vector2Int>();
        readonly List<SpriteRenderer> tinted = new List<SpriteRenderer>();
        readonly Dictionary<Vector2Int, (int r, int c)> props = new Dictionary<Vector2Int, (int, int)>();
        readonly Dictionary<int, int> surface = new Dictionary<int, int>();
        readonly List<(int x0, int x1)> busy = new List<(int, int)>();
        readonly Dictionary<string, int> counts = new Dictionary<string, int>();
        readonly List<(Transform t, float depth)> backgrounds = new List<(Transform, float)>();
        Vector3? spawn;
        int checkpoints;

        public Lane(string name, float y, Color color, int length, float startHour)
        {
            this.name = name;
            this.color = color;
            Length = length;
            this.startHour = startHour;
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

        // Spawn point, also checkpoint 0. The player sprite shows where it is, so
        // the box is hidden.
        public void Spawn(int x)
        {
            var go = Marker($"Spawn_{name}", x, 0, 1, 1.5f, color);
            go.GetComponent<SpriteRenderer>().enabled = false;
            spawn = new Vector3(go.transform.position.x, root.transform.position.y + Surface(x), 0f);
            AddCheckpoint(go, 0, spawn.Value);
        }

        // Lane index for GameFlow.players and Checkpoint.lane.
        int LaneIndex => root.transform.position.y < 0f ? 1 : 0;

        void AddCheckpoint(GameObject go, int index, Vector2 feet)
        {
            var c = go.AddComponent<Checkpoint>();
            c.index = index;
            c.lane = LaneIndex;
            c.feet = feet;
        }

        // The player for this lane, its collider standing on the spawn point (null without one).
        public Transform PlacePlayer(string prefabPath)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            if (prefab == null || spawn == null)
            {
                Debug.LogWarning($"[LevelMap] No player placed in {name}'s lane (prefab {prefabPath}, spawn {spawn}).");
                return null;
            }
            var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            var box = prefab.GetComponent<BoxCollider2D>();
            float feet = box != null ? box.offset.y - box.size.y / 2f : -0.5f;
            go.transform.position = spawn.Value - new Vector3(0f, feet, 0f);
            return go.transform;
        }

        // Background layers scroll at their depth behind the lane camera.
        public void AddParallax(Transform cam)
        {
            foreach (var (t, depth) in backgrounds)
            {
                var p = t.gameObject.AddComponent<Parallax>();
                p.cameraTransform = cam;
                p.depth = depth;
            }
        }

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
            go.AddComponent<BoxCollider2D>().size = Vector2.one;

            // The leaf art. The object keeps its (1, length) scale, which
            // BridgeMovement's hinge maths relies on, so the sprite is drawn
            // 1x1 in local units (sliced, no borders) and the scale stretches
            // it to the leaf: exactly 32 px per tile for a 7-tile pit.
            var sr = go.GetComponent<SpriteRenderer>();
            var art = AssetDatabase.LoadAssetAtPath<Sprite>(DrawbridgeSprite);
            if (art != null)
            {
                sr.sprite = art;
                sr.color = Color.white;
                sr.drawMode = SpriteDrawMode.Sliced;
                sr.size = Vector2.one;
                go.transform.localScale = new Vector3(1f, length, 1f);   // the draw mode change rescales it
                tinted.Add(sr);
            }

            // Slot in the ground where the leaf lands (flat, pointing left from the
            // hinge), so it lies flush with the floor instead of sinking into it.
            Recess(x0 - length + 1, x0 - 1);

            // Hinge at the centre of the leaf's top tile, at ground level.
            var bridge = new SerializedObject(go.AddComponent<BridgeMovement>());
            bridge.FindProperty("hingeAnchor").vector2Value = new Vector2(0f, 0.5f - 0.5f / length);
            // Lane time comes from GameManager.GetWorldSpeed(lane).
            bridge.FindProperty("lane").intValue = LaneIndex;
            bridge.ApplyModifiedPropertiesWithoutUndo();
            return go;
        }

        // One-tile-deep slot cut into the ground surface over x0..x1. Its floor and
        // lip are normal ground tiles; its back is a darkened wall without collider.
        public void Recess(int x0, int x1)
        {
            for (int x = x0; x <= x1; x++)
            {
                var p = new Vector2Int(x, Surface(x) - 1);
                solid.Remove(p);
                recess.Add(p);
            }
            Busy(x0 - 1, x1 + 1);
        }

        // Respawn point, numbered in order after the spawn (checkpoint 0). Reached
        // when a player walks through its column (a trigger 12 units tall).
        public void Checkpoint(int x)
        {
            var go = Marker("Checkpoint", x, Surface(x), 0.5f, 0.5f, new Color(1f, 0.4f, 0.7f));
            Prop(x, Surface(x), 3, 4);          // lit lantern
            var trigger = go.AddComponent<BoxCollider2D>();
            trigger.isTrigger = true;
            trigger.size = new Vector2(1f, 24f);        // local units: the marker is 0.5 x 0.5
            trigger.offset = new Vector2(0f, 11.5f);
            var feet = new Vector2(go.transform.position.x, root.transform.position.y + Surface(x));
            AddCheckpoint(go, ++checkpoints, feet);
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

            var back = CreateTilemap("Recess", -1);
            foreach (var p in recess)
            {
                string wall = !recess.Contains(p + Vector2Int.left) ? RecessBackLeft
                    : !recess.Contains(p + Vector2Int.right) ? RecessBackRight : RecessBack;
                back.SetTile((Vector3Int)p, ObstacleTile(wall));
            }

            CreateSky(ground, platforms, decor, back);
            Sky.sprites = tinted.ToArray();
        }

        // Day, dusk and night copies of each background layer (the unsuffixed PNG is dusk),
        // plus sun and moon; LaneSky shows the right mix for its hour.
        void CreateSky(params Tilemap[] tilemaps)
        {
            var go = new GameObject("Sky");
            go.transform.SetParent(root.transform, false);
            Sky = go.AddComponent<LaneSky>();
            Sky.layers = new[]
            {
                SkyLayer(go.transform, "Far", "bg_far_360h", -20, FarDepth),
                SkyLayer(go.transform, "Mid", "bg_mid_360h", -10, MidDepth),
            };
            Sky.tilemaps = tilemaps;
            Sky.sun = SkyBody(go.transform, "Sun");
            Sky.moon = SkyBody(go.transform, "Moon");
            Sky.hour = startHour;
        }

        LaneSky.Layer SkyLayer(Transform parent, string id, string file, int order, float depth) => new LaneSky.Layer
        {
            day = CreateBackground(parent, $"Background_{id}_Day", $"Assets/Art/Backgrounds/{file}_day.png", order, depth),
            dusk = CreateBackground(parent, $"Background_{id}_Dusk", $"Assets/Art/Backgrounds/{file}.png", order, depth),
            night = CreateBackground(parent, $"Background_{id}_Night", $"Assets/Art/Backgrounds/{file}_night.png", order, depth),
            sortingOrder = order,
        };

        // Between the far (-20) and mid (-10) layers, so buildings pass in front.
        static SpriteRenderer SkyBody(Transform parent, string id)
        {
            var go = new GameObject(id);
            go.transform.SetParent(parent, false);
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = AssetDatabase.LoadAssetAtPath<Sprite>($"Assets/Art/Sky/{id.ToLowerInvariant()}.png");
            sr.sortingOrder = -15;
            return sr;
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
                bool slotFloor = recess.Contains(p + Vector2Int.up);   // no ivy under the drawbridge
                if (row == 0) col = h < 12 && !slotFloor ? 2 + h % 3 : new[] { 1, 5, 6 }[h % 3];   // 2-4 have ivy
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

        SpriteRenderer CreateBackground(Transform parent, string id, string path, int order, float depth)
        {
            var go = new GameObject(id);
            go.transform.SetParent(parent, false);
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = AssetDatabase.LoadAssetAtPath<Sprite>(path);
            sr.drawMode = SpriteDrawMode.Tiled;
            var size = sr.sprite.bounds.size;
            float width = Length + 60;
            sr.size = new Vector2(width, size.y);
            sr.sortingOrder = order;
            // Top edge just above the view; the bottom hides behind the ground.
            float bottom = CameraY + CameraSize + 0.25f - size.y;
            // A layer at parallax depth d travels d times as far as the camera, so
            // it still covers the view at both ends of the level.
            go.transform.localPosition = new Vector3(width / 2f - 30f, bottom + size.y / 2f, 0f);
            backgrounds.Add((go.transform, depth));
            return sr;
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

    static Transform CreateCamera(string id, float laneY, Rect viewport, bool listener)
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
        return go.transform;
    }

    // Death and respawn for both players (lane order: Tick, Tock).
    static void CreateGameFlow(Transform[] players, CoopCamera rig)
    {
        var flow = new GameObject("GameFlow").AddComponent<GameFlow>();
        flow.players = players;
        flow.cameraRig = rig;
    }

    // Shared side-scrolling camera: both lane cameras follow the players together,
    // and ScreenLeash keeps each player inside the view with an elastic edge.
    static CoopCamera CreateCameraRig(int length, Transform[] cameras, Transform[] players)
    {
        var go = new GameObject("CameraRig");
        var rig = go.AddComponent<CoopCamera>();
        rig.cameras = cameras.Select(c => c.GetComponent<Camera>()).ToArray();
        rig.players = players.Where(p => p != null).ToArray();
        rig.levelLeft = -2f;
        rig.levelRight = length + 2f;
        go.AddComponent<ScreenLeash>();
        return rig;
    }

    // Screen-space overlay canvas at 1280x720 reference size, so one canvas unit is
    // one art pixel (each lane is 360 px tall); the rift is centred on the seam.
    static void CreateDivider(Transform tickCamera, Transform tockCamera)
    {
        var go = new GameObject("LaneDivider", typeof(Canvas), typeof(CanvasScaler));
        go.layer = LayerMask.NameToLayer("UI");
        var canvas = go.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 10;
        var scaler = go.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1280f, 720f);
        scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
        scaler.matchWidthOrHeight = 1f;

        var divider = go.AddComponent<LaneDivider>();
        divider.rift = DividerLayer(go.transform, "Rift");
        divider.topCamera = tickCamera;
        divider.bottomCamera = tockCamera;
    }

    // The rift texture is drawn at runtime by LaneDivider.
    static RawImage DividerLayer(Transform parent, string id)
    {
        var go = new GameObject(id, typeof(RectTransform), typeof(RawImage));
        go.transform.SetParent(parent, false);
        go.layer = parent.gameObject.layer;
        var rt = (RectTransform)go.transform;
        rt.anchorMin = new Vector2(0f, 0.5f);
        rt.anchorMax = new Vector2(1f, 0.5f);
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.sizeDelta = new Vector2(0f, LaneDivider.TextureHeight);
        rt.anchoredPosition = Vector2.zero;
        var image = go.GetComponent<RawImage>();
        image.raycastTarget = false;
        return image;
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

    static string ObstaclePng(string name) => $"Assets/Art/Obstacles/{name}_32px.png";

    // Tile asset next to its sprite, made on first use; no collider.
    static Tile ObstacleTile(string name)
    {
        string path = $"Assets/Art/Obstacles/{name}.asset";
        var t = AssetDatabase.LoadAssetAtPath<Tile>(path);
        if (t != null) return t;
        t = ScriptableObject.CreateInstance<Tile>();
        t.sprite = AssetDatabase.LoadAssetAtPath<Sprite>(ObstaclePng(name));
        t.colliderType = UnityEngine.Tilemaps.Tile.ColliderType.None;
        AssetDatabase.CreateAsset(t, path);
        return t;
    }

    // Single sprite, 32 PPU, point filter. The drawbridge leaf has a centre
    // pivot (the object sits at the leaf's centre; BridgeMovement.hingeAnchor
    // puts the hinge on the pin at pixel (16, 16)) and needs a full-rect mesh
    // to be drawn sliced.
    static void PrepareSprite(string path, bool fullRect, Vector2 pivot)
    {
        if (!(AssetImporter.GetAtPath(path) is TextureImporter ti)) return;
        var settings = new TextureImporterSettings();
        ti.ReadTextureSettings(settings);
        var mesh = fullRect ? SpriteMeshType.FullRect : SpriteMeshType.Tight;
        if (ti.textureType == TextureImporterType.Sprite && ti.spriteImportMode == SpriteImportMode.Single
            && ti.spritePixelsPerUnit == 32 && settings.spriteMeshType == mesh && ti.spritePivot == pivot) return;
        settings.textureType = TextureImporterType.Sprite;
        settings.spriteMode = (int)SpriteImportMode.Single;
        settings.spritePixelsPerUnit = 32;
        settings.filterMode = FilterMode.Point;
        settings.mipmapEnabled = false;
        settings.spriteMeshType = mesh;
        settings.spriteAlignment = (int)SpriteAlignment.Custom;
        settings.spritePivot = pivot;
        ti.SetTextureSettings(settings);   // replaces all of them, so set them on settings first
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
