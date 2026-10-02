using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Ship life and death. Bomb hits are decided from the victim's point of view:
///  - the owner checks its own hull against armed bombs every physics step and reports a touch
///    (<see cref="ReportBombHitRpc"/>); the host sanity-checks the report and sinks the ship.
///    A report can only sink the reporter's own ship, so lying gains nothing.
///  - fallback for a client that never reports: if the host itself sees the ship deep inside a bomb that was armed
///    long enough for the victim to have seen it, and no report arrives in time, the host sinks it anyway.
/// Runs after PlayerMovement so checks use this step's ship position.
/// </summary>
[DefaultExecutionOrder(100)]
public class PlayerDeathHandler : NetworkBehaviour
{
    [SerializeField] private GameObject[] shipModels;
    [SerializeField] private MonoBehaviour[] componentsToDisable; // assign in inspector: e.g. PlayerMovement
    [SerializeField] private Transform spectatorCameraPosition;   // optional: assign a transform for top-down view

    [Header("Bomb hit reports (host checks)")]
    [Tooltip("A report may claim a bomb armed up to this many seconds before the host's clock says so (jitter).")]
    [SerializeField] private float reportArmTolerance = 0.15f;
    [Tooltip("Max distance between the reported ship position and where the host sees the ship.")]
    [SerializeField] private float reportMaxDrift = 12f;
    [Tooltip("Max gap between the bomb and the ship at its reported position (slack for rotation differences).")]
    [SerializeField] private float reportMaxGap = 0.75f;

    [Header("Host fallback (client never reports)")]
    [Tooltip("The host sinks the ship itself only if it sees it at least this deep inside a bomb...")]
    [SerializeField] private float fallbackDepth = 0.35f;
    [Tooltip("...armed at least the victim's RTT plus this long before the contact...")]
    [SerializeField] private float fallbackArmedMargin = 0.2f;
    [Tooltip("...and no report arrived within the victim's RTT plus this.")]
    [SerializeField] private float fallbackReportWait = 0.25f;

    public NetworkVariable<bool> isAlive = new NetworkVariable<bool>(true);

    private class Contact
    {
        public Bomb Bomb;
        public double FirstTime;
        public float MaxDepth;
    }

    private static readonly Collider[] scanBuffer = new Collider[64];
    private Collider shipCollider;
    private PlayerClass playerClass;
    private readonly HashSet<ulong> reportedBombs = new();        // owner: bombs already reported
    private readonly Dictionary<ulong, Contact> contacts = new(); // server: contacts seen by the host, awaiting a report
    private readonly List<ulong> contactIds = new();

    private void Awake()
    {
        shipCollider = GetComponent<Collider>();
        playerClass = GetComponent<PlayerClass>();
    }

    private void FixedUpdate()
    {
        if (!IsSpawned || !isAlive.Value || !PlayerMovement.AllowMovement) return;
        if (!IsOwner && !IsServer) return;
        if (shipCollider == null || !shipCollider.enabled) return;

        // Ships move by transform; make collider poses match this step before querying.
        Physics.SyncTransforms();

        bool isProtected = GameManager.Instance != null && GameManager.Instance.IsSpawnProtected;
        bool invincible = playerClass != null && playerClass.isInvincible.Value;

        float scanRadius = shipCollider.bounds.extents.magnitude + 2f;
        int count = Physics.OverlapSphereNonAlloc(transform.position, scanRadius, scanBuffer, ~0, QueryTriggerInteraction.Collide);
        for (int i = 0; i < count; i++)
        {
            if (!scanBuffer[i].TryGetComponent(out Bomb bomb) || !bomb.IsSpawned) continue;
            if (!bomb.IsArmed || bomb.Ignores(OwnerClientId)) continue;

            float gap = bomb.GapTo(shipCollider);
            if (gap > 0f) continue;

            if (IsServer && invincible)
            {
                bomb.ServerShatter(); // Caravel slide smashes through bombs
                continue;
            }
            if (isProtected || invincible) continue;

            if (IsOwner && reportedBombs.Add(bomb.NetworkObjectId))
                ReportBombHitRpc(bomb.NetworkObjectId, transform.position);

            // Host's own ship is fully handled by the owner path above.
            if (IsServer && !IsOwner) NoteServerContact(bomb, -gap);
        }

        if (IsServer && !IsOwner) ResolveServerContacts();
    }

    // ── Owner report → host check ────────────────────────────────────────────

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
    private void ReportBombHitRpc(ulong bombId, Vector3 reportedPosition)
    {
        if (!isAlive.Value) return;

        string rejection = ValidateReport(bombId, reportedPosition, out Bomb bomb);
        if (rejection != null)
        {
            Debug.Log($"[Hits] Rejected bomb hit report from client {OwnerClientId} (bomb {bombId}): {rejection}");
            return;
        }

        contacts.Remove(bombId);
        bomb.ServerSinkShip(this, shipCollider, reportedPosition, viaFallback: false);
    }

    /// <summary>Returns null if the report is plausible, otherwise the reason it was rejected.</summary>
    private string ValidateReport(ulong bombId, Vector3 reportedPosition, out Bomb bomb)
    {
        bomb = null;
        if (GameManager.Instance != null && GameManager.Instance.IsSpawnProtected) return "spawn protection";
        if (playerClass != null && playerClass.isInvincible.Value) return "ship is invincible";

        if (!NetworkManager.SpawnManager.SpawnedObjects.TryGetValue(bombId, out NetworkObject bombObject) ||
            !bombObject.TryGetComponent(out bomb))
            return "bomb no longer exists";

        if (!bomb.IsArmedWithin(reportArmTolerance)) return "bomb not armed yet";
        if (bomb.Ignores(OwnerClientId)) return "bomb ignores this ship";

        float drift = Vector3.Distance(reportedPosition, transform.position);
        if (drift > reportMaxDrift) return $"reported position {drift:0.0} m from the host's view";

        Physics.SyncTransforms();
        float gap = bomb.GapTo(shipCollider, reportedPosition - transform.position);
        if (gap > reportMaxGap) return $"bomb {gap:0.00} m away from the reported position";

        return null;
    }

