using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.InputSystem;

[DisallowMultipleComponent]
public class GameSaveManager : MonoBehaviour
{
    private const string PrefSelectedSlotIndex = "save.selectedSlotIndex";
    private const int FixedSaveSlotCount = 3;

    [Serializable]
    public class SaveSlotInfo
    {
        public int slotIndex;
        public bool exists;
        public bool corrupted;
        public string filePath;
        public string slotName;
        public string savedAtUtc;
        public float totalGameDays;
    }

    [Header("References")]
    [SerializeField] private Player player;
    [SerializeField] private Inventory playerInventory;
    [SerializeField] private PlayerSkills playerSkills;
    [SerializeField] private InjurySystem injurySystem;
    [SerializeField] private DayNightManager dayNightManager;

    [Header("Save Slots")]
    [SerializeField] private int saveSlotCount = 3;
    [SerializeField] private int activeSlotIndex = 1;
    [SerializeField] private string saveFilePrefix = "save_slot_";
    [SerializeField] private bool loadSelectedSlotOnStart = true;

    private bool hasAutoLoadedSelectedSlot;

    public int ActiveSlotIndex => activeSlotIndex;
    public int SaveSlotCount => FixedSaveSlotCount;

    private void OnValidate()
    {
        saveSlotCount = FixedSaveSlotCount;
        activeSlotIndex = Mathf.Clamp(activeSlotIndex, 1, FixedSaveSlotCount);
    }

    private string SaveDirectoryPath => Application.persistentDataPath;

    private void Awake()
    {
        ResolveReferences(true);
        if (PlayerPrefs.HasKey(PrefSelectedSlotIndex))
            activeSlotIndex = PlayerPrefs.GetInt(PrefSelectedSlotIndex, activeSlotIndex);

        activeSlotIndex = ClampSlotIndex(activeSlotIndex);
        PlayerPrefs.SetInt(PrefSelectedSlotIndex, activeSlotIndex);
        PlayerPrefs.Save();
    }

    private void Update()
    {
        if (Keyboard.current == null)
            return;

        if (Keyboard.current.f9Key.wasPressedThisFrame)
            LoadGame();
    }

    private void Start()
    {
        if (loadSelectedSlotOnStart)
            StartCoroutine(LoadSelectedSlotOnStartRoutine());
    }

    [ContextMenu("Save Game")]
    public void SaveGame()
    {
        TrySaveGame();
    }

    public bool TrySaveGame()
    {
        return SaveGameToSlot(activeSlotIndex);
    }

    [ContextMenu("Load Game")]
    public void LoadGame()
    {
        LoadGameFromSlot(activeSlotIndex);
    }

    public bool SaveGameToSlot(int slotIndex)
    {
        ResolveReferences(true);
        slotIndex = ClampSlotIndex(slotIndex);
        activeSlotIndex = slotIndex;
        PlayerPrefs.SetInt(PrefSelectedSlotIndex, activeSlotIndex);
        PlayerPrefs.Save();

        GameSaveData data = BuildSaveData(slotIndex);
        if (data == null)
        {
            Debug.LogWarning("[GameSaveManager] Save aborted. Missing required references.");
            return false;
        }

        string json = JsonUtility.ToJson(data, true);

        try
        {
            EnsureSaveDirectoryExists();
            string path = GetSaveFilePath(slotIndex);
            File.WriteAllText(path, json);
            Debug.Log($"[GameSaveManager] Saved slot {slotIndex} to: {path}");
            return true;
        }
        catch (Exception ex)
        {
            Debug.LogError($"[GameSaveManager] Save failed for slot {slotIndex}: {ex.Message}");
            return false;
        }
    }

    public void LoadGameFromSlot(int slotIndex)
    {
        ResolveReferences(true);
        slotIndex = ClampSlotIndex(slotIndex);
        activeSlotIndex = slotIndex;
        PlayerPrefs.SetInt(PrefSelectedSlotIndex, activeSlotIndex);
        PlayerPrefs.Save();

        string path = GetSaveFilePath(slotIndex);
        if (!File.Exists(path))
        {
            StorageContainer.ClearPersistentLootRuntimeState();
            Debug.LogWarning($"[GameSaveManager] No save file found for slot {slotIndex} at: {path}");
            return;
        }

        try
        {
            string json = File.ReadAllText(path);
            GameSaveData data = JsonUtility.FromJson<GameSaveData>(json);
            if (data == null)
            {
                Debug.LogError($"[GameSaveManager] Save file for slot {slotIndex} could not be parsed.");
                return;
            }

            ApplySaveData(data);
            RefreshTerrainStreamingAroundLoadedPlayer();
            Debug.Log($"[GameSaveManager] Loaded slot {slotIndex} from: {path}");
        }
        catch (Exception ex)
        {
            Debug.LogError($"[GameSaveManager] Load failed for slot {slotIndex}: {ex.Message}");
        }
    }

