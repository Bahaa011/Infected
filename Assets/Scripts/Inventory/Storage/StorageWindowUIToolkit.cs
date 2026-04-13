using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

public class StorageWindowUIToolkit : MonoBehaviour
{
    [SerializeField] private UIDocument uiDocument;
    [SerializeField] private InventoryUIToolkit inventoryUI;
    [SerializeField] private Inventory playerInventory;

    private Inventory activeStorageInventory;
    private string activeStorageLabel = "STORAGE";
    private bool inventoryWasOpenBeforeStorage;

    private VisualElement root;
    private VisualElement storagePanel;
    private Label storageTitle;
    private ScrollView playerScroll;
    private ScrollView storageScroll;

    private readonly Dictionary<int, VisualElement> playerCellsByIndex = new Dictionary<int, VisualElement>();
    private readonly Dictionary<int, VisualElement> storageCellsByIndex = new Dictionary<int, VisualElement>();

    private bool isItemDragPending;
    private bool isItemDragging;
    private int itemDragPointerId = -1;
    private int itemDragSourceSlotIndex = -1;
    private Vector2 itemDragStartMouse;
    private Inventory itemDragSourceInventory;
    private Item itemDragItem;
    private int itemDragQuantity;
    private VisualElement itemDragGhost;
    private const float ItemDragStartThreshold = 8f;

    private void Awake()
    {
        if (inventoryUI == null)
            inventoryUI = FindAnyObjectByType<InventoryUIToolkit>();

        if (uiDocument == null)
        {
            if (inventoryUI != null)
                uiDocument = inventoryUI.GetComponent<UIDocument>();

            if (uiDocument == null)
                uiDocument = FindAnyObjectByType<UIDocument>();
        }

        ResolvePlayerInventory();

        if (uiDocument == null)
            return;

        root = uiDocument.rootVisualElement;
        BuildStorageWindow();

        root.RegisterCallback<PointerMoveEvent>(OnRootPointerMoveItemDrag);
        root.RegisterCallback<PointerUpEvent>(OnRootPointerUpItemDrag);
    }

    private void OnDestroy()
    {
        if (root != null)
        {
            root.UnregisterCallback<PointerMoveEvent>(OnRootPointerMoveItemDrag);
            root.UnregisterCallback<PointerUpEvent>(OnRootPointerUpItemDrag);
        }

        UnsubscribeInventoryEvents();
    }

    private void Update()
    {
        if (activeStorageInventory == null)
            return;

        if (!InventoryUIToolkit.IsInventoryOpen)
            CloseStorage(false);
    }

    public bool IsOpenFor(Inventory storageInventory)
    {
        return activeStorageInventory != null && activeStorageInventory == storageInventory;
    }

    public void OpenStorage(Inventory storageInventory, string displayName)
    {
        if (storageInventory == null)
            return;

        ResolvePlayerInventory();

        if (playerInventory == null || root == null || storagePanel == null)
            return;

        if (activeStorageInventory == storageInventory)
        {
            Refresh();
            return;
        }

        UnsubscribeInventoryEvents();

        activeStorageInventory = storageInventory;
        activeStorageLabel = string.IsNullOrWhiteSpace(displayName) ? storageInventory.gameObject.name : displayName;

        SubscribeInventoryEvents();

        inventoryWasOpenBeforeStorage = inventoryUI != null && inventoryUI.IsOpenNow();
        if (inventoryUI != null)
        {
            inventoryUI.OpenFromExternal();
            inventoryUI.SetInventoryPanelVisible(false);
        }

        storagePanel.style.display = DisplayStyle.Flex;
        Refresh();
    }

    public void CloseStorage(bool closeInventoryIfOpenedByStorage = true)
    {
        CancelItemDrag();
        UnsubscribeInventoryEvents();

        activeStorageInventory = null;
        activeStorageLabel = "STORAGE";

        if (storageTitle != null)
            storageTitle.text = "STORAGE";

        if (storagePanel != null)
            storagePanel.style.display = DisplayStyle.None;

        if (inventoryUI != null)
            inventoryUI.SetInventoryPanelVisible(true);

        if (closeInventoryIfOpenedByStorage && !inventoryWasOpenBeforeStorage && inventoryUI != null)
            inventoryUI.CloseFromExternal();

        inventoryWasOpenBeforeStorage = false;
    }

