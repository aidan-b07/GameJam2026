using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.U2D.Sprites;
using UnityEngine;
using UnityEngine.Tilemaps;

// Imports the pixel-art sheets from unity-tiles/ (copied into Assets/Art).
// Backgrounds (including the _day and _night variants) and the sun and moon
// in Assets/Art/Sky are imported as single sprites; Assets/Art/UI textures
// (lane divider) as point-filtered, repeating textures.
// Tick & Tock > Import Art: sets point-filter sprite settings, slices the tile
// sheets into a grid (sprites named <sheet>_r<row>c<col>, row 0 at the top),
// creates one Tile asset per non-empty cell and a Tile Palette per sheet.
public static class ArtImporter
{
    const string TilesDir = "Assets/Art/Tiles";
    const string BackgroundsDir = "Assets/Art/Backgrounds";
    const string SkyDir = "Assets/Art/Sky";
    const string UiDir = "Assets/Art/UI";
    const string GeneratedDir = "Assets/Art/Tiles/Generated";
    const string PalettesDir = "Assets/Art/Tiles/Palettes";

    [MenuItem("Tick & Tock/Import Art")]
    public static void ImportAll()
    {
        foreach (var path in PngsIn(TilesDir))
            ImportSheet(path);
        foreach (var path in PngsIn(BackgroundsDir))
            ImportBackground(path);
        foreach (var path in PngsIn(SkyDir))
            ImportSky(path);
        foreach (var path in PngsIn(UiDir))
            ImportUi(path);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log("[ArtImporter] Art import finished.");
    }

    static IEnumerable<string> PngsIn(string dir) =>
        AssetDatabase.FindAssets("t:Texture2D", new[] { dir })
            .Select(AssetDatabase.GUIDToAssetPath)
            .Where(p => p.EndsWith(".png") && Path.GetDirectoryName(p).Replace('\\', '/') == dir);

    // "ground_16px" -> 16
    static int CellSize(string path)
    {
        var name = Path.GetFileNameWithoutExtension(path);
        return name.EndsWith("_32px") ? 32 : 16;
    }

    static void ApplyPixelSettings(TextureImporter ti, int ppu)
    {
        ti.textureType = TextureImporterType.Sprite;
        ti.spritePixelsPerUnit = ppu;
        ti.filterMode = FilterMode.Point;
        ti.textureCompression = TextureImporterCompression.Uncompressed;
        ti.mipmapEnabled = false;
        ti.alphaIsTransparency = true;
    }

    static void ImportSheet(string path)
    {
        int cell = CellSize(path);
        var ti = (TextureImporter)AssetImporter.GetAtPath(path);
        ApplyPixelSettings(ti, cell);
        ti.spriteImportMode = SpriteImportMode.Multiple;
        ti.wrapMode = TextureWrapMode.Clamp;
        ti.isReadable = true;
        ti.SaveAndReimport();

        var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        string sheet = Path.GetFileNameWithoutExtension(path);
        int cols = tex.width / cell, rows = tex.height / cell;

        var factory = new SpriteDataProviderFactories();
        factory.Init();
        var provider = factory.GetSpriteEditorDataProviderFromObject(ti);
        provider.InitSpriteEditorDataProvider();

        var rects = new List<SpriteRect>();
        for (int r = 0; r < rows; r++)
        for (int c = 0; c < cols; c++)
        {
            // Texture y runs bottom-up; row 0 is the top row of the image.
            var rect = new Rect(c * cell, tex.height - (r + 1) * cell, cell, cell);
            if (IsEmpty(tex, rect)) continue;
            rects.Add(new SpriteRect
            {
                name = $"{sheet}_r{r}c{c}",
                rect = rect,
                alignment = SpriteAlignment.Center,
                pivot = new Vector2(0.5f, 0.5f),
                spriteID = GUID.Generate(),
            });
        }
        provider.SetSpriteRects(rects.ToArray());
        var names = provider.GetDataProvider<ISpriteNameFileIdDataProvider>();
        names?.SetNameFileIdPairs(rects.Select(r => new SpriteNameFileIdPair(r.name, r.spriteID)));
        provider.Apply();

        ti.isReadable = false;
        ti.SaveAndReimport();

        CreateTilesAndPalette(path, sheet, cell);
    }

