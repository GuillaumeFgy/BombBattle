using System.Collections.Generic;
using System.Collections;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

public class PlayerMovement : NetworkBehaviour
{
    // Ship stats come from the ship's ShipConfig (ApplyConfig), on every machine, so the host checks movement with
    // the same numbers the owner moves with. Edit them on the ShipConfig assets, not here.
    private float moveSpeed = 10f;
    private float rotationSpeed = 1f;
    private float spawnInterval = 0.5f;
    private Camera playerCamera;

    [SerializeField] GameObject bombPrefab;
    [SerializeField] private Vector3 bombSpawnPos;
    private float bombTimer = 0f;

    private float sprintMultiplier = 1f;
    public bool IsInputLocked = false;

    private Vector3 _launchVelocity = Vector3.zero;
    [SerializeField] private float launchDecay = 5f;

    public static bool AllowMovement = false;

    public override void OnNetworkSpawn()
    {
        if (IsServer && GameManager.Instance.IsGameActive())
        {
            int index = GetPlayerIndex(OwnerClientId);
            Transform spawnPoint = GameManager.Instance.GetSpawnPoint(index);
            if (spawnPoint != null)
            {
                transform.position = spawnPoint.position;
                transform.rotation = spawnPoint.rotation;
            }
        }
    }

    private void Awake()
    {
        playerCamera = GetComponentInChildren<Camera>(true);
    }

    void FixedUpdate()
    {
        if (IsServer && !IsOwner) ServerValidateMovement();

        if (!IsOwner || !AllowMovement || IsInputLocked) return;

        MoveForward();
        RotateTowardsMouse();
        SpawnBombs();
        ApplyLaunch();
    }

    public void SetSprintMultiplier(float mult)
    {
        sprintMultiplier = mult;
    }

    public void ApplyConfig(ShipConfig config)
    {
        moveSpeed = config.moveSpeed;
        rotationSpeed = config.turnSpeed;
        spawnInterval = config.bombInterval;
    }

    void MoveForward()
    {
        float currentSpeed = moveSpeed * sprintMultiplier * sloopSpeedMultiplier;
        transform.position += transform.forward * currentSpeed * Time.fixedDeltaTime;
    }

    void RotateTowardsMouse()
    {
        if (Mouse.current == null || playerCamera == null) return;

        Vector2 mousePos = Mouse.current.position.ReadValue();
        Ray ray = playerCamera.ScreenPointToRay(mousePos);
        Plane plane = new Plane(Vector3.up, transform.position);

        if (plane.Raycast(ray, out float distance))
        {
            Vector3 hitPoint = ray.GetPoint(distance);
            Vector3 direction = (hitPoint - transform.position).normalized;
            Quaternion targetRotation = Quaternion.LookRotation(direction);
            transform.rotation = Quaternion.Slerp(transform.rotation, targetRotation, rotationSpeed * Time.fixedDeltaTime);
        }
    }

    void SpawnBombs()
    {
        bombTimer += Time.fixedDeltaTime;
        if (bombTimer >= spawnInterval)
        {
            bombTimer = 0f;
            RequestBombSpawnServerRpc(transform.position - transform.forward * 3f);
        }
    }

    [ServerRpc]
    void RequestBombSpawnServerRpc(Vector3 spawnPosition)
    {
        // Requests still in flight when the round ended would leave bombs on the next round's sea.
        if (!GameManager.Instance.IsRoundLive) return;
        if (TryGetComponent(out PlayerDeathHandler deathHandler) && !deathHandler.isAlive.Value) return;

        GameObject bomb = Instantiate(bombPrefab, spawnPosition, Quaternion.identity);
        bomb.GetComponent<Bomb>().DropperClientId = OwnerClientId;
        bomb.GetComponent<NetworkObject>().Spawn();
        if (TryGetComponent(out PlayerClass playerClass))
        {
            Bomb bombScript = bomb.GetComponent<Bomb>();
            if (bombScript != null)
            {
                int index = playerClass.GetColorIndex();
                Color bombColor = GameManager.Instance.GetColorByIndex(index);
                bombScript.SetColor(bombColor);
            }
        }
    }



    private int GetPlayerIndex(ulong clientId)
    {
        var list = ScoreboardManager.Instance.GetPlayerList(); // Add a getter method
        for (int i = 0; i < list.Count; i++)
        {
            if (list[i].playerId == clientId)
                return i;
        }
        return 0; // Fallback
    }

    public float GetSpawnInterval() => spawnInterval;
    public void SetSpawnInterval(float value) => spawnInterval = value;

    private void ApplyLaunch()
    {
        if (_launchVelocity.sqrMagnitude < 0.01f) return;
        transform.position += _launchVelocity * Time.fixedDeltaTime;
        _launchVelocity = Vector3.Lerp(_launchVelocity, Vector3.zero, launchDecay * Time.fixedDeltaTime);
    }

