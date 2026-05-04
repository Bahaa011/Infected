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
    [Header("Running Camera Shake")]
    [SerializeField] private float runShakeAmplitude = 0.06f;
    [SerializeField] private float runShakeFrequency = 10f;
    [SerializeField] private float runShakeSmooth = 10f;

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
    [SerializeField] private float crouchCameraHeightOffset = 0.45f;
    [SerializeField] private float cameraHeightSmooth = 8f;
    [SerializeField] private InputActionReference sprintAction;

    [Header("Stealth Noise")]
    [SerializeField] private float walkNoiseRadius = 7f;
    [SerializeField] private float sprintNoiseRadius = 13f;
    [SerializeField] private float crouchNoiseRadius = 3f;
    [SerializeField] private float footstepNoiseInterval = 0.38f;

    [Header("Footstep Audio")]
    [SerializeField] private AudioClip[] walkClips;
    [SerializeField] private AudioClip[] runClips;
    [SerializeField] private AudioClip[] crouchWalkClips;
    [SerializeField] private float footstepVolume = 0.8f;

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
    private bool wasMoving = false;
    private AudioSource footstepAudioSource;

    // Camera
    private float yaw;
    private float pitch;
    private float lockedYaw;
    private bool cameraYawLocked = false;
    private Vector3 cameraLocalPosition;
    private float baseCameraHeight;
    private float currentCameraHeight;
    private float runShakeTimer;
    private float currentShakeAmplitude;
    private float currentShakeOffsetY;
    private float nextFootstepNoiseTime;

    private void Awake()
    {
        controller = GetComponent<CharacterController>();
        player = GetComponent<Player>();
        inventory = GetComponent<Inventory>();
        animationController = GetComponent<AnimationController>();
        equipmentManager = GetComponent<EquipmentManager>();
        hasSprintAction = sprintAction != null;
        originalMouseSensitivity = mouseSensitivity;
        
        // Create AudioSource for footsteps
        footstepAudioSource = GetComponent<AudioSource>();
        if (footstepAudioSource == null)
            footstepAudioSource = gameObject.AddComponent<AudioSource>();
        footstepAudioSource.playOnAwake = false;
        footstepAudioSource.spatialBlend = 0f;
    }

    private void Start()
    {
        // Only lock cursor if menu didn't already
        if (Cursor.lockState != CursorLockMode.Locked)
        {
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
        }

        if (cameraTarget != null)
        {
            baseCameraHeight = cameraTarget.localPosition.y;
            currentCameraHeight = baseCameraHeight;
        }

        ResolveCameraReferences();
    }

    private void OnValidate()
    {
        // Ensure serialized action refs are visible in editor sanity checks
    }


    void OnEnable()
    {
        // Enable ALL action maps
        try
        {
            var pi = GetComponent<UnityEngine.InputSystem.PlayerInput>();
            if (pi == null && player != null)
                pi = player.GetComponent<UnityEngine.InputSystem.PlayerInput>();
            
            if (pi != null)
            {
                UnityEngine.Debug.Log($"[ThirdPersonController] OnEnable: Found {pi.actions.actionMaps.Count} action maps");
                foreach (var map in pi.actions.actionMaps)
                {
                    UnityEngine.Debug.Log($"  - Action map: {map.name}");
                    if (!map.enabled)
                        map.Enable();
                }
            }
        }
        catch (System.Exception ex)
        {
            UnityEngine.Debug.LogWarning($"[ThirdPersonController] OnEnable action map setup error: {ex.Message}");
        }

        if (sprintAction != null && sprintAction.action != null)
            sprintAction.action.Enable();
        
        if (lookAction != null && lookAction.action != null)
        {
            if (!InventoryUIToolkit.IsInventoryOpen)
                lookAction.action.Enable();
            else
                lookAction.action.Disable();
        }

        // Subscribe to world loading state changes so we can enable input when ready
        try
        {
            WorldLoadingState.OnChanged += OnWorldLoadingChanged;
        }
        catch (System.Exception)
        {
            // In case namespace/type differences, fallback to direct checks
        }

        UnityEngine.Debug.Log($"[ThirdPersonController] OnEnable. IsWorldReady={WorldLoadingState.IsWorldReady}");
        
        // Log PlayerInput state
        try
        {
            var pi = GetComponent<UnityEngine.InputSystem.PlayerInput>();
            if (pi == null && player != null)
                pi = player.GetComponent<UnityEngine.InputSystem.PlayerInput>();
            
            if (pi != null)
            {
                UnityEngine.Debug.Log($"[ThirdPersonController] OnEnable PlayerInput status: Enabled={pi.enabled}, ActionMaps={pi.actions.actionMaps.Count}, Active={pi.currentActionMap?.name}");
            }
            else
            {
                UnityEngine.Debug.LogWarning("[ThirdPersonController] OnEnable: PlayerInput not found!");
            }
        }
        catch { }

        // If the world is already ready by the time this component enables, ensure inputs are enabled
        if (WorldLoadingState.IsWorldReady)
            OnWorldLoadingChanged(true);
    }

    void OnDisable()
    {
        if (sprintAction != null)
            sprintAction.action.Disable();
        if (lookAction != null)
            lookAction.action.Disable();
        try { WorldLoadingState.OnChanged -= OnWorldLoadingChanged; } catch (System.Exception) { }
    }

    private void OnWorldLoadingChanged(bool ready)
    {
        UnityEngine.Debug.Log($"[ThirdPersonController] World loading changed -> ready={ready}");
        if (ready)
        {
            // Make sure cursor is locked and hidden when gameplay resumes
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;

            // Ensure PlayerInput component is enabled and ALL action maps are active
            try
            {
                var pi = GetComponent<UnityEngine.InputSystem.PlayerInput>();
                if (pi == null && player != null)
                    pi = player.GetComponent<UnityEngine.InputSystem.PlayerInput>();
                
                if (pi != null)
                {
                    UnityEngine.Debug.Log($"[ThirdPersonController] Found PlayerInput. Enabled={pi.enabled}");
                    
                    if (!pi.enabled)
                    {
                        pi.enabled = true;
                        UnityEngine.Debug.Log("[ThirdPersonController] Enabled PlayerInput component.");
                    }
                    
                    // Enable ALL action maps (gameplay + UI)
                    foreach (var map in pi.actions.actionMaps)
                    {
                        if (!map.enabled)
                        {
                            map.Enable();
                            UnityEngine.Debug.Log($"[ThirdPersonController] Enabled action map '{map.name}'");
                        }
                    }

                    // Enable ALL individual actions in the input system
                    foreach (var action in pi.actions)
                    {
                        if (!action.enabled)
                        {
                            action.Enable();
                            UnityEngine.Debug.Log($"[ThirdPersonController] Enabled action '{action.name}'");
                        }
                    }
                }
                else
                {
                    UnityEngine.Debug.LogError("[ThirdPersonController] PlayerInput component not found!");
                }
            }
            catch (System.Exception ex)
            {
                UnityEngine.Debug.LogError("[ThirdPersonController] Failed to enable PlayerInput/actions: " + ex.Message);
            }

            // Also ensure specific action references are enabled
            if (sprintAction != null && sprintAction.action != null && !sprintAction.action.enabled)
            {
                sprintAction.action.Enable();
                UnityEngine.Debug.Log("[ThirdPersonController] Re-enabled sprintAction");
            }
            
            if (lookAction != null && lookAction.action != null && !lookAction.action.enabled)
            {
                lookAction.action.Enable();
                UnityEngine.Debug.Log("[ThirdPersonController] Re-enabled lookAction");
            }

            // Enable inventory toggle action if available
            try
            {
                var inventoryUI = FindAnyObjectByType<InventoryUIToolkit>();
                if (inventoryUI != null)
                {
                    UnityEngine.Debug.Log("[ThirdPersonController] Found InventoryUIToolkit, will trigger rebind");
                }
            }
            catch { }
        }
    }

    public void OnMove(InputValue value)
    {
        moveInput = value.Get<Vector2>();
    }

    private void Update()
    {
        if (!WorldLoadingState.IsWorldReady)
            return;

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
        UpdateRunCameraShake();
        if (animationController != null)
        {
            animationController.UpdateMovementAnimation(moveInput.sqrMagnitude, isActuallySprinting, runSpeedMultiplier);
            animationController.UpdateCrouchAnimation();
            animationController.UpdateAimingAnimation();
        }
    }

    private void LateUpdate()
    {
        if (!WorldLoadingState.IsWorldReady)
            return;

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

        float targetCameraHeight = baseCameraHeight;
        if (player != null && player.IsCrouching())
            targetCameraHeight = Mathf.Max(0.2f, baseCameraHeight - crouchCameraHeightOffset);
        currentCameraHeight = Mathf.Lerp(currentCameraHeight, targetCameraHeight, cameraHeightSmooth * Time.deltaTime);

        // Follow player XZ position, keep original Y position
        Vector3 targetPos = cameraTarget.position;
        targetPos.x = transform.position.x;
        targetPos.z = transform.position.z;
        targetPos.y = transform.position.y + currentCameraHeight + currentShakeOffsetY;
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
        if (!WorldLoadingState.IsWorldReady)
            return;

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

            bool shouldFaceCamera = player != null && player.IsAiming();
            if (shouldFaceCamera)
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

        EmitMovementNoise(isMoving);
        
        // Stop audio when movement stops
        if (!isMoving && wasMoving)
        {
            if (footstepAudioSource != null)
                footstepAudioSource.Stop();
        }
        wasMoving = isMoving;
    }

    private void EmitMovementNoise(bool isMoving)
    {
        if (!isMoving || controller == null || !controller.isGrounded)
        {
            return;
        }

        if (Time.time < nextFootstepNoiseTime)
            return;

        float radius = walkNoiseRadius;
        float strength = 0.75f;
        bool isCrouching = false;
        bool isRunning = false;

        if (player != null)
        {
            if (player.IsCrouching())
            {
                radius = crouchNoiseRadius;
                strength = 0.35f;
                isCrouching = true;
            }
            else if (player.IsCurrentlySprinting())
            {
                radius = sprintNoiseRadius;
                strength = 1f;
                isRunning = true;
            }
        }

        ZombieNoiseSystem.EmitNoise(transform.position, radius, strength, ZombieNoiseSystem.NoiseType.Footstep);
        
        // Play footstep audio based on movement mode
        PlayFootstepAudio(isCrouching, isRunning);
        
        nextFootstepNoiseTime = Time.time + footstepNoiseInterval;
    }

    private void PlayFootstepAudio(bool isCrouching, bool isRunning)
    {
        if (footstepAudioSource == null)
            return;

        AudioClip clip = null;

        if (isCrouching && crouchWalkClips != null && crouchWalkClips.Length > 0)
        {
            clip = crouchWalkClips[Random.Range(0, crouchWalkClips.Length)];
        }
        else if (isRunning && runClips != null && runClips.Length > 0)
        {
            clip = runClips[Random.Range(0, runClips.Length)];
        }
        else if (walkClips != null && walkClips.Length > 0)
        {
            clip = walkClips[Random.Range(0, walkClips.Length)];
        }

        if (clip != null)
        {
            footstepAudioSource.clip = clip;
            footstepAudioSource.volume = footstepVolume;
            footstepAudioSource.Play();
        }
    }

    void UpdateAiming()
    {
        if (mainCamera == null || aimingCamera == null)
            ResolveCameraReferences();

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

    private void ResolveCameraReferences()
    {
        if (mainCamera != null && aimingCamera != null)
            return;

        CinemachineCamera[] cameras = FindObjectsByType<CinemachineCamera>(FindObjectsSortMode.None);
        for (int i = 0; i < cameras.Length; i++)
        {
            CinemachineCamera cam = cameras[i];
            if (cam == null)
                continue;

            if (mainCamera == null && cam.name.Contains("Third Person Camera"))
                mainCamera = cam;
            else if (aimingCamera == null && cam.name.Contains("Aiming Camera"))
                aimingCamera = cam;
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
        if (player == null)
            return;

        bool shouldFaceCamera = player.IsAiming();
        if (!shouldFaceCamera)
            return;

        Quaternion targetRotation = Quaternion.Euler(0f, yaw, 0f);
        float angle = Quaternion.Angle(transform.rotation, targetRotation);
        if (angle <= aimTurnDeadZone)
            return;

        transform.rotation = Quaternion.Slerp(transform.rotation, targetRotation, aimTurnSpeed * Time.deltaTime);
    }

    void UpdateRunCameraShake()
    {
        bool shouldShake = isActuallySprinting && controller != null && controller.isGrounded;
        float targetAmplitude = shouldShake ? runShakeAmplitude : 0f;
        currentShakeAmplitude = Mathf.Lerp(currentShakeAmplitude, targetAmplitude, runShakeSmooth * Time.deltaTime);

        if (currentShakeAmplitude > 0.0001f)
        {
            runShakeTimer += Time.deltaTime * runShakeFrequency;
            currentShakeOffsetY = Mathf.Sin(runShakeTimer) * currentShakeAmplitude;
        }
        else
        {
            currentShakeOffsetY = Mathf.Lerp(currentShakeOffsetY, 0f, runShakeSmooth * Time.deltaTime);
            runShakeTimer = 0f;
        }
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
