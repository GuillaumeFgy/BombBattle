/// <summary>
/// All sound effect IDs. Add entries here when a new SFX is needed,
/// then assign a clip to it in the AudioManager inspector.
/// byte backing keeps RPC payload small.
/// </summary>
public enum SFXId : byte
{
    // Bombs
    BombPlace,
    BombExplosion,

    // Ships / abilities
    DrakkarWave,
    DrakkarPush,
    GalleonBombThrow,
    CaravelTeleport,
    SloopAuraStart,
    SloopAuraEnd,

    // Players
    PlayerDeath,
    PlayerSprint,

    // Game flow
    RoundStart,
    RoundEnd,
    CountdownBeep,
}

/// <summary>
/// All music track IDs. Add entries here when a new track is needed,
/// then assign a clip to it in the AudioManager inspector.
/// </summary>
public enum MusicTrackId : byte
{
    MainMenu,
    InGame,
}
