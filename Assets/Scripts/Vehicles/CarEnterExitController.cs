using UnityEngine;
using UnityEngine.InputSystem;

[RequireComponent(typeof(AdvancedCarController))]
public class CarEnterExitController : MonoBehaviour
{
    [Header("Driver Points")]
    [SerializeField] private Transform driverSeat;
    [SerializeField] private Transform exitPoint;
    [SerializeField] private Vector3 seatLocalPositionOffset = Vector3.zero;
    [SerializeField] private Vector3 seatEulerOffset = Vector3.zero;
    [SerializeField] private bool seatYawOnly = true;

    [Header("Interaction")]
    [SerializeField] private float enterDistance = 3f;
    [SerializeField] private string playerTag = "Player";
    [SerializeField] private float reInteractCooldown = 0.2f;
    [SerializeField] private bool autoCreateEnterTrigger = true;
    [SerializeField] private float enterTriggerRadius = 4f;

    private AdvancedCarController carController;
    private Player cachedPlayer;
    private Player currentDriver;
    private PlayerInput currentDriverInput;
    private InputAction interactAction;
    private ThirdPersonController thirdPersonController;
    private CharacterController characterController;
    private Collider[] carColliders;
    private float nextInteractAllowedTime;
    private bool isPlayerInRange;

    private void Awake()
    {
        carController = GetComponent<AdvancedCarController>();
        carColliders = GetComponentsInChildren<Collider>(true);
        EnsureEnterTrigger();

        if (driverSeat == null)
            driverSeat = transform;

        if (exitPoint == null)
            exitPoint = transform;
    }

    private void OnEnable()
    {
        TryBindInteractAction();
    }

    private void OnDisable()
    {
        UnbindInteractAction();
    }

    private void Update()
    {
        // Keep bindings valid even if player object/actions got recreated.
        if (interactAction == null)
            TryBindInteractAction();

        // Fallback path for projects where callback isn't fired reliably.
        if (Keyboard.current != null && Keyboard.current.eKey.wasPressedThisFrame)
            OnInteractPressedFallback();
    }

    private void TryBindInteractAction()
    {
        Player player = GetPlayer();
        if (player == null)
            return;

        PlayerInput playerInput = player.GetComponent<PlayerInput>();
        if (playerInput == null)
            return;

        InputAction action = playerInput.actions.FindAction("Interact");
        if (action == null)
            return;

        if (interactAction == action)
            return;

        UnbindInteractAction();
        currentDriverInput = playerInput;
        interactAction = action;

        if (!interactAction.enabled)
            interactAction.Enable();

        interactAction.performed += OnInteractPerformed;
    }

    private void UnbindInteractAction()
    {
        if (interactAction != null)
            interactAction.performed -= OnInteractPerformed;

        interactAction = null;
    }

    private void OnInteractPerformed(InputAction.CallbackContext ctx)
    {
        HandleInteractPress();
    }

    private void OnInteractPressedFallback()
    {
        HandleInteractPress();
    }

    private void HandleInteractPress()
    {
        if (Time.time < nextInteractAllowedTime)
            return;

        if (currentDriver == null)
        {
            TryEnterVehicle();
        }
        else
        {
            ExitVehicle();
        }

        nextInteractAllowedTime = Time.time + reInteractCooldown;
    }

    private void TryEnterVehicle()
    {
        Player player = GetPlayer();
        if (player == null)
            return;

        float distance = GetDistanceToCar(player.transform.position);
        if (!isPlayerInRange && distance > enterDistance)
            return;

        PlayerInput playerInput = ResolvePlayerInput(player);
        if (playerInput == null)
            return;

        EnterVehicle(player, playerInput);
    }

