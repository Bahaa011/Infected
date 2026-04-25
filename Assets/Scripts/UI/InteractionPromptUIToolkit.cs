using UnityEngine;
using UnityEngine.UIElements;

[DisallowMultipleComponent]
public class InteractionPromptUIToolkit : MonoBehaviour
{
    [SerializeField] private UIDocument uiDocument;
    [SerializeField] private Player player;
    [SerializeField] private string playerTag = "Player";
    [SerializeField] private float maxRayDistance = 4.5f;
    [SerializeField] private LayerMask interactionLayers = ~0;
    [SerializeField] private Vector2 normalizedAnchor = new Vector2(0.5f, 0.58f);
    [SerializeField] private Vector2 anchorOffset = new Vector2(0f, 0f);

    private VisualElement root;
    private VisualElement panel;
    private Label label;
    private Camera cachedCamera;
    private string currentPrompt;

    private void Awake()
    {
        if (uiDocument == null)
            uiDocument = GetComponent<UIDocument>();

        if (uiDocument == null)
        {
            Debug.LogWarning("[InteractionPromptUIToolkit] No UIDocument found.");
            enabled = false;
            return;
        }

        root = uiDocument.rootVisualElement;
        BuildUi();
        HidePrompt();
    }

    private void OnEnable()
    {
        ResolveReferences();
    }

    private void Update()
    {
        ResolveReferences();

        if (root == null)
            return;

        if (InventoryUIToolkit.IsInventoryOpen)
        {
            HidePrompt();
            return;
        }

        Camera cam = GetViewCamera();
        if (cam == null || player == null)
        {
            HidePrompt();
            return;
        }

        if (!TryGetHoveredPrompt(cam, out string prompt))
        {
            HidePrompt();
            return;
        }

        ShowPrompt(prompt);
    }

    private void ResolveReferences()
    {
        if (player == null)
            player = FindAnyObjectByType<Player>();

        if (cachedCamera == null)
        {
            ThirdPersonController controller = player != null ? player.GetComponent<ThirdPersonController>() : null;
            if (controller != null && controller.MainUnityCamera != null)
                cachedCamera = controller.MainUnityCamera;
            else if (Camera.main != null)
                cachedCamera = Camera.main;
        }
    }

    private Camera GetViewCamera()
    {
        if (cachedCamera != null)
            return cachedCamera;

        ResolveReferences();
        return cachedCamera;
    }

    private void BuildUi()
    {
        if (root == null)
            return;

        panel = root.Q<VisualElement>("interaction-prompt-panel");
        label = root.Q<Label>("interaction-prompt-label");

        if (panel == null)
        {
            panel = new VisualElement();
            panel.name = "interaction-prompt-panel";
            panel.pickingMode = PickingMode.Ignore;
            panel.style.position = Position.Absolute;
            panel.style.display = DisplayStyle.None;
            panel.style.paddingTop = 6;
            panel.style.paddingRight = 10;
            panel.style.paddingBottom = 6;
            panel.style.paddingLeft = 10;
            panel.style.backgroundColor = new StyleColor(new Color(0.08f, 0.08f, 0.08f, 0.94f));
            panel.style.borderTopWidth = 1;
            panel.style.borderRightWidth = 1;
            panel.style.borderBottomWidth = 1;
            panel.style.borderLeftWidth = 1;
            panel.style.borderTopColor = new StyleColor(new Color(0.65f, 0.65f, 0.65f, 0.20f));
            panel.style.borderRightColor = new StyleColor(new Color(0.65f, 0.65f, 0.65f, 0.20f));
            panel.style.borderBottomColor = new StyleColor(new Color(0.65f, 0.65f, 0.65f, 0.14f));
            panel.style.borderLeftColor = new StyleColor(new Color(0.65f, 0.65f, 0.65f, 0.14f));
            panel.style.borderTopLeftRadius = 6;
            panel.style.borderTopRightRadius = 6;
            panel.style.borderBottomLeftRadius = 6;
            panel.style.borderBottomRightRadius = 6;

            label = new Label();
            label.name = "interaction-prompt-label";
            label.style.fontSize = 11;
            label.style.unityFontStyleAndWeight = FontStyle.Bold;
            label.style.color = new StyleColor(new Color(0.96f, 0.96f, 0.96f, 1f));
            label.style.whiteSpace = WhiteSpace.Normal;
            label.style.unityTextAlign = TextAnchor.MiddleCenter;

            panel.Add(label);
            root.Add(panel);
            panel.BringToFront();
        }

        root.style.flexGrow = 1;
    }

    private bool TryGetHoveredPrompt(Camera cam, out string prompt)
    {
        prompt = string.Empty;

        Ray ray = new Ray(cam.transform.position, cam.transform.forward);
        if (!Physics.Raycast(ray, out RaycastHit hit, maxRayDistance, interactionLayers, QueryTriggerInteraction.Collide))
            return false;

        if (player != null && hit.collider != null && hit.collider.transform.IsChildOf(player.transform))
            return false;

        var source = hit.collider != null
            ? hit.collider.GetComponentInParent<IInteractionPromptSource>()
            : null;

        if (source == null || !source.TryGetInteractionPrompt(player != null ? player.transform : cam.transform, out prompt))
            return false;

        return !string.IsNullOrWhiteSpace(prompt);
    }

    private void ShowPrompt(string text)
    {
        if (panel == null || label == null)
            return;

        if (currentPrompt != text)
        {
            currentPrompt = text;
            label.text = text;
        }

        panel.style.display = DisplayStyle.Flex;
        PositionPrompt();
    }

    private void PositionPrompt()
    {
        if (panel == null || root == null)
            return;

        float rootWidth = root.resolvedStyle.width;
        float rootHeight = root.resolvedStyle.height;
        if (rootWidth <= 0f || rootHeight <= 0f)
            return;

        float panelWidth = panel.resolvedStyle.width;
        float panelHeight = panel.resolvedStyle.height;

        if (panelWidth <= 0f)
            panelWidth = 220f;
        if (panelHeight <= 0f)
            panelHeight = 34f;

        float anchorX = Mathf.Clamp01(normalizedAnchor.x) * rootWidth;
        float anchorY = Mathf.Clamp01(normalizedAnchor.y) * rootHeight;

        float left = anchorX - panelWidth * 0.5f + anchorOffset.x;
        float top = anchorY - panelHeight * 0.5f + anchorOffset.y;

        panel.style.right = StyleKeyword.Auto;
        panel.style.bottom = StyleKeyword.Auto;
        panel.style.left = Mathf.Clamp(left, 8f, Mathf.Max(8f, rootWidth - panelWidth - 8f));
        panel.style.top = Mathf.Clamp(top, 8f, Mathf.Max(8f, rootHeight - panelHeight - 8f));
    }

    private void HidePrompt()
    {
        currentPrompt = string.Empty;

        if (panel != null)
            panel.style.display = DisplayStyle.None;
    }
}
