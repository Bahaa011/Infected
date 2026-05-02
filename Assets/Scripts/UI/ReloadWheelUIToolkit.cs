using UnityEngine;
using UnityEngine.UIElements;

public class ReloadWheelUIToolkit : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private UIDocument uiDocument;
    [SerializeField] private EquipmentManager equipmentManager;

    [Header("Wheel Settings")]
    [SerializeField] private Vector2 wheelSize = new Vector2(82, 82);
    [SerializeField] private Vector2 screenOffset = new Vector2(36, 116);
    [SerializeField] private float thickness = 4.5f;
    [SerializeField] private Color trackColor = new Color(0.18f, 0.18f, 0.18f, 0.58f);
    [SerializeField] private Color fillColor = new Color(0.96f, 0.96f, 0.96f, 1f);
    [SerializeField] private bool showLabel = false;
    [SerializeField] private string labelText = "Reload";
    [SerializeField] private bool centerOnScreen = true;
    [SerializeField] private bool enforceCleanVisualPreset = true;

    private VisualElement container;
    private RadialProgressElement wheel;
    private Label label;
    private Gun currentGun;
    [SerializeField] private bool usePollingFallback = true;

    private void Awake()
    {
        if (uiDocument == null)
            uiDocument = GetComponent<UIDocument>();

        BuildUI();
        SetVisible(false);
    }

    private void OnEnable()
    {
        if (equipmentManager == null)
            equipmentManager = FindFirstObjectByType<EquipmentManager>();

        if (equipmentManager != null)
        {
            equipmentManager.onWeaponSwitched.AddListener(OnWeaponSwitched);
        }

        BindToGun(equipmentManager != null ? equipmentManager.GetCurrentWeapon() : null);
    }

    private void OnDisable()
    {
        if (equipmentManager != null)
        {
            equipmentManager.onWeaponSwitched.RemoveListener(OnWeaponSwitched);
        }

        UnbindFromGun();
    }

    private void Update()
    {
        EnsureBinding();

        if (!usePollingFallback || currentGun == null)
            return;

        if (currentGun.IsReloading())
        {
            if (wheel != null)
                wheel.SetProgress(currentGun.GetReloadProgress());
            SetVisible(true);
        }
        else
        {
            SetVisible(false);
        }
    }

    private void EnsureBinding()
    {
        if (equipmentManager == null)
        {
            equipmentManager = FindFirstObjectByType<EquipmentManager>();
            if (equipmentManager != null)
            {
                equipmentManager.onWeaponSwitched.AddListener(OnWeaponSwitched);
            }
        }

        if (equipmentManager == null)
            return;

        Gun weapon = equipmentManager.GetCurrentWeapon();
        if (weapon != currentGun)
        {
            BindToGun(weapon);
        }
    }

    private void BuildUI()
    {
        if (uiDocument == null)
            return;

        if (enforceCleanVisualPreset)
        {
            thickness = 4.5f;
            trackColor = new Color(0.2f, 0.2f, 0.2f, 0.5f);
            fillColor = new Color(0.96f, 0.96f, 0.96f, 1f);
            showLabel = false;
        }

        var root = uiDocument.rootVisualElement;

        float scale = ResponsiveUiUtility.GetScaleFactor();
        wheelSize = wheelSize * scale;
        screenOffset = screenOffset * scale;
        thickness *= scale;

        container = root.Q<VisualElement>(className: "reload-wheel-container");
        label = root.Q<Label>(className: "reload-wheel-label");

        // Fallback: create container if not found in UXML
        if (container == null)
        {
            container = new VisualElement();
            container.name = "reload-wheel-container";
            container.style.position = Position.Absolute;
            container.style.right = screenOffset.x;
            container.style.bottom = screenOffset.y;
            container.style.width = wheelSize.x;
            container.style.height = wheelSize.y;
            container.style.justifyContent = Justify.Center;
            container.style.alignItems = Align.Center;
            container.pickingMode = PickingMode.Ignore;
            root.Add(container);
        }

        // Create wheel element in code (keeps UXML simple and matches existing UI pattern)
        wheel = container.Q<RadialProgressElement>(className: "reload-wheel");
        if (wheel == null)
        {
            wheel = new RadialProgressElement();
            wheel.name = "reload-wheel";
            wheel.AddToClassList("reload-wheel");
            container.Insert(0, wheel);
        }

        // Create label if missing
        if (label == null && showLabel)
        {
            label = new Label(labelText);
            label.name = "reload-wheel-label";
            label.AddToClassList("reload-wheel-label");
            container.Add(label);
        }

        if (wheel != null)
        {
            wheel.style.width = wheelSize.x;
            wheel.style.height = wheelSize.y;
            wheel.SetThickness(thickness);
            wheel.SetTrackColor(trackColor);
            wheel.SetFillColor(fillColor);
            wheel.SetProgress(0f);
        }

        if (container != null)
        {
            if (centerOnScreen)
            {
                container.style.left = new Length(50, LengthUnit.Percent);
                container.style.top = new Length(50, LengthUnit.Percent);
                container.style.right = StyleKeyword.Auto;
                container.style.bottom = StyleKeyword.Auto;
                container.style.translate = new Translate(new Length(-50, LengthUnit.Percent), new Length(-50, LengthUnit.Percent), 0);
            }
            else
            {
                container.style.translate = new Translate(0, 0, 0);
                container.style.right = screenOffset.x;
                container.style.bottom = screenOffset.y;
                container.style.left = StyleKeyword.Auto;
                container.style.top = StyleKeyword.Auto;
            }
        }

        if (label != null)
        {
            label.text = labelText;
            label.style.display = showLabel ? DisplayStyle.Flex : DisplayStyle.None;
        }
    }

    private void OnWeaponSwitched(Gun gun)
    {
        BindToGun(gun);
    }

    private void BindToGun(Gun gun)
    {
        if (currentGun == gun)
            return;

        UnbindFromGun();
        currentGun = gun;

        if (currentGun != null)
        {
            currentGun.onReloadStarted.AddListener(OnReloadStarted);
            currentGun.onReloadProgress.AddListener(OnReloadProgress);
            currentGun.onReloadCompleted.AddListener(OnReloadCompleted);
        }
        else
        {
            SetVisible(false);
        }
    }

    private void UnbindFromGun()
    {
        if (currentGun == null)
            return;

        currentGun.onReloadStarted.RemoveListener(OnReloadStarted);
        currentGun.onReloadProgress.RemoveListener(OnReloadProgress);
        currentGun.onReloadCompleted.RemoveListener(OnReloadCompleted);
        currentGun = null;
    }

    private void OnReloadStarted()
    {
        if (wheel != null)
            wheel.SetProgress(0f);
        SetVisible(true);
    }

    private void OnReloadProgress(float value)
    {
        if (wheel != null)
            wheel.SetProgress(value);
    }

    private void OnReloadCompleted()
    {
        if (wheel != null)
            wheel.SetProgress(1f);
        SetVisible(false);
    }

    private void SetVisible(bool visible)
    {
        if (container != null)
            container.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
    }
}
