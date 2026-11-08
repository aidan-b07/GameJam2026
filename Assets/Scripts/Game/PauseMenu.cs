using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

// Pause menu for the levels. Escape (or a gamepad's Start) opens it: the screen
// dims and a small brass-framed panel shows Resume, Restart from checkpoint
// and Main menu in the pixel font. W/S, the arrows, the d-pad or a stick choose;
// Enter, Space or the gamepad's A confirm; Escape, Start or B resume.
//
// Pausing sets Time.timeScale to 0, pauses all audio (AudioListener.pause) and
// switches the players' PlayerMovement off (W and Up are also their jump keys,
// and a press made while paused would otherwise jump on resume). All of it is
// restored on resume, restart, menu load and on any scene load.
//
// Creates itself on scene load (like Sfx and Music) in scenes with a GameFlow,
// so no scene or builder change is needed. It does not open while the ending
// runs (GameFlow.Finished, DawnEnding.Running), which uses Escape to skip.
// Restart reuses GameFlow.KillAll; during a respawn it just resumes.
public class PauseMenu : MonoBehaviour
{
    public static PauseMenu Instance { get; private set; }
    public static bool IsPaused => Instance != null && Instance.paused;

    [Tooltip("Scene loaded by Main menu.")]
    public string menuScene = "MainMenu";
    [Tooltip("Canvas units per art pixel of the panel and its text (720p reference).")]
    public float scale = 2f;
    [Range(0f, 1f)] public float dim = 0.6f;
    public float stickThreshold = 0.5f;
    public float stickRepeat = 0.3f;

    static readonly string[] Items = { "RESUME", "RESTART FROM CHECKPOINT", "MAIN MENU" };
    static readonly Color32 Gold = new Color32(0xff, 0xe0, 0x8a, 255);
    static readonly Color32 Muted = new Color32(0xb4, 0x9c, 0x78, 255);
    static readonly Color32 Ink = new Color32(0x1a, 0x10, 0x0a, 255);

    // Time and audio held by a pause (static: a scene load restores them even
    // if the menu object is already gone).
    static bool holding;
    static float heldTimeScale = 1f;

    bool paused;
    int selected;
    int stickDir;
    float repeatAt;
    bool thawPending;
    DawnEnding ending;

