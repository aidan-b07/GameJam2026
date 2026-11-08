using System.Collections;
using System.Collections.Generic;
using UnityEngine;

// The respawn light (GameFlow): each player's lantern on the checkpoint lamp
// flares, its flame leaves as a wisp in the player's colour, the lantern goes
// dark, the wisp loops out and down to the respawn point, circles it once
// (behind the player on the far side of the ring, in front on the near side),
// and bursts as the respawn animation starts. Once the player has formed the
// lantern lights again with a puff of sparks. Without a lamp (a spawn point
// built before spawn lamps existed) the wisp falls from the sky instead.
//
// Art from tools/make_wisp.py in Resources/Art/Wisp (one row of square
// frames per sheet, cut here), so no scene wiring is needed. Runs in real
// time, like the rest of the respawn.
public class SpawnWisp : MonoBehaviour
{
    [Header("Timing (seconds)")]
    [Tooltip("Lantern flare before the flame leaves; overlaps the end of the camera pan.")]
    public float flareTime = 0.3f;
    [Tooltip("Flight from the lantern to the ring round the respawn point.")]
    public float flightTime = 0.6f;
    [Tooltip("One turn round the respawn point, closing in to the burst.")]
    public float circleTime = 0.35f;
    [Tooltip("From the burst to the lantern lighting again.")]
    public float relightDelay = 0.8f;

    [Header("Shape")]
    [Tooltip("Burst point above the respawn feet (the respawn sheet's void ring).")]
    public float targetHeight = 0.55f;
    public float circleRadius = 0.8f;
    [Tooltip("Height of the ring against its width (seen from the side).")]
    public float circleTilt = 0.45f;
    [Tooltip("Sorting orders: in front of the player (20), behind it, burst.")]
    public int frontOrder = 25, backOrder = 15, burstOrder = 22;

    // Flare starts this long before the camera arrives.
    public float Lead => flareTime;
    // From Play to the burst.
    public float Duration => flareTime + flightTime + circleTime;

    const float Ppu = 32f;
    // Frames cut from the sheets, owned by this component and destroyed with
    // it (the textures belong to Resources and are not destroyed).
    readonly Dictionary<string, Sprite[]> sheets = new Dictionary<string, Sprite[]>();

    class Mote
    {
        public SpriteRenderer sr;
        public Vector2 pos, vel;
        public float age, life, drag;
        public Sprite[] frames;
    }

    readonly List<Mote> motes = new List<Mote>();
    readonly Stack<SpriteRenderer> spare = new Stack<SpriteRenderer>();

    public void Play(int lane, CheckpointLamp lamp, Vector3 target)
    {
        StartCoroutine(Run(lane, lamp, target + new Vector3(0f, targetHeight, 0f)));
    }

