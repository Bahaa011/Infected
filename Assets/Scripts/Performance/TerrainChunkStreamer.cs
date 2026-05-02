using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.SceneManagement;

[DisallowMultipleComponent]
public class TerrainChunkStreamer : MonoBehaviour
{
    private static TerrainChunkStreamer activeInstance;

    [Header("Player")]
    [SerializeField] private Transform player;
    [SerializeField] private string playerTag = "Player";

    [Header("Tile/Scene Setup")]
    [SerializeField] private float chunkSize = 256f;
    [SerializeField] private Vector2 worldOrigin = Vector2.zero;
    [SerializeField] private string scenePrefix = "Tile";
    [SerializeField] private bool limitToGridBounds = false;
    [SerializeField] private Vector2Int gridSize = new Vector2Int(8, 8);

    [Header("Streaming Radius (in chunks)")]
    [SerializeField] private int loadRadius = 2;
    [SerializeField] private int unloadRadius = 3;
    [SerializeField] private float checkInterval = 0.25f;
    [SerializeField] private bool unloadPreloadedTileScenesOnStart = true;

    public static bool IsWorldReady { get; private set; } = true;

    private float checkTimer;
    private float initialLoadTimeout;

    private readonly Dictionary<Vector2Int, string> coordToSceneName = new Dictionary<Vector2Int, string>();
    private readonly HashSet<Vector2Int> loadedCoords = new HashSet<Vector2Int>();
    private readonly HashSet<Vector2Int> loadingCoords = new HashSet<Vector2Int>();
    private readonly HashSet<Vector2Int> unloadingCoords = new HashSet<Vector2Int>();
    private Vector2Int lastCenterCoord;
    private bool hasLastCenterCoord;
    private bool initialLoadCompleted;

    private void OnEnable()
    {
        if (activeInstance != null && activeInstance != this)
        {
            Debug.LogWarning("Multiple TerrainChunkStreamer instances are active. Disabling duplicate instance.", this);
            enabled = false;
            return;
        }

        activeInstance = this;
        SceneManager.sceneLoaded += OnSceneLoaded;
        SceneManager.sceneUnloaded += OnSceneUnloaded;
    }

    private void OnDisable()
    {
        if (activeInstance == this)
            activeInstance = null;

        SceneManager.sceneLoaded -= OnSceneLoaded;
        SceneManager.sceneUnloaded -= OnSceneUnloaded;
    }

    private void Awake()
    {
        if (activeInstance != null && activeInstance != this)
            return;

        IsWorldReady = false;
        initialLoadCompleted = false;
        initialLoadTimeout = 0f;

        ResolvePlayer();
        BuildSceneIndex();
        SyncLoadedTileScenes();

        if (unloadRadius <= loadRadius)
            unloadRadius = loadRadius + 1;

        if (unloadPreloadedTileScenesOnStart)
            ForceRefresh();

        TryMarkInitialLoadComplete();
    }

    private void Update()
    {
        if (activeInstance != this)
            return;

        if (player == null)
            ResolvePlayer();

        if (player == null || coordToSceneName.Count == 0)
            return;

        // Safety timeout for initial load - unblock movement if it takes too long
        if (!initialLoadCompleted && initialLoadTimeout < 3f)
        {
            initialLoadTimeout += Time.deltaTime;
            if (initialLoadTimeout >= 3f)
            {
                Debug.LogWarning("[TerrainChunkStreamer] Initial load timeout - marking world ready");
                initialLoadCompleted = true;
                IsWorldReady = true;
                WorldLoadingState.MarkWorldReady();
                return;
            }
        }

        checkTimer += Time.deltaTime;
        if (checkTimer < checkInterval)
            return;

        checkTimer = 0f;

        Vector2Int center = WorldToChunkCoord(player.position);
        bool centerChanged = !hasLastCenterCoord || center != lastCenterCoord;
        bool hasPendingOps = loadingCoords.Count > 0 || unloadingCoords.Count > 0;

        if (!centerChanged && !hasPendingOps)
        {
            TryMarkInitialLoadComplete();
            return;
        }

        hasLastCenterCoord = true;
        lastCenterCoord = center;
        UpdateChunkVisibility(center);

        TryMarkInitialLoadComplete();
    }

    [ContextMenu("Build Scene Index")]
    public void BuildSceneIndex()
    {
        coordToSceneName.Clear();

        int total = SceneManager.sceneCountInBuildSettings;
        string expectedPrefix = scenePrefix + "_";

        for (int i = 0; i < total; i++)
        {
            string path = SceneUtility.GetScenePathByBuildIndex(i);
            if (string.IsNullOrWhiteSpace(path))
                continue;

            string sceneName = Path.GetFileNameWithoutExtension(path);
            if (!sceneName.StartsWith(expectedPrefix, StringComparison.Ordinal))
                continue;

            if (!TryParseCoord(sceneName, scenePrefix, out Vector2Int coord))
                continue;

            if (!coordToSceneName.ContainsKey(coord))
                coordToSceneName.Add(coord, sceneName);
            else
                Debug.LogWarning($"Duplicate tile scene coordinate {coord} on scene {sceneName}.");
        }

        if (coordToSceneName.Count == 0)
        {
            Debug.LogWarning("No tile scenes found in Build Settings. Add scenes like Tile_x_z to Build Settings.");
        }
    }

