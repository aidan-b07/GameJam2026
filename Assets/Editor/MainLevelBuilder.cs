using System.Linq;
using UnityEditor;
using UnityEngine;
using static LevelMap;

// The main level (Samuel's sketch, 2026-11-08; DESIGN.md "Main level").
// Levels 1 and 2 are kept but left out of the build. Map only; see LevelMap.
// Each lane's obstacle sits next to one in the other lane that needs the
// opposite from the partner (moving runs the partner's lane at up to 3x,
// standing still slows it to 0.35x), while the rift (LaneRift) keeps a
// player from standing still for more than about 5 s:
// 1. Opening: Tick climbs a step while Tock passes a trap door, then they swap.
// 2. Bridge and gate: Tick crosses the drawbridge (the winch holds the leaf
//    across the pit for spanPause lane seconds; Tock stands still when it
//    starts to winch, so the hold lasts seconds instead of a blink) and
//    presses the button that opens the gate on the far lip of Tock's pit.
//    Tock's belt runs toward that pit whenever Tick moves.
// 3. Scenery: decor only, with a landmark in each lane (Tick's fountain,
//    Tock's orrery; animated on lane time).
// 4. Gauntlet: a crusher, a pit, then belts between crushers in alternating
//    directions and a plain gap before the last crusher. The last crusher in
//    each lane has a button in its strike plate; both buttons (any order)
//    open both final gates. Whoever presses first waits in the plain gap,
//    standing still, which slows the partner's crushers. A death lets the
//    buttons go and shuts the gates.
// Crossing notes (bot-measured timings): DESIGN.md "Main level".
public static class MainLevelBuilder
{
    public const string ScenePath = "Assets/Scenes/MainLevel.unity";
    const int Length = 136;
    const float Belt = 2f;   // units per second at the idle lane speed (Conveyor.fastSpeed at full)
    const float SpanPause = 2.5f;   // lane seconds the drawbridge holds across its pit
    // Lane seconds each crusher waits raised (PlungerMotion default 2): at 3x
    // the gap under a head lasts about 1.4 s, enough for a 0.4 s reaction.
    const float CrusherPause = 3.5f;

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

    static void Crusher(Lane l, int x, float offset = 0f)
    {
        var motion = new SerializedObject(l.Crusher(x, offset).GetComponent<PlungerMotion>());
        motion.FindProperty("raisedPause").floatValue = CrusherPause;
        motion.ApplyModifiedPropertiesWithoutUndo();
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
        //    frame, which starts at x 16). The 9-wide pit is too wide to jump.
        //    The winch holds the leaf across the pit for SpanPause lane
        //    seconds: 0.8 s at 3x (too short to cross 9 tiles), 2.3 s when
        //    Tock stands still as it starts to winch, longer if Tock stops
        //    before. The button beyond
        //    opens Tock's gate.
        var bridge = new SerializedObject(l.Drawbridge(30, 39).GetComponent<BridgeMovement>());
        bridge.FindProperty("spanPause").floatValue = SpanPause;
        bridge.ApplyModifiedPropertiesWithoutUndo();
        l.Checkpoint(21, lampX: 20f / 32f, lampBehind: true);
        gateButton = l.Button(41);

        // 3. Scenery.
        l.Decor("hedge", 46.5f);
        l.Decor("lamp", 49.5f);
        l.Decor("bench", 52f);
        l.Landmark("fountain", 59f, 256);   // Grand Clockwork Fountain, x 55-63
        l.Decor("planter", 65f);
        l.Decor("lamp", 67.5f);
        l.Decor("topiary", 70.5f);
        l.Decor("hedge", 74.5f);
        l.Decor("gears", 78f);
        l.Checkpoint(81);

        // 4. Gauntlet: crusher straight into a pit, a belt back toward it,
        //    belts between crushers, a plain gap (104-106: the first to press
        //    waits there), then the last crusher's button with a hole after it
        //    and the gate on the far lip: both cross once both buttons are
        //    down and the gates are up.
        Crusher(l, 82);
        l.Conveyor(91, 94, -Belt);
        Crusher(l, 95, 0.9f);
        l.Conveyor(98, 100, Belt);
        Crusher(l, 101, 1.8f);
        Crusher(l, 107, 2.7f);
        tickFinal = l.Button(108, narrow: true);   // in the strike plate
        tickGate = l.ButtonGate(114);

        // 5. Ending.
        l.Checkpoint(118);
        l.Decor("lamp", Length - 15.5f);
        l.Door(Length - 12);   // the ending's clock door, placed from the lane's end
        l.Decor("clock", Length - 5.5f);
    }

    static void Tock(Lane l)
    {
        l.Floor((-8, 7), (12, 19), (23, 30), (35, 86), (92, 110), (115, Length + 8));
        l.Spawn(3);

        // 1. Trap door, then a ledge higher than a jump that drops straight
        //    onto the belt. The checkpoint is on the ledge, the last safe spot.
        //    The ledge's right half overhangs a hole at the belt's start, so
        //    walking left against the belt drops Tock in instead of letting
        //    them lean on a wall (moving 0 keeps Tick's lane slow).
        l.TrapDoor(8);
        l.Fill(15, 17, 2);
        l.Fill(18, 19, 4);
        l.Fill(20, 22, 4, bottom: 2);
        l.Checkpoint(19);

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
        l.Landmark("orrery", 72f, 192);     // Founders' Orrery, x 69-75
        l.Decor("hedge", 77.5f);
        l.Checkpoint(81);

        // 4. Gauntlet, offset from Tick's.
        Crusher(l, 83, 0.45f);
        l.Conveyor(92, 95, -Belt);
        Crusher(l, 96, 1.35f);
        l.Conveyor(99, 101, Belt);
        Crusher(l, 102, 2.25f);
        Crusher(l, 108, 3.15f);
        var tockFinal = l.Button(109, narrow: true);
        var tockGate = l.ButtonGate(115);
        WireGate(tockGate, tickFinal, tockFinal);
        WireGate(tickGate, tickFinal, tockFinal);

        // 5. Ending.
        l.Checkpoint(119);
        l.Decor("lamp", Length - 15.5f);
        l.Door(Length - 12);
        l.Decor("clock", Length - 5.5f);
    }
}
