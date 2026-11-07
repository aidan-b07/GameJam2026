using UnityEngine;

// The drawbridge's geared hinge (art from tools/make_winch.py, built by
// LevelMap.Lane.Drawbridge). Pure scenery: everything here follows the leaf
// (BridgeMovement), nothing pushes back on it.
//
// A big brass gear on the hinge turns with the leaf and drives a pinion with
// a crank wheel on the pit's lip, so the wheel spins fast while the winch
// lifts the leaf and faster still while it drops; while the leaf falls freely
// the wheel's brake throws sparks. The lamp on its post blinks amber while
// winching, glows red while the leaf falls and green while it spans the pit,
// even while the winch carries it across (the moment to stop time and
// cross). Every bounce kicks up dust where the leaf hits.
//
// The lamp, its glow and the sparks sort behind the leaf (order 5), so the
// leaf passes in front of them. The object sits on the hinge point; the parts
// are its children, placed by the builder at their resting pose (leaf
// hanging). Effects run on lane time. Positions are in world units (1 tile =
// 32 art pixels), local to this object.
public class DrawbridgeWinch : MonoBehaviour
{
    [SerializeField] private BridgeMovement bridge;

    [Header("Gears")]
    [SerializeField] private Transform hingeGear;
    [SerializeField] private Transform crank;
    [Tooltip("Crank turns per hinge gear turn (gear teeth / pinion teeth).")]
    [SerializeField] private float crankRatio = 2.5f;

    [Header("Lamp")]
    [SerializeField] private SpriteRenderer lamp;
    [SerializeField] private SpriteRenderer glow;
    [SerializeField] private Sprite lampOff, lampAmber, lampDim, lampRed, lampGreen;
    [Tooltip("The lamp shows green within this many degrees of spanning the pit.")]
    [SerializeField] private float greenWindow = 12f;

    [Header("Effects")]
    [Tooltip("Plain white sprite the sparks and dust are drawn with.")]
    [SerializeField] private Sprite moteSprite;
    [Tooltip("Takes its colour (the lane's light) for the dust.")]
    [SerializeField] private SpriteRenderer tintSource;
    [Tooltip("Where the crank wheel's brake throws sparks.")]
    [SerializeField] private Vector2 brakePoint = new Vector2(-0.625f, 0.625f);
    [Tooltip("Ground slot the leaf lands in (x from, x to, at the ground surface y).")]
    [SerializeField] private Vector3 landingSlot = new Vector3(-6.5f, -0.5f, 0.5f);
    [Tooltip("Where the leaf meets the pit wall (x, y from, y to).")]
    [SerializeField] private Vector3 wallContact = new Vector3(-0.5f, -1.4f, -0.4f);
    [Tooltip("Sorting order of the sparks; below the leaf's 5 so it passes in front.")]
    [SerializeField] private int sparkOrder = 4;
    [SerializeField] private int dustOrder = 7;

    private const float Px = 1f / 32f;
    private static readonly Color AmberLight = new Color32(255, 181, 46, 255);
    private static readonly Color RedLight = new Color32(255, 58, 42, 255);
    private static readonly Color GreenLight = new Color32(92, 255, 122, 255);
    private static readonly Color DustColor = new Color32(200, 150, 110, 255);
    private static readonly Color SparkHot = new Color32(255, 243, 160, 255);
    private static readonly Color SparkCool = new Color32(240, 144, 48, 255);

    private struct Mote { public bool dust; public Vector2 pos, vel; public float age, life; }
    private const int MoteCount = 48;
    private readonly Mote[] motes = new Mote[MoteCount];
    private SpriteRenderer[] moteRenderers;
    private int nextMote;
    private float laneTime;
    private bool wasFalling;

