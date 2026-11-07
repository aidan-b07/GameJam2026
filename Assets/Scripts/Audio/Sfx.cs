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
//   higher (`pitchPerSpeed`, clamped to `minPitch`..`maxPitch`).
// The Unraveling rift hums as a 2D loop that grows with the lanes' speed gap.
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
    public float pitchPerSpeed = 0.12f;
    public float minPitch = 0.5f;
    public float maxPitch = 1.6f;

    [Header("Rift hum")]
    public float riftMinVolume = 0.12f;
    public float riftMaxVolume = 0.5f;
    [Tooltip("Speed gap between the lanes at which the hum is about 63 % of the way to its maximum.")]
    public float riftGapScale = 2f;

    [Header("Mix")]
    [Range(0f, 1f)] public float masterVolume = 1f;
    [Tooltip("Voices for one-shot sounds; the oldest is reused when all are busy.")]
    public int voices = 24;

    // World y below which a position counts as Tock's lane (lanes at 0 and -40).
    const float LaneSplitY = -20f;
    const string ClipFolder = "Audio/Sfx/";

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
            var src = gameObject.AddComponent<AudioSource>();
            src.playOnAwake = false;
            src.spatialBlend = 0f;
            pool.Add(new Voice { source = src });
        }

        rift = gameObject.AddComponent<AudioSource>();
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
        var src = Instance.gameObject.AddComponent<AudioSource>();
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
        if (loop.source != null) Destroy(loop.source);
    }

    public static int LaneOf(Vector3 position) => position.y < LaneSplitY ? 1 : 0;

    // --- mixing -------------------------------------------------------------

    void Begin(string name, Vector3 position, bool positional, float volume, float pitch, int lane)
    {
        AudioClip clip = Clip(name);
        if (clip == null) return;

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

        var gm = GameManager.INSTANCE;
        if (rift != null)
        {
            float gap = gm != null ? Mathf.Abs(gm.GetWorldSpeed(0) - gm.GetWorldSpeed(1)) : 0f;
            float k = 1f - Mathf.Exp(-gap / Mathf.Max(0.01f, riftGapScale));
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
        src.volume = masterVolume * volume * gain;
        src.panStereo = pan;
        src.pitch = pitch * LanePitch(lane);
    }

    float LanePitch(int lane)
    {
        var gm = GameManager.INSTANCE;
        if (lane < 0 || gm == null) return 1f;
        return Mathf.Clamp(1f + (gm.GetWorldSpeed(lane) - 1f) * pitchPerSpeed, minPitch, maxPitch);
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
