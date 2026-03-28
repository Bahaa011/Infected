using UnityEngine;
using UnityEngine.UIElements;
using UnityEngine.InputSystem;
using System;
using System.Collections.Generic;

public class InventoryUIToolkit : MonoBehaviour
{
    public enum InventoryTab
    {
        Inventory,
        Skills,
        Injury
    }

    public static bool IsInventoryOpen { get; private set; }
    public static InventoryTab ActiveTab { get; private set; } = InventoryTab.Inventory;
    public static bool UseTabbedInventoryUI => true;
    public static event Action<InventoryTab> OnActiveTabChanged;

    [SerializeField] private Inventory inventory;
    [SerializeField] private UIDocument uiDocument;
    [SerializeField] private EquipmentManager equipmentManager;
    [SerializeField] private PlayerSkills playerSkills;
    [SerializeField] private Player player;
    [SerializeField] private InputActionReference lookAction;
    [SerializeField] private InputActionReference moveAction;

    private Label inventoryTitle;
    private VisualElement rootVisual;
    private VisualElement inventoryPanel;
    private VisualElement inventoryDragHandle;
    private Button inventoryTabButton;
    private Button skillsTabButton;
    private Button injuryTabButton;

    private VisualElement inventoryTabContent;
    private VisualElement skillsTabContent;
    private VisualElement injuryTabContent;

    private ScrollView itemsScrollView;
    private ScrollView skillsScrollView;
    private ScrollView injuryScrollView;

    private InjurySystem injurySystem;

    [SerializeField] private InputActionReference toggleInventoryAction;
    private bool isOpen = false;
    private bool isDraggingInventory;
    private int activeDragPointerId = -1;
    private Vector2 dragStartPointerPos;
    private Vector2 dragStartPanelPos;

    private bool isItemDragPending;
    private bool isItemDragging;
    private int itemDragPointerId = -1;
    private int itemDragSourceSlotIndex = -1;
    private Vector2 itemDragStartMouse;
    private Item itemDragItem;
    private int itemDragQuantity;
    private VisualElement itemDragGhost;
    private readonly Dictionary<int, VisualElement> inventoryCellsByIndex = new Dictionary<int, VisualElement>();
    private const float ItemDragStartThreshold = 8f;

    private void Awake()
    {
        if (uiDocument == null)
            uiDocument = GetComponent<UIDocument>();
        rootVisual = uiDocument.rootVisualElement;
        inventoryPanel = rootVisual.Q<VisualElement>(className: "inventory-panel");
        inventoryDragHandle = rootVisual.Q<VisualElement>("inventory-drag-handle");
        inventoryTitle = rootVisual.Q<Label>(className: "inventory-title");

        inventoryTabButton = rootVisual.Q<Button>("tab-inventory");
        skillsTabButton = rootVisual.Q<Button>("tab-skills");
        injuryTabButton = rootVisual.Q<Button>("tab-injury");

        inventoryTabContent = rootVisual.Q<VisualElement>("tab-content-inventory");
        skillsTabContent = rootVisual.Q<VisualElement>("tab-content-skills");
        injuryTabContent = rootVisual.Q<VisualElement>("tab-content-injury");

        itemsScrollView = rootVisual.Q<ScrollView>(className: "items-list");
        skillsScrollView = rootVisual.Q<ScrollView>("skills-list");
        injuryScrollView = rootVisual.Q<ScrollView>("injury-list");

        rootVisual.RegisterCallback<PointerMoveEvent>(OnRootPointerMoveItemDrag);
        rootVisual.RegisterCallback<PointerUpEvent>(OnRootPointerUpItemDrag);

        SetupInventoryDragging();
        StyleAllScrollViews();

        RegisterTabCallbacks();
        SetActiveTab(InventoryTab.Inventory, true);
        SetInventoryOpen(false);
    }


