using System.Collections.Generic;
using UnityEngine;

// ── Inspector data classes ────────────────────────────────────────────────────

[System.Serializable]
public class SFXEntry
{
    public SFXId id;
    public AudioClip clip;
    [Range(0f, 1f)] public float volume      = 1f;
    public float minDistance                 = 5f;   // full-volume radius
    public float maxDistance                 = 40f;  // silence radius
}

[System.Serializable]
public class MusicEntry
{
    public MusicTrackId id;
    public AudioClip clip;
    [Range(0f, 1f)] public float volume      = 0.7f;
}

// ── Manager ───────────────────────────────────────────────────────────────────

/// <summary>
/// Central audio singleton. Handles music playback and a pooled SFX system.
///
/// Scene setup:
///   • Place AudioManager on a GameObject in your first scene — it will
///     persist across all scenes via DontDestroyOnLoad.
///   • Populate the SFX Entries and Music Entries arrays in the Inspector.
///   • Make sure the scene has an AudioListener (usually on the main camera).
///
/// For sounds triggered by players over the network use NetworkAudioPlayer,
/// which calls PlaySpatialSFX / PlaySFX on every client from a single RPC.
/// </summary>
public class AudioManager : MonoBehaviour
{
    public static AudioManager Instance { get; private set; }

    [Header("Library")]
    [SerializeField] private SFXEntry[]   sfxEntries;
    [SerializeField] private MusicEntry[] musicEntries;

    [Header("Pool")]
    [Tooltip("How many SFX can play simultaneously before oldest is stolen.")]
    [SerializeField] private int sfxPoolSize = 16;

    // ── Runtime ──────────────────────────────────────────────────────────────

    private AudioSource   _musicSource;
    private AudioSource[] _sfxPool;
    private int           _poolIndex;        // next candidate for round-robin steal
    private MusicEntry    _currentMusic;

    private Dictionary<SFXId,       SFXEntry>   _sfxMap;
    private Dictionary<MusicTrackId, MusicEntry> _musicMap;

    // Exposed so the future options menu can read current values
    public float MusicVolume { get; private set; }
    public float SFXVolume   { get; private set; }

    // ── Unity lifecycle ───────────────────────────────────────────────────────

    private void Awake()
    {
        if (Instance != null) { Destroy(gameObject); return; }
        Instance = this;
        DontDestroyOnLoad(gameObject);

        BuildLookups();
        BuildMusicSource();
        BuildSFXPool();
        LoadVolumes();
    }

    // ── Initialization helpers ────────────────────────────────────────────────

    private void BuildLookups()
    {
        _sfxMap   = new Dictionary<SFXId,        SFXEntry>();
        _musicMap = new Dictionary<MusicTrackId, MusicEntry>();

        if (sfxEntries   != null) foreach (var e in sfxEntries)   _sfxMap[e.id]   = e;
        if (musicEntries != null) foreach (var e in musicEntries) _musicMap[e.id] = e;
    }

    private void BuildMusicSource()
    {
        var go    = new GameObject("Music_Source");
        go.transform.SetParent(transform);
        _musicSource              = go.AddComponent<AudioSource>();
        _musicSource.playOnAwake  = false;
        _musicSource.loop         = true;
        _musicSource.spatialBlend = 0f;   // always 2D
        _musicSource.dopplerLevel = 0f;
    }

    private void BuildSFXPool()
    {
        _sfxPool = new AudioSource[sfxPoolSize];
        for (int i = 0; i < sfxPoolSize; i++)
        {
            var go   = new GameObject($"SFX_Source_{i}");
            go.transform.SetParent(transform);
            var src              = go.AddComponent<AudioSource>();
            src.playOnAwake      = false;
            src.dopplerLevel     = 0f;
            _sfxPool[i]          = src;
        }
    }

    private void LoadVolumes()
    {
        MusicVolume = PlayerPrefs.GetFloat("MusicVolume", 1f);
        SFXVolume   = PlayerPrefs.GetFloat("SFXVolume",   1f);
    }

    // ── Music API ─────────────────────────────────────────────────────────────

    /// <summary>Start playing a music track. Ignored if already playing the same track.</summary>
    public void PlayMusic(MusicTrackId id, bool forceRestart = false)
    {
        if (!_musicMap.TryGetValue(id, out var entry) || entry.clip == null) return;
        if (!forceRestart && _musicSource.clip == entry.clip && _musicSource.isPlaying) return;

        _currentMusic        = entry;
        _musicSource.clip    = entry.clip;
        _musicSource.volume  = entry.volume * MusicVolume;
        _musicSource.Play();
    }

    public void StopMusic() => _musicSource.Stop();

    // ── SFX API ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Play a 2-D (non-spatial) SFX locally on this client.
    /// For game-event sounds all players should hear identically (round start, etc.).
    /// </summary>
    public void PlaySFX(SFXId id) => PlaySFXInternal(id, Vector3.zero, spatial: false);

    /// <summary>
    /// Play a spatial SFX at a world-space position locally on this client.
    /// For player-triggered spatial sounds, call NetworkAudioPlayer.Instance
    /// .PlaySpatialSFXServerRpc() instead so all clients hear it.
    /// </summary>
    public void PlaySpatialSFX(SFXId id, Vector3 position) => PlaySFXInternal(id, position, spatial: true);

    private void PlaySFXInternal(SFXId id, Vector3 position, bool spatial)
    {
        if (!_sfxMap.TryGetValue(id, out var entry) || entry.clip == null) return;

        AudioSource src          = GetFreeSource();
        src.transform.position   = position;
        src.spatialBlend         = spatial ? 1f : 0f;
        src.minDistance          = entry.minDistance;
        src.maxDistance          = entry.maxDistance;
        src.rolloffMode          = AudioRolloffMode.Linear;
        src.clip                 = entry.clip;
        src.volume               = entry.volume * SFXVolume;
        src.Play();
    }

    private AudioSource GetFreeSource()
    {
        // Prefer a source that has finished playing
        foreach (var src in _sfxPool)
            if (!src.isPlaying) return src;

        // All busy: stop and steal the next one in round-robin order
        AudioSource stolen = _sfxPool[_poolIndex];
        stolen.Stop();
        _poolIndex = (_poolIndex + 1) % _sfxPool.Length;
        return stolen;
    }

    // ── Volume API (wired up by the future options menu) ─────────────────────

    /// <summary>Set master music volume [0–1] and persist it.</summary>
    public void SetMusicVolume(float volume)
    {
        MusicVolume = Mathf.Clamp01(volume);
        if (_currentMusic != null)
            _musicSource.volume = _currentMusic.volume * MusicVolume;
        PlayerPrefs.SetFloat("MusicVolume", MusicVolume);
    }

    /// <summary>Set master SFX volume [0–1] and persist it.</summary>
    public void SetSFXVolume(float volume)
    {
        SFXVolume = Mathf.Clamp01(volume);
        PlayerPrefs.SetFloat("SFXVolume", SFXVolume);
    }
}
