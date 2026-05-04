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

    [Header("Stamina")]
    [SerializeField] private float maxStamina = 100f;
    [SerializeField] private float currentStamina = 100f;
    [SerializeField] private float staminaDrainRate = 30f; // Stamina points lost per second while sprinting
    [SerializeField] private float staminaRegenRate = 20f; // Stamina points gained per second while not sprinting
    [SerializeField] private InputActionReference sprintAction;

    [SerializeField] private InputActionReference crouchAction;
    [SerializeField] private InputActionReference aimAction;

    [Header("Death Flow")]
    [SerializeField] private string mainMenuSceneName = "MainMenu";
    [SerializeField] private string saveFilePrefix = "save_slot_";

    private float timeSinceDamage = 0f;
    private bool isAlive = true;
    private bool isCrouching = false;
    private bool isAiming = false;
    private bool isCurrentlySprinting = false;
    private bool isSprintInputPressed = false;

    public UnityEvent<float, float> onHealthChanged; // Current, Max
    public UnityEvent<float> onDamageReceived; // Damage amount
    public UnityEvent onDeath;
    public UnityEvent<float, float> onHungerChanged; // Current, Max
    public UnityEvent<float, float> onThirstChanged; // Current, Max
    public UnityEvent<float, float> onStaminaChanged; // Current, Max

    private ThirdPersonController thirdPersonController;
    private AnimationController animationController;
    private Animator animator;
    private Inventory inventory;
    private EquipmentManager equipmentManager;
    private InjurySystem injurySystem;
    private PlayerSkills playerSkills;
    private DayNightManager dayNightManager;
    
    [Header("Audio")]
    [SerializeField] private AudioClip eatClip;
    [SerializeField] private AudioClip drinkClip;
    [SerializeField, Range(0f,1f)] private float audioVolume = 1f;

    private Coroutine playerAudioCoroutine;

    private void Awake()
    {
        currentHealth = maxHealth;
        currentStamina = maxStamina;
        thirdPersonController = GetComponent<ThirdPersonController>();
        animationController = GetComponent<AnimationController>();
        animator = GetComponent<Animator>();
        inventory = GetComponent<Inventory>();
        equipmentManager = GetComponent<EquipmentManager>();
        injurySystem = GetComponent<InjurySystem>();
        playerSkills = GetComponent<PlayerSkills>();
        dayNightManager = FindAnyObjectByType<DayNightManager>();
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
        UpdateStamina();
        UpdateAimInput();
    }

    private void UpdateStamina()
    {
        if (isCurrentlySprinting && currentStamina > 0)
        {
            // Drain stamina while sprinting (reduced by Stamina skill)
            float drainMultiplier = playerSkills != null ? playerSkills.GetStaminaDrainMultiplier() : 1f;
            currentStamina -= staminaDrainRate * drainMultiplier * Time.deltaTime;
            if (currentStamina < 0) currentStamina = 0;
        }
        else if (!isSprintInputPressed && currentStamina < maxStamina)
        {
            // Regenerate stamina only when sprint button is not being held
            currentStamina += staminaRegenRate * Time.deltaTime;
            if (currentStamina > maxStamina) currentStamina = maxStamina;
        }

        onStaminaChanged?.Invoke(currentStamina, maxStamina);
    }

    private void UpdateVitals()
    {
        // Get metabolism multiplier from skills (reduces decay rate)
        float metabolismMultiplier = playerSkills != null ? playerSkills.GetMetabolismMultiplier() : 1f;
        
        // Get game days per second from DayNightManager to scale hunger/thirst with game time speed
        // This accounts for both timeScale and dayLengthInSeconds
        float gameSpeedMultiplier = dayNightManager != null ? dayNightManager.GetGameDaysPerSecond() * dayNightManager.GetDayLengthInSeconds() : 1f;
        
        // Decay hunger (reduced by Metabolism skill, scaled by game time speed)
        currentHunger -= (hungerDecayRate / 60f) * metabolismMultiplier * gameSpeedMultiplier * Time.deltaTime;
        if (currentHunger < 0) currentHunger = 0;
        onHungerChanged?.Invoke(currentHunger, maxHunger);

        // Decay thirst (reduced by Metabolism skill, scaled by game time speed)
        currentThirst -= (thirstDecayRate / 60f) * metabolismMultiplier * gameSpeedMultiplier * Time.deltaTime;
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
        bool aimPressed = aimAction != null && aimAction.action.IsPressed();
        isAiming = aimPressed && HasWeaponInHand();
    }

    private bool HasWeaponInHand()
    {
        if (equipmentManager == null)
            return false;

        return equipmentManager.GetCurrentWeapon() != null || equipmentManager.IsMeleeEquipped();
    }

    public void TakeDamage(float damage)
    {
        if (!isAlive) return;

        currentHealth -= damage;
        timeSinceDamage = 0f; // Reset regen timer

        onDamageReceived?.Invoke(damage);
        onHealthChanged?.Invoke(currentHealth, maxHealth);

        // Award Vitality XP for surviving damage
        if (currentHealth > 0 && playerSkills != null)
        {
            playerSkills.RegisterDamageSurvived(damage);
        }

        if (currentHealth <= 0)
        {
            Die();
        }
    }

    public void TakeDamageWithInjury(float baseDamage)
    {
        if (!isAlive || injurySystem == null) 
        {
            TakeDamage(baseDamage);
            return;
        }

        // Apply random injury
        Injury injury = injurySystem.ApplyRandomInjury();
        
        if (injury != null)
        {
            // Calculate actual damage based on injury type and body part
            float actualDamage = injury.GetTotalDamage(baseDamage);
            TakeDamage(actualDamage);
        }
        else
        {
            // If no injury was applied (max injuries reached), just take base damage
            TakeDamage(baseDamage);
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

        PlayerGameOverFlow.Show(mainMenuSceneName, saveFilePrefix);

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
    public float GetStamina() => currentStamina;
    public float GetMaxStamina() => maxStamina;
    public float GetStaminaPercent() => currentStamina / maxStamina;
    public bool IsAlive() => isAlive;
    public bool IsCrouching() => isCrouching;
    public bool IsAiming() => isAiming;
    public Inventory GetInventory() => inventory;
    public EquipmentManager GetEquipmentManager() => equipmentManager;
    public InjurySystem GetInjurySystem() => injurySystem;
    
    public bool IsHoldingPistol() => equipmentManager.IsCurrentWeaponPistol();
    public bool IsHoldingAssaultRifle() => equipmentManager.IsCurrentWeaponAssaultRifle();
    
    public void Eat(float amount)
    {
        currentHunger = Mathf.Min(currentHunger + amount, maxHunger);
        onHungerChanged?.Invoke(currentHunger, maxHunger);
        if (eatClip != null)
        {
            PlayPlayerClipTemp(eatClip);
        }
    }

    public void Drink(float amount)
    {
        currentThirst = Mathf.Min(currentThirst + amount, maxThirst);
        onThirstChanged?.Invoke(currentThirst, maxThirst);
        if (drinkClip != null)
        {
            PlayPlayerClipTemp(drinkClip);
        }
    }

    public void ApplySavedVitals(float health, float hunger, float thirst, float stamina, bool alive)
    {
        currentHealth = Mathf.Clamp(health, 0f, maxHealth);
        currentHunger = Mathf.Clamp(hunger, 0f, maxHunger);
        currentThirst = Mathf.Clamp(thirst, 0f, maxThirst);
        currentStamina = Mathf.Clamp(stamina, 0f, maxStamina);
        isAlive = alive;

        onHealthChanged?.Invoke(currentHealth, maxHealth);
        onHungerChanged?.Invoke(currentHunger, maxHunger);
        onThirstChanged?.Invoke(currentThirst, maxThirst);
        onStaminaChanged?.Invoke(currentStamina, maxStamina);
    }

    public void SetIsCurrentlySprinting(bool isSprinting)
    {
        isCurrentlySprinting = isSprinting;
    }

    public bool IsCurrentlySprinting()
    {
        return isCurrentlySprinting;
    }

    public void SetSprintInputPressed(bool pressed)
    {
        isSprintInputPressed = pressed;
    }

    public bool ConsumeStamina(float amount)
    {
        if (amount <= 0f)
            return true;

        if (currentStamina < amount)
            return false;

        currentStamina -= amount;
        currentStamina = Mathf.Max(0f, currentStamina);
        onStaminaChanged?.Invoke(currentStamina, maxStamina);
        return true;
    }

    public PlayerSkills GetPlayerSkills()
    {
        return playerSkills;
    }

    private void PlayPlayerClipTemp(AudioClip clip)
    {
        if (clip == null)
            return;

        StartCoroutine(PlayPlayerClipTempCoroutine(clip));
    }

    private System.Collections.IEnumerator PlayPlayerClipTempCoroutine(AudioClip clip)
    {
        GameObject temp = new GameObject("TempPlayerAudio");
        temp.transform.position = transform.position;
        AudioSource src = temp.AddComponent<AudioSource>();
        src.playOnAwake = false;
        src.spatialBlend = 0f;
        src.volume = audioVolume;
        src.clip = clip;
        src.loop = false;
        src.Play();

        yield return new WaitForSeconds(clip.length);

        if (src != null && src.isPlaying)
            src.Stop();

        Destroy(temp);
    }

}