    private void OnEnable()
    {
        if (inventory == null)
            inventory = FindAnyObjectByType<Inventory>();
        if (player == null)
            player = FindAnyObjectByType<Player>();
        if (playerSkills == null)
            playerSkills = FindAnyObjectByType<PlayerSkills>();
        if (equipmentManager == null)
            equipmentManager = FindAnyObjectByType<EquipmentManager>();
        if (injurySystem == null)
            injurySystem = FindAnyObjectByType<InjurySystem>();

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

    private void Update()
    {
        if (!isOpen)
            return;

        EnsureRuntimeReferences();

        if (ActiveTab == InventoryTab.Skills)
            UpdateSkillsTab();
        else if (ActiveTab == InventoryTab.Injury)
            UpdateInjuryTab();
    }

    private void EnsureRuntimeReferences()
    {
        if (player == null)
            player = FindAnyObjectByType<Player>();

        if (playerSkills == null)
            playerSkills = FindAnyObjectByType<PlayerSkills>();

        if (equipmentManager == null)
            equipmentManager = FindAnyObjectByType<EquipmentManager>();

        if (injurySystem == null)
            injurySystem = FindAnyObjectByType<InjurySystem>();
    }

    private void RegisterTabCallbacks()
    {
        if (inventoryTabButton != null)
            inventoryTabButton.clicked += () => SetActiveTab(InventoryTab.Inventory);

        if (skillsTabButton != null)
            skillsTabButton.clicked += () => SetActiveTab(InventoryTab.Skills);

        if (injuryTabButton != null)
            injuryTabButton.clicked += () => SetActiveTab(InventoryTab.Injury);
    }

    private void SetupInventoryDragging()
    {
        if (inventoryPanel == null)
            return;

        if (inventoryDragHandle == null && inventoryTitle != null)
            inventoryDragHandle = inventoryTitle.parent;

        if (inventoryDragHandle == null)
            return;

        inventoryDragHandle.RegisterCallback<PointerDownEvent>(OnInventoryDragStart);
        inventoryDragHandle.RegisterCallback<PointerMoveEvent>(OnInventoryDragMove);
        inventoryDragHandle.RegisterCallback<PointerUpEvent>(OnInventoryDragEnd);
        inventoryDragHandle.RegisterCallback<PointerCaptureOutEvent>(_ => StopInventoryDrag());
    }

    private void OnInventoryDragStart(PointerDownEvent evt)
    {
        if (evt.button != 0 || inventoryPanel == null)
            return;

        isDraggingInventory = true;
        activeDragPointerId = evt.pointerId;
        dragStartPointerPos = new Vector2(evt.position.x, evt.position.y);

        dragStartPanelPos = new Vector2(inventoryPanel.resolvedStyle.left, inventoryPanel.resolvedStyle.top);
        inventoryPanel.style.left = dragStartPanelPos.x;
        inventoryPanel.style.top = dragStartPanelPos.y;
        inventoryPanel.style.marginLeft = 0;
        inventoryPanel.style.marginTop = 0;

        inventoryDragHandle?.CapturePointer(evt.pointerId);
        evt.StopPropagation();
    }

    private void OnInventoryDragMove(PointerMoveEvent evt)
    {
        if (!isDraggingInventory || evt.pointerId != activeDragPointerId || inventoryPanel == null)
            return;

        Vector2 pointerPos = new Vector2(evt.position.x, evt.position.y);
        Vector2 delta = pointerPos - dragStartPointerPos;
        float targetLeft = dragStartPanelPos.x + delta.x;
        float targetTop = dragStartPanelPos.y + delta.y;

        var root = uiDocument != null ? uiDocument.rootVisualElement : null;
        if (root != null)
        {
            float maxLeft = Mathf.Max(0f, root.resolvedStyle.width - inventoryPanel.resolvedStyle.width);
            float maxTop = Mathf.Max(0f, root.resolvedStyle.height - inventoryPanel.resolvedStyle.height);
            targetLeft = Mathf.Clamp(targetLeft, 0f, maxLeft);
            targetTop = Mathf.Clamp(targetTop, 0f, maxTop);
        }

        inventoryPanel.style.left = targetLeft;
        inventoryPanel.style.top = targetTop;
        evt.StopPropagation();
    }

    private void OnInventoryDragEnd(PointerUpEvent evt)
    {
        if (evt.pointerId != activeDragPointerId)
            return;

        StopInventoryDrag();
        evt.StopPropagation();
    }

    private void StopInventoryDrag()
    {
        if (inventoryDragHandle != null && activeDragPointerId >= 0 && inventoryDragHandle.HasPointerCapture(activeDragPointerId))
            inventoryDragHandle.ReleasePointer(activeDragPointerId);

        isDraggingInventory = false;
        activeDragPointerId = -1;
    }

    private void SetActiveTab(InventoryTab tab, bool force = false)
    {
        if (!force && ActiveTab == tab)
            return;

        ActiveTab = tab;

        if (inventoryTabContent != null)
            inventoryTabContent.style.display = tab == InventoryTab.Inventory ? DisplayStyle.Flex : DisplayStyle.None;

        if (skillsTabContent != null)
            skillsTabContent.style.display = tab == InventoryTab.Skills ? DisplayStyle.Flex : DisplayStyle.None;

        if (injuryTabContent != null)
            injuryTabContent.style.display = tab == InventoryTab.Injury ? DisplayStyle.Flex : DisplayStyle.None;

        SetTabButtonStyle(inventoryTabButton, tab == InventoryTab.Inventory);
        SetTabButtonStyle(skillsTabButton, tab == InventoryTab.Skills);
        SetTabButtonStyle(injuryTabButton, tab == InventoryTab.Injury);

        if (inventoryTitle != null)
            inventoryTitle.text = tab switch
            {
                InventoryTab.Skills => "SKILLS",
                InventoryTab.Injury => "INJURY",
                _ => "INVENTORY"
            };

        if (tab == InventoryTab.Inventory)
            UpdateUI();
        else if (tab == InventoryTab.Skills)
            UpdateSkillsTab();
        else
            UpdateInjuryTab();

        OnActiveTabChanged?.Invoke(tab);
    }

    private static void SetTabButtonStyle(Button button, bool isActive)
    {
        if (button == null)
            return;

        button.style.backgroundColor = isActive
            ? new StyleColor(new Color(0.2f, 0.25f, 0.32f, 0.96f))
            : new StyleColor(new Color(0.12f, 0.15f, 0.2f, 0.96f));
        button.style.color = isActive
            ? new StyleColor(new Color(0.92f, 0.94f, 0.98f, 1f))
            : new StyleColor(new Color(0.72f, 0.78f, 0.88f, 1f));
        button.style.borderTopLeftRadius = 6;
        button.style.borderTopRightRadius = 6;
        button.style.borderBottomLeftRadius = 6;
        button.style.borderBottomRightRadius = 6;
    }

    private void SetInventoryOpen(bool open)
    {
        isOpen = open;
        IsInventoryOpen = open;

        if (uiDocument != null)
            uiDocument.rootVisualElement.style.display = open ? DisplayStyle.Flex : DisplayStyle.None;

        if (open)
        {
            SetActiveTab(InventoryTab.Inventory, true);
            UpdateUI();

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
            CancelItemDrag();
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
        if (inventory == null)
            return;

        StyleAllScrollViews();

        if (inventoryTitle != null)
        {
            inventoryTitle.text = ActiveTab switch
            {
                InventoryTab.Skills => "SKILLS",
                InventoryTab.Injury => "INJURY",
                _ => "INVENTORY"
            };
        }

        if (itemsScrollView != null)
        {
            itemsScrollView.Clear();
            inventoryCellsByIndex.Clear();

            var slots = inventory.GetAllItems();
            var root = uiDocument.rootVisualElement;
            int columns = Mathf.Max(1, inventory.GetGridColumns());
            int rows = Mathf.Max(1, inventory.GetGridRows());
            float gap = 6f;
            float viewportWidth = itemsScrollView.contentViewport != null
                ? itemsScrollView.contentViewport.resolvedStyle.width
                : itemsScrollView.resolvedStyle.width;
            if (viewportWidth <= 0f)
                viewportWidth = 700f;
            float cellSize = Mathf.Max(60f, (viewportWidth - ((columns - 1) * gap) - 4f) / columns);

            BuildInventoryGridLayered(slots, root, columns, rows, cellSize, gap);
        }

        if (ActiveTab == InventoryTab.Skills)
            UpdateSkillsTab();

        if (ActiveTab == InventoryTab.Injury)
            UpdateInjuryTab();
    }

    private void ConfigureInventoryGridLayout()
    {
        if (itemsScrollView == null)
            return;

        var container = itemsScrollView.contentContainer;
        container.style.flexDirection = FlexDirection.Row;
        container.style.flexWrap = Wrap.Wrap;
        container.style.alignContent = Align.FlexStart;
    }

    private void StyleAllScrollViews()
    {
        StyleScrollView(itemsScrollView);
        StyleScrollView(skillsScrollView);
        StyleScrollView(injuryScrollView);
    }

    private static void StyleScrollView(ScrollView scrollView)
    {
        if (scrollView == null)
            return;

        // Keep scrollbar full-height: move outer padding to the content container.
        scrollView.style.paddingTop = 0;
        scrollView.style.paddingRight = 0;
        scrollView.style.paddingBottom = 0;
        scrollView.style.paddingLeft = 0;

        if (scrollView.contentContainer != null)
        {
            scrollView.contentContainer.style.paddingTop = 8;
            scrollView.contentContainer.style.paddingRight = 8;
            scrollView.contentContainer.style.paddingBottom = 8;
            scrollView.contentContainer.style.paddingLeft = 8;
        }

        scrollView.verticalScrollerVisibility = ScrollerVisibility.AlwaysVisible;
        var verticalScroller = scrollView.verticalScroller;
        if (verticalScroller != null)
        {
            // Pin scroller to full ScrollView height.
            verticalScroller.style.position = Position.Absolute;
            verticalScroller.style.top = 0;
            verticalScroller.style.bottom = 0;
            verticalScroller.style.right = 0;
            verticalScroller.style.width = 10;
            verticalScroller.style.minWidth = 10;
            verticalScroller.style.backgroundColor = new StyleColor(new Color(0.08f, 0.11f, 0.15f, 0.9f));
            verticalScroller.style.borderTopLeftRadius = 4;
            verticalScroller.style.borderTopRightRadius = 4;
            verticalScroller.style.borderBottomLeftRadius = 4;
            verticalScroller.style.borderBottomRightRadius = 4;
            verticalScroller.style.paddingTop = 0;
            verticalScroller.style.paddingBottom = 0;
            verticalScroller.style.marginLeft = 2;
            verticalScroller.style.alignSelf = Align.Stretch;
            verticalScroller.style.height = StyleKeyword.Auto;

            var slider = verticalScroller.slider;
            if (slider != null)
            {
                slider.style.flexGrow = 1;
                slider.style.height = StyleKeyword.Auto;
                slider.style.minHeight = 0;
                slider.style.backgroundColor = new StyleColor(new Color(0.1f, 0.14f, 0.2f, 0.9f));
                slider.style.borderTopLeftRadius = 4;
                slider.style.borderTopRightRadius = 4;
                slider.style.borderBottomLeftRadius = 4;
                slider.style.borderBottomRightRadius = 4;
                slider.style.marginTop = 0;
                slider.style.marginBottom = 0;

                var tracker = slider.Q<VisualElement>(className: "unity-slider__tracker")
                              ?? slider.Q<VisualElement>(className: "unity-base-slider__tracker");
                if (tracker != null)
                {
                    tracker.style.backgroundColor = new StyleColor(new Color(0.1f, 0.14f, 0.2f, 0.9f));
                    tracker.style.borderTopLeftRadius = 4;
                    tracker.style.borderTopRightRadius = 4;
                    tracker.style.borderBottomLeftRadius = 4;
                    tracker.style.borderBottomRightRadius = 4;
                }

                var dragger = slider.Q<VisualElement>(className: "unity-dragger")
                              ?? slider.Q<VisualElement>(className: "unity-base-slider__dragger");
                if (dragger != null)
                {
                    dragger.style.backgroundColor = new StyleColor(new Color(0.36f, 0.45f, 0.58f, 1f));
                    dragger.style.unityBackgroundImageTintColor = new StyleColor(new Color(0.36f, 0.45f, 0.58f, 1f));
                    dragger.style.borderTopLeftRadius = 4;
                    dragger.style.borderTopRightRadius = 4;
                    dragger.style.borderBottomLeftRadius = 4;
                    dragger.style.borderBottomRightRadius = 4;
                    dragger.style.borderTopWidth = 0;
                    dragger.style.borderRightWidth = 0;
                    dragger.style.borderBottomWidth = 0;
                    dragger.style.borderLeftWidth = 0;
                }
            }

            var lowButton = verticalScroller.Q<VisualElement>(className: "unity-scroller__low-button");
            if (lowButton != null)
            {
                lowButton.style.display = DisplayStyle.None;
                lowButton.style.height = 0;
                lowButton.style.minHeight = 0;
            }

            var highButton = verticalScroller.Q<VisualElement>(className: "unity-scroller__high-button");
            if (highButton != null)
            {
                highButton.style.display = DisplayStyle.None;
                highButton.style.height = 0;
                highButton.style.minHeight = 0;
            }

            // Force-style any internal pieces Unity may generate with different class names.
            foreach (var part in verticalScroller.Query<VisualElement>().ToList())
            {
                if (part.ClassListContains("unity-base-slider__dragger") || part.ClassListContains("unity-dragger"))
                {
                    part.style.backgroundColor = new StyleColor(new Color(0.36f, 0.45f, 0.58f, 1f));
                    part.style.unityBackgroundImageTintColor = new StyleColor(new Color(0.36f, 0.45f, 0.58f, 1f));
                    part.style.borderTopWidth = 0;
                    part.style.borderRightWidth = 0;
                    part.style.borderBottomWidth = 0;
                    part.style.borderLeftWidth = 0;
                }
                else if (part.ClassListContains("unity-base-slider__tracker") || part.ClassListContains("unity-slider__tracker"))
                {
                    part.style.backgroundColor = new StyleColor(new Color(0.1f, 0.14f, 0.2f, 0.9f));
                }
                else
                {
                    // Safety net: remove remaining white defaults on internal visuals.
                    part.style.unityBackgroundImageTintColor = new StyleColor(new Color(0.1f, 0.14f, 0.2f, 1f));
                }
            }

            // Fallback for Unity variants where button class names differ.
            foreach (var btn in verticalScroller.Query<Button>().ToList())
            {
                btn.style.display = DisplayStyle.None;
                btn.style.height = 0;
                btn.style.minHeight = 0;
            }
        }

        scrollView.horizontalScrollerVisibility = ScrollerVisibility.AlwaysVisible;
        var horizontalScroller = scrollView.horizontalScroller;
        if (horizontalScroller != null)
        {
            // Match right scrollbar styling for bottom scrollbar.
            horizontalScroller.style.position = Position.Absolute;
            horizontalScroller.style.left = 0;
            horizontalScroller.style.right = 0;
            horizontalScroller.style.bottom = 0;
            horizontalScroller.style.height = 10;
            horizontalScroller.style.minHeight = 10;
            horizontalScroller.style.backgroundColor = new StyleColor(new Color(0.08f, 0.11f, 0.15f, 0.9f));
            horizontalScroller.style.borderTopLeftRadius = 4;
            horizontalScroller.style.borderTopRightRadius = 4;
            horizontalScroller.style.borderBottomLeftRadius = 4;
            horizontalScroller.style.borderBottomRightRadius = 4;
            horizontalScroller.style.paddingLeft = 0;
            horizontalScroller.style.paddingRight = 0;
            horizontalScroller.style.marginTop = 2;
            horizontalScroller.style.alignSelf = Align.Stretch;
            horizontalScroller.style.width = StyleKeyword.Auto;

            var slider = horizontalScroller.slider;
            if (slider != null)
            {
                slider.style.flexGrow = 1;
                slider.style.width = StyleKeyword.Auto;
                slider.style.minWidth = 0;
                slider.style.backgroundColor = new StyleColor(new Color(0.1f, 0.14f, 0.2f, 0.9f));
                slider.style.borderTopLeftRadius = 4;
                slider.style.borderTopRightRadius = 4;
                slider.style.borderBottomLeftRadius = 4;
                slider.style.borderBottomRightRadius = 4;
                slider.style.marginLeft = 0;
                slider.style.marginRight = 0;

                var tracker = slider.Q<VisualElement>(className: "unity-slider__tracker")
                              ?? slider.Q<VisualElement>(className: "unity-base-slider__tracker");
                if (tracker != null)
                {
                    tracker.style.backgroundColor = new StyleColor(new Color(0.1f, 0.14f, 0.2f, 0.9f));
                    tracker.style.borderTopLeftRadius = 4;
                    tracker.style.borderTopRightRadius = 4;
                    tracker.style.borderBottomLeftRadius = 4;
                    tracker.style.borderBottomRightRadius = 4;
                }

                var dragger = slider.Q<VisualElement>(className: "unity-dragger")
                              ?? slider.Q<VisualElement>(className: "unity-base-slider__dragger");
                if (dragger != null)
                {
                    dragger.style.backgroundColor = new StyleColor(new Color(0.36f, 0.45f, 0.58f, 1f));
                    dragger.style.unityBackgroundImageTintColor = new StyleColor(new Color(0.36f, 0.45f, 0.58f, 1f));
                    dragger.style.borderTopLeftRadius = 4;
                    dragger.style.borderTopRightRadius = 4;
                    dragger.style.borderBottomLeftRadius = 4;
                    dragger.style.borderBottomRightRadius = 4;
                    dragger.style.borderTopWidth = 0;
                    dragger.style.borderRightWidth = 0;
                    dragger.style.borderBottomWidth = 0;
                    dragger.style.borderLeftWidth = 0;
                }
            }

            var lowButton = horizontalScroller.Q<VisualElement>(className: "unity-scroller__low-button");
            if (lowButton != null)
            {
                lowButton.style.display = DisplayStyle.None;
                lowButton.style.width = 0;
                lowButton.style.minWidth = 0;
            }

            var highButton = horizontalScroller.Q<VisualElement>(className: "unity-scroller__high-button");
            if (highButton != null)
            {
                highButton.style.display = DisplayStyle.None;
                highButton.style.width = 0;
                highButton.style.minWidth = 0;
            }

            foreach (var part in horizontalScroller.Query<VisualElement>().ToList())
            {
                if (part.ClassListContains("unity-base-slider__dragger") || part.ClassListContains("unity-dragger"))
                {
                    part.style.backgroundColor = new StyleColor(new Color(0.36f, 0.45f, 0.58f, 1f));
                    part.style.unityBackgroundImageTintColor = new StyleColor(new Color(0.36f, 0.45f, 0.58f, 1f));
                    part.style.borderTopWidth = 0;
                    part.style.borderRightWidth = 0;
                    part.style.borderBottomWidth = 0;
                    part.style.borderLeftWidth = 0;
                }
                else if (part.ClassListContains("unity-base-slider__tracker") || part.ClassListContains("unity-slider__tracker"))
                {
                    part.style.backgroundColor = new StyleColor(new Color(0.1f, 0.14f, 0.2f, 0.9f));
                }
                else
                {
                    part.style.unityBackgroundImageTintColor = new StyleColor(new Color(0.1f, 0.14f, 0.2f, 1f));
                }
            }

            foreach (var btn in horizontalScroller.Query<Button>().ToList())
            {
                btn.style.display = DisplayStyle.None;
                btn.style.width = 0;
                btn.style.minWidth = 0;
            }
        }
    }

    private void BuildInventoryGridLayered(List<InventorySlot> slots, VisualElement root, int columns, int rows, float cellSize, float gap)
    {
        if (itemsScrollView == null)
            return;

        ConfigureInventoryGridLayout();

        var container = new VisualElement();
        container.name = "inventory-grid-container";
        container.style.position = Position.Relative;
        container.style.width = (columns * cellSize) + ((columns - 1) * gap);
        container.style.height = (rows * cellSize) + ((rows - 1) * gap);
        container.style.minWidth = container.style.width;
        container.style.minHeight = container.style.height;
        container.style.marginBottom = 2;

        // Base slot grid.
        for (int i = 0; i < slots.Count; i++)
        {
            var bg = CreateInventoryBackgroundCell(i, columns, cellSize, gap);
            inventoryCellsByIndex[i] = bg;
            container.Add(bg);
        }

        // Item overlays (anchor-only): clean 1x1 or 2x2 visual blocks.
        for (int i = 0; i < slots.Count; i++)
        {
            InventorySlot slot = slots[i];
            if (slot == null || !slot.isOccupied || !slot.isAnchor || slot.item == null)
                continue;

            var tile = CreateInventoryItemTile(slot, i, columns, cellSize, gap, root);
            if (tile != null)
                container.Add(tile);
        }

        itemsScrollView.Add(container);
    }

    private VisualElement CreateInventoryBackgroundCell(int slotIndex, int columns, float cellSize, float gap)
    {
        int col = slotIndex % columns;
        int row = slotIndex / columns;

        var cell = new VisualElement();
        cell.style.position = Position.Absolute;
        cell.style.left = col * (cellSize + gap);
        cell.style.top = row * (cellSize + gap);
        cell.style.width = cellSize;
        cell.style.height = cellSize;
        cell.style.backgroundColor = new StyleColor(new Color(0.1f, 0.13f, 0.18f, 0.7f));
        cell.style.borderTopWidth = 1;
        cell.style.borderRightWidth = 1;
        cell.style.borderBottomWidth = 1;
        cell.style.borderLeftWidth = 1;
        cell.style.borderTopColor = new StyleColor(new Color(0.66f, 0.72f, 0.84f, 0.16f));
        cell.style.borderRightColor = new StyleColor(new Color(0.66f, 0.72f, 0.84f, 0.16f));
        cell.style.borderBottomColor = new StyleColor(new Color(0.66f, 0.72f, 0.84f, 0.12f));
        cell.style.borderLeftColor = new StyleColor(new Color(0.66f, 0.72f, 0.84f, 0.12f));
        cell.style.borderTopLeftRadius = 5;
        cell.style.borderTopRightRadius = 5;
        cell.style.borderBottomLeftRadius = 5;
        cell.style.borderBottomRightRadius = 5;

        var slotLabel = new Label((slotIndex + 1).ToString());
        slotLabel.style.position = Position.Absolute;
        slotLabel.style.left = 3;
        slotLabel.style.top = 2;
        slotLabel.style.fontSize = 8;
        slotLabel.style.color = new StyleColor(new Color(0.68f, 0.73f, 0.83f, 0.58f));
        cell.Add(slotLabel);

        return cell;
    }

    private VisualElement CreateInventoryItemTile(InventorySlot slot, int anchorIndex, int columns, float cellSize, float gap, VisualElement root)
    {
        if (slot == null || slot.item == null)
            return null;

        int width = Mathf.Max(1, slot.footprintWidth);
        int height = Mathf.Max(1, slot.footprintHeight);
        int col = anchorIndex % columns;
        int row = anchorIndex / columns;

        float tileWidth = (width * cellSize) + ((width - 1) * gap);
        float tileHeight = (height * cellSize) + ((height - 1) * gap);

        var tile = new VisualElement();
        tile.name = $"inventory-item-tile-{anchorIndex}";
        tile.style.position = Position.Absolute;
        tile.style.left = col * (cellSize + gap);
        tile.style.top = row * (cellSize + gap);
        tile.style.width = tileWidth;
        tile.style.height = tileHeight;
        tile.style.backgroundColor = new StyleColor(new Color(0.14f, 0.18f, 0.24f, 0.95f));
        tile.style.borderTopWidth = 1;
        tile.style.borderRightWidth = 1;
        tile.style.borderBottomWidth = 1;
        tile.style.borderLeftWidth = 1;
        tile.style.borderTopColor = new StyleColor(new Color(0.66f, 0.72f, 0.84f, 0.22f));
        tile.style.borderRightColor = new StyleColor(new Color(0.66f, 0.72f, 0.84f, 0.22f));
        tile.style.borderBottomColor = new StyleColor(new Color(0.66f, 0.72f, 0.84f, 0.18f));
        tile.style.borderLeftColor = new StyleColor(new Color(0.66f, 0.72f, 0.84f, 0.18f));
        tile.style.borderTopLeftRadius = 5;
        tile.style.borderTopRightRadius = 5;
        tile.style.borderBottomLeftRadius = 5;
        tile.style.borderBottomRightRadius = 5;

        if (slot.item.Icon != null)
        {
            var icon = new Image();
            icon.image = slot.item.Icon.texture;
            icon.scaleMode = ScaleMode.ScaleToFit;
            icon.style.position = Position.Absolute;
            icon.style.left = 3;
            icon.style.right = 3;
            icon.style.top = 3;
            icon.style.bottom = 3;
            icon.pickingMode = PickingMode.Ignore;
            tile.Add(icon);
        }

        if (slot.quantity > 1)
        {
            var qtyLabel = new Label($"x{slot.quantity}");
            qtyLabel.style.position = Position.Absolute;
            qtyLabel.style.right = 5;
            qtyLabel.style.bottom = 3;
            qtyLabel.style.fontSize = 10;
            qtyLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
            qtyLabel.style.color = new StyleColor(new Color(0.92f, 0.95f, 1f, 1f));
            qtyLabel.pickingMode = PickingMode.Ignore;
            tile.Add(qtyLabel);
        }

        tile.tooltip = $"{slot.item.ItemName} x{slot.quantity}";

        tile.RegisterCallback<MouseUpEvent>(evt =>
        {
            if (evt.button == 1)
                ShowInventoryContextMenu(slot, anchorIndex, root, evt);
        });

        tile.RegisterCallback<PointerDownEvent>(evt =>
        {
            if (evt.button != 0)
                return;

            BeginItemDragCandidate(slot.item, slot.quantity, anchorIndex, evt.pointerId, evt.position);
            evt.StopPropagation();
        });

        return tile;
    }

    private void ShowInventoryContextMenu(InventorySlot slot, int slotIndex, VisualElement root, MouseUpEvent evt)
    {
        if (slot == null || slot.item == null)
            return;

        var existingMenu = root.Q("inventory-context-menu");
        if (existingMenu != null)
            existingMenu.RemoveFromHierarchy();

        Vector2 clickLocalToRoot = root.WorldToLocal(evt.mousePosition);

        var menu = new VisualElement { name = "inventory-context-menu" };
        menu.style.position = Position.Absolute;
        menu.style.left = clickLocalToRoot.x;
        menu.style.top = clickLocalToRoot.y;
        menu.style.backgroundColor = new StyleColor(new Color(0.1f, 0.14f, 0.19f, 0.98f));
        menu.style.borderTopLeftRadius = 5;
        menu.style.borderTopRightRadius = 5;
        menu.style.borderBottomLeftRadius = 5;
        menu.style.borderBottomRightRadius = 5;
        menu.style.borderTopWidth = 1;
        menu.style.borderBottomWidth = 1;
        menu.style.borderLeftWidth = 1;
        menu.style.borderRightWidth = 1;
        menu.style.borderTopColor = new StyleColor(new Color(0.68f, 0.74f, 0.84f, 0.2f));
        menu.style.borderBottomColor = new StyleColor(new Color(0.68f, 0.74f, 0.84f, 0.2f));
        menu.style.borderLeftColor = new StyleColor(new Color(0.68f, 0.74f, 0.84f, 0.2f));
        menu.style.borderRightColor = new StyleColor(new Color(0.68f, 0.74f, 0.84f, 0.2f));
        menu.style.paddingTop = 6;
        menu.style.paddingBottom = 6;
        menu.style.paddingLeft = 10;
        menu.style.paddingRight = 10;
        menu.style.flexDirection = FlexDirection.Column;
        menu.style.minWidth = 180;
        menu.pickingMode = PickingMode.Position;

        if (equipmentManager == null)
            equipmentManager = FindAnyObjectByType<EquipmentManager>();

        AddContextActions(slot, slotIndex, menu);

        System.Action removeMenu = () =>
        {
            if (menu.parent != null)
                menu.RemoveFromHierarchy();
        };

        root.RegisterCallback<MouseDownEvent>((MouseDownEvent e) =>
        {
            if (!menu.worldBound.Contains(e.mousePosition))
                removeMenu();
        });
        root.RegisterCallback<WheelEvent>((WheelEvent _) => removeMenu());

        root.Add(menu);

        // Keep menu inside the root bounds after layout is calculated.
        menu.schedule.Execute(() =>
        {
            float maxLeft = Mathf.Max(0f, root.resolvedStyle.width - menu.resolvedStyle.width - 2f);
            float maxTop = Mathf.Max(0f, root.resolvedStyle.height - menu.resolvedStyle.height - 2f);
            menu.style.left = Mathf.Clamp(clickLocalToRoot.x, 0f, maxLeft);
            menu.style.top = Mathf.Clamp(clickLocalToRoot.y, 0f, maxTop);
        }).ExecuteLater(0);
    }

    private void AddContextActions(InventorySlot slot, int slotIndex, VisualElement menu)
    {
        if (slot.item is GunItem gun && equipmentManager != null)
        {
            if (gun.GunType == Gun.GunType.Pistol)
            {
                AddContextActionLabel(menu, "Equip as Secondary", () => equipmentManager.EquipAsSecondary(gun));
                AddContextActionLabel(menu, "Dequip Secondary", () => equipmentManager.EquipAsSecondary(null));
            }
            else
            {
                AddContextActionLabel(menu, "Equip as Primary", () => equipmentManager.EquipAsPrimary(gun));
                AddContextActionLabel(menu, "Dequip Primary", () => equipmentManager.EquipAsPrimary(null));
            }
        }

        if (slot.item is MeleeWeaponItem melee && equipmentManager != null)
        {
            AddContextActionLabel(menu, "Equip as Melee Slot", () => equipmentManager.EquipMeleeWeapon(melee));
            AddContextActionLabel(menu, "Select Melee (3)", () => equipmentManager.SelectMelee());
            AddContextActionLabel(menu, "Unequip Melee Slot", () => equipmentManager.EquipMeleeWeapon(null));
        }

        AddContextActionLabel(menu, "Drop", () =>
        {
            if (inventory == null)
                return;

            if (!inventory.DropItemAtSlot(slotIndex, 1))
                inventory.DropItem(slot.item, 1);
        });

        if (slot.quantity > 1)
            AddContextActionLabel(menu, "Drop Stack", () =>
            {
                if (inventory == null)
                    return;

                if (!inventory.DropItemAtSlot(slotIndex, slot.quantity))
                    inventory.DropItem(slot.item, slot.quantity);
            });
    }

    private void AddContextActionLabel(VisualElement menu, string text, Action callback)
    {
        var actionLabel = new Label(text);
        actionLabel.style.unityTextAlign = TextAnchor.MiddleLeft;
        actionLabel.style.fontSize = 12;
        actionLabel.style.color = new StyleColor(new Color(0.89f, 0.93f, 0.98f, 1f));
        actionLabel.style.paddingTop = 5;
        actionLabel.style.paddingBottom = 5;
        actionLabel.style.paddingLeft = 8;
        actionLabel.style.paddingRight = 8;
        actionLabel.style.marginBottom = 4;
        actionLabel.style.backgroundColor = new StyleColor(new Color(0.18f, 0.23f, 0.3f, 0.9f));
        actionLabel.style.borderTopLeftRadius = 4;
        actionLabel.style.borderTopRightRadius = 4;
        actionLabel.style.borderBottomLeftRadius = 4;
        actionLabel.style.borderBottomRightRadius = 4;
        actionLabel.RegisterCallback<MouseUpEvent>(_ =>
        {
            callback?.Invoke();
            var root = uiDocument != null ? uiDocument.rootVisualElement : null;
            var existingMenu = root != null ? root.Q("inventory-context-menu") : null;
            if (existingMenu != null)
                existingMenu.RemoveFromHierarchy();
        });
        menu.Add(actionLabel);
    }

    private void BeginItemDragCandidate(Item item, int quantity, int sourceSlotIndex, int pointerId, Vector2 pointerPos)
    {
        if (item == null || quantity <= 0)
            return;

        isItemDragPending = true;
        isItemDragging = false;
        itemDragPointerId = pointerId;
        itemDragSourceSlotIndex = sourceSlotIndex;
        itemDragItem = item;
        itemDragQuantity = quantity;
        itemDragStartMouse = pointerPos;

        if (rootVisual != null)
            rootVisual.CapturePointer(pointerId);
    }

    private void OnRootPointerMoveItemDrag(PointerMoveEvent evt)
    {
        if (!isItemDragPending || evt.pointerId != itemDragPointerId)
            return;

        Vector2 pointerPos = new Vector2(evt.position.x, evt.position.y);

        if (!isItemDragging)
        {
            Vector2 delta = pointerPos - itemDragStartMouse;
            if (delta.sqrMagnitude < ItemDragStartThreshold * ItemDragStartThreshold)
                return;

            StartItemDragGhost();
            isItemDragging = true;
        }

        UpdateItemDragGhostPosition(pointerPos);
    }

    private void OnRootPointerUpItemDrag(PointerUpEvent evt)
    {
        if (evt.button != 0 || evt.pointerId != itemDragPointerId)
            return;

        if (!isItemDragPending)
            return;

        bool handledInInventory = false;
        bool shouldDrop = false;
        if (isItemDragging)
        {
            bool releasedInsideInventory = inventoryPanel != null && inventoryPanel.worldBound.Contains(evt.position);
            if (releasedInsideInventory && inventory != null)
            {
                int targetSlot = GetInventorySlotIndexAtWorldPosition(evt.position);
                if (targetSlot >= 0)
                    handledInInventory = inventory.MoveItem(itemDragSourceSlotIndex, targetSlot);
            }

            // Drop only when released outside inventory and not placed inside.
            shouldDrop = !handledInInventory && (inventoryPanel == null || !inventoryPanel.worldBound.Contains(evt.position));
        }

        if (shouldDrop && inventory != null && itemDragItem != null)
        {
            bool dropStack = (evt.modifiers & EventModifiers.Shift) != 0;
            int qty = dropStack ? Mathf.Max(1, itemDragQuantity) : 1;
            if (!inventory.DropItemAtSlot(itemDragSourceSlotIndex, qty))
                inventory.DropItem(itemDragItem, qty);
        }

        CancelItemDrag();
    }

    private int GetInventorySlotIndexAtWorldPosition(Vector2 worldPosition)
    {
        if (inventoryCellsByIndex == null || inventoryCellsByIndex.Count == 0)
            return -1;

        foreach (var kvp in inventoryCellsByIndex)
        {
            if (kvp.Value != null && kvp.Value.worldBound.Contains(worldPosition))
                return kvp.Key;
        }

        return -1;
    }

    private void StartItemDragGhost()
    {
        if (rootVisual == null || itemDragItem == null)
            return;

        itemDragGhost = new VisualElement();
        itemDragGhost.name = "inventory-item-drag-ghost";
        itemDragGhost.style.position = Position.Absolute;
        itemDragGhost.style.width = 52;
        itemDragGhost.style.height = 52;
        itemDragGhost.style.backgroundColor = new StyleColor(new Color(0.12f, 0.16f, 0.22f, 0.88f));
        itemDragGhost.style.borderTopLeftRadius = 6;
        itemDragGhost.style.borderTopRightRadius = 6;
        itemDragGhost.style.borderBottomLeftRadius = 6;
        itemDragGhost.style.borderBottomRightRadius = 6;
        itemDragGhost.style.borderTopWidth = 1;
        itemDragGhost.style.borderRightWidth = 1;
        itemDragGhost.style.borderBottomWidth = 1;
        itemDragGhost.style.borderLeftWidth = 1;
        itemDragGhost.style.borderTopColor = new StyleColor(new Color(0.7f, 0.78f, 0.9f, 0.35f));
        itemDragGhost.style.borderRightColor = new StyleColor(new Color(0.7f, 0.78f, 0.9f, 0.35f));
        itemDragGhost.style.borderBottomColor = new StyleColor(new Color(0.7f, 0.78f, 0.9f, 0.2f));
        itemDragGhost.style.borderLeftColor = new StyleColor(new Color(0.7f, 0.78f, 0.9f, 0.2f));
        itemDragGhost.pickingMode = PickingMode.Ignore;

        var icon = new Image();
        icon.image = itemDragItem.Icon != null ? itemDragItem.Icon.texture : null;
        icon.scaleMode = ScaleMode.ScaleToFit;
        icon.style.width = 42;
        icon.style.height = 42;
        icon.style.marginLeft = 5;
        icon.style.marginTop = 5;
        itemDragGhost.Add(icon);

        if (itemDragQuantity > 1)
        {
            var qty = new Label($"x{itemDragQuantity}");
            qty.style.position = Position.Absolute;
            qty.style.right = 4;
            qty.style.bottom = 2;
            qty.style.fontSize = 10;
            qty.style.unityFontStyleAndWeight = FontStyle.Bold;
            qty.style.color = new StyleColor(new Color(0.95f, 0.97f, 1f, 1f));
            itemDragGhost.Add(qty);
        }

        rootVisual.Add(itemDragGhost);
    }

    private void UpdateItemDragGhostPosition(Vector2 mouseWorldPos)
    {
        if (itemDragGhost == null)
            return;

        itemDragGhost.style.left = mouseWorldPos.x - 26f;
        itemDragGhost.style.top = mouseWorldPos.y - 26f;
    }

    private void CancelItemDrag()
    {
        isItemDragPending = false;
        isItemDragging = false;
        if (rootVisual != null && itemDragPointerId >= 0 && rootVisual.HasPointerCapture(itemDragPointerId))
            rootVisual.ReleasePointer(itemDragPointerId);
        itemDragPointerId = -1;
        itemDragSourceSlotIndex = -1;
        itemDragItem = null;
        itemDragQuantity = 0;

        if (itemDragGhost != null)
        {
            itemDragGhost.RemoveFromHierarchy();
            itemDragGhost = null;
        }
    }

    private void UpdateSkillsTab()
    {
        if (skillsScrollView == null)
            return;

        skillsScrollView.Clear();

        if (playerSkills == null)
        {
            skillsScrollView.Add(CreateInfoLabel("Player skills component not found."));
            return;
        }

        Gun.GunType displayedGunType = Gun.GunType.Pistol;
        if (equipmentManager != null)
        {
            Gun currentWeapon = equipmentManager.GetCurrentWeapon();
            if (currentWeapon != null)
                displayedGunType = currentWeapon.GetGunType();
        }

        skillsScrollView.Add(CreateSkillCard(
            $"{displayedGunType} Accuracy",
            playerSkills.GetSkillLevel(displayedGunType),
            playerSkills.GetCurrentXP(displayedGunType),
            playerSkills.GetXPNeeded(displayedGunType),
            new Color(1f, 0.78f, 0.4f, 1f)));

        skillsScrollView.Add(CreateSkillCard(
            "Stamina",
            playerSkills.GetGeneralSkillLevel(PlayerSkills.SkillType.Stamina),
            playerSkills.GetGeneralSkillXP(PlayerSkills.SkillType.Stamina),
            playerSkills.GetGeneralSkillXPNeeded(PlayerSkills.SkillType.Stamina),
            new Color(0.45f, 0.82f, 1f, 1f)));

        skillsScrollView.Add(CreateSkillCard(
            "Metabolism",
            playerSkills.GetGeneralSkillLevel(PlayerSkills.SkillType.Metabolism),
            playerSkills.GetGeneralSkillXP(PlayerSkills.SkillType.Metabolism),
            playerSkills.GetGeneralSkillXPNeeded(PlayerSkills.SkillType.Metabolism),
            new Color(1f, 0.63f, 0.42f, 1f)));

        skillsScrollView.Add(CreateSkillCard(
            "Vitality",
            playerSkills.GetGeneralSkillLevel(PlayerSkills.SkillType.Vitality),
            playerSkills.GetGeneralSkillXP(PlayerSkills.SkillType.Vitality),
            playerSkills.GetGeneralSkillXPNeeded(PlayerSkills.SkillType.Vitality),
            new Color(1f, 0.45f, 0.45f, 1f)));

        skillsScrollView.Add(CreateSkillCard(
            "Stealth",
            playerSkills.GetGeneralSkillLevel(PlayerSkills.SkillType.Stealth),
            playerSkills.GetGeneralSkillXP(PlayerSkills.SkillType.Stealth),
            playerSkills.GetGeneralSkillXPNeeded(PlayerSkills.SkillType.Stealth),
            new Color(0.76f, 0.55f, 1f, 1f)));

        skillsScrollView.Add(CreateSkillCard(
            "Strength",
            playerSkills.GetGeneralSkillLevel(PlayerSkills.SkillType.Strength),
            playerSkills.GetGeneralSkillXP(PlayerSkills.SkillType.Strength),
            playerSkills.GetGeneralSkillXPNeeded(PlayerSkills.SkillType.Strength),
            new Color(0.6f, 0.92f, 0.55f, 1f)));
    }

    private VisualElement CreateSkillCard(string skillName, int level, float currentXp, float neededXp, Color accentColor)
    {
        var card = new VisualElement();
        card.style.flexDirection = FlexDirection.Column;
        card.style.marginBottom = 6;
        card.style.paddingTop = 7;
        card.style.paddingRight = 10;
        card.style.paddingBottom = 7;
        card.style.paddingLeft = 10;
        card.style.backgroundColor = new StyleColor(new Color(0.15f, 0.2f, 0.28f, 0.94f));
        card.style.borderTopLeftRadius = 8;
        card.style.borderTopRightRadius = 8;
        card.style.borderBottomLeftRadius = 8;
        card.style.borderBottomRightRadius = 8;
        card.style.borderTopWidth = 1;
        card.style.borderRightWidth = 1;
        card.style.borderBottomWidth = 1;
        card.style.borderLeftWidth = 1;
        card.style.borderTopColor = new StyleColor(new Color(0.73f, 0.79f, 0.9f, 0.22f));
        card.style.borderRightColor = new StyleColor(new Color(0.73f, 0.79f, 0.9f, 0.22f));
        card.style.borderBottomColor = new StyleColor(new Color(0.73f, 0.79f, 0.9f, 0.14f));
        card.style.borderLeftColor = new StyleColor(new Color(0.73f, 0.79f, 0.9f, 0.14f));

        var top = new VisualElement();
        top.style.flexDirection = FlexDirection.Row;
        top.style.justifyContent = Justify.SpaceBetween;
        top.style.alignItems = Align.Center;

        var nameLabel = new Label(skillName);
        nameLabel.style.color = new StyleColor(accentColor);
        nameLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
        nameLabel.style.fontSize = 13;

        var levelLabel = new Label($"Lvl {level}");
        levelLabel.style.color = new StyleColor(new Color(0.95f, 0.95f, 1f, 1f));
        levelLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
        levelLabel.style.fontSize = 12;

        top.Add(nameLabel);
        top.Add(levelLabel);
        card.Add(top);

        var barBackground = new VisualElement();
        barBackground.style.height = 14;
        barBackground.style.marginTop = 7;
        barBackground.style.backgroundColor = new StyleColor(new Color(0.05f, 0.07f, 0.1f, 1f));
        barBackground.style.borderTopLeftRadius = 2;
        barBackground.style.borderTopRightRadius = 2;
        barBackground.style.borderBottomLeftRadius = 2;
        barBackground.style.borderBottomRightRadius = 2;
        barBackground.style.position = Position.Relative;

        var fill = new VisualElement();
        float progress = neededXp <= 0f ? 0f : Mathf.Clamp01(currentXp / neededXp);
        fill.style.height = Length.Percent(100);
        fill.style.width = Length.Percent(progress * 100f);
        fill.style.backgroundColor = new StyleColor(accentColor);
        fill.style.borderTopLeftRadius = 2;
        fill.style.borderTopRightRadius = 2;
        fill.style.borderBottomLeftRadius = 2;
        fill.style.borderBottomRightRadius = 2;
        barBackground.Add(fill);

        var xpLabel = new Label($"{currentXp:F0} / {neededXp:F0}");
        xpLabel.style.position = Position.Absolute;
        xpLabel.style.right = 6;
        xpLabel.style.top = -3;
        xpLabel.style.fontSize = 9;
        xpLabel.style.color = new StyleColor(Color.white);
        xpLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
        barBackground.Add(xpLabel);

        card.Add(barBackground);
        return card;
    }

    private void UpdateInjuryTab()
    {
        if (injuryScrollView == null)
            return;

        injuryScrollView.Clear();

        if (injurySystem == null)
        {
            injuryScrollView.Add(CreateInfoLabel("Injury system component not found."));
            return;
        }

        var activeInjuries = injurySystem.GetActiveInjuries();

        var summary = new Label($"Total Injuries: {activeInjuries.Count}    Infected: {injurySystem.GetInfectedInjuryCount()}    Bleeding: {injurySystem.GetBleedingInjuryCount()}");
        summary.style.fontSize = 12;
        summary.style.color = new StyleColor(new Color(0.84f, 0.9f, 1f, 0.95f));
        summary.style.unityFontStyleAndWeight = FontStyle.Bold;
        summary.style.marginBottom = 8;
        injuryScrollView.Add(summary);

        if (activeInjuries.Count == 0)
        {
            injuryScrollView.Add(CreateInfoLabel("No active injuries. You are healthy."));
            return;
        }

        foreach (var injury in activeInjuries)
        {
            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Column;
            row.style.marginBottom = 8;
            row.style.paddingTop = 7;
            row.style.paddingRight = 10;
            row.style.paddingBottom = 7;
            row.style.paddingLeft = 10;
            row.style.backgroundColor = new StyleColor(GetInjuryBackgroundColor(injury));
            row.style.borderTopLeftRadius = 3;
            row.style.borderTopRightRadius = 3;
            row.style.borderBottomLeftRadius = 3;
            row.style.borderBottomRightRadius = 3;

            var title = new Label($"{injury.injuryType} - {injury.bodyPart}");
            title.style.fontSize = 13;
            title.style.unityFontStyleAndWeight = FontStyle.Bold;
            title.style.color = new StyleColor(Color.white);
            row.Add(title);

            string status = injury.isInfected ? "INFECTED" : "Clean";
            status += injury.isBandaged ? " • Bandaged" : " • Bleeding";
            if (injury.isHealing)
                status += $" • Healing {Mathf.RoundToInt(injury.GetHealingProgress01() * 100f)}%";

            var statusLabel = new Label(status);
            statusLabel.style.fontSize = 11;
            statusLabel.style.color = new StyleColor(new Color(0.86f, 0.91f, 1f, 0.9f));
            statusLabel.style.marginTop = 2;
            row.Add(statusLabel);

            injuryScrollView.Add(row);
        }
    }

    private static Color GetInjuryBackgroundColor(Injury injury)
    {
        if (injury.isInfected)
            return new Color(0.27f, 0.13f, 0.14f, 0.92f);

        return injury.injuryType switch
        {
            InjuryType.Bitten => new Color(0.3f, 0.16f, 0.15f, 0.9f),
            InjuryType.Laceration => new Color(0.28f, 0.21f, 0.16f, 0.9f),
            _ => new Color(0.16f, 0.24f, 0.18f, 0.9f)
        };
    }

    private static Label CreateInfoLabel(string text)
    {
        var label = new Label(text);
        label.style.fontSize = 14;
        label.style.color = new StyleColor(new Color(0.84f, 0.88f, 0.96f, 1f));
        label.style.whiteSpace = WhiteSpace.Normal;
        label.style.unityTextAlign = TextAnchor.UpperLeft;
        return label;
    }
}
