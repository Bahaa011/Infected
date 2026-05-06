using UnityEngine;
using System.Collections.Generic;

public class Inventory : MonoBehaviour
{
    [Header("Grid Settings")]
    [SerializeField] private int gridColumns = 8;
    [SerializeField] private int gridRows = 5;

    [Header("Drop Settings")]
    [SerializeField] private float dropForwardDistance = 1.4f;
    [SerializeField] private float dropUpOffset = 0.4f;
    [SerializeField] private float droppedPickupRadius = 2f;

    [Header("Debug - Inspector Add Item")]
    [SerializeField] private Item debugItemToAdd;
    [SerializeField] private int debugItemQuantity = 1;
    [SerializeField] private bool debugAddImmediately;
    [SerializeField] private bool debugClearBeforeAdd;
    
    private List<InventorySlot> slots = new List<InventorySlot>();
    private EquipmentManager equipmentManager;

    public delegate void InventoryChangedEvent();
    public event InventoryChangedEvent OnInventoryChanged;

    private void Awake()
    {
        equipmentManager = GetComponent<EquipmentManager>();
        EnsureSlotCapacity();
    }

    private void OnValidate()
    {
        gridColumns = Mathf.Max(1, gridColumns);
        gridRows = Mathf.Max(1, gridRows);
        debugItemQuantity = Mathf.Max(1, debugItemQuantity);
    }

    private void Update()
    {
        if (!debugAddImmediately)
            return;

        debugAddImmediately = false;

        if (!Application.isPlaying)
            return;

        DebugAddFromInspector();
    }

    [ContextMenu("Debug Add Item Now")]
    private void DebugAddFromInspector()
    {
        if (debugItemToAdd == null)
        {
            Debug.LogWarning("[Inventory] Debug add failed: no item assigned.", this);
            return;
        }

        EnsureSlotCapacity();

        if (debugClearBeforeAdd)
            ClearInventory();

        bool added = AddItem(debugItemToAdd, Mathf.Max(1, debugItemQuantity));
        if (!added)
            Debug.LogWarning($"[Inventory] Debug add failed: inventory full for {debugItemToAdd.ItemName} x{Mathf.Max(1, debugItemQuantity)}.", this);
    }

    public int GetGridColumns() => Mathf.Max(1, gridColumns);

    public int GetGridRows() => Mathf.Max(1, gridRows);

    public int GetSlotCapacity() => GetGridColumns() * GetGridRows();

    public bool AddItem(Item item, int quantity = 1)
    {
        if (item == null || quantity <= 0)
            return false;

        EnsureSlotCapacity();

        bool usesLargeFootprint = UsesLargeFootprint(item);
        int footprintWidth = usesLargeFootprint ? 2 : 1;
        int footprintHeight = usesLargeFootprint ? 2 : 1;

        // Find empty slot for remaining items
        while (quantity > 0)
        {
            Item itemToPlace = CreateRuntimeItemInstance(item);
            int emptySlot = usesLargeFootprint
                ? FindEmptyFootprintSlot(footprintWidth, footprintHeight)
                : FindEmptySlot();

            if (emptySlot == -1)
            {
                Debug.LogWarning("Inventory grid is full. Cannot add more items.");
                OnInventoryChanged?.Invoke();
                return false;
            }

            int toAdd = 1;

            if (usesLargeFootprint)
            {
                PlaceItemWithFootprint(emptySlot, itemToPlace, toAdd, footprintWidth, footprintHeight);
            }
            else
            {
                var slot = new InventorySlot(itemToPlace, toAdd)
                {
                    isOccupied = true,
                    isAnchor = true,
                    anchorIndex = emptySlot,
                    footprintWidth = 1,
                    footprintHeight = 1
                };
                slots[emptySlot] = slot;
            }

            quantity -= toAdd;
        }

        OnInventoryChanged?.Invoke();
        return true;
    }