    private void EnterVehicle(Player player, PlayerInput playerInput)
    {
        currentDriver = player;
        currentDriverInput = playerInput;

        thirdPersonController = player.GetComponent<ThirdPersonController>();
        if (thirdPersonController != null)
            thirdPersonController.enabled = false;

        characterController = player.GetComponent<CharacterController>();
        if (characterController != null && characterController.enabled)
            characterController.enabled = false;

        Transform playerTransform = player.transform;
        playerTransform.SetParent(driverSeat);

        playerTransform.localPosition = seatLocalPositionOffset;

        if (seatYawOnly)
        {
            float yaw = driverSeat.eulerAngles.y + seatEulerOffset.y;
            playerTransform.rotation = Quaternion.Euler(seatEulerOffset.x, yaw, seatEulerOffset.z);
        }
        else
        {
            playerTransform.localRotation = Quaternion.Euler(seatEulerOffset);
        }

        carController.SetDriver(currentDriverInput);
    }

    private void ExitVehicle()
    {
        if (currentDriver == null)
            return;

        carController.ClearDriver();

        Transform playerTransform = currentDriver.transform;
        playerTransform.SetParent(null);

        Vector3 spawnPos = exitPoint != null ? exitPoint.position : transform.position + transform.right * 2f;
        Quaternion spawnRot = Quaternion.Euler(0f, transform.eulerAngles.y, 0f);
        playerTransform.SetPositionAndRotation(spawnPos, spawnRot);

        if (characterController != null)
            characterController.enabled = true;

        if (thirdPersonController != null)
            thirdPersonController.enabled = true;

        currentDriver = null;
        currentDriverInput = null;
        thirdPersonController = null;
        characterController = null;
        isPlayerInRange = false;
    }

    private void OnTriggerEnter(Collider other)
    {
        if (other == null)
            return;

        if (IsPlayerCollider(other))
            isPlayerInRange = true;
    }

    private void OnTriggerExit(Collider other)
    {
        if (other == null)
            return;

        if (IsPlayerCollider(other))
            isPlayerInRange = false;
    }

    private bool IsPlayerCollider(Collider other)
    {
        Player player = GetPlayer();
        if (player == null)
            return false;

        Transform playerTransform = player.transform;
        Transform otherTransform = other.transform;

        return otherTransform == playerTransform
            || otherTransform.IsChildOf(playerTransform)
            || playerTransform.IsChildOf(otherTransform);
    }

    private void EnsureEnterTrigger()
    {
        if (!autoCreateEnterTrigger)
            return;

        SphereCollider existing = GetComponent<SphereCollider>();
        if (existing != null && existing.isTrigger)
        {
            existing.radius = Mathf.Max(existing.radius, enterTriggerRadius);
            return;
        }

        SphereCollider trigger = gameObject.AddComponent<SphereCollider>();
        trigger.isTrigger = true;
        trigger.radius = enterTriggerRadius;
        trigger.center = Vector3.zero;
    }

    private float GetDistanceToCar(Vector3 worldPosition)
    {
        float best = Vector3.Distance(worldPosition, driverSeat.position);

        if (carColliders == null || carColliders.Length == 0)
            return best;

        for (int i = 0; i < carColliders.Length; i++)
        {
            Collider c = carColliders[i];
            if (c == null || !c.enabled || c.isTrigger)
                continue;

            Vector3 p = c.ClosestPoint(worldPosition);
            float d = Vector3.Distance(worldPosition, p);
            if (d < best)
                best = d;
        }

        return best;
    }

    private Player GetPlayer()
    {
        if (cachedPlayer != null && cachedPlayer.isActiveAndEnabled)
            return cachedPlayer;

        GameObject playerGo = GameObject.FindGameObjectWithTag(playerTag);
        if (playerGo != null)
        {
            cachedPlayer = playerGo.GetComponent<Player>();
            if (cachedPlayer == null)
                cachedPlayer = playerGo.GetComponentInChildren<Player>();
            if (cachedPlayer == null)
                cachedPlayer = playerGo.GetComponentInParent<Player>();
        }

        if (cachedPlayer == null)
            cachedPlayer = FindAnyObjectByType<Player>();

        return cachedPlayer;
    }

    private PlayerInput ResolvePlayerInput(Player player)
    {
        if (player == null)
            return null;

        PlayerInput input = player.GetComponent<PlayerInput>();
        if (input == null)
            input = player.GetComponentInChildren<PlayerInput>();
        if (input == null)
            input = player.GetComponentInParent<PlayerInput>();

        return input;
    }
}
