Bomb battle is a Unity multiplayer arena game where players control different ship classes with unique abilities and compete to eliminate each other using bombs, movement, and strategy. The last surviving player earns points, and rounds reset until a winner is declared.

The game is still in early development and not yet finished.

---

## Audio System

### Files

| File | Type | Purpose |
|---|---|---|
| `AudioIds.cs` | Plain C# | Enums — add IDs here as you grow |
| `AudioManager.cs` | MonoBehaviour singleton | SFX pool, music, volume |
| `NetworkAudioPlayer.cs` | NetworkBehaviour | RPC relay for networked sounds |

### Scene setup (one-time)

1. **AudioManager** — create an empty GameObject in your first scene, attach `AudioManager`. It will `DontDestroyOnLoad` itself. Fill the **SFX Entries** and **Music Entries** arrays in the Inspector (id → AudioClip → volume → distances).

2. **NetworkAudioPlayer** — add the `NetworkAudioPlayer` component to any existing `NetworkObject` in the game scene (the GameManager object is the obvious choice), or on a dedicated empty NetworkObject.

3. **AudioListener** — make sure the main camera (or follow-camera) still has an `AudioListener` so positional audio works.

### How to trigger sounds

```csharp
// Non-networked local music (call from UIManager, GameManager, etc.)
AudioManager.Instance.PlayMusic(MusicTrackId.InGame);
AudioManager.Instance.PlayMusic(MusicTrackId.MainMenu);

// Non-spatial SFX all clients hear identically (round start, countdown, etc.)
NetworkAudioPlayer.Instance.PlaySFXServerRpc(SFXId.RoundStart);

// Spatial SFX — every client hears it at the right world position
// e.g. in DrakkarWall.OnTriggerEnter, Bomb.OnTriggerEnter, etc.
NetworkAudioPlayer.Instance.PlaySpatialSFXServerRpc(SFXId.BombExplosion, transform.position);
NetworkAudioPlayer.Instance.PlaySpatialSFXServerRpc(SFXId.DrakkarWave, transform.position);
```

### Options menu (future)

When you build the audio settings screen, wire the sliders directly to:

```csharp
AudioManager.Instance.SetMusicVolume(slider.value);  // 0–1
AudioManager.Instance.SetSFXVolume(slider.value);    // 0–1
```

Values are read back via `AudioManager.Instance.MusicVolume` / `.SFXVolume`, and both are saved automatically to `PlayerPrefs`.
