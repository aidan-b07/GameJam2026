using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

// Sound effects for the split-screen pair. Every sound plays as plain 2D audio
// and this script sets its volume, pan and pitch itself, because Unity's 3D
// rolloff from one AudioListener cannot serve two lanes 40 units apart:
// - volume: full while the source is on screen (horizontally; both lanes are
//   always on screen), fading to silence `fadeDistance` past the screen edge;
// - pan: left or right by the position on screen, up to `maxPan` at the edge;
// - pitch: follows the lane's world speed (GameManager), so a fast lane sounds
//   higher (`pitchPerSpeed`, clamped to `minPitch`..`maxPitch`); a lane slower
//   than `silentBelowSpeed` fades out, silent when frozen.
// The Unraveling rift hums as a 2D loop that grows as the deeper lane rift opens (LaneRift).
//
// Mix: the effects sit under the music. `masterVolume` and the per-clip trims
// (`Trim`) bring the loud ones (plunger slam, warn blink, ratchet) down, every
// voice runs through a low-pass (`lowPassCutoff`) that takes the shrill top off
// the synthesised clips, and at most `maxSameClip` copies of one clip play at
// once, so a row of crushers firing together does not stack up.
//
// Clips are loaded by name from Resources/Audio/Sfx (made by tools/make_sfx.py
// in the jam workspace). The object creates itself on scene load, so the level
// builder does not need to place it, and gives every player a PlayerAudio.
public class Sfx : MonoBehaviour
{
    public static Sfx Instance { get; private set; }

    [Header("Distance")]
    [Tooltip("Units past the screen edge at which a sound has faded to silence.")]
    public float fadeDistance = 8f;
    [Tooltip("Stereo pan at the screen edge (0 = centre, 1 = one speaker).")]
    [Range(0f, 1f)] public float maxPan = 0.6f;

    [Header("Lane pitch")]
    [Tooltip("Pitch added per unit of lane world speed above 1.")]
    public float pitchPerSpeed = 0.06f;
    public float minPitch = 0.7f;
    public float maxPitch = 1.2f;
    [Tooltip("Lane speed below which a lane's sounds fade out, silent at 0 (a frozen lane).")]
    public float silentBelowSpeed = 0.25f;

    [Header("Rift hum")]
    public float riftMinVolume = 0.08f;
    public float riftMaxVolume = 0.35f;
    [Tooltip("Rift openness (LaneRift.Openness, 0..1) at which the hum is about 63 % of the way to its maximum.")]
    public float riftOpenScale = 0.25f;

    [Header("Mix")]
    [Range(0f, 1f)] public float masterVolume = 0.5f;
    [Tooltip("Low-pass cutoff on every effect (Hz); 22000 leaves them unfiltered.")]
    public float lowPassCutoff = 4000f;
    [Tooltip("Most copies of one clip that play at once; further ones are skipped.")]
    public int maxSameClip = 2;
    [Tooltip("Voices for one-shot sounds; the oldest is reused when all are busy.")]
    public int voices = 24;

    // World y below which a position counts as Tock's lane (lanes at 0 and -40).
    const float LaneSplitY = -20f;
    const string ClipFolder = "Audio/Sfx/";

    // Per-clip volume on top of the caller's: the obstacle sounds repeat all
    // through a level, so they sit lowest.
    static readonly Dictionary<string, float> Trim = new Dictionary<string, float>
    {
        { "plunger_slam", 0.55f },
        { "plunger_warn", 0.4f },
        { "ratchet_click", 0.45f },
        { "bridge_slam", 0.7f },
        { "ui_confirm", 0.7f },
        { "checkpoint", 0.8f },
    };

    class Voice
    {
        public AudioSource source;
        public Vector3 position;
        public int lane;            // -1: not pitched by lane time
        public bool positional;     // false: centred, full volume
        public float volume;
        public float pitch;
        public float started;
    }

