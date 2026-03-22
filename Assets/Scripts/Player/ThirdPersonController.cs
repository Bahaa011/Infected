using UnityEngine;
using UnityEngine.InputSystem;
using Unity.Cinemachine;

public class ThirdPersonController : MonoBehaviour
{
    [Header("Camera")]
    [SerializeField] private InputActionReference lookAction;
    [SerializeField] private Transform cameraTarget;
    [SerializeField] private float mouseSensitivity = 2f;
    [SerializeField] private float minPitch = -40f;
    [SerializeField] private float maxPitch = 70f;

    [Header("Movement")]
    [SerializeField] private float moveSpeed = 3f;
    [SerializeField] private float runSpeedMultiplier = 2f;
    [SerializeField] private float crouchSpeedMultiplier = 0.5f;
    [SerializeField] private float rotationSpeed = 10f;
    [SerializeField] private float aimTurnSpeed = 14f;
    [SerializeField] private float aimTurnDeadZone = 1f;
    [SerializeField] private float gravity = -9.81f;
    [SerializeField] private float crouchHeight = 1f;
    [SerializeField] private float normalHeight = 2f;
    [SerializeField] private InputActionReference sprintAction;

    [Header("Cameras")]
    [SerializeField] private CinemachineCamera mainCamera;
    [SerializeField] private CinemachineCamera aimingCamera;

    private float originalMouseSensitivity;

    public Camera MainUnityCamera
    {
        get
        {
            if (mainCamera != null)
            {
                // Try to get the Unity Camera component from CinemachineCamera
                var cam = mainCamera.GetComponent<Camera>();
                if (cam != null)
                    return cam;
            }
            return Camera.main;
        }
    }

    private const float INPUT_THRESHOLD = 0.1f;
    private const float IDLE_THRESHOLD = 0.01f;

    private CharacterController controller;
    private Vector2 moveInput;
    private Vector3 velocity;
    private bool hasSprintAction;
    private Player player;
    private Inventory inventory;
    private AnimationController animationController;
    private EquipmentManager equipmentManager;
    private bool isActuallySprinting = false;

    // Camera
    private float yaw;
    private float pitch;
    private float lockedYaw;
    private bool cameraYawLocked = false;
    private Vector3 cameraLocalPosition;

    private void Awake()
    {
        controller = GetComponent<CharacterController>();
        player = GetComponent<Player>();
        inventory = GetComponent<Inventory>();
        animationController = GetComponent<AnimationController>();
        equipmentManager = GetComponent<EquipmentManager>();
        hasSprintAction = sprintAction != null;
        originalMouseSensitivity = mouseSensitivity;
    }

    private void Start()
    {
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
    }


    void OnEnable()
    {
        if (sprintAction != null)
            sprintAction.action.Enable();
        // Only enable lookAction if inventory is not open
        if (lookAction != null)
        {
            if (!InventoryUIToolkit.IsInventoryOpen)
                lookAction.action.Enable();
            else
                lookAction.action.Disable();
        }
    }

    void OnDisable()
    {
        if (sprintAction != null)
            sprintAction.action.Disable();
        if (lookAction != null)
            lookAction.action.Disable();
    }

    public void OnMove(InputValue value)
    {
        moveInput = value.Get<Vector2>();
    }

    private void Update()
    {
        // Lock yaw when inventory opens to prevent camera from rotating
        if (InventoryUIToolkit.IsInventoryOpen)
        {
            if (!cameraYawLocked)
            {
                lockedYaw = yaw;
                cameraYawLocked = true;
            }
            // Keep yaw locked at the stored value
            yaw = lockedYaw;
        }
        else
        {
            cameraYawLocked = false;
        }

        HandleMovementAndGravity();
        UpdateCrouch();
        UpdateAiming();
        UpdateAimFacing();
        if (animationController != null)
        {
            animationController.UpdateMovementAnimation(moveInput.sqrMagnitude, isActuallySprinting, runSpeedMultiplier);
            animationController.UpdateCrouchAnimation();
            animationController.UpdateAimingAnimation();
        }
    }

    private void LateUpdate()
    {
        // Detach camera from player when inventory is open to prevent rotation
        if (cameraYawLocked && cameraTarget != null && cameraTarget.parent == transform)
        {
            cameraLocalPosition = cameraTarget.localPosition;
            cameraTarget.parent = null;
        }
        // Reattach camera to player when inventory closes
        else if (!cameraYawLocked && cameraTarget != null && cameraTarget.parent == null)
        {
            cameraTarget.parent = transform;
            cameraTarget.localPosition = cameraLocalPosition;
        }
        
        // When camera is detached, still follow the player's position
        if (cameraYawLocked && cameraTarget != null && cameraTarget.parent == null)
        {
            Vector3 desiredWorldPos = transform.position + cameraLocalPosition;
            cameraTarget.position = desiredWorldPos;
        }

        HandleCamera();
    }

    // ═══════════════════════════════════════════════════════════════
    // CAMERA CONTROL
    // ═══════════════════════════════════════════════════════════════

    void HandleCamera()
    {
        if (cameraTarget == null || lookAction == null)
            return;
        if (InventoryUIToolkit.IsInventoryOpen)
            return;

        Vector2 lookInput = lookAction.action.ReadValue<Vector2>();

        yaw += lookInput.x * (mouseSensitivity / 100f);
        pitch -= lookInput.y * (mouseSensitivity / 100f);
        pitch = Mathf.Clamp(pitch, minPitch, maxPitch);

        // Follow player XZ position, keep original Y position
        Vector3 targetPos = cameraTarget.position;
        targetPos.x = transform.position.x;
        targetPos.z = transform.position.z;
        cameraTarget.position = targetPos;
        
        // Only rotate camera target on both axes (independent of player rotation)
        cameraTarget.rotation = Quaternion.Euler(pitch, yaw, 0f);
    }