    // ── Host fallback ────────────────────────────────────────────────────────

    private void NoteServerContact(Bomb bomb, float depth)
    {
        ulong id = bomb.NetworkObjectId;
        if (!contacts.TryGetValue(id, out Contact c))
        {
            c = new Contact { Bomb = bomb, FirstTime = NetworkManager.ServerTime.Time };
            contacts[id] = c;
        }
        if (depth > c.MaxDepth) c.MaxDepth = depth;
    }

    private void ResolveServerContacts()
    {
        if (contacts.Count == 0) return;

        double now = NetworkManager.ServerTime.Time;
        double rtt = NetworkManager.NetworkConfig.NetworkTransport.GetCurrentRtt(OwnerClientId) / 1000.0;

        contactIds.Clear();
        contactIds.AddRange(contacts.Keys);
        foreach (ulong id in contactIds)
        {
            Contact c = contacts[id];
            if (now - c.FirstTime < rtt + fallbackReportWait) continue; // still waiting for the owner's report
            contacts.Remove(id);

            bool bombStillThere = c.Bomb != null && c.Bomb.IsSpawned;
            bool deep = c.MaxDepth >= fallbackDepth;
            bool victimSawItArmed = bombStillThere && c.FirstTime - c.Bomb.ArmServerTime >= rtt + fallbackArmedMargin;
            if (!bombStillThere || !deep || !victimSawItArmed) continue;

            Debug.Log($"[Hits] Host fallback: client {OwnerClientId} never reported bomb {id} " +
                      $"(host saw it {c.MaxDepth:0.00} m deep). Sinking it.");
            c.Bomb.ServerSinkShip(this, shipCollider, transform.position, viaFallback: true);
            return;
        }
    }

    /// <summary>
    /// Server only. Sinks this ship. Safe to call several times (e.g. two bombs in the same physics step):
    /// only the first call counts. Ignored outside a live round: during the reset a ship that sank against the arena
    /// wall gets its collider back while it still sits on the wall (the host's view only reaches the spawn point once
    /// the owner's teleport comes back), and it would sink again before the next round even starts.
    /// </summary>
    public void ServerKill()
    {
        if (!IsServer || !isAlive.Value) return;
        if (GameManager.Instance == null || !GameManager.Instance.IsRoundLive) return;

        isAlive.Value = false;
        HideModelAndDisableClientRpc();
        GameManager.Instance.CheckEndCondition();
    }

    // The owner reports its own ship hitting the arena wall. Owner-only, so a client can only ever sink itself.
    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
    public void ReportOwnWallHitRpc()
    {
        ServerKill();
    }


    [ClientRpc]
    private void HideModelAndDisableClientRpc()
    {
        foreach (var model in shipModels)
        {
            if (model != null)
                model.SetActive(false);
        }

        // 2. Disable movement & abilities
        foreach (var comp in componentsToDisable)
        {
            if (comp != null)
                comp.enabled = false;
        }

        // 3. Disable all colliders
        foreach (var collider in GetComponentsInChildren<Collider>())
        {
            collider.enabled = false;
        }

        // 4. Disable physics
        foreach (var rb in GetComponentsInChildren<Rigidbody>())
        {
            rb.isKinematic = true;
        }

        // 5. Camera switch
        if (IsOwner)
        {
            SetSpectatorCamera();
        }
    }

    private void SetSpectatorCamera()
    {
        Camera cam = GetComponentInChildren<Camera>(true);
        if (cam != null)
        {
            cam.enabled = true;

            if (spectatorCameraPosition != null)
            {
                cam.transform.position = spectatorCameraPosition.position;
                cam.transform.rotation = spectatorCameraPosition.rotation;
            }
            else
            {
                cam.transform.position = new Vector3(0, 100, 0); // fallback overhead
                cam.transform.rotation = Quaternion.Euler(90, 0, 0);
            }
        }
    }

    [ClientRpc]
    public void ResetPlayerClientRpc()
    {
        reportedBombs.Clear();
        contacts.Clear();

        foreach (var model in shipModels)
        {
            if (model != null)
                model.SetActive(false); // deactivate all
        }

        if (TryGetComponent(out PlayerClass playerClass))
        {
            playerClass.ApplyCurrentModel();
        }

        foreach (var comp in componentsToDisable)
            if (comp != null) comp.enabled = true;

        foreach (var collider in GetComponentsInChildren<Collider>())
            collider.enabled = true;

        foreach (var rb in GetComponentsInChildren<Rigidbody>())
            rb.isKinematic = false;

        if (IsServer)
        {
            isAlive.Value = true;
        }
    }

    [ClientRpc]
    public void TeleportToPositionClientRpc(Vector3 position, Quaternion rotation)
    {
        // Host-approved teleport: don't let the movement check mistake it for a cheat.
        if (IsServer && TryGetComponent(out PlayerMovement movement)) movement.ServerExpectTeleport();

        if (!IsOwner) return;
        transform.SetPositionAndRotation(position, rotation);
    }



}
