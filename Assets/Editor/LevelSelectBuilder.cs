using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// Builds the level select scene (Tick & Tock > Build Level Select), the scene
// the game starts in. Same stage as the main menu (MainMenuBuilder.BuildStage:
// scrolling backdrop, day cycle, side rift, 640x360 screen) with eight level
// cards and a Back plate on it.
//
// Art from tools/menu/make_level_select.py in Assets/Art/UI/LevelSelect: open
// cards (card_<n>_idle / _sel) for the levels in Levels, locked "coming soon"
// cards (card_<n>_locked) for the rest. Layout (art pixels, origin top-left):
//   header   "Select Level", centred, top at y 14
//   cards    116x100 in a 4x2 grid, 16 px apart, left edges at x 64, 196, 328, 460,
//            tops at y 70 and 180
//   back     64x22 plate, centred, top at y 288
public static class LevelSelectBuilder
{
    public const string ScenePath = "Assets/Scenes/LevelSelect.unity";
    const string Dir = "Assets/Art/UI/LevelSelect/";
    const int Cards = 8, Columns = 4;
    const int CardW = 116, CardH = 100, Gap = 16;
    static readonly int[] RowTops = { 70, 180 };
    const int HeaderTop = 14, BackTop = 288;
    // Card art canvas: 140x120 with the card's top-left at (12, 10); back: 84x34 with the plate at (10, 6).
    static readonly Vector2Int CardInset = new Vector2Int(12, 10), BackInset = new Vector2Int(10, 6);

    // Playable levels in card order; every card after these is locked (coming soon).
    static readonly string[] Levels = { "Level1_Clockworks", "Level2_Foundry" };

    [MenuItem("Tick & Tock/Build Level Select")]
    public static void Build()
    {
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        var stage = MainMenuBuilder.BuildStage();
        PrepareArt();

        var root = new GameObject("LevelSelect");
        var select = root.AddComponent<LevelSelect>();
        SpriteRenderer Part(string name, Sprite sprite, int order, Vector3 at)
        {
            var go = new GameObject(name);
            go.transform.SetParent(root.transform, false);
            go.transform.localPosition = at;
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = sprite;
            sr.sortingOrder = order;
            return sr;
        }

        var header = Sprite("header_select_level");
        select.header = Part("Header", header, 20,
            MainMenuBuilder.At(header, Mathf.Round(MainMenuBuilder.ArtWidth / 2f - header.rect.width / 2f), HeaderTop));

        int gridLeft = (MainMenuBuilder.ArtWidth - (Columns * CardW + (Columns - 1) * Gap)) / 2;
        select.cards = new LevelSelect.Card[Cards];
        for (int i = 0; i < Cards; i++)
        {
            int n = i + 1;
            bool open = i < Levels.Length;
            var card = new LevelSelect.Card
            {
                idle = open ? Sprite($"card_{n}_idle") : null,
                selected = open ? Sprite($"card_{n}_sel") : null,
                locked = open ? null : Sprite($"card_{n}_locked"),
                scene = open ? Levels[i] : "",
                size = new Vector2(CardW, CardH),
            };
            var shown = open ? card.idle : card.locked;
            int x = gridLeft + i % Columns * (CardW + Gap), y = RowTops[i / Columns];
            card.renderer = Part(open ? $"Card_{n}_{Levels[i]}" : $"Card_{n}_ComingSoon", shown, 20,
                MainMenuBuilder.At(shown, x - CardInset.x, y - CardInset.y));
            select.cards[i] = card;
        }

        select.backIdle = Sprite("back_idle");
        select.backSelected = Sprite("back_sel");
        select.back = Part("Back", select.backIdle, 20, MainMenuBuilder.At(select.backIdle,
            (MainMenuBuilder.ArtWidth - 64) / 2 - BackInset.x, BackTop - BackInset.y));
        select.backSize = new Vector2(64f, 22f);
        select.backScene = "MainMenu";

        var cogs = LevelMap.StripFrames($"{MainMenuBuilder.MenuDir}menu_cog.png");
        select.cogFrames = cogs;
        select.cogLeft = Part("CogLeft", cogs.FirstOrDefault(), 22, Vector3.zero);
        select.cogRight = Part("CogRight", cogs.FirstOrDefault(), 22, Vector3.zero);
        // Beside the first card (selected at start), raised a pixel with it,
        // so the scene looks right before LevelSelect first runs.
        float px = 1f / MainMenuBuilder.Ppu;
        // On its bottom corners, as LevelSelect places them on a selected card.
        var first = select.cards[0].renderer.transform.localPosition + new Vector3(0f, px, 0f);
        var corner = new Vector2(CardW, CardH) / 2f - Vector2.one * select.cogInset;
        select.cogLeft.transform.localPosition = first + new Vector3(-corner.x, -corner.y, 0f) * px;
        select.cogRight.transform.localPosition = first + new Vector3(corner.x, -corner.y, 0f) * px;

        select.menuCamera = stage.camera;
        select.screen = stage.screen;
        select.sideRift = stage.rift;
        select.selected = 0;
        select.ApplyLook();

        EditorSceneManager.SaveScene(scene, ScenePath);
        LevelMap.AddToBuildSettings(ScenePath);
        Debug.Log($"[LevelSelectBuilder] Saved {ScenePath}");
    }

    static void PrepareArt()
    {
        foreach (var path in AssetDatabase.FindAssets("t:Texture2D", new[] { Dir.TrimEnd('/') })
                     .Select(AssetDatabase.GUIDToAssetPath).Where(p => p.EndsWith(".png")))
        {
            var ti = (TextureImporter)AssetImporter.GetAtPath(path);
            ti.GetSourceTextureWidthAndHeight(out int w, out int h);
            // Centre pivot rounded down to a whole pixel, so odd sizes stay on the pixel grid.
            LevelMap.PrepareSprite(path, false, new Vector2(Mathf.Floor(w / 2f) / w, Mathf.Floor(h / 2f) / h));
        }
    }

    static Sprite Sprite(string name)
    {
        var s = AssetDatabase.LoadAssetAtPath<Sprite>($"{Dir}{name}.png");
        if (s == null) Debug.LogError($"[LevelSelectBuilder] Missing {Dir}{name}.png; run tools/menu/make_level_select.py.");
        return s;
    }
}
