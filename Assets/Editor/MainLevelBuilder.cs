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
    const int Length = 172;
    const float Belt = 0.75f;   // units per lane second

    static FloorButton gateButton, tickFinal;
    static ButtonGate tickGate;

    [MenuItem("Tick & Tock/Build Main Level")]
    public static void Build()
    {
        LevelMap.Build(ScenePath, Length, Tick, Tock);
        // The main level is the game: first and only enabled scene in the build.
        var scenes = EditorBuildSettings.scenes.ToList();
        foreach (var s in scenes) s.enabled = s.path == ScenePath;
        EditorBuildSettings.scenes = scenes.Where(s => s.path == ScenePath)
            .Concat(scenes.Where(s => s.path != ScenePath)).ToArray();
    }

    static void Tick(Lane l)
    {
        l.Floor((-8, 29), (34, 51), (61, 120), (126, Length + 8));
        l.Spawn(3);

        // 1. Step up and down, then the trap door Tock has just passed.
        l.Fill(14, 16, 1);
        l.Fill(17, 21, 2);
        l.TrapDoor(30);

        // 2. Drawbridge over a 9-wide pit (too wide to jump). The leaf lands
        //    in the slot at 44-51: the death zone. Tock standing still freezes
        //    it across the pit; the button beyond opens Tock's gate.
        l.Checkpoint(37);
        l.Drawbridge(52, 61);
        l.Busy(42, 61);
        gateButton = l.Button(65);

        // 3. Scenery.
        l.Decor("hedge", 71.5f);
        l.Decor("lamp", 75.5f);
        l.Decor("bench", 78f);
        l.Decor("planter", 82f);
        l.Decor("fountain", 88f);
        l.Decor("planter", 94f);
        l.Decor("lamp", 97.5f);
        l.Decor("topiary", 100.5f);
        l.Decor("hedge", 104.5f);
        l.Decor("gears", 109f);
        l.Checkpoint(113);

        // 4. Gauntlet: crusher, pit, then belts between crushers.
        l.Crusher(116);
        l.Conveyor(126, 129, -Belt);              // pushes back toward the pit
        l.Crusher(130, 0.9f);
        l.Conveyor(133, 135, Belt);
        l.Crusher(136, 1.8f);
        l.Conveyor(139, 141, -Belt);
        l.Crusher(142, 2.7f);
        tickFinal = l.Button(143, narrow: true);   // in the strike plate
        tickGate = l.ButtonGate(147);

        // 5. Ending.
        l.Checkpoint(151);
        l.Decor("lamp", 155.5f);
        l.Decor("clock", 165.5f);
        l.Door(159);
    }

    static void Tock(Lane l)
    {
        l.Floor((-8, 13), (18, 51), (56, 121), (127, Length + 8));
        l.Spawn(3);

        // 1. Trap door first, then the step up and down.
        l.TrapDoor(14);
        l.Fill(28, 30, 1);
        l.Fill(31, 35, 2);

        // 2. Belt toward a pit with a shut gate on its far lip: jumping into
        //    the bars drops Tock into the pit. Tick's button opens it.
        l.Checkpoint(40);
        l.Conveyor(44, 51, Belt);
        var gate = l.ButtonGate(56);
        WireGate(gate, gateButton);

        // 3. Scenery.
        l.Decor("planter", 62f);
        l.Decor("lamp", 66.5f);
        l.Decor("topiary", 70.5f);
        l.Decor("hedge", 75.5f);
        l.Decor("bench", 80f);
        l.Decor("lamp", 84.5f);
        l.Decor("gears", 89f);
        l.Decor("planter", 94f);
        l.Decor("fountain", 100f);
        l.Decor("hedge", 106.5f);
        l.Checkpoint(113);

        // 4. Gauntlet, offset from Tick's.
        l.Crusher(117, 0.45f);
        l.Conveyor(127, 130, -Belt);
        l.Crusher(131, 1.35f);
        l.Conveyor(134, 136, Belt);
        l.Crusher(137, 2.25f);
        l.Conveyor(140, 142, -Belt);
        l.Crusher(143, 3.15f);
        var tockFinal = l.Button(144, narrow: true);
        var tockGate = l.ButtonGate(148);
        WireGate(tockGate, tickFinal, tockFinal);
        WireGate(tickGate, tickFinal, tockFinal);

        // 5. Ending.
        l.Checkpoint(152);
        l.Decor("lamp", 156.5f);
        l.Decor("clock", 165.5f);
        l.Door(159);
    }
}
