using UnityEngine;
using UnityEngine.UIElements;

public class CrosshairUIToolkit : MonoBehaviour
{
    [SerializeField] private UIDocument uiDocument;
    [SerializeField] private Player player;
    [SerializeField] private EquipmentManager equipmentManager;

    [Header("Visual")]
    [SerializeField] private Color crosshairColor = new Color(0.96f, 0.96f, 0.96f, 0.95f);

    private VisualElement root;
    private bool isVisible;

    private void Awake()
    {
        if (uiDocument == null)
            uiDocument = GetComponent<UIDocument>();

        if (uiDocument == null)
            return;

        root = uiDocument.rootVisualElement?.Q<VisualElement>(className: "crosshair-root");
        if (root != null)
        {
            root.pickingMode = PickingMode.Ignore;
            root.style.display = DisplayStyle.None;
            ApplyColor();
        }
    }

    private void OnEnable()
    {
        if (player == null)
            player = FindAnyObjectByType<Player>();

        if (equipmentManager == null)
            equipmentManager = FindAnyObjectByType<EquipmentManager>();

        SetVisible(false);
    }

    private void Update()
    {
        if (root == null)
            return;

        if (player == null)
            player = FindAnyObjectByType<Player>();

        if (equipmentManager == null)
            equipmentManager = FindAnyObjectByType<EquipmentManager>();

        bool hasGunInHand = equipmentManager != null && equipmentManager.GetCurrentWeapon() != null;
        bool shouldShow = !InventoryUIToolkit.IsInventoryOpen && hasGunInHand && player != null && player.IsAiming();

        if (shouldShow != isVisible)
            SetVisible(shouldShow);
    }

    private void SetVisible(bool visible)
    {
        isVisible = visible;
        if (root != null)
            root.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
    }

    private void ApplyColor()
    {
        if (root == null)
            return;

        SetElementColor("crosshair-line-top");
        SetElementColor("crosshair-line-bottom");
        SetElementColor("crosshair-line-left");
        SetElementColor("crosshair-line-right");
        SetElementColor("crosshair-dot");
    }

    private void SetElementColor(string name)
    {
        var element = root.Q<VisualElement>(name);
        if (element != null)
            element.style.backgroundColor = new StyleColor(crosshairColor);
    }
}