    public bool RemoveItem(Item item, int quantity = 1)
    {
        if (item == null || quantity <= 0)
            return false;

        EnsureSlotCapacity();

        int remaining = quantity;

        // Remove from back to front (more efficient)
        for (int i = slots.Count - 1; i >= 0 && remaining > 0; i--)
        {
            if (slots[i].item == item && slots[i].isAnchor)
            {
                int removed = RemoveFromAnchorIndex(i, remaining);
                remaining -= removed;
            }
        }

        bool success = remaining == 0;
        if (success)
        {
            OnInventoryChanged?.Invoke();
        }

        return success;
    }

    public bool RemoveItemAtSlot(int slotIndex, int quantity = 1)
    {
        if (quantity <= 0)
            return false;

        EnsureSlotCapacity();

        int anchorIndex = ResolveAnchorIndex(slotIndex);
        if (anchorIndex < 0 || anchorIndex >= slots.Count)
            return false;

        int removed = RemoveFromAnchorIndex(anchorIndex, quantity);
        bool success = removed > 0;
        if (success)
            OnInventoryChanged?.Invoke();

        return success;
    }

    public bool MoveItem(int fromSlotIndex, int toSlotIndex)
    {
        EnsureSlotCapacity();

        int fromAnchorIndex = ResolveAnchorIndex(fromSlotIndex);
        if (fromAnchorIndex < 0 || fromAnchorIndex >= slots.Count)
            return false;

        if (toSlotIndex < 0 || toSlotIndex >= slots.Count)
            return false;

        InventorySlot sourceAnchor = slots[fromAnchorIndex];
        if (sourceAnchor == null || !sourceAnchor.isAnchor || !sourceAnchor.isOccupied || sourceAnchor.item == null)
            return false;

        int width = Mathf.Max(1, sourceAnchor.footprintWidth);
        int height = Mathf.Max(1, sourceAnchor.footprintHeight);
        int quantity = Mathf.Max(1, sourceAnchor.quantity);
        Item item = sourceAnchor.item;

        // Releasing inside current footprint means no movement is needed.
        if (IsIndexWithinAnchorFootprint(fromAnchorIndex, toSlotIndex, width, height))
            return true;

        int columns = GetGridColumns();
        int rows = GetGridRows();
        int toCol = toSlotIndex % columns;
        int toRow = toSlotIndex / columns;
        if (toCol + width > columns || toRow + height > rows)
            return false;

        bool[] occupied = new bool[slots.Count];
        for (int i = 0; i < slots.Count; i++)
            occupied[i] = slots[i] != null && slots[i].isOccupied;

        // Ignore source footprint cells while validating target.
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                int idx = ((fromAnchorIndex / columns) + y) * columns + ((fromAnchorIndex % columns) + x);
                if (idx >= 0 && idx < occupied.Length)
                    occupied[idx] = false;
            }
        }

        if (!CanPlaceAt(toSlotIndex, width, height, occupied))
            return false;

        ClearAnchoredItem(fromAnchorIndex);
        PlaceItemWithFootprint(toSlotIndex, item, quantity, width, height);
        OnInventoryChanged?.Invoke();
        return true;
    }

    public bool AddItemAtSlot(Item item, int quantity, int slotIndex)
    {
        if (item == null || quantity <= 0)
            return false;

        EnsureSlotCapacity();

        if (slotIndex < 0 || slotIndex >= slots.Count)
            return false;

        bool usesLargeFootprint = UsesLargeFootprint(item);
        int width = usesLargeFootprint ? 2 : 1;
        int height = usesLargeFootprint ? 2 : 1;

        if (!CanPlaceAt(slotIndex, width, height))
            return false;

        PlaceItemWithFootprint(slotIndex, CreateRuntimeItemInstance(item), quantity, width, height);
        OnInventoryChanged?.Invoke();
        return true;
    }

    public int ResolveAnchorSlotIndex(int slotIndex)
    {
        EnsureSlotCapacity();
        return ResolveAnchorIndex(slotIndex);
    }

    public int GetItemQuantity(Item item)
    {
        EnsureSlotCapacity();

        int total = 0;
        foreach (InventorySlot slot in slots)
        {
            if (slot.item == item && slot.isAnchor)
                total += slot.quantity;
        }
        return total;
    }

    public List<InventorySlot> GetAllItems()
    {
        EnsureSlotCapacity();
        return new List<InventorySlot>(slots);
    }

    public InventorySlot GetSlot(int index)
    {
        EnsureSlotCapacity();

        if (index >= 0 && index < slots.Count)
            return slots[index];
        return null;
    }

    public bool CanAddItem(Item item, int quantity = 1)
    {
        if (item == null || quantity <= 0)
            return false;

        EnsureSlotCapacity();

        bool usesLargeFootprint = UsesLargeFootprint(item);
        if (usesLargeFootprint)
        {
            int needed = quantity;
            bool[] occupied = new bool[slots.Count];

            for (int i = 0; i < slots.Count; i++)
                occupied[i] = slots[i] != null && slots[i].isOccupied;

            for (int i = 0; i < slots.Count && needed > 0; i++)
            {
                if (!CanPlaceAt(i, 2, 2, occupied))
                    continue;

                MarkFootprint(i, 2, 2, occupied, GetGridColumns(), true);
                needed--;
            }

            return needed == 0;
        }

        int emptySlots = 0;
        for (int i = 0; i < slots.Count; i++)
        {
            if (slots[i] == null || !slots[i].isOccupied)
                emptySlots++;
        }

        // No stacking: every non-large item needs one free slot per quantity.
        return emptySlots >= quantity;
    }

    public void ClearInventory()
    {
        EnsureSlotCapacity();

        for (int i = 0; i < slots.Count; i++)
            slots[i] = InventorySlot.Empty();

        OnInventoryChanged?.Invoke();
    }

    public void PrintInventory()
    {
        Debug.Log("=== INVENTORY ===");
        for (int i = 0; i < slots.Count; i++)
        {
            if (slots[i].item != null && slots[i].isAnchor)
                Debug.Log($"Slot {i}: {slots[i]}");
        }
    }

    private int FindEmptySlot()
    {
        EnsureSlotCapacity();

        for (int i = 0; i < slots.Count; i++)
        {
            if (slots[i] == null || !slots[i].isOccupied)
                return i;
        }
        return -1;
    }

    private int FindEmptyFootprintSlot(int width, int height)
    {
        EnsureSlotCapacity();

        for (int i = 0; i < slots.Count; i++)
        {
            if (CanPlaceAt(i, width, height))
                return i;
        }

        return -1;
    }

    private int ResolveAnchorIndex(int slotIndex)
    {
        if (slotIndex < 0 || slotIndex >= slots.Count)
            return -1;

        InventorySlot slot = slots[slotIndex];
        if (slot == null || !slot.isOccupied || slot.item == null)
            return -1;

        if (slot.isAnchor)
            return slotIndex;

        if (slot.anchorIndex >= 0 && slot.anchorIndex < slots.Count)
            return slot.anchorIndex;

        return -1;
    }

    private bool IsIndexWithinAnchorFootprint(int anchorIndex, int candidateIndex, int width, int height)
    {
        if (anchorIndex < 0 || candidateIndex < 0)
            return false;

        int columns = GetGridColumns();
        int anchorCol = anchorIndex % columns;
        int anchorRow = anchorIndex / columns;

        int candidateCol = candidateIndex % columns;
        int candidateRow = candidateIndex / columns;

        return candidateCol >= anchorCol
            && candidateCol < anchorCol + Mathf.Max(1, width)
            && candidateRow >= anchorRow
            && candidateRow < anchorRow + Mathf.Max(1, height);
    }

    private int RemoveFromAnchorIndex(int anchorIndex, int quantity)
    {
        if (quantity <= 0 || anchorIndex < 0 || anchorIndex >= slots.Count)
            return 0;

        InventorySlot anchor = slots[anchorIndex];
        if (anchor == null || !anchor.isOccupied || anchor.item == null || !anchor.isAnchor)
            return 0;

        int toRemove = Mathf.Min(Mathf.Max(1, anchor.quantity), quantity);
        anchor.quantity -= toRemove;

        if (anchor.quantity <= 0)
            ClearAnchoredItem(anchorIndex);

        return toRemove;
    }

    private bool UsesLargeFootprint(Item item)
    {
        return item is GunItem || item is MeleeWeaponItem;
    }

    private static Item CreateRuntimeItemInstance(Item item)
    {
        if (item is GunItem)
        {
            Item clone = Instantiate(item);
            clone.name = item.name;
            return clone;
        }

        return item;
    }

    private bool CanPlaceAt(int anchorIndex, int width, int height)
    {
        return CanPlaceAt(anchorIndex, width, height, null);
    }

    private bool CanPlaceAt(int anchorIndex, int width, int height, bool[] occupiedOverride)
    {
        int columns = GetGridColumns();
        int rows = GetGridRows();

        if (anchorIndex < 0 || anchorIndex >= slots.Count)
            return false;

        int anchorCol = anchorIndex % columns;
        int anchorRow = anchorIndex / columns;

        if (anchorCol + width > columns || anchorRow + height > rows)
            return false;

        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                int idx = (anchorRow + y) * columns + (anchorCol + x);
                bool occupied = occupiedOverride != null
                    ? occupiedOverride[idx]
                    : (slots[idx] != null && slots[idx].isOccupied);

                if (occupied)
                    return false;
            }
        }

        return true;
    }

    private void PlaceItemWithFootprint(int anchorIndex, Item item, int quantity, int width, int height)
    {
        int columns = GetGridColumns();
        int anchorCol = anchorIndex % columns;
        int anchorRow = anchorIndex / columns;

        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                int idx = (anchorRow + y) * columns + (anchorCol + x);
                bool isAnchorCell = x == 0 && y == 0;
                slots[idx] = new InventorySlot(item, isAnchorCell ? quantity : 0)
                {
                    isOccupied = true,
                    isAnchor = isAnchorCell,
                    anchorIndex = anchorIndex,
                    footprintWidth = width,
                    footprintHeight = height
                };
            }
        }
    }

    private void ClearAnchoredItem(int anchorIndex)
    {
        if (anchorIndex < 0 || anchorIndex >= slots.Count)
            return;

        InventorySlot origin = slots[anchorIndex];
        if (origin == null || !origin.isOccupied)
            return;

        int realAnchorIndex = anchorIndex;

        // If called with a child cell, resolve its anchor. If called with an anchor,
        // trust the provided index to avoid stale metadata wiping the wrong block.
        if (!origin.isAnchor && origin.anchorIndex >= 0 && origin.anchorIndex < slots.Count)
            realAnchorIndex = origin.anchorIndex;

        InventorySlot anchor = slots[realAnchorIndex];
        if (anchor == null)
            return;

        int width = Mathf.Max(1, anchor.footprintWidth);
        int height = Mathf.Max(1, anchor.footprintHeight);
        int columns = GetGridColumns();
        int anchorCol = realAnchorIndex % columns;
        int anchorRow = realAnchorIndex / columns;
        Item anchorItem = anchor.item;

        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                int idx = (anchorRow + y) * columns + (anchorCol + x);
                if (idx >= 0 && idx < slots.Count)
                {
                    var candidate = slots[idx];
                    bool belongsToThisAnchor = candidate != null
                        && candidate.isOccupied
                        && candidate.item == anchorItem
                        && ((candidate.isAnchor && idx == realAnchorIndex)
                            || (!candidate.isAnchor && candidate.anchorIndex == realAnchorIndex));

                    if (belongsToThisAnchor)
                        slots[idx] = InventorySlot.Empty();
                }
            }
        }

        // Fallback for legacy/stale metadata: ensure the anchor cell itself is always cleared.
        if (realAnchorIndex >= 0 && realAnchorIndex < slots.Count)
            slots[realAnchorIndex] = InventorySlot.Empty();
    }

    private static void MarkFootprint(int anchorIndex, int width, int height, bool[] occupied, int columns, bool value)
    {
        if (occupied == null || occupied.Length == 0 || columns <= 0)
            return;

        // The caller guarantees indices through CanPlaceAt checks; this method only marks.
        int anchorCol = anchorIndex % columns;
        int anchorRow = anchorIndex / columns;

        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                int idx = (anchorRow + y) * columns + (anchorCol + x);
                if (idx >= 0 && idx < occupied.Length)
                    occupied[idx] = value;
            }
        }
    }

    private void SpawnDroppedPickup(Item item, int quantity)
    {
        if (item == null || quantity <= 0)
            return;

        Vector3 forward = transform.forward;
        var controller = GetComponent<ThirdPersonController>();
        if (controller != null && controller.MainUnityCamera != null)
        {
            forward = controller.MainUnityCamera.transform.forward;
            forward.y = 0f;
            if (forward.sqrMagnitude < 0.0001f)
                forward = transform.forward;
            else
                forward.Normalize();
        }

        Vector3 dropPosition = transform.position + forward * dropForwardDistance + Vector3.up * dropUpOffset;

        // Root pickup object (contains pickup logic).
        var droppedObject = new GameObject($"Dropped_{item.ItemName}");
        droppedObject.transform.position = dropPosition;

        // Try to use the item's real world/prefab visual first.
        GameObject visualPrefab = ResolveDropVisualPrefab(item);
        if (visualPrefab != null)
        {
            GameObject visual = Instantiate(visualPrefab, droppedObject.transform);
            visual.transform.localPosition = Vector3.zero;
            visual.transform.localRotation = Quaternion.identity;
            visual.transform.localScale = Vector3.one;
            DisableInteractiveComponents(visual);
        }
        else
        {
            // Fallback visual if no prefab is available.
            var fallback = GameObject.CreatePrimitive(PrimitiveType.Cube);
            fallback.name = "FallbackVisual";
            fallback.transform.SetParent(droppedObject.transform, false);
            fallback.transform.localScale = Vector3.one * 0.28f;

            var fallbackCollider = fallback.GetComponent<Collider>();
            if (fallbackCollider != null)
                Destroy(fallbackCollider);

            var renderer = fallback.GetComponent<Renderer>();
            if (renderer != null)
            {
                renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                renderer.receiveShadows = false;
                renderer.material.color = new Color(0.2f, 0.26f, 0.34f, 1f);
            }
        }

        var pickup = droppedObject.AddComponent<ItemPickup>();
        pickup.Initialize(item, quantity, droppedPickupRadius);
    }

    private static GameObject ResolveDropVisualPrefab(Item item)
    {
        if (item == null)
            return null;

        return item.Prefab;
    }

    private static void DisableInteractiveComponents(GameObject root)
    {
        if (root == null)
            return;

        foreach (var behaviour in root.GetComponentsInChildren<MonoBehaviour>(true))
            behaviour.enabled = false;

        foreach (var collider in root.GetComponentsInChildren<Collider>(true))
            collider.enabled = false;

        foreach (var rb in root.GetComponentsInChildren<Rigidbody>(true))
            rb.isKinematic = true;
    }

    private void EnsureSlotCapacity()
    {
        int targetCapacity = Mathf.Max(1, GetSlotCapacity());

        if (slots.Count == targetCapacity)
            return;

        if (slots.Count < targetCapacity)
        {
            while (slots.Count < targetCapacity)
                slots.Add(InventorySlot.Empty());

            return;
        }

        // Shrinking: preserve filled slots as much as possible.
        slots.RemoveRange(targetCapacity, slots.Count - targetCapacity);
    }
}
