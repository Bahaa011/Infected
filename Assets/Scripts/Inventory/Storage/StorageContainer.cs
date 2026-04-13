using System;
using UnityEngine;
using UnityEngine.InputSystem;

[RequireComponent(typeof(Inventory))]
[RequireComponent(typeof(Collider))]
public class StorageContainer : MonoBehaviour
{
    [Serializable]
    private struct StartingStack
    {
        public Item item;
        public int quantity;
    }

    [Header("Storage")]
    [SerializeField] private string storageDisplayName = "Loot Box";
    [SerializeField] private bool fillStartingItemsOnStart = false;
    [SerializeField] private StartingStack[] startingItems;

    private Inventory storageInventory;
    private StorageWindowUIToolkit storageWindow;
    private Player playerInRange;

    private void Awake()
    {
        storageInventory = GetComponent<Inventory>();
        storageWindow = FindAnyObjectByType<StorageWindowUIToolkit>();
    }

    private void Start()
    {
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

    private void Update()
    {
        if (playerInRange == null)
            return;

        if (Keyboard.current == null || !Keyboard.current.eKey.wasPressedThisFrame)
            return;

        if (storageWindow == null)
            storageWindow = FindAnyObjectByType<StorageWindowUIToolkit>();

        if (storageWindow == null)
            return;

        if (storageWindow.IsOpenFor(storageInventory))
            storageWindow.CloseStorage();
        else
            storageWindow.OpenStorage(storageInventory, storageDisplayName);
    }

    private void OnTriggerEnter(Collider other)
    {
        Player player = other.GetComponent<Player>();
        if (player != null)
            playerInRange = player;
    }

    private void OnTriggerExit(Collider other)
    {
        Player player = other.GetComponent<Player>();
        if (player == null || player != playerInRange)
            return;

        playerInRange = null;

        if (storageWindow != null && storageWindow.IsOpenFor(storageInventory))
            storageWindow.CloseStorage();
    }
}
