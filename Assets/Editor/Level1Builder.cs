using UnityEditor;
using static LevelMap;

// Level 1 "The Clockworks" (DESIGN.md, Level 1 sketch). Map only; see LevelMap.
// Teaches: crusher (freeze), treadmill + drawbridge (fast), falling gears
// (freeze), alternation, then a vine (fast) and waterfall (freeze) finale.
public static class Level1Builder
{
    public const string ScenePath = "Assets/Scenes/Level1_Clockworks.unity";
    const int Length = 200;

    [MenuItem("Tick & Tock/Build Level 1 Map")]
    public static void Build() => LevelMap.Build(ScenePath, Length, Tick, Tock);

    static void Tick(Lane l)
    {
        l.Floor((-8, Length + 8));
        l.Spawn(3);

        // 1. Tutorial A: crusher. Tock must STOP to freeze it.
        l.Crusher(22);

        // 2. Tutorial B: treadmill. Tick runs on it to drop Tock's drawbridge.
        l.Checkpoint(40);
        l.Marker("Treadmill", 47, 0, 7, 0.4f, Treadmill);
        l.Fill(58, 63, 1);                        // small step

        // 3. Frozen gears: Tick stops while Tock climbs. Rest pad to wait on.
        l.RestPad(70);
        l.Ledge(77, 82, 2);
        l.Fill(86, 91, 2);

        // 4. Alternation: crushers offset against Tock's (116, 132, 148).
        l.Checkpoint(98);
        l.Crusher(108);
        l.Crusher(124);
        l.Crusher(140);

        // 5. Finale: vine grows to the high ground (Tick needs fast time).
        l.Checkpoint(152);
        l.Fill(166, Length + 8, 5);
        l.Marker("Vine", 165, 0, 1, 5, Vine);
        l.Door(186);
    }

    static void Tock(Lane l)
    {
        l.Floor((-8, 49), (57, 159), (169, Length + 8));
        l.Spawn(3);

        // 1. Tutorial A: rest pad. "Tock: STOP".
        l.RestPad(19);

        // 2. Tutorial B: slow drawbridge over a pit (needs Tick on the treadmill).
        l.Checkpoint(40);
        l.Drawbridge(50, 57);                     // swings past the pit; stop time to cross
        l.Busy(48, 57);                           // no props at the pit edges

        // 3. Frozen gears: gears fall from the top; freeze them and climb the wall.
        l.Marker("FallingGears", 66, 9, 10, 1, Gear);
        l.Fill(78, 94, 5);
        l.Prop(79, 5, 1, 2);                      // big gear on the wall top
        l.Fill(95, 97, 2);                        // step down

        // 4. Alternation: crushers offset against Tick's (108, 124, 140).
        l.Checkpoint(100);
        l.Crusher(116);
        l.Crusher(132);
        l.Crusher(148);

        // 5. Finale: waterfall over a pit; frozen water is a bridge (Tock needs freeze).
        l.Checkpoint(152);
        l.Fill(157, 170, 10, 8);                  // overhang the water falls from
        l.Marker("Waterfall", 160, -4, 9, 12, Water);
        l.Door(186);
    }
}
