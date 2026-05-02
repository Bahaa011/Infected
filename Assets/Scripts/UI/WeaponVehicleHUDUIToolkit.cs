using System;
using UnityEngine;
using UnityEngine.UIElements;

[DisallowMultipleComponent]
public class WeaponVehicleHUDUIToolkit : MonoBehaviour
{
    [SerializeField] private UIDocument uiDocument;
    [SerializeField] private Player player;
    [SerializeField] private EquipmentManager equipmentManager;
    [SerializeField] private AdvancedCarController carController;

    [Header("Visual")]
    [SerializeField] private Color accentColor = new Color(0.92f, 0.62f, 0.28f, 1f);
    [SerializeField] private Color panelBackground = new Color(0.05f, 0.06f, 0.07f, 0.94f);
    [SerializeField] private Color panelBorder = new Color(0.78f, 0.53f, 0.28f, 0.3f);

    private VisualElement root;
    private VisualElement weaponPanel;
    private VisualElement vehiclePanel;

    private Label weaponNameLabel;
    private Label weaponAmmoLabel;
    private Label weaponCapacityLabel;
    private Label weaponFireModeLabel;
    private Label weaponHintLabel;
    private VisualElement weaponAmmoFill;

    private Label vehicleNameLabel;
    private Label vehicleSpeedLabel;
    private Label vehicleSpeedUnitLabel;
    private Label vehicleHintLabel;
    private VisualElement vehicleSpeedFill;
    private int lastScreenWidth;
    private int lastScreenHeight;

    private void Awake()
    {
        if (uiDocument == null)
            uiDocument = GetComponent<UIDocument>();

        if (uiDocument == null || uiDocument.rootVisualElement == null)
        {
            enabled = false;
            return;
        }

        root = uiDocument.rootVisualElement;
        CacheUi();
        ApplyStaticTheme();
        RefreshResponsiveScale(true);
        SetRootVisible(false);
    }

    private void OnEnable()
    {
        ResolveReferences();
        SetRootVisible(false);
    }

    private void Update()
    {
        RefreshResponsiveScale();

        if (root == null)
            return;

        if (PauseMenuUIToolkit.IsPaused)
        {
            SetRootVisible(false);
            return;
        }

        ResolveReferences();
        UpdateHud();
    }

    private void ResolveReferences()
    {
        if (player == null)
            player = FindAnyObjectByType<Player>();

        if (equipmentManager == null)
            equipmentManager = FindAnyObjectByType<EquipmentManager>();

        if (carController == null || !carController.HasDriver)
            carController = FindActiveCarController();
    }

    private AdvancedCarController FindActiveCarController()
    {
        AdvancedCarController[] cars = FindObjectsByType<AdvancedCarController>();
        for (int i = 0; i < cars.Length; i++)
        {
            AdvancedCarController car = cars[i];
            if (car != null && car.HasDriver)
                return car;
        }

        return null;
    }

    private void CacheUi()
    {
        weaponPanel = root.Q<VisualElement>("weapon-panel");
        vehiclePanel = root.Q<VisualElement>("vehicle-panel");

        weaponNameLabel = root.Q<Label>("weapon-name-label");
        weaponAmmoLabel = root.Q<Label>("weapon-ammo-label");
        weaponCapacityLabel = root.Q<Label>("weapon-capacity-label");
        weaponFireModeLabel = root.Q<Label>("weapon-firemode-label");
        weaponHintLabel = root.Q<Label>("weapon-hint-label");
        weaponAmmoFill = root.Q<VisualElement>("weapon-ammo-fill");

        vehicleNameLabel = root.Q<Label>("vehicle-name-label");
        vehicleSpeedLabel = root.Q<Label>("vehicle-speed-label");
        vehicleSpeedUnitLabel = root.Q<Label>("vehicle-speed-unit-label");
        vehicleHintLabel = root.Q<Label>("vehicle-hint-label");
        vehicleSpeedFill = root.Q<VisualElement>("vehicle-speed-fill");
    }

    private void ApplyStaticTheme()
    {
        if (weaponPanel != null)
        {
            StyleCard(weaponPanel);
            weaponPanel.style.display = DisplayStyle.None;
        }

        if (vehiclePanel != null)
        {
            StyleCard(vehiclePanel);
            vehiclePanel.style.display = DisplayStyle.None;
        }

        ApplyAccent(weaponAmmoFill);
        ApplyAccent(vehicleSpeedFill);

        if (weaponHintLabel != null)
            weaponHintLabel.style.color = new StyleColor(new Color(0.82f, 0.82f, 0.82f, 0.65f));

        if (vehicleHintLabel != null)
            vehicleHintLabel.style.color = new StyleColor(new Color(0.82f, 0.82f, 0.82f, 0.65f));
    }

