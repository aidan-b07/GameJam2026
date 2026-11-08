using System;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

// Level select: eight level cards in a 4x2 grid and a Back plate, drawn like the
// main menu (pixel art in world space, rendered into the menu screen texture).
//
// Cards with a scene are open: they can be selected and pressing one loads
// its scene. Cards without a scene are locked ("coming soon"): the cursor
// skips them, and clicking one shakes it and its padlock with a dull click.
// There is no level progression lock: every open level can be played.
//
// Input: arrows / WASD / d-pad / left stick move between the open cards and
// Back (by direction on screen), Enter / Space / south button press, Escape /
// east button goes back. The mouse hovers and clicks. The selected item lifts
// a pixel with a brass cog turning on each side, and the side rift flares.
public class LevelSelect : MonoBehaviour
{
    [Serializable]
    public class Card
    {
        public SpriteRenderer renderer;
        public Sprite idle, selected, locked;
        [Tooltip("Scene to load (name in Build Settings). Empty = locked, coming soon.")]
        public string scene;
        [Tooltip("Card size in art pixels, for mouse picking and the cogs.")]
        public Vector2 size = new Vector2(116f, 100f);
        public bool Open => !string.IsNullOrEmpty(scene);
    }

    [Header("Items")]
    public Card[] cards;
    public SpriteRenderer back;
    public Sprite backIdle, backSelected;
    public Vector2 backSize = new Vector2(64f, 22f);
    [Tooltip("Scene the Back plate and Escape go to.")]
    public string backScene = "MainMenu";

    [Header("Parts")]
    public SpriteRenderer cogLeft, cogRight;
    public Sprite[] cogFrames;
    [Tooltip("Gap between an item's edge and the centre of its cog, in art pixels.")]
    public float cogGap = 11f;
    [Tooltip("How far a card's corner cogs sit in from its corners, in art pixels.")]
    public float cogInset = 2f;
    public SpriteRenderer header;
    public Camera menuCamera;
    public RawImage screen;
    public SideRift sideRift;

    [Header("Feel")]
    public float pixelsPerUnit = 32f;
    public float pressTime = 0.14f;
    [Tooltip("Seconds between pressing an open card and its scene loading, so the press shows.")]
    public float loadDelay = 0.3f;
    public float cogFrameTime = 0.09f;
    public float shakeTime = 0.3f;
    public float stickThreshold = 0.5f;
    public float repeatDelay = 0.28f;

    [Header("State")]
    [Tooltip("Selected item: a card index, or cards.Length for Back.")]
    public int selected;

    // Raised just before a level loads, with its scene name.
    public event Action<string> LevelChosen;

    Vector3[] rest;           // rest positions: cards, then Back
    int pressed = -1, shaking = -1;
    float pressedAt, shakeAt, repeatAt;
    Vector2Int stickDir;
    bool loading;

    int Count => cards.Length + 1;
    Transform Item(int i) => i < cards.Length ? cards[i].renderer.transform : back.transform;
    Vector2 Size(int i) => i < cards.Length ? cards[i].size : backSize;
    bool Selectable(int i) => i >= cards.Length || cards[i].Open;

    void Awake()
    {
        rest = new Vector3[Count];
        for (int i = 0; i < Count; i++) rest[i] = Item(i).localPosition;
        if (!Selectable(selected)) selected = FirstOpen();
        ApplyLook();
    }

    void OnValidate()
    {
        if (cards == null || back == null) return;
        ApplyLook();
    }

    int FirstOpen()
    {
        for (int i = 0; i < cards.Length; i++) if (cards[i].Open) return i;
        return cards.Length;
    }

    public void ApplyLook()
    {
        for (int i = 0; i < cards.Length; i++)
        {
            var c = cards[i];
            if (c.renderer == null) continue;
            var s = !c.Open ? c.locked : i == selected ? c.selected : c.idle;
            if (s != null) c.renderer.sprite = s;
        }
        var b = selected == cards.Length ? backSelected : backIdle;
        if (back != null && b != null) back.sprite = b;
    }

    void Update()
    {
        if (loading) { Animate(); return; }
        ReadKeyboard();
        ReadGamepad();
        ReadMouse();
        Animate();
    }

    void ReadKeyboard()
    {
        var kb = Keyboard.current;
        if (kb == null) return;
        if (kb.rightArrowKey.wasPressedThisFrame || kb.dKey.wasPressedThisFrame) Move(Vector2Int.right);
        if (kb.leftArrowKey.wasPressedThisFrame || kb.aKey.wasPressedThisFrame) Move(Vector2Int.left);
        if (kb.downArrowKey.wasPressedThisFrame || kb.sKey.wasPressedThisFrame) Move(Vector2Int.down);
        if (kb.upArrowKey.wasPressedThisFrame || kb.wKey.wasPressedThisFrame) Move(Vector2Int.up);
        if (kb.enterKey.wasPressedThisFrame || kb.numpadEnterKey.wasPressedThisFrame || kb.spaceKey.wasPressedThisFrame)
            Press(selected);
        if (kb.escapeKey.wasPressedThisFrame || kb.backspaceKey.wasPressedThisFrame) Press(cards.Length);
    }

