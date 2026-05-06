using UnityEngine;
using UnityEngine.UIElements;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
using UnityEngine.EventSystems;
using System.Collections.Generic;
using System.Linq;
using System;

public class InventoryUIToolkit : MonoBehaviour
{
    public enum InventoryTab { Inventory, Skills, Injury, Crafting }

    private enum CraftingCategoryTab
    {
        Ammunition,
        Weapons,
        HealingItems,
        Food,
        Water
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
    [SerializeField] private InputActionReference toggleInventoryAction;
    [Header("Crafting")]
    [SerializeField] private CraftingSystem craftingSystem;

    private VisualElement rootVisual;
    private VisualElement inventoryPanel;
    private VisualElement tabContentInventory;
    private VisualElement tabContentSkills;
    private VisualElement tabContentInjury;
    private VisualElement tabContentCrafting;
    // inventory drag handle removed; UI is static
    private ScrollView itemsScrollView;
    private VisualElement itemDetailPanel;
    private Image detailItemIcon;
    private Label detailItemName;
    private Label detailItemDescription;
    private ScrollView detailItemProperties;
    private Item selectedItem;
    private int selectedItemSlotIndex = -1;
    private ScrollView skillsScrollView;
    private ScrollView injuryScrollView;
    private ScrollView craftingScrollView;
    private Button inventoryTabButton;
    private Button skillsTabButton;
    private Button injuryTabButton;
    private Button craftingTabButton;
    private CraftingCategoryTab activeCraftingCategory = CraftingCategoryTab.Ammunition;
    
    [SerializeField]
    private float uiBaseScale = 1.4f; // multiplier to tweak overall UI sizing (increase default for bigger tiles)
    private float uiScale = 1f;

    private readonly Dictionary<int, VisualElement> inventoryCellsByIndex = new();

    private bool isItemDragPending;
    private bool isItemDragging;
    private int itemDragPointerId = -1;
    private int itemDragSourceSlotIndex = -1;
    private Item itemDragItem;
    private int itemDragQuantity;
    private Vector2 itemDragStartMouse;
    private VisualElement itemDragGhost;
    private int highlightedDropSlotIndex = -1;
    private const float ItemDragStartThreshold = 8f;
    private InputAction runtimeToggleAction;
    private float originalDocumentSortingOrder;
    private bool hasCapturedDocumentSortingOrder;
    private readonly Dictionary<UIDocument, PickingMode> suppressedOtherDocumentPickingModes = new();
    private float lastInventoryViewportWidth;
    private int lastScreenWidth;
    private int lastScreenHeight;

    // window dragging disabled for static inventory UI

    private void Awake()
    {
        EnsureRuntimeUIInput();
        ResolveReferences();
        if (uiDocument == null) return;

        rootVisual = uiDocument.rootVisualElement;
        CacheUiReferences();
        HookTabButtons();
        ApplyInventoryScrollStyle(itemsScrollView);
        ApplyInventoryScrollStyle(skillsScrollView);
        ApplyInventoryScrollStyle(injuryScrollView);
        ApplyInventoryScrollStyle(craftingScrollView);
        SetActiveTabInternal(InventoryTab.Inventory, false);
        SetInventoryPanelVisible(false);
        RefreshResponsiveScale(true);

        if (rootVisual != null)
        {
            rootVisual.pickingMode = PickingMode.Position;
            ItemTooltipUtility.EnsureTooltipPanel(rootVisual);
        }

        rootVisual.RegisterCallback<PointerMoveEvent>(OnRootPointerMoveItemDrag);
        rootVisual.RegisterCallback<PointerUpEvent>(OnRootPointerUpItemDrag);
        rootVisual.RegisterCallback<PointerDownEvent>(OnRootPointerDownInventoryInput);
        
        Debug.Log("[InventoryUIToolkit] Initialized. Panel: " + (inventoryPanel != null) + ", Root: " + (rootVisual != null));
    }

    public static void EnsureRuntimeUIInput()
    {
        var eventSystem = EventSystem.current;
        if (eventSystem == null)
        {
            var go = new GameObject("EventSystem");
            eventSystem = go.AddComponent<EventSystem>();
            go.AddComponent<InputSystemUIInputModule>();
            DontDestroyOnLoad(go);
            Debug.Log("[InventoryUIToolkit] Created runtime EventSystem with InputSystemUIInputModule for UI clicks.");
        }

        var inputModule = eventSystem.GetComponent<InputSystemUIInputModule>();
        if (inputModule == null)
        {
            inputModule = eventSystem.gameObject.AddComponent<InputSystemUIInputModule>();
            Debug.Log("[InventoryUIToolkit] Added InputSystemUIInputModule to existing EventSystem.");
        }

        if (inputModule != null)
        {
            // Ensure Point/Click/Scroll actions exist even if project UI actions were changed.
            inputModule.AssignDefaultActions();
            inputModule.enabled = true;
        }

        var playerInput = FindAnyObjectByType<PlayerInput>();
        if (playerInput != null && inputModule != null && playerInput.uiInputModule != inputModule)
            playerInput.uiInputModule = inputModule;
    }

    private void OnEnable()
    {
        SubscribeInventoryEvents();
        BindToggleInput();
    }

    private void OnDisable()
    {
        UnbindToggleInput();
        UnsubscribeInventoryEvents();
        CancelItemDrag();
    }

    private void OnDestroy()
    {
        UnsubscribeInventoryEvents();

        // inventory drag callbacks removed

        if (rootVisual != null)
        {
            rootVisual.UnregisterCallback<PointerMoveEvent>(OnRootPointerMoveItemDrag);
            rootVisual.UnregisterCallback<PointerUpEvent>(OnRootPointerUpItemDrag);
            rootVisual.UnregisterCallback<PointerDownEvent>(OnRootPointerDownInventoryInput);
        }
    }

    private void Update()
    {
        RefreshResponsiveScale();
        ResolveReferences();

        if (!IsInventoryOpen)
            return;

        // Keep cursor usable while inventory is open in case another system relocks it.
        if (UnityEngine.Cursor.lockState != CursorLockMode.None)
            UnityEngine.Cursor.lockState = CursorLockMode.None;
        if (!UnityEngine.Cursor.visible)
            UnityEngine.Cursor.visible = true;

        // Avoid rebuilding tab UI every frame; this can interrupt click events.
        // Only keep crafting tab live-updated while an active craft is running.
        if (ActiveTab == InventoryTab.Crafting && craftingSystem != null && craftingSystem.IsCraftingInProgress)
            UpdateCraftingTab();
    }

    private void RefreshResponsiveScale(bool force = false)
    {
        if (!force && Screen.width == lastScreenWidth && Screen.height == lastScreenHeight)
            return;

        lastScreenWidth = Screen.width;
        lastScreenHeight = Screen.height;

        if (inventoryPanel == null)
            return;

        // If the serialized uiBaseScale was left at 1.0 in the Inspector, override it
        // so the default in-code size increase takes effect.
        if (Mathf.Approximately(uiBaseScale, 1f))
        {
            Debug.Log("[InventoryUIToolkit] uiBaseScale appears to be 1.0 (likely Inspector default). Overriding to 1.4 for better default sizing.");
            uiBaseScale = 1.4f;
        }

        // Compute a UI scale based on screen height relative to 1080p baseline, clamped for consistency.
        float raw = ((float)Screen.height / 1080f) * uiBaseScale;
        uiScale = Mathf.Clamp(raw, 0.8f, 1.6f);
        Debug.Log($"[InventoryUIToolkit] RefreshResponsiveScale: Screen={Screen.width}x{Screen.height}, uiBaseScale={uiBaseScale}, rawScale={raw}, uiScale={uiScale}");

        // Keep inventory anchored by layout percentages; avoid transform scaling that shifts position.
        inventoryPanel.style.scale = new Scale(Vector3.one);
    }

    private void BindToggleInput()
    {
        if (toggleInventoryAction != null)
        {
            runtimeToggleAction = toggleInventoryAction.action;
            Debug.Log("[InventoryUIToolkit] Using assigned toggleInventoryAction");
        }
        else
        {
            var playerInput = FindAnyObjectByType<PlayerInput>();
            if (playerInput != null)
            {
                runtimeToggleAction = playerInput.actions.FindAction("ToggleInventory")
                                     ?? playerInput.actions.FindAction("Inventory");
                Debug.Log("[InventoryUIToolkit] Found action from PlayerInput: " + (runtimeToggleAction != null));
            }
        }

        if (runtimeToggleAction != null)
        {
            Debug.Log($"[InventoryUIToolkit] runtimeToggleAction is: {runtimeToggleAction.name}, enabled before: {runtimeToggleAction.enabled}");
            runtimeToggleAction.performed += OnToggleInventory;
            if (!runtimeToggleAction.enabled)
                runtimeToggleAction.Enable();
            Debug.Log($"[InventoryUIToolkit] Input action bound successfully, enabled after: {runtimeToggleAction.enabled}");
        }
        else
        {
            Debug.LogWarning("[InventoryUIToolkit] Toggle action not found. Assign toggleInventoryAction or add 'ToggleInventory'/'Inventory' action.");
        }
    }

    private void UnbindToggleInput()
    {
        if (runtimeToggleAction == null)
            return;

        runtimeToggleAction.performed -= OnToggleInventory;

        if (toggleInventoryAction != null && runtimeToggleAction == toggleInventoryAction.action)
            runtimeToggleAction.Disable();

        runtimeToggleAction = null;
    }

    private void OnToggleInventory(InputAction.CallbackContext _)
    {
        if (PauseMenuUIToolkit.IsPaused)
            return;

        Debug.Log("[InventoryUIToolkit] OnToggleInventory called");
        ToggleInventory();
    }

    public void ToggleInventory() => SetInventoryOpen(!IsInventoryOpen);
    public void OpenFromExternal() => SetInventoryOpen(true);
    public void CloseFromExternal() => SetInventoryOpen(false);
    public bool IsOpenNow() => IsInventoryOpen;

    public void SetInventoryPanelVisible(bool visible)
    {
        if (inventoryPanel != null)
        {
            inventoryPanel.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
            // Allow pointer events when visible, block when hidden
            inventoryPanel.pickingMode = visible ? PickingMode.Position : PickingMode.Ignore;
        }
    }

    public void SetActiveTab(InventoryTab tab) => SetActiveTabInternal(tab, true);

    private void ResolveReferences()
    {
        if (player == null || !player.gameObject.activeInHierarchy)
            player = FindAnyObjectByType<Player>();

        if ((inventory == null || !inventory.gameObject.activeInHierarchy) && player != null)
            inventory = player.GetComponent<Inventory>();

        if (inventory == null)
            inventory = FindAnyObjectByType<Inventory>();

        if (equipmentManager == null) equipmentManager = FindAnyObjectByType<EquipmentManager>();
        if (playerSkills == null) playerSkills = FindAnyObjectByType<PlayerSkills>();
        if ((craftingSystem == null || !craftingSystem.gameObject.activeInHierarchy) && player != null)
            craftingSystem = player.GetComponent<CraftingSystem>();
        if (craftingSystem == null && inventory != null)
            craftingSystem = inventory.GetComponent<CraftingSystem>();
        if (craftingSystem == null)
            craftingSystem = FindAnyObjectByType<CraftingSystem>();
        if (craftingSystem != null && inventory != null)
            craftingSystem.SetRuntimeInventory(inventory);
        if (uiDocument == null) uiDocument = GetComponent<UIDocument>() ?? FindAnyObjectByType<UIDocument>();
    }

    private void SubscribeInventoryEvents()
    {
        if (inventory != null) inventory.OnInventoryChanged += OnInventoryChanged;
    }

    private void UnsubscribeInventoryEvents()
    {
        if (inventory != null) inventory.OnInventoryChanged -= OnInventoryChanged;
    }

    private void OnInventoryChanged()
    {
        if (IsInventoryOpen) RefreshCurrentTab();
    }

    private void SetInventoryOpen(bool open)
    {
        Debug.Log("[InventoryUIToolkit] SetInventoryOpen: " + open);
        if (IsInventoryOpen == open) return;
        IsInventoryOpen = open;

        UnityEngine.Cursor.visible = open;
        UnityEngine.Cursor.lockState = open ? CursorLockMode.None : CursorLockMode.Locked;

        if (open)
            SetActiveTabInternal(InventoryTab.Inventory, false);

        SetInventoryDocumentPriority(open);
        SuppressOtherUIDocumentPicking(open);

        SetInventoryPanelVisible(open);
        if (open) RefreshCurrentTab(); else CancelItemDrag();
    }

    private void SetInventoryDocumentPriority(bool inventoryOpen)
    {
        if (uiDocument == null)
            return;

        if (inventoryOpen)
        {
            if (!hasCapturedDocumentSortingOrder)
            {
                originalDocumentSortingOrder = uiDocument.sortingOrder;
                hasCapturedDocumentSortingOrder = true;
            }

            uiDocument.sortingOrder = Mathf.Max(uiDocument.sortingOrder, 9000);
            rootVisual?.BringToFront();
            inventoryPanel?.BringToFront();
        }
        else if (hasCapturedDocumentSortingOrder)
        {
            uiDocument.sortingOrder = originalDocumentSortingOrder;
        }
    }