    private void SubscribeInventoryEvents()
    {
        if (playerInventory != null)
            playerInventory.OnInventoryChanged += Refresh;

        if (activeStorageInventory != null)
            activeStorageInventory.OnInventoryChanged += Refresh;
    }

    private void UnsubscribeInventoryEvents()
    {
        if (playerInventory != null)
            playerInventory.OnInventoryChanged -= Refresh;

        if (activeStorageInventory != null)
            activeStorageInventory.OnInventoryChanged -= Refresh;
    }

    private void BuildStorageWindow()
    {
        storagePanel = new VisualElement();
        storagePanel.name = "storage-transfer-panel";
        storagePanel.style.position = Position.Absolute;
        const float panelWidth = 950f;
        const float panelHeight = 500f;
        storagePanel.style.left = Length.Percent(50);
        storagePanel.style.top = Length.Percent(50);
        storagePanel.style.width = panelWidth;
        storagePanel.style.height = panelHeight;
        storagePanel.style.marginLeft = -panelWidth * 0.5f;
        storagePanel.style.marginTop = -panelHeight * 0.5f;
        storagePanel.style.backgroundColor = new StyleColor(new Color(0.05f, 0.05f, 0.05f, 0.97f));
        storagePanel.style.borderTopLeftRadius = 8;
        storagePanel.style.borderTopRightRadius = 8;
        storagePanel.style.borderBottomLeftRadius = 8;
        storagePanel.style.borderBottomRightRadius = 8;
        storagePanel.style.borderTopWidth = 1;
        storagePanel.style.borderRightWidth = 1;
        storagePanel.style.borderBottomWidth = 1;
        storagePanel.style.borderLeftWidth = 1;
        storagePanel.style.borderTopColor = new StyleColor(new Color(0.45f, 0.45f, 0.45f, 0.24f));
        storagePanel.style.borderRightColor = new StyleColor(new Color(0.45f, 0.45f, 0.45f, 0.24f));
        storagePanel.style.borderBottomColor = new StyleColor(new Color(0.45f, 0.45f, 0.45f, 0.14f));
        storagePanel.style.borderLeftColor = new StyleColor(new Color(0.45f, 0.45f, 0.45f, 0.14f));
        storagePanel.style.paddingTop = 10;
        storagePanel.style.paddingRight = 10;
        storagePanel.style.paddingBottom = 10;
        storagePanel.style.paddingLeft = 10;
        storagePanel.style.display = DisplayStyle.None;

        storageTitle = new Label("STORAGE");
        storageTitle.style.fontSize = 14;
        storageTitle.style.unityFontStyleAndWeight = FontStyle.Bold;
        storageTitle.style.color = new StyleColor(new Color(0.95f, 0.95f, 0.95f, 1f));
        storageTitle.style.marginBottom = 8;
        storagePanel.Add(storageTitle);

        var content = new VisualElement();
        content.style.flexDirection = FlexDirection.Row;
        content.style.flexGrow = 1;

        var playerPane = CreatePane("PLAYER", out playerScroll);
        playerPane.style.marginRight = 8;
        var storagePane = CreatePane("CONTAINER", out storageScroll);

        content.Add(playerPane);
        content.Add(storagePane);
        storagePanel.Add(content);

        root.Add(storagePanel);
    }

