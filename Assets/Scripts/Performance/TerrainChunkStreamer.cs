using System.Collections.Generic;
using UnityEngine;

[DisallowMultipleComponent]
public class TerrainChunkStreamer : MonoBehaviour
{
    [Header("Player")]
    [SerializeField] private Transform player;
    [SerializeField] private string playerTag = "Player";

    [Header("Chunk Setup")]
    [SerializeField] private Transform chunksRoot;
    [SerializeField] private bool autoCollectChildChunks = true;
    [SerializeField] private float chunkSize = 256f;

    [Header("Streaming Radius (in chunks)")]
    [SerializeField] private int loadRadius = 2;
    [SerializeField] private int unloadRadius = 3;
    [SerializeField] private float checkInterval = 0.25f;

    [Header("Performance")]
    [SerializeField] private bool keepChunkCollidersEnabled = true;

    private float checkTimer;
    private readonly Dictionary<Vector2Int, GameObject> chunkMap = new Dictionary<Vector2Int, GameObject>();
    private readonly HashSet<Vector2Int> visibleSet = new HashSet<Vector2Int>();

    private void Awake()
    {
        ResolvePlayer();
        if (autoCollectChildChunks)
            BuildChunkMap();

        if (unloadRadius <= loadRadius)
            unloadRadius = loadRadius + 1;
    }

    private void Update()
    {
        if (player == null)
            ResolvePlayer();

        if (player == null || chunkMap.Count == 0)
            return;

        checkTimer += Time.deltaTime;
        if (checkTimer < checkInterval)
            return;

        checkTimer = 0f;
        UpdateChunkVisibility();
    }

    [ContextMenu("Build Chunk Map")]
    public void BuildChunkMap()
    {
        chunkMap.Clear();

        if (chunksRoot == null)
        {
            chunksRoot = transform;
        }

        if (chunksRoot == null)
        {
            return;
        }

        for (int i = 0; i < chunksRoot.childCount; i++)
        {
            Transform child = chunksRoot.GetChild(i);
            if (child == null)
                continue;

            Vector2Int coord = WorldToChunkCoord(child.position);
            if (!chunkMap.ContainsKey(coord))
                chunkMap.Add(coord, child.gameObject);
            else
                Debug.LogWarning($"Duplicate terrain chunk coordinate {coord} on {child.name}", child);
        }
    }

    [ContextMenu("Force Refresh")]
    public void ForceRefresh()
    {
        checkTimer = checkInterval;
        UpdateChunkVisibility();
    }

    private void ResolvePlayer()
    {
        if (player != null)
            return;

        if (!string.IsNullOrWhiteSpace(playerTag))
        {
            GameObject playerObject = GameObject.FindGameObjectWithTag(playerTag);
            if (playerObject != null)
                player = playerObject.transform;
        }

        if (player == null && Camera.main != null)
            player = Camera.main.transform;
    }

    private Vector2Int WorldToChunkCoord(Vector3 worldPosition)
    {
        int x = Mathf.FloorToInt(worldPosition.x / Mathf.Max(1f, chunkSize));
        int z = Mathf.FloorToInt(worldPosition.z / Mathf.Max(1f, chunkSize));
        return new Vector2Int(x, z);
    }

    private void UpdateChunkVisibility()
    {
        Vector2Int center = WorldToChunkCoord(player.position);
        int loadR = Mathf.Max(0, loadRadius);
        int unloadR = Mathf.Max(loadR + 1, unloadRadius);
        int unloadR2 = unloadR * unloadR;

        visibleSet.Clear();

        for (int x = -loadR; x <= loadR; x++)
        {
            for (int z = -loadR; z <= loadR; z++)
            {
                Vector2Int coord = new Vector2Int(center.x + x, center.y + z);
                visibleSet.Add(coord);

                GameObject chunk;
                if (chunkMap.TryGetValue(coord, out chunk) && chunk != null && !chunk.activeSelf)
                    chunk.SetActive(true);
            }
        }

        foreach (var kvp in chunkMap)
        {
            Vector2Int coord = kvp.Key;
            GameObject chunk = kvp.Value;
            if (chunk == null)
                continue;

            int dx = coord.x - center.x;
            int dz = coord.y - center.y;
            int dist2 = dx * dx + dz * dz;

            if (dist2 > unloadR2 && chunk.activeSelf)
            {
                chunk.SetActive(false);
                if (!keepChunkCollidersEnabled)
                {
                    Collider[] colliders = chunk.GetComponentsInChildren<Collider>(true);
                    for (int i = 0; i < colliders.Length; i++)
                    {
                        if (colliders[i] != null)
                            colliders[i].enabled = false;
                    }
                }
            }
            else if (dist2 <= unloadR2 && !chunk.activeSelf)
            {
                chunk.SetActive(true);

                if (!keepChunkCollidersEnabled)
                {
                    Collider[] colliders = chunk.GetComponentsInChildren<Collider>(true);
                    for (int i = 0; i < colliders.Length; i++)
                    {
                        if (colliders[i] != null)
                            colliders[i].enabled = true;
                    }
                }
            }
        }
    }
}
