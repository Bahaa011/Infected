using UnityEngine;
using UnityEngine.Events;
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
    [Header("Ammo & Reload")]
    [SerializeField] private float reloadTime = 1.6f;
    [SerializeField] private bool canFireWhileReloading = false;
    [SerializeField] private string reloadActionName = "Reload";
    [SerializeField] private bool requireMagazineItem = true;
    [Header("Aim Alignment")]
    [Tooltip("Rotate weapon visuals to point toward aim point while aiming.")]
    [SerializeField] private bool rotateWeaponToAim = true;
    [Tooltip("Transform to rotate for visual aiming. Leave empty to rotate this gun transform.")]
    [SerializeField] private Transform aimRotationTransform;
    [Tooltip("Extra euler offset after LookRotation to match prefab forward axis.")]
    [SerializeField] private Vector3 aimRotationOffset = Vector3.zero;
    [SerializeField] private float aimRotationSpeed = 20f;
    [SerializeField] private float maxAimDistance = 250f;
    [SerializeField] private LayerMask aimLayers = ~0;
    
    private InputAction fireAction;
    private InputAction reloadAction;
    
    private float lastFireTime = 0f;
    private int burstCounter = 0;
    private Camera mainCamera;
    private bool isEquipped = false;
    private bool isBurstFiring = false;
    private GunItem gunItem;
    private PlayerSkills playerSkills;
    private Inventory inventory;
    private bool isReloading = false;
    private float reloadProgress = 0f;
    private Player ownerPlayer;
    private Quaternion defaultAimLocalRotation;
    private bool hasDefaultAimRotation;

    [Header("Reload Events")]
    public UnityEvent onReloadStarted;
    public UnityEvent<float> onReloadProgress;
    public UnityEvent onReloadCompleted;

    private void Awake()
    {
    }

    void Start()
    {
        mainCamera = Camera.main;
        ownerPlayer = GetComponentInParent<Player>();

        if (aimRotationTransform == null)
            aimRotationTransform = transform;
        
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
            reloadAction = playerInput.actions.FindAction(reloadActionName);
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

        if (inventory == null)
        {
            inventory = GetComponentInParent<Inventory>();
        }
    }

    void Update()
    {
        if (InventoryUIToolkit.IsInventoryOpen)
            return;

        if (!isEquipped || !gameObject.activeInHierarchy || fireAction == null)
            return;

        if (reloadAction != null && reloadAction.WasPerformedThisFrame())
        {
            Reload();
        }

        if (isReloading && !canFireWhileReloading)
            return;

        if (fireMode == FireMode.Automatic)
        {
            if (fireAction.IsPressed())
                Fire();
        }
        else if (fireMode == FireMode.Burst)
        {
            if (fireAction.WasPerformedThisFrame() && !isBurstFiring)
            {
                burstCounter = 0;
                isBurstFiring = true;
            }

            if (isBurstFiring)
            {
                if (burstCounter < burstCount)
                    Fire();
                else
                {
                    isBurstFiring = false;
                    burstCounter = 0;
                }
            }
        }
        else
        {
            if (fireAction.WasPerformedThisFrame())
                Fire();
        }

        if (fireMode != FireMode.Burst && !fireAction.IsPressed())
            burstCounter = 0;
    }

    private void LateUpdate()
    {
        UpdateVisualAimRotation();
    }

    public void Fire()
    {
        if (!isEquipped || !gameObject.activeInHierarchy || !CanFire())
            return;

        if (gunItem != null && !gunItem.UseAmmo(1))
        {
            // No ammo in magazine
            return;
        }

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

    float GetAdjustedSpread(float baseSpread)
    {
        if (playerSkills == null)
            return baseSpread;
        
        float accuracyBonus = playerSkills.GetAccuracyImprovement(gunType);
        return Mathf.Max(0.1f, baseSpread - accuracyBonus); // Minimum 0.1 degree spread
    }

    Vector3 GetAimDirection()
    {
        Vector3 aimPoint = GetAimPoint();
        Vector3 origin = firePoint != null ? firePoint.position : transform.position;
        Vector3 direction = (aimPoint - origin).normalized;
        if (direction.sqrMagnitude < 0.0001f)
            return transform.forward;
        return direction;
    }

    Vector3 GetAimPoint()
    {
        if (mainCamera == null)
            mainCamera = Camera.main;

        if (mainCamera == null)
            return transform.position + transform.forward * maxAimDistance;

        Ray ray = mainCamera.ScreenPointToRay(new Vector3(Screen.width * 0.5f, Screen.height * 0.5f, 0f));
        if (Physics.Raycast(ray, out RaycastHit hit, maxAimDistance, aimLayers, QueryTriggerInteraction.Ignore))
            return hit.point;

        return ray.origin + ray.direction * maxAimDistance;
    }

    void UpdateVisualAimRotation()
    {
        if (!rotateWeaponToAim || !isEquipped || !gameObject.activeInHierarchy)
            return;

        if (aimRotationTransform == null)
            aimRotationTransform = transform;

        if (!hasDefaultAimRotation)
        {
            defaultAimLocalRotation = aimRotationTransform.localRotation;
            hasDefaultAimRotation = true;
        }

        if (ownerPlayer == null)
            ownerPlayer = GetComponentInParent<Player>();

        bool shouldAim = ownerPlayer != null && ownerPlayer.IsAiming();
        if (!shouldAim)
        {
            aimRotationTransform.localRotation = Quaternion.Slerp(
                aimRotationTransform.localRotation,
                defaultAimLocalRotation,
                aimRotationSpeed * Time.deltaTime
            );
            return;
        }

        Vector3 aimPoint = GetAimPoint();
        Vector3 lookDirection = aimPoint - aimRotationTransform.position;
        if (lookDirection.sqrMagnitude < 0.0001f)
            return;

        Quaternion targetWorldRotation = Quaternion.LookRotation(lookDirection.normalized, Vector3.up)
                                         * Quaternion.Euler(aimRotationOffset);

        aimRotationTransform.rotation = Quaternion.Slerp(
            aimRotationTransform.rotation,
            targetWorldRotation,
            aimRotationSpeed * Time.deltaTime
        );
    }

    public void SetFireMode(FireMode mode)
    {
        fireMode = mode;
        burstCounter = 0;
        isBurstFiring = false;
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

        if (aimRotationTransform == null)
            aimRotationTransform = transform;
        defaultAimLocalRotation = aimRotationTransform.localRotation;
        hasDefaultAimRotation = true;
        
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
                reloadAction = playerInput.actions.FindAction(reloadActionName);
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

        if (inventory == null)
        {
            inventory = GetComponentInParent<Inventory>();
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
        isBurstFiring = false;
        isReloading = false;
        if (aimRotationTransform != null && hasDefaultAimRotation)
            aimRotationTransform.localRotation = defaultAimLocalRotation;
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

    public void Reload()
    {
        if (isReloading)
            return;

        if (gunItem == null)
            return;

        if (gunItem.CurrentAmmo >= gunItem.AmmoCapacity)
            return;

        if (requireMagazineItem)
        {
            if (inventory == null)
                inventory = GetComponentInParent<Inventory>();

            if (!TryConsumeMagazine())
                return;
        }

        StartCoroutine(ReloadRoutine());
    }

    private System.Collections.IEnumerator ReloadRoutine()
    {
        isReloading = true;
        reloadProgress = 0f;
        onReloadStarted?.Invoke();

        if (animator != null)
        {
            animator.SetTrigger("reload");
        }

        float elapsed = 0f;
        while (elapsed < reloadTime)
        {
            elapsed += Time.deltaTime;
            reloadProgress = Mathf.Clamp01(elapsed / reloadTime);
            onReloadProgress?.Invoke(reloadProgress);
            yield return null;
        }

        gunItem.SetAmmo(gunItem.AmmoCapacity);
        isReloading = false;
        reloadProgress = 1f;
        onReloadProgress?.Invoke(reloadProgress);
        onReloadCompleted?.Invoke();
    }

    private bool TryConsumeMagazine()
    {
        if (inventory == null)
            return false;

        MagazineItem magazine = FindMagazineForGunType();
        if (magazine == null)
            return false;

        return inventory.RemoveItem(magazine, 1);
    }

    private MagazineItem FindMagazineForGunType()
    {
        if (inventory == null)
            return null;

        var slots = inventory.GetAllItems();
        for (int i = 0; i < slots.Count; i++)
        {
            var slot = slots[i];
            if (slot.item is MagazineItem mag && slot.quantity > 0 && mag.GunType == gunType)
            {
                return mag;
            }
        }

        return null;
    }

    public bool IsReloading() => isReloading;
    public float GetReloadProgress() => reloadProgress;
}
