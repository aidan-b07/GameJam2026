using UnityEngine;

// The drawbridge's winch tower (art from tools/make_winch.py, built by
// LevelMap.Lane.Drawbridge). Pure scenery: everything here follows the leaf
// (BridgeMovement), nothing pushes back on it.
//
// A chain runs from the shackle on the leaf's tip over the sheave on the mast
// and down to the drum at its foot. Every part moves from how much chain is
// wound in (the tip's distance from the sheave): the sheave and drum turn
// with the chain, the drum gear drives the pinion, the belt the flywheel, the
// flywheel's crank the crosshead and piston, and the counterweight sinks down
// the mast as the chain winds in. While the winch lifts, the ratchet pawl
// clicks over the drum gear's teeth, the boiler gauge is up and the exhaust
// puffs steam; when the leaf goes over the top the pawl lifts and the brake
// band glows and throws sparks until the first bounce. The lamp on the head
// blinks amber while winching, glows red while the leaf falls and green while
// it spans the pit (the moment to stop time and cross). Every bounce kicks
// up dust where the leaf hits.
//
// The object sits on the hinge point; the parts are its children, placed by
// the builder at their resting pose (leaf hanging). Effects run on lane time.
// Positions and radii are in world units (1 tile = 32 art pixels).
public class DrawbridgeWinch : MonoBehaviour
{
    [SerializeField] private BridgeMovement bridge;
    [Tooltip("Distance from the hinge pin to the shackle on the leaf's tip.")]
    [SerializeField] private float tipDistance = 196f / 32f;

    [Header("Chain")]
    [SerializeField] private Transform sheave;
    [SerializeField] private float sheaveRadius = 12f / 32f;
    [Tooltip("Tip to sheave (Tiled, drawn from the tip).")]
    [SerializeField] private SpriteRenderer bridgeChain;
    [Tooltip("Sheave down to the drum (Tiled, drawn downwards).")]
    [SerializeField] private SpriteRenderer downChain;
    [Tooltip("Head down to the counterweight (Tiled, drawn downwards).")]
    [SerializeField] private SpriteRenderer weightChain;
    [SerializeField] private float linkPitch = 6f / 32f;

    [Header("Winch")]
    [SerializeField] private Transform drumGear;
    [SerializeField] private Transform drum;
    [SerializeField] private float drumRadius = 9f / 32f;
    [SerializeField] private int drumTeeth = 16;
    [SerializeField] private Transform pinion;
    [Tooltip("Pinion turns per drum turn (drum gear teeth / pinion teeth).")]
    [SerializeField] private float pinionRatio = 2f;
    [SerializeField] private Transform flywheel;
    [Tooltip("Flywheel turns per pinion turn (belt pulley sizes).")]
    [SerializeField] private float flywheelRatio = 0.8f;
    [SerializeField] private Transform crankRod;
    [SerializeField] private Transform crosshead;
    [SerializeField] private SpriteRenderer pistonRod;
    [SerializeField] private float crankRadius = 9f / 32f;
    [SerializeField] private float rodLength = 26f / 32f;
    [Tooltip("Cylinder face and centre line, local to this object.")]
    [SerializeField] private Vector2 cylinder = new Vector2(10.25f, 2.03125f);
    [SerializeField] private Transform pawl;
    [SerializeField] private SpriteRenderer brakeGlow;
    [SerializeField] private Transform counterweight;
    [Tooltip("Counterweight travel per unit of chain wound in.")]
    [SerializeField] private float counterweightRatio = 0.45f;
    [SerializeField] private Transform gaugeNeedle;

    [Header("Lamp")]
    [SerializeField] private SpriteRenderer lamp;
    [SerializeField] private SpriteRenderer glow;
    [SerializeField] private Sprite lampOff, lampAmber, lampDim, lampRed, lampGreen;
    [Tooltip("The lamp shows green within this many degrees of spanning the pit.")]
    [SerializeField] private float greenWindow = 12f;

