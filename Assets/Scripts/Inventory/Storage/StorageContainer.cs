using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.InputSystem;

[RequireComponent(typeof(Inventory))]
[RequireComponent(typeof(Collider))]
public class StorageContainer : MonoBehaviour, IInteractionPromptSource
{
    [Serializable]
    public struct LootStackRuntimeData
    {
        public Item item;
        public int quantity;
    }

    [Serializable]
    public class PersistentLootRuntimeState
    {
        public string containerKey;
        public float nextRespawnGameDay;
        public List<LootStackRuntimeData> lootStacks = new List<LootStackRuntimeData>();
    }

    private static StorageContainer activeContainer;
    private static readonly Dictionary<string, List<SavedLootStack>> SavedLootByContainer = new Dictionary<string, List<SavedLootStack>>();
    private static readonly Dictionary<string, float> NextRespawnGameDayByContainer = new Dictionary<string, float>();

    [Serializable]
    private struct StartingStack
    {
        public Item item;
        public int quantity;
    }

    [Serializable]
    private struct SavedLootStack
    {
        public Item item;
        public int quantity;
    }

    [Header("Storage")]
    [SerializeField] private string storageDisplayName = "Loot Box";
    [SerializeField] private string containerIdOverride = string.Empty;
    [SerializeField] private float interactionDistance = 2.5f;

    [Header("Auto Loot Generation")]
    [SerializeField] private bool autoGenerateLoot = true;
    [SerializeField] private LootRarityDropSettings lootDropSettings = new LootRarityDropSettings();

    [Header("Loot Respawn")]
    [SerializeField] private float respawnIntervalInGameDays = 1f; // 1 day = 24 in-game hours
    [SerializeField] private float fallbackDayLengthInSeconds = 1200f;

    [Header("Fallback Start Items")]
    [SerializeField] private bool fillStartingItemsOnStart = false;
    [SerializeField] private StartingStack[] startingItems;

    private Inventory storageInventory;
    private StorageWindowUIToolkit storageWindow;
    private Player playerInRange;
    private DayNightManager dayNightManager;
    private string containerKey;
    private bool suppressSnapshotUpdates;

    private void Awake()
    {
        if (lootDropSettings == null)
            lootDropSettings = new LootRarityDropSettings();

        storageInventory = GetComponent<Inventory>();
        storageWindow = FindAnyObjectByType<StorageWindowUIToolkit>();
        dayNightManager = FindAnyObjectByType<DayNightManager>();
        containerKey = BuildContainerKey();

        if (storageInventory != null)
            storageInventory.OnInventoryChanged += OnStorageInventoryChanged;
    }

    private void Start()
    {
        ResolveDayNightManager();

        if (autoGenerateLoot)
        {
            InitializeOrRestoreAutoLoot();
            return;
        }

        if (!fillStartingItemsOnStart || startingItems == null)
            return;

        for (int i = 0; i < startingItems.Length; i++)
        {
            Item item = startingItems[i].item;
            int qty = Mathf.Max(1, startingItems[i].quantity);
            if (item != null)
                storageInventory.AddItem(item, qty);
        }
    }

    private void OnDisable()
    {
        SaveCurrentLootSnapshot();

        if (activeContainer == this)
            activeContainer = null;
    }

    private void OnDestroy()
    {
        if (storageInventory != null)
            storageInventory.OnInventoryChanged -= OnStorageInventoryChanged;

        SaveCurrentLootSnapshot();

        if (activeContainer == this)
            activeContainer = null;
    }

    private void Update()
    {
        if (activeContainer == this && storageWindow != null && !storageWindow.IsOpenFor(storageInventory))
            activeContainer = null;

        if (autoGenerateLoot)
            TryRespawnLootIfDue();

        if (playerInRange != null && !IsViewerWithinInteractionDistance(playerInRange.transform))
        {
            playerInRange = null;

            if (storageWindow != null && storageWindow.IsOpenFor(storageInventory))
            {
                storageWindow.CloseStorage();
                if (activeContainer == this)
                    activeContainer = null;
            }

            return;
        }

        if (playerInRange == null)
            return;

        if (Keyboard.current == null || !Keyboard.current.eKey.wasPressedThisFrame)
            return;

        if (storageWindow == null)
            storageWindow = FindAnyObjectByType<StorageWindowUIToolkit>();

        if (storageWindow == null)
            return;

        bool isThisContainerOpen = storageWindow.IsOpenFor(storageInventory);

        if (!isThisContainerOpen && !IsPlayerPointingAtThisContainer(playerInRange))
            return;

        // If another loot box is currently active, ignore this key press entirely.
        if (activeContainer != null && activeContainer != this && !isThisContainerOpen)
            return;

        if (isThisContainerOpen)
        {
            storageWindow.CloseStorage();
            if (activeContainer == this)
                activeContainer = null;
        }
        else
        {
            storageWindow.OpenStorage(storageInventory, storageDisplayName);
            activeContainer = this;
        }
    }

