using UnityEngine;
using UnityEngine.UIElements;
using UnityEngine.InputSystem;

public class InventoryUIToolkit : MonoBehaviour
{
    public static bool IsInventoryOpen { get; private set; }
    [SerializeField] private Inventory inventory;
    [SerializeField] private UIDocument uiDocument;
    [SerializeField] private EquipmentManager equipmentManager;
    [SerializeField] private InputActionReference lookAction;
    [SerializeField] private InputActionReference moveAction;

    private Label inventoryTitle;
    private Label weightLabel;
    private ScrollView itemsScrollView;
    [SerializeField] private InputActionReference toggleInventoryAction;
    private bool isOpen = false;

    private void Awake()
    {
        if (uiDocument == null)
            uiDocument = GetComponent<UIDocument>();
        var root = uiDocument.rootVisualElement;
        inventoryTitle = root.Q<Label>(className: "inventory-title");
        weightLabel = root.Q<Label>(className: "inventory-weight");
        itemsScrollView = root.Q<ScrollView>(className: "items-list");
        SetInventoryOpen(false);
    }


    private void OnEnable()
    {
        if (inventory == null)
            inventory = FindFirstObjectByType<Inventory>();
        if (inventory != null)
            inventory.OnInventoryChanged += UpdateUI;
        UpdateUI();
        if (toggleInventoryAction != null)
        {
            toggleInventoryAction.action.performed += OnToggleInventory;
            toggleInventoryAction.action.Enable();
        }
    }

    private void OnDisable()
    {
        if (inventory != null)
            inventory.OnInventoryChanged -= UpdateUI;
        if (toggleInventoryAction != null)
        {
            toggleInventoryAction.action.performed -= OnToggleInventory;
            toggleInventoryAction.action.Disable();
        }
    }


    private void OnToggleInventory(InputAction.CallbackContext context)
    {
        SetInventoryOpen(!isOpen);
    }

    private void SetInventoryOpen(bool open)
    {
        isOpen = open;
        IsInventoryOpen = open;
        if (uiDocument != null)
            uiDocument.rootVisualElement.style.display = open ? DisplayStyle.Flex : DisplayStyle.None;
        if (open)
        {
            UnityEngine.Cursor.lockState = CursorLockMode.None;
            UnityEngine.Cursor.visible = true;
            if (lookAction != null)
            {
                lookAction.action.Disable();
                Debug.Log("[InventoryUIToolkit] Camera lookAction DISABLED (inventory open)");
            }
        }
        else
        {
            UnityEngine.Cursor.lockState = CursorLockMode.Locked;
            UnityEngine.Cursor.visible = false;
            if (lookAction != null)
            {
                lookAction.action.Enable();
                Debug.Log("[InventoryUIToolkit] Camera lookAction ENABLED (inventory closed)");
            }
        }
    }