    [Header("Effects (positions local to this object)")]
    [Tooltip("Plain white sprite the steam, sparks and dust are drawn with.")]
    [SerializeField] private Sprite moteSprite;
    [Tooltip("Takes its colour (the lane's light) for steam and dust.")]
    [SerializeField] private SpriteRenderer tintSource;
    [SerializeField] private Vector2 steamVent = new Vector2(10.875f, 3.15625f);
    [SerializeField] private Vector2 brakePoint = new Vector2(7.40625f, 1.15625f);
    [Tooltip("Ground slot the leaf lands in (x from, x to, at the ground surface y).")]
    [SerializeField] private Vector3 landingSlot = new Vector3(-6.5f, -0.5f, 0.5f);
    [Tooltip("Where the leaf meets the pit wall (x, y from, y to).")]
    [SerializeField] private Vector3 wallContact = new Vector3(-0.5f, -1.4f, -0.4f);
    [SerializeField] private float steamInterval = 0.22f;

    private const float Px = 1f / 32f;
    private static readonly Color AmberLight = new Color32(255, 181, 46, 255);
    private static readonly Color RedLight = new Color32(255, 58, 42, 255);
    private static readonly Color GreenLight = new Color32(92, 255, 122, 255);
    private static readonly Color SteamColor = new Color32(235, 235, 245, 255);
    private static readonly Color DustColor = new Color32(200, 150, 110, 255);
    private static readonly Color SparkHot = new Color32(255, 243, 160, 255);
    private static readonly Color SparkCool = new Color32(240, 144, 48, 255);

    private enum Kind { Steam, Spark, Dust }
    private struct Mote { public Kind kind; public Vector2 pos, vel; public float age, life; }
    private const int MoteCount = 64;
    private readonly Mote[] motes = new Mote[MoteCount];
    private SpriteRenderer[] moteRenderers;
    private int nextMote;

    private Vector2 counterweightRest, downChainTop, weightChainTop;
    private float fullChain;        // chain out with the leaf hanging
    private float laneTime, steamTimer, needle;
    private bool wasFalling;

    private void Awake()
    {
        counterweightRest = counterweight.localPosition;
        downChainTop = downChain.transform.localPosition;
        weightChainTop = weightChain.transform.localPosition;
        CreateMotes();
    }

    private void OnEnable()
    {
        if (bridge != null) bridge.Impact += OnImpact;
    }

    private void OnDisable()
    {
        if (bridge != null) bridge.Impact -= OnImpact;
    }

    private void Start()
    {
        // The leaf's hinge is where this object stands; at rest the leaf hangs.
        fullChain = Vector2.Distance(Tip(0f), sheave.position);
    }

    private Vector2 Hinge => Application.isPlaying && bridge != null ? bridge.Pivot : (Vector2)transform.position;

    private Vector2 Tip(float angle) => Hinge + Rotate(new Vector2(0f, -tipDistance), angle);

    private static Vector2 Rotate(Vector2 v, float degrees)
    {
        float r = degrees * Mathf.Deg2Rad, c = Mathf.Cos(r), s = Mathf.Sin(r);
        return new Vector2(c * v.x - s * v.y, s * v.x + c * v.y);
    }

