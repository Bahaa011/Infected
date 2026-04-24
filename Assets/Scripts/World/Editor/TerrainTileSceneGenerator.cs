using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public class TerrainTileSceneGenerator : EditorWindow
{
    private const string DefaultOutputFolder = "Assets/Scenes/TerrainTiles";

    [SerializeField] private string outputFolder = DefaultOutputFolder;
    [SerializeField] private string scenePrefix = "Tile";
    [SerializeField] private bool overwriteExisting = true;
    [SerializeField] private bool addToBuildSettings = true;
    [SerializeField] private bool useOnlySelectedTerrains = false;

    [MenuItem("Tools/Terrain/Generate Tile Scenes")]
    private static void OpenWindow()
    {
        var window = GetWindow<TerrainTileSceneGenerator>("Terrain Tile Scenes");
        window.minSize = new Vector2(420f, 220f);
        window.Show();
    }

    private void OnGUI()
    {
        EditorGUILayout.LabelField("Generate one scene per terrain tile", EditorStyles.boldLabel);
        EditorGUILayout.Space();

        outputFolder = EditorGUILayout.TextField("Output Folder", outputFolder);
        scenePrefix = EditorGUILayout.TextField("Scene Prefix", scenePrefix);

        overwriteExisting = EditorGUILayout.ToggleLeft("Overwrite existing tile scenes", overwriteExisting);
        addToBuildSettings = EditorGUILayout.ToggleLeft("Add generated scenes to Build Settings", addToBuildSettings);
        useOnlySelectedTerrains = EditorGUILayout.ToggleLeft("Use selected terrains only", useOnlySelectedTerrains);

        EditorGUILayout.Space();

        using (new EditorGUI.DisabledScope(string.IsNullOrWhiteSpace(scenePrefix)))
        {
            if (GUILayout.Button("Generate Scenes", GUILayout.Height(32f)))
            {
                GenerateScenes();
            }
        }

        EditorGUILayout.HelpBox(
            "Open your source scene that contains all terrain tiles, then click Generate Scenes. " +
            "Each tile is cloned into a separate scene named like Tile_x_z.",
            MessageType.Info);
    }

    private void GenerateScenes()
    {
        if (!EnsureOutputFolder(outputFolder))
        {
            Debug.LogError("Invalid output folder. It must be under Assets/.");
            return;
        }

        Scene sourceScene = SceneManager.GetActiveScene();
        if (!sourceScene.IsValid() || !sourceScene.isLoaded)
        {
            Debug.LogError("No active loaded scene found.");
            return;
        }

        var terrains = GetTerrains(sourceScene, useOnlySelectedTerrains);
        if (terrains.Count == 0)
        {
            Debug.LogWarning("No terrains found. Select terrain objects or disable 'Use selected terrains only'.");
            return;
        }

        float tileSizeX = terrains[0].terrainData.size.x;
        float tileSizeZ = terrains[0].terrainData.size.z;
        float minX = terrains.Min(t => t.transform.position.x);
        float minZ = terrains.Min(t => t.transform.position.z);

        List<string> generatedPaths = new List<string>();

        try
        {
            AssetDatabase.StartAssetEditing();

            for (int i = 0; i < terrains.Count; i++)
            {
                Terrain terrain = terrains[i];
                Vector3 p = terrain.transform.position;

                int x = Mathf.RoundToInt((p.x - minX) / tileSizeX);
                int z = Mathf.RoundToInt((p.z - minZ) / tileSizeZ);

                string sceneName = $"{scenePrefix}_{x}_{z}";
                string scenePath = $"{outputFolder}/{sceneName}.unity";

                if (File.Exists(scenePath) && !overwriteExisting)
                {
                    Debug.Log($"Skipped existing scene: {scenePath}");
                    continue;
                }

                var tileScene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);

                GameObject clone = Instantiate(terrain.gameObject);
                clone.name = terrain.gameObject.name;
                SceneManager.MoveGameObjectToScene(clone, tileScene);

                bool saveOk = EditorSceneManager.SaveScene(tileScene, scenePath);
                EditorSceneManager.CloseScene(tileScene, true);

                if (saveOk)
                {
                    generatedPaths.Add(scenePath);
                    Debug.Log($"Generated: {scenePath}");
                }
                else
                {
                    Debug.LogError($"Failed to save: {scenePath}");
                }
            }
        }
        finally
        {
            AssetDatabase.StopAssetEditing();
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
        }

        if (addToBuildSettings && generatedPaths.Count > 0)
        {
            AddScenesToBuildSettings(generatedPaths);
        }

        Debug.Log($"Terrain tile scene generation complete. Created/updated {generatedPaths.Count} scene(s).");
    }

    private static List<Terrain> GetTerrains(Scene scene, bool onlySelected)
    {
        if (onlySelected)
        {
            return Selection.gameObjects
                .Where(go => go.scene == scene)
                .SelectMany(go => go.GetComponentsInChildren<Terrain>(true))
                .Distinct()
                .OrderBy(t => t.transform.position.x)
                .ThenBy(t => t.transform.position.z)
                .ToList();
        }

        return scene.GetRootGameObjects()
            .SelectMany(go => go.GetComponentsInChildren<Terrain>(true))
            .Distinct()
            .OrderBy(t => t.transform.position.x)
            .ThenBy(t => t.transform.position.z)
            .ToList();
    }

    private static bool EnsureOutputFolder(string folder)
    {
        if (string.IsNullOrWhiteSpace(folder) || !folder.StartsWith("Assets", StringComparison.Ordinal))
        {
            return false;
        }

        string[] parts = folder.Split('/');
        string current = parts[0];

        for (int i = 1; i < parts.Length; i++)
        {
            string next = parts[i];
            string combined = $"{current}/{next}";
            if (!AssetDatabase.IsValidFolder(combined))
            {
                AssetDatabase.CreateFolder(current, next);
            }
            current = combined;
        }

        return true;
    }

    private static void AddScenesToBuildSettings(List<string> newScenePaths)
    {
        var existing = EditorBuildSettings.scenes.ToList();
        HashSet<string> existingPaths = new HashSet<string>(existing.Select(s => s.path));

        foreach (string path in newScenePaths)
        {
            if (existingPaths.Add(path))
            {
                existing.Add(new EditorBuildSettingsScene(path, true));
            }
        }

        EditorBuildSettings.scenes = existing.ToArray();
        Debug.Log("Added generated scenes to Build Settings.");
    }
}