    static bool IsEmpty(Texture2D tex, Rect rect)
    {
        var px = tex.GetPixels((int)rect.x, (int)rect.y, (int)rect.width, (int)rect.height);
        return px.All(p => p.a < 0.01f);
    }

    static void CreateTilesAndPalette(string path, string sheet, int cell)
    {
        string dir = $"{GeneratedDir}/{sheet}";
        EnsureFolder(dir);
        var sprites = AssetDatabase.LoadAllAssetsAtPath(path).OfType<Sprite>().ToList();
        var placed = new List<(Vector3Int pos, TileBase tile)>();
        foreach (var sprite in sprites)
        {
            string tilePath = $"{dir}/{sprite.name}.asset";
            var tile = AssetDatabase.LoadAssetAtPath<Tile>(tilePath);
            if (tile == null)
            {
                tile = ScriptableObject.CreateInstance<Tile>();
                AssetDatabase.CreateAsset(tile, tilePath);
            }
            tile.sprite = sprite;
            tile.colliderType = Tile.ColliderType.Sprite;
            EditorUtility.SetDirty(tile);

            // Palette layout mirrors the sheet: row 0 at the top.
            var rc = sprite.name.Split('_').Last().Substring(1).Split('c');
            int r = int.Parse(rc[0]), c = int.Parse(rc[1]);
            placed.Add((new Vector3Int(c, -r, 0), tile));
        }
        CreatePalette(sheet, placed);
    }

    static void CreatePalette(string sheet, List<(Vector3Int pos, TileBase tile)> tiles)
    {
        EnsureFolder(PalettesDir);
        string palettePath = $"{PalettesDir}/{sheet}.prefab";
        if (AssetDatabase.LoadAssetAtPath<GameObject>(palettePath) != null)
            AssetDatabase.DeleteAsset(palettePath);

        var paletteGO = UnityEditor.Tilemaps.GridPaletteUtility.CreateNewPalette(
            PalettesDir, sheet, GridLayout.CellLayout.Rectangle,
            UnityEditor.GridPalette.CellSizing.Automatic, Vector3.one,
            GridLayout.CellSwizzle.XYZ);
        string createdPath = AssetDatabase.GetAssetPath(paletteGO);

        var root = PrefabUtility.LoadPrefabContents(createdPath);
        var tilemap = root.GetComponentInChildren<Tilemap>();
        foreach (var (pos, tile) in tiles)
            tilemap.SetTile(pos, tile);
        PrefabUtility.SaveAsPrefabAsset(root, createdPath);
        PrefabUtility.UnloadPrefabContents(root);
    }

    static void ImportBackground(string path)
    {
        var ti = (TextureImporter)AssetImporter.GetAtPath(path);
        // Backgrounds are 180 or 360 px tall; PPU keeps them 11.25 units tall either way.
        int ppu = path.Contains("_360h") ? 32 : 16;
        ApplyPixelSettings(ti, ppu);
        ti.spriteImportMode = SpriteImportMode.Single;
        ti.wrapMode = TextureWrapMode.Repeat;
        // Tiled draw mode needs a full-rect mesh.
        var settings = new TextureImporterSettings();
        ti.ReadTextureSettings(settings);
        settings.spriteMeshType = SpriteMeshType.FullRect;
        ti.SetTextureSettings(settings);
        ti.SaveAndReimport();
    }

    // Sun and moon: single sprites at 32 PPU, the same scale as the 360h backgrounds.
    static void ImportSky(string path)
    {
        var ti = (TextureImporter)AssetImporter.GetAtPath(path);
        ApplyPixelSettings(ti, 32);
        ti.spriteImportMode = SpriteImportMode.Single;
        ti.wrapMode = TextureWrapMode.Clamp;
        ti.SaveAndReimport();
    }

    // Screen overlay textures (the lane divider): point filter, repeat so they tile.
    static void ImportUi(string path)
    {
        var ti = (TextureImporter)AssetImporter.GetAtPath(path);
        ApplyPixelSettings(ti, 32);
        ti.spriteImportMode = SpriteImportMode.Single;
        ti.wrapMode = TextureWrapMode.Repeat;
        ti.SaveAndReimport();
    }

    static void EnsureFolder(string path)
    {
        if (AssetDatabase.IsValidFolder(path)) return;
        string parent = Path.GetDirectoryName(path).Replace('\\', '/');
        EnsureFolder(parent);
        AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
    }
}
