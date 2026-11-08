using UnityEngine;

// Background music: one looping 2D track that plays from the first scene on and
// carries on across scene loads (menu into level) without restarting.
//
// The clip is loaded by name from Resources/Audio/Music (made by
// tools/make_music.py in the jam workspace; the track loops seamlessly). The
// object creates itself on scene load, so no scene needs to place it.
public class Music : MonoBehaviour
{
    public static Music Instance { get; private set; }

    [Tooltip("Clip name in Resources/Audio/Music.")]
    public string track = "clockwork_rush";
    [Range(0f, 1f)] public float volume = 0.5f;
    [Tooltip("Seconds to fade in when the music starts.")]
    public float fadeIn = 2f;

    const string ClipFolder = "Audio/Music/";

    AudioSource source;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Create()
    {
        if (Instance == null) new GameObject("Music").AddComponent<Music>();
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

        source = gameObject.AddComponent<AudioSource>();
        source.playOnAwake = false;
        source.loop = true;
        source.spatialBlend = 0f;
        source.volume = 0f;
        source.clip = Resources.Load<AudioClip>(ClipFolder + track);
        if (source.clip == null)
        {
            Debug.LogWarning($"Music: no clip '{track}' in Resources/{ClipFolder}");
            return;
        }
        source.Play();
    }

    void Update()
    {
        if (source == null || source.clip == null) return;
        float step = fadeIn > 0f ? volume / fadeIn * Time.unscaledDeltaTime : volume;
        source.volume = Mathf.MoveTowards(source.volume, volume, step);
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }
}