    private void StyleCard(VisualElement panel)
    {
        panel.style.backgroundColor = new StyleColor(panelBackground);
        panel.style.borderTopWidth = 1;
        panel.style.borderRightWidth = 1;
        panel.style.borderBottomWidth = 1;
        panel.style.borderLeftWidth = 1;
        panel.style.borderTopColor = new StyleColor(panelBorder);
        panel.style.borderRightColor = new StyleColor(panelBorder);
        panel.style.borderBottomColor = new StyleColor(new Color(0f, 0f, 0f, 0.5f));
        panel.style.borderLeftColor = new StyleColor(new Color(0f, 0f, 0f, 0.5f));
        panel.style.borderTopLeftRadius = 8;
        panel.style.borderTopRightRadius = 8;
        panel.style.borderBottomLeftRadius = 8;
        panel.style.borderBottomRightRadius = 8;
    }

    private void ApplyAccent(VisualElement element)
    {
        if (element == null)
            return;

        element.style.backgroundColor = new StyleColor(accentColor);
    }

    private void SetRootVisible(bool visible)
    {
        if (root != null)
            root.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
    }

    private void UpdateHud()
    {
        bool inVehicle = carController != null && carController.HasDriver;
        bool hasGun = equipmentManager != null && equipmentManager.GetCurrentWeapon() != null;
        bool shouldShow = inVehicle || hasGun;

        if (!shouldShow)
        {
            SetRootVisible(false);
            if (weaponPanel != null)
                weaponPanel.style.display = DisplayStyle.None;
            if (vehiclePanel != null)
                vehiclePanel.style.display = DisplayStyle.None;
            return;
        }

        SetRootVisible(true);

        if (inVehicle)
        {
            UpdateVehicleHud(carController);
            if (vehiclePanel != null)
                vehiclePanel.style.display = DisplayStyle.Flex;
            if (weaponPanel != null)
                weaponPanel.style.display = DisplayStyle.None;
            return;
        }

        if (weaponPanel != null)
            weaponPanel.style.display = hasGun ? DisplayStyle.Flex : DisplayStyle.None;
        if (vehiclePanel != null)
            vehiclePanel.style.display = DisplayStyle.None;

        if (!hasGun)
            return;

        UpdateWeaponHud(equipmentManager.GetCurrentWeapon());
    }

    private void UpdateWeaponHud(Gun gun)
    {
        if (gun == null)
            return;

        GunItem gunItem = gun.GetGunItem();
        string weaponName = gunItem != null && !string.IsNullOrWhiteSpace(gunItem.ItemName)
            ? gunItem.ItemName
            : gun.GetGunType().ToString();

        int currentAmmo = gunItem != null ? gunItem.CurrentAmmo : 0;
        int maxAmmo = gunItem != null ? gunItem.AmmoCapacity : 0;
        float ammoPercent = maxAmmo > 0 ? Mathf.Clamp01((float)currentAmmo / maxAmmo) : 0f;

        if (weaponNameLabel != null)
            weaponNameLabel.text = weaponName.ToUpperInvariant();

        if (weaponAmmoLabel != null)
            weaponAmmoLabel.text = $"{currentAmmo:0}";

        if (weaponFireModeLabel != null)
            weaponFireModeLabel.text = gun.GetFireMode().ToString().ToUpperInvariant();

        if (weaponHintLabel != null)
            weaponHintLabel.text = gun.IsReloading() ? "Reloading" : "R to reload";

        if (weaponCapacityLabel != null)
            weaponCapacityLabel.text = maxAmmo.ToString("0");

        if (weaponAmmoFill != null)
            weaponAmmoFill.style.width = new Length(ammoPercent * 100f, LengthUnit.Percent);
    }

    private void UpdateVehicleHud(AdvancedCarController car)
    {
        if (car == null)
            return;

        float speed = car.SpeedKmh;
        float maxSpeed = Mathf.Max(1f, car.MaxForwardSpeedKmh);
        float speedPercent = Mathf.Clamp01(speed / maxSpeed);

        if (vehicleNameLabel != null)
            vehicleNameLabel.text = car.gameObject.name.ToUpperInvariant();

        if (vehicleSpeedLabel != null)
            vehicleSpeedLabel.text = Mathf.RoundToInt(speed).ToString("000");

        if (vehicleSpeedUnitLabel != null)
            vehicleSpeedUnitLabel.text = "KM/H";

        if (vehicleHintLabel != null)
            vehicleHintLabel.text = "WASD / Space / Brake";

        if (vehicleSpeedFill != null)
            vehicleSpeedFill.style.width = new Length(speedPercent * 100f, LengthUnit.Percent);
    }

    private void RefreshResponsiveScale(bool force = false)
    {
        if (!force && Screen.width == lastScreenWidth && Screen.height == lastScreenHeight)
            return;

        lastScreenWidth = Screen.width;
        lastScreenHeight = Screen.height;

        if (root == null)
            return;

        // Root is fullscreen; keep scale at 1 to preserve absolute/anchored placement.
        root.style.scale = new Scale(Vector3.one);
    }
}
