using UnityEngine;

public class AnimationController : MonoBehaviour
{
    private Animator animator;
    private Player player;

    private float smoothVelocity = 0f;
    private float smoothAimBlend = 0f;
    private const float IDLE_THRESHOLD = 0.01f;
    private const float VELOCITY_SMOOTH_TIME = 0.1f;
    private const float AIM_SMOOTH_TIME = 0.1f;

    private Gun.GunType lastWeaponType = Gun.GunType.Pistol;
    private bool lastAimingState = false;

    private void Awake()
    {
        animator = GetComponent<Animator>();
        player = GetComponent<Player>();
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
        bool hasWeaponInHand = equipmentManager.GetCurrentWeapon() != null;
        bool isAiming = player.IsAiming() && hasWeaponInHand;

        // Update weapon type booleans
        animator.SetBool("isPistol", currentWeaponType == Gun.GunType.Pistol && hasWeaponInHand);
        animator.SetBool("isAssaultRifle", currentWeaponType == Gun.GunType.AssaultRifle && hasWeaponInHand);
        
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
}
