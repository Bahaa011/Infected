using UnityEngine;
using UnityEngine.InputSystem;

public class ItemPickup : MonoBehaviour
{
    [SerializeField] private Item itemToPickup;
    [SerializeField] private int quantity = 1;
    [SerializeField] private float pickupRadius = 2f;
    [SerializeField] private InputActionReference pickupAction;
    
    private Collider pickupCollider;
    private Player player;
    private ThirdPersonController playerController;
    private bool isPickedUp = false;

    private void OnEnable()
    {
        Debug.Log($"[{itemToPickup?.ItemName ?? "Unknown"}] OnEnable called");
        // Don't disable/enable the action - it's shared and managed elsewhere
    }

    private void OnDisable()
    {
        // Don't disable the action - it's shared and managed elsewhere
    }

    private void Start()
    {
        Debug.Log($"[{itemToPickup?.ItemName ?? "Unknown"}] Start called");
        // Create a trigger collider for pickup detection if it doesn't exist
        pickupCollider = GetComponent<Collider>();
        if (pickupCollider == null)
        {
            SphereCollider sphere = gameObject.AddComponent<SphereCollider>();
            sphere.radius = pickupRadius;
            sphere.isTrigger = true;
            pickupCollider = sphere;
        }
        else
        {
            pickupCollider.isTrigger = true;
        }

        // Try to find player in scene
        player = FindFirstObjectByType<Player>();
        if (player != null)
        {
            playerController = player.GetComponent<ThirdPersonController>();
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
            Debug.Log($"Player in range of {itemToPickup.ItemName}. Press E to pick up!");
        }
    }

    private void OnTriggerExit(Collider collision)
    {
        Player p = collision.GetComponent<Player>();
        if (p != null && p == player)
        {
            Debug.Log($"Player out of range of {itemToPickup.ItemName}");
        }  
    }

    private void Update()
    {
        if (isPickedUp) return;
        if (pickupAction == null) return;
        if (!pickupAction.action.WasPerformedThisFrame()) return;
        
        Debug.Log($"[{itemToPickup.ItemName}] E pressed!");
        
        if (player == null)
            player = FindFirstObjectByType<Player>();
        
        if (player == null)
        {
            Debug.Log($"[{itemToPickup.ItemName}] No player found");
            return;
        }
        
        float distance = Vector3.Distance(transform.position, player.transform.position);
        if (distance > pickupRadius + 1f)
        {
            Debug.Log($"[{itemToPickup.ItemName}] Too far: {distance}");
            return;
        }
        
        if (playerController == null)
            playerController = player.GetComponent<ThirdPersonController>();
        
        if (playerController == null || playerController.MainUnityCamera == null)
        {
            Debug.Log($"[{itemToPickup.ItemName}] No camera");
            return;
        }
        
        Ray ray = new Ray(playerController.MainUnityCamera.transform.position, playerController.MainUnityCamera.transform.forward);
        RaycastHit hit;
        
        if (!Physics.Raycast(ray, out hit, pickupRadius + 1f))
        {
            Debug.Log($"[{itemToPickup.ItemName}] Raycast hit nothing");
            return;
        }
        
        if (hit.collider != pickupCollider)
        {
            Debug.Log($"[{itemToPickup.ItemName}] Hit wrong object: {hit.collider.gameObject.name}");
            return;
        }
        
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
            Debug.LogWarning($"Current weight: {inventory.GetCurrentWeight()}/{inventory.GetMaxWeight()}");
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