    Canvas canvas;
    RawImage[] normal, highlit;
    AudioSource ui;
    AudioClip click, confirm;
    readonly List<Texture2D> owned = new List<Texture2D>();
    readonly List<(PlayerMovement movement, bool wasEnabled)> frozen = new List<(PlayerMovement, bool)>();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Bootstrap()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        SceneManager.sceneLoaded += OnSceneLoaded;
        Setup(SceneManager.GetActiveScene());
    }

    static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        ReleaseTime();
        Setup(scene);
    }

    static void Setup(Scene scene)
    {
        if (Instance != null && Instance.gameObject.scene == scene) return;
        var flow = GameFlow.Instance != null ? GameFlow.Instance : FindAnyObjectByType<GameFlow>();
        if (flow == null || flow.gameObject.scene != scene) return;
        var go = new GameObject("PauseMenu");
        SceneManager.MoveGameObjectToScene(go, scene);
        go.AddComponent<PauseMenu>();
    }

    static void ReleaseTime()
    {
        if (!holding) return;
        holding = false;
        Time.timeScale = heldTimeScale > 0f ? heldTimeScale : 1f;
        AudioListener.pause = false;
    }

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        ending = FindAnyObjectByType<DawnEnding>();
        click = Resources.Load<AudioClip>("Audio/Sfx/ui_click");
        confirm = Resources.Load<AudioClip>("Audio/Sfx/ui_confirm");
        ui = gameObject.AddComponent<AudioSource>();
        ui.playOnAwake = false;
        ui.ignoreListenerPause = true;
        ui.spatialBlend = 0f;
        ui.volume = 0.35f;
        Build();
        canvas.enabled = false;
    }

    void OnDestroy()
    {
        if (paused) { paused = false; Thaw(); }
        ReleaseTime();
        foreach (var t in owned) if (t != null) Destroy(t);
        owned.Clear();
        if (Instance == this) Instance = null;
    }

    // --- input -----------------------------------------------------------------

    void Update()
    {
        var kb = Keyboard.current;
        bool toggle = kb != null && kb.escapeKey.wasPressedThisFrame;
        bool back = false;
        foreach (var pad in Gamepad.all)
        {
            toggle |= pad.startButton.wasPressedThisFrame;
            back |= pad.buttonEast.wasPressedThisFrame;
        }

        if (!paused)
        {
            if (toggle && CanOpen()) Pause();
            return;
        }

        if (toggle || back) { Resume(); return; }

        int move = 0;
        bool press = false;
        if (kb != null)
        {
            if (kb.downArrowKey.wasPressedThisFrame || kb.sKey.wasPressedThisFrame) move++;
            if (kb.upArrowKey.wasPressedThisFrame || kb.wKey.wasPressedThisFrame) move--;
            press |= kb.enterKey.wasPressedThisFrame || kb.numpadEnterKey.wasPressedThisFrame || kb.spaceKey.wasPressedThisFrame;
        }
        float y = 0f;
        foreach (var pad in Gamepad.all)
        {
            if (pad.dpad.down.wasPressedThisFrame) move++;
            if (pad.dpad.up.wasPressedThisFrame) move--;
            press |= pad.buttonSouth.wasPressedThisFrame;
            float v = pad.leftStick.y.ReadValue();
            if (Mathf.Abs(v) > Mathf.Abs(y)) y = v;
        }
        // Stick: one step on the push, then repeats while held.
        int dir = y > stickThreshold ? -1 : y < -stickThreshold ? 1 : 0;
        if (dir == 0) stickDir = 0;
        else if (dir != stickDir || Time.unscaledTime >= repeatAt)
        {
            move += dir;
            stickDir = dir;
            repeatAt = Time.unscaledTime + stickRepeat;
        }

        if (move != 0) Select(selected + move);
        if (press) Choose(selected);
    }

    // Movement comes back a frame after the resume, so the press that closed
    // the menu (A is also jump) is not read as a jump.
    void LateUpdate()
    {
        if (!thawPending) return;
        thawPending = false;
        Thaw();
    }

    bool CanOpen()
    {
        var flow = GameFlow.Instance;
        if (flow == null || flow.Finished) return false;
        if (ending != null && ending.Running) return false;
        return true;
    }

    // --- actions ---------------------------------------------------------------

    public void Pause()
    {
        if (paused) return;
        paused = true;
        if (!holding)
        {
            heldTimeScale = Time.timeScale;
            holding = true;
        }
        Time.timeScale = 0f;
        AudioListener.pause = true;
        Freeze();
        thawPending = false;
        Select(0, false);
        canvas.enabled = true;
        Play(click);
    }

    public void Resume()
    {
        if (!paused) return;
        Close();
        thawPending = true;
        Play(click);
    }

    void Choose(int item)
    {
        Play(confirm);
        switch (item)
        {
            case 0: Resume(); break;
            case 1:
            {
                Close();
                Thaw();
                // GameFlow's death sequence freezes the players again until the respawn ends.
                var flow = GameFlow.Instance;
                if (flow != null && !flow.Respawning && !flow.Finished) flow.KillAll();
                break;
            }
            case 2:
                if (!Application.CanStreamedLevelBeLoaded(menuScene))
                {
                    Debug.LogWarning($"[PauseMenu] Scene '{menuScene}' is not in Build Settings.");
                    Resume();
                    break;
                }
                Close();
                Thaw();
                SceneManager.LoadScene(menuScene);
                break;
        }
    }

    void Close()
    {
        paused = false;
        canvas.enabled = false;
        ReleaseTime();
    }

    void Freeze()
    {
        frozen.Clear();
        foreach (var m in FindObjectsByType<PlayerMovement>())
        {
            frozen.Add((m, m.enabled));
            m.enabled = false;
        }
    }

    void Thaw()
    {
        foreach (var (m, was) in frozen)
            if (m != null && was) m.enabled = true;
        frozen.Clear();
    }

    void Select(int item, bool sound = true)
    {
        item = (item % Items.Length + Items.Length) % Items.Length;
        if (sound && item != selected) Play(click);
        selected = item;
        for (int i = 0; i < Items.Length; i++)
        {
            normal[i].enabled = i != selected;
            highlit[i].enabled = i == selected;
        }
    }

    void Play(AudioClip clip)
    {
        if (ui != null && clip != null) ui.PlayOneShot(clip);
    }

    // --- look ------------------------------------------------------------------

    const int PanelW = 200, PanelH = 100;

    void Build()
    {
        var go = new GameObject("PauseCanvas", typeof(Canvas), typeof(CanvasScaler));
        go.transform.SetParent(transform, false);
        canvas = go.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 200;   // over the divider (10), HUD (11) and ending (50)
        var scaler = go.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1280f, 720f);
        scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
        scaler.matchWidthOrHeight = 1f;

        var shade = Image(go.transform, "Dim", Texture2D.whiteTexture, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
        shade.color = new Color(0.02f, 0.01f, 0.04f, dim);

        var panelTex = Own(PanelTexture());
        var panel = Image(go.transform, "Panel", panelTex, Half, Half,
            new Vector2(PanelW * scale, PanelH * scale), Vector2.zero);

        // Rows in art pixels from the panel's centre: title, rule, items, hint.
        Text(panel.transform, "Title", "PAUSED", Gold, Ink, scale * 2f, 34f);
        normal = new RawImage[Items.Length];
        highlit = new RawImage[Items.Length];
        for (int i = 0; i < Items.Length; i++)
        {
            float y = 8f - i * 14f;
            normal[i] = Text(panel.transform, Items[i], Items[i], Muted, Ink, scale, y);
            highlit[i] = Text(panel.transform, Items[i] + " (selected)", "> " + Items[i] + " <", Gold, Ink, scale, y);
        }
        var hint = Text(panel.transform, "Hint", "ESC TO RESUME", Muted, Ink, scale * 0.5f, -38f);
        hint.color = new Color(1f, 1f, 1f, 0.7f);
        Select(0, false);
    }

    static readonly Vector2 Half = new Vector2(0.5f, 0.5f);

    Texture2D Own(Texture2D t)
    {
        owned.Add(t);
        return t;
    }

    static RawImage Image(Transform parent, string name, Texture tex, Vector2 min, Vector2 max, Vector2 size, Vector2 pos)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(RawImage));
        go.transform.SetParent(parent, false);
        var rt = (RectTransform)go.transform;
        rt.anchorMin = min; rt.anchorMax = max; rt.sizeDelta = size; rt.anchoredPosition = pos;
        var img = go.GetComponent<RawImage>();
        img.texture = tex;
        img.raycastTarget = false;
        return img;
    }

    // Pixel-font text at s canvas units per pixel, centred y art pixels above the panel's centre.
    RawImage Text(Transform parent, string name, string text, Color32 fill, Color32 outline, float s, float y)
    {
        var tex = Own(PixelFont.Render(text, fill, outline));
        return Image(parent, name, tex, Half, Half, new Vector2(tex.width * s, tex.height * s), new Vector2(0f, Mathf.Round(y) * scale));
    }

    // Dark iron plate in a bevelled brass frame with rivets, and a brass rule
    // under the title. Pixel art, drawn once.
    static Texture2D PanelTexture()
    {
        int w = PanelW, h = PanelH;
        var px = new Color32[w * h];
        var outline = new Color32(0x14, 0x0c, 0x08, 255);
        var lit = new Color32(0xf0, 0xc8, 0x6a, 255);
        var brass = new Color32(0xb8, 0x86, 0x34, 255);
        var dark = new Color32(0x6e, 0x4a, 0x1c, 255);
        var rivet = new Color32(0xff, 0xec, 0xb0, 255);
        var rivetShade = new Color32(0x4a, 0x30, 0x12, 255);
        void Set(int x, int yTop, Color32 c) { if (x >= 0 && yTop >= 0 && x < w && yTop < h) px[(h - 1 - yTop) * w + x] = c; }

        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                int edge = Mathf.Min(Mathf.Min(x, w - 1 - x), Mathf.Min(y, h - 1 - y));
                Color32 c;
                if (edge == 0) c = outline;
                else if (edge <= 4)
                {
                    // Bevel: the top and left faces catch the light, bottom and right fall in shadow.
                    bool topLeft = edge == y || edge == x;
                    if (edge == 1) c = topLeft ? lit : dark;
                    else if (edge == 4) c = topLeft ? dark : lit;
                    else c = brass;
                }
                else if (edge == 5) c = outline;
                else
                {
                    // Iron plate, a touch lighter at the top, dithered between two shades.
                    float t = (float)y / h;
                    bool light = ((x + y) & 1) == 0 && t < 0.35f + 0.1f * ((x * 7 + y * 3) % 5) / 5f;
                    c = light ? new Color32(0x30, 0x29, 0x36, 240) : new Color32(0x24, 0x1e, 0x2a, 240);
                }
                Set(x, y, c);
            }

        // Rivets in the frame: corners and the middle of each side.
        int[] rx = { 2, w / 2 - 1, w - 4 }, ry = { 2, h / 2 - 1, h - 4 };
        foreach (int x in rx)
            foreach (int y in ry)
            {
                if (x == rx[1] && y == ry[1]) continue;
                Set(x, y, rivet); Set(x + 1, y, brass); Set(x, y + 1, brass); Set(x + 1, y + 1, rivetShade);
            }

        // Rule under the title (its centre is 34 px above the middle, 18 px tall at 4x).
        int rule = h / 2 - 24;
        for (int x = 16; x < w - 16; x++)
        {
            Set(x, rule, (x & 3) == 0 ? lit : brass);
            Set(x, rule + 1, dark);
        }
        Set(14, rule, rivet); Set(w - 15, rule, rivet);

        var tex = new Texture2D(w, h, TextureFormat.RGBA32, false)
        {
            filterMode = FilterMode.Point,
            wrapMode = TextureWrapMode.Clamp,
            hideFlags = HideFlags.HideAndDontSave,
            name = "PausePanel",
        };
        tex.SetPixels32(px);
        tex.Apply(false);
        return tex;
    }
}
