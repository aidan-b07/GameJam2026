using System;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

// Main menu: Play, Select Level, Settings, Quit.
//
// The menu is pixel art in world space, drawn by the menu camera into a
// 640x360 render texture that the screen canvas shows scaled up (screen).
// Selection follows the keyboard (arrows / W S, Enter / Space), any gamepad
// (d-pad or left stick, south button) and the mouse (hover, click). The
// selected plate lifts a pixel and gets its selected art, with a brass cog
// turning on each side; pressing pushes it down a pixel. Each change of
// selection makes the side rift flare.
//
// Play opens the main level, Select Level the level select, Quit closes the game (stops
// play mode in the editor), Settings has no screen yet and only logs. Every
// press also raises Activated with the button.
//
// Plate material and title font are switchable here; the sprites for every
// option are assigned by MainMenuBuilder, so this is a look you can change in
// the inspector, also in edit mode.
public class MainMenu : MonoBehaviour
{
    public enum Button { Play, SelectLevel, Settings, Quit }
    public enum PlateMaterial { Stone, Brass, Rift }
    public enum TitleFont { Jersey, Tiny, Workbench, Pixelify }

    [Serializable]
    public class PlateArt
    {
        [Tooltip("Per button, in Button order.")]
        public Sprite[] idle, selected, labelIdle, labelSelected;
    }

    [Header("Look")]
    public PlateMaterial plateMaterial = PlateMaterial.Stone;
    public PlateArt stone, brass, rift;
    public TitleFont titleFont = TitleFont.Jersey;
    [Tooltip("In TitleFont order.")]
    public Sprite[] titleSprites;

    [Header("Parts")]
    public SpriteRenderer title;
    [Tooltip("Plates and labels per button, in Button order.")]
    public SpriteRenderer[] plates, labels;
    public SpriteRenderer cogLeft, cogRight;
    public Sprite[] cogFrames;
    [Tooltip("Cog centre from the plate centre, in art pixels (the right cog mirrors it).")]
    public Vector2 cogOffset = new Vector2(-91f, 0f);
    public Camera menuCamera;
    [Tooltip("The screen image showing the menu camera's render texture (mouse picking).")]
    public RawImage screen;
    public SideRift sideRift;

    [Header("Feel")]
    [Tooltip("Plate size in art pixels, for mouse picking.")]
    public Vector2 plateSize = new Vector2(168f, 32f);
    public float pixelsPerUnit = 32f;
    public float pressTime = 0.14f;
    [Tooltip("Scene that Play opens (name in Build Settings).")]
    public string playScene = "MainLevel";
    [Tooltip("Scene that Select Level opens (name in Build Settings).")]
    public string levelSelectScene = "LevelSelect";
    [Tooltip("Seconds between a press and its scene change or quit, so the press shows.")]
    public float leaveDelay = 0.3f;
    [Tooltip("Seconds per cog frame.")]
    public float cogFrameTime = 0.09f;
    [Tooltip("Title bob, in art pixels, and its period in seconds.")]
    public float titleBob = 1f;
    public float titleBobPeriod = 5.6f;
    [Tooltip("Stick travel that counts as a press, and the repeat delay while held.")]
    public float stickThreshold = 0.5f;
    public float repeatDelay = 0.28f;

    [Header("State")]
    public int selected;

    // Raised when a button is pressed (index in Button order). Nothing listens yet.
    public event Action<Button> Activated;

    Vector3[] rest;           // plate rest positions
    Vector3 titleRest;
    int pressed = -1;
    float pressedAt, repeatAt;
    int stickDir;

    void Awake()
    {
        rest = new Vector3[plates.Length];
        for (int i = 0; i < plates.Length; i++) rest[i] = plates[i].transform.localPosition;
        if (title != null) titleRest = title.transform.localPosition;
        ApplyLook();
    }

    void OnValidate()
    {
        if (plates == null || labels == null) return;
        ApplyLook();
    }

    // Sprites for the chosen material and title font, and the selected state.
    public void ApplyLook()
    {
        var art = Art;
        for (int i = 0; i < plates.Length; i++)
        {
            bool sel = i == selected;
            if (plates[i] != null) plates[i].sprite = Pick(sel ? art?.selected : art?.idle, i, plates[i].sprite);
            if (i < labels.Length && labels[i] != null)
                labels[i].sprite = Pick(sel ? art?.labelSelected : art?.labelIdle, i, labels[i].sprite);
        }
        if (title != null) title.sprite = Pick(titleSprites, (int)titleFont, title.sprite);
    }

    PlateArt Art => plateMaterial == PlateMaterial.Brass ? brass : plateMaterial == PlateMaterial.Rift ? rift : stone;

    static Sprite Pick(Sprite[] set, int i, Sprite fallback) =>
        set != null && i >= 0 && i < set.Length && set[i] != null ? set[i] : fallback;

    void Update()
    {
        if (!leaving)
        {
            ReadKeyboard();
            ReadGamepad();
            ReadMouse();
        }
        Animate();
    }

    void ReadKeyboard()
    {
        var kb = Keyboard.current;
        if (kb == null) return;
        if (kb.downArrowKey.wasPressedThisFrame || kb.sKey.wasPressedThisFrame) Select(selected + 1);
        if (kb.upArrowKey.wasPressedThisFrame || kb.wKey.wasPressedThisFrame) Select(selected - 1);
        if (kb.enterKey.wasPressedThisFrame || kb.numpadEnterKey.wasPressedThisFrame || kb.spaceKey.wasPressedThisFrame)
            Press(selected);
    }

