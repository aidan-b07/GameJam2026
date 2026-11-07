using UnityEngine;

// Plunger crusher: waits raised, slams down, bounces, then ratchets back up in
// clockwork steps. All timing runs on lane time (GameManager.GetWorldSpeed).
// Before the slam it warns: the dial hand sweeps once round during the wait,
// the lamps blink amber, and the head pulls up and judders; while slamming the
// lamps glow red. The impact throws dust and sparks and flashes the brass
// strike plate under the head. Art from
// tools/make_plunger.py; the level builder (LevelMap.Lane.Crusher) wires it up.
[RequireComponent(typeof(Rigidbody2D))]
public class PlungerMotion : MonoBehaviour
{
    [Header("Movement")]
    [Tooltip("Distance the plunger travels from its raised position.")]
    [SerializeField, Min(0f)] private float travelDistance = 5f;
    [Tooltip("Direction the plunger slams in. The value is normalized.")]
    [SerializeField] private Vector2 slamDirection = Vector2.down;

    [Header("Timing (seconds of lane time)")]
    [Tooltip("Wait at the raised position before slamming.")]
    [SerializeField, Min(0f)] private float raisedPause = 2f;
    [Tooltip("Time for the plunger to slam down.")]
    [SerializeField, Min(0.05f)] private float slamDuration = 0.25f;
    [Tooltip("Time spent making the small rebound after the slam.")]
    [SerializeField, Min(0f)] private float bounceDuration = 0.2f;
    [Tooltip("Time for the plunger to return to its raised position.")]
    [SerializeField, Min(0.05f)] private float returnDuration = 1.2f;
    [Tooltip("Height of the small rebound after the slam.")]
    [SerializeField, Min(0f)] private float bounceDistance = 0.15625f;
    [Tooltip("Lane time the cycle starts at, to offset neighbouring plungers.")]
    [SerializeField, Min(0f)] private float cycleOffset;

    [Header("Return ratchet")]
    [Tooltip("Clockwork steps the return is split into.")]
    [SerializeField, Min(1)] private int ratchetSteps = 6;
    [Tooltip("Part of each step spent moving; the rest holds still.")]
    [SerializeField, Range(0.05f, 1f)] private float ratchetMove = 0.4f;

    [Header("Warning")]
    [Tooltip("Last part of the wait in which the head pulls back and judders.")]
    [SerializeField, Min(0f)] private float tellTime = 0.3f;
    [Tooltip("How far the head pulls back before the slam.")]
    [SerializeField, Min(0f)] private float tellLift = 0.125f;
    [Tooltip("Last part of the wait in which the lamps blink amber.")]
    [SerializeField, Min(0f)] private float blinkTime = 0.8f;
    [Tooltip("Lamp blinks per lane second (on and off count as one each).")]
    [SerializeField, Min(1f)] private float blinkRate = 8f;

    [Header("Art")]
    [Tooltip("Head sprite; shakes sideways during the warning.")]
    [SerializeField] private Transform visual;
    [SerializeField] private SpriteRenderer lamp;
    [SerializeField] private Sprite amberLamp;
    [SerializeField] private Sprite redLamp;
    [Tooltip("Dial hand, one sprite per hour, 0 = 12 o'clock.")]
    [SerializeField] private SpriteRenderer hand;
    [SerializeField] private Sprite[] handSprites;
    [Tooltip("Tiled rod, pivot at its bottom on the head's top.")]
    [SerializeField] private SpriteRenderer rod;
    [Tooltip("World y the rod reaches up to (inside the housing).")]
    [SerializeField] private float rodTop;
    [Tooltip("Plain white sprite the dust and sparks are drawn with.")]
    [SerializeField] private Sprite dustSprite;
    [Tooltip("Lit strike plate under the head, shown for a moment on impact.")]
    [SerializeField] private SpriteRenderer strikeFlash;
    [Tooltip("Lane seconds the strike plate stays lit after the impact.")]
    [SerializeField, Min(0f)] private float flashTime = 0.08f;

    [SerializeField]
    [Tooltip("Which lane is the object in")]
    [UnityEngine.Range(0,1)]
    private int lane;

    private enum Phase
    {
        RaisedPause,
        Slamming,
        Bouncing,
        Returning
    }

    const float PixelsPerUnit = 32f;
    const int DustCount = 18, SparkCount = 8;
    const float DustLife = 0.6f;
    static readonly Color DustLight = new Color32(232, 200, 160, 255);
    static readonly Color DustDark = new Color32(200, 160, 120, 255);
    static readonly Color SparkLight = new Color32(252, 236, 140, 255);
    static readonly Color SparkDark = new Color32(240, 180, 70, 255);

    private Rigidbody2D rb;
    private Vector2 raisedPosition;
    private Vector2 slamVector;
    private Phase phase = Phase.RaisedPause;
    private float phaseTime;
    private float laneTime;

