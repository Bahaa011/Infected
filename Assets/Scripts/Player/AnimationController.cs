using UnityEngine;

public class AnimationController : MonoBehaviour
{
    private Animator animator;
    private Player player;
    private Camera mainCamera;
    private EquipmentManager equipmentManager;

    private float smoothVelocity = 0f;
    private float smoothAimBlend = 0f;
    private const float IDLE_THRESHOLD = 0.01f;
    private const float VELOCITY_SMOOTH_TIME = 0.1f;
    private const float AIM_SMOOTH_TIME = 0.1f;

    private Gun.GunType lastWeaponType = Gun.GunType.Pistol;
    private bool lastAimingState = false;

    [Header("Animation Layers")]
    [SerializeField] private int weaponLayerIndex = 1;

    [Header("Aim IK")]
    [SerializeField] private bool enableAimIK = true;
    [SerializeField] private float ikLookAtWeight = 0.65f;
    [SerializeField] private float ikBodyWeight = 0.35f;
    [SerializeField] private float ikHeadWeight = 0.8f;
    [SerializeField] private float ikEyesWeight = 0.8f;
    [SerializeField] private float ikClampWeight = 0.5f;
    [SerializeField] private float ikAimDistance = 120f;

    private void Awake()
    {
        animator = GetComponent<Animator>();
        player = GetComponent<Player>();
        equipmentManager = GetComponent<EquipmentManager>();
        mainCamera = Camera.main;
    }

    private void Start()
    {
        InitializeAnimationState();
    }

    private void InitializeAnimationState()
    {
        if (animator == null) return;

        animator.SetBool("isPistol", false);
        animator.SetBool("isAssaultRifle", false);
        animator.SetBool("isCrouching", false);
        animator.SetBool("isBrawling", false);
        animator.SetFloat("Speed", 0f);
        animator.SetFloat("AimBlend", 0f);
    }

    public void UpdateMovementAnimation(float inputMagnitude, bool isRunning, float runSpeedMultiplier = 2f)
    {
        if (animator == null) return;

        float magnitude = Mathf.Max(0f, inputMagnitude);
        float targetSpeed = 0f;

        if (magnitude > IDLE_THRESHOLD)
        {
            targetSpeed = isRunning ? runSpeedMultiplier : magnitude;
        }

        // Frame-rate independent smoothing toward the target speed without overshooting below 0
        float dampRate = 1f / Mathf.Max(VELOCITY_SMOOTH_TIME, 0.0001f);
        smoothVelocity = Mathf.MoveTowards(smoothVelocity, targetSpeed, dampRate * Time.deltaTime);
        smoothVelocity = Mathf.Max(0f, smoothVelocity);
        animator.SetFloat("Speed", smoothVelocity);
    }

    public void UpdateCrouchAnimation()
    {
        if (animator == null) return;

        animator.SetBool("isCrouching", player.IsCrouching());
    }

    public void UpdateAimingAnimation()
    {
        if (animator == null || player == null) return;

        var equipmentManager = player.GetEquipmentManager();
        Gun.GunType currentWeaponType = equipmentManager.GetCurrentGunType();
        bool hasGunInHand = equipmentManager.GetCurrentWeapon() != null;
        bool hasMeleeInHand = equipmentManager.IsMeleeEquipped();
        bool hasWeaponInHand = hasGunInHand || hasMeleeInHand;
        bool isAiming = player.IsAiming() && hasWeaponInHand;
        bool isBrawling = player.IsBrawling();

        animator.SetBool("isBrawling", isBrawling);

        // Keep upper-body/weapon layer active while brawling so unarmed animations are visible.
        if (weaponLayerIndex >= 0)
        {
            float targetLayerWeight = (hasWeaponInHand || isBrawling) ? 1f : 0f;
            animator.SetLayerWeight(weaponLayerIndex, targetLayerWeight);
        }

        // Update weapon type booleans
        bool allowWeaponPose = !isBrawling && hasGunInHand;
        animator.SetBool("isPistol", currentWeaponType == Gun.GunType.Pistol && allowWeaponPose);
        animator.SetBool("isAssaultRifle", currentWeaponType == Gun.GunType.AssaultRifle && allowWeaponPose);
        
        // Update aim blend for 1D blend tree (0 = idle, 1 = aiming)
        float targetAimBlend = isAiming ? 1f : 0f;
        float dampRate = 1f / Mathf.Max(AIM_SMOOTH_TIME, 0.0001f);
        smoothAimBlend = Mathf.MoveTowards(smoothAimBlend, targetAimBlend, dampRate * Time.deltaTime);
        animator.SetFloat("AimBlend", smoothAimBlend);
        
        lastWeaponType = currentWeaponType;
        lastAimingState = isAiming;
    }

    public float GetSmoothVelocity() => smoothVelocity;
    public float GetAimBlend() => smoothAimBlend;

    public void TriggerBrawlPunch()
    {
        if (animator == null || player == null || !player.IsBrawling())
            return;

        animator.SetTrigger("Punch");
    }

    private void OnAnimatorIK(int layerIndex)
    {
        if (animator == null || player == null || !enableAimIK)
            return;

        if (mainCamera == null)
            mainCamera = Camera.main;

        if (mainCamera == null)
            return;

        if (equipmentManager == null)
            equipmentManager = player.GetEquipmentManager();

        bool hasGunInHand = equipmentManager != null && equipmentManager.GetCurrentWeapon() != null;
        bool shouldAimIK = player.IsAiming() && hasGunInHand;

        if (!shouldAimIK)
        {
            animator.SetLookAtWeight(0f);
            return;
        }

        Ray ray = mainCamera.ScreenPointToRay(new Vector3(Screen.width * 0.5f, Screen.height * 0.5f, 0f));
        Vector3 lookTarget = ray.origin + ray.direction * ikAimDistance;

        animator.SetLookAtWeight(ikLookAtWeight, ikBodyWeight, ikHeadWeight, ikEyesWeight, ikClampWeight);
        animator.SetLookAtPosition(lookTarget);
    }
}
