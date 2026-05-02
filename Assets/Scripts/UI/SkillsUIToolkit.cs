using UnityEngine;
using UnityEngine.UIElements;
using UnityEngine.InputSystem;

public class SkillsUIToolkit : MonoBehaviour
{
    [SerializeField] private UIDocument uiDocument;
    [SerializeField] private PlayerSkills playerSkills;
    [SerializeField] private InputActionReference toggleSkillsAction;

    // Weapon skill UI elements
    private VisualElement skillBar;
    private Label skillLevelText;
    private Label skillNameText;
    private Label xpText;
    
    // Stamina skill UI elements
    private VisualElement staminaBar;
    private Label staminaLevelText;
    private Label staminaXpText;
    
    // Metabolism skill UI elements
    private VisualElement metabolismBar;
    private Label metabolismLevelText;
    private Label metabolismXpText;
    
    // Vitality skill UI elements
    private VisualElement vitalityBar;
    private Label vitalityLevelText;
    private Label vitalityXpText;
    
    // Stealth skill UI elements
    private VisualElement stealthBar;
    private Label stealthLevelText;
    private Label stealthXpText;
    
    private VisualElement skillsPanel;
    private EquipmentManager equipmentManager;
    private Gun.GunType currentDisplayedGunType = Gun.GunType.Pistol;
    private bool isOpen = false;
    private InputAction runtimeToggleAction;
    private int lastScreenWidth;
    private int lastScreenHeight;

    private void Awake()
    {
        if (uiDocument == null)
            uiDocument = GetComponent<UIDocument>();
        
        var root = uiDocument.rootVisualElement;
        
        // Get skills panel for show/hide
        skillsPanel = root.Q<VisualElement>(className: "skills-panel");
        
        // Weapon skill
        skillBar = root.Q<VisualElement>(className: "skill-bar");
        skillLevelText = root.Q<Label>(className: "skill-level");
        skillNameText = root.Q<Label>(className: "skill-name");
        xpText = root.Q<Label>(className: "skill-xp");
        
        // Stamina skill
        staminaBar = root.Q<VisualElement>(className: "stamina-bar");
        staminaLevelText = root.Q<Label>(className: "stamina-level");
        staminaXpText = root.Q<Label>(className: "stamina-xp");
        
        // Metabolism skill
        metabolismBar = root.Q<VisualElement>(className: "metabolism-bar");
        metabolismLevelText = root.Q<Label>(className: "metabolism-level");
        metabolismXpText = root.Q<Label>(className: "metabolism-xp");
        
        // Vitality skill
        vitalityBar = root.Q<VisualElement>(className: "vitality-bar");
        vitalityLevelText = root.Q<Label>(className: "vitality-level");
        vitalityXpText = root.Q<Label>(className: "vitality-xp");
        
        // Stealth skill
        stealthBar = root.Q<VisualElement>(className: "stealth-bar");
        stealthLevelText = root.Q<Label>(className: "stealth-level");
        stealthXpText = root.Q<Label>(className: "stealth-xp");
        
        // Start hidden
        SetUIOpen(false);
        RefreshResponsiveScale(true);
    }

    private void OnEnable()
    {
        if (playerSkills == null)
            playerSkills = FindFirstObjectByType<PlayerSkills>();
        
        if (equipmentManager == null)
            equipmentManager = FindFirstObjectByType<EquipmentManager>();

        BindToggleInput();
        
        if (playerSkills != null)
        {
            playerSkills.onSkillLevelUp.AddListener(OnSkillLevelUp);
            playerSkills.onSkillProgressChanged.AddListener(OnSkillProgressChanged);
            playerSkills.onGeneralSkillLevelUp.AddListener(OnGeneralSkillLevelUp);
            playerSkills.onGeneralSkillProgressChanged.AddListener(OnGeneralSkillProgressChanged);
        }
    }

    private void OnDisable()
    {
        UnbindToggleInput();

        if (playerSkills != null)
        {
            playerSkills.onSkillLevelUp.RemoveListener(OnSkillLevelUp);
            playerSkills.onSkillProgressChanged.RemoveListener(OnSkillProgressChanged);
            playerSkills.onGeneralSkillLevelUp.RemoveListener(OnGeneralSkillLevelUp);
            playerSkills.onGeneralSkillProgressChanged.RemoveListener(OnGeneralSkillProgressChanged);
        }
    }

    private void BindToggleInput()
    {
        if (toggleSkillsAction != null)
        {
            runtimeToggleAction = toggleSkillsAction.action;
        }
        else
        {
            var playerInput = FindFirstObjectByType<PlayerInput>();
            if (playerInput != null)
            {
                runtimeToggleAction = playerInput.actions.FindAction("ToggleSkills");
            }
        }

        if (runtimeToggleAction != null)
        {
            runtimeToggleAction.performed += OnToggleSkills;
            runtimeToggleAction.Enable();
        }
        else
        {
            Debug.LogWarning("[SkillsUIToolkit] ToggleSkills action not found. Assign toggleSkillsAction or add 'ToggleSkills' action to PlayerInput.");
        }
    }

