using UnityEngine;
using UnityEngine.UIElements;

public class VitalsUIToolkit : MonoBehaviour
{
    [SerializeField] private UIDocument uiDocument;
    [SerializeField] private Player player;

    private VisualElement healthBar;
    private VisualElement hungerBar;
    private VisualElement thirstBar;
    private VisualElement staminaBar;
    private Label healthValue;
    private Label hungerValue;
    private Label thirstValue;
    private Label staminaValue;

    private void Awake()
    {
        if (uiDocument == null)
            uiDocument = GetComponent<UIDocument>();
        
        var root = uiDocument.rootVisualElement;
        healthBar = root.Q<VisualElement>(className: "health-bar");
        hungerBar = root.Q<VisualElement>(className: "hunger-bar");
        thirstBar = root.Q<VisualElement>(className: "thirst-bar");
        staminaBar = root.Q<VisualElement>(className: "stamina-bar");
        healthValue = root.Q<Label>(className: "health-value");
        hungerValue = root.Q<Label>(className: "hunger-value");
        thirstValue = root.Q<Label>(className: "thirst-value");
        staminaValue = root.Q<Label>(className: "stamina-value");
    }

    private void OnEnable()
    {
        if (player == null)
            player = FindFirstObjectByType<Player>();
        
        if (player != null)
        {
            player.onHealthChanged.AddListener(OnHealthChanged);
            player.onHungerChanged.AddListener(OnHungerChanged);
            player.onThirstChanged.AddListener(OnThirstChanged);
            player.onStaminaChanged.AddListener(OnStaminaChanged);
            
            // Initial update
            OnHealthChanged(player.GetHealth(), player.GetMaxHealth());
            OnHungerChanged(player.GetHunger(), player.GetMaxHunger());
            OnThirstChanged(player.GetThirst(), player.GetMaxThirst());
            OnStaminaChanged(player.GetStamina(), player.GetMaxStamina());
        }
    }

    private void OnDisable()
    {
        if (player != null)
        {
            player.onHealthChanged.RemoveListener(OnHealthChanged);
            player.onHungerChanged.RemoveListener(OnHungerChanged);
            player.onThirstChanged.RemoveListener(OnThirstChanged);
            player.onStaminaChanged.RemoveListener(OnStaminaChanged);
        }
    }

    private void OnHealthChanged(float current, float max)
    {
        float percent = Mathf.Clamp01(current / max) * 100f;
        if (healthBar != null)
            healthBar.style.width = new Length(percent, LengthUnit.Percent);
        if (healthValue != null)
            healthValue.text = $"{current:F0}/{max:F0}";
    }

    private void OnHungerChanged(float current, float max)
    {
        float percent = Mathf.Clamp01(current / max) * 100f;
        if (hungerBar != null)
            hungerBar.style.width = new Length(percent, LengthUnit.Percent);
        if (hungerValue != null)
            hungerValue.text = $"{current:F0}/{max:F0}";
    }

    private void OnThirstChanged(float current, float max)
    {
        float percent = Mathf.Clamp01(current / max) * 100f;
        if (thirstBar != null)
            thirstBar.style.width = new Length(percent, LengthUnit.Percent);
        if (thirstValue != null)
            thirstValue.text = $"{current:F0}/{max:F0}";
    }

    private void OnStaminaChanged(float current, float max)
    {
        float percent = Mathf.Clamp01(current / max) * 100f;
        if (staminaBar != null)
            staminaBar.style.width = new Length(percent, LengthUnit.Percent);
        if (staminaValue != null)
            staminaValue.text = $"{current:F0}/{max:F0}";
    }
}
