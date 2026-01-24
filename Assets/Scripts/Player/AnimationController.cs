using UnityEngine;

public class AnimationController : MonoBehaviour
{
    private Animator animator;
    private Player player;

    private float smoothVelocity = 0f;
    private const float IDLE_THRESHOLD = 0.01f;
    private const float VELOCITY_SMOOTH_TIME = 0.1f;

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
        animator.SetBool("isAssault", false);
        animator.SetFloat("aim", 0f);
        animator.SetBool("isCrouching", false);
        animator.SetFloat("Speed", 0f);
    }

    public void UpdateMovementAnimation(float inputMagnitude, bool isRunning, float runSpeedMultiplier = 2f)
    {
        if (animator == null) return;

        float targetSpeed = 0f;

        if (inputMagnitude > IDLE_THRESHOLD)
        {
            targetSpeed = 1f;

            if (isRunning)
            {
                targetSpeed = runSpeedMultiplier;
            }
        }

        float smoothSpeed = Mathf.Lerp(smoothVelocity, targetSpeed, VELOCITY_SMOOTH_TIME / Time.deltaTime * Time.deltaTime);
        animator.SetFloat("Speed", smoothSpeed);
        smoothVelocity = smoothSpeed;
    }

    public void UpdateCrouchAnimation()
    {
        if (animator == null) return;

        animator.SetBool("isCrouching", player.IsCrouching());
    }

    public void UpdateAimingAnimation()
    {
        if (animator == null || player == null) return;

        Gun.GunType currentWeaponType = player.GetEquipmentManager().GetCurrentGunType();
        bool isAiming = player.IsAiming();

        if (currentWeaponType != lastWeaponType)
        {
            animator.SetBool("isPistol", currentWeaponType == Gun.GunType.Pistol);
            animator.SetBool("isAssault", currentWeaponType == Gun.GunType.AssaultRifle);
            lastWeaponType = currentWeaponType;
        }

        if (isAiming != lastAimingState)
        {
            animator.SetFloat("aim", isAiming ? 1f : 0f);
            lastAimingState = isAiming;
        }
    }

    public void SetDeadAnimation(bool isDead)
    {
        if (animator == null) return;

        animator.SetBool("isDead", isDead);
    }

    public float GetSmoothVelocity() => smoothVelocity;
}
