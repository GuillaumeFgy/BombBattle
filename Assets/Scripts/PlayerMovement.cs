using System.Collections;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

public class PlayerMovement : NetworkBehaviour
{
    public float moveSpeed = 20f;
    public float rotationSpeed = 5f;
    private Camera playerCamera;

    [SerializeField] GameObject bombPrefab;
    [SerializeField] private Vector3 bombSpawnPos;
    [SerializeField] private float spawnInterval = 0.5f;
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

    public 

    void MoveForward()
    {
        float currentSpeed = moveSpeed * sprintMultiplier;
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
        GameObject bomb = Instantiate(bombPrefab, spawnPosition, Quaternion.identity);
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

    public void SetMoveSpeed(float speed) => moveSpeed = speed;
    public float GetMoveSpeed() => moveSpeed;

    private void ApplyLaunch()
    {
        if (_launchVelocity.sqrMagnitude < 0.01f) return;
        transform.position += _launchVelocity * Time.fixedDeltaTime;
        _launchVelocity = Vector3.Lerp(_launchVelocity, Vector3.zero, launchDecay * Time.fixedDeltaTime);
    }

    [ClientRpc]
    public void ApplyKnockbackClientRpc(Vector3 direction, float force)
    {
        if (!IsOwner) return;
        _launchVelocity = direction * force;
    }

    // ── Sloop speed boost (owner-side only) ──────────────────────────────────

    private float _originalMoveSpeedBeforeSloop;
    private bool _sloopAffected = false;
    private Coroutine _sloopRestoreCoroutine;

    // Called by the aura when this player enters it.
    // Applies the boost once and cancels any pending restore timer.
    [ClientRpc]
    public void StartSloopSpeedBoostClientRpc(float boostMultiplier)
    {
        if (!IsOwner) return;
        if (!_sloopAffected)
        {
            _originalMoveSpeedBeforeSloop = moveSpeed;
            moveSpeed *= boostMultiplier;
            _sloopAffected = true;
        }
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
    public void BeginSloopSpeedRestoreClientRpc(float delay)
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
        if (_sloopAffected)
        {
            moveSpeed = _originalMoveSpeedBeforeSloop;
            _sloopAffected = false;
        }
    }

    private IEnumerator RestoreSloopSpeedRoutine(float delay)
    {
        yield return new WaitForSeconds(delay);
        if (_sloopAffected)
        {
            moveSpeed = _originalMoveSpeedBeforeSloop;
            _sloopAffected = false;
        }
        _sloopRestoreCoroutine = null;
    }

}
