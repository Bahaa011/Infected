using UnityEngine;
using UnityEngine.InputSystem;

public class Gun : MonoBehaviour
{
    public enum FireMode { Single, Automatic, Burst, Shotgun }
    public enum GunType { Pistol, AssaultRifle, Shotgun, Sniper }

    [SerializeField] private GunType gunType = GunType.Pistol;
    [SerializeField] private FireMode fireMode = FireMode.Single;
    [SerializeField] private GameObject bulletPrefab;
    [SerializeField] private Transform firePoint;
    [SerializeField] private float fireRate = 0.1f;
    [SerializeField] private float burstCount = 3;
    [SerializeField] private float shotgunPellets = 8;
    [SerializeField] private float shotgunSpread = 15f;
    [SerializeField] private float automaticSpread = 5f;
    [SerializeField] private Animator animator;
    [SerializeField] private int animatorLayerIndex = 1;
    
    private InputAction fireAction;
    
    private float lastFireTime = 0f;
    private int burstCounter = 0;
    private Camera mainCamera;
    private bool isEquipped = false;
    private GunItem gunItem;
    private PlayerSkills playerSkills;

    private void Awake()
    {
    }

    void Start()
    {
        mainCamera = Camera.main;
        
        // If animator is not set in inspector, try to find it on the parent (player)
        if (animator == null)
        {
            animator = GetComponentInParent<Animator>();
        }
        
        // Get the Attack action from the Player's InputActionMap
        var playerInput = GetComponentInParent<PlayerInput>();
        if (playerInput != null)
        {
            fireAction = playerInput.actions.FindAction("Attack");
        }
        else
        {
            Debug.LogError("Could not find PlayerInput component!");
        }
        
        // Get PlayerSkills reference
        if (playerSkills == null)
        {
            playerSkills = GetComponentInParent<PlayerSkills>();
        }
    }

    void Update()
    {
        if (!isEquipped || !gameObject.activeInHierarchy || fireAction == null)
            return;

        if (fireMode == FireMode.Automatic)
        {
            if (fireAction.IsPressed())
                Fire();
        }
        else if (fireMode == FireMode.Burst)
        {
            if (fireAction.WasPerformedThisFrame())
                burstCounter = 0;
            
            if (burstCounter < burstCount)
                Fire();
        }
        else
        {
            if (fireAction.WasPerformedThisFrame())
                Fire();
        }

        if (!fireAction.IsPressed())
            burstCounter = 0;
    }

    public void Fire()
    {
        if (!isEquipped || !gameObject.activeInHierarchy || !CanFire())
            return;

        // Register shot with skill system
        if (playerSkills != null)
        {
            playerSkills.RegisterShot(gunType, false, false);
            Debug.Log($"[Gun] Shot registered for {gunType}");
        }
        else
        {
            Debug.LogWarning("[Gun] PlayerSkills is null, cannot register shot!");
        }

        switch (fireMode)
        {
            case FireMode.Single:
                FireSingle();
                break;
            case FireMode.Automatic:
                FireAutomatic();
                break;
            case FireMode.Burst:
                FireBurst();
                break;
            case FireMode.Shotgun:
                FireShotgun();
                break;
        }
        lastFireTime = Time.time;
    }

    bool CanFire()
    {
        return Time.time - lastFireTime >= fireRate;
    }

    void FireSingle()
    {
        Vector3 aimDirection = GetAimDirection();
        SpawnBullet(aimDirection);
    }

    void FireAutomatic()
    {
        Vector3 aimDirection = GetAimDirection();
        float spread = GetAdjustedSpread(automaticSpread);
        Vector3 spreadDirection = GetSpreadDirection(aimDirection, spread);
        SpawnBullet(spreadDirection);
    }

    void FireBurst()
    {
        Vector3 aimDirection = GetAimDirection();
        SpawnBullet(aimDirection);
        burstCounter++;
    }

    void FireShotgun()
    {
        Vector3 aimDirection = GetAimDirection();
        float spread = GetAdjustedSpread(shotgunSpread);
        for (int i = 0; i < shotgunPellets; i++)
        {
            Vector3 spreadDirection = GetSpreadDirection(aimDirection, spread);
            SpawnBullet(spreadDirection);
        }
    }

