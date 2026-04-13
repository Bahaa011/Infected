using UnityEngine;
using UnityEngine.UIElements;

public class VitalsUIToolkit : MonoBehaviour
{
    [SerializeField] private UIDocument uiDocument;
    [SerializeField] private Player player;
    [SerializeField] private Sprite healthIcon;
    [SerializeField] private Sprite hungerIcon;
    [SerializeField] private Sprite thirstIcon;
    [SerializeField] private Sprite staminaIcon;

    private RadialProgressElement healthWheel;
    private RadialProgressElement hungerWheel;
    private RadialProgressElement thirstWheel;
    private RadialProgressElement staminaWheel;

    private void Awake()
    {
        if (uiDocument == null)
            uiDocument = GetComponent<UIDocument>();

        var root = uiDocument.rootVisualElement;

        var panel = root.Q<VisualElement>(className: "vitals-panel");
        if (panel == null)
            panel = root;

        BuildCircularVitals(panel);
    }

    private void OnEnable()
    {
        BindPlayer(ResolvePlayer());
        RefreshAllVitals();
    }

    private void OnDisable()
    {
        BindPlayer(null);
    }

    private void Update()
    {
        BindPlayer(ResolvePlayer());
        RefreshAllVitals();
    }

    private void OnHealthChanged(float current, float max)
    {
        float progress = max <= 0f ? 0f : Mathf.Clamp01(current / max);
        if (healthWheel != null)
            healthWheel.SetProgress(progress);
    }

    private void OnHungerChanged(float current, float max)
    {
        float progress = max <= 0f ? 0f : Mathf.Clamp01(current / max);
        if (hungerWheel != null)
            hungerWheel.SetProgress(progress);
    }

    private void OnThirstChanged(float current, float max)
    {
        float progress = max <= 0f ? 0f : Mathf.Clamp01(current / max);
        if (thirstWheel != null)
            thirstWheel.SetProgress(progress);
    }

    private void OnStaminaChanged(float current, float max)
    {
        float progress = max <= 0f ? 0f : Mathf.Clamp01(current / max);
        if (staminaWheel != null)
            staminaWheel.SetProgress(progress);
    }

    private Player ResolvePlayer()
    {
        if (player == null || !player.gameObject.activeInHierarchy)
            player = FindAnyObjectByType<Player>();

        return player;
    }

    private void BindPlayer(Player newPlayer)
    {
        if (player == newPlayer)
            return;

        if (player != null)
        {
            player.onHealthChanged.RemoveListener(OnHealthChanged);
            player.onHungerChanged.RemoveListener(OnHungerChanged);
            player.onThirstChanged.RemoveListener(OnThirstChanged);
            player.onStaminaChanged.RemoveListener(OnStaminaChanged);
        }

        player = newPlayer;

        if (player != null && isActiveAndEnabled)
        {
            player.onHealthChanged.AddListener(OnHealthChanged);
            player.onHungerChanged.AddListener(OnHungerChanged);
            player.onThirstChanged.AddListener(OnThirstChanged);
            player.onStaminaChanged.AddListener(OnStaminaChanged);
        }
    }

    private void RefreshAllVitals()
    {
        if (player == null)
            return;

        OnHealthChanged(player.GetHealth(), player.GetMaxHealth());
        OnHungerChanged(player.GetHunger(), player.GetMaxHunger());
        OnThirstChanged(player.GetThirst(), player.GetMaxThirst());
        OnStaminaChanged(player.GetStamina(), player.GetMaxStamina());
    }