    private void LateUpdate()
    {
        if (bridge == null) return;
        var manager = GameManager.INSTANCE;
        float dt = Time.deltaTime * (manager != null ? manager.GetWorldSpeed(bridge.Lane) : 1f);
        laneTime += dt;

        // The leaf's drawn (interpolated) rotation, so the chain stays on the shackle.
        float angle = bridge.transform.eulerAngles.z;
        Vector2 tip = Tip(angle), s = sheave.position;
        float chain = Vector2.Distance(tip, s);
        float wound = fullChain - chain;

        // Chain from the shackle to where it leaves the sheave (its upper side).
        Vector2 v = (tip - s).normalized;
        Vector2 perp = new Vector2(v.y, -v.x);
        if (perp.y < 0f) perp = -perp;
        Stretch(bridgeChain, tip, s + perp * sheaveRadius);

        // The rest of the chain: down the mast to the drum, links moving with it.
        float drop = downChainTop.y - drum.localPosition.y;
        float phase = Mathf.Repeat(chain, linkPitch);
        downChain.transform.localPosition = downChainTop + Vector2.up * phase;
        downChain.size = new Vector2(drop + phase, downChain.size.y);

        sheave.localRotation = Quaternion.Euler(0f, 0f, chain / sheaveRadius * Mathf.Rad2Deg);
        float drumTurn = wound / drumRadius;    // radians, counterclockwise
        drum.localRotation = drumGear.localRotation = Quaternion.Euler(0f, 0f, drumTurn * Mathf.Rad2Deg);
        float pinionTurn = -drumTurn * pinionRatio - 0.2f;
        pinion.localRotation = Quaternion.Euler(0f, 0f, pinionTurn * Mathf.Rad2Deg);
        float flyTurn = pinionTurn * flywheelRatio;
        flywheel.localRotation = Quaternion.Euler(0f, 0f, flyTurn * Mathf.Rad2Deg);

        // Crank pin, connecting rod to the crosshead, piston rod into the cylinder.
        Vector2 pin = (Vector2)flywheel.localPosition + new Vector2(Mathf.Cos(flyTurn), Mathf.Sin(flyTurn)) * crankRadius;
        float dy = pin.y - cylinder.y;
        Vector2 head = new Vector2(pin.x + Mathf.Sqrt(Mathf.Max(0f, rodLength * rodLength - dy * dy)), cylinder.y);
        crankRod.localPosition = pin;
        crankRod.localRotation = Quaternion.Euler(0f, 0f, Mathf.Atan2(head.y - pin.y, head.x - pin.x) * Mathf.Rad2Deg);
        crosshead.localPosition = head;
        pistonRod.transform.localPosition = head;
        pistonRod.size = new Vector2(Mathf.Max(0f, cylinder.x - head.x), pistonRod.size.y);

        // Ratchet pawl: rides the drum gear's teeth while winching, lifted while the leaf falls.
        float tooth = Mathf.Repeat(drumTurn * drumTeeth / (2f * Mathf.PI), 1f);
        float pawlAngle = bridge.FreeFalling ? -1.05f : -0.35f + (bridge.Winching ? 0.25f * tooth : 0f);
        pawl.localRotation = Quaternion.Euler(0f, 0f, -(pawlAngle + 0.95f) * Mathf.Rad2Deg);
        brakeGlow.enabled = bridge.FreeFalling;

        // Counterweight sinks as the chain winds in; its chain hangs from the head.
        counterweight.localPosition = counterweightRest + Vector2.down * (wound * counterweightRatio);
        weightChain.size = new Vector2(weightChainTop.y - counterweight.localPosition.y, weightChain.size.y);

        // Boiler gauge: up and trembling while the engine works.
        float gauge = bridge.Winching ? -0.4f + Mathf.Sin(laneTime * 23f) * 0.08f : bridge.FreeFalling ? -2.2f : -2.6f;
        needle = Mathf.Lerp(needle, gauge, 1f - Mathf.Exp(-dt * 12f));
        gaugeNeedle.localRotation = Quaternion.Euler(0f, 0f, -needle * Mathf.Rad2Deg);

        UpdateLamp(angle);
        Emit(dt);
        UpdateMotes(dt);
    }

    private void UpdateLamp(float angle)
    {
        bool spanning = Mathf.Abs(Mathf.DeltaAngle(angle, 90f)) < greenWindow && !bridge.Winching && !bridge.FreeFalling;
        bool blink = Mathf.Repeat(laneTime * 4f, 2f) < 1f;
        Color light = Color.clear;
        if (bridge.FreeFalling) { lamp.sprite = lampRed; light = RedLight; }
        else if (bridge.Winching) { lamp.sprite = blink ? lampAmber : lampDim; if (blink) light = AmberLight; }
        else if (spanning) { lamp.sprite = lampGreen; light = GreenLight; }
        else lamp.sprite = lampOff;
        glow.enabled = light.a > 0f;
        glow.color = light;
    }

    // Lays a Tiled chain (left-pivot sprite along +x) from a to b.
    private static void Stretch(SpriteRenderer sr, Vector2 a, Vector2 b)
    {
        Vector2 d = b - a;
        sr.transform.position = new Vector3(a.x, a.y, sr.transform.position.z);
        sr.transform.rotation = Quaternion.Euler(0f, 0f, Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg);
        sr.size = new Vector2(d.magnitude, sr.size.y);
    }

