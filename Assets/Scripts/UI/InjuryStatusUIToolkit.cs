using System.Collections;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.UIElements;

public class InjuryStatusUIToolkit : MonoBehaviour
{
    [SerializeField] private UIDocument uiDocument;
    [SerializeField] private Player player;

    [Header("Bandage Use")]
    [SerializeField] private float bandageUseDuration = 2f;
    [SerializeField] private string bandageItemKeyword = "bandage";

    private VisualElement root;
    private VisualElement injuryPanel;
    private VisualElement contextMenu;
    private Label detailsTitleLabel;
    private Label detailsContentLabel;

    private BodyPart? selectedBodyPart;
    private BodyPart contextMenuBodyPart;
    private bool isUIOpen;
    private bool isApplyingBandage;

    private readonly Dictionary<string, Button> bodyPartButtons = new Dictionary<string, Button>();
    private readonly Dictionary<BodyPart, Button> bodyPartToButton = new Dictionary<BodyPart, Button>();

    private InjurySystem injurySystem;
    private Inventory inventory;

    private void Awake()
    {
        if (uiDocument == null)
            uiDocument = GetComponent<UIDocument>();

        root = uiDocument.rootVisualElement;
        injuryPanel = root.Q<VisualElement>(className: "injury-panel");

        bodyPartButtons["head"] = root.Q<Button>("body-head");
        bodyPartButtons["torso"] = root.Q<Button>("body-torso");
        bodyPartButtons["leftarm"] = root.Q<Button>("body-leftarm");
        bodyPartButtons["rightarm"] = root.Q<Button>("body-rightarm");
        bodyPartButtons["leftleg"] = root.Q<Button>("body-leftleg");
        bodyPartButtons["rightleg"] = root.Q<Button>("body-rightleg");

        bodyPartToButton[BodyPart.Head] = bodyPartButtons["head"];
        bodyPartToButton[BodyPart.Torso] = bodyPartButtons["torso"];
        bodyPartToButton[BodyPart.LeftArm] = bodyPartButtons["leftarm"];
        bodyPartToButton[BodyPart.RightArm] = bodyPartButtons["rightarm"];
        bodyPartToButton[BodyPart.LeftLeg] = bodyPartButtons["leftleg"];
        bodyPartToButton[BodyPart.RightLeg] = bodyPartButtons["rightleg"];

        CreateDetailsArea();
        CreateContextMenu();
        RegisterBodyPartCallbacks();
        SetUIOpen(false);
    }

    private void OnEnable()
    {
        if (player == null)
            player = FindFirstObjectByType<Player>();

        if (player == null)
            return;

        injurySystem = player.GetInjurySystem();
        inventory = player.GetComponent<Inventory>();

        if (injurySystem != null)
        {
            injurySystem.onInjuryReceived.AddListener(OnInjuryChanged);
            injurySystem.onInjuryHealed.AddListener(OnInjuryChanged);
            injurySystem.onInjuryInfected.AddListener(OnInjuryChanged);
        }
    }

    private void OnDisable()
    {
        if (injurySystem != null)
        {
            injurySystem.onInjuryReceived.RemoveListener(OnInjuryChanged);
            injurySystem.onInjuryHealed.RemoveListener(OnInjuryChanged);
            injurySystem.onInjuryInfected.RemoveListener(OnInjuryChanged);
        }
    }

    private void Update()
    {
        if (player == null)
            player = FindFirstObjectByType<Player>();

        bool shouldBeOpen = InventoryUIToolkit.IsInventoryOpen;
        if (shouldBeOpen != isUIOpen)
            SetUIOpen(shouldBeOpen);

        if (!isUIOpen || injurySystem == null)
            return;

        UpdateBodyPartColors();
        if (selectedBodyPart.HasValue)
            UpdateDetailsForBodyPart(selectedBodyPart.Value);
    }

    private void SetUIOpen(bool open)
    {
        isUIOpen = open;

        if (injuryPanel != null)
            injuryPanel.style.display = open ? DisplayStyle.Flex : DisplayStyle.None;

        if (!open)
        {
            HideContextMenu();
            return;
        }

        if (injurySystem == null && player != null)
            injurySystem = player.GetInjurySystem();

        if (inventory == null && player != null)
            inventory = player.GetComponent<Inventory>();

        UpdateBodyPartColors();
        if (selectedBodyPart.HasValue)
            UpdateDetailsForBodyPart(selectedBodyPart.Value);
    }

