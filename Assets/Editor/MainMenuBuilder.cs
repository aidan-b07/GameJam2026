using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Tilemaps;
using UnityEngine.UI;

// Builds the main menu scene (Tick & Tock > Build Main Menu), the first scene in
// Build Settings. Everything is laid out in art pixels on a 640x360 screen, the
// same art size as one lane: the menu camera renders the pixel art at exactly
// one texel per art pixel into a render texture, and the screen canvas shows it
// scaled up to the window (letterboxed at 16:9).
//
// Art from tools/menu (make_menu_buttons.py, make_menu_text.py) in
// Assets/Art/UI/Menu, plus the level's own backgrounds, ground tiles, sun, moon
// and player sheets. Layout (art pixels, origin top-left):
//   title    centred, top at y 44 (bobs a pixel)
//   plates   168x32, centred, tops at y 140, 180, 220, 260
//   ground   strip of ground tiles from y 312; Tick and Tock run on it at x 70 and 104
//   rift     both sides, drawn by SideRift
public static class MainMenuBuilder
{
    public const string ScenePath = "Assets/Scenes/MainMenu.unity";
    internal const string MenuDir = "Assets/Art/UI/Menu/";
    const string ScreenTexturePath = MenuDir + "MenuScreen.renderTexture";
    internal const int ArtWidth = 640, ArtHeight = 360;
    internal const float Ppu = 32f;
    const float HalfWidth = ArtWidth / Ppu / 2f;    // 10 units
    const float HalfHeight = ArtHeight / Ppu / 2f;  // 5.625 units
    static readonly int[] PlateTops = { 140, 180, 220, 260 };
    const int GroundTop = 312;
    static readonly string[] Styles = { "stone", "brass", "rift" };
    static readonly string[] TitleFonts = { "jersey", "tiny", "workbench", "pixelify" };
    // Top-row ground tiles per column of the 20-tile pattern (columns 2-4 have ivy).
    static readonly int[] GroundTops = { 1, 5, 6, 1, 2, 6, 5, 1, 3, 5, 6, 1, 5, 4, 6, 1, 5, 6, 2, 5 };

    [MenuItem("Tick & Tock/Build Main Menu")]
    public static void Build()
    {
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        var stage = BuildStage();
        Menu(stage.camera, stage.screen, stage.rift);

        EditorSceneManager.SaveScene(scene, ScenePath);
        LevelMap.AddToBuildSettings(ScenePath);
        Debug.Log($"[MainMenuBuilder] Saved {ScenePath}");
    }

    // Everything behind a menu, shared by the main menu and the level select:
    // the cameras and render texture, the scrolling backdrop with its day
    // cycle and runners, the letterboxed screen and the side rift.
    internal static (Camera camera, RawImage screen, SideRift rift) BuildStage()
    {
        PrepareArt();
        var screenTexture = ScreenTexture();
        var menuCamera = MenuCamera(screenTexture);
        ScreenCamera();

        var backdrop = new GameObject("Backdrop");
        var far = Background(backdrop.transform, "Far", "bg_far_360h", -20);
        var mid = Background(backdrop.transform, "Mid", "bg_mid_360h", -10);
        var sun = SkyBody(backdrop.transform, "Sun");
        var moon = SkyBody(backdrop.transform, "Moon");
        var ground = Ground(backdrop.transform);
        var tick = Runner(backdrop.transform, "Tick", "Assets/Art/Characters/tick_32px.png", 70, 0);
        var tock = Runner(backdrop.transform, "Tock", "Assets/Art/Characters/tock_32px.png", 104, 1);

        var sky = backdrop.AddComponent<LaneSky>();
        sky.layers = new[] { far.layer, mid.layer };
        sky.tilemaps = new[] { ground.GetComponentInChildren<Tilemap>() };
        sky.sprites = new[] { tick.renderer, tock.renderer };
        sky.sun = sun;
        sky.moon = moon;
        sky.skyAnchor = menuCamera.transform;
        sky.lane = -1;
        sky.hour = 10f;

        var scroll = backdrop.AddComponent<MenuBackdrop>();
        scroll.strips = new[]
        {
            new MenuBackdrop.Strip { transform = far.root, speed = 14f, wrap = ArtWidth / Ppu },
            new MenuBackdrop.Strip { transform = mid.root, speed = 38f, wrap = ArtWidth / Ppu },
            new MenuBackdrop.Strip { transform = ground.transform, speed = 110f, wrap = GroundTops.Length },
        };
        scroll.runners = new[] { tick, tock };
        scroll.sky = sky;

        var screen = ScreenCanvas(screenTexture, out var glitchLayer);
        var rift = Rift(screen, glitchLayer);
        return (menuCamera, screen, rift);
    }