    void ReadGamepad()
    {
        foreach (var pad in Gamepad.all)
        {
            if (pad.dpad.down.wasPressedThisFrame) Select(selected + 1);
            if (pad.dpad.up.wasPressedThisFrame) Select(selected - 1);
            if (pad.buttonSouth.wasPressedThisFrame) Press(selected);
        }
        // Left stick of any pad: one step on the push, then repeats while held.
        float y = 0f;
        foreach (var pad in Gamepad.all)
        {
            float v = pad.leftStick.y.ReadValue();
            if (Mathf.Abs(v) > Mathf.Abs(y)) y = v;
        }
        int dir = y > stickThreshold ? -1 : y < -stickThreshold ? 1 : 0;
        if (dir == 0) { stickDir = 0; return; }
        if (dir != stickDir || Time.unscaledTime >= repeatAt)
        {
            Select(selected + dir);
            repeatAt = Time.unscaledTime + (dir != stickDir ? repeatDelay * 1.6f : repeatDelay);
            stickDir = dir;
        }
    }

    void ReadMouse()
    {
        var mouse = Mouse.current;
        if (mouse == null || menuCamera == null || screen == null) return;
        bool moved = mouse.delta.ReadValue().sqrMagnitude > 0.01f;
        bool clicked = mouse.leftButton.wasPressedThisFrame;
        if (!moved && !clicked) return;    // a resting mouse doesn't fight the keyboard

        var rect = screen.rectTransform;
        if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(rect, mouse.position.ReadValue(), null, out var local))
            return;
        var r = rect.rect;
        var viewport = new Vector2((local.x - r.x) / r.width, (local.y - r.y) / r.height);
        if (viewport.x < 0f || viewport.x > 1f || viewport.y < 0f || viewport.y > 1f) return;
        Vector3 world = menuCamera.ViewportToWorldPoint(viewport);

        int hit = -1;
        Vector2 half = plateSize / pixelsPerUnit / 2f;
        for (int i = 0; i < plates.Length; i++)
        {
            Vector3 c = plates[i].transform.parent.TransformPoint(rest[i]);
            if (Mathf.Abs(world.x - c.x) <= half.x && Mathf.Abs(world.y - c.y) <= half.y) { hit = i; break; }
        }
        if (hit < 0) return;
        Select(hit);
        if (clicked) Press(hit);
    }

    public void Select(int index)
    {
        int n = plates.Length;
        index = ((index % n) + n) % n;
        if (index == selected) return;
        selected = index;
        ApplyLook();
        if (sideRift != null) sideRift.Flare();
        Sfx.PlayCentred("ui_click", 0.6f);
    }

    public void Press(int index)
    {
        if (leaving || index < 0 || index >= plates.Length) return;
        if (index != selected) Select(index);
        pressed = index;
        pressedAt = Time.unscaledTime;
        if (sideRift != null) { sideRift.Flare(); sideRift.Flare(); }
        Sfx.PlayCentred("ui_confirm", 0.8f);
        var button = (Button)index;
        Activated?.Invoke(button);
        switch (button)
        {
            case Button.Play:
            case Button.SelectLevel:
            case Button.Quit:
                leaving = true;                  // ignore input until we go
                Invoke(button == Button.Quit ? nameof(QuitGame)
                    : button == Button.Play ? nameof(OpenPlayScene) : nameof(OpenLevelSelect), leaveDelay);
                break;
            default:
                Debug.Log($"[MainMenu] {button} pressed (no screen for it yet).");
                break;
        }
    }

    bool leaving;

    void OpenPlayScene() => SceneManager.LoadScene(playScene);

    void OpenLevelSelect() => SceneManager.LoadScene(levelSelectScene);

    void QuitGame()
    {
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }

    void Animate()
    {
        float px = 1f / pixelsPerUnit;
        bool pressing = pressed >= 0 && Time.unscaledTime - pressedAt < pressTime;
        for (int i = 0; i < plates.Length; i++)
        {
            float lift = i != selected ? 0f : pressing && pressed == i ? -px : px;
            plates[i].transform.localPosition = rest[i] + new Vector3(0f, lift, 0f);
        }

        var at = plates[selected].transform.localPosition;
        int frame = cogFrames != null && cogFrames.Length > 0
            ? Mathf.FloorToInt(Time.unscaledTime / Mathf.Max(0.01f, cogFrameTime)) % cogFrames.Length : 0;
        if (cogLeft != null)
        {
            cogLeft.transform.localPosition = at + (Vector3)(cogOffset * px);
            if (cogFrames != null && cogFrames.Length > 0) cogLeft.sprite = cogFrames[frame];
        }
        if (cogRight != null)
        {
            cogRight.transform.localPosition = at + new Vector3(-cogOffset.x, cogOffset.y, 0f) * px;
            if (cogFrames != null && cogFrames.Length > 0) cogRight.sprite = cogFrames[cogFrames.Length - 1 - frame];
        }

        if (title != null)
        {
            float bob = Mathf.Round(Mathf.Sin(Time.unscaledTime * Mathf.PI * 2f / Mathf.Max(0.1f, titleBobPeriod)) * titleBob);
            title.transform.localPosition = titleRest + new Vector3(0f, bob * px, 0f);
        }
    }
}
