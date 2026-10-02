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

    [Header("Ship tuning (one ShipConfig per ship)")]
    [SerializeField] private ShipConfig galleonConfig;
    [SerializeField] private ShipConfig caravelConfig;
    [SerializeField] private ShipConfig drakkarConfig;
    [SerializeField] private ShipConfig sloopConfig;

    /// <summary>Tuning of the currently selected ship (same on every machine: the selection is a NetworkVariable).</summary>
    public ShipConfig Config => ConfigFor(networkedSelectedShip.Value);

    private ShipConfig ConfigFor(ShipType ship)
    {
        ShipConfig config = ship switch
        {
            ShipType.Galleon => galleonConfig,
            ShipType.Caravel => caravelConfig,
            ShipType.Drakkar => drakkarConfig,
            ShipType.Sloop => sloopConfig,
            _ => null,
        };
        return config != null ? config : ShipConfig.Defaults;
    }

    private bool isSprintOnCooldown = false;

    // Galleon
    private bool isBombOnCooldown = false;

    // Sloop ability: bomb frenzy + speed aura for others
    [SerializeField] private GameObject sloopAuraPrefab;
    private bool isSloopOnCooldown = false;

    //drakkar
    [SerializeField] private GameObject drakkarWallPrefab;
    private bool isDrakkarWallActive = false;
    private bool isDrakkarWallOnCooldown = false;

    //caravel
    [SerializeField] private GameObject caravelTeleporterPrefab;
    private NetworkVariable<NetworkObjectReference> teleporterRef = new();
    private bool hasTeleporterPlaced = false;
    private bool isCaravelOnCooldown = false;
    public NetworkVariable<bool> isInvincible = new(false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    private MaterialPropertyBlock _propBlock;
    private Renderer[] _caravelRenderers;

    private PlayerDeathHandler deathHandler;

    private void Awake()
    {
        deathHandler = GetComponent<PlayerDeathHandler>();
    }

    private bool IsAlive => deathHandler == null || deathHandler.isAlive.Value;

    // Server: abilities only work for a living ship during a live round (not dead, not in the countdown or lobby).
    private bool ServerCanUseAbility() => GameManager.Instance.IsRoundLive && IsAlive;

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
        if (TryGetComponent(out PlayerMovement movement)) movement.ApplyConfig(ConfigFor(ship));

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
        if (!IsOwner || !GameManager.Instance.IsGameActive() || !PlayerMovement.AllowMovement || !IsAlive) return;

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
        AbilityUI.Instance.StartCooldown(0, Config.sprintCooldown);

        GetComponent<PlayerMovement>().SetSprintMultiplier(Config.sprintMultiplier);
        ReportSprintRpc(); // applied locally right away (no input lag); the host checks the cooldown

        yield return new WaitForSeconds(Config.sprintDuration);

        GetComponent<PlayerMovement>().SetSprintMultiplier(1f);

        yield return new WaitForSeconds(Config.sprintCooldown - Config.sprintDuration);

        isSprintOnCooldown = false;
    }

    private double lastSprintGrantTime = double.NegativeInfinity; // server

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
    private void ReportSprintRpc()
    {
        if (!ServerCanUseAbility()) return;

        double now = Time.timeAsDouble;
        // 1 s tolerance: the owner's cooldown and the host's don't start at exactly the same moment.
        if (now - lastSprintGrantTime < Config.sprintCooldown - 1f)
        {
            Debug.LogWarning($"[Move] Client {OwnerClientId} sprinted while sprint is on cooldown; not allowed.");
            return;
        }
        lastSprintGrantTime = now;
        GetComponent<PlayerMovement>().ServerGrantSprint(Config.sprintMultiplier, Config.sprintDuration);
    }

    private IEnumerator GalleonBombRoutine()
    {
        isBombOnCooldown = true;

        Vector3 spawnPos = transform.position + transform.forward * Config.shotSpawnDistance + Vector3.up * 1f;
        Quaternion rotation = transform.rotation;

        ShootBombServerRpc(spawnPos, rotation, OwnerClientId, true);
        AbilityUI.Instance.StartCooldown(1, Config.abilityCooldown);

        yield return new WaitForSeconds(Config.abilityCooldown);
        isBombOnCooldown = false;
    }

    [ServerRpc]
    void ShootBombServerRpc(Vector3 position, Quaternion rotation, ulong creatorId, bool ignoreCreator)
    {
        if (!ServerCanUseAbility()) return;

        GameObject bomb = Instantiate(bombPrefab, position, rotation);
        bomb.GetComponent<Bomb>().DropperClientId = OwnerClientId;
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
            rb.linearVelocity = transform.forward * Config.shotSpeed;
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
        AbilityUI.Instance.StartCooldown(1, Config.abilityCooldown);

        // Bomb frenzy: drop trail bombs faster
        if (movement != null)
        {
            movement.SetSpawnInterval(Config.bombInterval / Config.frenzyBombRateMultiplier);
        }

        // Tell server to spawn aura
        SpawnSloopAuraServerRpc();

        // Wait for duration of the boost
        yield return new WaitForSeconds(Config.frenzyDuration);

        // Restore spawn interval
        if (movement != null)
        {
            movement.SetSpawnInterval(Config.bombInterval);
        }

        // Wait remaining cooldown
        yield return new WaitForSeconds(Config.abilityCooldown - Config.frenzyDuration);
        isSloopOnCooldown = false;
    }

    [ServerRpc]
    private void SpawnSloopAuraServerRpc(ServerRpcParams rpcParams = default)
    {
        if (sloopAuraPrefab == null || !ServerCanUseAbility()) return;

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

            StartCoroutine(DespawnAuraAfterDelay(netObj, Config.frenzyDuration));
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
        AbilityUI.Instance.StartCooldown(1, Config.abilityCooldown);

        yield return new WaitForSeconds(Config.abilityCooldown);
        isDrakkarWallOnCooldown = false;
    }

    [ServerRpc]
    private void SpawnDrakkarWallServerRpc(Vector3 position, Quaternion rotation)
    {
        if (!ServerCanUseAbility()) return;

        GameObject wall = Instantiate(drakkarWallPrefab, position, rotation);
        wall.GetComponent<DrakkarWall>().SetCaster(OwnerClientId);
        wall.GetComponent<NetworkObject>().Spawn();
    }

    private void PlaceTeleporter()
    {
        Vector3 pos = transform.position + Vector3.up * Config.anchorHeight;
        Quaternion rot = Quaternion.identity;

        SpawnTeleporterServerRpc(pos, rot);
        hasTeleporterPlaced = true;
    }

    [ServerRpc]
    private void SpawnTeleporterServerRpc(Vector3 position, Quaternion rotation)
    {
        if (!ServerCanUseAbility()) return;

        GameObject obj = Instantiate(caravelTeleporterPrefab, position, rotation);
        var netObj = obj.GetComponent<NetworkObject>();
        netObj.SpawnWithOwnership(OwnerClientId);
        teleporterRef.Value = netObj;
    }


    private void TeleportToTeleporter()
    {
        if (!teleporterRef.Value.TryGet(out NetworkObject netObj))
        {
            Debug.LogWarning($"[Client {OwnerClientId}] Tried to slide but teleporterRef was unresolved.");
            return;
        }

        Vector3 anchorPos = netObj.transform.position;
        Vector3 target = new Vector3(anchorPos.x, transform.position.y, anchorPos.z);

        // Movement is owner-authoritative: the owner slides its own ship and gets its controls back on arrival.
        StartCoroutine(SlideToTargetClientRoutine(target));

        // The host removes the anchor and handles invincibility (and the bombs it smashes) for the slide's duration.
        StartSlideServerRpc();
        StartCoroutine(CaravelCooldownRoutine());
    }

    [ServerRpc]
    private void StartSlideServerRpc()
    {
        if (!ServerCanUseAbility()) return;

        float distance = 0f;
        if (teleporterRef.Value.TryGet(out NetworkObject anchorNet) && anchorNet.IsSpawned)
        {
            Vector3 a = anchorNet.transform.position;
            distance = Vector3.Distance(transform.position, new Vector3(a.x, transform.position.y, a.z));
            anchorNet.Despawn();
        }
        teleporterRef.Value = default;

        isInvincible.Value = true;
        SetInvincibleVisualClientRpc(true);
        GetComponent<PlayerMovement>().ServerBeginSlide(Config.slideSpeed, Config.slideTurnSpeed);
        StartCoroutine(SlideServerRoutine(distance));
    }

    // Server: the slide's invincibility window. The host used to move the ship itself and only gave the owner its
    // controls back once the host's copy reached the anchor, which could never happen: the owner's synced position
    // kept overriding it, and the Caravel stayed frozen on the anchor.
    private IEnumerator SlideServerRoutine(float distance)
    {
        yield return new WaitForSeconds(distance / Mathf.Max(Config.slideSpeed, 0.01f) + SlideEndSlack);
        GetComponent<PlayerMovement>().ServerEndSlide();

        yield return new WaitForSeconds(Config.invincibilityAfterSlide);

        isInvincible.Value = false;
        SetInvincibleVisualClientRpc(false);
    }

    // The host sees the owner's slide late (latency + interpolation); keep the slide allowance a bit longer.
    private const float SlideEndSlack = 0.3f;

    private IEnumerator SlideToTargetClientRoutine(Vector3 targetPos)
    {
        PlayerMovement movement = GetComponent<PlayerMovement>();
        movement.IsInputLocked = true;

        // Safety net: controls always come back, even if something (a host snap-back) moved the ship mid-slide.
        float maxDuration = Vector3.Distance(transform.position, targetPos) / Mathf.Max(Config.slideSpeed, 0.01f) + 1f;
        float elapsed = 0f;

        const float threshold = 0.3f;
        while (Vector3.Distance(transform.position, targetPos) > threshold && elapsed < maxDuration)
        {
            Vector3 dir = (targetPos - transform.position).normalized;
            Quaternion targetRot = Quaternion.LookRotation(dir);
            transform.rotation = Quaternion.RotateTowards(transform.rotation, targetRot, Config.slideTurnSpeed * Time.deltaTime);
            transform.position = Vector3.MoveTowards(transform.position, targetPos, Config.slideSpeed * Time.deltaTime);
            elapsed += Time.deltaTime;
            yield return null;
        }

        movement.IsInputLocked = false;
    }

    [ClientRpc]
    private void SetInvincibleVisualClientRpc(bool active)
    {
        ApplyWhiteTint(active);
    }

    private void ApplyWhiteTint(bool active)
    {
        if (_propBlock == null) _propBlock = new MaterialPropertyBlock();
        if (_caravelRenderers == null) _caravelRenderers = Caravel.GetComponentsInChildren<Renderer>();

        foreach (var r in _caravelRenderers)
        {
            r.GetPropertyBlock(_propBlock);
            if (active)
                _propBlock.SetColor("_BaseColor", Color.white);
            else
                _propBlock.Clear();
            r.SetPropertyBlock(_propBlock);
        }
    }

    private IEnumerator CaravelCooldownRoutine()
    {
        isCaravelOnCooldown = true;
        hasTeleporterPlaced = false;
        AbilityUI.Instance.StartCooldown(1, Config.abilityCooldown);
        yield return new WaitForSeconds(Config.abilityCooldown);

        isCaravelOnCooldown = false;
    }

    /// <summary>
    /// Called from the server to destroy any active teleporter and clear the ref.
    /// </summary>
    public void ServerClearTeleporter()
    {
        if (!IsServer) return;

        if (teleporterRef.Value.TryGet(out NetworkObject netObj) && netObj.IsSpawned)
        {
            netObj.Despawn();
            Destroy(netObj.gameObject);
        }
        teleporterRef.Value = default;
    }

    /// <summary>
    /// Resets all ability cooldowns and state. Called on both server and clients via ClientRpc.
    /// Server should call ServerClearTeleporter() before calling this.
    /// </summary>
    [ClientRpc]
    public void ResetAbilityStateClientRpc()
    {
        StopAllCoroutines();
        AbilityUI.Instance?.ResetCooldowns();

        // Sprint
        isSprintOnCooldown = false;
        GetComponent<PlayerMovement>().SetSprintMultiplier(1f);
        if (IsServer)
        {
            lastSprintGrantTime = double.NegativeInfinity;
            GetComponent<PlayerMovement>().ServerResetAllowances();
        }

        // Galleon
        isBombOnCooldown = false;

        // Sloop
        isSloopOnCooldown = false;
        PlayerMovement movement = GetComponent<PlayerMovement>();
        if (movement != null)
        {
            movement.SetSpawnInterval(Config.bombInterval);
            if (IsOwner) movement.CancelSloopEffect();
        }

        // Drakkar
        isDrakkarWallActive = false;
        isDrakkarWallOnCooldown = false;

        // Caravel
        hasTeleporterPlaced = false;
        isCaravelOnCooldown = false;
        if (IsServer) isInvincible.Value = false;
        GetComponent<PlayerMovement>().IsInputLocked = false;
        ApplyWhiteTint(false);
    }

    public void SetPlayerColor(int index)
    {
        if (IsServer)
        {
            playerColorIndex.Value = index;
        }
    }
}
