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

    private readonly Dictionary<int, VisualElement> inventoryCellsByIndex = new();

    private bool isItemDragPending;
    private bool isItemDragging;
    private int itemDragPointerId = -1;
    private int itemDragSourceSlotIndex = -1;
    private Item itemDragItem;
    private int itemDragQuantity;
    private Vector2 itemDragStartMouse;
    private VisualElement itemDragGhost;
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

    private static void EnsureRuntimeUIInput()
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
            // Active tab: darker, warmer (gold/brown) text
            button.style.backgroundColor = new StyleColor(new Color(0.25f, 0.27f, 0.33f, 0.95f));
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
            // Inactive tab: very dark, washed out
            button.style.backgroundColor = new StyleColor(new Color(0.15f, 0.16f, 0.2f, 0.8f));
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
        row.style.marginBottom = 8;

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

        button.style.height = 26;
        button.style.paddingLeft = 8;
        button.style.paddingRight = 8;
        button.style.marginRight = 6;
        button.style.marginBottom = 6;
        button.style.unityFontStyleAndWeight = FontStyle.Bold;
        button.style.fontSize = 10;
        button.style.borderTopLeftRadius = 4;
        button.style.borderTopRightRadius = 4;
        button.style.borderBottomLeftRadius = 4;
        button.style.borderBottomRightRadius = 4;
        button.style.backgroundColor = new StyleColor(isActive
            ? new Color(0.17f, 0.2f, 0.24f, 0.98f)
            : new Color(0.08f, 0.09f, 0.11f, 0.96f));
        button.style.color = new StyleColor(isActive
            ? new Color(0.91f, 0.95f, 0.99f, 1f)
            : new Color(0.66f, 0.7f, 0.76f, 0.96f));
        button.style.borderTopWidth = 1;
        button.style.borderRightWidth = 1;
        button.style.borderBottomWidth = 1;
        button.style.borderLeftWidth = 1;
        button.style.borderTopColor = new StyleColor(new Color(0.34f, 0.39f, 0.46f, isActive ? 0.66f : 0.25f));
        button.style.borderRightColor = new StyleColor(new Color(0.34f, 0.39f, 0.46f, isActive ? 0.66f : 0.25f));
        button.style.borderBottomColor = new StyleColor(new Color(0.21f, 0.24f, 0.29f, isActive ? 0.9f : 0.5f));
        button.style.borderLeftColor = new StyleColor(new Color(0.21f, 0.24f, 0.29f, isActive ? 0.9f : 0.5f));

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
        var card = new VisualElement();
        card.style.flexDirection = FlexDirection.Column;
        card.style.marginBottom = 8;
        card.style.paddingTop = 10;
        card.style.paddingRight = 12;
        card.style.paddingBottom = 10;
        card.style.paddingLeft = 12;
        card.style.backgroundColor = new StyleColor(new Color(0.1f, 0.1f, 0.1f, 0.96f));
        card.style.borderTopLeftRadius = 8;
        card.style.borderTopRightRadius = 8;
        card.style.borderBottomLeftRadius = 8;
        card.style.borderBottomRightRadius = 8;
        card.style.borderTopWidth = 1;
        card.style.borderRightWidth = 1;
        card.style.borderBottomWidth = 1;
        card.style.borderLeftWidth = 1;
        card.style.borderTopColor = new StyleColor(new Color(0.28f, 0.28f, 0.28f, 0.4f));
        card.style.borderRightColor = new StyleColor(new Color(0.28f, 0.28f, 0.28f, 0.4f));
        card.style.borderBottomColor = new StyleColor(new Color(0.22f, 0.22f, 0.22f, 0.3f));
        card.style.borderLeftColor = new StyleColor(new Color(0.22f, 0.22f, 0.22f, 0.3f));

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
        durationBadge.style.backgroundColor = new StyleColor(new Color(0.16f, 0.16f, 0.16f, 0.98f));
        durationBadge.style.color = new StyleColor(new Color(0.87f, 0.87f, 0.87f, 1f));
        durationBadge.style.borderTopLeftRadius = 4;
        durationBadge.style.borderTopRightRadius = 4;
        durationBadge.style.borderBottomLeftRadius = 4;
        durationBadge.style.borderBottomRightRadius = 4;
        durationBadge.style.unityFontStyleAndWeight = FontStyle.Bold;

        string recipeName = !string.IsNullOrWhiteSpace(recipe.RecipeName)
            ? recipe.RecipeName
            : (recipe.OutputItem != null ? recipe.OutputItem.ItemName : "Recipe");

        var title = new Label($"{recipeName}  →  {GetOutputText(recipe)}");
        title.style.fontSize = 13;
        title.style.unityFontStyleAndWeight = FontStyle.Bold;
        title.style.color = new StyleColor(new Color(0.94f, 0.94f, 0.94f, 1f));
        topRow.Add(title);
        topRow.Add(durationBadge);
        card.Add(topRow);

        if (!string.IsNullOrWhiteSpace(recipe.Description))
        {
            var description = new Label(recipe.Description);
            description.style.fontSize = 11;
            description.style.marginTop = 2;
            description.style.marginBottom = 6;
            description.style.color = new StyleColor(new Color(0.74f, 0.74f, 0.74f, 0.95f));
            description.style.whiteSpace = WhiteSpace.Normal;
            card.Add(description);
        }

        var requirementsTitle = new Label("Requirements:");
        requirementsTitle.style.fontSize = 11;
        requirementsTitle.style.marginTop = 4;
        requirementsTitle.style.marginBottom = 2;
        requirementsTitle.style.color = new StyleColor(new Color(0.82f, 0.82f, 0.82f, 0.95f));
        card.Add(requirementsTitle);

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
                var reqLabel = new Label($"• {req.Item.ItemName}   {owned}/{req.Quantity}");
                reqLabel.style.fontSize = 11;
                reqLabel.style.color = new StyleColor(ok
                    ? new Color(0.76f, 0.9f, 0.76f, 0.95f)
                    : new Color(1f, 0.52f, 0.52f, 0.95f));
                card.Add(reqLabel);
            }
        }

        if (!hasRequirements)
        {
            var none = new Label("• No requirements");
            none.style.fontSize = 11;
            none.style.color = new StyleColor(new Color(0.72f, 0.72f, 0.72f, 0.96f));
            card.Add(none);
        }

        bool isCraftingBusy = craftingSystem != null && craftingSystem.IsCraftingInProgress;
        bool canCraft = craftingSystem != null && craftingSystem.CanCraftRecipe(recipe) && !isCraftingBusy;
        string buttonText = isCraftingBusy
            ? "Crafting in progress..."
            : (canCraft ? $"Craft ({recipe.CraftDurationSeconds:0.0}s)" : "Missing resources");

        var craftButton = new Button(() => TryCraftRecipe(recipe)) { text = buttonText };
        craftButton.SetEnabled(canCraft);
        craftButton.style.marginTop = 8;
        craftButton.style.height = 30;
        craftButton.style.unityFontStyleAndWeight = FontStyle.Bold;
        craftButton.style.backgroundColor = new StyleColor(canCraft
            ? new Color(0.24f, 0.24f, 0.24f, 0.98f)
            : new Color(0.16f, 0.16f, 0.16f, 0.95f));
        craftButton.style.color = new StyleColor(canCraft
            ? new Color(0.95f, 0.95f, 0.95f, 1f)
            : new Color(0.62f, 0.62f, 0.62f, 1f));
        card.Add(craftButton);

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
        container.style.paddingTop = 8;
        container.style.paddingRight = 10;
        container.style.paddingBottom = 8;
        container.style.paddingLeft = 10;
        container.style.backgroundColor = new StyleColor(new Color(0.09f, 0.09f, 0.09f, 0.97f));
        container.style.borderTopLeftRadius = 8;
        container.style.borderTopRightRadius = 8;
        container.style.borderBottomLeftRadius = 8;
        container.style.borderBottomRightRadius = 8;
        container.style.borderTopWidth = 1;
        container.style.borderRightWidth = 1;
        container.style.borderBottomWidth = 1;
        container.style.borderLeftWidth = 1;
        container.style.borderTopColor = new StyleColor(new Color(0.28f, 0.28f, 0.28f, 0.45f));
        container.style.borderRightColor = new StyleColor(new Color(0.28f, 0.28f, 0.28f, 0.45f));
        container.style.borderBottomColor = new StyleColor(new Color(0.22f, 0.22f, 0.22f, 0.3f));
        container.style.borderLeftColor = new StyleColor(new Color(0.22f, 0.22f, 0.22f, 0.3f));

        var title = new Label("Crafting Bench");
        title.style.fontSize = 14;
        title.style.unityFontStyleAndWeight = FontStyle.Bold;
        title.style.color = new StyleColor(new Color(0.95f, 0.95f, 0.95f, 1f));
        container.Add(title);

        string statusText = craftingSystem != null ? craftingSystem.StatusMessage : "Crafting unavailable.";
        Color statusColor = craftingSystem != null ? craftingSystem.StatusColor : new Color(1f, 0.55f, 0.55f, 0.95f);

        var status = new Label(statusText);
        status.style.fontSize = 11;
        status.style.marginTop = 2;
        status.style.color = new StyleColor(statusColor);
        container.Add(status);

        var progressTrack = new VisualElement();
        progressTrack.style.height = 14;
        progressTrack.style.marginTop = 7;
        progressTrack.style.backgroundColor = new StyleColor(new Color(0.06f, 0.08f, 0.06f, 1f));
        progressTrack.style.borderTopLeftRadius = 3;
        progressTrack.style.borderTopRightRadius = 3;
        progressTrack.style.borderBottomLeftRadius = 3;
        progressTrack.style.borderBottomRightRadius = 3;

        var progressFill = new VisualElement();
        progressFill.style.height = Length.Percent(100);
        float progress = craftingSystem != null ? craftingSystem.CraftingProgress01 : 0f;
        progressFill.style.width = Length.Percent(progress * 100f);
        progressFill.style.backgroundColor = new StyleColor(new Color(0.45f, 0.84f, 0.45f, 1f));
        progressFill.style.borderTopLeftRadius = 3;
        progressFill.style.borderTopRightRadius = 3;
        progressFill.style.borderBottomLeftRadius = 3;
        progressFill.style.borderBottomRightRadius = 3;
        progressTrack.Add(progressFill);

        container.Add(progressTrack);
        return container;
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
        float gap = 4f;
        float viewportWidth = GetInventoryViewportWidth();
        // Calculate cell size to fill viewport evenly based on resolution
        float cellSize = (viewportWidth - ((columns - 1) * gap)) / columns;
        cellSize = Mathf.Max(40f, cellSize); // Ensure minimum 40px but scale up with viewport
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

        viewportWidth -= 14f; // reserve width for vertical scrollbar
        return Mathf.Max(120f, viewportWidth);
    }
    private static void ApplyInventoryScrollStyle(ScrollView scrollView)
    {
        if (scrollView == null)
            return;

        scrollView.pickingMode = PickingMode.Position;
        
        scrollView.style.paddingTop = 0;
        scrollView.style.paddingRight = 0;
        scrollView.style.paddingBottom = 0;
        scrollView.style.paddingLeft = 0;

        if (scrollView.contentContainer != null)
        {
            scrollView.contentContainer.pickingMode = PickingMode.Position;
            scrollView.contentContainer.style.paddingTop = 8;
            scrollView.contentContainer.style.paddingRight = 16;
            scrollView.contentContainer.style.paddingBottom = 8;
            scrollView.contentContainer.style.paddingLeft = 8;
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
            verticalScroller.style.width = 10;
            verticalScroller.style.minWidth = 10;
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
        cell.style.backgroundColor = new StyleColor(new Color(0.1f, 0.11f, 0.14f, 0.88f));
        cell.style.borderTopWidth = 1;
        cell.style.borderRightWidth = 1;
        cell.style.borderBottomWidth = 1;
        cell.style.borderLeftWidth = 1;
        cell.style.borderTopColor = new StyleColor(new Color(0.5f, 0.55f, 0.65f, 0.2f));
        cell.style.borderRightColor = new StyleColor(new Color(0.5f, 0.55f, 0.65f, 0.2f));
        cell.style.borderBottomColor = new StyleColor(new Color(0.3f, 0.33f, 0.4f, 0.12f));
        cell.style.borderLeftColor = new StyleColor(new Color(0.3f, 0.33f, 0.4f, 0.12f));
        cell.style.borderTopLeftRadius = 2;
        cell.style.borderTopRightRadius = 2;
        cell.style.borderBottomLeftRadius = 2;
        cell.style.borderBottomRightRadius = 2;
        cell.name = $"inventory-slot-cell-{slotIndex}";
        cell.pickingMode = PickingMode.Position;  // Allow dragging detection over this cell

        // Slot number label
        var slotLabel = new Label((slotIndex + 1).ToString());
        slotLabel.style.position = Position.Absolute;
        slotLabel.style.left = 2;
        slotLabel.style.top = 1;
        slotLabel.style.fontSize = 8;
        slotLabel.style.color = new StyleColor(new Color(0.5f, 0.6f, 0.75f, 0.5f));
        slotLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
        cell.Add(slotLabel);

        return cell;
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
        tile.style.backgroundColor = new StyleColor(new Color(0.12f, 0.12f, 0.12f, 0.96f));
        tile.style.borderTopWidth = 1;
        tile.style.borderRightWidth = 1;
        tile.style.borderBottomWidth = 1;
        tile.style.borderLeftWidth = 1;
        tile.style.borderTopColor = new StyleColor(new Color(0.62f, 0.62f, 0.62f, 0.35f));
        tile.style.borderRightColor = new StyleColor(new Color(0.62f, 0.62f, 0.62f, 0.35f));
        tile.style.borderBottomColor = new StyleColor(new Color(0.62f, 0.62f, 0.62f, 0.24f));
        tile.style.borderLeftColor = new StyleColor(new Color(0.62f, 0.62f, 0.62f, 0.24f));
        tile.style.borderTopLeftRadius = 5;
        tile.style.borderTopRightRadius = 5;
        tile.style.borderBottomLeftRadius = 5;
        tile.style.borderBottomRightRadius = 5;
        tile.name = $"inventory-item-tile-{anchorIndex}";
        tile.pickingMode = PickingMode.Position;  // Allow mouse/pointer events
        tile.focusable = true;

        if (slot.item.Icon != null)
        {
            var icon = new Image { image = slot.item.Icon.texture, scaleMode = ScaleMode.ScaleToFit };
            icon.style.position = Position.Absolute;
            icon.style.left = 3;
            icon.style.right = 3;
            icon.style.top = 3;
            icon.style.bottom = 3;
            icon.pickingMode = PickingMode.Ignore;
            tile.Add(icon);
        }
        else
        {
            var fallbackLabel = new Label(string.IsNullOrWhiteSpace(slot.item.ItemName) ? "Item" : slot.item.ItemName);
            fallbackLabel.style.position = Position.Absolute;
            fallbackLabel.style.left = 6;
            fallbackLabel.style.right = 6;
            fallbackLabel.style.top = 6;
            fallbackLabel.style.fontSize = 15;
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
            qty.style.right = 5;
            qty.style.bottom = 3;
            qty.style.fontSize = 13;
            qty.style.unityFontStyleAndWeight = FontStyle.Bold;
            qty.pickingMode = PickingMode.Ignore;
            tile.Add(qty);
        }

        tile.RegisterCallback<MouseEnterEvent>(evt =>
        {
            ItemTooltipUtility.ShowTooltip(root, slot.item, slot.quantity, GetTooltipAnchorPoint(tile, evt.mousePosition));
        });

        tile.RegisterCallback<MouseMoveEvent>(evt =>
        {
            ItemTooltipUtility.ShowTooltip(root, slot.item, slot.quantity, GetTooltipAnchorPoint(tile, evt.mousePosition));
        });

        tile.RegisterCallback<MouseLeaveEvent>(_ => ItemTooltipUtility.HideTooltip(root));
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
    }

    private void OnRootPointerUpItemDrag(PointerUpEvent evt)
    {
        if (evt.button != 0 || evt.pointerId != itemDragPointerId) return;
        if (isItemDragging && inventory != null)
        {
            int targetSlot = GetInventorySlotIndexAtWorldPosition(evt.position);
            if (targetSlot >= 0) inventory.MoveItem(itemDragSourceSlotIndex, targetSlot);
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
        itemDragGhost.style.width = 52;
        itemDragGhost.style.height = 52;
        var icon = new Image { image = itemDragItem.Icon != null ? itemDragItem.Icon.texture : null, scaleMode = ScaleMode.ScaleToFit };
        icon.style.width = 42;
        icon.style.height = 42;
        icon.style.marginLeft = 5;
        icon.style.marginTop = 5;
        itemDragGhost.Add(icon);
        rootVisual.Add(itemDragGhost);
    }

    private void UpdateItemDragGhostPosition(Vector2 mouseWorldPos)
    {
        if (itemDragGhost == null) return;
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

            // Show quantity
            var qtyLabel = new Label($"Quantity: {slot.quantity}");
            qtyLabel.style.fontSize = 10;
            qtyLabel.style.color = new StyleColor(new Color(0.85f, 0.85f, 0.9f, 0.9f));
            qtyLabel.style.marginBottom = 4;
            detailItemProperties.Add(qtyLabel);

            // Show item type/category
            if (slot.item is GunItem gun)
            {
                var typeLabel = new Label($"Type: {gun.GunType}");
                typeLabel.style.fontSize = 10;
                typeLabel.style.color = new StyleColor(new Color(0.85f, 0.85f, 0.9f, 0.9f));
                typeLabel.style.marginBottom = 4;
                detailItemProperties.Add(typeLabel);
            }
            else if (slot.item is FoodItem food)
            {
                var typeLabel = new Label($"Type: Food");
                typeLabel.style.fontSize = 10;
                typeLabel.style.color = new StyleColor(new Color(0.85f, 0.85f, 0.9f, 0.9f));
                typeLabel.style.marginBottom = 2;
                detailItemProperties.Add(typeLabel);

                if (!Mathf.Approximately(food.HungerRestore, 0))
                {
                    var hungerLabel = new Label($"+{food.HungerRestore:F1} Hunger");
                    hungerLabel.style.fontSize = 9;
                    hungerLabel.style.color = new StyleColor(new Color(0.7f, 0.9f, 0.7f, 0.85f));
                    hungerLabel.style.marginBottom = 2;
                    detailItemProperties.Add(hungerLabel);
                }
                if (!Mathf.Approximately(food.ThirstRestore, 0))
                {
                    var thirstLabel = new Label($"+{food.ThirstRestore:F1} Thirst");
                    thirstLabel.style.fontSize = 9;
                    thirstLabel.style.color = new StyleColor(new Color(0.7f, 0.9f, 0.7f, 0.85f));
                    detailItemProperties.Add(thirstLabel);
                }
            }
            else if (slot.item is WaterItem water)
            {
                var typeLabel = new Label($"Type: Water");
                typeLabel.style.fontSize = 10;
                typeLabel.style.color = new StyleColor(new Color(0.85f, 0.85f, 0.9f, 0.9f));
                typeLabel.style.marginBottom = 2;
                detailItemProperties.Add(typeLabel);

                if (!Mathf.Approximately(water.ThirstRestore, 0))
                {
                    var thirstLabel = new Label($"+{water.ThirstRestore:F1} Thirst");
                    thirstLabel.style.fontSize = 9;
                    thirstLabel.style.color = new StyleColor(new Color(0.7f, 0.9f, 0.7f, 0.85f));
                    detailItemProperties.Add(thirstLabel);
                }
            }

            AddDetailActionButtons(slot, slotIndex);
        }
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
        actionsHeader.style.unityFontStyleAndWeight = FontStyle.Bold;
        actionsHeader.style.color = new StyleColor(new Color(0.82f, 0.82f, 0.9f, 0.95f));
        actionsHeader.style.marginTop = 6;
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
        });
        button.text = text;
        button.style.height = 28;
        button.style.marginBottom = 4;
        button.style.paddingLeft = 8;
        button.style.paddingRight = 8;
        button.style.borderTopLeftRadius = 6;
        button.style.borderTopRightRadius = 6;
        button.style.borderBottomLeftRadius = 6;
        button.style.borderBottomRightRadius = 6;
        button.style.borderTopWidth = 1;
        button.style.borderRightWidth = 1;
        button.style.borderBottomWidth = 1;
        button.style.borderLeftWidth = 1;
        button.style.borderTopColor = new StyleColor(new Color(0.62f, 0.62f, 0.62f, 0.2f));
        button.style.borderRightColor = new StyleColor(new Color(0.62f, 0.62f, 0.62f, 0.2f));
        button.style.borderBottomColor = new StyleColor(new Color(0.62f, 0.62f, 0.62f, 0.14f));
        button.style.borderLeftColor = new StyleColor(new Color(0.62f, 0.62f, 0.62f, 0.14f));
        button.style.backgroundColor = new StyleColor(new Color(0.13f, 0.13f, 0.14f, 0.98f));
        button.style.color = new StyleColor(new Color(0.92f, 0.92f, 0.94f, 1f));
        button.style.unityTextAlign = TextAnchor.MiddleLeft;
        button.style.unityFontStyleAndWeight = FontStyle.Normal;
        button.style.fontSize = 11;

        button.RegisterCallback<MouseEnterEvent>(_ =>
        {
            button.style.backgroundColor = new StyleColor(new Color(0.2f, 0.2f, 0.22f, 0.98f));
            button.style.color = new StyleColor(new Color(1f, 1f, 1f, 1f));
        });

        button.RegisterCallback<MouseLeaveEvent>(_ =>
        {
            button.style.backgroundColor = new StyleColor(new Color(0.13f, 0.13f, 0.14f, 0.98f));
            button.style.color = new StyleColor(new Color(0.92f, 0.92f, 0.94f, 1f));
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