    // ---- art --------------------------------------------------------------

    static void PrepareArt()
    {
        foreach (var path in AssetDatabase.FindAssets("t:Texture2D", new[] { MenuDir.TrimEnd('/') })
                     .Select(AssetDatabase.GUIDToAssetPath).Where(p => p.EndsWith(".png")))
        {
            if (path.EndsWith("menu_cog.png"))
                LevelMap.PrepareStrip(path, 15, new Vector2(7f / 15f, 7f / 15f));
            else
                LevelMap.PrepareSprite(path, false, PixelCentre(path));
        }
        LevelMap.PrepareSprite("Assets/Art/Sky/sun.png", false, new Vector2(0.5f, 0.5f));
        LevelMap.PrepareSprite("Assets/Art/Sky/moon.png", false, new Vector2(0.5f, 0.5f));
    }

    // Centre pivot rounded down to a whole pixel, so sprites of odd size stay on the pixel grid.
    static Vector2 PixelCentre(string path)
    {
        var ti = (TextureImporter)AssetImporter.GetAtPath(path);
        ti.GetSourceTextureWidthAndHeight(out int w, out int h);
        return new Vector2(Mathf.Floor(w / 2f) / w, Mathf.Floor(h / 2f) / h);
    }

    internal static Sprite MenuSprite(string name) => AssetDatabase.LoadAssetAtPath<Sprite>($"{MenuDir}{name}.png");

    // World position for a sprite whose top-left pixel sits at art pixel (x, y).
    internal static Vector3 At(Sprite s, float x, float y)
    {
        var size = s.rect.size;
        return new Vector3((x + s.pivot.x) / Ppu - HalfWidth, HalfHeight - (y + size.y - s.pivot.y) / Ppu, 0f);
    }

    // ---- cameras and screen -------------------------------------------------

    static RenderTexture ScreenTexture()
    {
        var rt = AssetDatabase.LoadAssetAtPath<RenderTexture>(ScreenTexturePath);
        if (rt != null) return rt;
        rt = new RenderTexture(ArtWidth, ArtHeight, 16, RenderTextureFormat.ARGB32)
        {
            filterMode = FilterMode.Point,
            wrapMode = TextureWrapMode.Clamp,
            antiAliasing = 1,
            useMipMap = false,
            name = "MenuScreen",
        };
        AssetDatabase.CreateAsset(rt, ScreenTexturePath);
        return rt;
    }

    // Renders the menu's pixel art, one texel per art pixel.
    static Camera MenuCamera(RenderTexture target)
    {
        var go = new GameObject("MenuCamera");
        go.transform.position = new Vector3(0f, 0f, -10f);
        var cam = go.AddComponent<Camera>();
        cam.orthographic = true;
        cam.orthographicSize = HalfHeight;
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = new Color32(0x28, 0x14, 0x3a, 255);
        cam.targetTexture = target;
        return cam;
    }

    // Clears the window to black around the letterboxed menu; draws nothing else.
    static void ScreenCamera()
    {
        var go = new GameObject("ScreenCamera");
        go.tag = "MainCamera";
        go.transform.position = new Vector3(0f, 0f, -10f);
        var cam = go.AddComponent<Camera>();
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = Color.black;
        cam.cullingMask = 0;
        cam.depth = -10;
        go.AddComponent<AudioListener>();
    }

    static RawImage ScreenCanvas(RenderTexture texture, out RectTransform glitchLayer)
    {
        var canvasGo = new GameObject("ScreenCanvas", typeof(Canvas), typeof(CanvasScaler));
        canvasGo.layer = LayerMask.NameToLayer("UI");
        var canvas = canvasGo.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        var scaler = canvasGo.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1280f, 720f);
        scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
        scaler.matchWidthOrHeight = 1f;

        var screenGo = new GameObject("Screen", typeof(RectTransform), typeof(RawImage), typeof(AspectRatioFitter));
        screenGo.layer = canvasGo.layer;
        screenGo.transform.SetParent(canvasGo.transform, false);
        Stretch((RectTransform)screenGo.transform);
        var image = screenGo.GetComponent<RawImage>();
        image.texture = texture;
        image.raycastTarget = false;
        var fitter = screenGo.GetComponent<AspectRatioFitter>();
        fitter.aspectMode = AspectRatioFitter.AspectMode.FitInParent;
        fitter.aspectRatio = ArtWidth / (float)ArtHeight;

