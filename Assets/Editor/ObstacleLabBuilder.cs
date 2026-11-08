using System.Linq;
using UnityEditor;
using UnityEngine;
using static LevelMap;

// Obstacle lab: a showcase scene with the five new obstacles (pendulum blade,
// gear fall, tesla gate, steam vent, water wheel), one per section in both
// lanes, each with a checkpoint after it and a label in the ground, so they
// can be played and judged before any goes into the main level. Uses the
// normal level machinery (LevelMap.Build: players, cameras, GameFlow, rifts).
// Added to Build Settings disabled: MainLevel stays the only enabled level.
public static class ObstacleLabBuilder
{
    public const string ScenePath = "Assets/Scenes/ObstacleLab.unity";
    const int Length = 118;

    // Section starts (x) and labels, the same in both lanes.
    static readonly (float x, string label)[] Labels =
    {
        (10f, "PENDULUM"), (30f, "GEAR FALL"), (51f, "TESLA GATE"), (67f, "STEAM VENT"), (84f, "WATER WHEEL"),
    };

    [MenuItem("Tick & Tock/Build Obstacle Lab")]
    public static void Build()
    {
        LevelMap.Build(ScenePath, Length, Lab, Lab);
        foreach (var lane in new[] { 0f, TockLaneY })
            foreach (var (x, label) in Labels)
                Label(label, new Vector3(x, lane - 0.9f, 0f));
        UnityEditor.SceneManagement.EditorSceneManager.SaveScene(
            UnityEngine.SceneManagement.SceneManager.GetActiveScene(), ScenePath);

        // Keep the lab out of the game: disabled, after the enabled scenes.
        var scenes = EditorBuildSettings.scenes.ToList();
        foreach (var s in scenes) if (s.path == ScenePath) s.enabled = false;
        EditorBuildSettings.scenes = scenes.Where(s => s.enabled).Concat(scenes.Where(s => !s.enabled)).ToArray();
        AssetDatabase.SaveAssets();   // else the disabled flag stays unsaved until the next save
    }

    static void Lab(Lane l)
    {
        l.Floor((-8, Length + 8));
        l.Spawn(3);
        l.Decor("lamp", 8f);

        // 1. Pendulum: the blade passes 0.6 above the ground at the bottom of
        //    its swing; run under while it is out to one side.
        l.Pendulum(16);
        l.Checkpoint(26);

        // 2. Gear fall: cogs roll right towards a block 4 high, too high to
        //    jump; stand on a slow cog to jump up there.
        l.Decor("gears", 29f);
        l.GearFall(34);
        l.Fill(41, 44, 4);
        l.Checkpoint(47);

        // 3. Tesla gate, with a lintel so the arc can't be jumped.
        l.Decor("lamp", 50f);
        l.TeslaGate(54, 59);
        l.Checkpoint(63);

        // 4. Steam vent in front of a wall 5 high: ride the steam over it.
        l.Decor("hedge", 66f);
        l.SteamVent(69);
        l.Fill(72, 75, 5);
        l.Checkpoint(78);

        // 5. Water wheel with a ledge 7 high on its right: ride a bucket up
        //    while the partner runs.
        l.Decor("lamp", 82.5f);
        l.WaterWheel(92);
        l.Fill(96, 101, 7);
        l.Checkpoint(104);
        l.Decor("clock", 110f);
    }

    // A white caption on the ground's face below the surface.
    static void Label(string text, Vector3 at)
    {
        var go = new GameObject($"Label_{text.Replace(' ', '_')}");
        go.transform.position = at;
        var mesh = go.AddComponent<TextMesh>();
        mesh.text = text;
        mesh.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        mesh.fontSize = 48;
        mesh.characterSize = 0.06f;
        mesh.anchor = TextAnchor.MiddleLeft;
        mesh.color = new Color(1f, 0.95f, 0.8f);
        var r = go.GetComponent<MeshRenderer>();
        r.sharedMaterial = mesh.font.material;
        r.sortingOrder = 30;
    }
}