    void ReadGamepad()
    {
        var stick = Vector2.zero;
        foreach (var pad in Gamepad.all)
        {
            if (pad.dpad.right.wasPressedThisFrame) Move(Vector2Int.right);
            if (pad.dpad.left.wasPressedThisFrame) Move(Vector2Int.left);
            if (pad.dpad.down.wasPressedThisFrame) Move(Vector2Int.down);
            if (pad.dpad.up.wasPressedThisFrame) Move(Vector2Int.up);
            if (pad.buttonSouth.wasPressedThisFrame) Press(selected);
            if (pad.buttonEast.wasPressedThisFrame) Press(cards.Length);
            var v = pad.leftStick.ReadValue();
            if (v.sqrMagnitude > stick.sqrMagnitude) stick = v;
        }
        // One step on the push, then repeats while held.
        var dir = Vector2Int.zero;
        if (Mathf.Abs(stick.x) > stickThreshold && Mathf.Abs(stick.x) >= Mathf.Abs(stick.y)) dir.x = stick.x > 0 ? 1 : -1;
        else if (Mathf.Abs(stick.y) > stickThreshold) dir.y = stick.y > 0 ? 1 : -1;
        if (dir == Vector2Int.zero) { stickDir = dir; return; }
        if (dir != stickDir || Time.unscaledTime >= repeatAt)
        {
            Move(dir);
            repeatAt = Time.unscaledTime + (dir != stickDir ? repeatDelay * 1.6f : repeatDelay);
            stickDir = dir;
        }
    }

    // To the nearest selectable item that lies in that direction on screen,
    // favouring ones straight ahead over ones off to the side.
    void Move(Vector2Int dir)
    {
        Vector2 from = rest[selected], d = dir;
        int best = -1;
        float bestScore = float.MaxValue;
        for (int i = 0; i < Count; i++)
        {
            if (i == selected || !Selectable(i)) continue;
            Vector2 to = (Vector2)rest[i] - from;
            float ahead = Vector2.Dot(to, d);
            if (ahead <= 0.01f) continue;
            float side = Mathf.Abs(to.x * d.y - to.y * d.x);
            float score = ahead + side * 2f;
            if (score < bestScore) { bestScore = score; best = i; }
        }
        if (best >= 0) Select(best);
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

        for (int i = 0; i < Count; i++)
        {
            Vector3 c = Item(i).parent.TransformPoint(rest[i]);
            Vector2 half = Size(i) / pixelsPerUnit / 2f;
            if (Mathf.Abs(world.x - c.x) > half.x || Mathf.Abs(world.y - c.y) > half.y) continue;
            if (Selectable(i))
            {
                Select(i);
                if (clicked) Press(i);
            }
            else if (clicked) Locked(i);
            return;
        }
    }

    public void Select(int index)
    {
        if (index < 0 || index >= Count || !Selectable(index) || index == selected) return;
        selected = index;
        ApplyLook();
        if (sideRift != null) sideRift.Flare();
        Sfx.PlayCentred("ui_click", 0.6f);
    }

    public void Press(int index)
    {
        if (loading || index < 0 || index >= Count) return;
        if (!Selectable(index)) { Locked(index); return; }
        if (index != selected) Select(index);
        pressed = index;
        pressedAt = Time.unscaledTime;
        if (sideRift != null) { sideRift.Flare(); sideRift.Flare(); }
        Sfx.PlayCentred("ui_confirm", 0.8f);
        string scene = index < cards.Length ? cards[index].scene : backScene;
        if (string.IsNullOrEmpty(scene)) return;
        if (index < cards.Length) LevelChosen?.Invoke(scene);
        loading = true;
        Invoke(nameof(Load), loadDelay);
        nextScene = scene;
    }

    string nextScene;
    void Load() => SceneManager.LoadScene(nextScene);

    // A locked card: shake it, with a dull click.
    void Locked(int index)
    {
        shaking = index;
        shakeAt = Time.unscaledTime;
        Sfx.PlayCentred("ratchet_click", 0.7f, 0.7f);
    }

    void Animate()
    {
        float px = 1f / pixelsPerUnit;
        bool pressing = pressed >= 0 && Time.unscaledTime - pressedAt < pressTime;
        float shakeT = Time.unscaledTime - shakeAt;
        for (int i = 0; i < Count; i++)
        {
            float lift = i != selected ? 0f : pressing && pressed == i ? -px : px;
            float dx = 0f;
            if (i == shaking && shakeT < shakeTime)
                dx = Mathf.Round(Mathf.Sin(shakeT * 60f) * 2f * (1f - shakeT / shakeTime)) * px;
            Item(i).localPosition = rest[i] + new Vector3(dx, lift, 0f);
        }

        // Cogs: pinned on a selected card's bottom corners (the cards sit too
        // close together for side cogs), beside the Back plate.
        var at = Item(selected).localPosition;
        float off = (Size(selected).x / 2f + cogGap) * px;
        if (selected < cards.Length)
        {
            var corner = Size(selected) / 2f - Vector2.one * cogInset;
            at += new Vector3(0f, -corner.y * px, 0f);
            off = corner.x * px;
        }
        int frame = cogFrames != null && cogFrames.Length > 0
            ? Mathf.FloorToInt(Time.unscaledTime / Mathf.Max(0.01f, cogFrameTime)) % cogFrames.Length : 0;
        if (cogLeft != null)
        {
            cogLeft.transform.localPosition = at + new Vector3(-off, 0f, 0f);
            if (cogFrames != null && cogFrames.Length > 0) cogLeft.sprite = cogFrames[frame];
        }
        if (cogRight != null)
        {
            cogRight.transform.localPosition = at + new Vector3(off, 0f, 0f);
            if (cogFrames != null && cogFrames.Length > 0) cogRight.sprite = cogFrames[cogFrames.Length - 1 - frame];
        }
    }
}