    // Sounds (Sfx): what the last frame played, to catch the changes.
    private Phase soundPhase = Phase.RaisedPause;
    private bool soundAmber;
    private int soundRatchet = -1;

    private struct Mote { public SpriteRenderer sr; public float side, speed, rise, size, life; }
    private Mote[] motes;
    private SpriteRenderer head;
    private Vector3 visualRest;
    private float rodWidth;

    private void Awake()
    {
        rb = GetComponent<Rigidbody2D>();
        rb.bodyType = RigidbodyType2D.Kinematic;
        rb.interpolation = RigidbodyInterpolation2D.Interpolate;
        rb.linearVelocity = Vector2.zero;
        rb.angularVelocity = 0f;

        raisedPosition = rb.position;
        slamVector = slamDirection.sqrMagnitude > Mathf.Epsilon
            ? slamDirection.normalized
            : Vector2.down;
        rb.position = raisedPosition;

        phaseTime = cycleOffset;
        AdvancePhase();

        if (visual != null)
        {
            visualRest = visual.localPosition;
            head = visual.GetComponent<SpriteRenderer>();
        }
        if (rod != null) rodWidth = rod.size.x;
        CreateMotes();
    }

    private void FixedUpdate()
    {
        float dt = Time.fixedDeltaTime * (GameManager.INSTANCE != null ? GameManager.INSTANCE.GetWorldSpeed(lane) : 1f);
        phaseTime += dt;
        laneTime += dt;
        AdvancePhase();
        rb.MovePosition(PositionForPhase());
    }

    private void LateUpdate()
    {
        UpdateTells();
        UpdateRod();
        UpdateMotes();
        UpdateSounds();
    }

    // Warning beep per amber blink, impact on landing, a click per ratchet step.
    private void UpdateSounds()
    {
        Vector3 at = transform.position;
        if (soundPhase == Phase.Slamming && phase != Phase.Slamming)
            Sfx.Play("plunger_slam", at, 1f, lane: lane);

        bool amber = phase == Phase.RaisedPause && raisedPause - phaseTime < blinkTime
            && Mathf.FloorToInt((raisedPause - phaseTime) * blinkRate) % 2 == 1;
        if (amber && !soundAmber) Sfx.Play("plunger_warn", at, 0.5f, lane: lane);

        int step = phase == Phase.Returning
            ? Mathf.Min(ratchetSteps - 1, Mathf.FloorToInt(Mathf.Clamp01(phaseTime / returnDuration) * ratchetSteps))
            : -1;
        if (step >= 0 && step != soundRatchet) Sfx.Play("ratchet_click", at, 0.6f, lane: lane);

        soundPhase = phase;
        soundAmber = amber;
        soundRatchet = step;
    }

    private void AdvancePhase()
    {
        // Several phases can pass in one step at high lane speed or a long offset.
        for (int guard = 0; guard < 64; guard++)
        {
            float length = PhaseLength(phase);
            if (phaseTime < length) return;

            phaseTime -= length;
            phase = (Phase)(((int)phase + 1) % 4);
        }
    }

    private float PhaseLength(Phase currentPhase)
    {
        switch (currentPhase)
        {
            case Phase.RaisedPause: return raisedPause;
            case Phase.Slamming: return slamDuration;
            case Phase.Bouncing: return bounceDuration;
            default: return returnDuration;
        }
    }

    private Vector2 PositionForPhase()
    {
        Vector2 bottomPosition = raisedPosition + slamVector * travelDistance;

        switch (phase)
        {
            case Phase.Slamming:
                return Vector2.Lerp(raisedPosition, bottomPosition,
                    Fall(phaseTime / slamDuration));

            case Phase.Bouncing:
            {
                if (bounceDuration <= 0f) return bottomPosition;

                float progress = Mathf.Clamp01(phaseTime / bounceDuration);
                float rebound = Mathf.Sin(Mathf.PI * progress) * bounceDistance;
                return bottomPosition - slamVector * rebound;
            }

            case Phase.Returning:
                return Vector2.Lerp(bottomPosition, raisedPosition, ReturnProgress());

            default:
                // Pull back before the slam.
                return raisedPosition - slamVector * (tellLift * Mathf.Sin(TellProgress() * Mathf.PI / 2f));
        }
    }

    // 0 until the warning starts, 1 at the slam.
    private float TellProgress()
    {
        if (tellTime <= 0f) return 0f;
        float left = raisedPause - phaseTime;
        return left < tellTime ? Mathf.Clamp01(1f - left / tellTime) : 0f;
    }

    // Ratchet: each step moves quickly, then holds.
    private float ReturnProgress()
    {
        float steps = Mathf.Clamp01(phaseTime / returnDuration) * ratchetSteps;
        int k = Mathf.Min(ratchetSteps - 1, Mathf.FloorToInt(steps));
        return (k + Smooth((steps - k) / ratchetMove)) / ratchetSteps;
    }