    IEnumerator Run(int lane, CheckpointLamp lamp, Vector3 target)
    {
        string name = lane == 0 ? "tick" : "tock";
        float side = lane == 0 ? -1f : 1f;   // Tick's lantern is on the left
        var flare = Sheet($"wisp_{name}_flare_64px", 64);
        var orbFrames = Sheet($"wisp_{name}_32px", 32);
        var burst = Sheet($"wisp_{name}_burst_64px", 64);
        var mote = Sheet($"wisp_{name}_mote_8px", 8);
        if (orbFrames == null) yield break;

        Vector3 from = lamp != null ? lamp.FlamePosition(lane) : target + new Vector3(side * 2.5f, 6f, 0f);
        var orb = Take($"Wisp_{name}", frontOrder);
        orb.enabled = false;
        float t = 0f;

        // 1. The lantern flares and the flame swells into a ball of light.
        // Timing and the douse do not depend on the flare art having loaded.
        if (lamp != null)
        {
            Sfx.Play("checkpoint", from, 0.3f, false, 1.5f);
            var fl = flare != null ? Take($"WispFlare_{name}", frontOrder) : null;
            if (fl != null) fl.transform.position = Snap(from);
            float emit = 0f;
            while (t < flareTime)
            {
                float u = t / flareTime;
                if (fl != null) fl.sprite = flare[Mathf.Min(flare.Length - 1, (int)(u * flare.Length))];
                if (u > 0.6f)
                {
                    orb.enabled = true;
                    orb.sprite = orbFrames[0];
                    orb.transform.position = Snap(from);
                }
                if ((emit += Time.deltaTime) > 0.05f)
                {
                    emit = 0f;
                    Spawn(mote, from, Random.insideUnitCircle * 1.2f + Vector2.up * 0.4f, 0.4f, 2f);
                }
                t += Time.deltaTime;
                yield return null;
            }
            if (fl != null) Give(fl);
            lamp.Douse(lane);
            t -= flareTime;
        }
        orb.enabled = true;

        // 2. Out and down to the top of the ring round the respawn point.
        float flight = lamp != null ? flightTime : flightTime + flareTime;
        float dir = -side;                       // Tick turns clockwise, Tock anticlockwise
        Vector3 tangent = new Vector3(-side, 0f, 0f);
        Vector3 p0 = target + new Vector3(0f, circleRadius * circleTilt, 0f);
        Vector3 c1 = from + new Vector3(side * 1.1f, 1.2f, 0f);
        Vector3 c2 = p0 - tangent * 1.3f + new Vector3(0f, 0.5f, 0f);
        float trail = 0f, scatter = 0f, anim = 0f;
        while (t < flight)
        {
            float u = Mathf.Pow(t / flight, 1.5f);
            Vector3 p = Bezier(from, c1, c2, p0, u);
            MoveOrb(orb, orbFrames, p, frontOrder, ref anim);
            Trail(mote, p, ref trail, ref scatter);
            t += Time.deltaTime;
            yield return null;
        }
        t -= flight;

        // 3. One turn round the point, closing in.
        while (t < circleTime)
        {
            float u = t / circleTime;
            float a = Mathf.PI / 2f + dir * 2f * Mathf.PI * u;
            float r = circleRadius * (1f - u * u);
            var p = target + new Vector3(Mathf.Cos(a) * r, Mathf.Sin(a) * r * circleTilt, 0f);
            MoveOrb(orb, orbFrames, p, Mathf.Sin(a) > 0f ? backOrder : frontOrder, ref anim);
            Trail(mote, p, ref trail, ref scatter);
            t += Time.deltaTime;
            yield return null;
        }
        Give(orb);

        // 4. Burst, in step with the respawn sheet's first frames.
        for (int i = 0; i < 14; i++)
        {
            float ang = (i + Random.value * 0.6f) * Mathf.PI * 2f / 14f;
            var v = new Vector2(Mathf.Cos(ang), Mathf.Sin(ang) * 0.8f) * Random.Range(2.5f, 4f);
            Spawn(mote, target, v, Random.Range(0.45f, 0.7f), 4f);
        }
        float since = 0f;
        if (burst != null)
        {
            var b = Take($"WispBurst_{name}", burstOrder);
            b.transform.position = Snap(target);
            const float fps = 16f;
            while (since * fps < burst.Length)
            {
                b.sprite = burst[(int)(since * fps)];
                since += Time.deltaTime;
                yield return null;
            }
            Give(b);
        }

        // 5. The lantern lights again as the player finishes forming.
        while (since < relightDelay)
        {
            since += Time.deltaTime;
            yield return null;
        }
        if (lamp != null)
        {
            lamp.Relight(lane);
            Sfx.Play(name, from, 0.5f, false);
            for (int i = 0; i < 8; i++)
            {
                float ang = Mathf.PI * (0.15f + 0.7f * i / 7f);
                Spawn(mote, from, new Vector2(Mathf.Cos(ang), Mathf.Sin(ang)) * Random.Range(0.8f, 1.6f), Random.Range(0.35f, 0.55f), 3f);
            }
        }
    }

