using System.Collections;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// A bomb in a ship's trail (or a Galleon shot). It doesn't decide kills itself: each ship's owner checks its own
/// hull against armed bombs and reports hits, the host validates them (see <see cref="PlayerDeathHandler"/>).
/// </summary>
public class Bomb : NetworkBehaviour
{
    [SerializeField] private Renderer bombRenderer;
    [SerializeField] private GameObject deathEffectPrefab;

    [Header("Size")]
    [Tooltip("Visual size multiplier on top of the prefab's scale.")]
    [SerializeField] private float visualSize = 1.6f;
    [Tooltip("Kill radius as a fraction of the visual radius. Below 1 = forgiving: a near-miss that looks like a miss is a miss.")]
    [SerializeField, Range(0.5f, 1f)] private float hitRadiusFraction = 0.78f;

    [Header("Arming")]
    [Tooltip("Seconds after spawn before the bomb can sink a ship. Must exceed the worst one-way latency so every " +
             "player sees the bomb before it becomes lethal.")]
    [SerializeField] private float armDelay = 0.4f;
    [SerializeField] private float armingStartScale = 0.5f;
    [SerializeField] private float armPopScale = 1.25f;
    [SerializeField] private float armPopDuration = 0.15f;
    [SerializeField] private Vector2 fuseBlinkHz = new(4f, 14f);

