using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Events;

public class Player : MonoBehaviour
{
    [SerializeField] private float maxHealth = 100f;
    [SerializeField] private float currentHealth;
    [SerializeField] private float regenRate = 0f; // Health regen per second (0 = disabled)
    [SerializeField] private float regenDelay = 3f; // Seconds before regen starts after taking damage

    [Header("Vitals")]
    [SerializeField] private float maxHunger = 100f;
    [SerializeField] private float currentHunger = 100f;
    [SerializeField] private float hungerDecayRate = 5f; // Hunger points lost per minute
    [SerializeField] private float maxThirst = 100f;
    [SerializeField] private float currentThirst = 100f;
    [SerializeField] private float thirstDecayRate = 8f; // Thirst points lost per minute

    [SerializeField] private InputActionReference crouchAction;
    [SerializeField] private InputActionReference aimAction;

    private float timeSinceDamage = 0f;
    private bool isAlive = true;
    private bool isCrouching = false;
    private bool isAiming = false;

    public UnityEvent<float, float> onHealthChanged; // Current, Max
    public UnityEvent<float> onDamageReceived; // Damage amount
    public UnityEvent onDeath;
    public UnityEvent<float, float> onHungerChanged; // Current, Max
    public UnityEvent<float, float> onThirstChanged; // Current, Max

    private ThirdPersonController thirdPersonController;
    private Animator animator;
    private Inventory inventory;
    private EquipmentManager equipmentManager;

    private void Awake()
    {
        currentHealth = maxHealth;
        thirdPersonController = GetComponent<ThirdPersonController>();
        animator = GetComponent<Animator>();
        inventory = GetComponent<Inventory>();
        equipmentManager = GetComponent<EquipmentManager>();
    }

    private void OnEnable()
    {
        if (crouchAction != null)
            crouchAction.action.Enable();
        if (aimAction != null)
            aimAction.action.Enable();
    }

    private void OnDisable()
    {
        if (crouchAction != null)
            crouchAction.action.Disable();
        if (aimAction != null)
            aimAction.action.Disable();
    }

    private void Update()
    {
        if (!isAlive) return;

        timeSinceDamage += Time.deltaTime;

        // Regenerate health if enabled and delay has passed
        if (regenRate > 0 && timeSinceDamage >= regenDelay)
        {
            Heal(regenRate * Time.deltaTime);
        }

        // Update hunger and thirst
        UpdateVitals();
        UpdateAimInput();
    }

    private void UpdateVitals()
    {
        // Decay hunger
        currentHunger -= (hungerDecayRate / 60f) * Time.deltaTime; // Convert per-minute to per-second
        if (currentHunger < 0) currentHunger = 0;
        onHungerChanged?.Invoke(currentHunger, maxHunger);

        // Decay thirst
        currentThirst -= (thirstDecayRate / 60f) * Time.deltaTime; // Convert per-minute to per-second
        if (currentThirst < 0) currentThirst = 0;
        onThirstChanged?.Invoke(currentThirst, maxThirst);

        // Take damage if starving or dehydrated
        if (currentHunger <= 0)
            TakeDamage(10f * Time.deltaTime);
        if (currentThirst <= 0)
            TakeDamage(15f * Time.deltaTime);
    }

    public void OnCrouch(InputValue value)
    {
        if (value.isPressed)
            isCrouching = !isCrouching;
    }

    private void UpdateAimInput()
    {
        if (aimAction != null)
        {
            isAiming = aimAction.action.IsPressed();
        }
    }

    public void TakeDamage(float damage)
    {
        if (!isAlive) return;

        currentHealth -= damage;
        timeSinceDamage = 0f; // Reset regen timer

        onDamageReceived?.Invoke(damage);
        onHealthChanged?.Invoke(currentHealth, maxHealth);

        if (currentHealth <= 0)
        {
            Die();
        }
    }

    public void Heal(float amount)
    {
        if (!isAlive) return;

        currentHealth = Mathf.Min(currentHealth + amount, maxHealth);
        onHealthChanged?.Invoke(currentHealth, maxHealth);
    }

    public void Die()
    {
        if (!isAlive) return;

        isAlive = false;
        onDeath?.Invoke();

        // Disable movement
        if (thirdPersonController != null)
            thirdPersonController.enabled = false;

        // Play death animation
        if (animator != null)
            animator.SetBool("isDead", true);

        // You can add respawn logic here or in a separate manager
        Destroy(gameObject, 3f); // Destroy after 3 seconds for death animation
    }

    public float GetHealth() => currentHealth;
    public float GetMaxHealth() => maxHealth;
    public float GetHealthPercent() => currentHealth / maxHealth;
    public float GetHunger() => currentHunger;
    public float GetMaxHunger() => maxHunger;
    public float GetHungerPercent() => currentHunger / maxHunger;
    public float GetThirst() => currentThirst;
    public float GetMaxThirst() => maxThirst;
    public float GetThirstPercent() => currentThirst / maxThirst;
    public bool IsAlive() => isAlive;
    public bool IsCrouching() => isCrouching;
    public bool IsAiming() => isAiming;
    public Inventory GetInventory() => inventory;
    public EquipmentManager GetEquipmentManager() => equipmentManager;
    
    public bool IsHoldingPistol() => equipmentManager.IsCurrentWeaponPistol();
    public bool IsHoldingAssaultRifle() => equipmentManager.IsCurrentWeaponAssaultRifle();
    
    public void Eat(float amount)
    {
        currentHunger = Mathf.Min(currentHunger + amount, maxHunger);
        onHungerChanged?.Invoke(currentHunger, maxHunger);
    }

    public void Drink(float amount)
    {
        currentThirst = Mathf.Min(currentThirst + amount, maxThirst);
        onThirstChanged?.Invoke(currentThirst, maxThirst);
    }
}
