using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

[DisallowMultipleComponent]
public class WorldMapUIToolkit : MonoBehaviour
{
    [Header("UI")]
    [SerializeField] private UIDocument uiDocument;

    [Header("Player")]
    [SerializeField] private Player player;
    [SerializeField] private string playerTag = "Player";

    [Header("Map Photo")]
    [SerializeField] private Texture2D mapTexture;
    [SerializeField] private Vector2 worldOrigin = Vector2.zero;
    [SerializeField] private Vector2 worldSize = new Vector2(1000f, 1000f);

    [Header("Visual Sizes")]
    [SerializeField] private Vector2 minimapSize = new Vector2(220f, 220f);
    [SerializeField] private Vector2 bigMapSize = new Vector2(780f, 820f);

    [Header("Colors")]
    [SerializeField] private Color mapBackground = new Color(0.06f, 0.06f, 0.06f, 0.92f);
    [SerializeField] private Color frameBackground = new Color(0.10f, 0.10f, 0.10f, 0.95f);
    [SerializeField] private Color borderColor = new Color(0.55f, 0.55f, 0.55f, 0.22f);
    [SerializeField] private Color markerColor = new Color(0.95f, 0.92f, 0.18f, 1f);
    [SerializeField] private Color overlayColor = new Color(0f, 0f, 0f, 0.35f);

    private VisualElement minimapPanel;
    private VisualElement minimapFrame;
    private Image minimapImage;
    private VisualElement minimapMarker;

    private VisualElement bigMapOverlay;
    private VisualElement bigMapPanel;
    private VisualElement bigMapFrame;
    private Image bigMapImage;
    private VisualElement bigMapMarker;

    private bool isBigMapOpen;

    private void Awake()
    {
        if (uiDocument == null)
            uiDocument = GetComponent<UIDocument>();

        if (uiDocument == null)
        {
            Debug.LogWarning("[WorldMapUIToolkit] No UIDocument found.");
            enabled = false;
            return;
        }

        BuildUi();
        SetBigMapOpen(false);
    }

    private void OnEnable()
    {
        if (player == null)
            player = FindAnyObjectByType<Player>();

        UpdateMapVisuals();
    }

    private void Update()
    {
        if (player == null)
            player = FindAnyObjectByType<Player>();

        if (Keyboard.current != null && Keyboard.current.mKey.wasPressedThisFrame)
            ToggleBigMap();

        UpdateMapVisuals();
    }

    private void BuildUi()
    {
        VisualElement root = uiDocument.rootVisualElement;
        if (root == null)
            return;

        minimapPanel = root.Q<VisualElement>("world-map-minimap-panel");
        minimapFrame = root.Q<VisualElement>("world-map-minimap-frame");
        minimapImage = root.Q<Image>("world-map-minimap-image");
        minimapMarker = root.Q<VisualElement>("world-map-minimap-marker");

        bigMapOverlay = root.Q<VisualElement>("world-map-overlay");
        bigMapPanel = root.Q<VisualElement>("world-map-big-panel");
        bigMapFrame = root.Q<VisualElement>("world-map-big-frame");
        bigMapImage = root.Q<Image>("world-map-big-image");
        bigMapMarker = root.Q<VisualElement>("world-map-big-marker");

        ApplyBaseStyles();
        ApplyMapTexture();
    }