    private void UpdateTells()
    {
        bool raised = phase == Phase.RaisedPause;
        if (visual != null)
        {
            bool judder = raised && TellProgress() > 0f && Mathf.FloorToInt(laneTime * 30f) % 2 == 1;
            visual.localPosition = visualRest + (judder ? new Vector3(1f / PixelsPerUnit, 0f, 0f) : Vector3.zero);
        }

        if (lamp != null)
        {
            Sprite s = null;
            if (phase == Phase.Slamming || phase == Phase.Bouncing) s = redLamp;
            else if (raised)
            {
                float left = raisedPause - phaseTime;
                if (left < blinkTime && Mathf.FloorToInt(left * blinkRate) % 2 == 1) s = amberLamp;
            }
            lamp.sprite = s;
            lamp.enabled = s != null;
        }

        if (strikeFlash != null) strikeFlash.enabled = phase == Phase.Bouncing && phaseTime < flashTime;

        if (hand != null && handSprites != null && handSprites.Length > 0)
        {
            int n = handSprites.Length;
            int hour = 0;
            if (raised) hour = Mathf.Clamp(Mathf.FloorToInt(phaseTime / Mathf.Max(raisedPause, 0.01f) * n), 0, n - 1);
            else if (phase == Phase.Returning) hour = (n - Mathf.RoundToInt(ReturnProgress() * n)) % n;   // winds back
            hand.sprite = handSprites[hour];
        }
    }

    // The rod reaches from the head's top up into the housing.
    private void UpdateRod()
    {
        if (rod == null) return;
        rod.size = new Vector2(rodWidth, Mathf.Max(0f, rodTop - rod.transform.position.y));
    }

    private void CreateMotes()
    {
        if (dustSprite == null) return;
        var root = new GameObject("ImpactDust").transform;
        root.SetParent(transform.parent, false);
        var random = new System.Random(42);
        float spriteSize = dustSprite.bounds.size.x;
        int order = head != null ? head.sortingOrder + 3 : 8;
        motes = new Mote[DustCount + SparkCount];
        for (int i = 0; i < motes.Length; i++)
        {
            bool dust = i < DustCount;
            var go = new GameObject(dust ? "Dust" : "Spark");
            go.transform.SetParent(root, false);
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = dustSprite;
            sr.sortingOrder = order;
            sr.enabled = false;
            var m = new Mote { sr = sr, side = i % 2 == 1 ? 1f : -1f };
            if (dust)
            {
                m.speed = 30f + (float)random.NextDouble() * 60f;
                m.rise = 10f + (float)random.NextDouble() * 30f;
                m.size = 2 + random.Next(3);
                m.life = 0.35f + (float)random.NextDouble() * 0.25f;
                sr.color = i % 3 == 0 ? DustDark : DustLight;
            }
            else
            {
                m.speed = 90f + (float)random.NextDouble() * 80f;
                m.rise = 60f + (float)random.NextDouble() * 60f;
                m.size = 1;
                m.life = 0.25f;
                sr.color = i % 2 == 1 ? SparkLight : SparkDark;
            }
            go.transform.localScale = Vector3.one * (m.size / PixelsPerUnit / spriteSize);
            motes[i] = m;
        }
    }

    // Dust puffs out both sides of the landed head and settles; sparks fly
    // higher and drop faster. Positions follow lane time since the impact, in
    // pixels, so the effect freezes and speeds up with the lane.
    private void UpdateMotes()
    {
        if (motes == null) return;
        float t = phase == Phase.Bouncing ? phaseTime
            : phase == Phase.Returning ? bounceDuration + phaseTime : -1f;
        bool show = t >= 0f && t <= DustLife;
        Vector2 bottom = raisedPosition + slamVector * travelDistance;
        Vector2 plate = bottom + slamVector * (head != null ? head.bounds.extents.y : 1f);
        Color tint = head != null ? head.color : Color.white;   // follows LaneSky's night tint
        for (int i = 0; i < motes.Length; i++)
        {
            var m = motes[i];
            bool on = show && t <= m.life;
            m.sr.enabled = on;
            if (!on) continue;
            bool dust = i < DustCount;
            float x = m.side * ((dust ? 46f : 40f) + m.speed * t);
            float y = (dust ? 2f : 3f) + m.rise * t - (dust ? 40f : 300f) * t * t;
            m.sr.transform.position = new Vector3(
                Mathf.Round(plate.x * PixelsPerUnit + x) / PixelsPerUnit,
                Mathf.Round(plate.y * PixelsPerUnit + y) / PixelsPerUnit, 0f);
            Color c = (dust ? (i % 3 == 0 ? DustDark : DustLight) : (i % 2 == 1 ? SparkLight : SparkDark)) * tint;
            if (dust) c.a = 1f - t / m.life;
            m.sr.color = c;
        }
    }

    private static float Fall(float progress)
    {
        progress = Mathf.Clamp01(progress);
        return progress * progress;
    }

    private static float Smooth(float progress)
    {
        progress = Mathf.Clamp01(progress);
        return progress * progress * (3f - 2f * progress);
    }
}
