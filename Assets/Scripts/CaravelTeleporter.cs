using UnityEngine;
using Unity.Netcode;

public class CaravelTeleporter : NetworkBehaviour
{
    [SerializeField] private float bombClearRadius = 5f;
    [SerializeField] private float clearInterval = 0.5f;

    [SerializeField] private float floatAmplitude = 0.3f;
    [SerializeField] private float floatSpeed = 1.5f;
    [SerializeField] private float spinSpeed = 90f; // degrees per second

    private float _clearTimer;
    private Vector3 _basePosition;

    private void Start()
    {
        _basePosition = transform.position;
    }

    void Update()
    {
        // Float up and down around the spawn position (all clients)
        float yOffset = Mathf.Sin(Time.time * floatSpeed) * floatAmplitude;
        transform.position = _basePosition + Vector3.up * yOffset;

        // Spin on the vertical axis (all clients)
        transform.Rotate(Vector3.up, spinSpeed * Time.deltaTime, Space.World);

        // Bomb clearing (server only)
        if (!IsServer) return;
        _clearTimer += Time.deltaTime;
        if (_clearTimer >= clearInterval)
        {
            _clearTimer = 0f;
            ClearNearbyBombs();
        }
    }

    private void ClearNearbyBombs()
    {
        Collider[] hits = Physics.OverlapSphere(_basePosition, bombClearRadius);
        foreach (var hit in hits)
        {
            if (hit.CompareTag("Bomb"))
            {
                if (hit.TryGetComponent(out NetworkObject netObj) && netObj.IsSpawned)
                    netObj.Despawn();

                Destroy(hit.gameObject);
            }
        }
    }
}