    private void BuildCircularVitals(VisualElement panel)
    {
        panel.Clear();

        // Keep vitals floating, no background card.
        panel.style.backgroundColor = new StyleColor(new Color(0f, 0f, 0f, 0f));
        panel.style.borderTopWidth = 0;
        panel.style.borderRightWidth = 0;
        panel.style.borderBottomWidth = 0;
        panel.style.borderLeftWidth = 0;
        panel.style.paddingTop = 0;
        panel.style.paddingRight = 0;
        panel.style.paddingBottom = 0;
        panel.style.paddingLeft = 0;
        panel.style.width = 429;
        panel.style.height = 303;

        var cluster = new VisualElement();
        cluster.style.position = Position.Relative;
        cluster.style.width = 429;
        cluster.style.height = 303;
        panel.Add(cluster);

        var healthNode = CreateVitalCircle("HP", true, healthIcon, new Color(0.88f, 0.33f, 0.42f, 1f), 168f, 12f, 12, 13);
        var hungerNode = CreateVitalCircle("HU", true, hungerIcon, new Color(0.67f, 0.89f, 0.38f, 1f), 87f, 7.5f, 9, 10);
        var thirstNode = CreateVitalCircle("TH", true, thirstIcon, new Color(0.42f, 0.73f, 1f, 1f), 87f, 7.5f, 9, 10);
        var staminaNode = CreateVitalCircle("ST", true, staminaIcon, new Color(0.73f, 0.5f, 1f, 1f), 87f, 7.5f, 9, 10);

        healthWheel = healthNode.wheel;
        hungerWheel = hungerNode.wheel;
        thirstWheel = thirstNode.wheel;
        staminaWheel = staminaNode.wheel;

        // Subnautica-like shape: large top-right with three smaller around lower-left arc.
        PlaceNode(cluster, healthNode.container, 156, 18);
        PlaceNode(cluster, hungerNode.container, 54, 54);
        PlaceNode(cluster, thirstNode.container, 37.5f, 168);
        PlaceNode(cluster, staminaNode.container, 147, 193.5f);
    }

    private void PlaceNode(VisualElement parent, VisualElement node, float left, float top)
    {
        node.style.position = Position.Absolute;
        node.style.left = left;
        node.style.top = top;
        parent.Add(node);
    }