    /// <summary>Server only: push this ship (Drakkar wave). Also lets the movement check allow the extra speed.</summary>
    public void ServerApplyKnockback(Vector3 direction, float force)
    {
        if (!IsServer) return;
        knockbackSpeed = force;
        knockbackUntil = Time.timeAsDouble + KnockbackAllowance + AllowanceSlack;
        ApplyKnockbackClientRpc(direction, force);
    }

    [ClientRpc]
    private void ApplyKnockbackClientRpc(Vector3 direction, float force)
    {
        if (!IsOwner) return;
        _launchVelocity = direction * force;
    }

    // ── Sloop speed boost (owner-side only) ──────────────────────────────────

    // Kept separate from moveSpeed so the boost never overwrites the ship's configured base speed.
    private float sloopSpeedMultiplier = 1f;
    private Coroutine _sloopRestoreCoroutine;

    /// <summary>Server only: the Sloop aura reached this ship. Records the boost for the movement check.</summary>
    public void ServerStartSloopBoost(float boostMultiplier)
    {
        if (!IsServer) return;
        sloopMultiplier = boostMultiplier;
        sloopUntil = double.MaxValue; // until the ship leaves the aura
        StartSloopSpeedBoostClientRpc(boostMultiplier);
    }

    /// <summary>Server only: the ship left the aura; the boost wears off after <paramref name="delay"/>.</summary>
    public void ServerBeginSloopRestore(float delay)
    {
        if (!IsServer) return;
        sloopUntil = Time.timeAsDouble + delay + AllowanceSlack;
        BeginSloopSpeedRestoreClientRpc(delay);
    }

    // Called by the aura when this player enters it.
    // Applies the boost once and cancels any pending restore timer.
    [ClientRpc]
    private void StartSloopSpeedBoostClientRpc(float boostMultiplier)
    {
        if (!IsOwner) return;
        sloopSpeedMultiplier = boostMultiplier;
        // Player re-entered the aura before the restore fired — cancel it
        if (_sloopRestoreCoroutine != null)
        {
            StopCoroutine(_sloopRestoreCoroutine);
            _sloopRestoreCoroutine = null;
        }
    }

    // Called by the aura when this player exits it (or the aura despawns).
    // Starts the countdown to restore base speed.
    [ClientRpc]
    private void BeginSloopSpeedRestoreClientRpc(float delay)
    {
        if (!IsOwner) return;
        if (_sloopRestoreCoroutine != null)
            StopCoroutine(_sloopRestoreCoroutine);
        _sloopRestoreCoroutine = StartCoroutine(RestoreSloopSpeedRoutine(delay));
    }

    // Called on reset/death to immediately cancel the effect.
    public void CancelSloopEffect()
    {
        if (_sloopRestoreCoroutine != null)
        {
            StopCoroutine(_sloopRestoreCoroutine);
            _sloopRestoreCoroutine = null;
        }
        sloopSpeedMultiplier = 1f;
    }

    private IEnumerator RestoreSloopSpeedRoutine(float delay)
    {
        yield return new WaitForSeconds(delay);
        sloopSpeedMultiplier = 1f;
        _sloopRestoreCoroutine = null;
    }

    // ── Host movement check (server side, remote ships only) ─────────────────
    // Movement is owner-authoritative. The host checks it loosely (party game): over a sliding window, distance
    // moved and yaw turned must fit what the ship's current allowances permit, plus margins. Speed boosts are recorded
    // here on the host when they're granted. Two failed checks in a row snap the ship back to its last valid spot.

    [Header("Host movement check")]
    [SerializeField] private float checkWindow = 0.5f;
    [SerializeField] private float checkInterval = 0.25f;
    [Tooltip("Extra fraction of allowed speed / turn rate tolerated (interpolation catch-up, jitter).")]
    [SerializeField] private float speedMargin = 0.3f;
    [SerializeField] private float distanceMargin = 3f;
    [SerializeField] private float turnMargin = 0.3f;
    [SerializeField] private float turnAngleMargin = 25f;
    [SerializeField] private int violationsBeforeSnap = 2;
    [Tooltip("Allowances last this much longer than the effect (plus the owner's RTT): the owner applies them late.")]
    [SerializeField] private float allowanceSlack = 0.5f;

    private const float KnockbackAllowance = 1f;

    private struct MoveSample
    {
        public double Time;
        public Vector3 Position;
        public float Yaw;
        public float YawStep;
    }

    private readonly List<MoveSample> moveSamples = new();
    private double nextCheckTime;
    private int consecutiveViolations;
    private bool hasLastGood;
    private Vector3 lastGoodPosition;
    private Quaternion lastGoodRotation;

    // Allowances (server time, Time.timeAsDouble)
    private float sprintAllowanceMultiplier = 1f;
    private double sprintUntil = -1;
    private float sloopMultiplier = 1f;
    private double sloopUntil = -1;
    private float knockbackSpeed;
    private double knockbackUntil = -1;
    private float slideSpeed, slideTurnRate;
    private double slideUntil = -1;
    private double teleportGraceUntil = -1;

    private double AllowanceSlack =>
        allowanceSlack + (IsSpawned ? NetworkManager.NetworkConfig.NetworkTransport.GetCurrentRtt(OwnerClientId) / 1000.0 : 0.0);

