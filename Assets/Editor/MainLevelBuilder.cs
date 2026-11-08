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
//    Tock's orrery; animated on lane time). A breather (ScenicZone): while
//    both players are in it, both lanes run in real time and the rifts
//    retract.
// 4. Pendulum belts: three belts over drops, a long clock pendulum over
//    each belt's middle (ClockPendulum). The bob shoves a player off the belt
//    as hard as it moves, which is the partner's speed: one crosses while the
//    other slows their lane. The last tile or so of each belt is never hit.
// 5. Piston run: a low corridor whose floor is 16 flush rams that slam up one
//    after another behind the runner (FloorRams). The wave only loses to a
//    runner in a lane at 0.35x, so one stands while the other runs.
// 6. Gauntlet: a crusher, a pit, then belts between crushers in alternating
//    directions, the gap before the last crusher too. The last crusher in
//    each lane has a button in its strike plate; both buttons (any order)
//    open both final gates. Whoever presses first waits on the belt before
//    it, walking against it, which slows the partner's crushers. A death
//    lets the buttons go and shuts the gates.
// Crossing notes (bot-measured timings): DESIGN.md "Main level".
public static class MainLevelBuilder
{
    public const string ScenePath = "Assets/Scenes/MainLevel.unity";
    const int Length = 198;
    // Tiles the pendulum belts section (x 82-117) moved the gauntlet and the
    // ending right by; their coordinates below are written as the old x + P.
    const int P = 37;
    // Tiles the piston run (x 119-143) moved the gauntlet and the ending right
    // by again: their coordinates are the old x + P + R.
    const int R = 25;
    // Piston run: 16 flush rams from x 123 (FloorRams), a 5-tile run-up from
    // the pendulum belts' checkpoint at 118, exit ground with a checkpoint at
    // 141 where the first through waits for the partner.
    const int RunX = 123, RunRams = 16, RunCheckpoint = 141;
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
        // menus (main menu first, the scene the game starts in, then the
        // level select; both stay enabled).
        var scenes = EditorBuildSettings.scenes.ToList();
        string[] menus = { "/MainMenu.unity", "/LevelSelect.unity" };
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

    // Pendulum belts (x 82-117, both lanes alike): entry ground 82-83, three
    // 9-tile belts (84-92, 96-104, 108-116) with 3-tile drops between them,
    // exit ground from 117 with a checkpoint at 118. A clock pendulum hangs
    // over each belt's centre from one beam on posts at 82 and 117: rod 6.5,
    // swing +-36 degrees in 3 lane s, so the bob reaches within 0.7 tiles of
    // the belt's ends but passes over head height there (the last tile is
    // never touched); it is fast and low (0.7 above the belt) in the middle.
    // On the outer 3.5 tiles of each belt (more than 1 from under the axis) a
    // hit is only a nudge (2.5 units/s, under a tile) at any lane speed, so
    // the belt ends never throw anyone off; the middle 2 tiles throw. The
    // belts run slower than the others (PendulumBelt, PendulumBeltFast). A
    // nudged player cannot be thrown by that pendulum for 0.6 s.
    // Bob speed at the bottom 3 units/s at 0.35x (a nudge), 8.6 at 1x, 26 at
    // 3x (thrown off). dirs: each belt's direction; phases: each pendulum's
    // starting point as a share of its swing.
    const float PendulumPeriod = 3f;
    const float PendulumBelt = 1.25f, PendulumBeltFast = 2.5f;   // Conveyor.speed and fastSpeed

    static void PendulumBelts(Lane l, float[] dirs, float[] phases)
    {
        int[] belts = { 84, 96, 108 };
        for (int i = 0; i < belts.Length; i++)
        {
            l.Conveyor(belts[i], belts[i] + 8, dirs[i] * PendulumBelt)
                .GetComponent<Conveyor>().fastSpeed = PendulumBeltFast;
            l.ClockPendulum(belts[i] + 4.5f, phases[i] * PendulumPeriod, period: PendulumPeriod);
        }
        l.PendulumBeam(82.5f, 117.5f, Lane.ClockAxisHeight() + Lane.BeamGap);
        l.Checkpoint(81 + P);
    }

