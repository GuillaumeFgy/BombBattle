using Unity.Netcode;
using UnityEngine;
using System.Collections;

public class PlayerClass : NetworkBehaviour
{

    public enum ShipType : byte
    {
        Galleon,
        Caravel,
        Drakkar,
        Sloop
    }

    [SerializeField] private GameObject Galleon, Caravel, Drakkar, Sloop;
    [SerializeField] private GameObject bombPrefab;

    private ShipType selectedShip = ShipType.Galleon;
    private NetworkVariable<ShipType> networkedSelectedShip = new(ShipType.Galleon, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    private NetworkVariable<int> playerColorIndex = new(-1, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    // Sprint values
    private float sprintMultiplier = 3f;
    private float sprintDuration = 2f;
    private float sprintCooldown = 15f;
    private bool isSprintOnCooldown = false;

    // Galleon bomb values
    private float bombCooldown = 30f;
    private bool isBombOnCooldown = false;

    // Sloop ability: double spawn speed
    [SerializeField] private GameObject sloopAuraPrefab;
    private float sloopBoostDuration = 3f;
    private float sloopBoostCooldown = 30f;
    private bool isSloopOnCooldown = false;
    private float originalSpawnInterval;
    private float increasedSpawnInterval = 3f;

    //drakkar
    [SerializeField] private GameObject drakkarWallPrefab;
    private bool isDrakkarWallActive = false;
    private float drakkarWallCooldown = 30f;
    private bool isDrakkarWallOnCooldown = false;

    //caravel
    [SerializeField] private GameObject caravelTeleporterPrefab;
    private NetworkVariable<NetworkObjectReference> teleporterRef = new();
    private bool hasTeleporterPlaced = false;
    private bool isCaravelOnCooldown = false;
    private float caravelCooldown = 30f;

    public override void OnNetworkSpawn()
    {
        teleporterRef.OnValueChanged += OnTeleporterRefChanged;

        // Always apply current value
        ApplyShipModel(networkedSelectedShip.Value);

        // Also apply on every future change
        networkedSelectedShip.OnValueChanged += (oldValue, newValue) =>
        {
            ApplyShipModel(newValue);
        };
    }

    private void ApplyShipModel(ShipType ship)
    {
        Galleon.SetActive(false);
        Caravel.SetActive(false);
        Drakkar.SetActive(false);
        Sloop.SetActive(false);

        switch (ship)
        {
            case ShipType.Galleon: Galleon.SetActive(true); break;
            case ShipType.Caravel: Caravel.SetActive(true); break;
            case ShipType.Drakkar: Drakkar.SetActive(true); break;
            case ShipType.Sloop: Sloop.SetActive(true); break;
        }
    }

    public void ApplyCurrentModel()
    {
        ApplyShipModel(networkedSelectedShip.Value);
    }


    public override void OnNetworkDespawn()
    {
        teleporterRef.OnValueChanged -= OnTeleporterRefChanged;
    }

    private void OnTeleporterRefChanged(NetworkObjectReference prev, NetworkObjectReference current)
    {
        if (current.TryGet(out var obj))
        {
            Debug.Log($"[Client {OwnerClientId}] TeleporterRef updated: {obj.name}");
        }
        else
        {
            Debug.Log($"[Client {OwnerClientId}] TeleporterRef updated but object not found yet.");
        }
    }

    [ServerRpc(RequireOwnership = false)]
    public void SetShipServerRpc(ShipType ship, ServerRpcParams rpcParams = default)
    {
        if (OwnerClientId != rpcParams.Receive.SenderClientId)
        {
            Debug.LogWarning($"Client {rpcParams.Receive.SenderClientId} tried to set ship on {OwnerClientId}'s object.");
            return;
        }

        networkedSelectedShip.Value = ship;
        selectedShip = ship;
    }

    private void Update()
    {
        if (!IsOwner || !GameManager.Instance.IsGameActive()) return;

        if (Input.GetKeyDown(KeyCode.Space) && !isSprintOnCooldown)
        {
            StartCoroutine(SprintRoutine());
        }

        if (Input.GetKeyDown(KeyCode.E))
        {
            switch (networkedSelectedShip.Value)
            {
                case ShipType.Galleon:
                    if (!isBombOnCooldown)
                        StartCoroutine(GalleonBombRoutine());
                    break;
                case ShipType.Sloop:
                    if (!isSloopOnCooldown)
                        StartCoroutine(SloopBoostRoutine());
                    break;
                case ShipType.Drakkar:
                    if (!isDrakkarWallOnCooldown && !isDrakkarWallActive)
                        StartCoroutine(DrakkarWallRoutine());
                    break;
                case ShipType.Caravel:
                    if (!isCaravelOnCooldown)
                    {
                        if (!hasTeleporterPlaced)
                        {
                            PlaceTeleporter();
                        }
                        else
                        {
                            TeleportToTeleporter();
                        }
                    }
                    break;

            }
        }
    }

    public ShipType GetSelectedShip() => selectedShip;

    private IEnumerator SprintRoutine()
    {
        isSprintOnCooldown = true;
        AbilityUI.Instance.StartCooldown(0, sprintCooldown);

        GetComponent<PlayerMovement>().SetSprintMultiplier(sprintMultiplier);

        yield return new WaitForSeconds(sprintDuration);

        GetComponent<PlayerMovement>().SetSprintMultiplier(1f);

        yield return new WaitForSeconds(sprintCooldown - sprintDuration);

        isSprintOnCooldown = false;
    }

    private IEnumerator GalleonBombRoutine()
    {
        isBombOnCooldown = true;

        Vector3 spawnPos = transform.position + transform.forward * 5f + Vector3.up * 1f;
        Quaternion rotation = transform.rotation;

        ShootBombServerRpc(spawnPos, rotation, OwnerClientId, true);
        AbilityUI.Instance.StartCooldown(1, bombCooldown);

        yield return new WaitForSeconds(bombCooldown);
        isBombOnCooldown = false;
    }

    [ServerRpc]
    void ShootBombServerRpc(Vector3 position, Quaternion rotation, ulong creatorId, bool ignoreCreator)
    {
        GameObject bomb = Instantiate(bombPrefab, position, rotation);
        bomb.GetComponent<NetworkObject>().Spawn();

        Bomb bombScript = bomb.GetComponent<Bomb>();
        if (bombScript != null)
        {
            bombScript.SetCreator(creatorId, ignoreCreator);

            int index = playerColorIndex.Value;
            Color bombColor = GameManager.Instance.GetColorByIndex(index);
            bombScript.SetColor(bombColor);
        }

        Rigidbody rb = bomb.GetComponent<Rigidbody>();
        if (rb != null)
        {
            rb.linearVelocity = transform.forward * 20f;
        }
    }

    public int GetColorIndex()
    {
        return playerColorIndex.Value;
    }


    private IEnumerator SloopBoostRoutine()
    {
        isSloopOnCooldown = true;
        PlayerMovement movement = GetComponent<PlayerMovement>();

        // Start cooldown in UI
        AbilityUI.Instance.StartCooldown(1, sloopBoostCooldown);

        // Save original spawn rate and apply boost
        if (movement != null)
        {
            originalSpawnInterval = movement.GetSpawnInterval();
            movement.SetSpawnInterval(originalSpawnInterval / increasedSpawnInterval);
        }

        // Tell server to spawn aura
        SpawnSloopAuraServerRpc();

        // Wait for duration of the boost
        yield return new WaitForSeconds(sloopBoostDuration);

        // Restore spawn interval
        if (movement != null)
        {
            movement.SetSpawnInterval(originalSpawnInterval);
        }

        // Wait remaining cooldown
        yield return new WaitForSeconds(sloopBoostCooldown - sloopBoostDuration);
        isSloopOnCooldown = false;
    }

    [ServerRpc]
    private void SpawnSloopAuraServerRpc(ServerRpcParams rpcParams = default)
    {
        if (sloopAuraPrefab == null) return;

        GameObject auraInstance = Instantiate(sloopAuraPrefab, transform.position, transform.rotation);
        var netObj = auraInstance.GetComponent<NetworkObject>();
        if (netObj != null)
        {
            netObj.Spawn();
            var auraScript = auraInstance.GetComponent<SloopAura>();
            if (auraScript != null)
            {
                auraScript.Initialize(GetComponent<NetworkObject>());
            }

            StartCoroutine(DespawnAuraAfterDelay(netObj, sloopBoostDuration));
        }
    }

    private IEnumerator DespawnAuraAfterDelay(NetworkObject netObj, float delay)
    {
        yield return new WaitForSeconds(delay);
        if (netObj != null && netObj.IsSpawned)
        {
            netObj.Despawn(true);
        }
    }




    private IEnumerator DrakkarWallRoutine()
    {
        isDrakkarWallOnCooldown = true;

        Vector3 wallPos = transform.position + transform.forward * 3f;
        Quaternion wallRot = Quaternion.LookRotation(transform.forward);

        SpawnDrakkarWallServerRpc(wallPos, wallRot);
        AbilityUI.Instance.StartCooldown(1, drakkarWallCooldown);

        yield return new WaitForSeconds(drakkarWallCooldown);
        isDrakkarWallOnCooldown = false;
    }

    [ServerRpc]
    private void SpawnDrakkarWallServerRpc(Vector3 position, Quaternion rotation)
    {
        GameObject wall = Instantiate(drakkarWallPrefab, position, rotation);
        wall.GetComponent<NetworkObject>().Spawn();
    }

    private void PlaceTeleporter()
    {
        Vector3 pos = transform.position;
        Quaternion rot = Quaternion.identity;

        SpawnTeleporterServerRpc(pos, rot);
        hasTeleporterPlaced = true;
    }

    [ServerRpc]
    private void SpawnTeleporterServerRpc(Vector3 position, Quaternion rotation)
    {
        GameObject obj = Instantiate(caravelTeleporterPrefab, position, rotation);
        var netObj = obj.GetComponent<NetworkObject>();
        netObj.SpawnWithOwnership(OwnerClientId);
        teleporterRef.Value = netObj;
    }


    private void TeleportToTeleporter()
    {
        if (!teleporterRef.Value.TryGet(out NetworkObject netObj))
        {
            Debug.LogWarning($"[Client {OwnerClientId}] Tried to teleport but teleporterRef was unresolved.");
            return;
        }

        Vector3 target = netObj.transform.position;
        TeleportAndDestroyServerRpc(target, teleporterRef.Value);
        StartCoroutine(CaravelCooldownRoutine());
    }

    [ServerRpc]
    private void TeleportAndDestroyServerRpc(Vector3 targetPos, NetworkObjectReference toDestroy)
    {
        TeleportClientRpc(targetPos); // Notify owner/client

        if (toDestroy.TryGet(out NetworkObject netObj) && netObj.IsSpawned)
        {
            netObj.Despawn();
            Destroy(netObj.gameObject);
        }

        teleporterRef.Value = default;
    }

    [ClientRpc]
    private void TeleportClientRpc(Vector3 targetPos)
    {
        if (!IsOwner) return;
        transform.position = targetPos;
    }

    [ServerRpc]
    private void DestroyTeleporterServerRpc()
    {
        if (teleporterRef.Value.TryGet(out NetworkObject netObj))
        {
            netObj.Despawn();
            Destroy(netObj.gameObject);
            teleporterRef.Value = default;
        }
    }

    private IEnumerator CaravelCooldownRoutine()
    {
        isCaravelOnCooldown = true;
        hasTeleporterPlaced = false;
        AbilityUI.Instance.StartCooldown(1, caravelCooldown);
        yield return new WaitForSeconds(caravelCooldown);

        isCaravelOnCooldown = false;
    }

    [ClientRpc]
    public void ClearTeleporterClientRpc()
    {
        RequestClearTeleporterServerRpc();
        hasTeleporterPlaced = false;
        isCaravelOnCooldown = false;
    }

    [ServerRpc(RequireOwnership = false)]
    private void RequestClearTeleporterServerRpc()
    {
        teleporterRef.Value = default;
    }

    public void SetPlayerColor(int index)
    {
        if (IsServer)
        {
            playerColorIndex.Value = index;
        }
    }
}