    /// <summary>Server only: the owner started a sprint the host approved.</summary>
    public void ServerGrantSprint(float multiplier, float duration)
    {
        sprintAllowanceMultiplier = multiplier;
        sprintUntil = Time.timeAsDouble + duration + AllowanceSlack;
    }

    /// <summary>Server only: Caravel slide started (fast move + fast turn until <see cref="ServerEndSlide"/>).</summary>
    public void ServerBeginSlide(float speed, float turnRateDegrees)
    {
        slideSpeed = speed;
        slideTurnRate = turnRateDegrees;
        slideUntil = double.MaxValue;
    }

    public void ServerEndSlide()
    {
        slideUntil = Time.timeAsDouble + AllowanceSlack;
    }

    /// <summary>Server only: a host-approved teleport (round reset, slide snap) is on its way to the owner.</summary>
    public void ServerExpectTeleport()
    {
        teleportGraceUntil = Time.timeAsDouble + 0.5 + AllowanceSlack;
        ResetMoveSamples();
    }

    /// <summary>Server only: abilities were reset (new round); drop every speed allowance.</summary>
    public void ServerResetAllowances()
    {
        sprintUntil = sloopUntil = knockbackUntil = slideUntil = -1;
    }

    private void OnEnable() => ResetMoveSamples();

    private void ResetMoveSamples()
    {
        moveSamples.Clear();
        consecutiveViolations = 0;
        hasLastGood = false;
    }

    private float AllowedSpeed(double now)
    {
        float speed = moveSpeed; // the ship's configured base speed (same ShipConfig as the owner)
        if (now < sprintUntil) speed *= sprintAllowanceMultiplier;
        if (now < sloopUntil) speed *= sloopMultiplier;
        if (now < knockbackUntil) speed += knockbackSpeed;
        if (now < slideUntil) speed = Mathf.Max(speed, slideSpeed);
        return speed;
    }

    private float AllowedTurnRate(double now)
    {
        // RotateTowardsMouse slerps by rotationSpeed * dt per step: fastest when the target is 180° away.
        float rate = 180f * rotationSpeed;
        if (now < slideUntil) rate = Mathf.Max(rate, slideTurnRate);
        return rate;
    }

    private void ServerValidateMovement()
    {
        double now = Time.timeAsDouble;
        if (!IsSpawned || !AllowMovement || now < teleportGraceUntil)
        {
            ResetMoveSamples();
            return;
        }

        Vector3 pos = transform.position;
        float yaw = transform.eulerAngles.y;
        float yawStep = moveSamples.Count > 0 ? Mathf.Abs(Mathf.DeltaAngle(moveSamples[^1].Yaw, yaw)) : 0f;
        moveSamples.Add(new MoveSample { Time = now, Position = pos, Yaw = yaw, YawStep = yawStep });

        // Keep exactly one sample at least checkWindow old as the window start.
        while (moveSamples.Count > 2 && now - moveSamples[1].Time >= checkWindow) moveSamples.RemoveAt(0);

        if (!hasLastGood)
        {
            hasLastGood = true;
            lastGoodPosition = pos;
            lastGoodRotation = transform.rotation;
        }

        MoveSample start = moveSamples[0];
        if (now < nextCheckTime || now - start.Time < checkWindow) return;
        nextCheckTime = now + checkInterval;

        float dt = (float)(now - start.Time);
        float moved = Vector2.Distance(new Vector2(start.Position.x, start.Position.z), new Vector2(pos.x, pos.z));
        float turned = 0f;
        for (int i = 1; i < moveSamples.Count; i++) turned += moveSamples[i].YawStep;

        float maxMove = AllowedSpeed(now) * dt * (1f + speedMargin) + distanceMargin;
        float maxTurn = AllowedTurnRate(now) * dt * (1f + turnMargin) + turnAngleMargin;

        if (moved <= maxMove && turned <= maxTurn)
        {
            consecutiveViolations = 0;
            lastGoodPosition = pos;
            lastGoodRotation = transform.rotation;
            return;
        }

        consecutiveViolations++;
        Debug.LogWarning($"[Move] Client {OwnerClientId}: moved {moved:0.0} m (max {maxMove:0.0}) and turned {turned:0}° " +
                         $"(max {maxTurn:0}°) in {dt:0.00} s — violation {consecutiveViolations}/{violationsBeforeSnap}");

        if (consecutiveViolations < violationsBeforeSnap) return;

        Debug.LogWarning($"[Move] Snapping client {OwnerClientId} back to {lastGoodPosition}.");
        Vector3 snapPosition = lastGoodPosition;
        Quaternion snapRotation = lastGoodRotation;
        ServerExpectTeleport();
        SnapToRpc(snapPosition, snapRotation);
    }

    [Rpc(SendTo.Owner)]
    private void SnapToRpc(Vector3 position, Quaternion rotation)
    {
        transform.SetPositionAndRotation(position, rotation);
        _launchVelocity = Vector3.zero;
    }
}
