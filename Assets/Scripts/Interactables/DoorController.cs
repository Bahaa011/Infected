using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

public class DoorController : MonoBehaviour, IInteractionPromptSource
{
    private static readonly List<DoorController> activeDoors = new List<DoorController>();

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
    [Tooltip("Single clip containing both open and close segments.")]
    [SerializeField] private AudioClip doorClip;
    [SerializeField, Tooltip("Volume multiplier for door audio. Values above 1 make it louder.")]
    private float audioVolume = 3f;
    [SerializeField, Tooltip("Open segment start time in seconds")]
    private float openStart = 0f;
    [SerializeField, Tooltip("Open segment duration in seconds")]
    private float openDuration = 1f;
    [SerializeField, Tooltip("Close segment start time in seconds")]
    private float closeStart = 4.5f;
    [SerializeField, Tooltip("Close segment duration in seconds")]
    private float closeDuration = 1f;
    private AudioSource audioSource;
    private Quaternion targetRotation;
    private Quaternion closedRotation;
    private bool isAnimating = false;
    private bool isPlayerNearby = false;
    private Transform player;
    private InputAction interactAction;
    private MeshCollider[] doorMeshColliders;

    private void OnEnable()
    {
        if (!activeDoors.Contains(this))
            activeDoors.Add(this);
    }

    private void Start()
    {
        audioSource = GetComponent<AudioSource>();
        if (audioSource == null)
        {
            audioSource = gameObject.AddComponent<AudioSource>();
        }
        audioSource.playOnAwake = false;
        audioSource.spatialBlend = 0f;
        audioSource.volume = 1f;
        audioSource.rolloffMode = AudioRolloffMode.Linear;

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

    private void OnDisable()
    {
        activeDoors.Remove(this);

        if (interactAction != null)
        {
            interactAction.performed -= OnInteract;
        }
    }

    private void OnDestroy()
    {
        activeDoors.Remove(this);
    }

    private void OnInteract(InputAction.CallbackContext context)
    {
        if (!isPlayerNearby || player == null)
            return;

        if (!IsNearestInteractableDoor())
            return;

        ToggleDoor();
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

        if (doorClip != null)
            PlayClipSegment(doorClip, openStart, openDuration);
        else
            PlaySound(doorClip);
    }

    public void CloseDoor()
    {
        if (!isOpen) return;

        isOpen = false;
        isAnimating = true;
        targetRotation = closedRotation;
        UpdateDoorCollisionState();

        if (doorClip != null)
            PlayClipSegment(doorClip, closeStart, closeDuration);
        else
            PlaySound(doorClip);
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

    private bool IsNearestInteractableDoor()
    {
        if (player == null)
            return false;

        float myDistance = Vector3.Distance(transform.position, player.position);
        if (myDistance > interactionRange)
            return false;

        DoorController closestDoor = null;
        float closestDistance = float.MaxValue;

        for (int i = 0; i < activeDoors.Count; i++)
        {
            DoorController door = activeDoors[i];
            if (door == null || !door.isActiveAndEnabled || door.player == null || !door.isPlayerNearby)
                continue;

            float distance = Vector3.Distance(door.transform.position, player.position);
            if (distance > door.interactionRange)
                continue;

            if (distance < closestDistance)
            {
                closestDistance = distance;
                closestDoor = door;
            }
        }

        return closestDoor == this;
    }

    private void PlaySound(AudioClip clip)
    {
        if (clip == null || audioSource == null)
            return;

        // Default: play full clip via one-shot
        audioSource.PlayOneShot(clip, audioVolume);
    }

    private void PlayClipSegment(AudioClip clip, float startTime, float duration)
    {
        if (clip == null)
            return;

        StopAllCoroutines();
        StartCoroutine(PlayClipSegmentCoroutine(clip, startTime, duration));
    }

    private System.Collections.IEnumerator PlayClipSegmentCoroutine(AudioClip clip, float startTime, float duration)
    {
        if (clip == null || audioSource == null)
            yield break;

        float clampedStart = Mathf.Clamp(startTime, 0f, Mathf.Max(0f, clip.length - 0.01f));
        float clampedDuration = Mathf.Clamp(duration, 0f, Mathf.Max(0f, clip.length - clampedStart));
        int startSamples = Mathf.Clamp((int)(clampedStart * clip.frequency), 0, Mathf.Max(0, clip.samples - 1));

        audioSource.Stop();
        audioSource.clip = clip;
        audioSource.timeSamples = startSamples;
        audioSource.volume = audioVolume;
        audioSource.loop = false;
        audioSource.Play();

        yield return new WaitForSeconds(clampedDuration);

        if (audioSource != null && audioSource.isPlaying)
            audioSource.Stop();
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