    private void InitializeOrRestoreAutoLoot()
    {
        if (storageInventory == null)
            return;

        float nowGameDays = GetCurrentGameDays();

        if (NextRespawnGameDayByContainer.TryGetValue(containerKey, out float nextRespawnDay)
            && nowGameDays < nextRespawnDay
            && SavedLootByContainer.TryGetValue(containerKey, out List<SavedLootStack> savedLoot))
        {
            RestoreLootSnapshot(savedLoot);
            return;
        }

        GenerateFreshLoot();
    }

    private void TryRespawnLootIfDue()
    {
        if (!NextRespawnGameDayByContainer.TryGetValue(containerKey, out float nextRespawnDay))
            return;

        if (GetCurrentGameDays() < nextRespawnDay)
            return;

        // Do not respawn while this container is actively open.
        if (storageWindow != null && storageWindow.IsOpenFor(storageInventory))
            return;

        GenerateFreshLoot();
    }

    private void GenerateFreshLoot()
    {
        if (storageInventory == null)
            return;

        suppressSnapshotUpdates = true;
        storageInventory.ClearInventory();

        int generatedCount = LootRarityDropGenerator.GenerateLoot(storageInventory, lootDropSettings);
        if (generatedCount <= 0)
        {
            if (fillStartingItemsOnStart)
                FillFromStartingItems();

            suppressSnapshotUpdates = false;
            SaveCurrentLootSnapshot();
            SetNextRespawnDay();
            return;
        }

        suppressSnapshotUpdates = false;
        SaveCurrentLootSnapshot();
        SetNextRespawnDay();
    }

    private void FillFromStartingItems()
    {
        if (startingItems == null || storageInventory == null)
            return;

        for (int i = 0; i < startingItems.Length; i++)
        {
            Item item = startingItems[i].item;
            if (item == null)
                continue;

            int qty = Mathf.Max(1, startingItems[i].quantity);
            storageInventory.AddItem(item, qty);
        }
    }

    private void OnStorageInventoryChanged()
    {
        if (suppressSnapshotUpdates)
            return;

        SaveCurrentLootSnapshot();
    }

    private void SaveCurrentLootSnapshot()
    {
        if (storageInventory == null || string.IsNullOrWhiteSpace(containerKey))
            return;

        List<InventorySlot> slots = storageInventory.GetAllItems();
        List<SavedLootStack> snapshot = new List<SavedLootStack>();

        for (int i = 0; i < slots.Count; i++)
        {
            InventorySlot slot = slots[i];
            if (slot == null || !slot.isOccupied || !slot.isAnchor || slot.item == null)
                continue;

            snapshot.Add(new SavedLootStack
            {
                item = slot.item,
                quantity = Mathf.Max(1, slot.quantity)
            });
        }

        SavedLootByContainer[containerKey] = snapshot;
    }

    private void RestoreLootSnapshot(List<SavedLootStack> snapshot)
    {
        if (storageInventory == null)
            return;

        suppressSnapshotUpdates = true;
        storageInventory.ClearInventory();

        if (snapshot != null)
        {
            for (int i = 0; i < snapshot.Count; i++)
            {
                if (snapshot[i].item == null)
                    continue;

                storageInventory.AddItem(snapshot[i].item, Mathf.Max(1, snapshot[i].quantity));
            }
        }

        suppressSnapshotUpdates = false;
        SaveCurrentLootSnapshot();
    }

    private void SetNextRespawnDay()
    {
        float interval = Mathf.Max(0.01f, respawnIntervalInGameDays);
        NextRespawnGameDayByContainer[containerKey] = GetCurrentGameDays() + interval;
    }

    private float GetCurrentGameDays()
    {
        ResolveDayNightManager();
        if (dayNightManager != null)
            return dayNightManager.GetTotalGameDays();

        float fallbackLength = Mathf.Max(1f, fallbackDayLengthInSeconds);
        return Time.time / fallbackLength;
    }

    private void ResolveDayNightManager()
    {
        if (dayNightManager == null)
            dayNightManager = FindAnyObjectByType<DayNightManager>();
    }

    public string GetContainerKeyForSave()
    {
        return containerKey;
    }

    public static List<PersistentLootRuntimeState> ExportPersistentLootRuntimeStates()
    {
        StorageContainer[] loadedContainers = FindObjectsByType<StorageContainer>(FindObjectsSortMode.None);
        for (int i = 0; i < loadedContainers.Length; i++)
        {
            StorageContainer container = loadedContainers[i];
            if (container != null)
                container.SaveCurrentLootSnapshot();
        }

        HashSet<string> keys = new HashSet<string>(SavedLootByContainer.Keys);
        foreach (string key in NextRespawnGameDayByContainer.Keys)
            keys.Add(key);

        List<PersistentLootRuntimeState> states = new List<PersistentLootRuntimeState>();
        foreach (string key in keys)
        {
            PersistentLootRuntimeState state = new PersistentLootRuntimeState();
            state.containerKey = key;

            if (!NextRespawnGameDayByContainer.TryGetValue(key, out state.nextRespawnGameDay))
                state.nextRespawnGameDay = 0f;

            if (SavedLootByContainer.TryGetValue(key, out List<SavedLootStack> loot))
            {
                for (int i = 0; i < loot.Count; i++)
                {
                    state.lootStacks.Add(new LootStackRuntimeData
                    {
                        item = loot[i].item,
                        quantity = Mathf.Max(1, loot[i].quantity)
                    });
                }
            }

            states.Add(state);
        }

        return states;
    }