    private void SuppressOtherUIDocumentPicking(bool suppress)
    {
        if (suppress)
        {
            suppressedOtherDocumentPickingModes.Clear();
            var allDocuments = FindObjectsByType<UIDocument>();
            for (int i = 0; i < allDocuments.Length; i++)
            {
                var doc = allDocuments[i];
                if (doc == null || doc == uiDocument)
                    continue;

                var root = doc.rootVisualElement;
                if (root == null)
                    continue;

                suppressedOtherDocumentPickingModes[doc] = root.pickingMode;
                root.pickingMode = PickingMode.Ignore;
            }
        }
        else
        {
            foreach (var kvp in suppressedOtherDocumentPickingModes)
            {
                if (kvp.Key == null)
                    continue;

                var root = kvp.Key.rootVisualElement;
                if (root == null)
                    continue;

                root.pickingMode = kvp.Value;
            }

            suppressedOtherDocumentPickingModes.Clear();
        }
    }

    private void SetActiveTabInternal(InventoryTab tab, bool refresh)
    {
        ActiveTab = tab;
        OnActiveTabChanged?.Invoke(tab);

        if (tabContentInventory != null) tabContentInventory.style.display = tab == InventoryTab.Inventory ? DisplayStyle.Flex : DisplayStyle.None;
        if (tabContentSkills != null) tabContentSkills.style.display = tab == InventoryTab.Skills ? DisplayStyle.Flex : DisplayStyle.None;
        if (tabContentInjury != null) tabContentInjury.style.display = tab == InventoryTab.Injury ? DisplayStyle.Flex : DisplayStyle.None;
        if (tabContentCrafting != null) tabContentCrafting.style.display = tab == InventoryTab.Crafting ? DisplayStyle.Flex : DisplayStyle.None;
        if (itemDetailPanel != null) itemDetailPanel.style.display = tab == InventoryTab.Inventory ? DisplayStyle.Flex : DisplayStyle.None;
        UpdateTopTabVisualState();

        if (refresh && IsInventoryOpen) RefreshCurrentTab();

        if (tab == InventoryTab.Inventory && itemsScrollView != null)
        {
            // Defer one frame so layout is finalized after tab visibility change.
            itemsScrollView.schedule.Execute(() =>
            {
                if (IsInventoryOpen && ActiveTab == InventoryTab.Inventory)
                    RefreshInventoryTab();
            }).ExecuteLater(0);
        }
    }

    private void CacheUiReferences()
    {
        if (rootVisual == null) return;
        inventoryPanel = rootVisual.Q<VisualElement>(className: "inventory-panel");
        tabContentInventory = rootVisual.Q<VisualElement>("tab-content-inventory");
        tabContentSkills = rootVisual.Q<VisualElement>("tab-content-skills");
        tabContentInjury = rootVisual.Q<VisualElement>("tab-content-injury");
        tabContentCrafting = rootVisual.Q<VisualElement>("tab-content-crafting");
        // ensure panel uses fullscreen-like sizing for a static UI
        if (inventoryPanel != null)
        {
            inventoryPanel.style.width = Length.Percent(75);
            inventoryPanel.style.height = Length.Percent(75);
            inventoryPanel.style.left = Length.Percent(12.5f);
            inventoryPanel.style.top = Length.Percent(12.5f);
            inventoryPanel.style.marginLeft = 0;
            inventoryPanel.style.marginTop = 0;
            inventoryPanel.pickingMode = PickingMode.Position;
            inventoryPanel.BringToFront();
        }
        itemsScrollView = rootVisual.Q<ScrollView>(className: "items-list");
        skillsScrollView = rootVisual.Q<ScrollView>("skills-list");
        injuryScrollView = rootVisual.Q<ScrollView>("injury-list");
        craftingScrollView = rootVisual.Q<ScrollView>("crafting-list");
        inventoryTabButton = rootVisual.Q<Button>("tab-inventory");
        skillsTabButton = rootVisual.Q<Button>("tab-skills");
        injuryTabButton = rootVisual.Q<Button>("tab-injury");
        craftingTabButton = rootVisual.Q<Button>("tab-crafting");
        
        // Ensure tab buttons can receive input
        if (inventoryTabButton != null) inventoryTabButton.pickingMode = PickingMode.Position;
        if (skillsTabButton != null) skillsTabButton.pickingMode = PickingMode.Position;
        if (injuryTabButton != null) injuryTabButton.pickingMode = PickingMode.Position;
        if (craftingTabButton != null) craftingTabButton.pickingMode = PickingMode.Position;
        
        itemDetailPanel = rootVisual.Q<VisualElement>("item-detail-panel");
        detailItemName = rootVisual.Q<Label>("detail-item-name");
        detailItemDescription = rootVisual.Q<Label>("detail-item-description");
        detailItemProperties = rootVisual.Q<ScrollView>("detail-item-properties");

        if (itemDetailPanel != null && detailItemName != null && detailItemIcon == null)
        {
            detailItemIcon = new Image
            {
                name = "detail-item-icon",
                pickingMode = PickingMode.Ignore,
                scaleMode = ScaleMode.ScaleToFit
            };
            detailItemIcon.style.height = 96;
            detailItemIcon.style.marginBottom = 8;
            detailItemIcon.style.unityBackgroundImageTintColor = Color.white;
            itemDetailPanel.Insert(0, detailItemIcon);
        }

        StyleItemDetailPanel();
    }

    private void StyleItemDetailPanel()
    {
        if (itemDetailPanel != null)
        {
            itemDetailPanel.style.borderLeftWidth = 1;
            itemDetailPanel.style.borderLeftColor = new StyleColor(new Color(0.62f, 0.52f, 0.32f, 0.24f));
        }

        if (detailItemIcon != null)
        {
            detailItemIcon.style.height = 118;
            detailItemIcon.style.marginBottom = 10;
            detailItemIcon.style.backgroundColor = new StyleColor(new Color(0.025f, 0.027f, 0.032f, 0.9f));
            detailItemIcon.style.borderTopWidth = 1;
            detailItemIcon.style.borderRightWidth = 1;
            detailItemIcon.style.borderBottomWidth = 1;
            detailItemIcon.style.borderLeftWidth = 1;
            SetBorderColor(detailItemIcon, new Color(0.62f, 0.52f, 0.32f, 0.26f), new Color(0f, 0f, 0f, 0.6f));
        }

        if (detailItemProperties != null)
        {
            detailItemProperties.style.backgroundColor = new StyleColor(new Color(0.025f, 0.028f, 0.034f, 0.68f));
            detailItemProperties.style.borderTopWidth = 1;
            detailItemProperties.style.borderRightWidth = 1;
            detailItemProperties.style.borderBottomWidth = 1;
            detailItemProperties.style.borderLeftWidth = 1;
            SetBorderColor(detailItemProperties, new Color(0.48f, 0.42f, 0.3f, 0.16f), new Color(0f, 0f, 0f, 0.48f));
        }
    }

    private void HookTabButtons()
    {
        if (inventoryTabButton != null)
        {
            inventoryTabButton.clicked += () => { Debug.Log("[InventoryUIToolkit] Inventory tab clicked"); SetActiveTab(InventoryTab.Inventory); };
        }

        if (skillsTabButton != null)
        {
            skillsTabButton.clicked += () => { Debug.Log("[InventoryUIToolkit] Skills tab clicked"); SetActiveTab(InventoryTab.Skills); };
        }

        if (injuryTabButton != null)
        {
            injuryTabButton.clicked += () => { Debug.Log("[InventoryUIToolkit] Injury tab clicked"); SetActiveTab(InventoryTab.Injury); };
        }

        if (craftingTabButton != null)
        {
            craftingTabButton.clicked += () => { Debug.Log("[InventoryUIToolkit] Crafting tab clicked"); SetActiveTab(InventoryTab.Crafting); };
        }

        UpdateTopTabVisualState();
    }

    private void UpdateTopTabVisualState()
    {
        StyleTopTabButton(inventoryTabButton, ActiveTab == InventoryTab.Inventory);
        StyleTopTabButton(skillsTabButton, ActiveTab == InventoryTab.Skills);
        StyleTopTabButton(injuryTabButton, ActiveTab == InventoryTab.Injury);
        StyleTopTabButton(craftingTabButton, ActiveTab == InventoryTab.Crafting);
    }

    private static void StyleTopTabButton(Button button, bool isActive)
    {
        if (button == null)
            return;

        if (isActive)
        {
            // Active tab: use a very dark background to match inventory panels
            button.style.backgroundColor = new StyleColor(new Color(0.06f, 0.06f, 0.065f, 0.95f));
            button.style.color = new StyleColor(new Color(0.95f, 0.85f, 0.55f, 1f));
            button.style.borderTopWidth = 2;
            button.style.borderRightWidth = 1;
            button.style.borderBottomWidth = 3;
            button.style.borderLeftWidth = 1;
            button.style.borderTopColor = new StyleColor(new Color(0.8f, 0.7f, 0.4f, 0.7f));
            button.style.borderRightColor = new StyleColor(new Color(0.5f, 0.5f, 0.5f, 0.35f));
            button.style.borderBottomColor = new StyleColor(new Color(0.8f, 0.65f, 0.3f, 0.6f));
            button.style.borderLeftColor = new StyleColor(new Color(0.5f, 0.5f, 0.5f, 0.35f));
        }
        else
        {
            // Inactive tab: match inventory black tone
            button.style.backgroundColor = new StyleColor(new Color(0.031f, 0.035f, 0.043f, 0.85f));
            button.style.color = new StyleColor(new Color(0.7f, 0.75f, 0.85f, 0.65f));
            button.style.borderTopWidth = 1;
            button.style.borderRightWidth = 1;
            button.style.borderBottomWidth = 1;
            button.style.borderLeftWidth = 1;
            button.style.borderTopColor = new StyleColor(new Color(0.4f, 0.4f, 0.45f, 0.2f));
            button.style.borderRightColor = new StyleColor(new Color(0.3f, 0.3f, 0.35f, 0.15f));
            button.style.borderBottomColor = new StyleColor(new Color(0.3f, 0.3f, 0.35f, 0.1f));
            button.style.borderLeftColor = new StyleColor(new Color(0.3f, 0.3f, 0.35f, 0.15f));
        }

        button.style.unityFontStyleAndWeight = FontStyle.Bold;
        button.style.fontSize = 11;
        button.style.borderTopLeftRadius = 2;
        button.style.borderTopRightRadius = 2;
        button.style.borderBottomLeftRadius = 2;
        button.style.borderBottomRightRadius = 2;
        button.style.paddingTop = 4;
        button.style.paddingBottom = 4;
        button.style.paddingLeft = 8;
        button.style.paddingRight = 8;
    }

    // Window-dragging handlers removed; inventory is static.

    private void RefreshCurrentTab()
    {
        if (ActiveTab == InventoryTab.Inventory) RefreshInventoryTab();
        else if (ActiveTab == InventoryTab.Skills) UpdateSkillsTab();
        else if (ActiveTab == InventoryTab.Injury) UpdateInjuryTab();
        else UpdateCraftingTab();
    }

    private void UpdateCraftingTab()
    {
        ApplyInventoryScrollStyle(craftingScrollView);

        if (craftingScrollView == null)
            return;

        craftingScrollView.Clear();

        if (inventory == null)
        {
            craftingScrollView.Add(CreateInfoLabel("Inventory not found."));
            return;
        }

        if (craftingSystem == null)
        {
            craftingScrollView.Add(CreateInfoLabel("Crafting system component not found."));
            return;
        }

        var recipes = craftingSystem.CraftingRecipes;
        if (recipes == null || recipes.Count == 0)
        {
            craftingScrollView.Add(CreateInfoLabel("No crafting recipes assigned yet."));
            return;
        }

        craftingScrollView.Add(CreateCraftingHeader());
        craftingScrollView.Add(CreateCraftingCategoryTabs());

        List<CraftingRecipe> filteredRecipes = recipes
            .Where(r => r != null && IsRecipeVisibleInActiveCategory(r))
            .ToList();

        if (filteredRecipes.Count == 0)
        {
            craftingScrollView.Add(CreateInfoLabel("No recipes in this category yet."));
            return;
        }

        for (int i = 0; i < filteredRecipes.Count; i++)
        {
            var recipe = filteredRecipes[i];
            craftingScrollView.Add(CreateCraftingRecipeCard(recipe));
        }
    }

    

    private VisualElement CreateCraftingCategoryTabs()
    {
        var row = new VisualElement();
        row.style.flexDirection = FlexDirection.Row;
        row.style.flexWrap = Wrap.Wrap;
        row.style.marginBottom = 10;
        row.style.paddingTop = 6;
        row.style.paddingRight = 6;
        row.style.paddingBottom = 4;
        row.style.paddingLeft = 6;
        row.style.backgroundColor = new StyleColor(new Color(0.045f, 0.042f, 0.038f, 0.9f));
        row.style.borderTopWidth = 1;
        row.style.borderRightWidth = 1;
        row.style.borderBottomWidth = 1;
        row.style.borderLeftWidth = 1;
        SetBorderColor(row, new Color(0.58f, 0.45f, 0.29f, 0.18f), new Color(0f, 0f, 0f, 0.5f));

        row.Add(CreateCraftingCategoryButton("Ammunition", CraftingCategoryTab.Ammunition));
        row.Add(CreateCraftingCategoryButton("Weapons", CraftingCategoryTab.Weapons));
        row.Add(CreateCraftingCategoryButton("Healing", CraftingCategoryTab.HealingItems));
        row.Add(CreateCraftingCategoryButton("Food", CraftingCategoryTab.Food));
        row.Add(CreateCraftingCategoryButton("Water", CraftingCategoryTab.Water));

        return row;
    }

