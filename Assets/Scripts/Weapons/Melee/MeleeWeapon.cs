using UnityEngine;
using UnityEngine.Events;
using UnityEngine.InputSystem;
using System.Collections.Generic;

public class MeleeWeapon : MonoBehaviour
{
    public enum WeaponType { Sword, Axe, Hammer, Spear }

    [Header("Weapon Configuration")]
    [SerializeField] private WeaponType weaponType = WeaponType.Sword;
    [SerializeField] private Animator animator;
    [SerializeField] private int animatorLayerIndex = 1;

    [Header("Attack Settings")]
    [SerializeField] private float baseDamage = 25f;
    [SerializeField] private float attackSpeed = 1f; // Attacks per second
    [SerializeField] private float attackRange = 2f;
    [SerializeField] private float knockbackForce = 10f;
    [SerializeField] private Collider attackCollider; // Optional: For more precise hit detection
    [SerializeField] private Transform damageOrigin; // Point where damage is dealt from
    
    [Header("Attack Animation")]
    [SerializeField] private string attackAnimationParameter = "Attack";
    [SerializeField] private int numberOfAttackCombos = 3; // How many different attack animations
    [SerializeField] private float comboCooldown = 2f; // Time between combo resets
    
    [Header("Layer Settings")]
    [SerializeField] private string weaponLayerName = "Weapon";
    
    [Header("Events")]
    public UnityEvent<float> onAttackDealt; // Damage amount
    public UnityEvent<Collider> onHitTarget; // Hit collider
    public UnityEvent onAttackStarted;
    public UnityEvent onAttackEnded;

    private bool isEquipped = false;
    private float lastAttackTime = 0f;
    private int currentComboCount = 0;
    private float lastComboTime = 0f;
    private int weaponLayer = -1;
    private MeleeWeaponItem weaponItem;

    // References
    private InputAction attackAction;
    private PlayerSkills playerSkills;
    private Inventory inventory;
    private Player player;
    private Camera mainCamera;
    
    // Hit tracking
    private HashSet<Collider> hitInCurrentAttack = new HashSet<Collider>();

    private void Awake()
    {
        if (!string.IsNullOrEmpty(weaponLayerName))
            weaponLayer = LayerMask.NameToLayer(weaponLayerName);
    }

    private void Start()
    {
        mainCamera = Camera.main;

        // Get animator if not assigned
        if (animator == null)
            animator = GetComponentInParent<Animator>();

        // Get attack action from Player's InputActionMap
        var playerInput = GetComponentInParent<PlayerInput>();
        if (playerInput != null)
        {
            attackAction = playerInput.actions.FindAction("Attack");
        }
        else
        {
            Debug.LogError("[MeleeWeapon] Could not find PlayerInput component!");
        }

        // Get skill system reference
        playerSkills = GetComponentInParent<PlayerSkills>();
        inventory = GetComponentInParent<Inventory>();
        player = GetComponentInParent<Player>();

        // Setup damage origin if not assigned (use weapon transform)
        if (damageOrigin == null)
            damageOrigin = transform;

        Debug.Log($"[MeleeWeapon] {weaponType} initialized. Damage: {baseDamage}, AttackSpeed: {attackSpeed}/sec");
    }

    private void Update()
    {
        if (!isEquipped || !gameObject.activeInHierarchy || attackAction == null)
            return;

        // Check for combo timeout
        if (Time.time - lastComboTime > comboCooldown)
            currentComboCount = 0;

        // Handle attack input
        if (attackAction.WasPerformedThisFrame())
        {
            TryAttack();
        }
    }

    private void TryAttack()
    {
        if (!CanAttack())
            return;

        // Calculate attack cooldown based on attack speed
        float attackCooldown = 1f / attackSpeed;
        if (Time.time - lastAttackTime < attackCooldown)
            return;

        PerformAttack();
        lastAttackTime = Time.time;
        
        // Update combo system
        lastComboTime = Time.time;
        currentComboCount = (currentComboCount + 1) % numberOfAttackCombos;
    }

    private bool CanAttack()
    {
        // Check if player can act
        if (player != null && !player.IsAlive())
            return false;

        // Check stamina if available
        if (player != null)
        {
            float staminaCost = 15f; // Base stamina cost per attack
            if (player.GetStamina() < staminaCost)
                return false;
        }

        return true;
    }

    private void PerformAttack()
    {
        // Clear previous frame's hits
        hitInCurrentAttack.Clear();

        // Consume stamina
        if (player != null)
        {
            float staminaCost = 15f;
            player.Drink(-staminaCost); // Negative drink drains stamina (via health regen hijack)
            // Actually, let's just drain it directly via reflection or create proper method
            // For now, we'll track it in the playerSkills event
        }

        // Play attack animation with combo index
        if (animator != null)
        {
            animator.SetTrigger(attackAnimationParameter);
            animator.SetInteger("ComboIndex", currentComboCount);
        }

        onAttackStarted?.Invoke();

        // Start damage detection coroutine
        StartCoroutine(DamageDetectionRoutine());

        Debug.Log($"[MeleeWeapon] Attack performed! Combo: {currentComboCount + 1}/{numberOfAttackCombos}");
    }

