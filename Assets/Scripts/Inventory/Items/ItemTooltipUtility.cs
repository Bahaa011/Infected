using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

public static class ItemTooltipUtility
{
    private sealed class TooltipState
    {
        public VisualElement Root;
        public VisualElement Panel;
        public Label TitleLabel;
        public Label BodyLabel;
    }

    private static readonly Dictionary<VisualElement, TooltipState> States = new Dictionary<VisualElement, TooltipState>();

    public static string BuildTooltip(Item item, int quantity = 1)
    {
        return string.Join("\n", BuildTooltipLines(item, quantity));
    }

    public static string BuildTooltipTitle(Item item)
    {
        if (item == null)
            return string.Empty;

        return string.IsNullOrWhiteSpace(item.ItemName) ? "Item" : item.ItemName;
    }

    public static string BuildTooltipBody(Item item, int quantity = 1)
    {
        if (item == null)
            return string.Empty;

        var lines = BuildTooltipLines(item, quantity);
        if (lines.Count <= 1)
            return string.Empty;

        return string.Join("\n", lines.GetRange(1, lines.Count - 1));
    }

    public static void EnsureTooltipPanel(VisualElement root)
    {
        if (root == null || States.ContainsKey(root))
            return;

        var panel = new VisualElement();
        panel.name = "item-tooltip-panel";
        panel.pickingMode = PickingMode.Ignore;
        panel.style.position = Position.Absolute;
        panel.style.display = DisplayStyle.None;
        panel.style.width = 260;
        panel.style.maxWidth = 300;
        panel.style.minWidth = 190;
        panel.style.paddingTop = 7;
        panel.style.paddingRight = 9;
        panel.style.paddingBottom = 7;
        panel.style.paddingLeft = 9;
        panel.style.backgroundColor = new StyleColor(new Color(0.08f, 0.08f, 0.09f, 0.98f));
        panel.style.borderTopWidth = 1;
        panel.style.borderRightWidth = 1;
        panel.style.borderBottomWidth = 1;
        panel.style.borderLeftWidth = 1;
        panel.style.borderTopColor = new StyleColor(new Color(0.6f, 0.6f, 0.6f, 0.24f));
        panel.style.borderRightColor = new StyleColor(new Color(0.6f, 0.6f, 0.6f, 0.24f));
        panel.style.borderBottomColor = new StyleColor(new Color(0.6f, 0.6f, 0.6f, 0.14f));
        panel.style.borderLeftColor = new StyleColor(new Color(0.6f, 0.6f, 0.6f, 0.14f));
        panel.style.borderTopLeftRadius = 6;
        panel.style.borderTopRightRadius = 6;
        panel.style.borderBottomLeftRadius = 6;
        panel.style.borderBottomRightRadius = 6;
        panel.style.opacity = 1f;

        var title = new Label();
        title.name = "item-tooltip-title";
        title.style.fontSize = 12;
        title.style.unityFontStyleAndWeight = FontStyle.Bold;
        title.style.color = new StyleColor(new Color(0.95f, 0.95f, 0.95f, 1f));
        title.style.marginBottom = 3;
        title.style.whiteSpace = WhiteSpace.Normal;

        var body = new Label();
        body.name = "item-tooltip-body";
        body.style.fontSize = 10;
        body.style.color = new StyleColor(new Color(0.82f, 0.82f, 0.84f, 0.98f));
        body.style.whiteSpace = WhiteSpace.Normal;
        body.style.unityTextAlign = TextAnchor.UpperLeft;

        panel.Add(title);
        panel.Add(body);
        root.Add(panel);
        panel.BringToFront();

        States[root] = new TooltipState
        {
            Root = root,
            Panel = panel,
            TitleLabel = title,
            BodyLabel = body
        };
    }

    public static void ShowTooltip(VisualElement root, Item item, int quantity, Vector2 anchorPoint)
    {
        if (root == null || item == null)
            return;

        EnsureTooltipPanel(root);
        if (!States.TryGetValue(root, out TooltipState state) || state.Panel == null)
            return;

        state.TitleLabel.text = BuildTooltipTitle(item);
        state.BodyLabel.text = BuildTooltipBody(item, quantity);
        state.Panel.style.display = DisplayStyle.Flex;

        PositionTooltip(state, anchorPoint);
    }

