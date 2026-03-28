using UnityEngine;
using UnityEngine.InputSystem;
using System.Linq;

public class ItemPickup : MonoBehaviour
{
    [Header("Item Settings")]
    [SerializeField] private Item itemToPickup;
    [SerializeField] private int quantity = 1;
    [SerializeField] private float pickupRadius = 2f;
    
    private Collider pickupCollider;
    private Player player;
    private ThirdPersonController playerController;
    private bool isPickedUp = false;
    private bool playerInRange = false;

    public void Initialize(Item item, int stackQuantity, float radius = 2f)
    {
        itemToPickup = item;
        quantity = Mathf.Max(1, stackQuantity);
        pickupRadius = Mathf.Max(0.25f, radius);
    }

    private void Start()
    {
        Debug.Log($"[{itemToPickup?.ItemName ?? "Unknown"}] Start called");
        
        if (itemToPickup == null)
        {
            Debug.LogError("[ItemPickup] itemToPickup is null! Assign an Item in the inspector.");
            return;
        }
        
        // Create a trigger collider for pickup detection if it doesn't exist
        pickupCollider = GetComponent<Collider>();
        if (pickupCollider == null)
        {
            SphereCollider sphere = gameObject.AddComponent<SphereCollider>();
            sphere.radius = pickupRadius;
            sphere.isTrigger = true;
            pickupCollider = sphere;
            Debug.Log($"[{itemToPickup.ItemName}] Created SphereCollider with radius {pickupRadius}");
        }
        else
        {
            pickupCollider.isTrigger = true;
            Debug.Log($"[{itemToPickup.ItemName}] Using existing collider: {pickupCollider.GetType().Name}");
        }

        // Try to find player
        player = FindFirstObjectByType<Player>();
        if (player == null)
        {
            Debug.LogError("[ItemPickup] Could not find Player in scene!");
            return;
        }
        
        Debug.Log($"[{itemToPickup.ItemName}] Found player: {player.gameObject.name}");
        
        playerController = player.GetComponent<ThirdPersonController>();
        if (playerController == null)
        {
            Debug.LogWarning($"[{itemToPickup.ItemName}] Player has no ThirdPersonController!");
        }
    }

    private void Update()
    {
        if (isPickedUp || !playerInRange) return;
        
        // Check for E key press
        if (Keyboard.current.eKey.wasPressedThisFrame)
        {
            Debug.Log($"[INPUT] E key pressed!");
            AttemptPickup();
        }
    }

    private void OnTriggerEnter(Collider collision)
    {
        if (isPickedUp)
            return;

        Player p = collision.GetComponent<Player>();
        if (p != null)
        {
            player = p;
            playerController = player.GetComponent<ThirdPersonController>();
            playerInRange = true;
            Debug.Log($"[TRIGGER] Player entered range of {itemToPickup?.ItemName}");
        }
    }

    private void OnTriggerExit(Collider collision)
    {
        Player p = collision.GetComponent<Player>();
        if (p != null && p == player)
        {
            playerInRange = false;
            Debug.Log($"[TRIGGER] Player left range of {itemToPickup?.ItemName}");
        }  
    }

    private void AttemptPickup()
    {
        Debug.Log($"[INPUT] Attempting to pick up {itemToPickup?.ItemName}");
        
        if (player == null)
        {
            Debug.LogError($"[INPUT] Player is null!");
            return;
        }
        
        float distance = Vector3.Distance(transform.position, player.transform.position);
        Debug.Log($"[INPUT] Distance to player: {distance} (max: {pickupRadius + 1f})");
        
        if (distance > pickupRadius + 1f)
        {
            Debug.Log($"[INPUT] Player too far away");
            return;
        }
        
        if (playerController == null)
            playerController = player.GetComponent<ThirdPersonController>();
        
        if (playerController == null)
        {
            Debug.LogError($"[INPUT] No ThirdPersonController found");
            return;
        }
        
        if (playerController.MainUnityCamera == null)
        {
            Debug.LogError($"[INPUT] No camera found");
            return;
        }
        
        Ray ray = new Ray(playerController.MainUnityCamera.transform.position, playerController.MainUnityCamera.transform.forward);
        RaycastHit hit;
        
        Debug.Log($"[INPUT] Raycasting from camera...");
        
        if (!Physics.Raycast(ray, out hit, pickupRadius + 1f))
        {
            Debug.Log($"[INPUT] Raycast hit nothing");
            return;
        }
        
        Debug.Log($"[INPUT] Raycast hit: {hit.collider.gameObject.name}");
        
        if (hit.collider != pickupCollider)
        {
            Debug.Log($"[INPUT] Hit wrong object: {hit.collider.gameObject.name} (expected {gameObject.name})");
            return;
        }
        
        Debug.Log($"[INPUT] All checks passed! Picking up item...");
        PickupItem();
    }

    private void PickupItem()
    {
        if (player == null)
        {
            Debug.LogWarning($"Cannot pickup {itemToPickup.ItemName}: Player not found!");
            return;
        }

        Inventory inventory = player.GetComponent<Inventory>();
        if (inventory == null)
        {
            Debug.LogWarning("Player has no Inventory component!");
            return;
        }

        if (inventory.AddItem(itemToPickup, quantity))
        {
            Debug.Log($"Picked up {quantity}x {itemToPickup.ItemName}! Inventory now has {inventory.GetAllItems().Count} slots");
            inventory.PrintInventory();
            isPickedUp = true;
            
            // Destroy the item in the world immediately so the next stacked item can be picked up
            Destroy(gameObject);
        }
        else
        {
            Debug.LogWarning($"Could not add {itemToPickup.ItemName} to inventory - inventory full or item cannot be added!");
            inventory.PrintInventory();
        }
    }

    public Item GetItem()
    {
        return itemToPickup;
    }

    public bool IsPickedUp()
    {
        return isPickedUp;
    }
}
