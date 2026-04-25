using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

[DisallowMultipleComponent]
public class ZombieSpawner : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private Player player;
    [SerializeField] private Transform playerTagFallback;
    [SerializeField] private GameObject zombiePrefab;

    [Header("Spawn Timing")]
    [SerializeField] private float spawnIntervalSeconds = 20f;
    [SerializeField] private int spawnBatchCount = 1;
    [SerializeField] private int maxAliveZombies = 20;

    [Header("Spawn Radius")]
    [SerializeField] private float minSpawnRadius = 25f;
    [SerializeField] private float maxSpawnRadius = 60f;
    [SerializeField] private float despawnRadius = 90f;

    [Header("Placement")]
    [SerializeField] private LayerMask groundLayers = ~0;
    [SerializeField] private float raycastHeight = 120f;
    [SerializeField] private float raycastDistance = 250f;
    [SerializeField] private float navMeshSampleDistance = 6f;
    [SerializeField] private int maxSpawnAttemptsPerBatch = 10;
    [SerializeField] private bool avoidSpawningTooCloseToPlayer = true;
    [SerializeField] private float minDistanceFromPlayer = 12f;

    [Header("Limits")]
    [SerializeField] private bool onlySpawnWhenPlayerIsGrounded = false;

    [Header("Debug")]
    [SerializeField] private bool drawGizmos = true;

    private float nextSpawnTime;
    private readonly List<GameObject> aliveZombies = new List<GameObject>();

    private void Awake()
    {
        ResolvePlayer();
        nextSpawnTime = Time.time + Random.Range(1f, Mathf.Max(1f, spawnIntervalSeconds));
    }

    private void Update()
    {
        ResolvePlayer();

        if (player == null || zombiePrefab == null)
            return;

        CleanupDeadZombies();
        DespawnFarZombies();

        if (onlySpawnWhenPlayerIsGrounded && !IsPlayerGrounded())
            return;

        if (Time.time < nextSpawnTime)
            return;

        nextSpawnTime = Time.time + Mathf.Max(0.1f, spawnIntervalSeconds);
        SpawnBatch();
    }

    private void SpawnBatch()
    {
        if (player == null || zombiePrefab == null)
            return;

        int aliveCount = GetAliveZombieCount();
        if (aliveCount >= maxAliveZombies)
            return;

        int toSpawn = Mathf.Clamp(spawnBatchCount, 1, Mathf.Max(1, maxAliveZombies - aliveCount));
        int spawned = 0;

        for (int i = 0; i < toSpawn; i++)
        {
            if (TrySpawnZombie())
                spawned++;
        }

        if (spawned > 0)
            Debug.Log($"[ZombieSpawner] Spawned {spawned} zombie(s). Alive now: {GetAliveZombieCount()}");
    }

    private bool TrySpawnZombie()
    {
        if (player == null || zombiePrefab == null)
            return false;

        for (int attempt = 0; attempt < Mathf.Max(1, maxSpawnAttemptsPerBatch); attempt++)
        {
            Vector3 candidate = GetRandomPointAroundPlayer();
            if (!TryProjectToGround(candidate, out Vector3 groundPoint))
                continue;

            if (avoidSpawningTooCloseToPlayer)
            {
                float dist = Vector3.Distance(groundPoint, player.transform.position);
                if (dist < minDistanceFromPlayer)
                    continue;
            }

            if (!TryProjectToNavMesh(groundPoint, out Vector3 navPoint))
                continue;

            GameObject zombie = Instantiate(zombiePrefab, navPoint, Quaternion.identity);
            aliveZombies.Add(zombie);
            return true;
        }

        return false;
    }

    private Vector3 GetRandomPointAroundPlayer()
    {
        Vector2 circle = Random.insideUnitCircle.normalized * Random.Range(minSpawnRadius, maxSpawnRadius);
        Vector3 basePos = player.transform.position + new Vector3(circle.x, 0f, circle.y);
        return new Vector3(basePos.x, player.transform.position.y + raycastHeight, basePos.z);
    }

    private bool TryProjectToGround(Vector3 position, out Vector3 groundPoint)
    {
        groundPoint = default;
        Vector3 rayStart = position + Vector3.up * raycastHeight;
        if (Physics.Raycast(rayStart, Vector3.down, out RaycastHit hit, raycastDistance, groundLayers, QueryTriggerInteraction.Ignore))
        {
            groundPoint = hit.point;
            return true;
        }

        return false;
    }

    private bool TryProjectToNavMesh(Vector3 position, out Vector3 navPoint)
    {
        if (NavMesh.SamplePosition(position, out NavMeshHit hit, navMeshSampleDistance, NavMesh.AllAreas))
        {
            navPoint = hit.position;
            return true;
        }

        navPoint = default;
        return false;
    }

    private void DespawnFarZombies()
    {
        if (despawnRadius <= 0f || player == null)
            return;

        float despawnSqr = despawnRadius * despawnRadius;
        for (int i = aliveZombies.Count - 1; i >= 0; i--)
        {
            GameObject zombie = aliveZombies[i];
            if (zombie == null)
            {
                aliveZombies.RemoveAt(i);
                continue;
            }

            if ((zombie.transform.position - player.transform.position).sqrMagnitude > despawnSqr)
            {
                Destroy(zombie);
                aliveZombies.RemoveAt(i);
            }
        }
    }

    private void CleanupDeadZombies()
    {
        for (int i = aliveZombies.Count - 1; i >= 0; i--)
        {
            if (aliveZombies[i] == null)
                aliveZombies.RemoveAt(i);
        }
    }

    private int GetAliveZombieCount()
    {
        CleanupDeadZombies();
        return aliveZombies.Count;
    }

    private bool IsPlayerGrounded()
    {
        if (player == null)
            return false;

        CharacterController controller = player.GetComponent<CharacterController>();
        if (controller != null)
            return controller.isGrounded;

        return Physics.Raycast(player.transform.position + Vector3.up * 0.2f, Vector3.down, 1.2f, groundLayers, QueryTriggerInteraction.Ignore);
    }

    private void ResolvePlayer()
    {
        if (player != null)
            return;

        if (playerTagFallback != null)
        {
            player = playerTagFallback.GetComponent<Player>();
            if (player != null)
                return;
        }

        player = FindAnyObjectByType<Player>();
    }

    private void OnDrawGizmosSelected()
    {
        if (!drawGizmos)
            return;

        Transform target = player != null ? player.transform : null;
        if (target == null)
            return;

        Gizmos.color = Color.red;
        Gizmos.DrawWireSphere(target.position, minSpawnRadius);
        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere(target.position, maxSpawnRadius);
        Gizmos.color = Color.blue;
        Gizmos.DrawWireSphere(target.position, despawnRadius);
    }
}