    public static void MoveTooltip(VisualElement root, Vector2 anchorPoint)
    {
        if (root == null)
            return;

        if (!States.TryGetValue(root, out TooltipState state) || state.Panel == null || state.Panel.resolvedStyle.display == DisplayStyle.None)
            return;

        PositionTooltip(state, anchorPoint);
    }

    public static void HideTooltip(VisualElement root)
    {
        if (root == null)
            return;

        if (!States.TryGetValue(root, out TooltipState state) || state.Panel == null)
            return;

        state.Panel.style.display = DisplayStyle.None;
    }

    private static List<string> BuildTooltipLines(Item item, int quantity)
    {
        var lines = new List<string>(8);
        if (item == null)
            return lines;

        int safeQuantity = Mathf.Max(1, quantity);

        string itemName = BuildTooltipTitle(item);
        lines.Add(itemName);
        lines.Add($"Type: {GetItemTypeLabel(item)}");

        if (!string.IsNullOrWhiteSpace(item.Description))
            lines.Add($"Info: {item.Description}");

        switch (item)
        {
            case GunItem gunItem:
                lines.Add($"Gun Type: {gunItem.GunType}");
                lines.Add($"Ammo: {gunItem.CurrentAmmo}/{gunItem.AmmoCapacity}");
                break;
            case MeleeWeaponItem meleeItem:
                lines.Add($"Weapon Type: {meleeItem.WeaponType}");
                lines.Add($"Damage: {meleeItem.BaseDamage:0.#}");
                lines.Add($"Attack Speed: {meleeItem.AttackSpeed:0.##}/s");
                lines.Add($"Range: {meleeItem.AttackRange:0.##}");
                break;
            case MagazineItem magazineItem:
                lines.Add($"Compatible Gun: {magazineItem.GunType}");
                lines.Add($"Ammo Capacity: {magazineItem.AmmoCapacity}");
                break;
            case FoodItem foodItem:
                lines.Add($"Hunger Restore: {foodItem.HungerRestore:0.#}");
                if (!Mathf.Approximately(foodItem.ThirstRestore, 0f))
                    lines.Add($"Thirst Restore: {foodItem.ThirstRestore:0.#}");
                break;
            case WaterItem waterItem:
                lines.Add($"Thirst Restore: {waterItem.ThirstRestore:0.#}");
                if (!Mathf.Approximately(waterItem.HungerRestore, 0f))
                    lines.Add($"Hunger Restore: {waterItem.HungerRestore:0.#}");
                break;
            case BandageItem bandageItem:
                lines.Add($"Heal Modifier: {bandageItem.HealModifier:0.##}x");
                break;
        }

        return lines;
    }

    private static void PositionTooltip(TooltipState state, Vector2 anchorPoint)
    {
        if (state == null || state.Panel == null || state.Root == null)
            return;

        float rootWidth = Mathf.Max(0f, state.Root.resolvedStyle.width);
        float rootHeight = Mathf.Max(0f, state.Root.resolvedStyle.height);

        float panelWidth = Mathf.Max(190f, state.Panel.resolvedStyle.width > 0f ? state.Panel.resolvedStyle.width : 260f);
        float panelHeight = Mathf.Max(68f, state.Panel.resolvedStyle.height > 0f ? state.Panel.resolvedStyle.height : 90f);

        float left = anchorPoint.x + 14f;
        float top = anchorPoint.y + 18f;

        if (rootWidth > 0f && left + panelWidth + 12f > rootWidth)
            left = Mathf.Max(8f, anchorPoint.x - panelWidth - 14f);

        if (rootHeight > 0f && top + panelHeight + 12f > rootHeight)
            top = Mathf.Max(8f, rootHeight - panelHeight - 8f);

        state.Panel.style.left = left;
        state.Panel.style.top = top;
    }

    private static string GetItemTypeLabel(Item item)
    {
        if (item is GunItem)
            return "Gun";
        if (item is MeleeWeaponItem)
            return "Melee Weapon";
        if (item is MagazineItem)
            return "Magazine";
        if (item is FoodItem)
            return "Food";
        if (item is WaterItem)
            return "Water";
        if (item is BandageItem)
            return "Medical";

        string typeName = item.GetType().Name;
        if (typeName.EndsWith("Item"))
            typeName = typeName.Substring(0, typeName.Length - 4);

        return string.IsNullOrWhiteSpace(typeName) ? "Item" : typeName;
    }
}