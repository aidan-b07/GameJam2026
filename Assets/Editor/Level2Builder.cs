using UnityEditor;
using static LevelMap;

// Level 2 "The Foundry". Map only; see LevelMap. Denser than Level 1 and built
// so both players are busy at once: each lane's obstacles sit next to ones in
// the other lane that need the opposite from the partner. Moving speeds up the
// partner's lane: time gates and bridges need the partner moving, lifts run
// faster with them moving, crushers are easier with them standing still.
public static class Level2Builder
{
    public const string ScenePath = "Assets/Scenes/Level2_Foundry.unity";
    const int Length = 176;

    [MenuItem("Tick & Tock/Build Level 2 Map")]
    public static void Build() => LevelMap.Build(ScenePath, Length, Tick, Tock);

    static void Tick(Lane l)
    {
        l.Floor((-8, 16), (19, 27), (30, 93), (96, Length + 8));
        l.Spawn(3);

        // 1. Hold the gate: Tock's hops keep this gate up; Tick's hops then hold Tock's.
        l.TimeGate(11);
        l.Fill(22, 25, 1);

        // 2. Crusher, then a lift to the high walk while Tock works the drawbridge.
        //    Tock waiting at the bridge keeps the crusher slow; Tick moving runs
        //    Tock's bridge out, Tock crossing it speeds up the lift.
        l.Checkpoint(34);
        l.Crusher(40);
        l.Lift(48, 5);
        l.Fill(51, 63, 5);
        l.Ledge(57, 60, 7);

        // 3. Crusher pair under Tock's high gate: Tock standing at the gate keeps
        //    the crushers slow, and Tick running them holds Tock's gate open.
        l.Checkpoint(68);
        l.Crusher(76);
        l.Crusher(84, 1.8f);
        l.Fill(90, 93, 1);
        l.TimeGate(102);                          // held up by Tock's hops

        // 4. Swap: crushers while Tock holds a gate, then a gate while Tock runs crushers.
        l.Checkpoint(110);
        l.Crusher(114);
        l.Crusher(120, 1.2f);
        l.TimeGate(130);
        l.Checkpoint(134);
        l.Lift(138, 4);
        l.Fill(141, Length + 8, 4);

        // 5. Finale: twin gates at the same spot, so both keep moving to get through.
        l.Ledge(146, 149, 6);
        l.TimeGate(154);
        l.Door(164);
    }

    static void Tock(Lane l)
    {
        l.Floor((-8, 7), (10, 16), (19, 49), (59, 95), (98, 101), (104, Length + 8));
        l.Spawn(3);

        // 1. Hops keep Tick's gate up, then Tick's hops hold this one.
        l.Fill(12, 14, 1);
        l.TimeGate(24);

        // 2. Sliding bridge over a pit too wide to jump: out while Tick moves.
        l.Checkpoint(34);
        l.Ledge(37, 40, 2);
        l.TimeBridge(50, 59);
        l.Fill(61, 64, 1);

        // 3. Lift to the high walk and a gate up there, held by Tick running the crushers.
        l.Checkpoint(68);
        l.Lift(74, 5);
        l.Fill(77, 92, 5);
        l.TimeGate(85);
        l.Ledge(105, 108, 2);                     // hops 96-103 hold Tick's gate up

        // 4. Gate while Tick runs the crushers, then crushers while Tick holds a gate.
        l.Checkpoint(110);
        l.TimeGate(116);
        l.Crusher(122);
        l.Crusher(128, 1.5f);
        l.Checkpoint(134);
        l.Lift(140, 4);
        l.Fill(143, Length + 8, 4);

        // 5. Finale: twin gates.
        l.Ledge(148, 151, 6);
        l.TimeGate(154);
        l.Door(164);
    }
}
