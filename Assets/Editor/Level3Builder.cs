using UnityEditor;
using static LevelMap;

// Level 3 "The Bell Tower". Map only; see LevelMap.
// New: laser gates (freeze in the "off" phase) and patrolling guard robots
// (freeze and slip past). Combines them with crushers, a drawbridge, a vine
// and a waterfall from Level 1.
public static class Level3Builder
{
    public const string ScenePath = "Assets/Scenes/Level3_BellTower.unity";
    const int Length = 190;

    [MenuItem("Tick & Tock/Build Level 3 Map")]
    public static void Build() => LevelMap.Build(ScenePath, Length, Tick, Tock);

    static void Tick(Lane l)
    {
        l.Floor((-8, 70), (78, Length + 8));
        l.Spawn(3);
        l.Checkpoint(1);

        // 1. Two laser gates: Tock freezes each in its off phase.
        l.LaserGate(20);
        l.LaserGate(34);

        // 2. Guard robot patrolling x 45-60.
        l.Marker("GuardRobot", 50, 0, 1.5f, 2, Guard);
        l.Marker("PatrolPath", 45, 0, 15, 0.1f, Guard);

        // 3. Drawbridge over a pit; Tock pumps time on a treadmill.
        l.Checkpoint(64);
        l.Marker("Drawbridge", 70, 0, 1, 7, Wood);
        l.Busy(70, 78);

        // 4. Crusher, laser, crusher: alternate with Tock's lane.
        l.Checkpoint(82);
        l.Crusher(95);
        l.LaserGate(106);
        l.Crusher(115);

        // 5. Vine up the bell tower base, then the door on top.
        l.Fill(130, 150, 4);
        l.Marker("Vine", 129, 0, 1, 4, Vine);
        l.Ledge(153, 158, 2);
        l.Checkpoint(162);
        l.Door(178);
    }

    static void Tock(Lane l)
    {
        l.Floor((-8, 99), (109, Length + 8));
        l.Spawn(3);
        l.Checkpoint(1);

        // 1. Rest pad: stand still so Tick's lasers freeze.
        l.RestPad(16);
        l.Marker("Treadmill", 26, 0, 6, 0.4f, Treadmill);

        // 2. Guard robot and a laser gate of Tock's own.
        l.Marker("GuardRobot", 42, 0, 1.5f, 2, Guard);
        l.Marker("PatrolPath", 38, 0, 12, 0.1f, Guard);
        l.LaserGate(56);

        // 3. Treadmill to drop Tick's drawbridge.
        l.Checkpoint(64);
        l.Marker("Treadmill", 68, 0, 7, 0.4f, Treadmill);

        // 4. Crusher, then a waterfall over a pit (freeze to cross).
        l.Crusher(88);
        l.Fill(97, 111, 10, 8);
        l.Marker("Waterfall", 100, -4, 8, 12, Water);

        // 5. Laser, robot, crusher, door.
        l.Checkpoint(114);
        l.LaserGate(125);
        l.Marker("GuardRobot", 140, 0, 1.5f, 2, Guard);
        l.Marker("PatrolPath", 135, 0, 12, 0.1f, Guard);
        l.Crusher(152);
        l.Checkpoint(164);
        l.Door(178);
    }
}
