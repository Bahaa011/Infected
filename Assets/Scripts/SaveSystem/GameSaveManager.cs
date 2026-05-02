using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.InputSystem;

[DisallowMultipleComponent]
public class GameSaveManager : MonoBehaviour
{
    private const string PrefSelectedSlotIndex = "save.selectedSlotIndex";

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

    [Header("Save File")]
    [SerializeField] private bool autoLoadOnStart = false;
    [SerializeField] private bool loadActiveSlotOnStart = true;

    [Header("Hotkeys")]
    [SerializeField] private bool enableHotkeys = true;

    public int ActiveSlotIndex => activeSlotIndex;
    public int SaveSlotCount => Mathf.Max(1, saveSlotCount);

    private string SaveDirectoryPath => Application.persistentDataPath;

    private void Awake()
    {
        ResolveReferences();
        if (PlayerPrefs.HasKey(PrefSelectedSlotIndex))
            activeSlotIndex = PlayerPrefs.GetInt(PrefSelectedSlotIndex, activeSlotIndex);

        activeSlotIndex = ClampSlotIndex(activeSlotIndex);
        PlayerPrefs.SetInt(PrefSelectedSlotIndex, activeSlotIndex);
        PlayerPrefs.Save();
    }

    private void Start()
    {
        if (loadActiveSlotOnStart && (autoLoadOnStart || PlayerPrefs.HasKey(PrefSelectedSlotIndex)))
        {
            if (File.Exists(GetSaveFilePath(activeSlotIndex)))
                LoadGame();
        }
    }

    private void Update()
    {
        if (!enableHotkeys || Keyboard.current == null)
            return;

        if (Keyboard.current.f5Key.wasPressedThisFrame)
            SaveGame();

        if (Keyboard.current.f9Key.wasPressedThisFrame)
            LoadGame();
    }

    [ContextMenu("Save Game")]
    public void SaveGame()
    {
        SaveGameToSlot(activeSlotIndex);
    }

    [ContextMenu("Load Game")]
    public void LoadGame()
    {
        LoadGameFromSlot(activeSlotIndex);
    }

    public void SaveGameToSlot(int slotIndex)
    {
        ResolveReferences();
        slotIndex = ClampSlotIndex(slotIndex);
        activeSlotIndex = slotIndex;
        PlayerPrefs.SetInt(PrefSelectedSlotIndex, activeSlotIndex);
        PlayerPrefs.Save();

        GameSaveData data = BuildSaveData(slotIndex);
        if (data == null)
        {
            Debug.LogWarning("[GameSaveManager] Save aborted. Missing required references.");
            return;
        }

        string json = JsonUtility.ToJson(data, true);

        try
        {
            EnsureSaveDirectoryExists();
            string path = GetSaveFilePath(slotIndex);
            File.WriteAllText(path, json);
            Debug.Log($"[GameSaveManager] Saved slot {slotIndex} to: {path}");
        }
        catch (Exception ex)
        {
            Debug.LogError($"[GameSaveManager] Save failed for slot {slotIndex}: {ex.Message}");
        }
    }

    public void LoadGameFromSlot(int slotIndex)
    {
        ResolveReferences();
        slotIndex = ClampSlotIndex(slotIndex);
        activeSlotIndex = slotIndex;
        PlayerPrefs.SetInt(PrefSelectedSlotIndex, activeSlotIndex);
        PlayerPrefs.Save();

        string path = GetSaveFilePath(slotIndex);
        if (!File.Exists(path))
        {
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
            Debug.Log($"[GameSaveManager] Loaded slot {slotIndex} from: {path}");
        }
        catch (Exception ex)
        {
            Debug.LogError($"[GameSaveManager] Load failed for slot {slotIndex}: {ex.Message}");
        }
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

    private void OnApplicationQuit()
    {
        SaveGame();
    }

    private GameSaveData BuildSaveData(int slotIndex)
    {
        if (player == null || playerInventory == null)
            return null;

        GameSaveData data = new GameSaveData();
        data.slotIndex = slotIndex;
        data.slotName = $"Slot {slotIndex}";
        data.savedAtUtc = DateTime.UtcNow.ToString("O");
        data.totalGameDays = dayNightManager != null ? dayNightManager.GetTotalGameDays() : 0f;

        data.player.position = new SerializableVector3(player.transform.position);
        data.player.rotation = new SerializableQuaternion(player.transform.rotation);
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
                        quantity = Mathf.Max(1, stack.quantity)
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

        ResolveReferences();

        if (dayNightManager != null)
            dayNightManager.SetTotalGameDays(Mathf.Max(0f, data.totalGameDays));

        if (player != null)
        {
            player.transform.SetPositionAndRotation(
                data.player.position.ToVector3(),
                data.player.rotation.ToQuaternion());

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

        if (playerSkills != null && data.player.skills != null)
            playerSkills.ApplySaveData(data.player.skills);

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
                            continue;

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

    private void ResolveReferences()
    {
        if (player == null)
            player = FindAnyObjectByType<Player>();

        if (playerInventory == null && player != null)
            playerInventory = player.GetComponent<Inventory>();

        if (playerSkills == null && player != null)
            playerSkills = player.GetComponent<PlayerSkills>();

        if (injurySystem == null && player != null)
            injurySystem = player.GetComponent<InjurySystem>();

        if (dayNightManager == null)
            dayNightManager = FindAnyObjectByType<DayNightManager>();
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
                quantity = Mathf.Max(1, slot.quantity)
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
                continue;

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

        if (itemById != null && itemById.TryGetValue(stack.itemId, out Item byId) && byId != null)
            return byId;

        if (!string.IsNullOrWhiteSpace(stack.itemName) && itemByName != null && itemByName.TryGetValue(stack.itemName, out Item byName))
            return byName;

        return null;
    }

    private static Dictionary<int, Item> BuildItemLookupById()
    {
        Dictionary<int, Item> dict = new Dictionary<int, Item>();
        Item[] items = Resources.FindObjectsOfTypeAll<Item>();

        for (int i = 0; i < items.Length; i++)
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
        Item[] items = Resources.FindObjectsOfTypeAll<Item>();

        for (int i = 0; i < items.Length; i++)
        {
            Item item = items[i];
            if (item == null)
                continue;

            if (!dict.ContainsKey(item.name))
                dict.Add(item.name, item);
        }

        return dict;
    }
}
