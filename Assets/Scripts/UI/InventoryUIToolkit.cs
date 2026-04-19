using UnityEngine;
using UnityEngine.UIElements;
using UnityEngine.InputSystem;
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
    private VisualElement inventoryDragHandle;
    private ScrollView itemsScrollView;
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

    private bool isWindowDragging;
    private int windowDragPointerId = -1;
    private Vector2 windowDragStartMouse;
    private Vector2 windowDragStartPanelPos;

    private void Awake()
    {
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

        rootVisual.RegisterCallback<PointerMoveEvent>(OnRootPointerMoveItemDrag);
        rootVisual.RegisterCallback<PointerUpEvent>(OnRootPointerUpItemDrag);
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

        if (inventoryDragHandle != null)
        {
            inventoryDragHandle.UnregisterCallback<PointerDownEvent>(OnInventoryDragPointerDown);
            inventoryDragHandle.UnregisterCallback<PointerMoveEvent>(OnInventoryDragPointerMove);
            inventoryDragHandle.UnregisterCallback<PointerUpEvent>(OnInventoryDragPointerUp);
        }

        if (rootVisual != null)
        {
            rootVisual.UnregisterCallback<PointerMoveEvent>(OnRootPointerMoveItemDrag);
            rootVisual.UnregisterCallback<PointerUpEvent>(OnRootPointerUpItemDrag);
        }
    }

    private void Update()
    {
        ResolveReferences();

        if (!IsInventoryOpen)
            return;

        // Avoid rebuilding tab UI every frame; this can interrupt click events.
        // Only keep crafting tab live-updated while an active craft is running.
        if (ActiveTab == InventoryTab.Crafting && craftingSystem != null && craftingSystem.IsCraftingInProgress)
            UpdateCraftingTab();
    }

    private void BindToggleInput()
    {
        if (toggleInventoryAction != null)
        {
            runtimeToggleAction = toggleInventoryAction.action;
        }
        else
        {
            var playerInput = FindAnyObjectByType<PlayerInput>();
            if (playerInput != null)
            {
                runtimeToggleAction = playerInput.actions.FindAction("ToggleInventory")
                                     ?? playerInput.actions.FindAction("Inventory");
            }
        }

        if (runtimeToggleAction != null)
        {
            runtimeToggleAction.performed += OnToggleInventory;
            runtimeToggleAction.Enable();
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
        ToggleInventory();
    }

    public void ToggleInventory() => SetInventoryOpen(!IsInventoryOpen);
    public void OpenFromExternal() => SetInventoryOpen(true);
    public void CloseFromExternal() => SetInventoryOpen(false);
    public bool IsOpenNow() => IsInventoryOpen;

    public void SetInventoryPanelVisible(bool visible)
    {
        if (inventoryPanel != null)
            inventoryPanel.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
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
        if (IsInventoryOpen == open) return;
        IsInventoryOpen = open;

        UnityEngine.Cursor.visible = open;
        UnityEngine.Cursor.lockState = open ? CursorLockMode.None : CursorLockMode.Locked;

        if (open)
            SetActiveTabInternal(InventoryTab.Inventory, false);

        SetInventoryPanelVisible(open);
        if (open) RefreshCurrentTab(); else CancelItemDrag();
    }

    private void SetActiveTabInternal(InventoryTab tab, bool refresh)
    {
        ActiveTab = tab;
        OnActiveTabChanged?.Invoke(tab);

        if (tabContentInventory != null) tabContentInventory.style.display = tab == InventoryTab.Inventory ? DisplayStyle.Flex : DisplayStyle.None;
        if (tabContentSkills != null) tabContentSkills.style.display = tab == InventoryTab.Skills ? DisplayStyle.Flex : DisplayStyle.None;
        if (tabContentInjury != null) tabContentInjury.style.display = tab == InventoryTab.Injury ? DisplayStyle.Flex : DisplayStyle.None;
        if (tabContentCrafting != null) tabContentCrafting.style.display = tab == InventoryTab.Crafting ? DisplayStyle.Flex : DisplayStyle.None;
        UpdateTopTabVisualState();

        if (refresh && IsInventoryOpen) RefreshCurrentTab();
    }

    private void CacheUiReferences()
    {
        if (rootVisual == null) return;
        inventoryPanel = rootVisual.Q<VisualElement>(className: "inventory-panel");
        tabContentInventory = rootVisual.Q<VisualElement>("tab-content-inventory");
        tabContentSkills = rootVisual.Q<VisualElement>("tab-content-skills");
        tabContentInjury = rootVisual.Q<VisualElement>("tab-content-injury");
        tabContentCrafting = rootVisual.Q<VisualElement>("tab-content-crafting");
        inventoryDragHandle = rootVisual.Q<VisualElement>("inventory-drag-handle");
        itemsScrollView = rootVisual.Q<ScrollView>(className: "items-list");
        skillsScrollView = rootVisual.Q<ScrollView>("skills-list");
        injuryScrollView = rootVisual.Q<ScrollView>("injury-list");
        craftingScrollView = rootVisual.Q<ScrollView>("crafting-list");
        inventoryTabButton = rootVisual.Q<Button>("tab-inventory");
        skillsTabButton = rootVisual.Q<Button>("tab-skills");
        injuryTabButton = rootVisual.Q<Button>("tab-injury");
        craftingTabButton = rootVisual.Q<Button>("tab-crafting");
    }

    private void HookTabButtons()
    {
        if (inventoryDragHandle != null)
        {
            inventoryDragHandle.RegisterCallback<PointerDownEvent>(OnInventoryDragPointerDown);
            inventoryDragHandle.RegisterCallback<PointerMoveEvent>(OnInventoryDragPointerMove);
            inventoryDragHandle.RegisterCallback<PointerUpEvent>(OnInventoryDragPointerUp);
        }

        if (inventoryTabButton != null)
        {
            inventoryTabButton.clicked += () => SetActiveTab(InventoryTab.Inventory);
            inventoryTabButton.RegisterCallback<ClickEvent>(_ => SetActiveTab(InventoryTab.Inventory));
        }

        if (skillsTabButton != null)
        {
            skillsTabButton.clicked += () => SetActiveTab(InventoryTab.Skills);
            skillsTabButton.RegisterCallback<ClickEvent>(_ => SetActiveTab(InventoryTab.Skills));
        }

        if (injuryTabButton != null)
        {
            injuryTabButton.clicked += () => SetActiveTab(InventoryTab.Injury);
            injuryTabButton.RegisterCallback<ClickEvent>(_ => SetActiveTab(InventoryTab.Injury));
        }

        if (craftingTabButton != null)
        {
            craftingTabButton.clicked += () => SetActiveTab(InventoryTab.Crafting);
            craftingTabButton.RegisterCallback<ClickEvent>(_ => SetActiveTab(InventoryTab.Crafting));
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

        button.style.backgroundColor = new StyleColor(isActive
            ? new Color(0.2f, 0.2f, 0.2f, 0.98f)
            : new Color(0.12f, 0.12f, 0.12f, 0.95f));

        button.style.color = new StyleColor(isActive
            ? new Color(0.96f, 0.96f, 0.96f, 1f)
            : new Color(0.75f, 0.75f, 0.75f, 0.96f));

        button.style.borderBottomWidth = isActive ? 2 : 1;
        button.style.borderBottomColor = new StyleColor(isActive
            ? new Color(0.68f, 0.68f, 0.68f, 0.9f)
            : new Color(0.22f, 0.22f, 0.22f, 0.9f));
    }

    private void OnInventoryDragPointerDown(PointerDownEvent evt)
    {
        if (evt.button != 0 || inventoryPanel == null)
            return;

        isWindowDragging = true;
        windowDragPointerId = evt.pointerId;
        windowDragStartMouse = new Vector2(evt.position.x, evt.position.y);

        float startLeft = inventoryPanel.resolvedStyle.left;
        float startTop = inventoryPanel.resolvedStyle.top;
        if (float.IsNaN(startLeft)) startLeft = inventoryPanel.worldBound.x;
        if (float.IsNaN(startTop)) startTop = inventoryPanel.worldBound.y;
        windowDragStartPanelPos = new Vector2(startLeft, startTop);

        inventoryPanel.style.marginLeft = 0;
        inventoryPanel.style.marginTop = 0;
        inventoryPanel.style.left = windowDragStartPanelPos.x;
        inventoryPanel.style.top = windowDragStartPanelPos.y;

        inventoryDragHandle.CapturePointer(windowDragPointerId);
        evt.StopPropagation();
    }

    private void OnInventoryDragPointerMove(PointerMoveEvent evt)
    {
        if (!isWindowDragging || evt.pointerId != windowDragPointerId || inventoryPanel == null || rootVisual == null)
            return;

        Vector2 currentMouse = new Vector2(evt.position.x, evt.position.y);
        Vector2 delta = currentMouse - windowDragStartMouse;
        float targetLeft = windowDragStartPanelPos.x + delta.x;
        float targetTop = windowDragStartPanelPos.y + delta.y;

        float maxLeft = Mathf.Max(0f, rootVisual.resolvedStyle.width - inventoryPanel.resolvedStyle.width);
        float maxTop = Mathf.Max(0f, rootVisual.resolvedStyle.height - inventoryPanel.resolvedStyle.height);

        inventoryPanel.style.left = Mathf.Clamp(targetLeft, 0f, maxLeft);
        inventoryPanel.style.top = Mathf.Clamp(targetTop, 0f, maxTop);

        evt.StopPropagation();
    }

    private void OnInventoryDragPointerUp(PointerUpEvent evt)
    {
        if (evt.pointerId != windowDragPointerId)
            return;

        isWindowDragging = false;

        if (inventoryDragHandle != null && inventoryDragHandle.HasPointerCapture(windowDragPointerId))
            inventoryDragHandle.ReleasePointer(windowDragPointerId);

        windowDragPointerId = -1;
        evt.StopPropagation();
    }

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
        button.style.borderTopLeftRadius = 6;
        button.style.borderTopRightRadius = 6;
        button.style.borderBottomLeftRadius = 6;
        button.style.borderBottomRightRadius = 6;
        button.style.backgroundColor = new StyleColor(isActive
            ? new Color(0.2f, 0.2f, 0.2f, 0.98f)
            : new Color(0.11f, 0.11f, 0.11f, 0.96f));
        button.style.color = new StyleColor(isActive
            ? new Color(0.95f, 0.95f, 0.95f, 1f)
            : new Color(0.73f, 0.73f, 0.73f, 0.96f));
        button.style.borderTopWidth = 1;
        button.style.borderRightWidth = 1;
        button.style.borderBottomWidth = 1;
        button.style.borderLeftWidth = 1;
        button.style.borderTopColor = new StyleColor(new Color(0.28f, 0.28f, 0.28f, 0.95f));
        button.style.borderRightColor = new StyleColor(new Color(0.28f, 0.28f, 0.28f, 0.95f));
        button.style.borderBottomColor = new StyleColor(new Color(0.2f, 0.2f, 0.2f, 0.95f));
        button.style.borderLeftColor = new StyleColor(new Color(0.2f, 0.2f, 0.2f, 0.95f));

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
        float gap = 6f;
        float viewportWidth = itemsScrollView.contentViewport != null ? itemsScrollView.contentViewport.resolvedStyle.width : itemsScrollView.resolvedStyle.width;
        viewportWidth -= 14f; // Reserve width so items don't get covered by the vertical scroller.
        if (viewportWidth <= 0f) viewportWidth = 700f;
        float cellSize = Mathf.Max(60f, (viewportWidth - ((columns - 1) * gap) - 12f) / columns);
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

        for (int i = 0; i < slots.Count; i++)
        {
            var slot = slots[i];
            if (slot == null || !slot.isOccupied || !slot.isAnchor || slot.item == null) continue;
            var tile = CreateInventoryItemTile(slot, i, columns, cellSize, gap, root);
            if (tile != null) grid.Add(tile);
        }

        itemsScrollView.Add(grid);
    }

    private static void ApplyInventoryScrollStyle(ScrollView scrollView)
    {
        if (scrollView == null)
            return;

        scrollView.style.paddingTop = 0;
        scrollView.style.paddingRight = 0;
        scrollView.style.paddingBottom = 0;
        scrollView.style.paddingLeft = 0;

        if (scrollView.contentContainer != null)
        {
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
        cell.style.backgroundColor = new StyleColor(new Color(0.08f, 0.08f, 0.08f, 0.82f));
        cell.style.borderTopWidth = 1;
        cell.style.borderRightWidth = 1;
        cell.style.borderBottomWidth = 1;
        cell.style.borderLeftWidth = 1;
        cell.style.borderTopColor = new StyleColor(new Color(0.42f, 0.42f, 0.42f, 0.24f));
        cell.style.borderRightColor = new StyleColor(new Color(0.42f, 0.42f, 0.42f, 0.24f));
        cell.style.borderBottomColor = new StyleColor(new Color(0.42f, 0.42f, 0.42f, 0.18f));
        cell.style.borderLeftColor = new StyleColor(new Color(0.42f, 0.42f, 0.42f, 0.18f));
        cell.style.borderTopLeftRadius = 5;
        cell.style.borderTopRightRadius = 5;
        cell.style.borderBottomLeftRadius = 5;
        cell.style.borderBottomRightRadius = 5;

        var slotLabel = new Label((slotIndex + 1).ToString());
        slotLabel.style.position = Position.Absolute;
        slotLabel.style.left = 3;
        slotLabel.style.top = 2;
        slotLabel.style.fontSize = 8;
        slotLabel.style.color = new StyleColor(new Color(0.68f, 0.73f, 0.83f, 0.66f));
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
            fallbackLabel.style.fontSize = 10;
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
            qty.style.fontSize = 10;
            qty.style.unityFontStyleAndWeight = FontStyle.Bold;
            tile.Add(qty);
        }

        tile.tooltip = $"{slot.item.ItemName} x{slot.quantity}";
        tile.RegisterCallback<MouseUpEvent>(evt => { if (evt.button == 1) ShowInventoryContextMenu(slot, root, evt); });
        tile.RegisterCallback<PointerDownEvent>(evt =>
        {
            if (evt.button != 0) return;
            BeginItemDragCandidate(slot.item, slot.quantity, anchorIndex, evt.pointerId, evt.position);
            evt.StopPropagation();
        });
        return tile;
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
    }

    private void ShowInventoryContextMenu(InventorySlot slot, VisualElement root, MouseUpEvent evt)
    {
        if (slot == null || slot.item == null) return;
        var existing = root.Q("inventory-context-menu");
        existing?.RemoveFromHierarchy();
        var menu = new VisualElement { name = "inventory-context-menu" };
        menu.style.position = Position.Absolute;
        Vector2 p = root.WorldToLocal(evt.mousePosition);
        menu.style.left = p.x;
        menu.style.top = p.y;
        menu.style.backgroundColor = new StyleColor(new Color(0.1f, 0.14f, 0.19f, 0.98f));
        menu.style.paddingLeft = 8;
        menu.style.paddingRight = 8;
        menu.style.paddingTop = 6;
        menu.style.paddingBottom = 6;
        AddContextActions(slot, menu);
        root.Add(menu);
    }

    private void AddContextActions(InventorySlot slot, VisualElement menu)
    {
        if (equipmentManager == null) equipmentManager = FindAnyObjectByType<EquipmentManager>();
        if (equipmentManager == null) return;
        if (slot.item is GunItem gun)
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
            return;
        }
        if (slot.item is MeleeWeaponItem melee)
        {
            AddContextActionLabel(menu, "Equip as Melee Slot", () => equipmentManager.EquipMeleeWeapon(melee));
            AddContextActionLabel(menu, "Select Melee (3)", equipmentManager.SelectMelee);
            AddContextActionLabel(menu, "Unequip Melee Slot", () => equipmentManager.EquipMeleeWeapon(null));
        }
    }

    private void AddContextActionLabel(VisualElement menu, string text, Action callback)
    {
        var label = new Label(text);
        label.RegisterCallback<MouseUpEvent>(_ =>
        {
            callback?.Invoke();
            var m = rootVisual?.Q("inventory-context-menu");
            m?.RemoveFromHierarchy();
        });
        menu.Add(label);
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