    public static void ImportPersistentLootRuntimeStates(List<PersistentLootRuntimeState> states)
    {
        SavedLootByContainer.Clear();
        NextRespawnGameDayByContainer.Clear();

        if (states == null)
            return;

        for (int i = 0; i < states.Count; i++)
        {
            PersistentLootRuntimeState state = states[i];
            if (state == null || string.IsNullOrWhiteSpace(state.containerKey))
                continue;

            List<SavedLootStack> stacks = new List<SavedLootStack>();
            if (state.lootStacks != null)
            {
                for (int s = 0; s < state.lootStacks.Count; s++)
                {
                    LootStackRuntimeData stack = state.lootStacks[s];
                    if (stack.item == null)
                        continue;

                    stacks.Add(new SavedLootStack
                    {
                        item = stack.item,
                        quantity = Mathf.Max(1, stack.quantity)
                    });
                }
            }

            SavedLootByContainer[state.containerKey] = stacks;
            NextRespawnGameDayByContainer[state.containerKey] = Mathf.Max(0f, state.nextRespawnGameDay);
        }
    }

    public static void RefreshLoadedContainersFromPersistentState()
    {
        StorageContainer[] loadedContainers = FindObjectsByType<StorageContainer>(FindObjectsSortMode.None);
        for (int i = 0; i < loadedContainers.Length; i++)
        {
            StorageContainer container = loadedContainers[i];
            if (container == null)
                continue;

            if (SavedLootByContainer.TryGetValue(container.containerKey, out List<SavedLootStack> snapshot))
                container.RestoreLootSnapshot(snapshot);
        }
    }

    private string BuildContainerKey()
    {
        if (!string.IsNullOrWhiteSpace(containerIdOverride))
            return containerIdOverride.Trim();

        string scenePath = gameObject.scene.path;
        if (string.IsNullOrWhiteSpace(scenePath))
            scenePath = gameObject.scene.name;

        StringBuilder sb = new StringBuilder();
        BuildTransformPath(transform, sb);
        return scenePath + ":" + sb;
    }

    private static void BuildTransformPath(Transform target, StringBuilder sb)
    {
        if (target == null)
            return;

        if (target.parent != null)
        {
            BuildTransformPath(target.parent, sb);
            sb.Append('/');
        }

        sb.Append(target.name);
    }

    private void OnTriggerEnter(Collider other)
    {
        Player player = other.GetComponent<Player>();
        if (player != null && IsViewerWithinInteractionDistance(player.transform))
            playerInRange = player;
    }

    private void OnTriggerExit(Collider other)
    {
        Player player = other.GetComponent<Player>();
        if (player == null || player != playerInRange)
            return;

        playerInRange = null;

        if (storageWindow != null && storageWindow.IsOpenFor(storageInventory))
        {
            storageWindow.CloseStorage();
            if (activeContainer == this)
                activeContainer = null;
        }
    }

    public bool TryGetInteractionPrompt(Transform viewer, out string prompt)
    {
        prompt = string.Empty;

        if (viewer == null || playerInRange == null)
            return false;

        if (!IsViewerWithinInteractionDistance(viewer))
            return false;

        if (activeContainer != null && activeContainer != this)
            return false;

        string displayName = string.IsNullOrWhiteSpace(storageDisplayName) ? "Loot Box" : storageDisplayName;
        bool isOpen = storageWindow != null && storageWindow.IsOpenFor(storageInventory);
        prompt = isOpen
            ? $"Press E to close {displayName}"
            : $"Press E to open {displayName}";
        return true;
    }

    private bool IsViewerWithinInteractionDistance(Transform viewer)
    {
        if (viewer == null)
            return false;

        return Vector3.Distance(transform.position, viewer.position) <= Mathf.Max(0.25f, interactionDistance);
    }

    private bool IsPlayerPointingAtThisContainer(Player player)
    {
        if (player == null)
            return false;

        ThirdPersonController controller = player.GetComponent<ThirdPersonController>();
        Camera cam = controller != null && controller.MainUnityCamera != null
            ? controller.MainUnityCamera
            : Camera.main;

        if (cam == null)
            return false;

        Ray ray = new Ray(cam.transform.position, cam.transform.forward);
        float maxDistance = Mathf.Max(0.25f, interactionDistance);
        if (!Physics.Raycast(ray, out RaycastHit hit, maxDistance, ~0, QueryTriggerInteraction.Collide))
            return false;

        if (hit.collider == null)
            return false;

        if (hit.collider.transform.IsChildOf(player.transform))
            return false;

        return hit.collider.GetComponentInParent<StorageContainer>() == this;
    }
}
