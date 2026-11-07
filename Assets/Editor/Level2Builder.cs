using UnityEditor;
using static LevelMap;

// Level 2 "The Aqueduct". Map only; see LevelMap.
// New: obstacles that need the partner moving steadily (~1x): moving
// platforms, pendulums and a conveyor. Revisits falling gears and a crusher.
public static class Level2Builder
{
    public const string ScenePath = "Assets/Scenes/Level2_Aqueduct.unity";
    const int Length = 184;

    [MenuItem("Tick & Tock/Build Level 2 Map")]
    public static void Build() => LevelMap.Build(ScenePath, Length, Tick, Tock);

    static void Tick(Lane l)
    {
        l.Floor((-8, 30), (42, 80), (96, 125), (140, Length + 8));
        l.Spawn(3);
        l.Checkpoint(1);

        // 1. Moving platform over a pit; Tock pumps time on a treadmill.
        l.Marker("MovingPlatform", 31, -0.5f, 3, 0.5f, Platform);    // travels to x 38
        l.Busy(30, 42);

        // 2. Walk steadily over the ledges while Tock rides the pendulum
        //    (standing still would stop it mid-swing).
        l.Checkpoint(44);
        l.RestPad(48);
        l.Ledge(66, 72, 2);

        // 3. Pendulum swing across a wide gap.
        l.Fill(86, 90, 10, 8);                    // anchor beam
        l.Marker("Pendulum", 87.85f, 1, 0.3f, 7, Pendulum);
        l.Marker("PendulumSeat", 87, 0.5f, 2, 0.5f, Pendulum);
        l.Busy(80, 96);

        // 4. Conveyor only carries Tick while Tock keeps moving.
        l.Checkpoint(98);
        l.Marker("Conveyor", 105, 0, 12, 0.4f, Conveyor);

        // 5. Two platforms in sequence: one sliding, one rising.
        l.Marker("MovingPlatform", 127, -0.5f, 3, 0.5f, Platform);   // slides to x 131
        l.Marker("MovingPlatform", 134, 0, 3, 0.5f, Platform);       // rises to y 3
        l.Busy(125, 140);
        l.Checkpoint(142);
        l.Fill(150, 160, 3);
        l.Door(172);
    }

    static void Tock(Lane l)
    {
        l.Floor((-8, 55), (70, 110), (122, Length + 8));
        l.Spawn(3);
        l.Checkpoint(1);

        // 1. Treadmill: run in place to drive Tick's moving platform.
        l.Marker("Treadmill", 18, 0, 7, 0.4f, Treadmill);

        // 2. Pendulum over a pit: only swings while Tick keeps walking.
        l.Checkpoint(44);
        l.Fill(60, 64, 10, 8);
        l.Marker("Pendulum", 61.85f, 1, 0.3f, 7, Pendulum);
        l.Marker("PendulumSeat", 61, 0.5f, 2, 0.5f, Pendulum);
        l.Busy(55, 70);

        // 3. Falling gears again, onto a lower wall.
        l.Marker("FallingGears", 76, 9, 10, 1, Gear);
        l.Fill(88, 100, 4);
        l.Prop(89, 4, 1, 1);

        // 4. Moving platform while Tick rides the conveyor.
        l.Checkpoint(102);
        l.Marker("MovingPlatform", 111, -0.5f, 3, 0.5f, Platform);   // travels to x 118
        l.Busy(110, 122);

        // 5. A crusher, then the door.
        l.Crusher(135);
        l.RestPad(150);
        l.Door(172);
    }
}