    private void ApplyBaseStyles()
    {
        float scale = ResponsiveUiUtility.GetScaleFactor();
        minimapSize = minimapSize * scale;
        bigMapSize = bigMapSize * scale;

        if (minimapPanel != null)
        {
            minimapPanel.style.width = minimapSize.x;
            minimapPanel.style.height = minimapSize.y;
            minimapPanel.style.backgroundColor = mapBackground;
            minimapPanel.style.borderTopColor = borderColor;
            minimapPanel.style.borderRightColor = borderColor;
            minimapPanel.style.borderBottomColor = borderColor;
            minimapPanel.style.borderLeftColor = borderColor;
        }

        if (minimapFrame != null)
        {
            minimapFrame.style.width = minimapSize.x;
            minimapFrame.style.height = minimapSize.y;
            minimapFrame.style.backgroundColor = frameBackground;
        }

        if (bigMapOverlay != null)
            bigMapOverlay.style.backgroundColor = overlayColor;

        if (bigMapPanel != null)
        {
            bigMapPanel.style.width = bigMapSize.x;
            bigMapPanel.style.height = bigMapSize.y;
            bigMapPanel.style.backgroundColor = mapBackground;
            bigMapPanel.style.borderTopColor = borderColor;
            bigMapPanel.style.borderRightColor = borderColor;
            bigMapPanel.style.borderBottomColor = borderColor;
            bigMapPanel.style.borderLeftColor = borderColor;
        }

        if (bigMapFrame != null)
        {
            bigMapFrame.style.width = bigMapSize.x;
            bigMapFrame.style.height = bigMapSize.y;
            bigMapFrame.style.backgroundColor = frameBackground;
        }

        SetMarkerStyle(minimapMarker, ResponsiveUiUtility.Scale(12f));
        SetMarkerStyle(bigMapMarker, ResponsiveUiUtility.Scale(18f));
    }

    private void ApplyMapTexture()
    {
        if (mapTexture == null)
            return;

        if (minimapImage != null)
            minimapImage.image = mapTexture;

        if (bigMapImage != null)
            bigMapImage.image = mapTexture;
    }

    private void UpdateMapVisuals()
    {
        if (player == null)
            return;

        Vector2 normalized = GetNormalizedPosition(player.transform.position);
        UpdateMarker(minimapMarker, minimapFrame, normalized);
        UpdateMarker(bigMapMarker, bigMapFrame, normalized);
    }

    private void UpdateMarker(VisualElement marker, VisualElement frame, Vector2 normalized)
    {
        if (marker == null || frame == null)
            return;

        float width = GetFrameWidth(frame);
        float height = GetFrameHeight(frame);
        if (width <= 0f || height <= 0f)
            return;

        float x = Mathf.Clamp01(normalized.x) * width;
        float y = Mathf.Clamp01(1f - normalized.y) * height;

        marker.style.left = x - marker.resolvedStyle.width * 0.5f;
        marker.style.top = y - marker.resolvedStyle.height * 0.5f;
    }

    private void SetMarkerStyle(VisualElement marker, float size)
    {
        if (marker == null)
            return;

        marker.style.position = Position.Absolute;
        marker.style.width = size;
        marker.style.height = size;
        marker.style.backgroundColor = markerColor;
        marker.style.borderTopLeftRadius = size * 0.5f;
        marker.style.borderTopRightRadius = size * 0.5f;
        marker.style.borderBottomLeftRadius = size * 0.5f;
        marker.style.borderBottomRightRadius = size * 0.5f;
    }

    private static float GetFrameWidth(VisualElement frame)
    {
        float width = frame.resolvedStyle.width;
        if (width > 0f)
            return width;

        return frame.style.width.value.value;
    }

    private static float GetFrameHeight(VisualElement frame)
    {
        float height = frame.resolvedStyle.height;
        if (height > 0f)
            return height;

        return frame.style.height.value.value;
    }

    private Vector2 GetNormalizedPosition(Vector3 worldPosition)
    {
        float x = Mathf.InverseLerp(worldOrigin.x, worldOrigin.x + worldSize.x, worldPosition.x);
        float z = Mathf.InverseLerp(worldOrigin.y, worldOrigin.y + worldSize.y, worldPosition.z);
        return new Vector2(Mathf.Clamp01(x), Mathf.Clamp01(z));
    }

    public void ToggleBigMap() => SetBigMapOpen(!isBigMapOpen);

    public void SetBigMapOpen(bool open)
    {
        isBigMapOpen = open;

        if (bigMapOverlay != null)
            bigMapOverlay.style.display = open ? DisplayStyle.Flex : DisplayStyle.None;

        UnityEngine.Cursor.visible = open;
        UnityEngine.Cursor.lockState = open ? CursorLockMode.None : CursorLockMode.Locked;
    }
}