    private void OnImpact(float speed, bool open)
    {
        float n = Mathf.Clamp01(speed / 340f);
        if (open)
        {
            float step = n > 0.5f ? 5f * Px : 12f * Px;
            for (float x = landingSlot.x; x < landingSlot.y; x += step)
                Spawn(Kind.Dust, new Vector2(x + Random.value * 4f * Px, landingSlot.z),
                    new Vector2((Random.value - 0.5f) * 24f * n, (4f + Random.value * 16f) * n) * Px, 0.5f + 0.5f * n);
        }
        else
        {
            for (int i = 0; i < 4 + 12 * n; i++)
                Spawn(Kind.Dust, new Vector2(wallContact.x + Random.value * 6f * Px, Random.Range(wallContact.y, wallContact.z)),
                    new Vector2(Random.value * 20f * n, Random.value * 12f * n) * Px, 0.5f);
        }
    }

    private void Emit(float dt)
    {
        if (bridge.Winching)
        {
            steamTimer += dt;
            if (steamTimer >= steamInterval)
            {
                steamTimer -= steamInterval;
                Spawn(Kind.Steam, steamVent, new Vector2(-2f + Random.value * 4f, 14f) * Px, 1.2f);
            }
        }
        if (bridge.FreeFalling)
        {
            int count = Mathf.Max(1, Mathf.RoundToInt(dt * 120f));
            for (int i = 0; i < count; i++)
                Spawn(Kind.Spark, brakePoint + new Vector2(Random.value * 4f * Px, 0f),
                    new Vector2(-30f - Random.value * 40f, 30f + Random.value * 30f) * Px, 0.35f + Random.value * 0.2f);
        }
        if (wasFalling && !bridge.FreeFalling)   // pawl drops back in: a clank of sparks
            for (int i = 0; i < 8; i++)
                Spawn(Kind.Spark, pawl.localPosition, new Vector2(-20f + Random.value * 40f, Random.value * 40f) * Px, 0.25f);
        wasFalling = bridge.FreeFalling;
    }

    private void CreateMotes()
    {
        moteRenderers = new SpriteRenderer[MoteCount];
        if (moteSprite == null) return;
        for (int i = 0; i < MoteCount; i++)
        {
            var go = new GameObject("Mote");
            go.transform.SetParent(transform, false);
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = moteSprite;
            sr.enabled = false;
            moteRenderers[i] = sr;
            motes[i].life = 0f;
        }
    }

    private void Spawn(Kind kind, Vector2 at, Vector2 velocity, float life)
    {
        if (moteSprite == null) return;
        int i = nextMote;
        nextMote = (nextMote + 1) % MoteCount;
        motes[i] = new Mote { kind = kind, pos = at, vel = velocity, age = 0f, life = life };
    }

    private void UpdateMotes(float dt)
    {
        if (moteSprite == null) return;
        float spriteSize = moteSprite.bounds.size.x;
        Color tint = tintSource != null ? tintSource.color : Color.white;
        for (int i = 0; i < MoteCount; i++)
        {
            ref Mote m = ref motes[i];
            var sr = moteRenderers[i];
            if (m.age >= m.life) { sr.enabled = false; continue; }
            m.age += dt;
            m.pos += m.vel * dt;
            float size = 1f;
            Color c;
            switch (m.kind)
            {
                case Kind.Spark:
                    m.vel.y -= 220f * Px * dt;
                    c = m.age < m.life * 0.5f ? SparkHot : SparkCool;
                    sr.sortingOrder = 8;
                    break;
                case Kind.Dust:
                    m.vel.y -= 10f * Px * dt;
                    m.vel.x *= Mathf.Pow(0.97f, dt * 60f);
                    c = DustColor * tint;
                    c.a = 0.8f * (1f - m.age / m.life);
                    size = 2f;
                    sr.sortingOrder = 7;
                    break;
                default:
                    m.vel.x *= Mathf.Pow(0.98f, dt * 60f);
                    c = SteamColor * tint;
                    c.a = 0.8f * (1f - m.age / m.life);
                    size = 1f + 2f * (1f + m.age * 3f);
                    sr.sortingOrder = 7;
                    break;
            }
            sr.enabled = m.age < m.life;
            sr.color = c;
            // Snap to the art's pixel grid like the rest of the tower.
            Vector2 p = new Vector2(Mathf.Round(m.pos.x / Px), Mathf.Round(m.pos.y / Px)) * Px;
            sr.transform.localPosition = p;
            sr.transform.localScale = Vector3.one * (size * Px / spriteSize);
        }
    }
}