    private (VisualElement container, RadialProgressElement wheel) CreateVitalCircle(
        string shortName,
        bool showIcon,
        Sprite icon,
        Color accent,
        float size,
        float thickness,
        int shortNameFont,
        int valueFont)
    {
        var container = new VisualElement();
        container.style.width = size;
        container.style.height = size;
        container.style.flexDirection = FlexDirection.Column;
        container.style.alignItems = Align.Center;
        container.style.justifyContent = Justify.FlexStart;

        var circleWrap = new VisualElement();
        circleWrap.style.width = size;
        circleWrap.style.height = size;
        circleWrap.style.position = Position.Relative;
        circleWrap.style.backgroundColor = new StyleColor(new Color(0.05f, 0.05f, 0.05f, 0.42f));
        float radius = size * 0.5f;
        circleWrap.style.borderTopLeftRadius = radius;
        circleWrap.style.borderTopRightRadius = radius;
        circleWrap.style.borderBottomLeftRadius = radius;
        circleWrap.style.borderBottomRightRadius = radius;
        circleWrap.style.borderTopWidth = 1;
        circleWrap.style.borderRightWidth = 1;
        circleWrap.style.borderBottomWidth = 1;
        circleWrap.style.borderLeftWidth = 1;
        circleWrap.style.borderTopColor = new StyleColor(new Color(0.62f, 0.62f, 0.62f, 0.26f));
        circleWrap.style.borderRightColor = new StyleColor(new Color(0.62f, 0.62f, 0.62f, 0.26f));
        circleWrap.style.borderBottomColor = new StyleColor(new Color(0.62f, 0.62f, 0.62f, 0.14f));
        circleWrap.style.borderLeftColor = new StyleColor(new Color(0.62f, 0.62f, 0.62f, 0.14f));

        var glowRing = new VisualElement();
        glowRing.style.position = Position.Absolute;
        glowRing.style.left = -3;
        glowRing.style.top = -3;
        glowRing.style.width = size + 6;
        glowRing.style.height = size + 6;
        float glowRadius = (size + 6f) * 0.5f;
        glowRing.style.borderTopLeftRadius = glowRadius;
        glowRing.style.borderTopRightRadius = glowRadius;
        glowRing.style.borderBottomLeftRadius = glowRadius;
        glowRing.style.borderBottomRightRadius = glowRadius;
        glowRing.style.borderTopWidth = 1;
        glowRing.style.borderRightWidth = 1;
        glowRing.style.borderBottomWidth = 1;
        glowRing.style.borderLeftWidth = 1;
        glowRing.style.borderTopColor = new StyleColor(new Color(accent.r, accent.g, accent.b, 0.34f));
        glowRing.style.borderRightColor = new StyleColor(new Color(accent.r, accent.g, accent.b, 0.34f));
        glowRing.style.borderBottomColor = new StyleColor(new Color(accent.r, accent.g, accent.b, 0.18f));
        glowRing.style.borderLeftColor = new StyleColor(new Color(accent.r, accent.g, accent.b, 0.18f));

        var wheel = new RadialProgressElement();
        wheel.style.position = Position.Absolute;
        float wheelInset = size * 0.13f;
        float wheelSize = size - wheelInset * 2f;
        wheel.style.left = wheelInset;
        wheel.style.top = wheelInset;
        wheel.style.width = wheelSize;
        wheel.style.height = wheelSize;
        wheel.SetThickness(thickness);
        wheel.SetTrackColor(new Color(0.35f, 0.35f, 0.35f, 0.24f));
        wheel.SetFillColor(accent);
        wheel.SetProgress(1f);

        var innerCore = new VisualElement();
        innerCore.style.position = Position.Absolute;
        float coreInset = size * 0.31f;
        float coreSize = size - coreInset * 2f;
        innerCore.style.left = coreInset;
        innerCore.style.top = coreInset;
        innerCore.style.width = coreSize;
        innerCore.style.height = coreSize;
        float coreRadius = coreSize * 0.5f;
        innerCore.style.borderTopLeftRadius = coreRadius;
        innerCore.style.borderTopRightRadius = coreRadius;
        innerCore.style.borderBottomLeftRadius = coreRadius;
        innerCore.style.borderBottomRightRadius = coreRadius;
        innerCore.style.backgroundColor = new StyleColor(new Color(0.08f, 0.08f, 0.08f, 0.82f));
        innerCore.style.borderTopWidth = 1;
        innerCore.style.borderRightWidth = 1;
        innerCore.style.borderBottomWidth = 1;
        innerCore.style.borderLeftWidth = 1;
        innerCore.style.borderTopColor = new StyleColor(new Color(0.58f, 0.58f, 0.58f, 0.22f));
        innerCore.style.borderRightColor = new StyleColor(new Color(0.58f, 0.58f, 0.58f, 0.22f));
        innerCore.style.borderBottomColor = new StyleColor(new Color(0.58f, 0.58f, 0.58f, 0.12f));
        innerCore.style.borderLeftColor = new StyleColor(new Color(0.58f, 0.58f, 0.58f, 0.12f));

        var marker = new VisualElement();
        marker.style.position = Position.Absolute;
        marker.style.left = size * 0.47f;
        marker.style.top = size * 0.06f;
        float markerSize = Mathf.Max(4f, size * 0.06f);
        marker.style.width = markerSize;
        marker.style.height = markerSize;
        float markerRadius = markerSize * 0.5f;
        marker.style.borderTopLeftRadius = markerRadius;
        marker.style.borderTopRightRadius = markerRadius;
        marker.style.borderBottomLeftRadius = markerRadius;
        marker.style.borderBottomRightRadius = markerRadius;
        marker.style.backgroundColor = new StyleColor(accent);

        circleWrap.Add(glowRing);
        circleWrap.Add(wheel);
        circleWrap.Add(innerCore);
        circleWrap.Add(marker);

        if (showIcon && icon != null)
        {
            float iconSize = coreSize * 0.65f;
            var iconElement = new Image();
            iconElement.sprite = icon;
            iconElement.style.position = Position.Absolute;
            iconElement.style.left = (size - iconSize) * 0.5f;
            iconElement.style.top = (size - iconSize) * 0.5f;
            iconElement.style.width = iconSize;
            iconElement.style.height = iconSize;
            iconElement.pickingMode = PickingMode.Ignore;
            circleWrap.Add(iconElement);
        }

        container.Add(circleWrap);

        return (container, wheel);
    }
}