    private VisualElement CreatePane(string label, out ScrollView scrollView)
    {
        var pane = new VisualElement();
        pane.style.flexDirection = FlexDirection.Column;
        pane.style.flexGrow = 1;

        var title = new Label(label);
        title.style.fontSize = 10;
        title.style.unityFontStyleAndWeight = FontStyle.Bold;
        title.style.color = new StyleColor(new Color(0.82f, 0.82f, 0.82f, 0.95f));
        title.style.marginBottom = 4;
        pane.Add(title);

        scrollView = new ScrollView(ScrollViewMode.VerticalAndHorizontal);
        scrollView.verticalScrollerVisibility = ScrollerVisibility.Auto;
        scrollView.horizontalScrollerVisibility = ScrollerVisibility.Hidden;
        scrollView.style.flexGrow = 1;
        scrollView.style.backgroundColor = new StyleColor(new Color(0.07f, 0.07f, 0.07f, 0.95f));
        scrollView.style.borderTopLeftRadius = 6;
        scrollView.style.borderTopRightRadius = 6;
        scrollView.style.borderBottomLeftRadius = 6;
        scrollView.style.borderBottomRightRadius = 6;
        scrollView.style.borderTopWidth = 1;
        scrollView.style.borderRightWidth = 1;
        scrollView.style.borderBottomWidth = 1;
        scrollView.style.borderLeftWidth = 1;
        scrollView.style.borderTopColor = new StyleColor(new Color(0.45f, 0.45f, 0.45f, 0.16f));
        scrollView.style.borderRightColor = new StyleColor(new Color(0.45f, 0.45f, 0.45f, 0.16f));
        scrollView.style.borderBottomColor = new StyleColor(new Color(0.45f, 0.45f, 0.45f, 0.1f));
        scrollView.style.borderLeftColor = new StyleColor(new Color(0.45f, 0.45f, 0.45f, 0.1f));
        ApplyInventoryLikeScrollStyle(scrollView);
        pane.Add(scrollView);

        return pane;
    }