    static void PistonRun(Lane l)
    {
        l.FloorRams(RunX, RunRams);
        l.Checkpoint(RunCheckpoint);
    }

    static void Tick(Lane l)
    {
        l.Floor((-8, 16), (21, 29), (39, 92), (96, 104), (108, 85 + P + R), (91 + P + R, 109 + P + R), (114 + P + R, Length + 8));
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

        // 4. Pendulum belts: belts running right, left, right.
        PendulumBelts(l, new[] { 1f, -1f, 1f }, new[] { 0f, 0.5f, 0.25f });

        // 5. Piston run, the same in both lanes: one runs while the other
        //    stands (the rams only lose to a runner at 0.35x), then they swap.
        PistonRun(l);

        // 6. Gauntlet: crusher straight into a pit, a belt back toward it,
        //    belts between crushers (104-106 + P too: the first to press waits
        //    there), then the last crusher's button with a hole after it and
        //    the gate on the far lip: both cross once both buttons are down
        //    and the gates are up.
        Crusher(l, 82 + P + R);
        l.Conveyor(91 + P + R, 94 + P + R, -Belt);
        Crusher(l, 95 + P + R, 0.9f);
        l.Conveyor(98 + P + R, 100 + P + R, Belt);
        Crusher(l, 101 + P + R, 1.8f);
        l.Conveyor(104 + P + R, 106 + P + R, -Belt);
        Crusher(l, 107 + P + R, 2.7f);
        tickFinal = l.Button(108 + P + R, narrow: true);   // in the strike plate
        tickGate = l.ButtonGate(114 + P + R);

        // 7. Ending.
        l.Checkpoint(118 + P + R);
        l.Decor("lamp", Length - 15.5f);
        l.Door(Length - 12);   // the ending's clock door, placed from the lane's end
        l.Decor("clock", Length - 5.5f);
    }

    static void Tock(Lane l)
    {
        l.Floor((-8, 7), (12, 19), (23, 30), (35, 92), (96, 104), (108, 86 + P + R), (92 + P + R, 110 + P + R), (115 + P + R, Length + 8));
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
        // Tick's range starts past the gate button, Tock's past its gate;
        // both end at the gauntlet's checkpoint.
        new GameObject("ScenicZone").AddComponent<ScenicZone>().ranges =
            new[] { new Vector2(44f, 81f), new Vector2(38f, 81f) };

        // 4. Pendulum belts, belts and swings opposite to Tick's.
        PendulumBelts(l, new[] { -1f, 1f, -1f }, new[] { 0.5f, 0f, 0.75f });

        // 5. Piston run.
        PistonRun(l);

        // 6. Gauntlet, offset from Tick's.
        Crusher(l, 83 + P + R, 0.45f);
        l.Conveyor(92 + P + R, 95 + P + R, -Belt);
        Crusher(l, 96 + P + R, 1.35f);
        l.Conveyor(99 + P + R, 101 + P + R, Belt);
        Crusher(l, 102 + P + R, 2.25f);
        l.Conveyor(105 + P + R, 107 + P + R, -Belt);
        Crusher(l, 108 + P + R, 3.15f);
        var tockFinal = l.Button(109 + P + R, narrow: true);
        var tockGate = l.ButtonGate(115 + P + R);
        WireGate(tockGate, tickFinal, tockFinal);
        WireGate(tickGate, tickFinal, tockFinal);

        // 7. Ending.
        l.Checkpoint(119 + P + R);
        l.Decor("lamp", Length - 15.5f);
        l.Door(Length - 12);
        l.Decor("clock", Length - 5.5f);
    }
}