    public void UpdateUI()
    {
        if (inventory == null) return;
        float currentWeight = inventory.GetCurrentWeight();
        float maxWeight = inventory.GetMaxWeight();
        bool isOverWeight = inventory.IsOverWeightLimit();

        if (weightLabel != null)
        {
            string overWeightText = isOverWeight ? $" (+{inventory.GetOverWeightAmount():F1} OVERWEIGHT)" : "";
            weightLabel.text = $"{currentWeight:F1}/{maxWeight:F1} kg{overWeightText}";
        }

        if (inventoryTitle != null)
        {
            inventoryTitle.text = "INVENTORY";
        }

        if (itemsScrollView != null)
        {
            itemsScrollView.Clear();
            var items = inventory.GetAllItems();
            var root = uiDocument.rootVisualElement;
            foreach (var slot in items)
            {
                if (slot.item != null && slot.quantity > 0)
                {
                    var row = new VisualElement();
                    row.style.flexDirection = FlexDirection.Row;
                    row.style.alignItems = Align.Center;
                    row.style.marginBottom = 8;
                    row.style.paddingLeft = 4;
                    row.style.paddingRight = 4;
                    row.style.paddingTop = 4;
                    row.style.paddingBottom = 4;
                    row.style.backgroundColor = new StyleColor(new Color(0.18f, 0.18f, 0.18f, 0.7f));
                    row.style.borderBottomLeftRadius = 8;
                    row.style.borderBottomRightRadius = 8;
                    row.style.borderTopLeftRadius = 8;
                    row.style.borderTopRightRadius = 8;

                    // Item icon
                    var icon = new Image();
                    icon.image = slot.item.Icon != null ? slot.item.Icon.texture : null;
                    icon.style.width = 56;
                    icon.style.height = 56;
                    icon.style.marginRight = 12;
                    row.Add(icon);

                    // Item name
                    var nameLabel = new Label(slot.item.ItemName);
                    nameLabel.style.unityTextAlign = TextAnchor.MiddleLeft;
                    nameLabel.style.fontSize = 30;
                    nameLabel.style.color = new StyleColor(Color.white);
                    nameLabel.style.flexGrow = 1;
                    row.Add(nameLabel);

                    // Quantity
                    var qtyLabel = new Label($"x{slot.quantity}");
                    qtyLabel.style.fontSize = 28;
                    qtyLabel.style.color = new StyleColor(new Color(0.8f, 0.8f, 0.8f));
                    qtyLabel.style.marginLeft = 12;
                    qtyLabel.style.unityTextAlign = TextAnchor.MiddleRight;
                    row.Add(qtyLabel);

                    // Right-click context menu for equip/dequip
                    row.RegisterCallback<MouseUpEvent>(evt =>
                    {
                        if (evt.button == 1) // Right mouse button
                        {
                            // Remove any existing menu
                            var existingMenu = root.Q("inventory-context-menu");
                            if (existingMenu != null)
                                existingMenu.RemoveFromHierarchy();

                            var menu = new VisualElement { name = "inventory-context-menu" };
                            menu.style.position = Position.Absolute;
                            // Use screen coordinates for menu placement
                            menu.style.left = evt.originalMousePosition.x;
                            menu.style.top = evt.originalMousePosition.y;
                            menu.style.backgroundColor = new StyleColor(new Color(0.13f, 0.13f, 0.13f, 0.98f));
                            menu.style.borderTopLeftRadius = 8;
                            menu.style.borderTopRightRadius = 8;
                            menu.style.borderBottomLeftRadius = 8;
                            menu.style.borderBottomRightRadius = 8;
                            menu.style.borderTopWidth = 2;
                            menu.style.borderBottomWidth = 2;
                            menu.style.borderLeftWidth = 2;
                            menu.style.borderRightWidth = 2;
                            menu.style.borderTopColor = new StyleColor(Color.gray);
                            menu.style.borderBottomColor = new StyleColor(Color.gray);
                            menu.style.borderLeftColor = new StyleColor(Color.gray);
                            menu.style.borderRightColor = new StyleColor(Color.gray);
                            // menu.style.boxShadow is not supported in UI Toolkit; skipping shadow effect
                            menu.style.paddingTop = 8;
                            menu.style.paddingBottom = 8;
                            menu.style.paddingLeft = 16;
                            menu.style.paddingRight = 16;
                            menu.style.flexDirection = FlexDirection.Column;
                            menu.style.minWidth = 240;
                            menu.pickingMode = PickingMode.Position;

                            if (equipmentManager == null)
                                equipmentManager = FindFirstObjectByType<EquipmentManager>();
                            if (slot.item is GunItem gun)
                            {
                                var gunType = gun.GunType;
                                if (gunType == Gun.GunType.Pistol)
                                {
                                    var equipLabel = new Label("Equip as Secondary");
                                    equipLabel.style.unityTextAlign = TextAnchor.MiddleLeft;
                                    equipLabel.style.fontSize = 28;
                                    equipLabel.style.color = new StyleColor(Color.white);
                                    equipLabel.style.paddingTop = 4;
                                    equipLabel.style.paddingBottom = 4;
                                    equipLabel.RegisterCallback<MouseUpEvent>(_ => {
                                        equipmentManager.EquipAsSecondary(gun);
                                        menu.RemoveFromHierarchy();
                                    });
                                    menu.Add(equipLabel);
                                    var dequipLabel = new Label("Dequip Secondary");
                                    dequipLabel.style.unityTextAlign = TextAnchor.MiddleLeft;
                                    dequipLabel.style.fontSize = 28;
                                    dequipLabel.style.color = new StyleColor(Color.white);
                                    dequipLabel.style.paddingTop = 4;
                                    dequipLabel.style.paddingBottom = 4;
                                    dequipLabel.RegisterCallback<MouseUpEvent>(_ => {
                                        equipmentManager.EquipAsSecondary(null);
                                        menu.RemoveFromHierarchy();
                                    });
                                    menu.Add(dequipLabel);
                                }
                                else
                                {
                                    var equipLabel = new Label("Equip as Primary");
                                    equipLabel.style.unityTextAlign = TextAnchor.MiddleLeft;
                                    equipLabel.style.fontSize = 28;
                                    equipLabel.style.color = new StyleColor(Color.white);
                                    equipLabel.style.paddingTop = 4;
                                    equipLabel.style.paddingBottom = 4;
                                    equipLabel.RegisterCallback<MouseUpEvent>(_ => {
                                        equipmentManager.EquipAsPrimary(gun);
                                        menu.RemoveFromHierarchy();
                                    });
                                    menu.Add(equipLabel);
                                    var dequipLabel = new Label("Dequip Primary");
                                    dequipLabel.style.unityTextAlign = TextAnchor.MiddleLeft;
                                    dequipLabel.style.fontSize = 28;
                                    dequipLabel.style.color = new StyleColor(Color.white);
                                    dequipLabel.style.paddingTop = 4;
                                    dequipLabel.style.paddingBottom = 4;
                                    dequipLabel.RegisterCallback<MouseUpEvent>(_ => {
                                        equipmentManager.EquipAsPrimary(null);
                                        menu.RemoveFromHierarchy();
                                    });
                                    menu.Add(dequipLabel);
                                }
                            }

                            // Remove menu on any click or scroll
                            System.Action removeMenu = () => { if (menu.parent != null) menu.RemoveFromHierarchy(); };
                            root.RegisterCallback<MouseDownEvent>((MouseDownEvent e) =>
                            {
                                if (!menu.worldBound.Contains(e.mousePosition))
                                    removeMenu();
                            });
                            root.RegisterCallback<WheelEvent>((WheelEvent e) => removeMenu());

                            root.Add(menu);
                        }
                    });
                    itemsScrollView.Add(row);
                }
            }
        }
    }
}
