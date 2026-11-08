using System.Linq;
using UnityEditor;
using UnityEngine;
using static LevelMap;

// The main level (Samuel's sketch, 2026-11-08; DESIGN.md "Main level").
// Levels 1 and 2 are kept but left out of the build. Map only; see LevelMap.
// Each lane's obstacle sits next to one in the other lane that needs the
// opposite from the partner (moving speeds up the partner's lane, standing
// still freezes it):
// 1. Opening: Tick climbs a step while Tock passes a trap door, then they swap.
// 2. Bridge and gate: Tick crosses the drawbridge (Tock must stand still so
//    the leaf stops across the pit) and presses the button that opens the gate
//    on the far lip of Tock's pit. Tock's belt runs toward that pit whenever
//    Tick moves.
// 3. Scenery: decor only.
// 4. Gauntlet: a crusher, a pit, then belts between crushers in alternating
//    directions. The last crusher in each lane has a button in its strike
//    plate; both buttons (any order) open both final gates. A death lets the
//    buttons go and shuts the gates.
public static class MainLevelBuilder
{
    public const string ScenePath = "Assets/Scenes/MainLevel.unity";
    const int Length = 136;
    const float Belt = 0.75f;   // units per lane second

    static FloorButton gateButton, tickFinal;
    static ButtonGate tickGate;

    [MenuItem("Tick & Tock/Build Main Level")]
    public static void Build()
    {
        LevelMap.Build(ScenePath, Length, Tick, Tock);
        // The main level is the game: the only enabled level, right after the
        // menus (level select first, the scene the game starts in, then the
        // main menu; both stay enabled).
        var scenes = EditorBuildSettings.scenes.ToList();
        string[] menus = { "/LevelSelect.unity", "/MainMenu.unity" };
        int MenuRank(EditorBuildSettingsScene s) => System.Array.FindIndex(menus, m => s.path.EndsWith(m));
        bool IsMenu(EditorBuildSettingsScene s) => MenuRank(s) >= 0;
        foreach (var s in scenes) s.enabled = s.path == ScenePath || IsMenu(s);
        EditorBuildSettings.scenes = scenes.Where(IsMenu).OrderBy(MenuRank)
            .Concat(scenes.Where(s => s.path == ScenePath))
            .Concat(scenes.Where(s => s.path != ScenePath && !IsMenu(s))).ToArray();
    }

    static void Tick(Lane l)
    {
        l.Floor((-8, 16), (21, 29), (39, 85), (91, 109), (114, Length + 8));
        l.Spawn(3);

        // 1. Step up to a ledge higher than a jump (no way back), dropping
        //    straight onto the trap door.
        l.Fill(8, 10, 2);
        l.Fill(11, 15, 4);
        l.TrapDoor(17);

        // 2. Off the trap door straight into the drawbridge's slot (22-29),
        //    where the leaf lands: no safe spot between them. The checkpoint
        //    is on the one-tile sliver between them (x 21), its lamp behind
        //    the trap door's right lip lamp (pixel 180 of the trap door's
        //    frame, which starts at x 16). The 9-wide pit is too wide to jump;
        //    Tock standing still freezes the leaf across it. The button beyond
        //    opens Tock's gate.
        l.Drawbridge(30, 39);
        l.Checkpoint(21, lampX: 20f / 32f, lampBehind: true);
        gateButton = l.Button(41);

        // 3. Scenery.
        l.Decor("hedge", 46.5f);
        l.Decor("lamp", 49.5f);
        l.Decor("bench", 52f);
        l.Decor("planter", 55f);
        l.Decor("fountain", 60f);
        l.Decor("planter", 65f);
        l.Decor("lamp", 67.5f);
        l.Decor("topiary", 70.5f);
        l.Decor("hedge", 74.5f);
        l.Decor("gears", 78f);
        l.Checkpoint(81);

        // 4. Gauntlet: crusher straight into a pit, a belt back toward it,
        //    belts between crushers, then the last crusher's button with a hole
        //    after it and the gate on the far lip: both wait under their
        //    crushers until both buttons are down and the gates are up.
        l.Crusher(82);
        l.Conveyor(91, 94, -Belt);
        l.Crusher(95, 0.9f);
        l.Conveyor(98, 100, Belt);
        l.Crusher(101, 1.8f);
        l.Conveyor(104, 106, -Belt);
        l.Crusher(107, 2.7f);
        tickFinal = l.Button(108, narrow: true);   // in the strike plate
        tickGate = l.ButtonGate(114);

        // 5. Ending.
        l.Checkpoint(118);
        l.Decor("lamp", 121.5f);
        l.Door(124);
        l.Decor("clock", 130.5f);
    }

    static void Tock(Lane l)
    {
        l.Floor((-8, 7), (12, 30), (35, 86), (92, 110), (115, Length + 8));
        l.Spawn(3);

        // 1. Trap door, then a ledge higher than a jump that drops straight
        //    onto the belt. The checkpoint is on the ledge, the last safe spot.
        l.TrapDoor(8);
        l.Fill(15, 17, 2);
        l.Fill(18, 22, 4);
        l.Checkpoint(20);

        // 2. Belt toward a pit with a shut gate on its far lip: jumping into
        //    the bars drops Tock into the pit. Tick's button opens it.
        l.Conveyor(23, 30, Belt);
        var gate = l.ButtonGate(35);
        WireGate(gate, gateButton);

        // 3. Scenery.
        l.Decor("planter", 41f);
        l.Decor("lamp", 44.5f);
        l.Decor("topiary", 47.5f);
        l.Decor("hedge", 51.5f);
        l.Decor("bench", 56f);
        l.Decor("lamp", 59.5f);
        l.Decor("gears", 63f);
        l.Decor("planter", 67f);
        l.Decor("fountain", 72f);
        l.Decor("hedge", 77.5f);
        l.Checkpoint(81);

        // 4. Gauntlet, offset from Tick's.
        l.Crusher(83, 0.45f);
        l.Conveyor(92, 95, -Belt);
        l.Crusher(96, 1.35f);
        l.Conveyor(99, 101, Belt);
        l.Crusher(102, 2.25f);
        l.Conveyor(105, 107, -Belt);
        l.Crusher(108, 3.15f);
        var tockFinal = l.Button(109, narrow: true);
        var tockGate = l.ButtonGate(115);
        WireGate(tockGate, tickFinal, tockFinal);
        WireGate(tickGate, tickFinal, tockFinal);

        // 5. Ending.
        l.Checkpoint(119);
        l.Decor("lamp", 121.5f);
        l.Door(124);
        l.Decor("clock", 130.5f);
    }
}