    private NetworkVariable<Vector4> syncedColorVec = new(Vector4.one, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    // Network-clock time (NetworkManager.ServerTime) at which the bomb becomes lethal, identical on every machine.
    private readonly NetworkVariable<double> armServerTime = new(double.MaxValue, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    // Client whose ship this bomb can't sink (Galleon shot ignores its thrower). Synced so owners can check it locally.
    private readonly NetworkVariable<ulong> ignoredClientId = new(ulong.MaxValue, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    private Transform visual;
    private Color ownerColor = Color.white;
    private bool armedVisualDone;
    private float localArmedTime = -1f;

    public void SetColor(Color color)
    {
        if (IsServer)
        {
            SetBombColorClientRpc(color); // all clients apply directly
        }
    }

    private void ApplyColor(Color color)
    {
        ownerColor = color;
        if (bombRenderer != null)
        {
            bombRenderer.material.color = color;
        }
    }

    /// <summary>Server only, before Spawn(): override the arming delay for this bomb.</summary>
    public float ArmDelay
    {
        get => armDelay;
        set => armDelay = value;
    }

    /// <summary>Server only (after Spawn): if shouldIgnore, this bomb can't sink the ship of client <paramref name="id"/>.</summary>
    public void SetCreator(ulong id, bool shouldIgnore)
    {
        if (IsServer) ignoredClientId.Value = shouldIgnore ? id : ulong.MaxValue;
    }

    public bool Ignores(ulong clientId) => ignoredClientId.Value == clientId;

    // Server only: the client whose ship dropped this bomb (ulong.MaxValue if unknown).
    public ulong DropperClientId { get; set; } = ulong.MaxValue;

    // Local time this bomb appeared on this machine.
    public float LocalSpawnTime { get; private set; }

    private SphereCollider hitCollider;

    /// <summary>True once the shared network clock has passed the arm time (same moment on every machine).</summary>
    public bool IsArmed => IsSpawned && NetworkManager.ServerTime.Time >= armServerTime.Value;

    /// <summary>Like IsArmed, but also true up to <paramref name="tolerance"/> seconds early (absorbs clock jitter).</summary>
    public bool IsArmedWithin(double tolerance) => IsSpawned && NetworkManager.ServerTime.Time >= armServerTime.Value - tolerance;

    public double ArmServerTime => armServerTime.Value;

    /// <summary>0 at spawn, 1 when lethal.</summary>
    public float ArmProgress
    {
        get
        {
            if (!IsSpawned || armServerTime.Value == double.MaxValue || armDelay <= 0f) return IsArmed ? 1f : 0f;
            double remaining = armServerTime.Value - NetworkManager.ServerTime.Time;
            return Mathf.Clamp01(1f - (float)(remaining / armDelay));
        }
    }

    public float HitRadius
    {
        get
        {
            if (hitCollider == null) return 0f;
            Vector3 s = hitCollider.transform.lossyScale;
            return hitCollider.radius * Mathf.Max(Mathf.Abs(s.x), Mathf.Abs(s.y), Mathf.Abs(s.z));
        }
    }

    private void Awake()
    {
        hitCollider = GetComponent<SphereCollider>();
        if (bombRenderer != null) ownerColor = bombRenderer.sharedMaterial.color;
        CreateVisualPivot();
    }

    // The root's scale is synced by NetworkTransform, so the arming animation scales a visual-only child instead
    // (no network traffic, hitbox untouched).
    private void CreateVisualPivot()
    {
        if (bombRenderer == null || bombRenderer.gameObject != gameObject ||
            !TryGetComponent(out MeshFilter rootFilter)) return;

        var pivot = new GameObject("Visual");
        pivot.transform.SetParent(transform, false);
        pivot.AddComponent<MeshFilter>().sharedMesh = rootFilter.sharedMesh;
        var visualRenderer = pivot.AddComponent<MeshRenderer>();
        visualRenderer.sharedMaterials = bombRenderer.sharedMaterials;
        visualRenderer.shadowCastingMode = bombRenderer.shadowCastingMode;
        visualRenderer.receiveShadows = bombRenderer.receiveShadows;

        bombRenderer.enabled = false;
        bombRenderer = visualRenderer;
        visual = pivot.transform;
        SetVisualScale(1f);

        // Kill radius = a fraction of the (enlarged) visual radius.
        if (hitCollider != null && rootFilter.sharedMesh != null)
        {
            float meshRadius = rootFilter.sharedMesh.bounds.extents.x;
            hitCollider.radius = meshRadius * visualSize * hitRadiusFraction;
        }
    }

    public override void OnNetworkSpawn()
    {
        LocalSpawnTime = Time.time;
        if (IsServer)
        {
            armServerTime.Value = NetworkManager.ServerTime.Time + armDelay;
        }
        UpdateArmingVisual();
        NetDebugTools.OnBombSpawned(this);
    }

    public override void OnNetworkDespawn()
    {
        NetDebugTools.OnBombDespawned(this);
    }

    private void Update()
    {
        if (!armedVisualDone) UpdateArmingVisual();
    }

    // Arming: small, pale and blinking faster and faster (lit fuse), then a pop to full size in the owner's color.
    private void UpdateArmingVisual()
    {
        if (bombRenderer == null) return;

        if (!IsArmed)
        {
            float t = ArmProgress;
            float hz = Mathf.Lerp(fuseBlinkHz.x, fuseBlinkHz.y, t);
            bool lit = Mathf.Repeat((Time.time - LocalSpawnTime) * hz, 1f) < 0.5f;
            Color pale = Color.Lerp(ownerColor, Color.gray, 0.6f);
            Color flash = Color.Lerp(ownerColor, Color.white, 0.6f);
            bombRenderer.material.color = lit ? flash : pale;
            SetVisualScale(Mathf.Lerp(armingStartScale, 1f, 1f - (1f - t) * (1f - t)));
            return;
        }

        if (localArmedTime < 0f)
        {
            localArmedTime = Time.time;
            bombRenderer.material.color = ownerColor;
        }

        float pop = armPopDuration > 0f ? Mathf.Clamp01((Time.time - localArmedTime) / armPopDuration) : 1f;
        SetVisualScale(Mathf.Lerp(armPopScale, 1f, pop));
        if (pop >= 1f) armedVisualDone = true;
    }

    private void SetVisualScale(float s)
    {
        if (visual != null) visual.localScale = Vector3.one * (s * visualSize);
    }

    /// <summary>
    /// Distance between this bomb's kill sphere and a ship collider (negative = overlapping, the value is the depth).
    /// <paramref name="shipOffset"/> evaluates the ship as if it were moved by that much (e.g. to a reported position).
    /// The caller must have synced physics transforms (Physics.SyncTransforms) if ships moved this step.
    /// </summary>
    public float GapTo(Collider shipCollider, Vector3 shipOffset = default)
    {
        Vector3 center = transform.position - shipOffset;
        return Vector3.Distance(shipCollider.ClosestPoint(center), center) - HitRadius;
    }

    /// <summary>Server only. This bomb sinks <paramref name="victim"/>: death effect, kill, bomb removed.</summary>
    public void ServerSinkShip(PlayerDeathHandler victim, Collider victimCollider, Vector3 effectPosition, bool viaFallback)
    {
        if (!IsServer || !IsSpawned || victim == null || !victim.isAlive.Value) return;

        NetDebugTools.ServerReportBombKill(this, victimCollider, victim.OwnerClientId, viaFallback);
        SpawnDeathEffect(effectPosition);
        victim.ServerKill();
        ServerDestroy();
    }

    /// <summary>Server only. An invincible ship (Caravel slide) smashed through this bomb.</summary>
    public void ServerShatter()
    {
        if (!IsServer || !IsSpawned) return;
        SpawnDeathEffect(transform.position);
        ServerDestroy();
    }

    private void SpawnDeathEffect(Vector3 position)
    {
        if (deathEffectPrefab == null) return;

        GameObject effect = Instantiate(deathEffectPrefab, position, Quaternion.identity);
        var netObj = effect.GetComponent<NetworkObject>();
        if (netObj != null)
        {
            netObj.Spawn();
            // Run the timer on the GameManager: this bomb is destroyed right after, which would kill the coroutine
            // and leave the effect spawned forever.
            MonoBehaviour runner = GameManager.Instance != null ? GameManager.Instance : NetworkManager.Singleton;
            runner.StartCoroutine(DespawnAfterDelay(netObj, 2f));
        }
    }

    private static IEnumerator DespawnAfterDelay(NetworkObject netObj, float delay)
    {
        yield return new WaitForSeconds(delay);
        if (netObj != null && netObj.IsSpawned)
            netObj.Despawn(true);
    }

    /// <summary>Server only. Removes this bomb on every machine; safe to call more than once.</summary>
    public void ServerDestroy()
    {
        if (!IsServer || !IsSpawned) return;
        NetworkObject.Despawn(true);
    }

    [ClientRpc]
    public void SetBombColorClientRpc(Color color)
    {
        ApplyColor(color);
    }
}
