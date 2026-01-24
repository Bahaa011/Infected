using UnityEngine;
using System.Collections.Generic;

public class Inventory : MonoBehaviour
{
    [SerializeField] private float maxWeight = 50f;
    [SerializeField] private float absoluteMaxWeight = 150f;
    
    [SerializeField] private float speedPenaltyPercentPerKg = 0.5f;
    [SerializeField] private float staminaDrainMultiplier = 1.5f;
    
    private List<InventorySlot> slots = new List<InventorySlot>();
    private float currentWeight = 0f;

    public delegate void InventoryChangedEvent();
    public event InventoryChangedEvent OnInventoryChanged;

    private void Start() { }

    public bool AddItem(Item item, int quantity = 1)
    {
        if (item == null || quantity <= 0)
            return false;

        float weightToAdd = item.Weight * quantity;

        // Hard limit check - don't allow items if it would exceed absolute max
        if (currentWeight + weightToAdd > absoluteMaxWeight)
        {
            Debug.LogWarning($"Weight limit exceeded! Cannot add {item.ItemName}");
            return false;
        }

        // Try to stack with existing item
        if (item.MaxStackSize > 1)
        {
            for (int i = 0; i < slots.Count; i++)
            {
                if (slots[i].item == item)
                {
                    int canAdd = item.MaxStackSize - slots[i].quantity;
                    if (canAdd > 0)
                    {
                        int toAdd = Mathf.Min(canAdd, quantity);
                        slots[i].quantity += toAdd;
                        currentWeight += item.Weight * toAdd;
                        quantity -= toAdd;

                        if (quantity == 0)
                        {
                            OnInventoryChanged?.Invoke();
                            return true;
                        }
                    }
                }
            }
        }

        // Find empty slot for remaining items
        while (quantity > 0)
        {
            int emptySlot = FindEmptySlot();
            if (emptySlot == -1)
            {
                // Create a new slot if none are empty
                slots.Add(new InventorySlot(null, 0));
                emptySlot = slots.Count - 1;
            }

            int toAdd = Mathf.Min(item.MaxStackSize, quantity);
            slots[emptySlot] = new InventorySlot(item, toAdd);
            currentWeight += item.Weight * toAdd;
            quantity -= toAdd;
        }

        OnInventoryChanged?.Invoke();
        return true;
    }

    public bool RemoveItem(Item item, int quantity = 1)
    {
        if (item == null || quantity <= 0)
            return false;

        int remaining = quantity;

        // Remove from back to front (more efficient)
        for (int i = slots.Count - 1; i >= 0 && remaining > 0; i--)
        {
            if (slots[i].item == item)
            {
                int toRemove = Mathf.Min(slots[i].quantity, remaining);
                slots[i].quantity -= toRemove;
                currentWeight -= item.Weight * toRemove;
                remaining -= toRemove;

                if (slots[i].quantity <= 0)
                {
                    slots[i] = new InventorySlot(null, 0);
                }
            }
        }

        bool success = remaining == 0;
        if (success)
        {
            OnInventoryChanged?.Invoke();
        }

        return success;
    }

    public float GetCurrentWeight() => currentWeight;

    public float GetMaxWeight() => maxWeight;

    public float GetWeightPercentage() => currentWeight / maxWeight;

    public bool IsOverWeightLimit() => currentWeight > maxWeight;

    public float GetOverWeightAmount() => Mathf.Max(0, currentWeight - maxWeight);

    public float GetSpeedPenalty()
    {
        if (currentWeight <= maxWeight)
            return 1f;

        float overWeight = currentWeight - maxWeight;
        float penaltyPercent = overWeight * (speedPenaltyPercentPerKg / 100f);
        return Mathf.Max(0.3f, 1f - penaltyPercent);
    }

    public float GetStaminaConsumptionMultiplier()
    {
        if (currentWeight <= maxWeight)
            return 1f;

        float overWeight = currentWeight - maxWeight;
        return 1f + (overWeight * 0.1f);
    }

    public float GetEncumbranceFactor() => GetSpeedPenalty();

    public int GetItemQuantity(Item item)
    {
        int total = 0;
        foreach (InventorySlot slot in slots)
        {
            if (slot.item == item)
                total += slot.quantity;
        }
        return total;
    }

    public List<InventorySlot> GetAllItems() => new List<InventorySlot>(slots);

    public InventorySlot GetSlot(int index)
    {
        if (index >= 0 && index < slots.Count)
            return slots[index];
        return null;
    }

    public bool CanAddItem(Item item, int quantity = 1)
    {
        if (item == null)
            return false;

        float weightNeeded = item.Weight * quantity;
        return currentWeight + weightNeeded <= absoluteMaxWeight;
    }

    public void SetMaxWeight(float newMaxWeight) => maxWeight = newMaxWeight;

    public void SetAbsoluteMaxWeight(float newAbsoluteMaxWeight) => absoluteMaxWeight = newAbsoluteMaxWeight;

    public void ClearInventory()
    {
        slots.Clear();
        currentWeight = 0f;
        OnInventoryChanged?.Invoke();
    }

    public void PrintInventory()
    {
        Debug.Log("=== INVENTORY ===");
        for (int i = 0; i < slots.Count; i++)
        {
            if (slots[i].item != null)
                Debug.Log($"Slot {i}: {slots[i]}");
        }
        Debug.Log($"Total Weight: {currentWeight}/{maxWeight} kg");
    }

    private int FindEmptySlot()
    {
        for (int i = 0; i < slots.Count; i++)
        {
            if (slots[i].item == null)
                return i;
        }
        return -1;
    }
}