    private void CreateDetailsArea()
    {
        if (injuryPanel == null)
            return;

        var divider = new VisualElement();
        divider.style.width = Length.Percent(100);
        divider.style.height = 1f;
        divider.style.backgroundColor = new Color(1f, 1f, 1f, 0.15f);
        divider.style.marginTop = 8f;
        divider.style.marginBottom = 6f;
        injuryPanel.Add(divider);

        detailsTitleLabel = new Label("SELECT A BODY PART");
        detailsTitleLabel.style.color = new Color(0.9f, 0.9f, 0.9f, 1f);
        detailsTitleLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
        detailsTitleLabel.style.fontSize = 13f;
        detailsTitleLabel.style.marginBottom = 4f;
        injuryPanel.Add(detailsTitleLabel);

        detailsContentLabel = new Label("Right-click a body part to open treatment menu.");
        detailsContentLabel.style.color = new Color(0.85f, 0.85f, 0.85f, 1f);
        detailsContentLabel.style.fontSize = 12f;
        detailsContentLabel.style.whiteSpace = WhiteSpace.Normal;
        detailsContentLabel.style.unityTextAlign = TextAnchor.UpperLeft;
        detailsContentLabel.style.marginBottom = 6f;
        injuryPanel.Add(detailsContentLabel);
    }

    private void CreateContextMenu()
    {
        if (root == null)
            return;

        contextMenu = new VisualElement();
        contextMenu.name = "injury-context-menu";
        contextMenu.style.position = Position.Absolute;
        contextMenu.style.minWidth = 150f;
        contextMenu.style.backgroundColor = new Color(0.13f, 0.13f, 0.13f, 0.98f);
        contextMenu.style.borderTopWidth = 1f;
        contextMenu.style.borderRightWidth = 1f;
        contextMenu.style.borderBottomWidth = 1f;
        contextMenu.style.borderLeftWidth = 1f;
        contextMenu.style.borderTopColor = new Color(0.6f, 0.2f, 0.2f, 0.9f);
        contextMenu.style.borderRightColor = new Color(0.6f, 0.2f, 0.2f, 0.9f);
        contextMenu.style.borderBottomColor = new Color(0.6f, 0.2f, 0.2f, 0.9f);
        contextMenu.style.borderLeftColor = new Color(0.6f, 0.2f, 0.2f, 0.9f);
        contextMenu.style.paddingTop = 4f;
        contextMenu.style.paddingBottom = 4f;
        contextMenu.style.paddingLeft = 4f;
        contextMenu.style.paddingRight = 4f;
        contextMenu.style.display = DisplayStyle.None;

        var bandageButton = new Button(() =>
        {
            TryBandageBodyPart(contextMenuBodyPart);
            HideContextMenu();
        })
        {
            text = "Bandage"
        };

        bandageButton.style.unityTextAlign = TextAnchor.MiddleLeft;
        bandageButton.style.fontSize = 12f;
        bandageButton.style.paddingLeft = 8f;
        bandageButton.style.paddingRight = 8f;
        bandageButton.style.height = 28f;

        contextMenu.Add(bandageButton);
        root.Add(contextMenu);

        root.RegisterCallback<MouseDownEvent>(_ =>
        {
            if (contextMenu.style.display == DisplayStyle.Flex)
                HideContextMenu();
        });
    }

    private void RegisterBodyPartCallbacks()
    {
        RegisterBodyPartCallbacksForButton("head", BodyPart.Head);
        RegisterBodyPartCallbacksForButton("torso", BodyPart.Torso);
        RegisterBodyPartCallbacksForButton("leftarm", BodyPart.LeftArm);
        RegisterBodyPartCallbacksForButton("rightarm", BodyPart.RightArm);
        RegisterBodyPartCallbacksForButton("leftleg", BodyPart.LeftLeg);
        RegisterBodyPartCallbacksForButton("rightleg", BodyPart.RightLeg);
    }

    private void RegisterBodyPartCallbacksForButton(string key, BodyPart bodyPart)
    {
        if (!bodyPartButtons.TryGetValue(key, out var button) || button == null)
            return;

        button.clicked += () =>
        {
            selectedBodyPart = bodyPart;
            UpdateDetailsForBodyPart(bodyPart);
            HideContextMenu();
        };

        button.RegisterCallback<MouseEnterEvent>(_ =>
        {
            selectedBodyPart = bodyPart;
            UpdateDetailsForBodyPart(bodyPart);
        });

        button.RegisterCallback<ContextClickEvent>(evt =>
        {
            OnBodyPartRightClicked(bodyPart, evt);
            evt.StopPropagation();
        });
    }

    private void OnInjuryChanged(Injury injury)
    {
        if (!isUIOpen)
            return;

        UpdateBodyPartColors();
        if (selectedBodyPart.HasValue)
            UpdateDetailsForBodyPart(selectedBodyPart.Value);
    }

    private void UpdateBodyPartColors()
    {
        if (injurySystem == null)
            return;

        foreach (var bodyPart in bodyPartToButton.Keys)
            UpdateBodyPartButton(bodyPart);
    }

