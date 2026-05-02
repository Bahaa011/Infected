using UnityEngine;
using UnityEngine.InputSystem;

public class DoorController : MonoBehaviour, IInteractionPromptSource
{
    [Header("Door Settings")]
    [SerializeField] private float openAngle = 90f;
    [SerializeField] private float closeAngle = 0f;
    [SerializeField] private float rotationSpeed = 2f;
    [SerializeField] private bool isOpen = false;

    [Header("Interaction")]
    [SerializeField] private float interactionRange = 3f;

    [Header("Collision")]
    [SerializeField] private bool disableMeshCollidersWhenOpen = true;
    [SerializeField] private bool includeChildMeshColliders = true;

    [Header("Audio")]
    [SerializeField] private AudioClip openSound;
    [SerializeField] private AudioClip closeSound;
    [SerializeField] private float audioVolume = 1f;

    private AudioSource audioSource;
    private Quaternion targetRotation;
    private Quaternion closedRotation;
    private bool isAnimating = false;
    private bool isPlayerNearby = false;
    private Transform player;
    private InputAction interactAction;
    private MeshCollider[] doorMeshColliders;

    private void Start()
    {
        audioSource = GetComponent<AudioSource>();
        if (audioSource == null)
        {
            audioSource = gameObject.AddComponent<AudioSource>();
        }

        // Find player
        player = GameObject.FindGameObjectWithTag("Player")?.transform;

        // Set initial rotation
        targetRotation = transform.rotation;
        closedRotation = transform.rotation;

        // Ensure door has a collider for interaction
        if (GetComponent<Collider>() == null)
        {
            BoxCollider collider = gameObject.AddComponent<BoxCollider>();
            collider.isTrigger = false;
        }

        doorMeshColliders = includeChildMeshColliders
            ? GetComponentsInChildren<MeshCollider>(true)
            : GetComponents<MeshCollider>();

        // Setup Rigidbody for door physics
        Rigidbody rb = GetComponent<Rigidbody>();
        if (rb == null)
        {
            rb = gameObject.AddComponent<Rigidbody>();
        }
        
        // Configure rigidbody to be kinematic (won't fall)
        rb.isKinematic = true;
        rb.constraints = RigidbodyConstraints.FreezeAll;

        UpdateDoorCollisionState();

        // Get input action from player
        if (player != null)
        {
            PlayerInput playerInput = player.GetComponent<PlayerInput>();
            if (playerInput != null)
            {
                interactAction = playerInput.actions.FindAction("Interact");
                if (interactAction != null)
                {
                    interactAction.Enable();
                    interactAction.performed += OnInteract;
                }
            }
        }
    }

    private void OnDestroy()
    {
        if (interactAction != null)
        {
            interactAction.performed -= OnInteract;
        }
    }

    private void OnInteract(InputAction.CallbackContext context)
    {
        if (isPlayerNearby)
        {
            ToggleDoor();
        }
    }

    private void Update()
    {
        // Check if player is nearby
        if (player != null)
        {
            float distance = Vector3.Distance(transform.position, player.position);
            isPlayerNearby = distance <= interactionRange;
        }

        // Smoothly rotate door towards target rotation
        if (isAnimating)
        {
            transform.rotation = Quaternion.Lerp(
                transform.rotation,
                targetRotation,
                Time.deltaTime * rotationSpeed
            );

            // Check if door has reached target rotation
            if (Quaternion.Angle(transform.rotation, targetRotation) < 0.5f)
            {
                transform.rotation = targetRotation;
                isAnimating = false;
            }
        }
    }

    public void OpenDoor()
    {
        if (isOpen) return;

        isOpen = true;
        isAnimating = true;
        targetRotation = closedRotation * Quaternion.Euler(0, 0, openAngle);
        UpdateDoorCollisionState();

        PlaySound(openSound);
    }

    public void CloseDoor()
    {
        if (!isOpen) return;

        isOpen = false;
        isAnimating = true;
        targetRotation = closedRotation;
        UpdateDoorCollisionState();

        PlaySound(closeSound);
    }

    public void ToggleDoor()
    {
        if (isOpen)
        {
            CloseDoor();
        }
        else
        {
            OpenDoor();
        }
    }

    public bool IsOpen => isOpen;

    public bool IsPlayerNearby => isPlayerNearby;

    public bool TryGetInteractionPrompt(Transform viewer, out string prompt)
    {
        prompt = string.Empty;

        if (viewer == null)
            return false;

        float distance = Vector3.Distance(transform.position, viewer.position);
        if (distance > interactionRange)
            return false;

        prompt = isOpen ? "Press E to close door" : "Press E to open door";
        return true;
    }

    private void PlaySound(AudioClip clip)
    {
        if (clip != null && audioSource != null)
        {
            audioSource.PlayOneShot(clip, audioVolume);
        }
    }

    private void UpdateDoorCollisionState()
    {
        if (!disableMeshCollidersWhenOpen || doorMeshColliders == null)
            return;

        bool meshEnabled = !isOpen;
        for (int i = 0; i < doorMeshColliders.Length; i++)
        {
            MeshCollider mesh = doorMeshColliders[i];
            if (mesh == null)
                continue;

            mesh.enabled = meshEnabled;
        }
    }
}