    void SpawnBullet(Vector3 direction)
    {
        GameObject bulletObject = Instantiate(bulletPrefab, firePoint.position, Quaternion.identity);
        Bullet bullet = bulletObject.GetComponent<Bullet>();
        if (bullet != null)
        {
            bullet.Initialize(direction);
        }
    }

    Vector3 GetSpreadDirection(Vector3 baseDirection, float spread)
    {
        float randomX = Random.Range(-spread, spread);
        float randomY = Random.Range(-spread, spread);
        Quaternion spreadRotation = Quaternion.Euler(randomX, randomY, 0);
        return spreadRotation * baseDirection;
    }

    /// <summary>
    /// Get the adjusted spread based on player skill level
    /// </summary>
    float GetAdjustedSpread(float baseSpread)
    {
        if (playerSkills == null)
            return baseSpread;
        
        float accuracyBonus = playerSkills.GetAccuracyImprovement(gunType);
        return Mathf.Max(0.1f, baseSpread - accuracyBonus); // Minimum 0.1 degree spread
    }

    Vector3 GetAimDirection()
    {
        Ray ray = mainCamera.ScreenPointToRay(new Vector3(Screen.width / 2f, Screen.height / 2f, 0));
        return ray.direction;
    }

    public void SetFireMode(FireMode mode)
    {
        fireMode = mode;
        burstCounter = 0;
    }

    public FireMode GetFireMode()
    {
        return fireMode;
    }

    public GunType GetGunType()
    {
        return gunType;
    }

    public void SetGunType(GunType type)
    {
        gunType = type;
    }

    public void Equip()
    {
        isEquipped = true;
        
        // Re-fetch animator if not set (happens when instantiated at runtime)
        if (animator == null)
        {
            animator = GetComponentInParent<Animator>();
        }
        
        // Re-fetch fire action if not set
        if (fireAction == null)
        {
            var playerInput = GetComponentInParent<PlayerInput>();
            if (playerInput != null)
            {
                fireAction = playerInput.actions.FindAction("Attack");
            }
        }
        
        // Re-fetch PlayerSkills if not set (happens when instantiated at runtime)
        if (playerSkills == null)
        {
            playerSkills = GetComponentInParent<PlayerSkills>();
            if (playerSkills == null)
            {
                Debug.LogWarning($"Gun {gameObject.name} could not find PlayerSkills component!");
            }
        }
        
        if (animator != null)
        {
            animator.SetBool("HasWeapon", true);
            animator.SetLayerWeight(animatorLayerIndex, 1f);
            Debug.Log($"Gun {gameObject.name} Equipped - Setting layer weight index {animatorLayerIndex} to 1f");
            
            // Set weapon-specific animation parameters
            bool isPistol = gunType == GunType.Pistol;
            bool isAssault = gunType == GunType.AssaultRifle;
            
            animator.SetBool("isPistol", isPistol);
            animator.SetBool("isAssault", isAssault);
            animator.SetBool("isAssaultRifle", isAssault);
            
            Debug.Log($"Gun {gameObject.name} - GunType: {gunType}, isPistol: {isPistol}, isAssaultRifle: {isAssault}");
        }
        else
        {
            Debug.LogWarning($"Gun {gameObject.name} has no animator!");
        }
        
        Debug.Log($"Gun {gameObject.name} Equip complete. isEquipped={isEquipped}, fireAction={fireAction != null}");
    }

    public void Unequip()
    {
        isEquipped = false;
        burstCounter = 0;
        if (animator != null)
        {
            animator.SetBool("HasWeapon", false);
            animator.SetLayerWeight(animatorLayerIndex, 0f);
            Debug.Log($"Gun {gameObject.name} Unequipped - Setting layer weight index {animatorLayerIndex} to 0f");
            
            // Reset weapon-specific animation parameters
            animator.SetBool("isPistol", false);
            animator.SetBool("isAssault", false);
            animator.SetBool("isAssaultRifle", false);
        }
    }

    public void SetGunItem(GunItem item)
    {
        gunItem = item;
    }

    public GunItem GetGunItem()
    {
        return gunItem;
    }
}