    readonly List<Voice> pool = new List<Voice>();
    readonly List<SfxLoop> loops = new List<SfxLoop>();
    readonly Dictionary<string, AudioClip> clips = new Dictionary<string, AudioClip>();
    CoopCamera rig;
    AudioSource rift;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Create()
    {
        if (Instance == null) new GameObject("Sfx").AddComponent<Sfx>();
    }

    static void AddPlayerAudio(Scene scene, LoadSceneMode mode)
    {
        foreach (var p in FindObjectsByType<PlayerMovement>())
            if (p.GetComponent<PlayerAudio>() == null) p.gameObject.AddComponent<PlayerAudio>();
    }

    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
        DontDestroyOnLoad(gameObject);
        SceneManager.sceneLoaded += AddPlayerAudio;
        AddPlayerAudio(SceneManager.GetActiveScene(), LoadSceneMode.Single);

        for (int i = 0; i < voices; i++)
        {
            var src = NewSource("Voice_" + i);
            src.playOnAwake = false;
            src.spatialBlend = 0f;
            pool.Add(new Voice { source = src });
        }

        rift = NewSource("Rift");
        rift.clip = Clip("rift_loop");
        rift.loop = true;
        rift.spatialBlend = 0f;
        rift.volume = 0f;
        if (rift.clip != null) rift.Play();
    }

    void OnDestroy()
    {
        if (Instance != this) return;
        Instance = null;
        SceneManager.sceneLoaded -= AddPlayerAudio;
    }

    // --- public API ---------------------------------------------------------

    // A sound from a place in the level. lane: -1 = pick by y; pitched = follows
    // the lane's world speed (obstacles), false for the players' own sounds.
    public static void Play(string clip, Vector3 position, float volume = 1f, bool pitched = true,
                            float pitch = 1f, int lane = -1)
    {
        if (Instance == null) return;
        Instance.Begin(clip, position, true, volume, pitch, pitched ? (lane >= 0 ? lane : LaneOf(position)) : -1);
    }

    // A sound with no place (death, respawn, checkpoint): centred, full volume.
    public static void PlayCentred(string clip, float volume = 1f, float pitch = 1f)
    {
        if (Instance == null) return;
        Instance.Begin(clip, Vector3.zero, false, volume, pitch, -1);
    }

    // A looping sound owned by an object; call SfxLoop.Set every frame.
    public static SfxLoop Loop(string clip)
    {
        if (Instance == null) Create();
        var src = Instance.NewSource("Loop_" + clip);
        src.clip = Instance.Clip(clip);
        src.loop = true;
        src.playOnAwake = false;
        src.spatialBlend = 0f;
        var loop = new SfxLoop(src);
        Instance.loops.Add(loop);
        return loop;
    }

    public static void Release(SfxLoop loop)
    {
        if (Instance == null || loop == null) return;
        Instance.loops.Remove(loop);
        if (loop.source != null) Destroy(loop.source.gameObject);
    }

    public static int LaneOf(Vector3 position) => position.y < LaneSplitY ? 1 : 0;

    // --- mixing -------------------------------------------------------------

    void Begin(string name, Vector3 position, bool positional, float volume, float pitch, int lane)
    {
        AudioClip clip = Clip(name);
        if (clip == null) return;
        int same = 0;
        foreach (var candidate in pool)
            if (candidate.source.isPlaying && candidate.source.clip == clip) same++;
        if (same >= maxSameClip) return;
        if (Trim.TryGetValue(name, out float trim)) volume *= trim;

        Voice v = null;
        foreach (var candidate in pool)
            if (!candidate.source.isPlaying) { v = candidate; break; }
        if (v == null)
        {
            v = pool[0];
            foreach (var candidate in pool)
                if (candidate.started < v.started) v = candidate;
        }

        v.position = position;
        v.positional = positional;
        v.lane = lane;
        v.volume = volume;
        v.pitch = pitch;
        v.started = Time.unscaledTime;
        Apply(v.source, v.position, v.positional, v.volume, v.pitch, v.lane);
        v.source.clip = clip;
        v.source.Play();
    }

    void LateUpdate()
    {
        if (rig == null) rig = FindAnyObjectByType<CoopCamera>();

        foreach (var v in pool)
            if (v.source.isPlaying) Apply(v.source, v.position, v.positional, v.volume, v.pitch, v.lane);

        foreach (var l in loops)
        {
            if (l.source == null) continue;
            if (l.playing && !l.source.isPlaying) l.source.Play();
            else if (!l.playing && l.source.isPlaying) l.source.Stop();
            if (l.playing) Apply(l.source, l.position, true, l.volume, l.pitch, l.lane);
        }

        if (rift != null)
        {
            float open = LaneRift.Instance != null ? LaneRift.Instance.Openness : 0f;
            float k = 1f - Mathf.Exp(-open / Mathf.Max(0.01f, riftOpenScale));
            rift.volume = masterVolume * Mathf.Lerp(riftMinVolume, riftMaxVolume, k);
        }
    }

    void Apply(AudioSource src, Vector3 position, bool positional, float volume, float pitch, int lane)
    {
        float gain = 1f, pan = 0f;
        if (positional && rig != null)
        {
            float half = Mathf.Max(1f, rig.HalfWidth);
            float dx = position.x - rig.X;
            float outside = Mathf.Abs(dx) - half;
            gain = outside <= 0f ? 1f : Mathf.Clamp01(1f - outside / Mathf.Max(0.01f, fadeDistance));
            gain *= gain;   // eases out, sounds more natural than linear
            pan = Mathf.Clamp(dx / half, -1f, 1f) * maxPan;
        }
        gain *= LaneGain(lane);
        src.volume = masterVolume * volume * gain;
        src.panStereo = pan;
        src.pitch = pitch * LanePitch(lane);
    }

    // A frozen lane is silent: its sounds (the winch loop, say) fade out as it stops.
    float LaneGain(int lane)
    {
        var gm = GameManager.INSTANCE;
        if (lane < 0 || gm == null || silentBelowSpeed <= 0f) return 1f;
        return Mathf.Clamp01(gm.GetWorldSpeed(lane) / silentBelowSpeed);
    }

    float LanePitch(int lane)
    {
        var gm = GameManager.INSTANCE;
        if (lane < 0 || gm == null) return 1f;
        return Mathf.Clamp(1f + (gm.GetWorldSpeed(lane) - 1f) * pitchPerSpeed, minPitch, maxPitch);
    }

    // An AudioSource on its own child, so an AudioLowPassFilter there filters
    // only it (a filter acts on the sources of its own GameObject).
    AudioSource NewSource(string name)
    {
        var go = new GameObject(name);
        go.transform.SetParent(transform, false);
        var src = go.AddComponent<AudioSource>();
        var lp = go.AddComponent<AudioLowPassFilter>();
        lp.cutoffFrequency = lowPassCutoff;
        lp.lowpassResonanceQ = 1f;
        return src;
    }

    AudioClip Clip(string name)
    {
        if (!clips.TryGetValue(name, out var clip))
        {
            clip = Resources.Load<AudioClip>(ClipFolder + name);
            if (clip == null) Debug.LogWarning($"Sfx: no clip Resources/{ClipFolder}{name}");
            clips[name] = clip;
        }
        return clip;
    }
}

// Handle for a looping sound; the owner sets it every frame.
public class SfxLoop
{
    public readonly AudioSource source;
    public bool playing;
    public Vector3 position;
    public int lane;
    public float volume = 1f;
    public float pitch = 1f;

    public SfxLoop(AudioSource source) { this.source = source; }

    public void Set(bool playing, Vector3 position, float volume = 1f)
    {
        this.playing = playing;
        this.position = position;
        this.volume = volume;
        lane = Sfx.LaneOf(position);
    }
}