    private void Awake()
    {
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

    private void LateUpdate()
    {
        if (bridge == null) return;
        var manager = GameManager.INSTANCE;
        float dt = Time.deltaTime * (manager != null ? manager.GetWorldSpeed(bridge.Lane) : 1f);
        laneTime += dt;

        // The leaf's drawn (interpolated) rotation, so the gear stays on it.
        float angle = bridge.transform.eulerAngles.z;
        hingeGear.localRotation = Quaternion.Euler(0f, 0f, angle);
        crank.localRotation = Quaternion.Euler(0f, 0f, -angle * crankRatio - 0.3f * Mathf.Rad2Deg);

        UpdateLamp(angle);
        Emit(dt);
        UpdateMotes(dt);
    }

    private void UpdateLamp(float angle)
    {
        bool spanning = Mathf.Abs(Mathf.DeltaAngle(angle, 90f)) < greenWindow;
        bool blink = Mathf.Repeat(laneTime * 4f, 2f) < 1f;
        Color light = Color.clear;
        if (bridge.FreeFalling) { lamp.sprite = lampRed; light = RedLight; }
        else if (spanning) { lamp.sprite = lampGreen; light = GreenLight; }
        else if (bridge.Winching) { lamp.sprite = blink ? lampAmber : lampDim; if (blink) light = AmberLight; }
        else lamp.sprite = lampOff;
        glow.enabled = light.a > 0f;
        glow.color = light;
    }

    private void OnImpact(float speed, bool open)
    {
        float n = Mathf.Clamp01(speed / 340f);
        if (open)
        {
            float step = n > 0.5f ? 5f * Px : 12f * Px;
            for (float x = landingSlot.x; x < landingSlot.y; x += step)
                Spawn(true, new Vector2(x + Random.value * 4f * Px, landingSlot.z),
                    new Vector2((Random.value - 0.5f) * 24f * n, (4f + Random.value * 16f) * n) * Px, 0.5f + 0.5f * n);
        }
        else
        {
            for (int i = 0; i < 4 + 12 * n; i++)
                Spawn(true, new Vector2(wallContact.x + Random.value * 6f * Px, Random.Range(wallContact.y, wallContact.z)),
                    new Vector2(Random.value * 20f * n, Random.value * 12f * n) * Px, 0.5f);
        }
    }

    private void Emit(float dt)
    {
        if (bridge.FreeFalling)
        {
            int count = Mathf.Max(1, Mathf.RoundToInt(dt * 120f));
            for (int i = 0; i < count; i++)
                Spawn(false, brakePoint + new Vector2(Random.value * 4f * Px, 0f),
                    new Vector2(-30f - Random.value * 40f, 30f + Random.value * 30f) * Px, 0.35f + Random.value * 0.2f);
        }
        if (wasFalling && !bridge.FreeFalling)   // the brake bites: a last burst
            for (int i = 0; i < 8; i++)
                Spawn(false, brakePoint, new Vector2(-20f + Random.value * 40f, Random.value * 40f) * Px, 0.25f);
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
        }
    }

    private void Spawn(bool dust, Vector2 at, Vector2 velocity, float life)
    {
        if (moteSprite == null) return;
        motes[nextMote] = new Mote { dust = dust, pos = at, vel = velocity, age = 0f, life = life };
        nextMote = (nextMote + 1) % MoteCount;
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
            if (m.dust)
            {
                m.vel.y -= 10f * Px * dt;
                m.vel.x *= Mathf.Pow(0.97f, dt * 60f);
                c = DustColor * tint;
                c.a = 0.8f * (1f - m.age / m.life);
                size = 2f;
                sr.sortingOrder = dustOrder;
            }
            else
            {
                m.vel.y -= 220f * Px * dt;
                c = m.age < m.life * 0.5f ? SparkHot : SparkCool;
                sr.sortingOrder = sparkOrder;
            }
            sr.enabled = m.age < m.life;
            sr.color = c;
            // Snap to the art's pixel grid.
            sr.transform.localPosition = new Vector2(Mathf.Round(m.pos.x / Px), Mathf.Round(m.pos.y / Px)) * Px;
            sr.transform.localScale = Vector3.one * (size * Px / spriteSize);
        }
    }
}