        var glitch = new GameObject("Glitch", typeof(RectTransform));
        glitch.layer = canvasGo.layer;
        glitch.transform.SetParent(screenGo.transform, false);
        glitchLayer = (RectTransform)glitch.transform;
        Stretch(glitchLayer);
        return image;
    }

    static void Stretch(RectTransform rt)
    {
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = rt.offsetMax = Vector2.zero;
    }

    // ---- backdrop -----------------------------------------------------------

    // A background picture in its day, dusk and night copies (the unsuffixed
    // file is dusk), tiled two pictures wide so it can scroll one picture's width.
    static (Transform root, LaneSky.Layer layer) Background(Transform parent, string id, string file, int order)
    {
        var root = new GameObject(id).transform;
        root.SetParent(parent, false);
        SpriteRenderer Copy(string look, string suffix)
        {
            var go = new GameObject($"{id}_{look}");
            go.transform.SetParent(root, false);
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = AssetDatabase.LoadAssetAtPath<Sprite>($"Assets/Art/Backgrounds/{file}{suffix}.png");
            sr.drawMode = SpriteDrawMode.Tiled;
            sr.size = new Vector2(ArtWidth * 2 / Ppu, ArtHeight / Ppu);
            sr.sortingOrder = order;
            go.transform.localPosition = new Vector3(HalfWidth, 0f, 0f);   // left edge on the view's left edge
            return sr;
        }
        var layer = new LaneSky.Layer
        {
            day = Copy("Day", "_day"),
            dusk = Copy("Dusk", ""),
            night = Copy("Night", "_night"),
            sortingOrder = order,
        };
        return (root, layer);
    }

    // Between the far (-20) and mid (-10) layers, as in the levels; LaneSky moves them.
    static SpriteRenderer SkyBody(Transform parent, string id)
    {
        var go = new GameObject(id);
        go.transform.SetParent(parent, false);
        var sr = go.AddComponent<SpriteRenderer>();
        sr.sprite = AssetDatabase.LoadAssetAtPath<Sprite>($"Assets/Art/Sky/{id.ToLowerInvariant()}.png");
        sr.sortingOrder = -15;
        return sr;
    }

    // Two rows of ground tiles from art y 312 (top row with its 3 clear rows,
    // then the fill row, cut off by the screen), two 20-tile patterns wide.
    static GameObject Ground(Transform parent)
    {
        var grid = new GameObject("Ground", typeof(Grid));
        grid.transform.SetParent(parent, false);
        grid.transform.localPosition = new Vector3(0f, HalfHeight - (GroundTop + 32f) / Ppu, 0f);
        var tmGo = new GameObject("Tiles", typeof(Tilemap), typeof(TilemapRenderer));
        tmGo.transform.SetParent(grid.transform, false);
        tmGo.GetComponent<TilemapRenderer>().sortingOrder = 0;
        var tm = tmGo.GetComponent<Tilemap>();
        int n = GroundTops.Length;
        for (int i = 0; i < n * 2; i++)
        {
            int x = i - (int)HalfWidth;
            tm.SetTile(new Vector3Int(x, 0, 0), GroundTile(0, GroundTops[i % n]));
            tm.SetTile(new Vector3Int(x, -1, 0), GroundTile(1, 1 + (i % n * 7) % 6));
        }
        return grid;
    }

    static TileBase GroundTile(int r, int c)
    {
        string path = $"Assets/Art/Tiles/Generated/ground_32px/ground_32px_r{r}c{c}.asset";
        var t = AssetDatabase.LoadAssetAtPath<TileBase>(path);
        if (t == null) Debug.LogError($"[MainMenuBuilder] Missing tile {path}; run Tick & Tock > Import Art first.");
        return t;
    }

    // A player running in place on the ground (run cycle = frames 1-4 of the sheet).
    static MenuBackdrop.Runner Runner(Transform parent, string id, string sheet, int x, int phase)
    {
        var frames = LevelMap.StripFrames(sheet).Skip(1).Take(4).ToArray();
        var go = new GameObject(id);
        go.transform.SetParent(parent, false);
        var sr = go.AddComponent<SpriteRenderer>();
        sr.sprite = frames.Length > 0 ? frames[0] : null;
        sr.sortingOrder = 2;
        // Bottom-centre pivot; feet on the ground's surface (3 px into its top tile).
        go.transform.localPosition = new Vector3((x + 16f) / Ppu - HalfWidth, HalfHeight - (GroundTop + 3f) / Ppu, 0f);
        return new MenuBackdrop.Runner { renderer = sr, frames = frames, phase = phase };
    }

    // ---- rift ---------------------------------------------------------------

    static SideRift Rift(RawImage screen, RectTransform glitchLayer)
    {
        var go = new GameObject("SideRift");
        var sr = go.AddComponent<SpriteRenderer>();
        sr.sortingOrder = 30;
        var rift = go.AddComponent<SideRift>();
        rift.target = sr;
        rift.screen = screen;
        rift.glitchLayer = glitchLayer;
        return rift;
    }

    // ---- menu ---------------------------------------------------------------

    static void Menu(Camera menuCamera, RawImage screen, SideRift rift)
    {
        var root = new GameObject("MainMenu");
        var menu = root.AddComponent<MainMenu>();
        SpriteRenderer Part(string name, Transform parent, Sprite sprite, int order, Vector3 at)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = at;
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = sprite;
            sr.sortingOrder = order;
            return sr;
        }

        var titles = TitleFonts.Select(f => MenuSprite($"title_{f}")).ToArray();
        var t0 = titles[0];
        menu.title = Part("Title", root.transform, t0, 20,
            At(t0, Mathf.Round(ArtWidth / 2f - t0.rect.width / 2f), 44f));
        menu.titleSprites = titles;

        MainMenu.PlateArt Art(string style) => new MainMenu.PlateArt
        {
            idle = Enumerable.Range(0, 4).Select(i => MenuSprite($"btn_{style}_{i}_idle")).ToArray(),
            selected = Enumerable.Range(0, 4).Select(i => MenuSprite($"btn_{style}_{i}_sel")).ToArray(),
            labelIdle = Enumerable.Range(0, 4).Select(i => MenuSprite($"label_{style}_{i}_idle")).ToArray(),
            labelSelected = Enumerable.Range(0, 4).Select(i => MenuSprite($"label_{style}_{i}_sel")).ToArray(),
        };
        menu.stone = Art(Styles[0]);
        menu.brass = Art(Styles[1]);
        menu.rift = Art(Styles[2]);

        var names = new[] { "Play", "SelectLevel", "Settings", "Quit" };
        menu.plates = new SpriteRenderer[4];
        menu.labels = new SpriteRenderer[4];
        int plateLeft = (ArtWidth - 168) / 2;
        for (int i = 0; i < 4; i++)
        {
            var plateSprite = menu.stone.idle[i];
            // The plate sprite is 212x56 with the plate's top-left at (22, 12).
            var plate = Part($"Button_{names[i]}", root.transform, plateSprite, 20,
                At(plateSprite, plateLeft - 22, PlateTops[i] - 12));
            var label = menu.stone.labelIdle[i];
            var labelAt = At(label, Mathf.Round(ArtWidth / 2f - label.rect.width / 2f),
                Mathf.Round(PlateTops[i] + 16f - label.rect.height / 2f));
            // Child of the plate, so it lifts and sinks with it.
            menu.labels[i] = Part("Label", plate.transform, label, 21, labelAt - plate.transform.localPosition);
            menu.plates[i] = plate;
        }

        var cogs = LevelMap.StripFrames($"{MenuDir}menu_cog.png");
        menu.cogFrames = cogs;
        menu.cogLeft = Part("CogLeft", root.transform, cogs.FirstOrDefault(), 22, Vector3.zero);
        menu.cogRight = Part("CogRight", root.transform, cogs.FirstOrDefault(), 22, Vector3.zero);
        menu.cogOffset = new Vector2(-111f, 0f);
        // Beside the first plate (selected at start), raised a pixel with it,
        // so the scene looks right before MainMenu first runs.
        var firstPlate = menu.plates[0].transform.localPosition + new Vector3(0f, 1f / Ppu, 0f);
        menu.cogLeft.transform.localPosition = firstPlate + (Vector3)(menu.cogOffset / Ppu);
        menu.cogRight.transform.localPosition = firstPlate + new Vector3(-menu.cogOffset.x, menu.cogOffset.y, 0f) / Ppu;

        menu.menuCamera = menuCamera;
        menu.screen = screen;
        menu.sideRift = rift;
        menu.ApplyLook();
    }
}