    void LateUpdateCameraWhenLocked()
    {
        // When camera yaw is locked, prevent the cameraTarget from moving with the player
        if (cameraYawLocked && cameraTarget != null)
        {
            // Don't update position - keep it fixed in world space
            cameraTarget.parent = null;
        }
    }

    // ═══════════════════════════════════════════════════════════════
    // MOVEMENT
    // ═══════════════════════════════════════════════════════════════


    void HandleMovementAndGravity()
    {
        Vector3 move = Vector3.zero;
        bool isMoving = moveInput.sqrMagnitude >= IDLE_THRESHOLD;

        // Check if sprint button is being pressed
        bool sprintInputPressed = sprintAction != null && sprintAction.action.IsPressed();
        player.SetSprintInputPressed(sprintInputPressed);

        // Calculate XZ movement
        if (isMoving)
        {
            Quaternion yawRotation = Quaternion.Euler(0, yaw, 0f);
            Vector3 camForward = yawRotation * Vector3.forward;
            Vector3 camRight = yawRotation * Vector3.right;
            Vector3 moveDir = (camForward * moveInput.y) + (camRight * moveInput.x);
            moveDir.Normalize();

            if (player != null && player.IsAiming() && equipmentManager != null && equipmentManager.GetCurrentWeapon() != null)
            {
                Quaternion targetAimRotation = Quaternion.Euler(0f, yaw, 0f);
                transform.rotation = Quaternion.Slerp(transform.rotation, targetAimRotation, aimTurnSpeed * Time.deltaTime);
            }
            else
            {
                Quaternion targetRotation = Quaternion.LookRotation(moveDir);
                transform.rotation = Quaternion.Slerp(transform.rotation, targetRotation, rotationSpeed * Time.deltaTime);
            }

            // Check if can sprint (has stamina remaining)
            bool canSprint = sprintInputPressed && player.GetStamina() > 0.1f;
            
            // Update player's sprint state and track actual sprint state
            player.SetIsCurrentlySprinting(canSprint);
            isActuallySprinting = canSprint;

            float currentSpeed = moveSpeed;
            if (canSprint) currentSpeed *= runSpeedMultiplier;
            if (player.IsCrouching()) currentSpeed *= crouchSpeedMultiplier;
            if (inventory != null) currentSpeed *= inventory.GetSpeedPenalty();

            move = moveDir * currentSpeed;
        }
        else
        {
            // Not moving, stop sprinting
            player.SetIsCurrentlySprinting(false);
            isActuallySprinting = false;
        }

        // Gravity (Y only)
        if (controller.isGrounded)
        {
            velocity.y = 0f;
        }
        else
        {
            velocity.y += gravity * Time.deltaTime;
        }

        // Final move vector: XZ from input, Y from gravity
        Vector3 finalMove = new Vector3(move.x, velocity.y, move.z) * Time.deltaTime;
        controller.Move(finalMove);
    }

    void UpdateAiming()
    {
        // Only allow aiming if a weapon is equipped
        if (equipmentManager == null || equipmentManager.GetCurrentWeapon() == null)
        {
            // Reset to main camera if no weapon
            if (mainCamera != null && aimingCamera != null)
            {
                mainCamera.Priority = 10;
                aimingCamera.Priority = 0;
            }
            return;
        }

        // Switch Cinemachine cameras by priority
        if (mainCamera != null && aimingCamera != null)
        {
            if (player.IsAiming())
            {
                aimingCamera.Priority = 10;
                mainCamera.Priority = 0;
            }
            else
            {
                mainCamera.Priority = 10;
                aimingCamera.Priority = 0;
            }
        }
    }

    void UpdateCrouch()
    {
        float targetHeight = player.IsCrouching() ? crouchHeight : normalHeight;
        controller.height = Mathf.Lerp(controller.height, targetHeight, Time.deltaTime * 5f);

        float heightDifference = normalHeight - targetHeight;
        float targetCenterY = normalHeight / 2f - heightDifference / 2f;
        Vector3 newCenter = controller.center;
        newCenter.y = Mathf.Lerp(newCenter.y, targetCenterY, Time.deltaTime * 5f);
        controller.center = newCenter;
    }

    void UpdateAimFacing()
    {
        if (player == null || !player.IsAiming())
            return;

        if (equipmentManager == null || equipmentManager.GetCurrentWeapon() == null)
            return;

        Quaternion targetRotation = Quaternion.Euler(0f, yaw, 0f);
        float angle = Quaternion.Angle(transform.rotation, targetRotation);
        if (angle <= aimTurnDeadZone)
            return;

        transform.rotation = Quaternion.Slerp(transform.rotation, targetRotation, aimTurnSpeed * Time.deltaTime);
    }

    // ═══════════════════════════════════════════════════════════════
    // PUBLIC CAMERA METHODS
    // ═══════════════════════════════════════════════════════════════

    public void SetCameraSensitivity(float newSensitivity)
    {
        mouseSensitivity = Mathf.Max(0.1f, newSensitivity);
    }

    public void ToggleCursorLock()
    {
        if (Cursor.lockState == CursorLockMode.Locked)
        {
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }
        else
        {
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
        }
    }
}