    private Button CreateCraftingCategoryButton(string label, CraftingCategoryTab tab)
    {
        bool isActive = activeCraftingCategory == tab;
        var button = new Button(() =>
        {
            activeCraftingCategory = tab;
            if (IsInventoryOpen && ActiveTab == InventoryTab.Crafting)
                UpdateCraftingTab();
        })
        {
            text = label
        };

        button.style.height = 28;
        button.style.paddingLeft = 10;
        button.style.paddingRight = 10;
        button.style.marginRight = 6;
        button.style.marginBottom = 6;
        button.style.unityFontStyleAndWeight = FontStyle.Bold;
        button.style.fontSize = 10;
        button.style.letterSpacing = 1;
        button.style.borderTopLeftRadius = 2;
        button.style.borderTopRightRadius = 2;
        button.style.borderBottomLeftRadius = 2;
        button.style.borderBottomRightRadius = 2;
        button.style.backgroundColor = new StyleColor(isActive
            ? new Color(0.24f, 0.18f, 0.11f, 0.98f)
            : new Color(0.065f, 0.06f, 0.055f, 0.96f));
        button.style.color = new StyleColor(isActive
            ? new Color(0.95f, 0.82f, 0.58f, 1f)
            : new Color(0.63f, 0.58f, 0.5f, 0.96f));
        button.style.borderTopWidth = 1;
        button.style.borderRightWidth = 1;
        button.style.borderBottomWidth = 1;
        button.style.borderLeftWidth = 1;
        SetBorderColor(button,
            new Color(0.78f, 0.58f, 0.34f, isActive ? 0.72f : 0.2f),
            new Color(0.18f, 0.13f, 0.08f, isActive ? 0.95f : 0.55f));

        return button;
    }

    private bool IsRecipeVisibleInActiveCategory(CraftingRecipe recipe)
    {
        if (recipe == null)
            return false;

        return activeCraftingCategory switch
        {
            CraftingCategoryTab.Ammunition => recipe.Category == CraftingRecipe.CraftingCategory.Ammunition,
            CraftingCategoryTab.Weapons => recipe.Category == CraftingRecipe.CraftingCategory.Weapons,
            CraftingCategoryTab.HealingItems => recipe.Category == CraftingRecipe.CraftingCategory.HealingItems,
            CraftingCategoryTab.Food => recipe.Category == CraftingRecipe.CraftingCategory.Food,
            CraftingCategoryTab.Water => recipe.Category == CraftingRecipe.CraftingCategory.Water,
            _ => true
        };
    }

    private VisualElement CreateCraftingRecipeCard(CraftingRecipe recipe)
    {
        bool isCraftingBusy = craftingSystem != null && craftingSystem.IsCraftingInProgress;
        bool canCraft = craftingSystem != null && craftingSystem.CanCraftRecipe(recipe) && !isCraftingBusy;

        var card = new VisualElement();
        card.style.flexDirection = FlexDirection.Row;
        card.style.marginBottom = 10;
        card.style.paddingTop = 10;
        card.style.paddingRight = 10;
        card.style.paddingBottom = 10;
        card.style.paddingLeft = 10;
        card.style.backgroundColor = new StyleColor(canCraft
            ? new Color(0.105f, 0.095f, 0.078f, 0.97f)
            : new Color(0.07f, 0.066f, 0.062f, 0.96f));
        card.style.borderTopLeftRadius = 3;
        card.style.borderTopRightRadius = 3;
        card.style.borderBottomLeftRadius = 3;
        card.style.borderBottomRightRadius = 3;
        card.style.borderTopWidth = 1;
        card.style.borderRightWidth = 1;
        card.style.borderBottomWidth = 1;
        card.style.borderLeftWidth = 1;
        SetBorderColor(card,
            canCraft ? new Color(0.72f, 0.54f, 0.32f, 0.36f) : new Color(0.42f, 0.37f, 0.3f, 0.2f),
            new Color(0f, 0f, 0f, 0.62f));

        var iconFrame = new VisualElement();
        iconFrame.style.width = 74;
        iconFrame.style.height = 74;
        iconFrame.style.flexShrink = 0;
        iconFrame.style.alignItems = Align.Center;
        iconFrame.style.justifyContent = Justify.Center;
        iconFrame.style.marginRight = 12;
        iconFrame.style.backgroundColor = new StyleColor(new Color(0.025f, 0.025f, 0.025f, 0.92f));
        iconFrame.style.borderTopWidth = 1;
        iconFrame.style.borderRightWidth = 1;
        iconFrame.style.borderBottomWidth = 1;
        iconFrame.style.borderLeftWidth = 1;
        SetBorderColor(iconFrame, new Color(0.66f, 0.5f, 0.31f, 0.28f), new Color(0f, 0f, 0f, 0.7f));

        if (recipe.OutputItem != null && recipe.OutputItem.Icon != null)
        {
            var icon = new Image { image = recipe.OutputItem.Icon.texture, scaleMode = ScaleMode.ScaleToFit };
            icon.style.width = 58;
            icon.style.height = 58;
            icon.pickingMode = PickingMode.Ignore;
            iconFrame.Add(icon);
        }
        else
        {
            var fallbackIcon = new Label("?");
            fallbackIcon.style.fontSize = 24;
            fallbackIcon.style.unityFontStyleAndWeight = FontStyle.Bold;
            fallbackIcon.style.color = new StyleColor(new Color(0.55f, 0.5f, 0.42f, 0.9f));
            fallbackIcon.style.unityTextAlign = TextAnchor.MiddleCenter;
            iconFrame.Add(fallbackIcon);
        }
        card.Add(iconFrame);

        var body = new VisualElement();
        body.style.flexDirection = FlexDirection.Column;
        body.style.flexGrow = 1;
        body.style.minWidth = 0;

        var topRow = new VisualElement();
        topRow.style.flexDirection = FlexDirection.Row;
        topRow.style.alignItems = Align.Center;
        topRow.style.justifyContent = Justify.SpaceBetween;

        var durationBadge = new Label($"{recipe.CraftDurationSeconds:0.0}s");
        durationBadge.style.fontSize = 10;
        durationBadge.style.paddingLeft = 6;
        durationBadge.style.paddingRight = 6;
        durationBadge.style.paddingTop = 2;
        durationBadge.style.paddingBottom = 2;
        durationBadge.style.backgroundColor = new StyleColor(new Color(0.02f, 0.02f, 0.02f, 0.86f));
        durationBadge.style.color = new StyleColor(new Color(0.9f, 0.76f, 0.52f, 1f));
        durationBadge.style.borderTopLeftRadius = 2;
        durationBadge.style.borderTopRightRadius = 2;
        durationBadge.style.borderBottomLeftRadius = 2;
        durationBadge.style.borderBottomRightRadius = 2;
        durationBadge.style.unityFontStyleAndWeight = FontStyle.Bold;

        string recipeName = !string.IsNullOrWhiteSpace(recipe.RecipeName)
            ? recipe.RecipeName
            : (recipe.OutputItem != null ? recipe.OutputItem.ItemName : "Recipe");

        var title = new Label(recipeName.ToUpperInvariant());
        title.style.fontSize = 14;
        title.style.unityFontStyleAndWeight = FontStyle.Bold;
        title.style.letterSpacing = 1;
        title.style.color = new StyleColor(canCraft
            ? new Color(0.92f, 0.85f, 0.72f, 1f)
            : new Color(0.58f, 0.55f, 0.5f, 1f));
        topRow.Add(title);
        topRow.Add(durationBadge);
        body.Add(topRow);

        var output = new Label($"Makes: {GetOutputText(recipe)}");
        output.style.fontSize = 11;
        output.style.marginTop = 1;
        output.style.marginBottom = 4;
        output.style.color = new StyleColor(new Color(0.67f, 0.63f, 0.54f, 0.95f));
        body.Add(output);

        if (!string.IsNullOrWhiteSpace(recipe.Description))
        {
            var description = new Label(recipe.Description);
            description.style.fontSize = 11;
            description.style.marginTop = 2;
            description.style.marginBottom = 8;
            description.style.color = new StyleColor(new Color(0.72f, 0.7f, 0.65f, 0.9f));
            description.style.whiteSpace = WhiteSpace.Normal;
            body.Add(description);
        }

        var divider = new VisualElement();
        divider.style.height = 1;
        divider.style.marginTop = 2;
        divider.style.marginBottom = 6;
        divider.style.backgroundColor = new StyleColor(new Color(0.62f, 0.47f, 0.27f, 0.18f));
        body.Add(divider);

        var requirementsTitle = new Label("MATERIALS");
        requirementsTitle.style.fontSize = 11;
        requirementsTitle.style.letterSpacing = 1;
        requirementsTitle.style.marginBottom = 4;
        requirementsTitle.style.color = new StyleColor(new Color(0.76f, 0.61f, 0.4f, 0.95f));
        body.Add(requirementsTitle);

        bool hasRequirements = false;
        if (recipe.Requirements != null)
        {
            foreach (var req in recipe.Requirements)
            {
                if (req == null || req.Item == null || req.Quantity <= 0)
                    continue;

                hasRequirements = true;
                int owned = inventory != null ? inventory.GetItemQuantity(req.Item) : 0;
                bool ok = owned >= req.Quantity;
                var reqLabel = new Label($"{req.Item.ItemName}   {owned}/{req.Quantity}");
                reqLabel.style.fontSize = 11;
                reqLabel.style.marginBottom = 2;
                reqLabel.style.color = new StyleColor(ok
                    ? new Color(0.72f, 0.88f, 0.62f, 0.95f)
                    : new Color(0.95f, 0.45f, 0.38f, 0.95f));
                body.Add(reqLabel);
            }
        }

        if (!hasRequirements)
        {
            var none = new Label("No requirements");
            none.style.fontSize = 11;
            none.style.color = new StyleColor(new Color(0.62f, 0.6f, 0.56f, 0.96f));
            body.Add(none);
        }

        string buttonText = isCraftingBusy
            ? "CRAFTING..."
            : (canCraft ? "CRAFT" : "MISSING MATERIALS");

        var craftButton = new Button(() => TryCraftRecipe(recipe)) { text = buttonText };
        craftButton.SetEnabled(canCraft);
        craftButton.style.alignSelf = Align.FlexEnd;
        craftButton.style.marginTop = 8;
        craftButton.style.width = 168;
        craftButton.style.height = 32;
        craftButton.style.unityFontStyleAndWeight = FontStyle.Bold;
        craftButton.style.fontSize = 11;
        craftButton.style.letterSpacing = 1;
        craftButton.style.backgroundColor = new StyleColor(canCraft
            ? new Color(0.25f, 0.18f, 0.1f, 0.98f)
            : new Color(0.11f, 0.105f, 0.1f, 0.95f));
        craftButton.style.color = new StyleColor(canCraft
            ? new Color(0.96f, 0.82f, 0.56f, 1f)
            : new Color(0.48f, 0.46f, 0.42f, 1f));
        craftButton.style.borderTopWidth = 1;
        craftButton.style.borderRightWidth = 1;
        craftButton.style.borderBottomWidth = 1;
        craftButton.style.borderLeftWidth = 1;
        SetBorderColor(craftButton,
            canCraft ? new Color(0.84f, 0.62f, 0.34f, 0.62f) : new Color(0.32f, 0.29f, 0.25f, 0.35f),
            new Color(0f, 0f, 0f, 0.65f));
        body.Add(craftButton);

        card.Add(body);

        return card;
    }

    private void TryCraftRecipe(CraftingRecipe recipe)
    {
        if (craftingSystem == null)
            return;

        craftingSystem.TryStartCraft(recipe);
        RefreshCurrentTab();
    }