    private void UpdateBodyPartButton(BodyPart bodyPart)
    {
        if (!bodyPartToButton.TryGetValue(bodyPart, out var button))
            return;

        var injuries = injurySystem.GetInjuriesOnBodyPart(bodyPart);

        if (injuries.Count == 0)
        {
            button.style.backgroundColor = new Color(0f, 0f, 0f, 0f);
        }
        else if (AreAllInjuriesBandaged(injuries))
        {
            button.style.backgroundColor = new Color(0.20f, 0.78f, 0.35f, 0.92f);
        }
        else
        {
            Injury worstInjury = injuries[0];
            float totalSeverity = 0f;

            foreach (var injury in injuries)
            {
                float severity = GetInjurySeverity(injury);
                totalSeverity += severity;
                if (severity > GetInjurySeverity(worstInjury))
                    worstInjury = injury;
            }

            button.style.backgroundColor = GetInjuryDisplayColor(worstInjury, totalSeverity);
        }

        button.tooltip = BuildBodyPartSummary(bodyPart, injuries);
    }

    private static bool AreAllInjuriesBandaged(List<Injury> injuries)
    {
        if (injuries == null || injuries.Count == 0)
            return false;

        foreach (var injury in injuries)
        {
            if (!injury.isBandaged)
                return false;
        }

        return true;
    }

    private string BuildBodyPartSummary(BodyPart bodyPart, List<Injury> injuries)
    {
        if (injuries == null || injuries.Count == 0)
            return $"{GetBodyPartDisplayName(bodyPart)}: No injuries";

        StringBuilder summary = new StringBuilder();
        summary.AppendLine(GetBodyPartDisplayName(bodyPart));

        for (int i = 0; i < injuries.Count; i++)
        {
            Injury injury = injuries[i];
            summary.Append("• ");
            summary.Append(injury.injuryType);
            summary.Append(" - ");
            summary.Append(injury.isBandaged ? "Bandaged" : "Bleeding");

            if (injury.isBandaged && injury.isHealing)
            {
                summary.Append(" | Healing ");
                summary.Append(Mathf.RoundToInt(injury.GetHealingProgress01() * 100f));
                summary.Append("%");
            }

            if (injury.isInfected)
            {
                summary.Append(" | Infected ");
                summary.Append(Mathf.RoundToInt(injury.infectionProgress * 100f));
                summary.Append("%");
            }

            if (i < injuries.Count - 1)
                summary.AppendLine();
        }

        return summary.ToString();
    }

    private void UpdateDetailsForBodyPart(BodyPart bodyPart)
    {
        if (detailsTitleLabel == null || detailsContentLabel == null)
            return;

        detailsTitleLabel.text = GetBodyPartDisplayName(bodyPart).ToUpperInvariant();

        if (injurySystem == null)
        {
            detailsContentLabel.text = "Injury system not available.";
            return;
        }

        List<Injury> injuries = injurySystem.GetInjuriesOnBodyPart(bodyPart);
        if (injuries.Count == 0)
        {
            detailsContentLabel.text = "No injuries detected.";
            return;
        }

        StringBuilder details = new StringBuilder();
        for (int i = 0; i < injuries.Count; i++)
        {
            Injury injury = injuries[i];
            details.Append("• ");
            details.Append(injury.injuryType);
            details.Append(" | ");
            details.Append(injury.isBandaged ? "Bandaged" : "Bleeding");

            if (injury.isBandaged && injury.isHealing)
            {
                details.Append(" | Healing ");
                details.Append(Mathf.RoundToInt(injury.GetHealingProgress01() * 100f));
                details.Append("%");
            }

            if (injury.isInfected)
            {
                details.Append(" | Infected ");
                details.Append(Mathf.RoundToInt(injury.infectionProgress * 100f));
                details.Append("%");
            }

            if (i < injuries.Count - 1)
                details.AppendLine();
        }

        detailsContentLabel.text = details.ToString();
    }

    private static string GetBodyPartDisplayName(BodyPart bodyPart)
    {
        switch (bodyPart)
        {
            case BodyPart.LeftArm: return "Left Arm";
            case BodyPart.RightArm: return "Right Arm";
            case BodyPart.LeftLeg: return "Left Leg";
            case BodyPart.RightLeg: return "Right Leg";
            default: return bodyPart.ToString();
        }
    }

    private static float GetInjurySeverity(Injury injury)
    {
        float severity = 0f;

        switch (injury.injuryType)
        {
            case InjuryType.Scratch: severity = 1f; break;
            case InjuryType.Laceration: severity = 2.5f; break;
            case InjuryType.Bitten: severity = 3f; break;
        }

        if (injury.isInfected)
            severity *= 1.5f;

        return severity;
    }