    [ContextMenu("Force Refresh")]
    public void ForceRefresh()
    {
        SyncLoadedTileScenes();
        hasLastCenterCoord = false;
        checkTimer = checkInterval;
        if (player != null)
            UpdateChunkVisibility(WorldToChunkCoord(player.position));

        TryMarkInitialLoadComplete();
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
        float size = Mathf.Max(1f, chunkSize);
        int x = Mathf.FloorToInt((worldPosition.x - worldOrigin.x) / size);
        int z = Mathf.FloorToInt((worldPosition.z - worldOrigin.y) / size);
        return new Vector2Int(x, z);
    }

    private void UpdateChunkVisibility(Vector2Int center)
    {
        int loadR = Mathf.Max(0, loadRadius);
        int unloadR = Mathf.Max(loadR + 1, unloadRadius);
        int unloadR2 = unloadR * unloadR;

        HashSet<Vector2Int> shouldLoad = new HashSet<Vector2Int>();

        for (int x = -loadR; x <= loadR; x++)
        {
            for (int z = -loadR; z <= loadR; z++)
            {
                Vector2Int coord = new Vector2Int(center.x + x, center.y + z);

                if (limitToGridBounds && (coord.x < 0 || coord.y < 0 || coord.x >= gridSize.x || coord.y >= gridSize.y))
                    continue;

                if (!coordToSceneName.ContainsKey(coord))
                    continue;

                shouldLoad.Add(coord);

                if (!loadedCoords.Contains(coord) && !loadingCoords.Contains(coord) && !unloadingCoords.Contains(coord))
                {
                    StartCoroutine(LoadTileScene(coord));
                }
            }
        }

        List<Vector2Int> unloadList = new List<Vector2Int>();

        foreach (Vector2Int coord in loadedCoords)
        {
            if (shouldLoad.Contains(coord) || unloadingCoords.Contains(coord))
                continue;

            int dx = coord.x - center.x;
            int dz = coord.y - center.y;
            int dist2 = dx * dx + dz * dz;
            if (dist2 > unloadR2)
                unloadList.Add(coord);
        }

        for (int i = 0; i < unloadList.Count; i++)
        {
            StartCoroutine(UnloadTileScene(unloadList[i]));
        }
    }

    private void SyncLoadedTileScenes()
    {
        loadedCoords.Clear();

        foreach (KeyValuePair<Vector2Int, string> kvp in coordToSceneName)
        {
            Scene scene = SceneManager.GetSceneByName(kvp.Value);
            if (scene.IsValid() && scene.isLoaded)
                loadedCoords.Add(kvp.Key);
        }
    }

    private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (TryParseCoord(scene.name, scenePrefix, out Vector2Int coord) && coordToSceneName.ContainsKey(coord))
            loadedCoords.Add(coord);

        TryMarkInitialLoadComplete();
    }

    private void OnSceneUnloaded(Scene scene)
    {
        if (TryParseCoord(scene.name, scenePrefix, out Vector2Int coord))
            loadedCoords.Remove(coord);
    }

    private IEnumerator LoadTileScene(Vector2Int coord)
    {
        if (!coordToSceneName.TryGetValue(coord, out string sceneName))
            yield break;

        Scene scene = SceneManager.GetSceneByName(sceneName);
        if (scene.IsValid() && scene.isLoaded)
        {
            loadedCoords.Add(coord);
            yield break;
        }

        loadingCoords.Add(coord);

        AsyncOperation op = SceneManager.LoadSceneAsync(sceneName, LoadSceneMode.Additive);
        if (op == null)
        {
            Debug.LogError($"Failed to start loading scene: {sceneName}");
            loadingCoords.Remove(coord);
            yield break;
        }

        yield return op;

        loadingCoords.Remove(coord);

        Scene loaded = SceneManager.GetSceneByName(sceneName);
        if (loaded.IsValid() && loaded.isLoaded)
            loadedCoords.Add(coord);
        else
            Debug.LogWarning($"Scene did not finish loading correctly: {sceneName}");

        TryMarkInitialLoadComplete();
    }

    private IEnumerator UnloadTileScene(Vector2Int coord)
    {
        if (!coordToSceneName.TryGetValue(coord, out string sceneName))
            yield break;

        Scene scene = SceneManager.GetSceneByName(sceneName);
        if (!scene.IsValid() || !scene.isLoaded)
        {
            loadedCoords.Remove(coord);
            yield break;
        }

        unloadingCoords.Add(coord);

        AsyncOperation op = SceneManager.UnloadSceneAsync(sceneName);
        if (op == null)
        {
            Debug.LogError($"Failed to start unloading scene: {sceneName}");
            unloadingCoords.Remove(coord);
            yield break;
        }

        yield return op;

        unloadingCoords.Remove(coord);
        loadedCoords.Remove(coord);

        TryMarkInitialLoadComplete();
    }

    private void TryMarkInitialLoadComplete()
    {
        if (initialLoadCompleted)
            return;

        if (coordToSceneName.Count == 0)
            return;

        if (loadingCoords.Count > 0 || unloadingCoords.Count > 0)
            return;

        if (!hasLastCenterCoord)
            return;

        initialLoadCompleted = true;
        IsWorldReady = true;
        WorldLoadingState.MarkWorldReady();
    }

    private static bool TryParseCoord(string sceneName, string prefix, out Vector2Int coord)
    {
        coord = default;

        string expectedPrefix = prefix + "_";
        if (!sceneName.StartsWith(expectedPrefix, StringComparison.Ordinal))
            return false;

        string[] parts = sceneName.Substring(expectedPrefix.Length).Split('_');
        if (parts.Length != 2)
            return false;

        if (!int.TryParse(parts[0], out int x))
            return false;

        if (!int.TryParse(parts[1], out int z))
            return false;

        coord = new Vector2Int(x, z);
        return true;
    }
}