    private VisualElement CreateCraftingHeader()
    {
        var container = new VisualElement();
        container.style.flexDirection = FlexDirection.Column;
        container.style.marginBottom = 10;
        container.style.paddingTop = 14;
        container.style.paddingRight = 16;
        container.style.paddingBottom = 12;
        container.style.paddingLeft = 16;
        container.style.backgroundColor = new StyleColor(new Color(0.055f, 0.047f, 0.039f, 0.98f));
        container.style.borderTopLeftRadius = 3;
        container.style.borderTopRightRadius = 3;
        container.style.borderBottomLeftRadius = 3;
        container.style.borderBottomRightRadius = 3;
        container.style.borderTopWidth = 1;
        container.style.borderRightWidth = 1;
        container.style.borderBottomWidth = 1;
        container.style.borderLeftWidth = 1;
        SetBorderColor(container, new Color(0.76f, 0.57f, 0.34f, 0.34f), new Color(0f, 0f, 0f, 0.72f));

        var eyebrow = new Label("FIELD WORKBENCH");
        eyebrow.style.fontSize = 10;
        eyebrow.style.letterSpacing = 2;
        eyebrow.style.color = new StyleColor(new Color(0.73f, 0.58f, 0.38f, 0.9f));
        container.Add(eyebrow);

        var title = new Label("CRAFTING");
        title.style.fontSize = 20;
        title.style.unityFontStyleAndWeight = FontStyle.Bold;
        title.style.letterSpacing = 2;
        title.style.color = new StyleColor(new Color(0.94f, 0.87f, 0.73f, 1f));
        container.Add(title);

        string statusText = craftingSystem != null ? craftingSystem.StatusMessage : "Crafting unavailable.";
        Color statusColor = craftingSystem != null ? craftingSystem.StatusColor : new Color(1f, 0.55f, 0.55f, 0.95f);

        var status = new Label(statusText);
        status.style.fontSize = 11;
        status.style.marginTop = 4;
        status.style.marginBottom = 8;
        status.style.color = new StyleColor(statusColor);
        container.Add(status);

        var progressTrack = new VisualElement();
        progressTrack.style.height = 10;
        progressTrack.style.backgroundColor = new StyleColor(new Color(0.015f, 0.014f, 0.013f, 1f));
        progressTrack.style.borderTopWidth = 1;
        progressTrack.style.borderRightWidth = 1;
        progressTrack.style.borderBottomWidth = 1;
        progressTrack.style.borderLeftWidth = 1;
        SetBorderColor(progressTrack, new Color(0.38f, 0.29f, 0.18f, 0.38f), new Color(0f, 0f, 0f, 0.72f));

        var progressFill = new VisualElement();
        progressFill.style.height = Length.Percent(100);
        float progress = craftingSystem != null ? craftingSystem.CraftingProgress01 : 0f;
        progressFill.style.width = Length.Percent(progress * 100f);
        progressFill.style.backgroundColor = new StyleColor(new Color(0.77f, 0.55f, 0.29f, 1f));
        progressTrack.Add(progressFill);

        container.Add(progressTrack);
        return container;
    }

    private static void SetBorderColor(VisualElement element, Color topAndSide, Color bottom)
    {
        if (element == null)
            return;

        element.style.borderTopColor = new StyleColor(topAndSide);
        element.style.borderRightColor = new StyleColor(topAndSide);
        element.style.borderBottomColor = new StyleColor(bottom);
        element.style.borderLeftColor = new StyleColor(topAndSide);
    }

    private static string GetOutputText(CraftingRecipe recipe)
    {
        if (recipe == null || recipe.OutputItem == null || recipe.OutputAmount <= 0)
            return "Invalid output";

        return $"{recipe.OutputItem.ItemName} x{recipe.OutputAmount}";
    }

    private void RefreshInventoryTab()
    {
        if (itemsScrollView == null || inventory == null) return;
        ApplyInventoryScrollStyle(itemsScrollView);
        itemsScrollView.Clear();
        inventoryCellsByIndex.Clear();

        int columns = Mathf.Max(1, inventory.GetGridColumns());
        int rows = Mathf.Max(1, inventory.GetGridRows());
        float gap = 4f * uiScale;
        float viewportWidth = GetInventoryViewportWidth();
        // Calculate cell size to fill viewport evenly based on resolution
        float cellSize = (viewportWidth - ((columns - 1) * gap)) / columns;
        cellSize = Mathf.Max(40f * uiScale, cellSize); // Ensure minimum 40px scaled but fill viewport
        float gridWidth = (columns * cellSize) + ((columns - 1) * gap);
        float gridHeight = (rows * cellSize) + ((rows - 1) * gap);

        var grid = new VisualElement();
        grid.style.position = Position.Relative;
        grid.style.width = gridWidth;
        grid.style.height = gridHeight;
        grid.style.flexShrink = 0;

        var slots = inventory.GetAllItems();
        var root = rootVisual ?? uiDocument.rootVisualElement;

        for (int i = 0; i < slots.Count; i++)
        {
            var bg = CreateInventoryBackgroundCell(i, columns, cellSize, gap);
            inventoryCellsByIndex[i] = bg;
            grid.Add(bg);
        }
        // Add item tiles for anchor slots (items are represented by their anchor slot)
        for (int i = 0; i < slots.Count; i++)
        {
            var slot = slots[i];
            if (slot != null && slot.isAnchor && slot.isOccupied && slot.item != null)
            {
                var tile = CreateInventoryItemTile(slot, i, columns, cellSize, gap, root);
                grid.Add(tile);
            }
        }

        // Add the fully built grid to the scroll view
        if (itemsScrollView != null)
            itemsScrollView.Add(grid);
    }

    private float GetInventoryViewportWidth()
    {
        float viewportWidth = 0f;

        if (itemsScrollView?.contentViewport != null)
            viewportWidth = itemsScrollView.contentViewport.resolvedStyle.width;

        if (viewportWidth <= 0f && itemsScrollView != null)
            viewportWidth = itemsScrollView.resolvedStyle.width;

        if (viewportWidth <= 0f && itemsScrollView != null)
            viewportWidth = itemsScrollView.worldBound.width;

        if (viewportWidth > 0f)
            lastInventoryViewportWidth = viewportWidth;
        else if (lastInventoryViewportWidth > 0f)
            viewportWidth = lastInventoryViewportWidth;
        else
            viewportWidth = 900f;

        viewportWidth -= 14f * uiScale; // reserve width for vertical scrollbar (scaled)
        return Mathf.Max(120f, viewportWidth);
    }
    private void ApplyInventoryScrollStyle(ScrollView scrollView)
    {
        if (scrollView == null)
            return;

        scrollView.pickingMode = PickingMode.Position;
        
        scrollView.style.paddingTop = 0 * uiScale;
        scrollView.style.paddingRight = 0 * uiScale;
        scrollView.style.paddingBottom = 0 * uiScale;
        scrollView.style.paddingLeft = 0 * uiScale;

        if (scrollView.contentContainer != null)
        {
            scrollView.contentContainer.pickingMode = PickingMode.Position;
            scrollView.contentContainer.style.paddingTop = 8 * uiScale;
            scrollView.contentContainer.style.paddingRight = 16 * uiScale;
            scrollView.contentContainer.style.paddingBottom = 8 * uiScale;
            scrollView.contentContainer.style.paddingLeft = 8 * uiScale;
        }

        scrollView.verticalScrollerVisibility = ScrollerVisibility.AlwaysVisible;
        scrollView.horizontalScrollerVisibility = ScrollerVisibility.Hidden;

        var verticalScroller = scrollView.verticalScroller;
        if (verticalScroller != null)
        {
            verticalScroller.pickingMode = PickingMode.Position;
            verticalScroller.style.position = Position.Absolute;
            verticalScroller.style.top = 0;
            verticalScroller.style.bottom = 0;
            verticalScroller.style.right = 0;
            verticalScroller.style.width = 10 * uiScale;
            verticalScroller.style.minWidth = 10 * uiScale;
            verticalScroller.style.backgroundColor = new StyleColor(new Color(0.09f, 0.09f, 0.09f, 0.92f));
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
                slider.pickingMode = PickingMode.Position;
                slider.style.flexGrow = 1;
                slider.style.height = StyleKeyword.Auto;
                slider.style.minHeight = 0;
                slider.style.backgroundColor = new StyleColor(new Color(0.12f, 0.12f, 0.12f, 0.92f));

                var tracker = slider.Q<VisualElement>(className: "unity-slider__tracker")
                              ?? slider.Q<VisualElement>(className: "unity-base-slider__tracker");
                if (tracker != null)
                    tracker.style.backgroundColor = new StyleColor(new Color(0.12f, 0.12f, 0.12f, 0.92f));

                var dragger = slider.Q<VisualElement>(className: "unity-dragger")
                              ?? slider.Q<VisualElement>(className: "unity-base-slider__dragger");
                if (dragger != null)
                {
                    dragger.pickingMode = PickingMode.Position;
                    dragger.style.backgroundColor = new StyleColor(new Color(0.76f, 0.76f, 0.76f, 1f));
                    dragger.style.unityBackgroundImageTintColor = new StyleColor(new Color(0.76f, 0.76f, 0.76f, 1f));
                    dragger.style.borderTopWidth = 0;
                    dragger.style.borderRightWidth = 0;
                    dragger.style.borderBottomWidth = 0;
                    dragger.style.borderLeftWidth = 0;
                }
            }

            var lowButton = verticalScroller.Q<VisualElement>(className: "unity-scroller__low-button");
            if (lowButton != null)
                lowButton.style.display = DisplayStyle.None;

            var highButton = verticalScroller.Q<VisualElement>(className: "unity-scroller__high-button");
            if (highButton != null)
                highButton.style.display = DisplayStyle.None;
        }
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
        StyleInventorySlotCell(cell, false, false);
        cell.style.backgroundColor = new StyleColor(new Color(0.055f, 0.06f, 0.07f, 0.92f));
        cell.style.borderTopWidth = 1;
        cell.style.borderRightWidth = 1;
        cell.style.borderBottomWidth = 1;
        cell.style.borderLeftWidth = 1;
        SetBorderColor(cell, new Color(0.44f, 0.39f, 0.28f, 0.2f), new Color(0f, 0f, 0f, 0.48f));
        cell.style.borderTopLeftRadius = 3;
        cell.style.borderTopRightRadius = 3;
        cell.style.borderBottomLeftRadius = 3;
        cell.style.borderBottomRightRadius = 3;
        cell.name = $"inventory-slot-cell-{slotIndex}";
        cell.pickingMode = PickingMode.Position;  // Allow dragging detection over this cell

        // Slot number label
        var slotLabel = new Label((slotIndex + 1).ToString());
        slotLabel.style.position = Position.Absolute;
        slotLabel.style.left = 2 * uiScale;
        slotLabel.style.top = 1 * uiScale;
        slotLabel.style.fontSize = (int)(8 * uiScale);
        slotLabel.style.color = new StyleColor(new Color(0.48f, 0.46f, 0.4f, 0.44f));
        slotLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
        cell.Add(slotLabel);

        return cell;
    }

    private static void StyleInventorySlotCell(VisualElement cell, bool isDropTarget, bool isValidTarget)
    {
        if (cell == null)
            return;

        if (isDropTarget)
        {
            cell.style.backgroundColor = new StyleColor(isValidTarget
                ? new Color(0.1f, 0.16f, 0.11f, 0.98f)
                : new Color(0.16f, 0.065f, 0.055f, 0.98f));
            SetBorderColor(cell,
                isValidTarget ? new Color(0.54f, 0.86f, 0.42f, 0.8f) : new Color(0.9f, 0.28f, 0.22f, 0.8f),
                new Color(0f, 0f, 0f, 0.65f));
        }
        else
        {
            cell.style.backgroundColor = new StyleColor(new Color(0.055f, 0.06f, 0.07f, 0.92f));
            SetBorderColor(cell, new Color(0.44f, 0.39f, 0.28f, 0.2f), new Color(0f, 0f, 0f, 0.48f));
        }
    }

