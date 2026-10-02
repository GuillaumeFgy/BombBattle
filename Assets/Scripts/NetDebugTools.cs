#if UNITY_EDITOR || DEVELOPMENT_BUILD
#define NETDEBUG_ENABLED
#endif

using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using Unity.Collections;
using Unity.Multiplayer.Tools.NetworkSimulator.Runtime;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;

/// <summary>
/// Dev-only netcode testing tools (Editor + Development builds). Spawns itself, no scene setup needed.
///
///   F1  toggle overlay (RTT, lag preset, host-view lag, fair/unfair death stats)
///   F2  cycle simulated network conditions for THIS instance (latency / jitter / loss)
///   F3  toggle host ghosts (where the host thinks each ship is)
///   F4  toggle bomb hitboxes (yellow = not armed yet, red = lethal)
///
/// Every bomb death is judged from the victim's point of view: FAIR if the victim's own ship touched that
/// armed bomb on their own screen before the death arrived, UNFAIR otherwise ("I didn't touch that!").
/// The host collects every verdict into a CSV (path printed in the console and overlay).
/// </summary>
public class NetDebugTools : MonoBehaviour
{
    public static NetDebugTools Instance { get; private set; }

    const string GhostMsg = "NetDebug.Ghosts";
    const string DeathMsg = "NetDebug.Death";
    const string VerdictMsg = "NetDebug.Verdict";

    const float GhostSendInterval = 0.05f;
    const float TrackRadius = 10f;
    const float RecordLifetime = 10f;
    const float DuplicateDeathWindow = 1f;
    const float DrawRange = 60f;

    static readonly NetworkSimulatorPreset[] Presets =
    {
        NetworkSimulatorPreset.Create("Off"),
        NetworkSimulatorPreset.Create("Good (+20ms)", packetDelayMs: 20, packetJitterMs: 5),
        NetworkSimulatorPreset.Create("Typical (+50ms, 1% loss)", packetDelayMs: 50, packetJitterMs: 10, packetLossPercent: 1),
        NetworkSimulatorPreset.Create("Bad (+100ms, 3% loss)", packetDelayMs: 100, packetJitterMs: 25, packetLossPercent: 3),
        NetworkSimulatorPreset.Create("Awful (+200ms, 5% loss)", packetDelayMs: 200, packetJitterMs: 50, packetLossPercent: 5),
    };

    class BombRecord
    {
        public Bomb Bomb;
        public float SpawnTime;
        public float DespawnTime = -1f;
        public float MinArmedGap = float.PositiveInfinity;
        public float FirstTouchTime = -1f;
    }

    class PendingDeath
    {
        public ulong Victim, Dropper, BombId;
        public float BombAgeMs, HostGap, VictimRttMs, Time;
        public bool ViaFallback;
    }

    struct Ghost
    {
        public Vector3 Position;
        public float Yaw;
    }

    // ── State ────────────────────────────────────────────────────────────────

    NetworkManager registeredWith;
    NetworkSimulator simulator;
    int presetIndex;

    bool showOverlay, showGhosts = true, showHitboxes = true;
    Material lineMaterial;

    readonly Dictionary<ulong, BombRecord> bombs = new();
    readonly List<ulong> pruneBuffer = new();
    readonly Collider[] overlapBuffer = new Collider[64];
    readonly List<Collider> shipColliders = new();
    NetworkObject cachedShip;
    float shipColliderRefreshTime;

    // Victim side
    bool awaitingRespawn, sawDeadAfterDeath;
    string lastVerdict = "-";

    // Host side
    readonly Dictionary<(ulong, ulong), PendingDeath> pendingDeaths = new();
    readonly Dictionary<ulong, float> lastDeathTime = new();
    readonly List<ulong> ghostTargets = new();
    readonly List<(ulong id, Vector3 pos, float yaw)> ghostBuffer = new();
    float ghostSendTimer;
    string csvPath;
    string lastHostDeath = "-";

    // Client side ghosts
    readonly Dictionary<ulong, Ghost> ghosts = new();
    float lastGhostReceiveTime = -1f;
    Vector3 lastShipPos;
    float shipSpeed;

    int fairDeaths, unfairDeaths;

#if NETDEBUG_ENABLED
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Bootstrap()
    {
        var go = new GameObject(nameof(NetDebugTools));
        DontDestroyOnLoad(go);
        go.AddComponent<NetDebugTools>();
    }
#endif