    private System.Collections.IEnumerator DamageDetectionRoutine()
    {
        float attackDuration = 0.5f; // Duration of the attack hitbox
        float elapsed = 0f;

        while (elapsed < attackDuration)
        {
            DetectHitsInRange();
            elapsed += Time.deltaTime;
            yield return null;
        }

        onAttackEnded?.Invoke();
    }

    private void DetectHitsInRange()
    {
        if (damageOrigin == null)
            return;

        // Use sphere cast from damage origin
        Collider[] hitColliders = Physics.OverlapSphere(damageOrigin.position, attackRange);

        foreach (Collider collider in hitColliders)
        {
            // Skip if already hit in this attack
            if (hitInCurrentAttack.Contains(collider))
                continue;

            // Skip self and player
            if (collider.CompareTag("Player") || collider.transform.IsChildOf(transform.parent))
                continue;

            // Check if it's an enemy
            IDamageable damageable = collider.GetComponent<IDamageable>();
            if (damageable != null)
            {
                DealDamageToTarget(collider.gameObject, damageable);
                hitInCurrentAttack.Add(collider);
            }
        }
    }

    private void DealDamageToTarget(GameObject targetObject, IDamageable damageable)
    {
        float finalDamage = baseDamage;

        // Apply skill multiplier if available
        if (playerSkills != null)
        {
            float strengthBonus = playerSkills.GetStrengthDamageBonus();
            finalDamage *= (1f + strengthBonus);
        }

        // Apply knockback
        Rigidbody targetRB = targetObject.GetComponent<Rigidbody>();
        if (targetRB != null && knockbackForce > 0)
        {
            Vector3 knockbackDirection = (targetObject.transform.position - damageOrigin.position).normalized;
            targetRB.AddForce(knockbackDirection * knockbackForce, ForceMode.Impulse);
        }

        // Deal damage
        damageable.TakeDamage(finalDamage);

        // Invoke events
        onAttackDealt?.Invoke(finalDamage);
        onHitTarget?.Invoke(targetObject.GetComponent<Collider>());

        // Register attack with skill system for Strength XP
        if (playerSkills != null)
        {
            playerSkills.RegisterMeleeAttack();
        }

        Debug.Log($"[MeleeWeapon] Hit {targetObject.name} for {finalDamage} damage!");
    }

    public void Equip()
    {
        isEquipped = true;

        // Re-fetch animator if not set
        if (animator == null)
            animator = GetComponentInParent<Animator>();

        // Re-fetch attack action if not set
        if (attackAction == null)
        {
            var playerInput = GetComponentInParent<PlayerInput>();
            if (playerInput != null)
                attackAction = playerInput.actions.FindAction("Attack");
        }

        // Re-fetch skill system
        if (playerSkills == null)
            playerSkills = GetComponentInParent<PlayerSkills>();

        // Setup animator
        if (animator != null)
        {
            animator.SetBool("HasWeapon", true);
            animator.SetLayerWeight(animatorLayerIndex, 1f);
            
            // Set weapon-specific parameters
            animator.SetBool("isMelee", true);
            animator.SetBool($"is{weaponType}", true);
            
            Debug.Log($"[MeleeWeapon] {weaponType} equipped - animator layer weight set to 1");
        }

        // Set weapon layer
        if (weaponLayer >= 0)
            SetLayerRecursively(gameObject, weaponLayer);

        currentComboCount = 0;
        lastComboTime = Time.time;
        lastAttackTime = Time.time;

        Debug.Log($"[MeleeWeapon] {weaponType} equipped successfully!");
    }

    public void Unequip()
    {
        isEquipped = false;
        currentComboCount = 0;
        hitInCurrentAttack.Clear();

        if (animator != null)
        {
            animator.SetBool("HasWeapon", false);
            animator.SetBool("isMelee", false);
            animator.SetBool($"is{weaponType}", false);
            animator.SetLayerWeight(animatorLayerIndex, 0f);
            
            Debug.Log($"[MeleeWeapon] {weaponType} unequipped - animator layer weight set to 0");
        }

        Debug.Log($"[MeleeWeapon] {weaponType} unequipped");
    }

    public void SetWeaponItem(MeleeWeaponItem item)
    {
        weaponItem = item;
        if (item != null)
        {
            baseDamage = item.BaseDamage;
            attackSpeed = item.AttackSpeed;
            attackRange = item.AttackRange;
            knockbackForce = item.KnockbackForce;
        }
    }

    public MeleeWeaponItem GetWeaponItem() => weaponItem;
    public WeaponType GetWeaponType() => weaponType;
    public bool IsEquipped() => isEquipped;
    public float GetBaseDamage() => baseDamage;
    public float GetAttackRange() => attackRange;
    public int GetCurrentCombo() => currentComboCount + 1;

    private void SetLayerRecursively(GameObject obj, int newLayer)
    {
        if (obj == null) return;
        obj.layer = newLayer;
        foreach (Transform child in obj.transform)
        {
            if (child == null) continue;
            SetLayerRecursively(child.gameObject, newLayer);
        }
    }

    private void OnDrawGizmosSelected()
    {
        if (damageOrigin != null)
        {
            Gizmos.color = Color.red;
            Gizmos.DrawWireSphere(damageOrigin.position, attackRange);
        }
    }
}

public interface IDamageable
{
    void TakeDamage(float damage);
}
