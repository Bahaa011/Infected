using UnityEngine;

public class InventorySlot
{
    public Item item;
    public int quantity;
    public bool isOccupied;
    public bool isAnchor;
    public int anchorIndex;
    public int footprintWidth;
    public int footprintHeight;

    public InventorySlot(Item item, int quantity = 1)
    {
        this.item = item;
        this.quantity = quantity;
        isOccupied = item != null;
        isAnchor = item != null;
        anchorIndex = -1;
        footprintWidth = 1;
        footprintHeight = 1;
    }

    public static InventorySlot Empty()
    {
        return new InventorySlot(null, 0)
        {
            isOccupied = false,
            isAnchor = false,
            anchorIndex = -1,
            footprintWidth = 1,
            footprintHeight = 1
        };
    }

    public override string ToString()
    {
        if (item == null)
            return "Empty Slot";
        return $"{item.ItemName} x{quantity}";
    }
}