    private static Color GetInjuryDisplayColor(Injury injury, float totalSeverity)
    {
        if (injury.isInfected)
            return new Color(0.8f, 0.7f, 0.2f, 0.9f);

        if (totalSeverity >= 4f)
            return new Color(0.8f, 0.1f, 0.1f, 0.9f);

        if (totalSeverity >= 2.5f)
            return new Color(0.9f, 0.2f, 0.2f, 0.9f);

        if (totalSeverity >= 1.5f)
            return new Color(0.9f, 0.4f, 0.1f, 0.9f);

        return new Color(0.9f, 0.6f, 0.2f, 0.9f);
    }

    private void OnBodyPartRightClicked(BodyPart bodyPart, ContextClickEvent evt)
    {
        if (injurySystem == null || contextMenu == null || root == null)
            return;

        selectedBodyPart = bodyPart;
        UpdateDetailsForBodyPart(bodyPart);

        contextMenuBodyPart = bodyPart;
        Vector2 mousePos = evt.mousePosition;

        float menuWidth = 130f;
        float menuHeight = 34f;
        float clampedX = Mathf.Clamp(mousePos.x, 0f, Mathf.Max(0f, root.layout.width - menuWidth));
        float clampedY = Mathf.Clamp(mousePos.y, 0f, Mathf.Max(0f, root.layout.height - menuHeight));

        contextMenu.style.left = clampedX;
        contextMenu.style.top = clampedY;
        contextMenu.style.display = DisplayStyle.Flex;
    }

    private void HideContextMenu()
    {
        if (contextMenu != null)
            contextMenu.style.display = DisplayStyle.None;
    }

    private void TryBandageBodyPart(BodyPart bodyPart)
    {
        if (injurySystem == null || isApplyingBandage)
            return;

        List<Injury> injuries = injurySystem.GetInjuriesOnBodyPart(bodyPart);
        if (injuries == null || injuries.Count == 0)
        {
            ShowStatusMessage(bodyPart, "No injuries to bandage.");
            return;
        }

        bool hasUnbandaged = false;
        foreach (var injury in injuries)
        {
            if (!injury.isBandaged)
            {
                hasUnbandaged = true;
                break;
            }
        }

        if (!hasUnbandaged)
        {
            ShowStatusMessage(bodyPart, "Already bandaged.");
            return;
        }

        if (!TryFindBandageItem(out Item bandageItem))
        {
            ShowStatusMessage(bodyPart, "No bandage in inventory.");
            return;
        }

        StartCoroutine(ApplyBandageRoutine(bodyPart, bandageItem));
    }

    private IEnumerator ApplyBandageRoutine(BodyPart bodyPart, Item bandageItem)
    {
        isApplyingBandage = true;

        float duration = Mathf.Max(0.1f, bandageUseDuration);
        float elapsed = 0f;

        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            float remaining = Mathf.Max(0f, duration - elapsed);
            ShowStatusMessage(bodyPart, $"Applying bandage... {remaining:0.0}s");
            yield return null;
        }

        bool removed = inventory != null && inventory.RemoveItem(bandageItem, 1);
        if (!removed)
        {
            ShowStatusMessage(bodyPart, "Bandage missing.");
            isApplyingBandage = false;
            yield break;
        }

        injurySystem.BandageBodyPart(bodyPart);
        UpdateBodyPartColors();
        UpdateDetailsForBodyPart(bodyPart);
        ShowStatusMessage(bodyPart, "Bandaged. Healing started (1-2 days).");

        isApplyingBandage = false;
    }

    private bool TryFindBandageItem(out Item bandageItem)
    {
        bandageItem = null;

        if (inventory == null)
        {
            if (player != null)
                inventory = player.GetComponent<Inventory>();
            if (inventory == null)
                return false;
        }

        var slots = inventory.GetAllItems();
        if (slots == null || slots.Count == 0)
            return false;

        string keyword = string.IsNullOrWhiteSpace(bandageItemKeyword)
            ? "bandage"
            : bandageItemKeyword.ToLowerInvariant();

        foreach (var slot in slots)
        {
            if (slot == null || slot.item == null || slot.quantity <= 0)
                continue;

            string itemName = slot.item.ItemName;
            if (string.IsNullOrWhiteSpace(itemName))
                continue;

            if (itemName.ToLowerInvariant().Contains(keyword))
            {
                bandageItem = slot.item;
                return true;
            }
        }

        return false;
    }

    private void ShowStatusMessage(BodyPart bodyPart, string message)
    {
        if (detailsTitleLabel == null || detailsContentLabel == null)
            return;

        detailsTitleLabel.text = GetBodyPartDisplayName(bodyPart).ToUpperInvariant();
        detailsContentLabel.text = message;
    }
}