    void MoveOrb(SpriteRenderer orb, Sprite[] frames, Vector3 p, int order, ref float anim)
    {
        anim += Time.deltaTime;
        orb.sprite = frames[(int)(anim * 12f) % frames.Length];
        orb.sortingOrder = order;
        orb.transform.position = Snap(p);
    }

    // Sparkles left behind, and now and then a mote thrown off the wisp.
    void Trail(Sprite[] mote, Vector3 p, ref float trail, ref float scatter)
    {
        if ((trail += Time.deltaTime) >= 0.02f)
        {
            trail = 0f;
            Spawn(mote, p + (Vector3)(Random.insideUnitCircle * 2f / Ppu), new Vector2(0f, -0.25f), 0.35f, 1f);
        }
        if ((scatter += Time.deltaTime) >= 0.07f)
        {
            scatter = 0f;
            Spawn(mote, p, Random.insideUnitCircle.normalized * Random.Range(0.6f, 1.2f), 0.55f, 1.5f);
        }
    }

    void Spawn(Sprite[] frames, Vector3 pos, Vector2 vel, float life, float drag)
    {
        if (frames == null) return;
        var sr = Take("WispMote", frontOrder);
        motes.Add(new Mote { sr = sr, pos = pos, vel = vel, life = life, drag = drag, frames = frames });
        sr.sprite = frames[0];
        sr.transform.position = Snap(pos);
    }

    private void Update()
    {
        float dt = Time.deltaTime;
        for (int i = motes.Count - 1; i >= 0; i--)
        {
            var m = motes[i];
            m.age += dt;
            if (m.age >= m.life)
            {
                Give(m.sr);
                motes.RemoveAt(i);
                continue;
            }
            m.vel *= Mathf.Exp(-m.drag * dt);
            m.pos += m.vel * dt;
            m.sr.sprite = m.frames[Mathf.Min(m.frames.Length - 1, (int)(m.age / m.life * m.frames.Length))];
            m.sr.transform.position = Snap(m.pos);
        }
    }

    SpriteRenderer Take(string name, int order)
    {
        var sr = spare.Count > 0 ? spare.Pop() : PixelArt.Renderer(transform, name, order);
        sr.gameObject.name = name;
        sr.sortingOrder = order;
        sr.enabled = true;
        sr.gameObject.SetActive(true);
        return sr;
    }

    void Give(SpriteRenderer sr)
    {
        sr.gameObject.SetActive(false);
        spare.Push(sr);
    }

    static Vector3 Snap(Vector3 p) => new Vector3(Mathf.Round(p.x * Ppu) / Ppu, Mathf.Round(p.y * Ppu) / Ppu, 0f);

    static Vector3 Bezier(Vector3 a, Vector3 b, Vector3 c, Vector3 d, float u)
    {
        float v = 1f - u;
        return v * v * v * a + 3f * v * v * u * b + 3f * v * u * u * c + u * u * u * d;
    }

    private void OnDestroy()
    {
        foreach (var frames in sheets.Values)
            foreach (var f in frames)
                if (f != null) Destroy(f);
        sheets.Clear();
    }

    // Frames of a one-row sheet of square frames from Resources/Art/Wisp.
    Sprite[] Sheet(string file, int size)
    {
        if (sheets.TryGetValue(file, out var cached) && cached != null && cached.Length > 0 && cached[0] != null)
            return cached;
        var tex = Resources.Load<Texture2D>("Art/Wisp/" + file);
        if (tex == null)
        {
            Debug.LogWarning($"SpawnWisp: missing Resources/Art/Wisp/{file}");
            return null;
        }
        tex.filterMode = FilterMode.Point;
        int n = Mathf.Max(1, tex.width / size);
        var frames = new Sprite[n];
        for (int i = 0; i < n; i++)
        {
            frames[i] = Sprite.Create(tex, new Rect(i * size, 0, size, size), new Vector2(0.5f, 0.5f), Ppu, 0, SpriteMeshType.FullRect);
            frames[i].hideFlags = HideFlags.HideAndDontSave;
        }
        sheets[file] = frames;
        return frames;
    }
}