    void Awake()
    {
        Instance = this;
        simulator = gameObject.AddComponent<NetworkSimulator>();
        simulator.ConnectionPreset = Presets[0];
        csvPath = Path.Combine(Application.persistentDataPath, "netdebug_deaths_v2.csv");

        var shader = Shader.Find("Hidden/Internal-Colored");
        if (shader != null)
        {
            lineMaterial = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
            lineMaterial.SetInt("_ZTest", (int)CompareFunction.Always);
            lineMaterial.SetInt("_ZWrite", 0);
            lineMaterial.SetInt("_Cull", (int)CullMode.Off);
        }

        Debug.Log("[NetDebug] Netcode test tools ready — F1 overlay, F2 network conditions, F3 host ghosts, F4 bomb hitboxes.");
    }

    void OnEnable() => RenderPipelineManager.endCameraRendering += OnEndCameraRendering;
    void OnDisable() => RenderPipelineManager.endCameraRendering -= OnEndCameraRendering;

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
        if (lineMaterial != null) Destroy(lineMaterial);
    }

    // ── Hooks called from gameplay code (no-ops when the tool isn't running) ─

    public static void OnBombSpawned(Bomb bomb)
    {
        if (Instance == null) return;
        Instance.bombs[bomb.NetworkObjectId] = new BombRecord { Bomb = bomb, SpawnTime = Time.time };
    }

    public static void OnBombDespawned(Bomb bomb)
    {
        if (Instance == null) return;
        if (Instance.bombs.TryGetValue(bomb.NetworkObjectId, out var rec))
        {
            rec.DespawnTime = Time.time;
            rec.Bomb = null;
        }
    }

    /// <summary>Server only: a bomb trigger just killed a ship.</summary>
    public static void ServerReportBombKill(Bomb bomb, Collider victimCollider, ulong victimClientId, bool viaFallback)
    {
        if (Instance == null) return;
        Instance.HandleServerKill(bomb, victimCollider, victimClientId, viaFallback);
    }

    // ── Update loop ──────────────────────────────────────────────────────────

    void Update()
    {
        UpdateRegistration();
        HandleHotkeys();

        var nm = NetworkManager.Singleton;
        if (nm == null || !nm.IsListening) return;

        if (nm.IsServer)
        {
            ghostSendTimer += Time.deltaTime;
            if (ghostSendTimer >= GhostSendInterval)
            {
                ghostSendTimer = 0f;
                SendGhosts(nm);
            }
        }

        PruneRecords();
    }

    void FixedUpdate()
    {
        var nm = NetworkManager.Singleton;
        if (nm == null || !nm.IsListening) return;
        TrackLocalContacts(nm);
    }

    void HandleHotkeys()
    {
        var kb = Keyboard.current;
        if (kb == null) return;

        if (kb.f1Key.wasPressedThisFrame) showOverlay = !showOverlay;
        if (kb.f2Key.wasPressedThisFrame)
        {
            presetIndex = (presetIndex + 1) % Presets.Length;
            simulator.ConnectionPreset = Presets[presetIndex];
            Debug.Log($"[NetDebug] Network conditions on this instance: {Presets[presetIndex].Name}");
        }
        if (kb.f3Key.wasPressedThisFrame) showGhosts = !showGhosts;
        if (kb.f4Key.wasPressedThisFrame) showHitboxes = !showHitboxes;
    }

    void UpdateRegistration()
    {
        var nm = NetworkManager.Singleton;
        bool listening = nm != null && nm.IsListening && nm.CustomMessagingManager != null;

        if (listening && registeredWith != nm)
        {
            var cmm = nm.CustomMessagingManager;
            cmm.RegisterNamedMessageHandler(GhostMsg, OnGhostMessage);
            cmm.RegisterNamedMessageHandler(DeathMsg, OnDeathMessage);
            cmm.RegisterNamedMessageHandler(VerdictMsg, OnVerdictMessage);
            registeredWith = nm;
        }
        else if (!listening && registeredWith != null)
        {
            // The CustomMessagingManager is torn down with the session; just forget it.
            registeredWith = null;
            ghosts.Clear();
            pendingDeaths.Clear();
            awaitingRespawn = false;
        }
    }

    void PruneRecords()
    {
        pruneBuffer.Clear();
        foreach (var kv in bombs)
        {
            if (kv.Value.DespawnTime >= 0f && Time.time - kv.Value.DespawnTime > RecordLifetime)
                pruneBuffer.Add(kv.Key);
        }
        foreach (var id in pruneBuffer) bombs.Remove(id);
    }

    // ── Victim side: what did *my* screen see? ───────────────────────────────

    void TrackLocalContacts(NetworkManager nm)
    {
        var ship = nm.LocalClient?.PlayerObject;
        if (ship == null) return;

        if (Time.fixedDeltaTime > 0f)
        {
            Vector3 p = ship.transform.position;
            shipSpeed = Mathf.Lerp(shipSpeed, Vector3.Distance(p, lastShipPos) / Time.fixedDeltaTime, 0.2f);
            lastShipPos = p;
        }

        bool alive = !ship.TryGetComponent(out PlayerDeathHandler death) || death.isAlive.Value;
        if (awaitingRespawn)
        {
            if (!alive) sawDeadAfterDeath = true;
            if (sawDeadAfterDeath && alive) awaitingRespawn = sawDeadAfterDeath = false;
            else return;
        }
        if (!alive || !PlayerMovement.AllowMovement) return;

        RefreshShipColliders(ship);
        if (shipColliders.Count == 0) return;

        // Ships move by transform; make sure collider poses match before querying.
        Physics.SyncTransforms();

        int count = Physics.OverlapSphereNonAlloc(ship.transform.position, TrackRadius, overlapBuffer, ~0, QueryTriggerInteraction.Collide);
        for (int i = 0; i < count; i++)
        {
            var bomb = overlapBuffer[i].GetComponentInParent<Bomb>();
            if (bomb != null && bombs.TryGetValue(bomb.NetworkObjectId, out var rec))
                SampleBomb(rec);
        }
    }

    void SampleBomb(BombRecord rec)
    {
        var bomb = rec.Bomb;
        if (bomb == null || !bomb.IsSpawned || !bomb.IsArmed) return;

        float gap = GapToShip(bomb.transform.position, bomb.HitRadius);
        if (gap < rec.MinArmedGap) rec.MinArmedGap = gap;
        if (gap <= 0f && rec.FirstTouchTime < 0f) rec.FirstTouchTime = Time.time;
    }

    void RefreshShipColliders(NetworkObject ship)
    {
        if (ship == cachedShip && Time.time < shipColliderRefreshTime) return;
        cachedShip = ship;
        shipColliderRefreshTime = Time.time + 0.5f;
        shipColliders.Clear();
        foreach (var c in ship.GetComponentsInChildren<Collider>())
        {
            if (c.enabled && c.gameObject.activeInHierarchy && c.CompareTag("Player"))
                shipColliders.Add(c);
        }
    }

    /// <summary>Distance between the bomb's hit sphere and the ship's colliders (negative = overlapping).</summary>
    float GapToShip(Vector3 bombCenter, float bombRadius)
    {
        float best = float.PositiveInfinity;
        foreach (var c in shipColliders)
        {
            if (c == null) continue;
            float d = Vector3.Distance(c.ClosestPoint(bombCenter), bombCenter) - bombRadius;
            if (d < best) best = d;
        }
        return best;
    }

    void OnDeathMessage(ulong sender, FastBufferReader reader)
    {
        reader.ReadValueSafe(out ulong bombId);
        HandleLocalDeath(bombId);
    }

    void HandleLocalDeath(ulong bombId)
    {
        awaitingRespawn = true;
        sawDeadAfterDeath = false;

        bool seen = bombs.TryGetValue(bombId, out var rec);

        // One last look at this exact moment: the touch and the kill can land in the same physics step.
        var ship = NetworkManager.Singleton.LocalClient?.PlayerObject;
        if (seen && ship != null)
        {
            RefreshShipColliders(ship);
            Physics.SyncTransforms();
            SampleBomb(rec);
        }

        bool fair = seen && rec.FirstTouchTime >= 0f;
        float minGap = seen ? rec.MinArmedGap : float.PositiveInfinity;
        float touchToDeathMs = fair ? (Time.time - rec.FirstTouchTime) * 1000f : -1f;

        if (fair)
        {
            fairDeaths++;
            lastVerdict = $"FAIR — you touched it on your screen {touchToDeathMs:0} ms before the death arrived";
        }
        else
        {
            unfairDeaths++;
            lastVerdict = !seen ? "UNFAIR — that bomb never appeared on your screen"
                : float.IsPositiveInfinity(minGap) ? "UNFAIR — that bomb was never armed near you on your screen"
                : $"UNFAIR — on your screen you missed it by {minGap:0.00} m";
        }
        Debug.Log($"[NetDebug] You died to bomb {bombId}: {lastVerdict}");

        var nm = NetworkManager.Singleton;
        if (nm.IsServer)
        {
            RecordVerdict(nm.LocalClientId, bombId, fair, seen, minGap, touchToDeathMs, Presets[presetIndex].Name);
            return;
        }

        using var writer = new FastBufferWriter(64, Allocator.Temp, 256);
        writer.WriteValueSafe(bombId);
        writer.WriteValueSafe(fair);
        writer.WriteValueSafe(seen);
        writer.WriteValueSafe(minGap);
        writer.WriteValueSafe(touchToDeathMs);
        writer.WriteValueSafe(new FixedString32Bytes(Presets[presetIndex].Name));
        nm.CustomMessagingManager.SendNamedMessage(VerdictMsg, NetworkManager.ServerClientId, writer, NetworkDelivery.ReliableSequenced);
    }

    // ── Host side ────────────────────────────────────────────────────────────

    void HandleServerKill(Bomb bomb, Collider victimCollider, ulong victim, bool viaFallback)
    {
        var nm = NetworkManager.Singleton;
        if (nm == null || !nm.IsServer) return;

        // A ship can trigger several bombs (or one bomb twice) in the same instant; only judge the first.
        if (lastDeathTime.TryGetValue(victim, out float t) && Time.time - t < DuplicateDeathWindow) return;
        lastDeathTime[victim] = Time.time;

        ulong bombId = bomb.NetworkObjectId;
        Vector3 center = bomb.transform.position;
        var death = new PendingDeath
        {
            Victim = victim,
            Dropper = bomb.DropperClientId,
            BombId = bombId,
            BombAgeMs = (Time.time - bomb.LocalSpawnTime) * 1000f,
            HostGap = Vector3.Distance(victimCollider.ClosestPoint(center), center) - bomb.HitRadius,
            VictimRttMs = victim == nm.LocalClientId ? 0f : nm.NetworkConfig.NetworkTransport.GetCurrentRtt(victim),
            Time = Time.time,
            ViaFallback = viaFallback,
        };
        pendingDeaths[(victim, bombId)] = death;
        lastHostDeath = $"client {victim} killed by bomb {bombId} (dropper {FormatClient(death.Dropper)}, age {death.BombAgeMs:0} ms, {(viaFallback ? "HOST FALLBACK" : "victim report")})";

        if (victim == nm.LocalClientId)
        {
            HandleLocalDeath(bombId);
            return;
        }

        using var writer = new FastBufferWriter(16, Allocator.Temp, 64);
        writer.WriteValueSafe(bombId);
        nm.CustomMessagingManager.SendNamedMessage(DeathMsg, victim, writer, NetworkDelivery.ReliableSequenced);
    }

    void OnVerdictMessage(ulong sender, FastBufferReader reader)
    {
        reader.ReadValueSafe(out ulong bombId);
        reader.ReadValueSafe(out bool fair);
        reader.ReadValueSafe(out bool seen);
        reader.ReadValueSafe(out float minGap);
        reader.ReadValueSafe(out float touchToDeathMs);
        reader.ReadValueSafe(out FixedString32Bytes preset);
        if (fair) fairDeaths++; else unfairDeaths++;
        RecordVerdict(sender, bombId, fair, seen, minGap, touchToDeathMs, preset.ToString());
    }

    void RecordVerdict(ulong victim, ulong bombId, bool fair, bool seen, float minGap, float touchToDeathMs, string victimPreset)
    {
        pendingDeaths.TryGetValue((victim, bombId), out var d);
        pendingDeaths.Remove((victim, bombId));

        var inv = CultureInfo.InvariantCulture;
        string Num(float v) => float.IsInfinity(v) ? "" : v.ToString("0.###", inv);

        bool writeHeader = !File.Exists(csvPath);
        var line = new StringBuilder();
        if (writeHeader)
            line.AppendLine("utc_time,victim,dropper,bomb_id,verdict,bomb_seen_by_victim,victim_min_gap_m,touch_to_death_ms,host_gap_m,bomb_age_on_host_ms,victim_rtt_ms,victim_net_preset,kill_source");
        line.Append(System.DateTime.UtcNow.ToString("o", inv)).Append(',')
            .Append(victim).Append(',')
            .Append(d != null ? FormatClient(d.Dropper) : "").Append(',')
            .Append(bombId).Append(',')
            .Append(fair ? "FAIR" : "UNFAIR").Append(',')
            .Append(seen).Append(',')
            .Append(Num(minGap)).Append(',')
            .Append(touchToDeathMs >= 0f ? Num(touchToDeathMs) : "").Append(',')
            .Append(d != null ? Num(d.HostGap) : "").Append(',')
            .Append(d != null ? Num(d.BombAgeMs) : "").Append(',')
            .Append(d != null ? Num(d.VictimRttMs) : "").Append(',')
            .Append(victimPreset.Replace(',', ';')).Append(',')
            .Append(d == null ? "" : d.ViaFallback ? "host_fallback" : "victim_report");

        try
        {
            File.AppendAllText(csvPath, line.AppendLine().ToString());
        }
        catch (IOException e)
        {
            Debug.LogWarning($"[NetDebug] Could not write {csvPath}: {e.Message}");
        }

        Debug.Log($"[NetDebug] Death verdict: client {victim}, bomb {bombId} → {(fair ? "FAIR" : "UNFAIR")} (log: {csvPath})");
    }

    void SendGhosts(NetworkManager nm)
    {
        ghostBuffer.Clear();
        ghostTargets.Clear();
        foreach (var client in nm.ConnectedClientsList)
        {
            if (client.PlayerObject != null)
            {
                var tr = client.PlayerObject.transform;
                ghostBuffer.Add((client.ClientId, tr.position, tr.eulerAngles.y));
            }
            if (client.ClientId != nm.LocalClientId) ghostTargets.Add(client.ClientId);
        }

        // The host's own view *is* the host view; store it directly for drawing.
        ghosts.Clear();
        foreach (var g in ghostBuffer) ghosts[g.id] = new Ghost { Position = g.pos, Yaw = g.yaw };
        lastGhostReceiveTime = Time.time;

        if (ghostTargets.Count == 0) return;

        using var writer = new FastBufferWriter(4 + ghostBuffer.Count * 24, Allocator.Temp, 4096);
        writer.WriteValueSafe(ghostBuffer.Count);
        foreach (var g in ghostBuffer)
        {
            writer.WriteValueSafe(g.id);
            writer.WriteValueSafe(g.pos);
            writer.WriteValueSafe(g.yaw);
        }
        nm.CustomMessagingManager.SendNamedMessage(GhostMsg, ghostTargets, writer, NetworkDelivery.Unreliable);
    }

    void OnGhostMessage(ulong sender, FastBufferReader reader)
    {
        reader.ReadValueSafe(out int count);
        ghosts.Clear();
        for (int i = 0; i < count; i++)
        {
            reader.ReadValueSafe(out ulong id);
            reader.ReadValueSafe(out Vector3 pos);
            reader.ReadValueSafe(out float yaw);
            ghosts[id] = new Ghost { Position = pos, Yaw = yaw };
        }
        lastGhostReceiveTime = Time.time;
    }

    static string FormatClient(ulong id) => id == ulong.MaxValue ? "?" : id.ToString();

    // ── Overlay ──────────────────────────────────────────────────────────────

    void OnGUI()
    {
        if (!showOverlay) return;
        var nm = NetworkManager.Singleton;

        var sb = new StringBuilder();
        sb.AppendLine("<b>NET DEBUG</b>  (F1 hide · F2 network · F3 ghosts · F4 hitboxes)");
        sb.AppendLine($"Simulated network (this instance): <b>{Presets[presetIndex].Name}</b>");

        if (nm == null || !nm.IsListening)
        {
            sb.AppendLine("Not connected.");
        }
        else
        {
            var transport = nm.NetworkConfig.NetworkTransport;
            sb.AppendLine($"Role: {(nm.IsHost ? "Host" : nm.IsServer ? "Server" : "Client")}  id {nm.LocalClientId}  tick {nm.NetworkConfig.TickRate} Hz");

            if (nm.IsServer)
            {
                foreach (var id in nm.ConnectedClientsIds)
                {
                    if (id == nm.LocalClientId) continue;
                    sb.AppendLine($"  client {id}: RTT {transport.GetCurrentRtt(id)} ms");
                }
                sb.AppendLine($"Last kill: {lastHostDeath}");
                sb.AppendLine($"Deaths judged (all players): <color=#7f7>{fairDeaths} fair</color> / <color=#f77>{unfairDeaths} unfair</color>");
                sb.AppendLine($"CSV: {csvPath}");
            }
            else
            {
                sb.AppendLine($"RTT to host: {transport.GetCurrentRtt(NetworkManager.ServerClientId)} ms");
                var ship = nm.LocalClient?.PlayerObject;
                if (ship != null && ghosts.TryGetValue(nm.LocalClientId, out var g))
                {
                    float lagM = Vector3.Distance(Flat(ship.transform.position), Flat(g.Position));
                    string lagMs = shipSpeed > 0.5f ? $"≈{lagM / shipSpeed * 1000f:0} ms" : "-";
                    sb.AppendLine($"Host sees your ship {lagM:0.0} m behind ({lagMs})");
                }
                sb.AppendLine($"Your deaths: <color=#7f7>{fairDeaths} fair</color> / <color=#f77>{unfairDeaths} unfair</color>");
            }
            sb.AppendLine($"Last death: {lastVerdict}");
        }

        var style = new GUIStyle(GUI.skin.box) { alignment = TextAnchor.UpperLeft, richText = true, fontSize = 13, wordWrap = true };
        var content = new GUIContent(sb.ToString());
        float width = 460f;
        float height = style.CalcHeight(content, width);
        GUI.Box(new Rect(10, 10, width, height), content, style);
    }

    static Vector3 Flat(Vector3 v) => new(v.x, 0f, v.z);

    // ── World-space debug drawing ────────────────────────────────────────────

    void OnEndCameraRendering(ScriptableRenderContext context, Camera cam)
    {
        if (!showOverlay || lineMaterial == null) return;
        var nm = NetworkManager.Singleton;
        if (nm == null || !nm.IsListening) return;

        GL.PushMatrix();
        GL.LoadProjectionMatrix(cam.projectionMatrix);
        GL.modelview = cam.worldToCameraMatrix;
        lineMaterial.SetPass(0);
        GL.Begin(GL.LINES);

        Vector3 camPos = cam.transform.position;

        if (showHitboxes)
        {
            foreach (var rec in bombs.Values)
            {
                var bomb = rec.Bomb;
                if (bomb == null || !bomb.IsSpawned) continue;
                Vector3 p = bomb.transform.position;
                if (Vector3.Distance(Flat(p), Flat(camPos)) > DrawRange) continue;
                GL.Color(bomb.IsArmed ? new Color(1f, 0.2f, 0.2f) : new Color(1f, 0.9f, 0.2f));
                Circle(p, bomb.HitRadius, 16);
            }
        }

        bool ghostsFresh = lastGhostReceiveTime >= 0f && Time.time - lastGhostReceiveTime < 1f;
        if (showGhosts && ghostsFresh && !nm.IsServer)
        {
            var ship = nm.LocalClient?.PlayerObject;
            foreach (var kv in ghosts)
            {
                bool mine = kv.Key == nm.LocalClientId;
                GL.Color(mine ? new Color(0.2f, 1f, 1f) : new Color(1f, 1f, 1f, 0.8f));
                Vector3 p = kv.Value.Position;
                Circle(p, 2.5f, 24);
                Vector3 fwd = Quaternion.Euler(0f, kv.Value.Yaw, 0f) * Vector3.forward;
                GL.Vertex(p);
                GL.Vertex(p + fwd * 5f);

                if (mine && ship != null)
                {
                    GL.Color(new Color(1f, 0.3f, 1f));
                    GL.Vertex(p);
                    GL.Vertex(ship.transform.position);
                }
            }
        }

        GL.End();
        GL.PopMatrix();
    }

    static void Circle(Vector3 center, float radius, int segments)
    {
        Vector3 prev = center + new Vector3(radius, 0f, 0f);
        for (int i = 1; i <= segments; i++)
        {
            float a = i * Mathf.PI * 2f / segments;
            Vector3 next = center + new Vector3(Mathf.Cos(a) * radius, 0f, Mathf.Sin(a) * radius);
            GL.Vertex(prev);
            GL.Vertex(next);
            prev = next;
        }
    }
}
