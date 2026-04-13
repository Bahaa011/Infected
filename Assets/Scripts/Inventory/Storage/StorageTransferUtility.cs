using UnityEngine;

public static class StorageTransferUtility
{
    public static bool TransferItemToSlot(Inventory sourceInventory, Inventory targetInventory, int sourceSlotIndex, int targetSlotIndex)
    {
        if (sourceInventory == null || targetInventory == null)
            return false;

        int sourceAnchorIndex = sourceInventory.ResolveAnchorSlotIndex(sourceSlotIndex);
        if (sourceAnchorIndex < 0)
            return false;

        InventorySlot sourceAnchor = sourceInventory.GetSlot(sourceAnchorIndex);
        if (sourceAnchor == null || !sourceAnchor.isAnchor || !sourceAnchor.isOccupied || sourceAnchor.item == null)
            return false;

        Item item = sourceAnchor.item;
        int quantity = Mathf.Max(1, sourceAnchor.quantity);

        if (!targetInventory.AddItemAtSlot(item, quantity, targetSlotIndex))
            return false;

        if (sourceInventory.RemoveItemAtSlot(sourceAnchorIndex, quantity))
            return true;

        // Rollback if source remove failed unexpectedly.
        targetInventory.RemoveItemAtSlot(targetSlotIndex, quantity);
        return false;
    }
}