    private IEnumerator LoadSelectedSlotOnStartRoutine()
    {
        if (hasAutoLoadedSelectedSlot)
            yield break;

        hasAutoLoadedSelectedSlot = true;

        // Let scene Awake/Start finish so references like DayNightManager and Player are initialized first.
        yield return null;

        ResolveReferences(true);

        string path = GetSaveFilePath(activeSlotIndex);
        if (!File.Exists(path))
        {
            StorageContainer.ClearPersistentLootRuntimeState();
            Debug.Log($"[GameSaveManager] No save found for selected slot {activeSlotIndex}. Starting fresh at: {path}");
            yield break;
        }

        LoadGameFromSlot(activeSlotIndex);
    }

    public void DeleteSlot(int slotIndex)
    {
        slotIndex = ClampSlotIndex(slotIndex);
        string path = GetSaveFilePath(slotIndex);

        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
                Debug.Log($"[GameSaveManager] Deleted save slot {slotIndex}");
            }
        }
        catch (Exception ex)
        {
            Debug.LogError($"[GameSaveManager] Failed to delete slot {slotIndex}: {ex.Message}");
        }
    }

    public void SetActiveSlot(int slotIndex)
    {
        activeSlotIndex = ClampSlotIndex(slotIndex);
        PlayerPrefs.SetInt(PrefSelectedSlotIndex, activeSlotIndex);
        PlayerPrefs.Save();
    }

    public GameSaveManager.SaveSlotInfo[] GetSaveSlotInfos()
    {
        int count = SaveSlotCount;
        GameSaveManager.SaveSlotInfo[] infos = new GameSaveManager.SaveSlotInfo[count];

        for (int i = 1; i <= count; i++)
            infos[i - 1] = ReadSlotInfo(i);

        return infos;
    }

    public GameSaveManager.SaveSlotInfo GetSlotInfo(int slotIndex)
    {
        return ReadSlotInfo(ClampSlotIndex(slotIndex));
    }

    private GameSaveData BuildSaveData(int slotIndex)
    {
        if (player == null)
        {
            Debug.LogWarning("[GameSaveManager] Cannot build save data because Player was not found.");
            return null;
        }

        if (playerInventory == null)
            Debug.LogWarning("[GameSaveManager] Player inventory was not found. Saving player data with an empty inventory.");

        GameSaveData data = new GameSaveData();
        data.slotIndex = slotIndex;
        data.slotName = $"Slot {slotIndex}";
        data.savedAtUtc = DateTime.UtcNow.ToString("O");
        data.totalGameDays = dayNightManager != null ? dayNightManager.GetTotalGameDays() : 0f;

        data.player.position = new SerializableVector3(player.transform.position);
        data.player.rotation = new SerializableQuaternion(player.transform.rotation);
        Debug.Log($"[GameSaveManager] Capturing player transform for slot {slotIndex}: position={player.transform.position}, rotation={player.transform.rotation.eulerAngles}");
        data.player.health = player.GetHealth();
        data.player.hunger = player.GetHunger();
        data.player.thirst = player.GetThirst();
        data.player.stamina = player.GetStamina();
        data.player.isAlive = player.IsAlive();

        data.player.inventoryItems = CaptureInventoryStacks(playerInventory);

        if (playerSkills != null)
            data.player.skills = playerSkills.CaptureSaveData();

        if (injurySystem != null)
            data.player.injuries = injurySystem.CaptureSaveData();

        List<StorageContainer.PersistentLootRuntimeState> runtimeLoot = StorageContainer.ExportPersistentLootRuntimeStates();
        for (int i = 0; i < runtimeLoot.Count; i++)
        {
            StorageContainer.PersistentLootRuntimeState state = runtimeLoot[i];
            if (state == null || string.IsNullOrWhiteSpace(state.containerKey))
                continue;

            LootContainerSaveData saveContainer = new LootContainerSaveData
            {
                containerKey = state.containerKey,
                nextRespawnGameDay = state.nextRespawnGameDay
            };

            if (state.lootStacks != null)
            {
                for (int s = 0; s < state.lootStacks.Count; s++)
                {
                    var stack = state.lootStacks[s];
                    if (stack.item == null)
                        continue;

                    saveContainer.items.Add(new ItemStackSaveData
                    {
                        itemId = stack.item.ID,
                        itemName = stack.item.name,
                        quantity = Mathf.Max(1, stack.quantity),
                        currentAmmo = GetSavedCurrentAmmo(stack.item)
                    });
                }
            }

            data.lootContainers.Add(saveContainer);
        }

        return data;
    }

    private void ApplySaveData(GameSaveData data)
    {
        if (data == null)
            return;

        ResolveReferences(true);

        if (dayNightManager != null)
            dayNightManager.SetTotalGameDays(Mathf.Max(0f, data.totalGameDays));

        if (playerSkills != null && data.player.skills != null)
            playerSkills.ApplySaveData(data.player.skills);

        if (player != null)
        {
            ApplyPlayerTransform(
                player,
                data.player.position.ToVector3(),
                data.player.rotation.ToQuaternion());
            Debug.Log($"[GameSaveManager] Applied saved player transform: position={player.transform.position}, rotation={player.transform.rotation.eulerAngles}");

            player.ApplySavedVitals(
                data.player.health,
                data.player.hunger,
                data.player.thirst,
                data.player.stamina,
                data.player.isAlive);
        }

        Dictionary<int, Item> itemById = BuildItemLookupById();
        Dictionary<string, Item> itemByName = BuildItemLookupByName();

        if (playerInventory != null)
        {
            playerInventory.ClearInventory();
            RestoreInventoryStacks(playerInventory, data.player.inventoryItems, itemById, itemByName);
        }

        if (injurySystem != null)
            injurySystem.ApplySaveData(data.player.injuries);

        List<StorageContainer.PersistentLootRuntimeState> runtimeStates = new List<StorageContainer.PersistentLootRuntimeState>();
        if (data.lootContainers != null)
        {
            for (int i = 0; i < data.lootContainers.Count; i++)
            {
                LootContainerSaveData saveContainer = data.lootContainers[i];
                if (saveContainer == null || string.IsNullOrWhiteSpace(saveContainer.containerKey))
                    continue;

                StorageContainer.PersistentLootRuntimeState runtime = new StorageContainer.PersistentLootRuntimeState
                {
                    containerKey = saveContainer.containerKey,
                    nextRespawnGameDay = Mathf.Max(0f, saveContainer.nextRespawnGameDay)
                };

                if (saveContainer.items != null)
                {
                    for (int s = 0; s < saveContainer.items.Count; s++)
                    {
                        ItemStackSaveData stack = saveContainer.items[s];
                        Item item = ResolveItem(stack, itemById, itemByName);
                        if (item == null)
                        {
                            Debug.LogWarning($"[GameSaveManager] Could not restore loot item '{stack?.itemName}' with ID {stack?.itemId} in container '{saveContainer.containerKey}'. Make sure the Item asset is included in the build or referenced by a loaded scene/resource.");
                            continue;
                        }

                        ApplySavedItemState(item, stack);
                        runtime.lootStacks.Add(new StorageContainer.LootStackRuntimeData
                        {
                            item = item,
                            quantity = Mathf.Max(1, stack.quantity)
                        });
                    }
                }

                runtimeStates.Add(runtime);
            }
        }

        StorageContainer.ImportPersistentLootRuntimeStates(runtimeStates);
        StorageContainer.RefreshLoadedContainersFromPersistentState();
    }

    private void ResolveReferences(bool forceRefresh = false)
    {
        if (forceRefresh || player == null)
            player = FindRuntimePlayer();

        if ((forceRefresh || playerInventory == null) && player != null)
            playerInventory = player.GetComponent<Inventory>();

        if ((forceRefresh || playerSkills == null) && player != null)
            playerSkills = player.GetComponent<PlayerSkills>();

        if ((forceRefresh || injurySystem == null) && player != null)
            injurySystem = player.GetComponent<InjurySystem>();

        if (forceRefresh || dayNightManager == null)
            dayNightManager = FindRuntimeObject<DayNightManager>();
    }

    private static void RefreshTerrainStreamingAroundLoadedPlayer()
    {
        TerrainChunkStreamer streamer = FindRuntimeObject<TerrainChunkStreamer>();
        if (streamer != null)
            streamer.ForceRefresh();
    }

    private static void ApplyPlayerTransform(Player targetPlayer, Vector3 position, Quaternion rotation)
    {
        if (targetPlayer == null)
            return;

        CharacterController characterController = targetPlayer.GetComponent<CharacterController>();
        Rigidbody rigidbody = targetPlayer.GetComponent<Rigidbody>();

        bool controllerWasEnabled = characterController != null && characterController.enabled;
        if (controllerWasEnabled)
            characterController.enabled = false;

        if (rigidbody != null)
        {
            rigidbody.linearVelocity = Vector3.zero;
            rigidbody.angularVelocity = Vector3.zero;
            rigidbody.position = position;
            rigidbody.rotation = rotation;
        }

        targetPlayer.transform.SetPositionAndRotation(position, rotation);
        Physics.SyncTransforms();

        if (controllerWasEnabled)
            characterController.enabled = true;
    }

    private static Player FindRuntimePlayer()
    {
        GameObject taggedPlayer = null;
        try
        {
            taggedPlayer = GameObject.FindGameObjectWithTag("Player");
        }
        catch (UnityException)
        {
            taggedPlayer = null;
        }

        if (taggedPlayer != null)
        {
            Player playerComponent = taggedPlayer.GetComponent<Player>();
            if (playerComponent != null && IsSceneObject(playerComponent))
                return playerComponent;
        }

        Player[] activePlayers = FindObjectsByType<Player>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
        Player activePlayer = FindBestRuntimePlayer(activePlayers);
        if (activePlayer != null)
            return activePlayer;

        Player[] allPlayers = FindObjectsByType<Player>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        return FindBestRuntimePlayer(allPlayers);
    }

    private static Player FindBestRuntimePlayer(Player[] players)
    {
        if (players == null)
            return null;

        for (int i = 0; i < players.Length; i++)
        {
            Player candidate = players[i];
            if (candidate == null || !IsSceneObject(candidate))
                continue;

            if (candidate.gameObject.activeInHierarchy)
                return candidate;
        }

        for (int i = 0; i < players.Length; i++)
        {
            Player candidate = players[i];
            if (candidate != null && IsSceneObject(candidate))
                return candidate;
        }

        return null;
    }

    private static T FindRuntimeObject<T>() where T : UnityEngine.Object
    {
        T[] objects = FindObjectsByType<T>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        for (int i = 0; i < objects.Length; i++)
        {
            T obj = objects[i];
            if (obj == null)
                continue;

            if (obj is Component component && component.gameObject.scene.IsValid())
                return obj;

            if (obj is GameObject gameObject && gameObject.scene.IsValid())
                return obj;
        }

        return null;
    }

    private static bool IsSceneObject(UnityEngine.Object obj)
    {
        if (obj is Component component)
            return component.gameObject.scene.IsValid();

        if (obj is GameObject gameObject)
            return gameObject.scene.IsValid();

        return false;
    }

    private string GetSaveFilePath(int slotIndex)
    {
        return Path.Combine(SaveDirectoryPath, $"{saveFilePrefix}{slotIndex:00}.json");
    }

    private void EnsureSaveDirectoryExists()
    {
        string dir = SaveDirectoryPath;
        if (!Directory.Exists(dir))
            Directory.CreateDirectory(dir);
    }

    private GameSaveManager.SaveSlotInfo ReadSlotInfo(int slotIndex)
    {
        slotIndex = ClampSlotIndex(slotIndex);
        string path = GetSaveFilePath(slotIndex);

        GameSaveManager.SaveSlotInfo info = new GameSaveManager.SaveSlotInfo
        {
            slotIndex = slotIndex,
            exists = File.Exists(path),
            corrupted = false,
            filePath = path,
            slotName = $"Slot {slotIndex}",
            savedAtUtc = string.Empty,
            totalGameDays = 0f
        };

        if (!info.exists)
            return info;

        try
        {
            string json = File.ReadAllText(path);
            GameSaveData data = JsonUtility.FromJson<GameSaveData>(json);
            if (data == null)
            {
                info.corrupted = true;
                return info;
            }

            info.slotName = string.IsNullOrWhiteSpace(data.slotName) ? $"Slot {slotIndex}" : data.slotName;
            info.savedAtUtc = data.savedAtUtc;
            info.totalGameDays = data.totalGameDays;
        }
        catch
        {
            info.corrupted = true;
        }

        return info;
    }

    private int ClampSlotIndex(int slotIndex)
    {
        return Mathf.Clamp(slotIndex, 1, SaveSlotCount);
    }

    private static List<ItemStackSaveData> CaptureInventoryStacks(Inventory inventory)
    {
        List<ItemStackSaveData> list = new List<ItemStackSaveData>();
        if (inventory == null)
            return list;

        List<InventorySlot> slots = inventory.GetAllItems();
        for (int i = 0; i < slots.Count; i++)
        {
            InventorySlot slot = slots[i];
            if (slot == null || !slot.isOccupied || !slot.isAnchor || slot.item == null)
                continue;

            list.Add(new ItemStackSaveData
            {
                itemId = slot.item.ID,
                itemName = slot.item.name,
                quantity = Mathf.Max(1, slot.quantity),
                currentAmmo = GetSavedCurrentAmmo(slot.item)
            });
        }

        return list;
    }

    private static void RestoreInventoryStacks(
        Inventory inventory,
        List<ItemStackSaveData> stacks,
        Dictionary<int, Item> itemById,
        Dictionary<string, Item> itemByName)
    {
        if (inventory == null || stacks == null)
            return;

        for (int i = 0; i < stacks.Count; i++)
        {
            ItemStackSaveData stack = stacks[i];
            Item item = ResolveItem(stack, itemById, itemByName);
            if (item == null)
            {
                Debug.LogWarning($"[GameSaveManager] Could not restore inventory item '{stack?.itemName}' with ID {stack?.itemId}. Make sure the Item asset is included in the build or referenced by a loaded scene/resource.");
                continue;
            }

            ApplySavedItemState(item, stack);
            inventory.AddItem(item, Mathf.Max(1, stack.quantity));
        }
    }

    private static Item ResolveItem(
        ItemStackSaveData stack,
        Dictionary<int, Item> itemById,
        Dictionary<string, Item> itemByName)
    {
        if (stack == null)
            return null;

        if (!string.IsNullOrWhiteSpace(stack.itemName) && itemByName != null && itemByName.TryGetValue(stack.itemName, out Item byName))
            return byName;

        if (itemById != null && itemById.TryGetValue(stack.itemId, out Item byId) && byId != null)
            return byId;

        return null;
    }

    private static int GetSavedCurrentAmmo(Item item)
    {
        return item is GunItem gunItem ? gunItem.CurrentAmmo : -1;
    }

    private static void ApplySavedItemState(Item item, ItemStackSaveData stack)
    {
        if (item is GunItem gunItem && stack != null && stack.currentAmmo >= 0)
            gunItem.SetAmmo(stack.currentAmmo);
    }

    private static Dictionary<int, Item> BuildItemLookupById()
    {
        Dictionary<int, Item> dict = new Dictionary<int, Item>();
        List<Item> items = GetKnownItemAssets();

        for (int i = 0; i < items.Count; i++)
        {
            Item item = items[i];
            if (item == null)
                continue;

            if (!dict.ContainsKey(item.ID))
                dict.Add(item.ID, item);
        }

        return dict;
    }

    private static Dictionary<string, Item> BuildItemLookupByName()
    {
        Dictionary<string, Item> dict = new Dictionary<string, Item>(StringComparer.Ordinal);
        List<Item> items = GetKnownItemAssets();

        for (int i = 0; i < items.Count; i++)
        {
            Item item = items[i];
            if (item == null)
                continue;

            if (!dict.ContainsKey(item.name))
                dict.Add(item.name, item);
        }

        return dict;
    }

    private static List<Item> GetKnownItemAssets()
    {
        HashSet<Item> uniqueItems = new HashSet<Item>();
        AddKnownItems(uniqueItems, Resources.FindObjectsOfTypeAll<Item>());
        AddKnownItems(uniqueItems, Resources.LoadAll<Item>(string.Empty));
        return new List<Item>(uniqueItems);
    }

    private static void AddKnownItems(HashSet<Item> uniqueItems, Item[] items)
    {
        if (uniqueItems == null || items == null)
            return;

        for (int i = 0; i < items.Length; i++)
        {
            Item item = items[i];
            if (item != null)
                uniqueItems.Add(item);
        }
    }
}