    private VisualElement CreateInventoryItemTile(InventorySlot slot, int anchorIndex, int columns, float cellSize, float gap, VisualElement root)
    {
        int width = Mathf.Max(1, slot.footprintWidth);
        int height = Mathf.Max(1, slot.footprintHeight);
        int col = anchorIndex % columns;
        int row = anchorIndex / columns;
        float tileWidth = (width * cellSize) + ((width - 1) * gap);
        float tileHeight = (height * cellSize) + ((height - 1) * gap);

        var tile = new VisualElement();
        tile.style.position = Position.Absolute;
        tile.style.left = col * (cellSize + gap);
        tile.style.top = row * (cellSize + gap);
        tile.style.width = tileWidth;
        tile.style.height = tileHeight;
        bool isSelected = selectedItemSlotIndex == anchorIndex;
        Color itemAccent = GetItemAccentColor(slot.item);
        tile.style.backgroundColor = new StyleColor(new Color(0.085f, 0.082f, 0.074f, 0.98f));
        tile.style.borderTopWidth = 1;
        tile.style.borderRightWidth = 1;
        tile.style.borderBottomWidth = 1;
        tile.style.borderLeftWidth = 1;
        SetBorderColor(tile,
            isSelected ? new Color(0.92f, 0.76f, 0.42f, 0.9f) : new Color(itemAccent.r, itemAccent.g, itemAccent.b, 0.42f),
            new Color(0f, 0f, 0f, 0.62f));
        tile.style.borderTopLeftRadius = 4;
        tile.style.borderTopRightRadius = 4;
        tile.style.borderBottomLeftRadius = 4;
        tile.style.borderBottomRightRadius = 4;
        tile.name = $"inventory-item-tile-{anchorIndex}";
        tile.pickingMode = PickingMode.Position;  // Allow mouse/pointer events
        tile.focusable = true;

        if (slot.item.Icon != null)
        {
            var icon = new Image { image = slot.item.Icon.texture, scaleMode = ScaleMode.ScaleToFit };
            icon.style.position = Position.Absolute;
            icon.style.left = 3 * uiScale;
            icon.style.right = 3 * uiScale;
            icon.style.top = 3 * uiScale;
            icon.style.bottom = 3 * uiScale;
            icon.pickingMode = PickingMode.Ignore;
            tile.Add(icon);
        }
        else
        {
            var fallbackLabel = new Label(string.IsNullOrWhiteSpace(slot.item.ItemName) ? "Item" : slot.item.ItemName);
            fallbackLabel.style.position = Position.Absolute;
            fallbackLabel.style.left = 6 * uiScale;
            fallbackLabel.style.right = 6 * uiScale;
            fallbackLabel.style.top = 6 * uiScale;
            fallbackLabel.style.fontSize = (int)(15 * uiScale);
            fallbackLabel.style.color = new StyleColor(new Color(0.9f, 0.93f, 0.98f, 0.95f));
            fallbackLabel.style.whiteSpace = WhiteSpace.Normal;
            fallbackLabel.style.unityTextAlign = TextAnchor.UpperLeft;
            fallbackLabel.pickingMode = PickingMode.Ignore;
            tile.Add(fallbackLabel);
        }

        if (slot.quantity > 1)
        {
            var qty = new Label($"x{slot.quantity}");
            qty.style.position = Position.Absolute;
            qty.style.right = 4 * uiScale;
            qty.style.bottom = 4 * uiScale;
            qty.style.fontSize = (int)(11 * uiScale);
            qty.style.unityFontStyleAndWeight = FontStyle.Bold;
            qty.style.color = new StyleColor(new Color(0.96f, 0.9f, 0.76f, 1f));
            qty.style.backgroundColor = new StyleColor(new Color(0.015f, 0.014f, 0.012f, 0.88f));
            qty.style.paddingLeft = 5;
            qty.style.paddingRight = 5;
            qty.style.paddingTop = 1;
            qty.style.paddingBottom = 1;
            qty.style.borderTopLeftRadius = 2;
            qty.style.borderTopRightRadius = 2;
            qty.style.borderBottomLeftRadius = 2;
            qty.style.borderBottomRightRadius = 2;
            qty.pickingMode = PickingMode.Ignore;
            tile.Add(qty);
        }

        tile.RegisterCallback<MouseEnterEvent>(evt =>
        {
            tile.style.backgroundColor = new StyleColor(new Color(0.13f, 0.12f, 0.1f, 0.98f));
            SetBorderColor(tile, new Color(0.92f, 0.76f, 0.42f, 0.72f), new Color(0f, 0f, 0f, 0.65f));
            ItemTooltipUtility.ShowTooltip(root, slot.item, slot.quantity, GetTooltipAnchorPoint(tile, evt.mousePosition));
        });

        tile.RegisterCallback<MouseMoveEvent>(evt =>
        {
            ItemTooltipUtility.ShowTooltip(root, slot.item, slot.quantity, GetTooltipAnchorPoint(tile, evt.mousePosition));
        });

        tile.RegisterCallback<MouseLeaveEvent>(_ =>
        {
            tile.style.backgroundColor = new StyleColor(new Color(0.085f, 0.082f, 0.074f, 0.98f));
            SetBorderColor(tile,
                selectedItemSlotIndex == anchorIndex ? new Color(0.92f, 0.76f, 0.42f, 0.9f) : new Color(itemAccent.r, itemAccent.g, itemAccent.b, 0.42f),
                new Color(0f, 0f, 0f, 0.62f));
            ItemTooltipUtility.HideTooltip(root);
        });
        tile.RegisterCallback<MouseUpEvent>(evt => 
        { 
            if (evt.button == 0)
            {
                ShowItemDetail(slot, anchorIndex);
                return;
            }

            Debug.Log("[InventoryUIToolkit] Right click on item");
            if (evt.button == 1) ShowInventoryContextMenu(slot, anchorIndex, root, evt); 
        });
        tile.RegisterCallback<PointerDownEvent>(evt =>
        {
            Debug.Log("[InventoryUIToolkit] Pointer down on item, button: " + evt.button + ", pointerId: " + evt.pointerId);
            if (evt.button != 0) return;

            ShowItemDetail(slot, anchorIndex);
            ItemTooltipUtility.HideTooltip(rootVisual);
            BeginItemDragCandidate(slot.item, slot.quantity, anchorIndex, evt.pointerId, evt.position);
            evt.StopPropagation();
        });
        return tile;
    }

    private static Vector2 GetTooltipAnchorPoint(VisualElement element, Vector2 localPointerPosition)
    {
        return localPointerPosition;
    }

    private static string GetInventoryItemTypeLabel(Item item)
    {
        return item switch
        {
            GunItem => "Firearm",
            MeleeWeaponItem => "Melee",
            MagazineItem => "Magazine",
            BandageItem => "Medical",
            FoodItem => "Food",
            WaterItem => "Water",
            _ => "Item"
        };
    }

    private static Color GetItemAccentColor(Item item)
    {
        return item switch
        {
            GunItem => new Color(0.98f, 0.74f, 0.38f, 1f),
            MeleeWeaponItem => new Color(0.9f, 0.52f, 0.32f, 1f),
            MagazineItem => new Color(0.78f, 0.72f, 0.58f, 1f),
            BandageItem => new Color(0.9f, 0.36f, 0.32f, 1f),
            FoodItem => new Color(0.74f, 0.88f, 0.46f, 1f),
            WaterItem => new Color(0.48f, 0.74f, 1f, 1f),
            _ => new Color(0.66f, 0.62f, 0.5f, 1f)
        };
    }

    private void OnRootPointerDownInventoryInput(PointerDownEvent evt)
    {
        if (!IsInventoryOpen || rootVisual == null || inventoryPanel == null)
            return;

        var picked = rootVisual.panel?.Pick(evt.position) as VisualElement;
        if (picked == null)
            return;

        if (evt.button == 0)
            CloseInventoryContextMenuIfClickedOutside(picked);

        var target = picked;
        while (target != null)
        {
            if (target == inventoryTabButton)
            {
                SetActiveTab(InventoryTab.Inventory);
                evt.StopPropagation();
                return;
            }
            else if (target == skillsTabButton)
            {
                SetActiveTab(InventoryTab.Skills);
                evt.StopPropagation();
                return;
            }
            else if (target == injuryTabButton)
            {
                SetActiveTab(InventoryTab.Injury);
                evt.StopPropagation();
                return;
            }
            else if (target == craftingTabButton)
            {
                SetActiveTab(InventoryTab.Crafting);
                evt.StopPropagation();
                return;
            }

            target = target.parent;
        }
    }

    private void CloseInventoryContextMenuIfClickedOutside(VisualElement picked)
    {
        if (rootVisual == null || picked == null)
            return;

        var menu = rootVisual.Q("inventory-context-menu");
        if (menu == null)
            return;

        var current = picked;
        while (current != null)
        {
            if (current == menu)
                return;

            current = current.parent;
        }

        menu.RemoveFromHierarchy();
    }

    private void BeginItemDragCandidate(Item item, int quantity, int sourceSlotIndex, int pointerId, Vector2 pointerPos)
    {
        if (item == null || quantity <= 0) return;
        isItemDragPending = true;
        isItemDragging = false;
        itemDragPointerId = pointerId;
        itemDragSourceSlotIndex = sourceSlotIndex;
        itemDragItem = item;
        itemDragQuantity = quantity;
        itemDragStartMouse = pointerPos;
        rootVisual?.CapturePointer(pointerId);
    }

    private void OnRootPointerMoveItemDrag(PointerMoveEvent evt)
    {
        if (!isItemDragPending || evt.pointerId != itemDragPointerId) return;
        Vector2 pointerPos = evt.position;
        if (!isItemDragging)
        {
            Vector2 delta = pointerPos - itemDragStartMouse;
            if (delta.sqrMagnitude < ItemDragStartThreshold * ItemDragStartThreshold) return;
            ItemTooltipUtility.HideTooltip(rootVisual);
            StartItemDragGhost();
            isItemDragging = true;
        }
        UpdateItemDragGhostPosition(pointerPos);
        UpdateDropSlotHighlight(pointerPos);
    }

    private void OnRootPointerUpItemDrag(PointerUpEvent evt)
    {
        if (evt.button != 0 || evt.pointerId != itemDragPointerId) return;
        if (isItemDragging && inventory != null)
        {
            int targetSlot = GetInventorySlotIndexAtWorldPosition(evt.position);
            if (targetSlot >= 0 && CanDropDraggedItemAt(targetSlot))
                inventory.MoveItem(itemDragSourceSlotIndex, targetSlot);
        }
        CancelItemDrag();
    }

    private int GetInventorySlotIndexAtWorldPosition(Vector2 worldPosition)
    {
        foreach (var kvp in inventoryCellsByIndex)
            if (kvp.Value != null && kvp.Value.worldBound.Contains(worldPosition))
                return kvp.Key;
        return -1;
    }

    private void StartItemDragGhost()
    {
        if (rootVisual == null || itemDragItem == null) return;
        itemDragGhost = new VisualElement();
        itemDragGhost.pickingMode = PickingMode.Ignore;
        itemDragGhost.style.position = Position.Absolute;
        itemDragGhost.style.width = 58;
        itemDragGhost.style.height = 58;
        itemDragGhost.style.backgroundColor = new StyleColor(new Color(0.025f, 0.024f, 0.022f, 0.82f));
        itemDragGhost.style.borderTopWidth = 1;
        itemDragGhost.style.borderRightWidth = 1;
        itemDragGhost.style.borderBottomWidth = 1;
        itemDragGhost.style.borderLeftWidth = 1;
        SetBorderColor(itemDragGhost, new Color(0.88f, 0.72f, 0.42f, 0.72f), new Color(0f, 0f, 0f, 0.65f));
        itemDragGhost.style.opacity = 0.9f;
        var icon = new Image { image = itemDragItem.Icon != null ? itemDragItem.Icon.texture : null, scaleMode = ScaleMode.ScaleToFit };
        icon.style.width = 48;
        icon.style.height = 48;
        icon.style.marginLeft = 5;
        icon.style.marginTop = 5;
        itemDragGhost.Add(icon);
        rootVisual.Add(itemDragGhost);
    }

    private void UpdateItemDragGhostPosition(Vector2 mouseWorldPos)
    {
        if (itemDragGhost == null) return;
        itemDragGhost.style.left = mouseWorldPos.x - 29f;
        itemDragGhost.style.top = mouseWorldPos.y - 29f;
    }

    private void UpdateDropSlotHighlight(Vector2 worldPosition)
    {
        int targetSlot = GetInventorySlotIndexAtWorldPosition(worldPosition);
        if (targetSlot == highlightedDropSlotIndex)
            return;

        ClearDropSlotHighlight();
        highlightedDropSlotIndex = targetSlot;

        if (targetSlot < 0 || !inventoryCellsByIndex.TryGetValue(targetSlot, out VisualElement cell) || cell == null)
            return;

        StyleInventorySlotCell(cell, true, CanDropDraggedItemAt(targetSlot));
    }

    private void ClearDropSlotHighlight()
    {
        if (highlightedDropSlotIndex >= 0
            && inventoryCellsByIndex.TryGetValue(highlightedDropSlotIndex, out VisualElement previous)
            && previous != null)
        {
            StyleInventorySlotCell(previous, false, false);
        }

        highlightedDropSlotIndex = -1;
    }

