using UnityEngine;

public class InventorySlot
{
    public Item item;
    public int quantity;

    public InventorySlot(Item item, int quantity = 1)
    {
        this.item = item;
        this.quantity = quantity;
    }

    public float GetTotalWeight() => item != null ? item.Weight * quantity : 0f;

    public override string ToString()
    {
        if (item == null)
            return "Empty Slot";
        return $"{item.ItemName} x{quantity} ({GetTotalWeight()}kg)";
    }
}
