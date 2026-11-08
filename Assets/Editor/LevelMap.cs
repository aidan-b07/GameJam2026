using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEditor.U2D.Sprites;
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
    // Geared hinge winch of the drawbridge from tools/make_winch.py: the still
    // parts in one sprite (stand), the hinge gear and crank turned by
    // DrawbridgeWinch, and the lamp states, with the pivots below.
    const string WinchDir = "Assets/Art/Obstacles/Winch/";
    static string WinchPng(string id) => $"{WinchDir}winch_{id}_32px.png";
    static readonly (string id, Vector2 pivot)[] WinchParts =
    {
        ("stand", Vector2.zero), ("hinge_gear", Vector2.one * 0.5f), ("crank", Vector2.one * 0.5f),
        ("lamp_off", Vector2.one * 0.5f), ("lamp_amber", Vector2.one * 0.5f), ("lamp_dim", Vector2.one * 0.5f),
        ("lamp_red", Vector2.one * 0.5f), ("lamp_green", Vector2.one * 0.5f), ("glow", Vector2.one * 0.5f),
    };
    // Plunger crusher art from tools/make_plunger.py: 3x2 head with lamp and dial
    // hand overlays (12 hand sprites, 00 = 12 o'clock), tiled rod, brass ring.
    const string PlungerDir = "Assets/Art/Obstacles/Plunger/";
    static string PlungerPng(string id) => $"{PlungerDir}plunger_{id}_32px.png";
    // Basalt block with a brass strike plate under each plunger head (3x2 tiles),
    // from tools/make_strike.py: strike_r<row>c<col>, row 0 on top. They replace
    // those ground tiles. strike_flash is the lit plate (96x9, top-left pivot).
    const string StrikeDir = "Assets/Art/Obstacles/Strike/";
    static IEnumerable<(int r, int c)> StrikeCells =>
        from r in Enumerable.Range(0, 2) from c in Enumerable.Range(0, 3) select (r, c);
    // Twin checkpoint lamp, Victorian design from
    // tools/make_checkpoint_victorian.py --no-clock --vivid (saved under the twin names):
    // a post (one frame) and a lantern overlay sheet per player (19 frames), all
    // 96x128 with a bottom-centre pivot: the 64x96 lamp plus room above and
    // beside it for the light to fade out. CheckpointLamp plays the overlays.
    const string CheckpointDir = "Assets/Art/Obstacles/Checkpoint/";
    static string CheckpointPng(string id) => $"{CheckpointDir}checkpoint_twin_{id}_64px.png";
    const int LampFrameWidth = 96;
    // Time gate (portcullis) and lane lift from tools/make_gate_lift.py.
    const string GateLiftDir = "Assets/Art/Obstacles/GateLift/";
    static string GateLiftPng(string id) => $"{GateLiftDir}{id}_32px.png";
    // Flush conveyor belt from tools/make_conveyor.py: left, mid and right
    // sheets of 8 frames of 32x32, bottom-left pivot (the frame is one cell).
    const string ConveyorDir = "Assets/Art/Obstacles/Conveyor/";
    static string ConveyorPng(string id) => $"{ConveyorDir}conveyor_flush_{id}_32px.png";
    static readonly string[] ConveyorParts = { "left", "mid", "right" };
    // Latching floor button from tools/make_button.py: 64x32 (or narrow
    // 32x32) frames, 14 of them, bottom-left pivot; body and untinted lamp.
    const string ButtonDir = "Assets/Art/Obstacles/Button/";
    static string ButtonPng(string id, bool narrow) => $"{ButtonDir}{id}_{(narrow ? 32 : 64)}px.png";
    // Decor sprites from tools/make_decor.py, bottom-centre pivot.
    const string DecorDir = "Assets/Art/Decor/";
    static string DecorPng(string id) => $"{DecorDir}decor_{id}_32px.png";
    static IEnumerable<string> PlungerHands => Enumerable.Range(0, 12).Select(h => $"hand_{h:00}");
    // Trap door from tools/make_trapdoor.py: 192x128 frames (6x4 tiles, the
    // pit is tile columns 1-4), bottom-left pivot; body and untinted lamp overlay.
    const string TrapDoorDir = "Assets/Art/Obstacles/TrapDoor/";
    static string TrapDoorPng(string id) => $"{TrapDoorDir}{id}_192px.png";
    const int TrapDoorFrameWidth = 192;
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
    // Clock faces in each background picture (centre in pixels from its
    // bottom-left pixel, radius) and the colour of their painted hands in the
    // day, dusk and night pictures; printed by tools/make_clockfaces.py.
    static readonly Dictionary<string, TowerClocks.Face[]> ClockFaces = new Dictionary<string, TowerClocks.Face[]>
    {
        ["bg_far_360h"] = new[] { Face(105f, 132f, 6.5f), Face(412.5f, 131.5f, 18f), Face(560f, 132f, 6.5f) },
        ["bg_mid_360h"] = new[] { Face(449f, 179.5f, 16.5f) },
    };
    static readonly Dictionary<string, (Color day, Color dusk, Color night)> ClockHands = new Dictionary<string, (Color, Color, Color)>
    {
        ["bg_far_360h"] = (Rgb(134, 169, 208), Rgb(158, 96, 158), Rgb(78, 66, 122)),
        ["bg_mid_360h"] = (Rgb(126, 68, 72), Rgb(115, 54, 90), Rgb(110, 75, 75)),
    };
    // Chimney mouths in each background picture (pixels from its bottom-left
    // pixel), with the lane seconds between puffs and the puff size.
    static readonly Dictionary<string, ChimneySmoke.Chimney[]> Chimneys = new Dictionary<string, ChimneySmoke.Chimney[]>
    {
        ["bg_mid_360h"] = new[] { Chimney(31f, 237f, 0.32f, 1f), Chimney(253.5f, 222f, 0.4f, 0.8f), Chimney(67.5f, 142f, 0.5f, 0.8f) },
    };
    // Life on the mid picture (MidLayerLife), in pixels from its top-left:
    // pigeons' feet on the arcade railing (its top is row 179), the ivy clusters
    // leaves fall from, the clock tower spire tip for the pennant, and the lit
    // windows of the night picture (rects found by colour, the clock face left
    // out) that flicker like candles.
    class LayerLife { public Vector2[] pigeons; public RectInt[] ivy, windows; public Vector2 pennant; }
    static readonly Dictionary<string, LayerLife> MidLife = new Dictionary<string, LayerLife>
    {
        ["bg_mid_360h"] = new LayerLife
        {
            pigeons = new[] { new Vector2(112f, 179f), new Vector2(124f, 179f), new Vector2(340f, 179f), new Vector2(520f, 179f) },
            ivy = new[] { Box(41, 159, 58, 180), Box(222, 204, 265, 227), Box(260, 206, 299, 226), Box(381, 223, 429, 231), Box(430, 209, 441, 240), Box(591, 215, 615, 229), Box(441, 242, 494, 250), Box(507, 242, 575, 260), Box(10, 254, 54, 278), Box(606, 259, 639, 276), Box(340, 282, 389, 300), Box(153, 285, 174, 300), Box(492, 284, 525, 296), Box(37, 227, 45, 252) },
            pennant = new Vector2(448f, 112f),
            windows = new[]
            {
                W(443, 147, 4, 6), W(452, 147, 4, 6), W(263, 175, 3, 10), W(267, 175, 4, 10), W(278, 175, 3, 10), W(282, 175, 3, 10),
                W(3, 183, 4, 11), W(8, 183, 4, 11), W(23, 183, 8, 11), W(627, 183, 8, 11), W(400, 192, 3, 8), W(404, 193, 3, 7),
                W(400, 202, 3, 7), W(404, 202, 3, 7), W(445, 210, 5, 5), W(451, 210, 4, 5), W(277, 211, 3, 11), W(250, 212, 3, 5),
                W(281, 212, 3, 10), W(240, 217, 3, 5), W(445, 217, 4, 8), W(451, 217, 5, 8), W(445, 227, 5, 9), W(451, 227, 5, 9),
                W(14, 231, 4, 6), W(19, 232, 5, 5), W(630, 232, 6, 5), W(14, 239, 4, 13), W(19, 239, 5, 13), W(628, 239, 8, 13),
                W(80, 244, 4, 6), W(85, 244, 3, 6), W(111, 244, 4, 6), W(116, 244, 4, 6), W(244, 246, 4, 5), W(391, 249, 6, 10),
                W(413, 249, 7, 10), W(267, 250, 4, 5), W(273, 250, 4, 5), W(295, 250, 5, 5), W(301, 250, 4, 5), W(209, 256, 3, 5),
                W(267, 257, 4, 9), W(273, 257, 4, 9), W(295, 257, 5, 9), W(301, 257, 4, 9), W(360, 259, 3, 5), W(205, 263, 7, 12),
                W(331, 265, 7, 10), W(356, 265, 3, 10), W(360, 265, 3, 10), W(77, 267, 4, 5), W(108, 267, 3, 5), W(134, 267, 3, 5),
                W(138, 267, 4, 5), W(295, 267, 5, 7), W(469, 267, 4, 5), W(474, 267, 3, 5), W(301, 268, 5, 7), W(31, 271, 5, 6),
                W(77, 274, 4, 9), W(82, 274, 4, 9), W(107, 274, 4, 9), W(112, 274, 4, 9), W(134, 274, 3, 9), W(138, 274, 4, 9),
                W(448, 274, 3, 9), W(452, 274, 4, 9), W(469, 274, 4, 9), W(474, 274, 3, 9), W(165, 275, 4, 5), W(175, 275, 4, 5),
                W(397, 279, 4, 5), W(408, 279, 4, 5), W(361, 283, 6, 5),
            },
        },
    };
    static RectInt W(int x, int y, int w, int h) => new RectInt(x, y, w, h);
    static RectInt Box(int x0, int y0, int x1, int y1) => new RectInt(x0, y0, x1 - x0 + 1, y1 - y0 + 1);
    static ChimneySmoke.Chimney Chimney(float x, float y, float interval, float size) =>
        new ChimneySmoke.Chimney { mouth = new Vector2(x, y), interval = interval, size = size };
    static TowerClocks.Face Face(float x, float y, float r) => new TowerClocks.Face { centre = new Vector2(x, y), radius = r };
    static Color Rgb(int r, int g, int b) => new Color32((byte)r, (byte)g, (byte)b, 255);

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
        foreach (var (id, pivot) in WinchParts) PrepareSprite(WinchPng(id), false, pivot);
        PrepareSprite(ObstaclePng(RecessBackLeft), false, RecessLeftPivot);
        PrepareSprite(ObstaclePng(RecessBackRight), false, RecessPivot);
        foreach (var id in new[] { "head", "lamp_amber", "lamp_red" }.Concat(PlungerHands))
            PrepareSprite(PlungerPng(id), false, new Vector2(0.5f, 0.5f));
        PrepareSprite(PlungerPng("rod"), true, new Vector2(0.5f, 0f));
        PrepareSprite(PlungerPng("gland"), false, new Vector2(0.5f, 1f));
        foreach (var (r, c) in StrikeCells)
            PrepareSprite($"{StrikeDir}strike_r{r}c{c}_32px.png", false, new Vector2(0.5f, 0.5f));
        PrepareSprite($"{StrikeDir}strike_flash_32px.png", false, new Vector2(0f, 1f));
        PrepareSprite(CheckpointPng("post"), false, new Vector2(0.5f, 0f));
        PrepareStrip(CheckpointPng("tick"), LampFrameWidth, new Vector2(0.5f, 0f));
        PrepareSprite(GateLiftPng("gate_portcullis"), false, new Vector2(0.5f, 0.5f));
        PrepareSprite(GateLiftPng("gate_lamp"), false, new Vector2(0.5f, 0.5f));
        PrepareSprite(GateLiftPng("lift_platform"), false, new Vector2(0.5f, 1f));
        PrepareSprite(GateLiftPng("lift_chain"), true, new Vector2(0.5f, 0f));
        PrepareStrip(CheckpointPng("tock"), LampFrameWidth, new Vector2(0.5f, 0f));
        PrepareStrip(TrapDoorPng("trapdoor"), TrapDoorFrameWidth, Vector2.zero);
        PrepareStrip(TrapDoorPng("trapdoor_lamp"), TrapDoorFrameWidth, Vector2.zero);
        foreach (var id in ConveyorParts) PrepareStrip(ConveyorPng(id), 32, Vector2.zero);
        foreach (var narrow in new[] { false, true })
            foreach (var id in new[] { "button", "button_lamp" })
                PrepareStrip(ButtonPng(id, narrow), narrow ? 32 : 64, Vector2.zero);
        foreach (var path in AssetDatabase.FindAssets("t:Texture2D", new[] { DecorDir.TrimEnd('/') })
                     .Select(AssetDatabase.GUIDToAssetPath))
            PrepareSprite(path, false, new Vector2(0.5f, 0f));
        foreach (var path in AssetDatabase.FindAssets("drawbridge_ t:Texture2D", new[] { "Assets/Art/Obstacles" })
                     .Select(AssetDatabase.GUIDToAssetPath).Where(p => p.EndsWith("_32px.png")))
            PrepareSprite(path, true, new Vector2(0.5f, 0.5f));
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
        var manager = new GameObject("GameManager").AddComponent<GameManager>();
        manager.startHours = new[] { tickHour, tockHour };

        EditorSceneManager.SaveScene(scene, scenePath);
        AddToBuildSettings(scenePath);
        Debug.Log($"[LevelMap] Saved {scenePath}");
    }

    // Gate opens once every one of these buttons is pressed (any lane).
    public static void WireGate(ButtonGate gate, params FloorButton[] buttons) => gate.buttons = buttons;

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
        readonly Dictionary<Vector2Int, (int r, int c)> strike = new Dictionary<Vector2Int, (int r, int c)>();
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
            if (Sky != null && Sky.clouds != null) Sky.clouds.cameraTransform = cam;
            if (Sky != null && Sky.life != null) Sky.life.cameraTransform = cam;
            if (Sky != null && Sky.layers != null)
                foreach (var layer in Sky.layers)
                    if (layer.life != null) layer.life.cameraTransform = cam;
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
            // A leaf drawn for this length (make_drawbridge.py --length), else the 7-tile one stretched.
            var art = AssetDatabase.LoadAssetAtPath<Sprite>($"Assets/Art/Obstacles/drawbridge_{length}_32px.png")
                ?? AssetDatabase.LoadAssetAtPath<Sprite>(DrawbridgeSprite);
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

            Winch(go.GetComponent<BridgeMovement>(), new Vector2(x0 + 0.5f, -0.5f));
            return go;
        }

        // Geared hinge for a drawbridge whose hinge is at `hinge` (lane
        // coordinates): a big gear on the hinge driving a crank wheel on the
        // pit's lip, the hinge bracket and a lamp post. Offsets are in tiles from
        // the hinge, as printed by tools/make_winch.py. The leaf is order 5; the
        // lamp and the sparks sort below it so the leaf passes in front.
        void Winch(BridgeMovement bridge, Vector2 hinge)
        {
            var root = new GameObject($"{bridge.name}_Winch");
            root.transform.SetParent(markers, false);
            root.transform.localPosition = hinge;

            SpriteRenderer Part(string partName, string id, int order, Vector2 at, bool tint = true)
            {
                var part = new GameObject(partName);
                part.transform.SetParent(root.transform, false);
                part.transform.localPosition = at;
                var sr = part.AddComponent<SpriteRenderer>();
                sr.sprite = AssetDatabase.LoadAssetAtPath<Sprite>(WinchPng(id));
                sr.sortingOrder = order;
                if (tint) tinted.Add(sr);
                return sr;
            }

            var stand = Part("Stand", "stand", 1, new Vector2(-1.5f, -1f));
            var gear = Part("HingeGear", "hinge_gear", 2, Vector2.zero);
            var crank = Part("Crank", "crank", 3, new Vector2(-0.5f, 0.84375f));
            var glow = Part("LampGlow", "glow", 3, new Vector2(-1.234375f, 1.484375f), false);
            glow.enabled = false;
            var lamp = Part("Lamp", "lamp_off", 4, new Vector2(-1.234375f, 1.484375f), false);

            var winch = new SerializedObject(root.AddComponent<DrawbridgeWinch>());
            void Set(string field, UnityEngine.Object value) => winch.FindProperty(field).objectReferenceValue = value;
            Set("bridge", bridge);
            Set("hingeGear", gear.transform);
            Set("crank", crank.transform);
            Set("lamp", lamp);
            Set("glow", glow);
            foreach (var state in new[] { "Off", "Amber", "Dim", "Red", "Green" })
                Set("lamp" + state, AssetDatabase.LoadAssetAtPath<Sprite>(WinchPng("lamp_" + state.ToLowerInvariant())));
            Set("moteSprite", AssetDatabase.LoadAssetAtPath<Sprite>(WhiteSprite));
            Set("tintSource", stand);
            winch.ApplyModifiedPropertiesWithoutUndo();
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
        // lampDepth sinks the lamp below the surface, e.g. 1 to stand it on the
        // floor of the drawbridge's slot against its back wall (the leaf covers
        // it when it lands); players still come back on the surface. lampX moves
        // the lamp sideways (tiles from the column's left edge) and lampBehind
        // draws it behind obstacles and the ground, e.g. behind a trap door's lamp.
        public void Checkpoint(int x, int lampDepth = 0, float? lampX = null, bool lampBehind = false)
        {
            var go = Marker("Checkpoint", x, Surface(x), 0.5f, 0.5f, new Color(1f, 0.4f, 0.7f));
            go.GetComponent<SpriteRenderer>().enabled = false;   // the lamp shows where it is
            var trigger = go.AddComponent<BoxCollider2D>();
            trigger.isTrigger = true;
            trigger.size = new Vector2(1f, 24f);        // local units: the marker is 0.5 x 0.5
            trigger.offset = new Vector2(0f, 11.5f);
            var feet = new Vector2(go.transform.position.x, root.transform.position.y + Surface(x));
            AddCheckpoint(go, ++checkpoints, feet);
            Lamp(go, lampX.HasValue ? x + lampX.Value : go.transform.localPosition.x, Surface(x) - lampDepth, lampBehind ? -3 : 2);
        }

        // Twin lamp on a checkpoint: post standing on the ground (sunk by the
        // ground tiles' 3 transparent top rows), a lantern overlay per player.
        // The post is tinted with the lane's light; the lit lanterns are not.
        void Lamp(GameObject checkpoint, float x, int y, int order)
        {
            var lamp = new GameObject($"{checkpoint.name}_Lamp");
            lamp.transform.SetParent(markers, false);
            lamp.transform.localPosition = new Vector3(x, y - 3f / 32f, 0f);
            SpriteRenderer Part(string partName, Sprite sprite, int order)
            {
                var part = new GameObject(partName);
                part.transform.SetParent(lamp.transform, false);
                var sr = part.AddComponent<SpriteRenderer>();
                sr.sprite = sprite;
                sr.sortingOrder = order;
                return sr;
            }
            tinted.Add(Part("Post", AssetDatabase.LoadAssetAtPath<Sprite>(CheckpointPng("post")), order));
            var tickFrames = StripFrames(CheckpointPng("tick"));
            var tockFrames = StripFrames(CheckpointPng("tock"));
            var c = lamp.AddComponent<CheckpointLamp>();
            c.checkpoint = checkpoint.GetComponent<Checkpoint>();
            c.heads = new[] { Part("Tick", tickFrames[0], order + 1), Part("Tock", tockFrames[0], order + 1) };
            c.tickFrames = tickFrames;
            c.tockFrames = tockFrames;
        }

        public void RestPad(int x) => Marker("RestPad", x, Surface(x), 3, 0.25f, Rest);

        // Plunger crusher hanging from a stone housing (rows 8-9) over x..x+2, drawn
        // raised: a 3x2 head at y 5..7 on a rod, slamming down to the ground.
        // PlungerMotion runs it on lane time; offset (lane seconds) shifts its cycle.
        public GameObject Crusher(int x, float offset = 0f)
        {
            Fill(x - 1, x + 3, 10, 8);
            Prop(x - 2, 8, 1, 1);
            Prop(x + 4, 8, 1, 1);
            Busy(x - 3, x + 5);
            // Strike block where the head lands, so players can see the danger zone.
            foreach (var (r, c) in StrikeCells) strike[new Vector2Int(x + c, Surface(x + c) - 1 - r)] = (r, c);

            counts["Crusher"] = counts.TryGetValue("Crusher", out var n) ? n + 1 : 1;
            var go = new GameObject($"Crusher_{counts["Crusher"]}");
            go.transform.SetParent(markers, false);
            go.transform.localPosition = new Vector3(x + 1.5f, 6f, 0f);
            int layer = LayerMask.NameToLayer(GroundLayer);   // players can stand on the head
            if (layer >= 0) go.layer = layer;
            var rb = go.AddComponent<Rigidbody2D>();
            rb.bodyType = RigidbodyType2D.Kinematic;
            rb.gravityScale = 0f;
            rb.interpolation = RigidbodyInterpolation2D.Interpolate;
            go.AddComponent<BoxCollider2D>().size = new Vector2(3f, 2f);

            SpriteRenderer Part(string partName, Transform parent, string id, int order, Vector3 at)
            {
                var part = new GameObject(partName);
                part.transform.SetParent(parent, false);
                part.transform.localPosition = at;
                var sr = part.AddComponent<SpriteRenderer>();
                sr.sprite = AssetDatabase.LoadAssetAtPath<Sprite>(PlungerPng(id));
                sr.sortingOrder = order;
                tinted.Add(sr);
                return sr;
            }
            var head = Part("Visual", go.transform, "head", 5, Vector3.zero);
            var lamp = Part("Lamp", head.transform, "lamp_amber", 6, Vector3.zero);
            lamp.enabled = false;
            var hand = Part("Hand", head.transform, "hand_00", 6, Vector3.zero);
            var rod = Part("Rod", head.transform, "rod", 4, new Vector3(0f, 1f, 0f));
            rod.drawMode = SpriteDrawMode.Tiled;
            rod.size = new Vector2(14f / 32f, 1f);
            Part("Gland", markers, "gland", 6, new Vector3(x + 1.5f, 8f, 0f)).name = $"Crusher_{counts["Crusher"]}_Gland";
            // Lit plate over the strike block's pixel rows 3-11 (below the transparent top rows).
            var flash = Part($"Crusher_{counts["Crusher"]}_StrikeFlash", markers, "head", 1,
                new Vector3(x, Surface(x) - 3f / 32f, 0f));
            flash.sprite = AssetDatabase.LoadAssetAtPath<Sprite>($"{StrikeDir}strike_flash_32px.png");
            flash.enabled = false;

            var motion = new SerializedObject(go.AddComponent<PlungerMotion>());
            // Down to the ground, plus the 3 transparent top rows of the ground
            // tiles (players' feet sink by as much), so the teeth meet the stone.
            motion.FindProperty("travelDistance").floatValue = 5f - Surface(x + 1) + 3f / 32f;
            motion.FindProperty("cycleOffset").floatValue = offset;
            motion.FindProperty("visual").objectReferenceValue = head.transform;
            motion.FindProperty("lamp").objectReferenceValue = lamp;
            motion.FindProperty("amberLamp").objectReferenceValue = AssetDatabase.LoadAssetAtPath<Sprite>(PlungerPng("lamp_amber"));
            motion.FindProperty("redLamp").objectReferenceValue = AssetDatabase.LoadAssetAtPath<Sprite>(PlungerPng("lamp_red"));
            motion.FindProperty("hand").objectReferenceValue = hand;
            var hands = motion.FindProperty("handSprites");
            var handIds = PlungerHands.ToArray();
            hands.arraySize = handIds.Length;
            for (int i = 0; i < handIds.Length; i++)
                hands.GetArrayElementAtIndex(i).objectReferenceValue = AssetDatabase.LoadAssetAtPath<Sprite>(PlungerPng(handIds[i]));
            motion.FindProperty("rod").objectReferenceValue = rod;
            // Up to just inside the brass ring under the housing (ring spans y 7.75..8).
            motion.FindProperty("rodTop").floatValue = root.transform.position.y + 8f - 2f / 32f;
            motion.FindProperty("strikeFlash").objectReferenceValue = flash;
            motion.FindProperty("dustSprite").objectReferenceValue = AssetDatabase.LoadAssetAtPath<Sprite>(WhiteSprite);
            motion.FindProperty("lane").intValue = LaneIndex;
            motion.ApplyModifiedPropertiesWithoutUndo();
            return go;
        }

        // Portcullis across the path at x (Level 2), hanging from a stone housing
        // that starts 4 tiles up: it hides the raised gate and stops players
        // jumping over it. TimeGate winches it up while the lane runs fast (the
        // partner is moving) and lets it sink at normal speed.
        public GameObject TimeGate(int x)
        {
            int y = Surface(x);
            Fill(x - 1, x + 1, y + 10, y + 4);
            Busy(x - 2, x + 2);

            counts["TimeGate"] = counts.TryGetValue("TimeGate", out var n) ? n + 1 : 1;
            var go = new GameObject($"TimeGate_{counts["TimeGate"]}");
            go.transform.SetParent(markers, false);
            go.transform.localPosition = new Vector3(x + 0.5f, y + 2f, 0f);
            int layer = LayerMask.NameToLayer(GroundLayer);
            if (layer >= 0) go.layer = layer;
            var rb = go.AddComponent<Rigidbody2D>();
            rb.bodyType = RigidbodyType2D.Kinematic;
            rb.gravityScale = 0f;
            rb.interpolation = RigidbodyInterpolation2D.Interpolate;
            go.AddComponent<BoxCollider2D>().size = new Vector2(0.875f, 4f);
            // Behind the ground tiles, so the raised gate disappears into the housing.
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = AssetDatabase.LoadAssetAtPath<Sprite>(GateLiftPng("gate_portcullis"));
            sr.sortingOrder = -1;
            tinted.Add(sr);

            // Lamp on the housing's face, left of the gate (the side players come from).
            var lamp = new GameObject($"{go.name}_Lamp").AddComponent<SpriteRenderer>();
            lamp.transform.SetParent(markers, false);
            lamp.transform.localPosition = new Vector3(x - 0.5f, y + 4.5f, 0f);
            lamp.sprite = AssetDatabase.LoadAssetAtPath<Sprite>(GateLiftPng("gate_lamp"));
            lamp.sortingOrder = 3;

            var gate = new SerializedObject(go.AddComponent<TimeGate>());
            gate.FindProperty("lane").intValue = LaneIndex;
            gate.FindProperty("lamp").objectReferenceValue = lamp;
            gate.ApplyModifiedPropertiesWithoutUndo();
            return go;
        }

        // Sliding bridge over the pit between x0 and x1 (Level 2; ground ends
        // at x0 and starts again at x1, like Drawbridge). The deck hides in the
        // near wall under the ground surface and TimeGate runs it out across
        // the pit while the lane is fast, pulling it back at normal speed.
        public GameObject TimeBridge(int x0, int x1)
        {
            int length = x1 - x0;
            float top = Surface(x0 - 1) - 1f / 32f;   // level with the ground collider's top
            Busy(x0 - 2, x1);

            counts["TimeBridge"] = counts.TryGetValue("TimeBridge", out var n) ? n + 1 : 1;
            var go = new GameObject($"TimeBridge_{counts["TimeBridge"]}");
            go.transform.SetParent(markers, false);
            go.transform.localPosition = new Vector3(x0 - length / 2f, top, 0f);
            int layer = LayerMask.NameToLayer(GroundLayer);
            if (layer >= 0) go.layer = layer;
            var rb = go.AddComponent<Rigidbody2D>();
            rb.bodyType = RigidbodyType2D.Kinematic;
            rb.gravityScale = 0f;
            rb.interpolation = RigidbodyInterpolation2D.Interpolate;
            var box = go.AddComponent<BoxCollider2D>();
            box.size = new Vector2(length, 0.5f);
            box.offset = new Vector2(0f, -0.25f);
            // Behind the ground tiles, so the pulled-in deck disappears into the wall.
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = AssetDatabase.LoadAssetAtPath<Sprite>(GateLiftPng("lift_platform"));
            sr.drawMode = SpriteDrawMode.Tiled;
            sr.size = new Vector2(length, 0.5f);
            sr.sortingOrder = -1;
            tinted.Add(sr);

            // Lamp on a short post at the pit's edge.
            var lamp = new GameObject($"{go.name}_Lamp").AddComponent<SpriteRenderer>();
            lamp.transform.SetParent(markers, false);
            lamp.transform.localPosition = new Vector3(x0 - 1.5f, top + 1.2f, 0f);
            lamp.sprite = AssetDatabase.LoadAssetAtPath<Sprite>(GateLiftPng("gate_lamp"));
            lamp.sortingOrder = 3;

            var gate = new SerializedObject(go.AddComponent<TimeGate>());
            gate.FindProperty("lane").intValue = LaneIndex;
            gate.FindProperty("travel").floatValue = length;
            gate.FindProperty("direction").vector2Value = Vector2.right;
            gate.FindProperty("lamp").objectReferenceValue = lamp;
            gate.ApplyModifiedPropertiesWithoutUndo();
            return go;
        }

        // Flush conveyor belt over the ground's top row from x0 to x1 (at least
        // 2 tiles), carrying players at `speed` units per lane second (negative
        // runs left). The ground tiles stay under it for the collider; the belt
        // sprites cover them. Conveyor runs it on lane time.
        public GameObject Conveyor(int x0, int x1, float speed)
        {
            int y = Surface(x0);
            Busy(x0, x1);
            counts["Conveyor"] = counts.TryGetValue("Conveyor", out var n) ? n + 1 : 1;
            var go = new GameObject($"Conveyor_{counts["Conveyor"]}");
            go.transform.SetParent(markers, false);
            go.transform.localPosition = new Vector3(x0, y - 1, 0f);
            var frames = ConveyorParts.Select(id => StripFrames(ConveyorPng(id))).ToArray();
            var cells = new SpriteRenderer[x1 - x0 + 1];
            for (int i = 0; i < cells.Length; i++)
            {
                var cell = new GameObject($"Belt_{i}");
                cell.transform.SetParent(go.transform, false);
                cell.transform.localPosition = new Vector3(i, 0f, 0f);
                var sr = cell.AddComponent<SpriteRenderer>();
                sr.sprite = frames[i == 0 ? 0 : i == cells.Length - 1 ? 2 : 1][0];
                sr.sortingOrder = 1;   // over the ground tiles
                tinted.Add(sr);
                cells[i] = sr;
            }
            var belt = go.AddComponent<global::Conveyor>();
            belt.lane = LaneIndex;
            belt.speed = speed;
            belt.left = x0;
            belt.width = x1 - x0 + 1;
            // The ground collider's top is 1 px below the cell top.
            belt.top = root.transform.position.y + y - 1f / 32f;
            belt.cells = cells;
            belt.leftFrames = frames[0];
            belt.midFrames = frames[1];
            belt.rightFrames = frames[2];
            return go;
        }

        // Lift over x..x+2 (Level 2): a one-way deck half a tile above the
        // ground that rides up to stand level with `top` and back, on two chains
        // from a stone beam above. LaneLift runs it on lane time; offset (lane
        // seconds) shifts its cycle. Put the ledge it serves next to it.
        public GameObject Lift(int x, int top, float offset = 0f)
        {
            int y = Surface(x);
            Fill(x - 1, x + 3, top + 6, top + 5);
            Busy(x - 1, x + 3);

            counts["Lift"] = counts.TryGetValue("Lift", out var n) ? n + 1 : 1;
            var go = new GameObject($"Lift_{counts["Lift"]}");
            go.transform.SetParent(markers, false);
            go.transform.localPosition = new Vector3(x + 1.5f, y + 0.5f, 0f);
            int layer = LayerMask.NameToLayer(GroundLayer);
            if (layer >= 0) go.layer = layer;
            var rb = go.AddComponent<Rigidbody2D>();
            rb.bodyType = RigidbodyType2D.Kinematic;
            rb.gravityScale = 0f;
            rb.interpolation = RigidbodyInterpolation2D.Interpolate;
            var box = go.AddComponent<BoxCollider2D>();
            box.size = new Vector2(3f, 0.5f);
            box.offset = new Vector2(0f, -0.25f);
            box.usedByEffector = true;
            go.AddComponent<PlatformEffector2D>().useOneWay = true;
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = AssetDatabase.LoadAssetAtPath<Sprite>(GateLiftPng("lift_platform"));
            sr.sortingOrder = 4;
            tinted.Add(sr);

            var chains = new SpriteRenderer[2];
            for (int i = 0; i < 2; i++)
            {
                var chain = new GameObject(i == 0 ? "ChainLeft" : "ChainRight").AddComponent<SpriteRenderer>();
                chain.transform.SetParent(go.transform, false);
                chain.transform.localPosition = new Vector3(i == 0 ? -1.125f : 1.125f, 0f, 0f);
                chain.sprite = AssetDatabase.LoadAssetAtPath<Sprite>(GateLiftPng("lift_chain"));
                chain.drawMode = SpriteDrawMode.Tiled;
                chain.size = new Vector2(6f / 32f, top + 5f - y - 0.5f);
                chain.sortingOrder = 3;
                tinted.Add(chain);
                chains[i] = chain;
            }

            var lift = new SerializedObject(go.AddComponent<LaneLift>());
            lift.FindProperty("lane").intValue = LaneIndex;
            lift.FindProperty("travel").floatValue = top - y - 0.5f;
            lift.FindProperty("cycleOffset").floatValue = offset;
            var list = lift.FindProperty("chains");
            list.arraySize = 2;
            for (int i = 0; i < 2; i++) list.GetArrayElementAtIndex(i).objectReferenceValue = chains[i];
            lift.FindProperty("chainTop").floatValue = root.transform.position.y + top + 5f;
            lift.ApplyModifiedPropertiesWithoutUndo();
            return go;
        }

        // Trap door over the 4-tile pit x0..x0+3 (leave those columns out of the
        // floor): two short drawbridge leaves hinged on the lips, level and
        // flush with the ground when closed, dropping flat against the pit
        // walls and winched back up. TrapDoor plays the sheet on lane time and
        // turns the leaf colliders to the angle each frame shows.
        public GameObject TrapDoor(int x0, float offset = 0f)
        {
            int y = Surface(x0 - 1);
            Busy(x0 - 1, x0 + 4);

            counts["TrapDoor"] = counts.TryGetValue("TrapDoor", out var n) ? n + 1 : 1;
            var go = new GameObject($"TrapDoor_{counts["TrapDoor"]}");
            go.transform.SetParent(markers, false);
            // Frame bottom-left: the lip tile's column, 3 tiles under the surface
            // tile row (frame row 32 is the top of that row, at the surface).
            go.transform.localPosition = new Vector3(x0 - 1, y - 3f, 0f);
            var bodyFrames = StripFrames(TrapDoorPng("trapdoor"));
            var lampFrames = StripFrames(TrapDoorPng("trapdoor_lamp"));
            var body = go.AddComponent<SpriteRenderer>();
            body.sprite = bodyFrames.FirstOrDefault();
            body.sortingOrder = 2;
            tinted.Add(body);
            var lamp = new GameObject("Lamp").AddComponent<SpriteRenderer>();
            lamp.transform.SetParent(go.transform, false);
            lamp.sprite = lampFrames.FirstOrDefault();
            lamp.sortingOrder = 3;

            // Leaves: kinematic bodies on the hinge pins (frame px (37, 40) and
            // (155, 40)), 64 x 10 px boxes reaching 59 px past the pin.
            int layer = LayerMask.NameToLayer(GroundLayer);
            Rigidbody2D Leaf(string leafName, float pinX, float dir)
            {
                var leaf = new GameObject(leafName);
                leaf.transform.SetParent(go.transform, false);
                leaf.transform.localPosition = new Vector3(pinX / 32f, (128f - 40f) / 32f, 0f);
                if (layer >= 0) leaf.layer = layer;
                var rb = leaf.AddComponent<Rigidbody2D>();
                rb.bodyType = RigidbodyType2D.Kinematic;
                rb.gravityScale = 0f;
                rb.interpolation = RigidbodyInterpolation2D.Interpolate;
                var box = leaf.AddComponent<BoxCollider2D>();
                box.size = new Vector2(2f, 10f / 32f);
                box.offset = new Vector2(dir * 27f / 32f, 0f);
                return rb;
            }

            var door = new SerializedObject(go.AddComponent<TrapDoor>());
            door.FindProperty("lane").intValue = LaneIndex;
            door.FindProperty("cycleOffset").floatValue = offset;
            door.FindProperty("body").objectReferenceValue = body;
            door.FindProperty("lamp").objectReferenceValue = lamp;
            void Frames(string prop, Sprite[] frames)
            {
                var list = door.FindProperty(prop);
                list.arraySize = frames.Length;
                for (int i = 0; i < frames.Length; i++) list.GetArrayElementAtIndex(i).objectReferenceValue = frames[i];
            }
            Frames("bodyFrames", bodyFrames);
            Frames("lampFrames", lampFrames);
            door.FindProperty("leftLeaf").objectReferenceValue = Leaf("LeafLeft", 37f, 1f);
            door.FindProperty("rightLeaf").objectReferenceValue = Leaf("LeafRight", 155f, -1f);
            door.ApplyModifiedPropertiesWithoutUndo();
            return go;
        }

        // Latching floor button over x (narrow, one tile) or x..x+1, in the
        // ground's top row like the flush conveyor: the ground tiles stay under
        // it for the collider. Over a crusher's strike block it sits on the
        // brass plate. Wire it to a ButtonGate with WireGate.
        public FloorButton Button(int x, bool narrow = false)
        {
            int y = Surface(x);
            int w = narrow ? 1 : 2;
            Busy(x - 1, x + w);
            counts["Button"] = counts.TryGetValue("Button", out var n) ? n + 1 : 1;
            var go = new GameObject($"Button_{counts["Button"]}");
            go.transform.SetParent(markers, false);
            go.transform.localPosition = new Vector3(x, y - 1, 0f);
            var bodyFrames = StripFrames(ButtonPng("button", narrow));
            var lampFrames = StripFrames(ButtonPng("button_lamp", narrow));
            var body = go.AddComponent<SpriteRenderer>();
            body.sprite = bodyFrames.FirstOrDefault();
            body.sortingOrder = 2;   // over the ground and a strike flash
            tinted.Add(body);
            var lamp = new GameObject("Lamp").AddComponent<SpriteRenderer>();
            lamp.transform.SetParent(go.transform, false);
            lamp.sprite = lampFrames.FirstOrDefault();
            lamp.sortingOrder = 3;

            var b = go.AddComponent<FloorButton>();
            // The plate (make_button.py: x 12-51 of 64, x 6-25 of 32).
            b.left = x + (narrow ? 6f : 12f) / 32f;
            b.width = (narrow ? 20f : 40f) / 32f;
            b.top = root.transform.position.y + y - 1f / 32f;
            b.body = body;
            b.lamp = lamp;
            b.bodyFrames = bodyFrames;
            b.lampFrames = lampFrames;
            return b;
        }

        // Portcullis at x that FloorButtons open (main level): TimeGate's
        // housing and art, ButtonGate's logic. Wire buttons with WireGate.
        public ButtonGate ButtonGate(int x)
        {
            int y = Surface(x);
            Fill(x - 1, x + 1, y + 10, y + 4);
            Busy(x - 2, x + 2);

            counts["ButtonGate"] = counts.TryGetValue("ButtonGate", out var n) ? n + 1 : 1;
            var go = new GameObject($"ButtonGate_{counts["ButtonGate"]}");
            go.transform.SetParent(markers, false);
            go.transform.localPosition = new Vector3(x + 0.5f, y + 2f, 0f);
            int layer = LayerMask.NameToLayer(GroundLayer);
            if (layer >= 0) go.layer = layer;
            var rb = go.AddComponent<Rigidbody2D>();
            rb.bodyType = RigidbodyType2D.Kinematic;
            rb.gravityScale = 0f;
            rb.interpolation = RigidbodyInterpolation2D.Interpolate;
            go.AddComponent<BoxCollider2D>().size = new Vector2(0.875f, 4f);
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = AssetDatabase.LoadAssetAtPath<Sprite>(GateLiftPng("gate_portcullis"));
            sr.sortingOrder = -1;   // behind the ground tiles: rises into the housing
            tinted.Add(sr);

            var lamp = new GameObject($"{go.name}_Lamp").AddComponent<SpriteRenderer>();
            lamp.transform.SetParent(markers, false);
            lamp.transform.localPosition = new Vector3(x - 0.5f, y + 4.5f, 0f);
            lamp.sprite = AssetDatabase.LoadAssetAtPath<Sprite>(GateLiftPng("gate_lamp"));
            lamp.sortingOrder = 3;

            var gate = go.AddComponent<global::ButtonGate>();
            gate.lamp = lamp;
            gate.buttons = new FloorButton[0];
            return gate;
        }

        // Decor sprite from tools/make_decor.py standing on the ground at x
        // (bottom-centre pivot, sunk by the ground tiles' 3 transparent rows).
        // No collider. A `<name>_glow` overlay, if there is one, is added
        // untinted so it shines at night.
        public void Decor(string id, float x, int order = -2)
        {
            int y = Surface(Mathf.FloorToInt(x));
            var sprite = AssetDatabase.LoadAssetAtPath<Sprite>(DecorPng(id));
            if (sprite == null)
            {
                Debug.LogWarning($"[LevelMap] No decor sprite {DecorPng(id)}");
                return;
            }
            counts["Decor"] = counts.TryGetValue("Decor", out var n) ? n + 1 : 1;
            var go = new GameObject($"Decor_{counts["Decor"]}_{id}");
            go.transform.SetParent(markers, false);
            go.transform.localPosition = new Vector3(x, y - 3f / 32f, 0f);
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = sprite;
            sr.sortingOrder = order;
            tinted.Add(sr);
            var glow = AssetDatabase.LoadAssetAtPath<Sprite>(DecorPng(id + "_glow"));
            if (glow != null)
            {
                var g = new GameObject("Glow").AddComponent<SpriteRenderer>();
                g.transform.SetParent(go.transform, false);
                g.sprite = glow;
                g.sortingOrder = order + 1;
                go.AddComponent<DecorGlow>().glow = g;
            }
            Busy(Mathf.FloorToInt(x) - 1, Mathf.CeilToInt(x) + 1);
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
            foreach (var p in solid)
                ground.SetTile((Vector3Int)p, strike.TryGetValue(p, out var sc) ? StrikeTile(sc.r, sc.c) : GroundTile(p));
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
            Sky.clouds = CreateClouds(go.transform);
            Sky.life = CreateSkyLife(go.transform);
            Sky.hour = startHour;
            Sky.lane = LaneIndex;
        }

        LaneSky.Layer SkyLayer(Transform parent, string id, string file, int order, float depth)
        {
            var layer = new LaneSky.Layer
            {
                day = CreateBackground(parent, $"Background_{id}_Day", $"Assets/Art/Backgrounds/{file}_day.png", order, depth),
                dusk = CreateBackground(parent, $"Background_{id}_Dusk", $"Assets/Art/Backgrounds/{file}.png", order, depth),
                night = CreateBackground(parent, $"Background_{id}_Night", $"Assets/Art/Backgrounds/{file}_night.png", order, depth),
                sortingOrder = order,
            };
            // Live hands over the painted clock faces, above both blended looks.
            // Child of the day copy, so it scrolls with that layer's parallax.
            var go = new GameObject($"TowerClocks_{id}");
            go.transform.SetParent(layer.day.transform, false);
            go.AddComponent<SpriteRenderer>().sortingOrder = order + 2;
            var clocks = go.AddComponent<TowerClocks>();
            clocks.background = layer.day;
            clocks.faces = ClockFaces[file];
            (clocks.dayHands, clocks.duskHands, clocks.nightHands) = ClockHands[file];
            layer.clocks = clocks;
            if (Chimneys.TryGetValue(file, out var chimneys))
            {
                // Over the buildings and their clocks, under the level.
                var smokeGo = new GameObject($"ChimneySmoke_{id}");
                smokeGo.transform.SetParent(layer.day.transform, false);
                smokeGo.AddComponent<SpriteRenderer>().sortingOrder = order + 3;
                var smoke = smokeGo.AddComponent<ChimneySmoke>();
                smoke.background = layer.day;
                smoke.chimneys = chimneys;
                smoke.lane = LaneIndex;
                smoke.seed = LaneIndex + 1;
                layer.smoke = smoke;
            }
            if (MidLife.TryGetValue(file, out var life))
            {
                var lifeGo = new GameObject($"Life_{id}");
                lifeGo.transform.SetParent(layer.day.transform, false);
                var l = lifeGo.AddComponent<MidLayerLife>();
                l.background = layer.day;
                l.lane = LaneIndex;
                l.seed = LaneIndex + 1;
                l.groundY = root.transform.position.y;
                l.pigeons = life.pigeons;
                l.ivy = life.ivy;
                l.pennant = life.pennant;
                l.windows = life.windows;
                layer.life = l;
            }
            return layer;
        }

        // Birds, airship, bats and stars, in the same sky band as the clouds.
        SkyLife CreateSkyLife(Transform parent)
        {
            var go = new GameObject("Life");
            go.transform.SetParent(parent, false);
            var life = go.AddComponent<SkyLife>();
            float top = CameraY + CameraSize + 0.25f;
            life.bandTop = top - 2f / 32f;
            life.bandBottom = top - 136f / 32f;
            life.lane = LaneIndex;
            life.seed = LaneIndex + 11;
            return life;
        }

        // In the clear sky above the far skyline (the top 136 rows of the far
        // picture, whose spires reach row 140), in front of the sun and moon (-15),
        // behind the mid layer (-10). The lane camera is set in AddParallax.
        SkyClouds CreateClouds(Transform parent)
        {
            var go = new GameObject("Clouds");
            go.transform.SetParent(parent, false);
            var clouds = go.AddComponent<SkyClouds>();
            float top = CameraY + CameraSize + 0.25f;   // top edge of the background pictures
            clouds.bandTop = top - 2f / 32f;
            clouds.bandBottom = top - 136f / 32f;
            clouds.lane = LaneIndex;
            clouds.seed = LaneIndex + 1;
            return clouds;
        }

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

        #region Obstacle lab: pendulum, gear fall, tesla gate, steam vent, water wheel
        // Builders for the obstacles shown in ObstacleLab.unity (ObstacleLabBuilder).
        // Art from the tools/make_<name>.py generators in Assets/Art/Obstacles/<Name>/;
        // each helper sets the import settings it needs (sprites at 32 PPU, point
        // filter, strips sliced one sprite per frame). Tinted like the other
        // obstacles through LaneSky; lamps, sparks, arcs and steam are not.

        static Sprite[] LabStrip(string path, int frameWidth, Vector2 pivot, bool fullRect = false)
        {
            PrepareStrip(path, frameWidth, pivot);
            if (fullRect && AssetImporter.GetAtPath(path) is TextureImporter ti)
            {
                var st = new TextureImporterSettings();
                ti.ReadTextureSettings(st);
                if (st.spriteMeshType != SpriteMeshType.FullRect)
                {
                    st.spriteMeshType = SpriteMeshType.FullRect;   // drawn tiled
                    ti.SetTextureSettings(st);
                    ti.SaveAndReimport();
                }
            }
            return StripFrames(path);
        }

        static Sprite LabSprite(string path, Vector2 pivot, bool fullRect = false)
        {
            PrepareSprite(path, fullRect, pivot);
            return AssetDatabase.LoadAssetAtPath<Sprite>(path);
        }

        string Numbered(string type)
        {
            counts[type] = counts.TryGetValue(type, out var n) ? n + 1 : 1;
            return $"{type}_{counts[type]}";
        }

        SpriteRenderer LabPart(string partName, Transform parent, Sprite sprite, int order, Vector2 at, bool tint = true)
        {
            var go = new GameObject(partName);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = at;
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = sprite;
            sr.sortingOrder = order;
            if (tint) tinted.Add(sr);
            return sr;
        }

        static Rigidbody2D Kinematic(GameObject go)
        {
            int layer = LayerMask.NameToLayer(GroundLayer);
            if (layer >= 0) go.layer = layer;
            var rb = go.AddComponent<Rigidbody2D>();
            rb.bodyType = RigidbodyType2D.Kinematic;
            rb.gravityScale = 0f;
            rb.interpolation = RigidbodyInterpolation2D.Interpolate;
            return rb;
        }

        float LaneY => root.transform.position.y;

        // Pendulum blade swinging under a stone housing over x (axis at x + 0.5,
        // 9 tiles up): bracket, tiled rod `rodTiles` long, hub and bob. With the
        // default 6-tile rod the blade's edge passes 0.6 above the ground, so a
        // player can't duck under it; it swings `amplitude` degrees either side
        // (about 5 tiles) in `period` lane seconds. Leave x - 6..x + 6 free.
        public Pendulum Pendulum(int x, int rodTiles = 6, float amplitude = 44f, float period = 2.4f, float offset = 0f)
        {
            const string dir = "Assets/Art/Obstacles/Pendulum/";
            int y = Surface(x);
            int ceiling = y + 9;
            Fill(x - 1, x + 1, ceiling + 2, ceiling);
            Busy(x - 6, x + 6);

            var go = new GameObject(Numbered("Pendulum"));
            go.transform.SetParent(markers, false);
            go.transform.localPosition = new Vector3(x + 0.5f, ceiling - 22f / 32f, 0f);   // the swing axis
            LabPart("Bracket", go.transform, LabSprite(dir + "pendulum_bracket_32px.png", new Vector2(0.5f, 10f / 32f)), 4, Vector2.zero);
            var arm = new GameObject("Arm").transform;
            arm.SetParent(go.transform, false);
            var rod = LabPart("Rod", arm, LabSprite(dir + "pendulum_rod_32px.png", new Vector2(0.5f, 1f), true), 21, Vector2.zero);
            rod.drawMode = SpriteDrawMode.Tiled;
            rod.size = new Vector2(10f / 32f, rodTiles);
            LabPart("Hub", arm, LabSprite(dir + "pendulum_hub_32px.png", new Vector2(0.5f, 0.5f)), 23, Vector2.zero);
            var bobFrames = LabStrip(dir + "pendulum_bob_96px.png", 96, new Vector2(0.5f, 62f / 64f));
            // In front of the players, so the blade reads as cutting through them.
            var bob = LabPart("Bob", arm, bobFrames.FirstOrDefault(), 22, new Vector2(0f, -rodTiles));

            var p = go.AddComponent<global::Pendulum>();
            p.lane = LaneIndex;
            p.cycleOffset = offset;
            p.amplitude = amplitude;
            p.period = period;
            p.reach = rodTiles;
            p.arm = arm;
            p.bob = bob;
            p.bobFrames = bobFrames;
            return p;
        }

        // Gear fall: a chute under a stone housing over x - 1..x + 1 (9 tiles up)
        // drops a 2x2 cog every `interval` lane seconds; it lands at x + 0.5,
        // rolls `roll` units per lane second for 2 lane seconds (negative: left)
        // and shatters. Leave the roll path free (4 tiles at the default).
        public GearFall GearFall(int x, float roll = 2f, float interval = 3f, float offset = 0f)
        {
            const string dir = "Assets/Art/Obstacles/GearFall/";
            int y = Surface(x);
            int ceiling = y + 9;
            Fill(x - 1, x + 1, ceiling + 2, ceiling);
            float rollTime = 2f;
            Busy(x - 2, x + 1 + Mathf.CeilToInt(Mathf.Max(0f, roll * rollTime) + 1f));
            Busy(x - 1 + Mathf.FloorToInt(Mathf.Min(0f, roll * rollTime) - 1f), x + 2);

            var go = new GameObject(Numbered("GearFall"));
            go.transform.SetParent(markers, false);
            go.transform.localPosition = new Vector3(x + 0.5f, ceiling, 0f);
            var chuteFrames = LabStrip(dir + "gearfall_chute_96px.png", 96, new Vector2(0.5f, 1f));
            var lampFrames = LabStrip(dir + "gearfall_lamp_96px.png", 96, new Vector2(0.5f, 1f));
            var gearFrames = LabStrip(dir + "gearfall_gear_64px.png", 64, new Vector2(0.5f, 0.5f));
            var shatterFrames = LabStrip(dir + "gearfall_shatter_128px.png", 128, new Vector2(0.5f, 1f / 3f));
            var chute = LabPart("Chute", go.transform, chuteFrames.FirstOrDefault(), 6, Vector2.zero);
            var lamp = LabPart("Lamp", go.transform, lampFrames.FirstOrDefault(), 7, Vector2.zero, false);

            var g = go.AddComponent<global::GearFall>();
            g.lane = LaneIndex;
            g.cycleOffset = offset;
            g.interval = interval;
            g.rollSpeed = roll;
            g.rollTime = rollTime;
            g.spawn = new Vector2(x + 0.5f, LaneY + ceiling - 12f / 32f);
            g.landY = LaneY + y - 3f / 32f + 1f;   // bottom on the ground collider's top
            float fall = Mathf.Sqrt(2f * (g.spawn.y - g.landY) / g.gravity);
            int pool = Mathf.CeilToInt((fall + rollTime + 8f / 15f) / interval) + 1;
            g.gears = new Rigidbody2D[pool];
            for (int i = 0; i < pool; i++)
            {
                var sr = LabPart($"Gear_{i}", go.transform, gearFrames.FirstOrDefault(), 5, new Vector2(0f, -12f / 32f));
                sr.enabled = false;
                var rb = Kinematic(sr.gameObject);
                sr.gameObject.AddComponent<CircleCollider2D>().radius = g.radius;
                g.gears[i] = rb;
            }
            g.chute = chute;
            g.chuteFrames = chuteFrames;
            g.lamp = lamp;
            g.lampFrames = lampFrames;
            g.gearFrames = gearFrames;
            g.shatterFrames = shatterFrames;
            return g;
        }

        // Tesla gate: coil pylons on columns x0 and x1 (4 tiles tall, scenery:
        // players walk in front of them) and an arc between their emitters 1.16
        // above the ground. With `ceiling` > 0 a stone lintel between the pylons
        // from that height stops players jumping over the arc (at 3 they can
        // rise only 1.75), so the gate is passable only while it is off.
        public TeslaGate TeslaGate(int x0, int x1, int ceiling = 3, float offset = 0f)
        {
            const string dir = "Assets/Art/Obstacles/TeslaGate/";
            int y = Surface(x0);
            if (ceiling > 0) Fill(x0 + 1, x1 - 1, y + ceiling + 2, y + ceiling);
            Busy(x0 - 1, x1 + 1);

            var go = new GameObject(Numbered("TeslaGate"));
            go.transform.SetParent(markers, false);
            go.transform.localPosition = new Vector3(x0, y, 0f);
            var pylon = LabSprite(dir + "teslagate_pylon_32px.png", new Vector2(0.5f, 0f));
            var fxFrames = LabStrip(dir + "teslagate_fx_32px.png", 32, new Vector2(0.5f, 0f));
            var lampFrames = LabStrip(dir + "teslagate_lamp_32px.png", 32, new Vector2(0.5f, 0f));
            var arcFrames = LabStrip(dir + "teslagate_arc_32px.png", 32, new Vector2(0f, 0.5f));
            var fx = new List<SpriteRenderer>();
            var lamps = new List<SpriteRenderer>();
            foreach (var (px, flip) in new[] { (x0, false), (x1, true) })
            {
                var at = new Vector2(px - x0 + 0.5f, -3f / 32f);   // on the walking surface
                var body = LabPart(flip ? "PylonRight" : "PylonLeft", go.transform, pylon, 2, at);
                body.flipX = flip;
                var f = LabPart("Fx", body.transform, fxFrames.FirstOrDefault(), 3, Vector2.zero, false);
                var l = LabPart("Lamp", body.transform, lampFrames.FirstOrDefault(), 3, Vector2.zero, false);
                f.flipX = l.flipX = flip;
                fx.Add(f);
                lamps.Add(l);
            }

            // Arc: from the left pylon's emitter ball (pixel 29) to the right one's
            // (pixel 2, mirrored 29), in whole-tile segments stretched to fit.
            float left = 29.5f / 32f, right = x1 - x0 + 2.5f / 32f, arcY = -3f / 32f + 37f / 32f;
            int n = Mathf.Max(1, Mathf.CeilToInt(right - left));
            float w = (right - left) / n;
            var segments = new SpriteRenderer[n];
            for (int i = 0; i < n; i++)
            {
                var seg = LabPart($"Arc_{i}", go.transform, arcFrames.FirstOrDefault(), 21, new Vector2(left + i * w, arcY), false);
                seg.transform.localScale = new Vector3(w, 1f, 1f);
                seg.enabled = false;
                segments[i] = seg;
            }

            var gate = go.AddComponent<global::TeslaGate>();
            gate.lane = LaneIndex;
            gate.cycleOffset = offset;
            gate.arcLeft = x0 + left;
            gate.arcRight = x0 + right;
            gate.arcY = LaneY + y + arcY;
            gate.fx = fx.ToArray();
            gate.lamps = lamps.ToArray();
            gate.fxFrames = fxFrames;
            gate.lampFrames = lampFrames;
            gate.arcSegments = segments;
            gate.arcFrames = arcFrames;
            return gate;
        }

        // Steam vent over x..x + 1, flush in the ground's top row (the ground
        // tiles stay under it for the collider): blasts a 4-tile steam column
        // that lifts players about 5.4 tiles, over a wall a jump can't clear.
        public SteamVent SteamVent(int x, float offset = 0f)
        {
            const string dir = "Assets/Art/Obstacles/SteamVent/";
            int y = Surface(x);
            Busy(x - 1, x + 2);

            var go = new GameObject(Numbered("SteamVent"));
            go.transform.SetParent(markers, false);
            go.transform.localPosition = new Vector3(x, y - 1, 0f);
            var grateFrames = LabStrip(dir + "steamvent_grate_64px.png", 64, Vector2.zero);
            var steamFrames = LabStrip(dir + "steamvent_steam_64px.png", 64, new Vector2(0.5f, 0f));
            var grate = LabPart("Grate", go.transform, grateFrames.FirstOrDefault(), 1, Vector2.zero);
            var steam = LabPart("Steam", go.transform, steamFrames.FirstOrDefault(), 21, new Vector2(1f, 1f - 3f / 32f), false);
            steam.enabled = false;

            var v = go.AddComponent<global::SteamVent>();
            v.lane = LaneIndex;
            v.cycleOffset = offset;
            v.columnX = x + 1f;
            v.columnBottom = LaneY + y - 1f / 32f;   // the ground collider's top
            v.grate = grate;
            v.grateFrames = grateFrames;
            v.steam = steam;
            v.steamFrames = steamFrames;
            return v;
        }

        // Water wheel with its axle over x, 4.5 tiles up (7 tiles across, the
        // bottom bucket's floor 0.7 above the ground): a flume from the left
        // under a stone housing (x - 7..x - 3, rows 8-9) pours into the bucket at
        // the top left, a tailrace runs under the wheel (x - 4..x + 3). Put the
        // ledge it serves on the right from x + 4, 7 tiles high: only a ride in
        // a bucket (partner running) gets there.
        public WaterWheel WaterWheel(int x, float offset = 0f)
        {
            const string dir = "Assets/Art/Obstacles/WaterWheel/";
            int y = Surface(x);
            Fill(x - 7, x - 3, y + 10, y + 8);
            Busy(x - 8, x + 4);

            var go = new GameObject(Numbered("WaterWheel"));
            go.transform.SetParent(markers, false);
            go.transform.localPosition = new Vector3(x, y + 4.5f, 0f);
            var turn = LabStrip(dir + "waterwheel_turn_224px.png", 224, new Vector2(0.5f, 0.5f));
            var bucketFrames = LabStrip(dir + "waterwheel_bucket_64px.png", 64, new Vector2(0.5f, 56f / 64f));
            var waterFrames = LabStrip(dir + "waterwheel_bucket_water_64px.png", 64, new Vector2(0.5f, 56f / 64f));
            var pourFrames = LabStrip(dir + "waterwheel_pour_32px.png", 32, new Vector2(0.5f, 1f), true);
            var flumeFrames = LabStrip(dir + "waterwheel_flume_32px.png", 32, new Vector2(0f, 1f));
            var tailFrames = LabStrip(dir + "waterwheel_tailrace_32px.png", 32, new Vector2(0f, 1f));
            var splashFrames = LabStrip(dir + "waterwheel_splash_48px.png", 48, new Vector2(0.5f, 0f));
            var trickleFrames = LabStrip(dir + "waterwheel_trickle_32px.png", 32, new Vector2(0.5f, 1f), true);

            var w = go.AddComponent<global::WaterWheel>();
            w.lane = LaneIndex;
            w.cycleOffset = offset;
            w.centre = new Vector2(x, LaneY + y + 4.5f);
            w.wheel = LabPart("Wheel", go.transform, turn.FirstOrDefault(), 2, Vector2.zero);
            w.wheelFrames = turn;

            // Buckets on the pins (wheel angle 0: pin j at 45 j degrees).
            w.buckets = new Rigidbody2D[8];
            w.bucketWater = new SpriteRenderer[8];
            for (int j = 0; j < 8; j++)
            {
                float a = 45f * j * Mathf.Deg2Rad;
                var b = LabPart($"Bucket_{j}", go.transform, bucketFrames.ElementAtOrDefault(9), 4,
                    new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * w.pinRadius);
                var rb = Kinematic(b.gameObject);
                var box = b.gameObject.AddComponent<BoxCollider2D>();
                box.size = new Vector2(1.25f, 0.25f);
                box.offset = new Vector2(0f, -w.floorDrop - 0.125f);
                box.usedByEffector = true;
                b.gameObject.AddComponent<PlatformEffector2D>().useOneWay = true;
                w.buckets[j] = rb;
                var water = LabPart("Water", b.transform, waterFrames.ElementAtOrDefault(3), 5, Vector2.zero);
                water.enabled = false;
                w.bucketWater[j] = water;
            }
            w.bucketFrames = bucketFrames;
            w.waterFrames = waterFrames;

            // Flume: trough tiles from the left ending over the top-left bucket.
            float mouthX = -Mathf.Cos(Mathf.PI / 4f) * w.pinRadius;    // local, from the axle
            float flumeTop = 2.9f;                                     // local: 7.4 above the ground
            var flume = new List<SpriteRenderer>();
            for (int i = 0; i < 5; i++)
                flume.Add(LabPart($"Flume_{i}", go.transform, flumeFrames.FirstOrDefault(), 4,
                    new Vector2(mouthX + 0.25f - 5 + i, flumeTop)));
            w.flume = flume.ToArray();
            w.flumeFrames = flumeFrames;
            w.mouth = w.centre + new Vector2(mouthX, flumeTop - 0.5f);

            // Tailrace in the ground's top row under the wheel.
            var tail = new List<SpriteRenderer>();
            for (int i = -4; i <= 3; i++)
                tail.Add(LabPart($"Tailrace_{i + 4}", go.transform, tailFrames.FirstOrDefault(), 1,
                    new Vector2(i, -4.5f - 2f / 32f)));
            w.tailrace = tail.ToArray();
            w.tailraceFrames = tailFrames;
            w.tailraceY = LaneY + y - 6f / 32f;

            SpriteRenderer Stream(string streamName, Sprite[] frames, int order)
            {
                var sr = LabPart(streamName, go.transform, frames.FirstOrDefault(), order, w.mouth - w.centre);
                sr.drawMode = SpriteDrawMode.Tiled;
                sr.size = new Vector2(1f, 2f);
                sr.enabled = false;
                return sr;
            }
            w.pour = Stream("Pour", pourFrames, 3);
            w.trickle = Stream("Trickle", trickleFrames, 3);
            w.spill = Stream("Spill", trickleFrames, 3);
            w.pourFrames = pourFrames;
            w.trickleFrames = trickleFrames;
            w.splashTop = LabPart("SplashTop", go.transform, splashFrames.FirstOrDefault(), 6, Vector2.zero);
            w.splashBottom = LabPart("SplashBottom", go.transform, splashFrames.FirstOrDefault(), 6, Vector2.zero);
            w.splashTop.enabled = w.splashBottom.enabled = false;
            w.splashFrames = splashFrames;
            return w;
        }
        #endregion
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
        flow.laneGroundY = new[] { 0f, TockLaneY };
        flow.gameObject.AddComponent<LaneRift>();
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
        divider.rift = DividerLayer(go.transform, "Rift", 0.5f);
        divider.topRift = DividerLayer(go.transform, "TopRift", 1f);
        divider.topCamera = tickCamera;
        divider.bottomCamera = tockCamera;
    }

    // The rift textures are drawn at runtime by LaneDivider, which also sizes
    // the rect around its anchor line (anchorY: 0.5 the seam, 1 the screen top).
    static RawImage DividerLayer(Transform parent, string id, float anchorY)
    {
        var go = new GameObject(id, typeof(RectTransform), typeof(RawImage));
        go.transform.SetParent(parent, false);
        go.layer = parent.gameObject.layer;
        var rt = (RectTransform)go.transform;
        rt.anchorMin = new Vector2(0f, anchorY);
        rt.anchorMax = new Vector2(1f, anchorY);
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.sizeDelta = Vector2.zero;
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

    // Strike stone Tile next to its sprite, made on first use. Sprite collider
    // like the ground tiles, so the surface stays solid at the same height.
    static Tile StrikeTile(int r, int c)
    {
        string path = $"{StrikeDir}strike_r{r}c{c}.asset";
        var t = AssetDatabase.LoadAssetAtPath<Tile>(path);
        if (t != null) return t;
        t = ScriptableObject.CreateInstance<Tile>();
        t.sprite = AssetDatabase.LoadAssetAtPath<Sprite>($"{StrikeDir}strike_r{r}c{c}_32px.png");
        t.colliderType = UnityEngine.Tilemaps.Tile.ColliderType.Sprite;
        AssetDatabase.CreateAsset(t, path);
        return t;
    }

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
    internal static void PrepareSprite(string path, bool fullRect, Vector2 pivot)
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

    // Horizontal strip of equal frames, sliced one sprite per frame (<file>_<n>),
    // 32 PPU, point filter, every frame with the same pivot.
    internal static void PrepareStrip(string path, int frameWidth, Vector2 pivot)
    {
        if (!(AssetImporter.GetAtPath(path) is TextureImporter ti)) return;
        // Source size: a strip wider than the max texture size imports scaled down.
        ti.GetSourceTextureWidthAndHeight(out int width, out int height);
        int count = width / frameWidth;
        int maxSize = Mathf.Max(2048, Mathf.NextPowerOfTwo(width));
        if (ti.spriteImportMode == SpriteImportMode.Multiple && ti.spritePixelsPerUnit == 32
            && ti.maxTextureSize >= maxSize && StripFrames(path).Length == count) return;
        ti.textureType = TextureImporterType.Sprite;
        ti.spriteImportMode = SpriteImportMode.Multiple;
        ti.spritePixelsPerUnit = 32;
        ti.filterMode = FilterMode.Point;
        ti.mipmapEnabled = false;
        ti.alphaIsTransparency = true;
        ti.wrapMode = TextureWrapMode.Clamp;
        ti.textureCompression = TextureImporterCompression.Uncompressed;
        ti.maxTextureSize = Mathf.Max(ti.maxTextureSize, maxSize);
        ti.SaveAndReimport();

        var factory = new SpriteDataProviderFactories();
        factory.Init();
        var provider = factory.GetSpriteEditorDataProviderFromObject(ti);
        provider.InitSpriteEditorDataProvider();
        string sheet = System.IO.Path.GetFileNameWithoutExtension(path);
        var rects = Enumerable.Range(0, count).Select(i => new SpriteRect
        {
            name = $"{sheet}_{i}",
            rect = new Rect(i * frameWidth, 0, frameWidth, height),
            alignment = SpriteAlignment.Custom,
            pivot = pivot,
            spriteID = GUID.Generate(),
        }).ToArray();
        provider.SetSpriteRects(rects);
        provider.GetDataProvider<ISpriteNameFileIdDataProvider>()?
            .SetNameFileIdPairs(rects.Select(r => new SpriteNameFileIdPair(r.name, r.spriteID)));
        provider.Apply();
        ti.SaveAndReimport();
    }

    // Frames of a strip from PrepareStrip, in order.
    internal static Sprite[] StripFrames(string path) =>
        AssetDatabase.LoadAllAssetsAtPath(path).OfType<Sprite>()
            .OrderBy(s => int.Parse(s.name.Substring(s.name.LastIndexOf('_') + 1))).ToArray();

    // Menus in the order of MenuScenes come first in Build Settings (the first
    // one is the scene the game starts in), then the enabled scenes, then the
    // disabled ones; within each, levels sorted by name (Level1_, Level2_, ...)
    // before the rest.
    static readonly string[] MenuScenes = { "/LevelSelect.unity", "/MainMenu.unity" };

    internal static void AddToBuildSettings(string scenePath)
    {
        var scenes = EditorBuildSettings.scenes.Where(s => s.path != scenePath).ToList();
        scenes.Add(new EditorBuildSettingsScene(scenePath, true));
        int MenuRank(EditorBuildSettingsScene s) => Array.FindIndex(MenuScenes, m => s.path.EndsWith(m));
        var menus = scenes.Where(s => MenuRank(s) >= 0).OrderBy(MenuRank);
        var levels = scenes.Where(s => MenuRank(s) < 0 && s.path.Contains("/Level")).OrderBy(s => s.path);
        var others = scenes.Where(s => MenuRank(s) < 0 && !s.path.Contains("/Level"));
        var rest = levels.Concat(others).ToList();
        EditorBuildSettings.scenes = menus.Concat(rest.Where(s => s.enabled)).Concat(rest.Where(s => !s.enabled)).ToArray();
    }
}