    private bool CanDropDraggedItemAt(int targetSlot)
    {
        if (inventory == null || targetSlot < 0 || itemDragSourceSlotIndex < 0)
            return false;

        int sourceAnchor = inventory.ResolveAnchorSlotIndex(itemDragSourceSlotIndex);
        InventorySlot source = inventory.GetSlot(sourceAnchor);
        if (source == null || !source.isAnchor || source.item == null)
            return false;

        int columns = Mathf.Max(1, inventory.GetGridColumns());
        int rows = Mathf.Max(1, inventory.GetGridRows());
        int width = Mathf.Max(1, source.footprintWidth);
        int height = Mathf.Max(1, source.footprintHeight);

        if (IsIndexWithinFootprint(sourceAnchor, targetSlot, width, height, columns))
            return true;

        int targetCol = targetSlot % columns;
        int targetRow = targetSlot / columns;
        if (targetCol + width > columns || targetRow + height > rows)
            return false;

        var slots = inventory.GetAllItems();
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                int checkIndex = (targetRow + y) * columns + targetCol + x;
                if (checkIndex < 0 || checkIndex >= slots.Count)
                    return false;

                if (IsIndexWithinFootprint(sourceAnchor, checkIndex, width, height, columns))
                    continue;

                InventorySlot occupied = slots[checkIndex];
                if (occupied != null && occupied.isOccupied)
                    return false;
            }
        }

        return true;
    }

    private static bool IsIndexWithinFootprint(int anchorIndex, int index, int width, int height, int columns)
    {
        if (anchorIndex < 0 || index < 0 || columns <= 0)
            return false;

        int anchorCol = anchorIndex % columns;
        int anchorRow = anchorIndex / columns;
        int col = index % columns;
        int row = index / columns;
        return col >= anchorCol && col < anchorCol + width && row >= anchorRow && row < anchorRow + height;
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
        ClearDropSlotHighlight();
        itemDragGhost?.RemoveFromHierarchy();
        itemDragGhost = null;
        ItemTooltipUtility.HideTooltip(rootVisual);
    }

    private void ShowItemDetail(InventorySlot slot, int slotIndex)
    {
        if (slot == null || slot.item == null)
        {
            ClearItemDetail();
            return;
        }

        selectedItem = slot.item;
        selectedItemSlotIndex = slotIndex;

        if (detailItemIcon != null)
            detailItemIcon.image = slot.item.Icon != null ? slot.item.Icon.texture : null;

        if (detailItemName != null)
            detailItemName.text = slot.item.ItemName ?? "Unknown Item";

        if (detailItemDescription != null)
            detailItemDescription.text = slot.item.Description ?? "No description available.";

        if (detailItemProperties != null)
        {
            detailItemProperties.Clear();

            AddDetailPropertyLabel("TYPE", GetInventoryItemTypeLabel(slot.item), GetItemAccentColor(slot.item));
            AddDetailPropertyLabel("QUANTITY", slot.quantity.ToString(), new Color(0.86f, 0.78f, 0.58f, 1f));
            AddDetailPropertyLabel("STACK", $"Max {slot.item.MaxStackSize}", new Color(0.72f, 0.74f, 0.76f, 1f));

            if (slot.item is GunItem gun)
            {
                AddDetailPropertyLabel("WEAPON", gun.GunType.ToString(), GetItemAccentColor(slot.item));
            }
            else if (slot.item is FoodItem food)
            {
                if (!Mathf.Approximately(food.HungerRestore, 0))
                    AddDetailPropertyLabel("HUNGER", $"+{food.HungerRestore:0.#}", new Color(0.72f, 0.88f, 0.56f, 1f));
                if (!Mathf.Approximately(food.ThirstRestore, 0))
                    AddDetailPropertyLabel("THIRST", $"+{food.ThirstRestore:0.#}", new Color(0.55f, 0.78f, 1f, 1f));
            }
            else if (slot.item is WaterItem water)
            {
                if (!Mathf.Approximately(water.ThirstRestore, 0))
                    AddDetailPropertyLabel("THIRST", $"+{water.ThirstRestore:0.#}", new Color(0.55f, 0.78f, 1f, 1f));
                if (!Mathf.Approximately(water.HungerRestore, 0))
                    AddDetailPropertyLabel("HUNGER", $"+{water.HungerRestore:0.#}", new Color(0.72f, 0.88f, 0.56f, 1f));
            }

            AddDetailActionButtons(slot, slotIndex);
        }
    }

    private void AddDetailPropertyLabel(string label, string value, Color valueColor)
    {
        if (detailItemProperties == null)
            return;

        var row = new VisualElement();
        row.style.flexDirection = FlexDirection.Row;
        row.style.justifyContent = Justify.SpaceBetween;
        row.style.alignItems = Align.Center;
        row.style.marginBottom = 5;
        row.style.paddingTop = 4;
        row.style.paddingRight = 6;
        row.style.paddingBottom = 4;
        row.style.paddingLeft = 6;
        row.style.backgroundColor = new StyleColor(new Color(0.045f, 0.048f, 0.055f, 0.78f));
        row.style.borderTopWidth = 1;
        row.style.borderRightWidth = 1;
        row.style.borderBottomWidth = 1;
        row.style.borderLeftWidth = 1;
        SetBorderColor(row, new Color(0.38f, 0.34f, 0.25f, 0.14f), new Color(0f, 0f, 0f, 0.35f));

        var key = new Label(label);
        key.style.fontSize = 9;
        key.style.letterSpacing = 1;
        key.style.color = new StyleColor(new Color(0.58f, 0.6f, 0.62f, 0.92f));
        key.style.unityFontStyleAndWeight = FontStyle.Bold;
        row.Add(key);

        var val = new Label(value);
        val.style.fontSize = 10;
        val.style.color = new StyleColor(valueColor);
        val.style.unityFontStyleAndWeight = FontStyle.Bold;
        row.Add(val);

        detailItemProperties.Add(row);
    }

    private void ClearItemDetail()
    {
        selectedItem = null;
        selectedItemSlotIndex = -1;
        if (detailItemIcon != null) detailItemIcon.image = null;
        if (detailItemName != null) detailItemName.text = "Select an item";
        if (detailItemDescription != null) detailItemDescription.text = "";
        if (detailItemProperties != null) detailItemProperties.Clear();
    }

    private void AddDetailActionButtons(InventorySlot slot, int slotIndex)
    {
        if (detailItemProperties == null || slot == null || slot.item == null)
            return;

        var actionsHeader = new Label("Actions:");
        actionsHeader.style.fontSize = 10;
        actionsHeader.style.letterSpacing = 1;
        actionsHeader.style.unityFontStyleAndWeight = FontStyle.Bold;
        actionsHeader.style.color = new StyleColor(new Color(0.86f, 0.78f, 0.58f, 0.95f));
        actionsHeader.style.marginTop = 10;
        actionsHeader.style.marginBottom = 4;
        detailItemProperties.Add(actionsHeader);

        var actionsContainer = new VisualElement();
        actionsContainer.style.flexDirection = FlexDirection.Column;
        actionsContainer.style.marginTop = 2;
        detailItemProperties.Add(actionsContainer);

        AddContextActions(slot, slotIndex, actionsContainer);

        if (actionsContainer.childCount == 0)
        {
            var noActions = new Label("No actions available");
            noActions.style.fontSize = 10;
            noActions.style.color = new StyleColor(new Color(0.72f, 0.72f, 0.75f, 0.9f));
            actionsContainer.Add(noActions);
        }
    }

    private void ShowInventoryContextMenu(InventorySlot slot, int slotIndex, VisualElement root, MouseUpEvent evt)
    {
        if (slot == null || slot.item == null) return;
        var existing = root.Q("inventory-context-menu");
        existing?.RemoveFromHierarchy();

        var menu = new VisualElement { name = "inventory-context-menu" };
        menu.style.position = Position.Absolute;
        Vector2 p = root.WorldToLocal(evt.mousePosition);
        menu.style.left = p.x;
        menu.style.top = p.y;
        menu.style.minWidth = 220;
        menu.style.maxWidth = 260;
        menu.style.backgroundColor = new StyleColor(new Color(0.08f, 0.08f, 0.09f, 0.98f));
        menu.style.borderTopWidth = 1;
        menu.style.borderRightWidth = 1;
        menu.style.borderBottomWidth = 1;
        menu.style.borderLeftWidth = 1;
        menu.style.borderTopColor = new StyleColor(new Color(0.62f, 0.62f, 0.62f, 0.24f));
        menu.style.borderRightColor = new StyleColor(new Color(0.62f, 0.62f, 0.62f, 0.24f));
        menu.style.borderBottomColor = new StyleColor(new Color(0.62f, 0.62f, 0.62f, 0.15f));
        menu.style.borderLeftColor = new StyleColor(new Color(0.62f, 0.62f, 0.62f, 0.15f));
        menu.style.borderTopLeftRadius = 8;
        menu.style.borderTopRightRadius = 8;
        menu.style.borderBottomLeftRadius = 8;
        menu.style.borderBottomRightRadius = 8;
        menu.style.paddingLeft = 8;
        menu.style.paddingRight = 8;
        menu.style.paddingTop = 8;
        menu.style.paddingBottom = 8;

        string itemName = string.IsNullOrWhiteSpace(slot.item.ItemName) ? "Item" : slot.item.ItemName;
        var header = new Label(itemName);
        header.style.fontSize = 12;
        header.style.unityFontStyleAndWeight = FontStyle.Bold;
        header.style.color = new StyleColor(new Color(0.95f, 0.95f, 0.95f, 1f));
        header.style.marginBottom = 2;
        menu.Add(header);

        var subtitle = new Label("Actions");
        subtitle.style.fontSize = 10;
        subtitle.style.color = new StyleColor(new Color(0.76f, 0.76f, 0.78f, 0.95f));
        subtitle.style.marginBottom = 6;
        menu.Add(subtitle);

        var divider = new VisualElement();
        divider.style.height = 1;
        divider.style.backgroundColor = new StyleColor(new Color(0.62f, 0.62f, 0.62f, 0.18f));
        divider.style.marginBottom = 6;
        menu.Add(divider);

        AddContextActions(slot, slotIndex, menu);
        root.Add(menu);

        menu.BringToFront();

        float rootWidth = root.resolvedStyle.width;
        float rootHeight = root.resolvedStyle.height;
        float menuWidth = Mathf.Max(220f, menu.resolvedStyle.width > 0f ? menu.resolvedStyle.width : 220f);
        float menuHeight = Mathf.Max(120f, menu.resolvedStyle.height > 0f ? menu.resolvedStyle.height : 120f);
        if (rootWidth > 0f && p.x + menuWidth > rootWidth - 8f)
            menu.style.left = Mathf.Max(8f, rootWidth - menuWidth - 8f);
        if (rootHeight > 0f && p.y + menuHeight > rootHeight - 8f)
            menu.style.top = Mathf.Max(8f, rootHeight - menuHeight - 8f);
    }

    private void AddContextActions(InventorySlot slot, int slotIndex, VisualElement menu)
    {
        if (slot == null || slot.item == null)
            return;

        bool hasAnyAction = false;

        if (equipmentManager == null)
            equipmentManager = FindAnyObjectByType<EquipmentManager>();

        if (slot.item is GunItem gun)
        {
            if (equipmentManager != null && gun.GunType == Gun.GunType.Pistol)
            {
                AddContextActionLabel(menu, "Equip as Secondary", () => equipmentManager.EquipAsSecondary(gun));
                AddContextActionLabel(menu, "Dequip Secondary", () => equipmentManager.EquipAsSecondary(null));
                hasAnyAction = true;
            }
            else if (equipmentManager != null)
            {
                AddContextActionLabel(menu, "Equip as Primary", () => equipmentManager.EquipAsPrimary(gun));
                AddContextActionLabel(menu, "Dequip Primary", () => equipmentManager.EquipAsPrimary(null));
                hasAnyAction = true;
            }
        }

        if (slot.item is MeleeWeaponItem melee)
        {
            if (equipmentManager != null)
            {
                AddContextActionLabel(menu, "Equip as Melee Slot", () => equipmentManager.EquipMeleeWeapon(melee));
                AddContextActionLabel(menu, "Select Melee (3)", equipmentManager.SelectMelee);
                AddContextActionLabel(menu, "Unequip Melee Slot", () => equipmentManager.EquipMeleeWeapon(null));
                hasAnyAction = true;
            }
        }

        if (slot.item is FoodItem food)
        {
            AddContextActionLabel(menu, "Eat", () => ConsumeFoodItem(food, slotIndex));
            hasAnyAction = true;
        }

        if (slot.item is WaterItem water)
        {
            AddContextActionLabel(menu, "Drink", () => ConsumeWaterItem(water, slotIndex));
            hasAnyAction = true;
        }

        if (!hasAnyAction)
        {
            var emptyLabel = new Label("No actions available");
            emptyLabel.style.fontSize = 10;
            emptyLabel.style.color = new StyleColor(new Color(0.72f, 0.72f, 0.75f, 0.9f));
            emptyLabel.style.marginLeft = 4;
            emptyLabel.style.marginBottom = 2;
            menu.Add(emptyLabel);
        }
    }

    private void ConsumeFoodItem(FoodItem foodItem, int slotIndex)
    {
        if (foodItem == null)
            return;

        ResolveReferences();
        if (inventory == null || player == null)
            return;

        int anchorIndex = inventory.ResolveAnchorSlotIndex(slotIndex);
        if (anchorIndex < 0)
            return;

        if (!inventory.RemoveItemAtSlot(anchorIndex, 1))
            return;

        if (!Mathf.Approximately(foodItem.HungerRestore, 0f))
            player.Eat(foodItem.HungerRestore);
        if (!Mathf.Approximately(foodItem.ThirstRestore, 0f))
            player.Drink(foodItem.ThirstRestore);
    }

    private void ConsumeWaterItem(WaterItem waterItem, int slotIndex)
    {
        if (waterItem == null)
            return;

        ResolveReferences();
        if (inventory == null || player == null)
            return;

        int anchorIndex = inventory.ResolveAnchorSlotIndex(slotIndex);
        if (anchorIndex < 0)
            return;

        if (!inventory.RemoveItemAtSlot(anchorIndex, 1))
            return;

        if (!Mathf.Approximately(waterItem.ThirstRestore, 0f))
            player.Drink(waterItem.ThirstRestore);
        if (!Mathf.Approximately(waterItem.HungerRestore, 0f))
            player.Eat(waterItem.HungerRestore);
    }

    private void AddContextActionLabel(VisualElement menu, string text, Action callback)
    {
        var button = new Button(() =>
        {
            callback?.Invoke();
            var m = rootVisual?.Q("inventory-context-menu");
            m?.RemoveFromHierarchy();
            if (IsInventoryOpen && ActiveTab == InventoryTab.Inventory)
            {
                RefreshInventoryTab();
                InventorySlot refreshedSlot = inventory != null && selectedItemSlotIndex >= 0 ? inventory.GetSlot(selectedItemSlotIndex) : null;
                if (refreshedSlot != null && refreshedSlot.isOccupied && refreshedSlot.item != null)
                    ShowItemDetail(refreshedSlot, inventory.ResolveAnchorSlotIndex(selectedItemSlotIndex));
                else
                    ClearItemDetail();
            }
        });
        button.text = text;
        button.style.height = 30;
        button.style.marginBottom = 5;
        button.style.paddingLeft = 9;
        button.style.paddingRight = 9;
        button.style.borderTopLeftRadius = 3;
        button.style.borderTopRightRadius = 3;
        button.style.borderBottomLeftRadius = 3;
        button.style.borderBottomRightRadius = 3;
        button.style.borderTopWidth = 1;
        button.style.borderRightWidth = 1;
        button.style.borderBottomWidth = 1;
        button.style.borderLeftWidth = 1;
        SetBorderColor(button, new Color(0.62f, 0.52f, 0.32f, 0.32f), new Color(0f, 0f, 0f, 0.55f));
        button.style.backgroundColor = new StyleColor(new Color(0.095f, 0.085f, 0.07f, 0.98f));
        button.style.color = new StyleColor(new Color(0.92f, 0.84f, 0.64f, 1f));
        button.style.unityTextAlign = TextAnchor.MiddleLeft;
        button.style.unityFontStyleAndWeight = FontStyle.Bold;
        button.style.fontSize = 11;
        button.style.letterSpacing = 1;

        button.RegisterCallback<MouseEnterEvent>(_ =>
        {
            button.style.backgroundColor = new StyleColor(new Color(0.18f, 0.14f, 0.08f, 0.98f));
            button.style.color = new StyleColor(new Color(1f, 1f, 1f, 1f));
            SetBorderColor(button, new Color(0.92f, 0.76f, 0.42f, 0.68f), new Color(0f, 0f, 0f, 0.6f));
        });

        button.RegisterCallback<MouseLeaveEvent>(_ =>
        {
            button.style.backgroundColor = new StyleColor(new Color(0.095f, 0.085f, 0.07f, 0.98f));
            button.style.color = new StyleColor(new Color(0.92f, 0.84f, 0.64f, 1f));
            SetBorderColor(button, new Color(0.62f, 0.52f, 0.32f, 0.32f), new Color(0f, 0f, 0f, 0.55f));
        });

        menu.Add(button);
    }

    private void UpdateSkillsTab()
    {
        ApplyInventoryScrollStyle(skillsScrollView);

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

        skillsScrollView.Add(CreateSkillsHeader(displayedGunType));

        skillsScrollView.Add(CreateSkillCard(
            $"{displayedGunType} Accuracy",
            "Weapon Control",
            playerSkills.GetSkillLevel(displayedGunType),
            playerSkills.GetCurrentXP(displayedGunType),
            playerSkills.GetXPNeeded(displayedGunType),
            GetSkillEffectText($"{displayedGunType} Accuracy", playerSkills.GetSkillLevel(displayedGunType)),
            new Color(1f, 0.78f, 0.4f, 1f)));

        skillsScrollView.Add(CreateSkillCard(
            "Stamina",
            "Endurance",
            playerSkills.GetGeneralSkillLevel(PlayerSkills.SkillType.Stamina),
            playerSkills.GetGeneralSkillXP(PlayerSkills.SkillType.Stamina),
            playerSkills.GetGeneralSkillXPNeeded(PlayerSkills.SkillType.Stamina),
            GetSkillEffectText("Stamina", playerSkills.GetGeneralSkillLevel(PlayerSkills.SkillType.Stamina)),
            new Color(0.45f, 0.82f, 1f, 1f)));

        skillsScrollView.Add(CreateSkillCard(
            "Metabolism",
            "Survival",
            playerSkills.GetGeneralSkillLevel(PlayerSkills.SkillType.Metabolism),
            playerSkills.GetGeneralSkillXP(PlayerSkills.SkillType.Metabolism),
            playerSkills.GetGeneralSkillXPNeeded(PlayerSkills.SkillType.Metabolism),
            GetSkillEffectText("Metabolism", playerSkills.GetGeneralSkillLevel(PlayerSkills.SkillType.Metabolism)),
            new Color(1f, 0.63f, 0.42f, 1f)));

        skillsScrollView.Add(CreateSkillCard(
            "Vitality",
            "Conditioning",
            playerSkills.GetGeneralSkillLevel(PlayerSkills.SkillType.Vitality),
            playerSkills.GetGeneralSkillXP(PlayerSkills.SkillType.Vitality),
            playerSkills.GetGeneralSkillXPNeeded(PlayerSkills.SkillType.Vitality),
            GetSkillEffectText("Vitality", playerSkills.GetGeneralSkillLevel(PlayerSkills.SkillType.Vitality)),
            new Color(1f, 0.45f, 0.45f, 1f)));

        skillsScrollView.Add(CreateSkillCard(
            "Stealth",
            "Movement",
            playerSkills.GetGeneralSkillLevel(PlayerSkills.SkillType.Stealth),
            playerSkills.GetGeneralSkillXP(PlayerSkills.SkillType.Stealth),
            playerSkills.GetGeneralSkillXPNeeded(PlayerSkills.SkillType.Stealth),
            GetSkillEffectText("Stealth", playerSkills.GetGeneralSkillLevel(PlayerSkills.SkillType.Stealth)),
            new Color(0.76f, 0.55f, 1f, 1f)));

        skillsScrollView.Add(CreateSkillCard(
            "Strength",
            "Power",
            playerSkills.GetGeneralSkillLevel(PlayerSkills.SkillType.Strength),
            playerSkills.GetGeneralSkillXP(PlayerSkills.SkillType.Strength),
            playerSkills.GetGeneralSkillXPNeeded(PlayerSkills.SkillType.Strength),
            GetSkillEffectText("Strength", playerSkills.GetGeneralSkillLevel(PlayerSkills.SkillType.Strength)),
            new Color(0.6f, 0.92f, 0.55f, 1f)));
    }

    private VisualElement CreateSkillsHeader(Gun.GunType displayedGunType)
    {
        var container = new VisualElement();
        container.style.flexDirection = FlexDirection.Column;
        container.style.marginBottom = 10;
        container.style.paddingTop = 14;
        container.style.paddingRight = 16;
        container.style.paddingBottom = 12;
        container.style.paddingLeft = 16;
        container.style.backgroundColor = new StyleColor(new Color(0.04f, 0.055f, 0.07f, 0.98f));
        container.style.borderTopLeftRadius = 3;
        container.style.borderTopRightRadius = 3;
        container.style.borderBottomLeftRadius = 3;
        container.style.borderBottomRightRadius = 3;
        container.style.borderTopWidth = 1;
        container.style.borderRightWidth = 1;
        container.style.borderBottomWidth = 1;
        container.style.borderLeftWidth = 1;
        SetBorderColor(container, new Color(0.28f, 0.55f, 0.72f, 0.34f), new Color(0f, 0f, 0f, 0.72f));

        var eyebrow = new Label("CHARACTER PROGRESSION");
        eyebrow.style.fontSize = 10;
        eyebrow.style.letterSpacing = 2;
        eyebrow.style.color = new StyleColor(new Color(0.58f, 0.82f, 0.96f, 0.9f));
        container.Add(eyebrow);

        var title = new Label("SKILLS");
        title.style.fontSize = 20;
        title.style.unityFontStyleAndWeight = FontStyle.Bold;
        title.style.letterSpacing = 2;
        title.style.color = new StyleColor(new Color(0.86f, 0.94f, 1f, 1f));
        container.Add(title);

        var summary = new Label($"Active weapon profile: {displayedGunType}   Train skills through play");
        summary.style.fontSize = 11;
        summary.style.marginTop = 5;
        summary.style.color = new StyleColor(new Color(0.66f, 0.72f, 0.78f, 0.95f));
        container.Add(summary);

        return container;
    }

    private void UpdateInjuryTab()
    {
        ApplyInventoryScrollStyle(injuryScrollView);

        if (injuryScrollView == null)
            return;

        injuryScrollView.Clear();

        var injurySystem = GetInjurySystem();
        if (injurySystem == null)
        {
            injuryScrollView.Add(CreateInfoLabel("Injury system component not found."));
            return;
        }

        var activeInjuries = injurySystem.GetActiveInjuries();

        injuryScrollView.Add(CreateInjuryHeader(injurySystem, activeInjuries.Count));

        if (activeInjuries.Count == 0)
        {
            injuryScrollView.Add(CreateInfoLabel("No active injuries. You are healthy."));
            return;
        }

        foreach (var injury in activeInjuries)
            injuryScrollView.Add(CreateInjuryCard(injurySystem, injury));
    }

    private VisualElement CreateInjuryHeader(InjurySystem injurySystem, int totalInjuries)
    {
        var container = new VisualElement();
        container.style.flexDirection = FlexDirection.Column;
        container.style.marginBottom = 10;
        container.style.paddingTop = 14;
        container.style.paddingRight = 16;
        container.style.paddingBottom = 12;
        container.style.paddingLeft = 16;
        container.style.backgroundColor = new StyleColor(new Color(0.07f, 0.045f, 0.043f, 0.98f));
        container.style.borderTopLeftRadius = 3;
        container.style.borderTopRightRadius = 3;
        container.style.borderBottomLeftRadius = 3;
        container.style.borderBottomRightRadius = 3;
        container.style.borderTopWidth = 1;
        container.style.borderRightWidth = 1;
        container.style.borderBottomWidth = 1;
        container.style.borderLeftWidth = 1;
        SetBorderColor(container, new Color(0.65f, 0.22f, 0.18f, 0.38f), new Color(0f, 0f, 0f, 0.72f));

        var eyebrow = new Label("MEDICAL STATUS");
        eyebrow.style.fontSize = 10;
        eyebrow.style.letterSpacing = 2;
        eyebrow.style.color = new StyleColor(new Color(0.92f, 0.52f, 0.43f, 0.9f));
        container.Add(eyebrow);

        var title = new Label("INJURY TREATMENT");
        title.style.fontSize = 20;
        title.style.unityFontStyleAndWeight = FontStyle.Bold;
        title.style.letterSpacing = 2;
        title.style.color = new StyleColor(new Color(0.96f, 0.86f, 0.78f, 1f));
        container.Add(title);

        int bandageCount = CountBandageItems();
        var summary = new Label($"Wounds {totalInjuries}   Bleeding {injurySystem.GetBleedingInjuryCount()}   Infected {injurySystem.GetInfectedInjuryCount()}   Bandages {bandageCount}");
        summary.style.fontSize = 11;
        summary.style.marginTop = 5;
        summary.style.color = new StyleColor(new Color(0.76f, 0.72f, 0.66f, 0.95f));
        container.Add(summary);

        return container;
    }

    private VisualElement CreateInjuryCard(InjurySystem injurySystem, Injury injury)
    {
        bool canBandage = injury != null && !injury.isBandaged && CountBandageItems() > 0;
        Color severityColor = GetInjuryAccentColor(injury);

        var card = new VisualElement();
        card.style.flexDirection = FlexDirection.Row;
        card.style.marginBottom = 10;
        card.style.paddingTop = 10;
        card.style.paddingRight = 10;
        card.style.paddingBottom = 10;
        card.style.paddingLeft = 10;
        card.style.backgroundColor = new StyleColor(GetInjuryBackgroundColor(injury));
        card.style.borderTopLeftRadius = 3;
        card.style.borderTopRightRadius = 3;
        card.style.borderBottomLeftRadius = 3;
        card.style.borderBottomRightRadius = 3;
        card.style.borderTopWidth = 1;
        card.style.borderRightWidth = 1;
        card.style.borderBottomWidth = 1;
        card.style.borderLeftWidth = 1;
        SetBorderColor(card, new Color(severityColor.r, severityColor.g, severityColor.b, 0.38f), new Color(0f, 0f, 0f, 0.65f));

        var marker = new VisualElement();
        marker.style.width = 5;
        marker.style.alignSelf = Align.Stretch;
        marker.style.marginRight = 12;
        marker.style.backgroundColor = new StyleColor(severityColor);
        card.Add(marker);

        var body = new VisualElement();
        body.style.flexDirection = FlexDirection.Column;
        body.style.flexGrow = 1;
        body.style.minWidth = 0;

        var topRow = new VisualElement();
        topRow.style.flexDirection = FlexDirection.Row;
        topRow.style.alignItems = Align.Center;
        topRow.style.justifyContent = Justify.SpaceBetween;

        var title = new Label($"{FormatBodyPart(injury.bodyPart)} - {injury.injuryType}".ToUpperInvariant());
        title.style.fontSize = 14;
        title.style.letterSpacing = 1;
        title.style.unityFontStyleAndWeight = FontStyle.Bold;
        title.style.color = new StyleColor(new Color(0.96f, 0.88f, 0.8f, 1f));
        topRow.Add(title);

        var severity = new Label(GetInjurySeverityLabel(injury));
        severity.style.fontSize = 10;
        severity.style.unityFontStyleAndWeight = FontStyle.Bold;
        severity.style.paddingTop = 2;
        severity.style.paddingRight = 6;
        severity.style.paddingBottom = 2;
        severity.style.paddingLeft = 6;
        severity.style.backgroundColor = new StyleColor(new Color(0.02f, 0.018f, 0.018f, 0.88f));
        severity.style.color = new StyleColor(severityColor);
        topRow.Add(severity);
        body.Add(topRow);

        string status = injury.isInfected ? $"INFECTED {Mathf.RoundToInt(injury.infectionProgress * 100f)}%" : "Clean";
        status += injury.isBandaged ? " | Bandaged" : " | Bleeding";
        if (injury.isHealing)
            status += $" | Healing {Mathf.RoundToInt(injury.GetHealingProgress01() * 100f)}%";

        var statusLabel = new Label(status);
        statusLabel.style.fontSize = 11;
        statusLabel.style.marginTop = 4;
        statusLabel.style.marginBottom = 8;
        statusLabel.style.color = new StyleColor(new Color(0.74f, 0.7f, 0.64f, 0.95f));
        body.Add(statusLabel);

        if (injury.isHealing)
            body.Add(CreateInjuryProgressBar(injury.GetHealingProgress01(), new Color(0.72f, 0.86f, 0.56f, 1f)));
        else if (injury.isInfected)
            body.Add(CreateInjuryProgressBar(injury.infectionProgress, new Color(0.82f, 0.24f, 0.18f, 1f)));

        var actionRow = new VisualElement();
        actionRow.style.flexDirection = FlexDirection.Row;
        actionRow.style.justifyContent = Justify.FlexEnd;
        actionRow.style.marginTop = 8;

        var bandageButton = new Button(() => TryBandageInjuryFromTab(injurySystem, injury))
        {
            text = injury.isBandaged ? "BANDAGED" : "APPLY BANDAGE"
        };
        bandageButton.SetEnabled(canBandage);
        bandageButton.style.width = 168;
        bandageButton.style.height = 32;
        bandageButton.style.fontSize = 11;
        bandageButton.style.letterSpacing = 1;
        bandageButton.style.unityFontStyleAndWeight = FontStyle.Bold;
        bandageButton.style.backgroundColor = new StyleColor(canBandage
            ? new Color(0.27f, 0.08f, 0.065f, 0.98f)
            : new Color(0.11f, 0.095f, 0.09f, 0.95f));
        bandageButton.style.color = new StyleColor(canBandage
            ? new Color(0.98f, 0.78f, 0.68f, 1f)
            : new Color(0.48f, 0.43f, 0.4f, 1f));
        bandageButton.style.borderTopWidth = 1;
        bandageButton.style.borderRightWidth = 1;
        bandageButton.style.borderBottomWidth = 1;
        bandageButton.style.borderLeftWidth = 1;
        SetBorderColor(bandageButton,
            canBandage ? new Color(0.78f, 0.25f, 0.18f, 0.62f) : new Color(0.32f, 0.25f, 0.22f, 0.35f),
            new Color(0f, 0f, 0f, 0.65f));
        actionRow.Add(bandageButton);
        body.Add(actionRow);

        card.Add(body);
        return card;
    }

    private VisualElement CreateInjuryProgressBar(float progress, Color fillColor)
    {
        var track = new VisualElement();
        track.style.height = 9;
        track.style.backgroundColor = new StyleColor(new Color(0.018f, 0.014f, 0.014f, 1f));
        track.style.borderTopWidth = 1;
        track.style.borderRightWidth = 1;
        track.style.borderBottomWidth = 1;
        track.style.borderLeftWidth = 1;
        SetBorderColor(track, new Color(0.34f, 0.16f, 0.14f, 0.38f), new Color(0f, 0f, 0f, 0.7f));

        var fill = new VisualElement();
        fill.style.height = Length.Percent(100);
        fill.style.width = Length.Percent(Mathf.Clamp01(progress) * 100f);
        fill.style.backgroundColor = new StyleColor(fillColor);
        track.Add(fill);

        return track;
    }

    private void TryBandageInjuryFromTab(InjurySystem injurySystem, Injury injury)
    {
        if (injurySystem == null || injury == null || injury.isBandaged)
            return;

        if (!TryFindBandageItem(out Item bandageItem))
        {
            UpdateInjuryTab();
            return;
        }

        if (!inventory.RemoveItem(bandageItem, 1))
        {
            UpdateInjuryTab();
            return;
        }

        injurySystem.BandageInjury(injury);
        UpdateInjuryTab();
    }

    private bool TryFindBandageItem(out Item bandageItem)
    {
        bandageItem = null;

        if (inventory == null)
            return false;

        var slots = inventory.GetAllItems();
        if (slots == null)
            return false;

        foreach (var slot in slots)
        {
            if (slot == null || slot.item == null || slot.quantity <= 0)
                continue;

            if (slot.item is BandageItem)
            {
                bandageItem = slot.item;
                return true;
            }

            string itemName = slot.item.ItemName;
            if (!string.IsNullOrWhiteSpace(itemName) && itemName.ToLowerInvariant().Contains("bandage"))
            {
                bandageItem = slot.item;
                return true;
            }
        }

        return false;
    }

    private int CountBandageItems()
    {
        if (inventory == null)
            return 0;

        int count = 0;
        var slots = inventory.GetAllItems();
        if (slots == null)
            return 0;

        foreach (var slot in slots)
        {
            if (slot == null || slot.item == null || slot.quantity <= 0)
                continue;

            bool isBandage = slot.item is BandageItem;
            if (!isBandage)
            {
                string itemName = slot.item.ItemName;
                isBandage = !string.IsNullOrWhiteSpace(itemName) && itemName.ToLowerInvariant().Contains("bandage");
            }

            if (isBandage)
                count += slot.quantity;
        }

        return count;
    }

    private static string FormatBodyPart(BodyPart bodyPart)
    {
        return bodyPart switch
        {
            BodyPart.LeftArm => "Left Arm",
            BodyPart.RightArm => "Right Arm",
            BodyPart.LeftLeg => "Left Leg",
            BodyPart.RightLeg => "Right Leg",
            _ => bodyPart.ToString()
        };
    }

    private static string GetInjurySeverityLabel(Injury injury)
    {
        if (injury == null)
            return "UNKNOWN";

        if (injury.isInfected)
            return "CRITICAL";

        return injury.injuryType switch
        {
            InjuryType.Bitten => "SEVERE",
            InjuryType.Laceration => "MODERATE",
            _ => "MINOR"
        };
    }

    private static Color GetInjuryAccentColor(Injury injury)
    {
        if (injury == null)
            return new Color(0.6f, 0.55f, 0.5f, 1f);

        if (injury.isInfected)
            return new Color(0.92f, 0.2f, 0.16f, 1f);

        if (injury.isBandaged)
            return new Color(0.72f, 0.86f, 0.56f, 1f);

        return injury.injuryType switch
        {
            InjuryType.Bitten => new Color(0.86f, 0.24f, 0.2f, 1f),
            InjuryType.Laceration => new Color(0.92f, 0.5f, 0.25f, 1f),
            _ => new Color(0.78f, 0.66f, 0.42f, 1f)
        };
    }

    private VisualElement CreateSkillCard(string skillName, string category, int level, float currentXp, float neededXp, string effectText, Color accentColor)
    {
        var card = new VisualElement();
        card.style.flexDirection = FlexDirection.Row;
        card.style.marginBottom = 10;
        card.style.paddingTop = 10;
        card.style.paddingRight = 10;
        card.style.paddingBottom = 10;
        card.style.paddingLeft = 10;
        card.style.backgroundColor = new StyleColor(new Color(0.065f, 0.078f, 0.092f, 0.96f));
        card.style.borderTopLeftRadius = 3;
        card.style.borderTopRightRadius = 3;
        card.style.borderBottomLeftRadius = 3;
        card.style.borderBottomRightRadius = 3;
        card.style.borderTopWidth = 1;
        card.style.borderRightWidth = 1;
        card.style.borderBottomWidth = 1;
        card.style.borderLeftWidth = 1;
        SetBorderColor(card, new Color(accentColor.r, accentColor.g, accentColor.b, 0.3f), new Color(0f, 0f, 0f, 0.62f));

        var rankPlate = new VisualElement();
        rankPlate.style.width = 74;
        rankPlate.style.height = 74;
        rankPlate.style.flexShrink = 0;
        rankPlate.style.marginRight = 12;
        rankPlate.style.alignItems = Align.Center;
        rankPlate.style.justifyContent = Justify.Center;
        rankPlate.style.backgroundColor = new StyleColor(new Color(0.015f, 0.018f, 0.022f, 0.92f));
        rankPlate.style.borderTopWidth = 1;
        rankPlate.style.borderRightWidth = 1;
        rankPlate.style.borderBottomWidth = 1;
        rankPlate.style.borderLeftWidth = 1;
        SetBorderColor(rankPlate, new Color(accentColor.r, accentColor.g, accentColor.b, 0.42f), new Color(0f, 0f, 0f, 0.68f));

        var levelNumber = new Label(level.ToString());
        levelNumber.style.fontSize = 26;
        levelNumber.style.unityFontStyleAndWeight = FontStyle.Bold;
        levelNumber.style.color = new StyleColor(accentColor);
        levelNumber.style.unityTextAlign = TextAnchor.MiddleCenter;
        rankPlate.Add(levelNumber);
        card.Add(rankPlate);

        var body = new VisualElement();
        body.style.flexDirection = FlexDirection.Column;
        body.style.flexGrow = 1;
        body.style.minWidth = 0;

        var top = new VisualElement();
        top.style.flexDirection = FlexDirection.Row;
        top.style.justifyContent = Justify.SpaceBetween;
        top.style.alignItems = Align.Center;

        var titleStack = new VisualElement();
        titleStack.style.flexDirection = FlexDirection.Column;
        titleStack.style.minWidth = 0;
        titleStack.style.flexGrow = 1;

        var categoryLabel = new Label(category.ToUpperInvariant());
        categoryLabel.style.color = new StyleColor(new Color(0.55f, 0.63f, 0.7f, 0.92f));
        categoryLabel.style.fontSize = 10;
        categoryLabel.style.letterSpacing = 1;
        titleStack.Add(categoryLabel);

        var nameLabel = new Label(skillName.ToUpperInvariant());
        nameLabel.style.color = new StyleColor(accentColor);
        nameLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
        nameLabel.style.fontSize = 14;
        nameLabel.style.letterSpacing = 1;
        titleStack.Add(nameLabel);

        var levelLabel = new Label($"LVL {level}");
        levelLabel.style.color = new StyleColor(new Color(0.9f, 0.94f, 0.98f, 1f));
        levelLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
        levelLabel.style.fontSize = 11;
        levelLabel.style.paddingTop = 2;
        levelLabel.style.paddingRight = 7;
        levelLabel.style.paddingBottom = 2;
        levelLabel.style.paddingLeft = 7;
        levelLabel.style.backgroundColor = new StyleColor(new Color(0.015f, 0.018f, 0.022f, 0.82f));
        levelLabel.style.borderTopLeftRadius = 2;
        levelLabel.style.borderTopRightRadius = 2;
        levelLabel.style.borderBottomLeftRadius = 2;
        levelLabel.style.borderBottomRightRadius = 2;

        top.Add(titleStack);
        top.Add(levelLabel);
        body.Add(top);

        var effect = new Label(effectText);
        effect.style.fontSize = 11;
        effect.style.marginTop = 4;
        effect.style.marginBottom = 7;
        effect.style.color = new StyleColor(new Color(0.7f, 0.74f, 0.78f, 0.94f));
        effect.style.whiteSpace = WhiteSpace.Normal;
        body.Add(effect);

        var barBackground = new VisualElement();
        barBackground.style.height = 12;
        barBackground.style.backgroundColor = new StyleColor(new Color(0.015f, 0.018f, 0.022f, 1f));
        barBackground.style.borderTopWidth = 1;
        barBackground.style.borderRightWidth = 1;
        barBackground.style.borderBottomWidth = 1;
        barBackground.style.borderLeftWidth = 1;
        SetBorderColor(barBackground, new Color(accentColor.r, accentColor.g, accentColor.b, 0.22f), new Color(0f, 0f, 0f, 0.65f));
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

        var xpLabel = new Label(neededXp <= 0f ? "MAX" : $"{currentXp:F0} / {neededXp:F0} XP");
        xpLabel.style.position = Position.Absolute;
        xpLabel.style.right = 6;
        xpLabel.style.top = -2;
        xpLabel.style.fontSize = 9;
        xpLabel.style.color = new StyleColor(new Color(0.93f, 0.96f, 1f, 0.95f));
        xpLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
        barBackground.Add(xpLabel);

        body.Add(barBackground);
        card.Add(body);
        return card;
    }

    private static string GetSkillEffectText(string skillName, int level)
    {
        float normalized = Mathf.Clamp01((level - 1f) / 9f);

        if (skillName.Contains("Accuracy"))
            return $"Improves weapon handling and spread control. Current control bonus: {normalized * 10f:0.#}%.";

        return skillName switch
        {
            "Stamina" => $"Reduces sprint fatigue over time. Drain reduction: {normalized * 50f:0.#}%.",
            "Metabolism" => $"Slows hunger and thirst decay. Efficiency gain: {normalized * 50f:0.#}%.",
            "Vitality" => $"Raises long-term survivability. Health reserve bonus: {normalized * 50f:0.#}.",
            "Stealth" => "Improves by moving quietly while crouched.",
            "Strength" => $"Improves melee force. Damage bonus: {normalized * 50f:0.#}%.",
            _ => "Improves through repeated use."
        };
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

    private InjurySystem GetInjurySystem()
    {
        if (player == null) player = FindAnyObjectByType<Player>();
        if (player != null)
        {
            var sys = player.GetInjurySystem();
            if (sys != null) return sys;
        }
        return FindAnyObjectByType<InjurySystem>();
    }
}