    private static void ApplyInventoryLikeScrollStyle(ScrollView scrollView)
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
            scrollView.contentContainer.style.paddingRight = 8;
            scrollView.contentContainer.style.paddingBottom = 8;
            scrollView.contentContainer.style.paddingLeft = 8;
        }

        var verticalScroller = scrollView.verticalScroller;
        if (verticalScroller != null)
        {
            verticalScroller.style.position = Position.Absolute;
            verticalScroller.style.top = 0;
            verticalScroller.style.bottom = 0;
            verticalScroller.style.right = 0;
            verticalScroller.style.width = 10;
            verticalScroller.style.minWidth = 10;
            verticalScroller.style.backgroundColor = new StyleColor(new Color(0.1f, 0.1f, 0.1f, 0.9f));
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
                slider.style.backgroundColor = new StyleColor(new Color(0.12f, 0.12f, 0.12f, 0.9f));
                slider.style.borderTopLeftRadius = 4;
                slider.style.borderTopRightRadius = 4;
                slider.style.borderBottomLeftRadius = 4;
                slider.style.borderBottomRightRadius = 4;

                var tracker = slider.Q<VisualElement>(className: "unity-slider__tracker")
                              ?? slider.Q<VisualElement>(className: "unity-base-slider__tracker");
                if (tracker != null)
                    tracker.style.backgroundColor = new StyleColor(new Color(0.12f, 0.12f, 0.12f, 0.9f));

                var dragger = slider.Q<VisualElement>(className: "unity-dragger")
                              ?? slider.Q<VisualElement>(className: "unity-base-slider__dragger");
                if (dragger != null)
                {
                    dragger.style.backgroundColor = new StyleColor(new Color(0.75f, 0.75f, 0.75f, 1f));
                    dragger.style.unityBackgroundImageTintColor = new StyleColor(new Color(0.75f, 0.75f, 0.75f, 1f));
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

        var horizontalScroller = scrollView.horizontalScroller;
        if (horizontalScroller != null)
        {
            horizontalScroller.style.display = DisplayStyle.None;
            horizontalScroller.style.position = Position.Absolute;
            horizontalScroller.style.left = 0;
            horizontalScroller.style.right = 0;
            horizontalScroller.style.bottom = 0;
            horizontalScroller.style.height = 10;
            horizontalScroller.style.minHeight = 10;
            horizontalScroller.style.backgroundColor = new StyleColor(new Color(0.1f, 0.1f, 0.1f, 0.9f));
            horizontalScroller.style.borderTopLeftRadius = 4;
            horizontalScroller.style.borderTopRightRadius = 4;
            horizontalScroller.style.borderBottomLeftRadius = 4;
            horizontalScroller.style.borderBottomRightRadius = 4;

            var slider = horizontalScroller.slider;
            if (slider != null)
            {
                slider.style.flexGrow = 1;
                slider.style.backgroundColor = new StyleColor(new Color(0.12f, 0.12f, 0.12f, 0.9f));

                var tracker = slider.Q<VisualElement>(className: "unity-slider__tracker")
                              ?? slider.Q<VisualElement>(className: "unity-base-slider__tracker");
                if (tracker != null)
                    tracker.style.backgroundColor = new StyleColor(new Color(0.12f, 0.12f, 0.12f, 0.9f));

                var dragger = slider.Q<VisualElement>(className: "unity-dragger")
                              ?? slider.Q<VisualElement>(className: "unity-base-slider__dragger");
                if (dragger != null)
                {
                    dragger.style.backgroundColor = new StyleColor(new Color(0.75f, 0.75f, 0.75f, 1f));
                    dragger.style.unityBackgroundImageTintColor = new StyleColor(new Color(0.75f, 0.75f, 0.75f, 1f));
                    dragger.style.borderTopWidth = 0;
                    dragger.style.borderRightWidth = 0;
                    dragger.style.borderBottomWidth = 0;
                    dragger.style.borderLeftWidth = 0;
                }
            }

            var lowButton = horizontalScroller.Q<VisualElement>(className: "unity-scroller__low-button");
            if (lowButton != null)
                lowButton.style.display = DisplayStyle.None;

            var highButton = horizontalScroller.Q<VisualElement>(className: "unity-scroller__high-button");
            if (highButton != null)
                highButton.style.display = DisplayStyle.None;
        }
    }

    private void Refresh()
    {
        if (activeStorageInventory == null || playerInventory == null)
            return;

        ApplyInventoryLikeScrollStyle(playerScroll);
        ApplyInventoryLikeScrollStyle(storageScroll);

        if (storageTitle != null)
            storageTitle.text = $"STORAGE: {activeStorageLabel.ToUpperInvariant()}";

        BuildInventoryGrid(playerInventory, playerScroll, playerCellsByIndex, false);
        BuildInventoryGrid(activeStorageInventory, storageScroll, storageCellsByIndex, true);
    }

    private void BuildInventoryGrid(Inventory inv, ScrollView scrollView, Dictionary<int, VisualElement> cellMap, bool isStorageGrid)
    {
        if (inv == null || scrollView == null)
            return;

        scrollView.Clear();
        cellMap.Clear();

        var slots = inv.GetAllItems();
        int columns = Mathf.Max(1, inv.GetGridColumns());
        int rows = Mathf.Max(1, inv.GetGridRows());
        float gap = 6f;

        float viewportWidth = scrollView.contentViewport != null
            ? scrollView.contentViewport.resolvedStyle.width
            : scrollView.resolvedStyle.width;

        if (viewportWidth <= 0f)
            viewportWidth = 380f;

        float cellSize = Mathf.Max(50f, (viewportWidth - ((columns - 1) * gap) - 4f) / columns);

        var container = new VisualElement();
        container.style.position = Position.Relative;
        container.style.width = (columns * cellSize) + ((columns - 1) * gap);
        container.style.height = (rows * cellSize) + ((rows - 1) * gap);
        container.style.minWidth = container.style.width;
        container.style.minHeight = container.style.height;

        for (int i = 0; i < slots.Count; i++)
        {
            var bg = CreateBackgroundCell(i, columns, cellSize, gap, isStorageGrid);
            cellMap[i] = bg;
            container.Add(bg);
        }

        for (int i = 0; i < slots.Count; i++)
        {
            InventorySlot slot = slots[i];
            if (slot == null || !slot.isOccupied || !slot.isAnchor || slot.item == null)
                continue;

            var tile = CreateItemTile(slot, i, columns, cellSize, gap, inv);
            container.Add(tile);
        }

        scrollView.Add(container);
    }

    private VisualElement CreateBackgroundCell(int slotIndex, int columns, float cellSize, float gap, bool isStorageGrid)
    {
        int col = slotIndex % columns;
        int row = slotIndex / columns;

        var cell = new VisualElement();
        cell.style.position = Position.Absolute;
        cell.style.left = col * (cellSize + gap);
        cell.style.top = row * (cellSize + gap);
        cell.style.width = cellSize;
        cell.style.height = cellSize;
        cell.style.backgroundColor = isStorageGrid
            ? new StyleColor(new Color(0.09f, 0.09f, 0.09f, 0.78f))
            : new StyleColor(new Color(0.08f, 0.08f, 0.08f, 0.78f));
        cell.style.borderTopWidth = 1;
        cell.style.borderRightWidth = 1;
        cell.style.borderBottomWidth = 1;
        cell.style.borderLeftWidth = 1;
        cell.style.borderTopColor = new StyleColor(new Color(0.45f, 0.45f, 0.45f, 0.16f));
        cell.style.borderRightColor = new StyleColor(new Color(0.45f, 0.45f, 0.45f, 0.16f));
        cell.style.borderBottomColor = new StyleColor(new Color(0.45f, 0.45f, 0.45f, 0.1f));
        cell.style.borderLeftColor = new StyleColor(new Color(0.45f, 0.45f, 0.45f, 0.1f));
        cell.style.borderTopLeftRadius = 5;
        cell.style.borderTopRightRadius = 5;
        cell.style.borderBottomLeftRadius = 5;
        cell.style.borderBottomRightRadius = 5;

        return cell;
    }

    private VisualElement CreateItemTile(InventorySlot slot, int anchorIndex, int columns, float cellSize, float gap, Inventory sourceInventory)
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
        tile.style.backgroundColor = new StyleColor(new Color(0.13f, 0.13f, 0.13f, 0.96f));
        tile.style.borderTopWidth = 1;
        tile.style.borderRightWidth = 1;
        tile.style.borderBottomWidth = 1;
        tile.style.borderLeftWidth = 1;
        tile.style.borderTopColor = new StyleColor(new Color(0.5f, 0.5f, 0.5f, 0.22f));
        tile.style.borderRightColor = new StyleColor(new Color(0.5f, 0.5f, 0.5f, 0.22f));
        tile.style.borderBottomColor = new StyleColor(new Color(0.5f, 0.5f, 0.5f, 0.15f));
        tile.style.borderLeftColor = new StyleColor(new Color(0.5f, 0.5f, 0.5f, 0.15f));
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
        else
        {
            var fallbackLabel = new Label(string.IsNullOrWhiteSpace(slot.item.ItemName) ? "Item" : slot.item.ItemName);
            fallbackLabel.style.position = Position.Absolute;
            fallbackLabel.style.left = 6;
            fallbackLabel.style.right = 6;
            fallbackLabel.style.top = 6;
            fallbackLabel.style.fontSize = 10;
            fallbackLabel.style.color = new StyleColor(new Color(0.92f, 0.92f, 0.92f, 0.95f));
            fallbackLabel.style.whiteSpace = WhiteSpace.Normal;
            fallbackLabel.style.unityTextAlign = TextAnchor.UpperLeft;
            fallbackLabel.pickingMode = PickingMode.Ignore;
            tile.Add(fallbackLabel);
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

        tile.RegisterCallback<PointerDownEvent>(evt =>
        {
            if (evt.button != 0)
                return;

            BeginItemDragCandidate(slot.item, slot.quantity, anchorIndex, sourceInventory, evt.pointerId, evt.position);
            evt.StopPropagation();
        });

        return tile;
    }

    private void BeginItemDragCandidate(Item item, int quantity, int sourceSlotIndex, Inventory sourceInventory, int pointerId, Vector2 pointerPos)
    {
        if (item == null || quantity <= 0 || sourceInventory == null)
            return;

        isItemDragPending = true;
        isItemDragging = false;
        itemDragPointerId = pointerId;
        itemDragSourceSlotIndex = sourceSlotIndex;
        itemDragSourceInventory = sourceInventory;
        itemDragItem = item;
        itemDragQuantity = quantity;
        itemDragStartMouse = pointerPos;

        root?.CapturePointer(pointerId);
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

        bool handled = false;

        if (isItemDragging && itemDragSourceInventory != null)
        {
            int playerTargetSlot = GetSlotIndexAtWorldPosition(evt.position, playerCellsByIndex);
            int storageTargetSlot = GetSlotIndexAtWorldPosition(evt.position, storageCellsByIndex);

            if (playerTargetSlot >= 0)
                handled = TryHandleDrop(playerInventory, playerTargetSlot);
            else if (storageTargetSlot >= 0)
                handled = TryHandleDrop(activeStorageInventory, storageTargetSlot);
        }

        CancelItemDrag();

        if (handled)
            Refresh();
    }

    private bool TryHandleDrop(Inventory targetInventory, int targetSlot)
    {
        if (targetInventory == null || itemDragSourceInventory == null || targetSlot < 0)
            return false;

        if (targetInventory == itemDragSourceInventory)
            return itemDragSourceInventory.MoveItem(itemDragSourceSlotIndex, targetSlot);

        return StorageTransferUtility.TransferItemToSlot(itemDragSourceInventory, targetInventory, itemDragSourceSlotIndex, targetSlot);
    }

    private int GetSlotIndexAtWorldPosition(Vector2 worldPosition, Dictionary<int, VisualElement> cellMap)
    {
        if (cellMap == null || cellMap.Count == 0)
            return -1;

        foreach (var kvp in cellMap)
        {
            if (kvp.Value != null && kvp.Value.worldBound.Contains(worldPosition))
                return kvp.Key;
        }

        return -1;
    }

    private void StartItemDragGhost()
    {
        if (root == null || itemDragItem == null)
            return;

        itemDragGhost = new VisualElement();
        itemDragGhost.name = "storage-item-drag-ghost";
        itemDragGhost.style.position = Position.Absolute;
        itemDragGhost.style.width = 52;
        itemDragGhost.style.height = 52;
        itemDragGhost.style.backgroundColor = new StyleColor(new Color(0.12f, 0.12f, 0.12f, 0.9f));
        itemDragGhost.style.borderTopLeftRadius = 6;
        itemDragGhost.style.borderTopRightRadius = 6;
        itemDragGhost.style.borderBottomLeftRadius = 6;
        itemDragGhost.style.borderBottomRightRadius = 6;
        itemDragGhost.style.borderTopWidth = 1;
        itemDragGhost.style.borderRightWidth = 1;
        itemDragGhost.style.borderBottomWidth = 1;
        itemDragGhost.style.borderLeftWidth = 1;
        itemDragGhost.style.borderTopColor = new StyleColor(new Color(0.55f, 0.55f, 0.55f, 0.35f));
        itemDragGhost.style.borderRightColor = new StyleColor(new Color(0.55f, 0.55f, 0.55f, 0.35f));
        itemDragGhost.style.borderBottomColor = new StyleColor(new Color(0.55f, 0.55f, 0.55f, 0.2f));
        itemDragGhost.style.borderLeftColor = new StyleColor(new Color(0.55f, 0.55f, 0.55f, 0.2f));
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

        root.Add(itemDragGhost);
    }

    private void ResolvePlayerInventory()
    {
        if (playerInventory != null && playerInventory.gameObject.activeInHierarchy && playerInventory != activeStorageInventory)
            return;

        var player = FindAnyObjectByType<Player>();
        if (player != null)
        {
            playerInventory = player.GetComponent<Inventory>();
            if (playerInventory == null || playerInventory == activeStorageInventory)
                playerInventory = player.GetComponentInChildren<Inventory>();
        }

        if (playerInventory == null || playerInventory == activeStorageInventory)
        {
            foreach (var inv in FindObjectsByType<Inventory>(FindObjectsInactive.Exclude))
            {
                if (inv != null && inv != activeStorageInventory)
                {
                    playerInventory = inv;
                    break;
                }
            }
        }
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

        if (root != null && itemDragPointerId >= 0 && root.HasPointerCapture(itemDragPointerId))
            root.ReleasePointer(itemDragPointerId);

        itemDragPointerId = -1;
        itemDragSourceSlotIndex = -1;
        itemDragSourceInventory = null;
        itemDragItem = null;
        itemDragQuantity = 0;

        if (itemDragGhost != null)
        {
            itemDragGhost.RemoveFromHierarchy();
            itemDragGhost = null;
        }
    }
}