    private void UnbindToggleInput()
    {
        if (runtimeToggleAction == null)
            return;

        runtimeToggleAction.performed -= OnToggleSkills;

        if (toggleSkillsAction != null && runtimeToggleAction == toggleSkillsAction.action)
        {
            runtimeToggleAction.Disable();
        }

        runtimeToggleAction = null;
    }

    private void OnToggleSkills(InputAction.CallbackContext context)
    {
        SetUIOpen(!isOpen);
    }

    private void SetUIOpen(bool open)
    {
        isOpen = open;
        if (skillsPanel != null)
        {
            skillsPanel.style.display = open ? DisplayStyle.Flex : DisplayStyle.None;
        }
    }

    private void Update()
    {
        RefreshResponsiveScale();

        // Re-fetch references if lost
        if (playerSkills == null)
            playerSkills = FindFirstObjectByType<PlayerSkills>();
        
        if (equipmentManager == null)
            equipmentManager = FindFirstObjectByType<EquipmentManager>();

        // Keep HUD clean: close skills if inventory is opened
        if (InventoryUIToolkit.IsInventoryOpen && isOpen)
        {
            SetUIOpen(false);
            return;
        }

        if (isOpen)
        {
            UpdateAllSkills();
        }
    }

    private void UpdateAllSkills()
    {
        if (playerSkills == null)
            return;

        // Update weapon skill
        if (equipmentManager != null)
        {
            Gun currentWeapon = equipmentManager.GetCurrentWeapon();
            if (currentWeapon != null)
            {
                Gun.GunType gunType = currentWeapon.GetGunType();
                currentDisplayedGunType = gunType;
                UpdateWeaponDisplay(gunType);
            }
            else
            {
                if (skillNameText != null)
                    skillNameText.text = "No Weapon";
            }
        }
        
        // Update all general skills
        UpdateGeneralSkillDisplay(PlayerSkills.SkillType.Stamina, staminaBar, staminaLevelText, staminaXpText);
        UpdateGeneralSkillDisplay(PlayerSkills.SkillType.Metabolism, metabolismBar, metabolismLevelText, metabolismXpText);
        UpdateGeneralSkillDisplay(PlayerSkills.SkillType.Vitality, vitalityBar, vitalityLevelText, vitalityXpText);
        UpdateGeneralSkillDisplay(PlayerSkills.SkillType.Stealth, stealthBar, stealthLevelText, stealthXpText);
    }

    private void UpdateWeaponDisplay(Gun.GunType gunType)
    {
        int level = playerSkills.GetSkillLevel(gunType);
        float progress = playerSkills.GetSkillProgress(gunType);
        float currentXP = playerSkills.GetCurrentXP(gunType);
        float neededXP = playerSkills.GetXPNeeded(gunType);

        if (skillLevelText != null)
            skillLevelText.text = $"Lvl {level}";

        if (skillNameText != null)
            skillNameText.text = $"{gunType} Accuracy";

        if (xpText != null)
            xpText.text = $"{currentXP:F0} / {neededXP:F0}";

        if (skillBar != null)
        {
            float percent = Mathf.Clamp01(progress) * 100f;
            skillBar.style.width = new Length(percent, LengthUnit.Percent);
        }
    }

    private void UpdateGeneralSkillDisplay(PlayerSkills.SkillType skillType, VisualElement bar, Label levelText, Label xpLabel)
    {
        int level = playerSkills.GetGeneralSkillLevel(skillType);
        float progress = playerSkills.GetGeneralSkillProgress(skillType);
        float currentXP = playerSkills.GetGeneralSkillXP(skillType);
        float neededXP = playerSkills.GetGeneralSkillXPNeeded(skillType);

        if (levelText != null)
            levelText.text = $"Lvl {level}";

        if (xpLabel != null)
            xpLabel.text = $"{currentXP:F0} / {neededXP:F0}";

        if (bar != null)
        {
            float percent = Mathf.Clamp01(progress) * 100f;
            bar.style.width = new Length(percent, LengthUnit.Percent);
        }
    }

    private void OnSkillLevelUp(Gun.GunType gunType, int newLevel)
    {
        if (gunType == currentDisplayedGunType)
        {
            UpdateWeaponDisplay(gunType);
        }
    }

    private void OnSkillProgressChanged(Gun.GunType gunType, float currentXP, float neededXP)
    {
        if (gunType == currentDisplayedGunType)
        {
            UpdateWeaponDisplay(gunType);
        }
    }

    private void OnGeneralSkillLevelUp(PlayerSkills.SkillType skillType, int newLevel)
    {
        Debug.Log($"[SkillsUI] {skillType} leveled up to {newLevel}!");
    }

    private void OnGeneralSkillProgressChanged(PlayerSkills.SkillType skillType, float currentXP, float neededXP)
    {
        // Will be updated in Update()
    }

    private void RefreshResponsiveScale(bool force = false)
    {
        if (!force && Screen.width == lastScreenWidth && Screen.height == lastScreenHeight)
            return;

        lastScreenWidth = Screen.width;
        lastScreenHeight = Screen.height;

        if (skillsPanel == null)
            return;

        // Keep panel placement stable; avoid transform scaling that shifts anchored UI.
        skillsPanel.style.scale = new Scale(Vector3.one);
    }
}
