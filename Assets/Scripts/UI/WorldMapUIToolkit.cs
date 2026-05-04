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
    [SerializeField] private bool autoFitWorldBounds = true;
    [SerializeField] private Vector2 worldOrigin = Vector2.zero;
    [SerializeField] private Vector2 worldSize = new Vector2(1000f, 1000f);

    [Header("Visual Sizes")]
    [SerializeField] private Vector2 minimapSize = new Vector2(220f, 220f);
    [SerializeField] private Vector2 bigMapSize = new Vector2(780f, 820f);
    [SerializeField] private Vector2 minimapMargin = new Vector2(16f, 16f);
    [SerializeField] private float bigMapScreenMargin = 48f;
    [SerializeField] private float minimapWorldSpan = 260f;
    [SerializeField] private int gridLineCount = 8;

    [Header("Colors")]
    [SerializeField] private Color mapBackground = new Color(0f, 0f, 0f, 0.96f);
    [SerializeField] private Color frameBackground = new Color(0f, 0f, 0f, 1f);
    [SerializeField] private Color borderColor = new Color(1f, 1f, 1f, 0.14f);
    [SerializeField] private Color markerColor = new Color(0.95f, 0.92f, 0.18f, 1f);
    [SerializeField] private Color overlayColor = new Color(0f, 0f, 0f, 0.35f);

    private VisualElement minimapPanel;
    private VisualElement minimapFrame;
    private Image minimapImage;
    private VisualElement minimapProceduralLayer;
    private VisualElement minimapMarkerLayer;
    private VisualElement minimapMarker;
    private Label minimapCoordinateLabel;

    private VisualElement bigMapOverlay;
    private VisualElement bigMapPanel;
    private VisualElement bigMapFrame;
    private Image bigMapImage;
    private VisualElement bigMapProceduralLayer;
    private VisualElement bigMapMarkerLayer;
    private VisualElement bigMapMarker;
    private Label bigMapCoordinateLabel;

    private bool isBigMapOpen;
    private float landmarkRefreshTimer;
    private int lastScreenWidth;
    private int lastScreenHeight;

    private readonly List<MapLandmark> landmarks = new List<MapLandmark>();

    private struct MapLandmark
    {
        public Transform transform;
        public string label;
        public Color color;
        public float size;
        public bool showOnMinimap;
    }

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

        if (Screen.width != lastScreenWidth || Screen.height != lastScreenHeight)
            ApplyBaseStyles();

        landmarkRefreshTimer -= Time.unscaledDeltaTime;
        if (landmarkRefreshTimer <= 0f)
        {
            landmarkRefreshTimer = 2f;
            RefreshWorldBounds();
            RefreshLandmarks();
        }

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

        RefreshWorldBounds();
        RefreshLandmarks();
        ApplyBaseStyles();
        ApplyMapTexture();
        BuildProceduralMapLayers();

        root.RegisterCallback<GeometryChangedEvent>(_ => ApplyBaseStyles());
    }

    private void ApplyBaseStyles()
    {
        lastScreenWidth = Screen.width;
        lastScreenHeight = Screen.height;

        float minimapSquarePixels = Mathf.Max(120f, Mathf.Min(minimapSize.x, minimapSize.y));
        float minimapSquareSize = ScreenPixelsToPanelUnits(minimapSquarePixels);
        Vector2 minimapPanelMargin = ScreenPixelsToPanelUnits(minimapMargin);

        Vector2 bigMapPixelSize = GetFittedBigMapScreenSize();
        Vector2 bigMapPanelSize = ScreenPixelsToPanelUnits(bigMapPixelSize);

        if (minimapPanel != null)
        {
            minimapPanel.style.left = minimapPanelMargin.x;
            minimapPanel.style.top = minimapPanelMargin.y;
            minimapPanel.style.width = minimapSquareSize;
            minimapPanel.style.height = minimapSquareSize;
            minimapPanel.style.paddingTop = 0;
            minimapPanel.style.paddingRight = 0;
            minimapPanel.style.paddingBottom = 0;
            minimapPanel.style.paddingLeft = 0;
            minimapPanel.style.backgroundColor = new StyleColor(new Color(0f, 0f, 0f, 0f));
            minimapPanel.style.borderTopWidth = 0;
            minimapPanel.style.borderRightWidth = 0;
            minimapPanel.style.borderBottomWidth = 0;
            minimapPanel.style.borderLeftWidth = 0;
        }

        if (minimapFrame != null)
        {
            minimapFrame.style.width = minimapSquareSize;
            minimapFrame.style.height = minimapSquareSize;
            minimapFrame.style.backgroundColor = frameBackground;
            minimapFrame.style.borderTopWidth = 1;
            minimapFrame.style.borderRightWidth = 1;
            minimapFrame.style.borderBottomWidth = 1;
            minimapFrame.style.borderLeftWidth = 1;
            minimapFrame.style.borderTopColor = borderColor;
            minimapFrame.style.borderRightColor = borderColor;
            minimapFrame.style.borderBottomColor = new StyleColor(new Color(1f, 1f, 1f, 0.08f));
            minimapFrame.style.borderLeftColor = new StyleColor(new Color(1f, 1f, 1f, 0.08f));
        }

        if (bigMapOverlay != null)
            bigMapOverlay.style.backgroundColor = overlayColor;

        if (bigMapPanel != null)
        {
            bigMapPanel.style.width = bigMapPanelSize.x;
            bigMapPanel.style.height = bigMapPanelSize.y;
            bigMapPanel.style.backgroundColor = mapBackground;
            bigMapPanel.style.borderTopColor = borderColor;
            bigMapPanel.style.borderRightColor = borderColor;
            bigMapPanel.style.borderBottomColor = borderColor;
            bigMapPanel.style.borderLeftColor = borderColor;
        }

        if (bigMapFrame != null)
        {
            bigMapFrame.style.backgroundColor = frameBackground;
            bigMapFrame.style.borderTopWidth = 1;
            bigMapFrame.style.borderRightWidth = 1;
            bigMapFrame.style.borderBottomWidth = 1;
            bigMapFrame.style.borderLeftWidth = 1;
            bigMapFrame.style.borderTopColor = borderColor;
            bigMapFrame.style.borderRightColor = borderColor;
            bigMapFrame.style.borderBottomColor = new StyleColor(new Color(1f, 1f, 1f, 0.08f));
            bigMapFrame.style.borderLeftColor = new StyleColor(new Color(1f, 1f, 1f, 0.08f));
        }

        SetMarkerStyle(minimapMarker, ScreenPixelsToPanelUnits(12f));
        SetMarkerStyle(bigMapMarker, ScreenPixelsToPanelUnits(18f));
    }

    private void ApplyMapTexture()
    {
        if (minimapImage != null)
            minimapImage.style.display = DisplayStyle.None;

        if (bigMapImage != null)
            bigMapImage.style.display = DisplayStyle.None;
    }

    private void UpdateMapVisuals()
    {
        if (player == null)
            return;

        UpdateMarker(minimapMarker, minimapFrame, new Vector2(0.5f, 0.5f));
        UpdateMarker(bigMapMarker, bigMapFrame, GetNormalizedPosition(player.transform.position));
        UpdatePlayerMarkerRotation(minimapMarker);
        UpdatePlayerMarkerRotation(bigMapMarker);
        UpdateLandmarkMarkers();
        UpdateCoordinateLabels();
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

        marker.Clear();
        marker.style.position = Position.Absolute;
        marker.style.width = size;
        marker.style.height = size;
        marker.style.backgroundColor = markerColor;
        marker.style.borderTopLeftRadius = 3;
        marker.style.borderTopRightRadius = 3;
        marker.style.borderBottomLeftRadius = 3;
        marker.style.borderBottomRightRadius = 3;
        marker.style.borderTopWidth = 2;
        marker.style.borderRightWidth = 2;
        marker.style.borderBottomWidth = 2;
        marker.style.borderLeftWidth = 2;
        marker.style.borderTopColor = Color.white;
        marker.style.borderRightColor = Color.white;
        marker.style.borderBottomColor = Color.white;
        marker.style.borderLeftColor = Color.white;

        VisualElement nose = new VisualElement();
        nose.style.position = Position.Absolute;
        nose.style.left = size * 0.34f;
        nose.style.top = -size * 0.48f;
        nose.style.width = size * 0.32f;
        nose.style.height = size * 0.62f;
        nose.style.backgroundColor = Color.white;
        nose.style.borderTopLeftRadius = size * 0.16f;
        nose.style.borderTopRightRadius = size * 0.16f;
        marker.Add(nose);
    }

    private Vector2 GetFittedBigMapScreenSize()
    {
        float horizontalMargin = Mathf.Max(24f, bigMapScreenMargin);
        float verticalMargin = Mathf.Max(24f, bigMapScreenMargin);
        float maxWidth = Mathf.Max(320f, Screen.width - horizontalMargin * 2f);
        float maxHeight = Mathf.Max(320f, Screen.height - verticalMargin * 2f);

        return new Vector2(
            Mathf.Min(Mathf.Max(320f, bigMapSize.x), maxWidth),
            Mathf.Min(Mathf.Max(320f, bigMapSize.y), maxHeight));
    }

    private float ScreenPixelsToPanelUnits(float pixels)
    {
        return pixels / GetScreenToPanelScale();
    }

    private Vector2 ScreenPixelsToPanelUnits(Vector2 pixels)
    {
        return new Vector2(
            pixels.x / GetScreenToPanelScaleX(),
            pixels.y / GetScreenToPanelScaleY());
    }

    private float GetScreenToPanelScale()
    {
        float scaleX = GetScreenToPanelScaleX();
        float scaleY = GetScreenToPanelScaleY();
        return Mathf.Max(0.01f, Mathf.Min(scaleX, scaleY));
    }

    private float GetScreenToPanelScaleX()
    {
        VisualElement root = uiDocument != null ? uiDocument.rootVisualElement : null;
        float panelWidth = root != null ? root.resolvedStyle.width : 0f;
        if (float.IsNaN(panelWidth) || panelWidth <= 0f || Screen.width <= 0)
            return 1f;

        return Mathf.Max(0.01f, Screen.width / panelWidth);
    }

    private float GetScreenToPanelScaleY()
    {
        VisualElement root = uiDocument != null ? uiDocument.rootVisualElement : null;
        float panelHeight = root != null ? root.resolvedStyle.height : 0f;
        if (float.IsNaN(panelHeight) || panelHeight <= 0f || Screen.height <= 0)
            return GetScreenToPanelScaleX();

        return Mathf.Max(0.01f, Screen.height / panelHeight);
    }

    private void UpdatePlayerMarkerRotation(VisualElement marker)
    {
        if (marker == null || player == null)
            return;

        marker.style.rotate = new Rotate(new Angle(player.transform.eulerAngles.y, AngleUnit.Degree));
    }

    private void BuildProceduralMapLayers()
    {
        minimapProceduralLayer = BuildProceduralLayer(minimapFrame, false);
        bigMapProceduralLayer = BuildProceduralLayer(bigMapFrame, true);

        minimapMarkerLayer = BuildMarkerLayer(minimapFrame);
        bigMapMarkerLayer = BuildMarkerLayer(bigMapFrame);

        if (minimapMarker != null)
            minimapMarker.BringToFront();

        if (bigMapMarker != null)
            bigMapMarker.BringToFront();
    }

    private VisualElement BuildProceduralLayer(VisualElement frame, bool large)
    {
        if (frame == null)
            return null;

        VisualElement layer = new VisualElement();
        layer.name = large ? "world-map-big-procedural" : "world-map-minimap-procedural";
        layer.style.position = Position.Absolute;
        layer.style.left = 0;
        layer.style.top = 0;
        layer.style.right = 0;
        layer.style.bottom = 0;
        layer.style.backgroundColor = new StyleColor(Color.black);

        AddGrid(layer, large);
        AddCompass(layer, large);

        frame.Insert(0, layer);
        return layer;
    }

    private VisualElement BuildMarkerLayer(VisualElement frame)
    {
        if (frame == null)
            return null;

        VisualElement layer = new VisualElement();
        layer.style.position = Position.Absolute;
        layer.style.left = 0;
        layer.style.top = 0;
        layer.style.right = 0;
        layer.style.bottom = 0;
        layer.pickingMode = PickingMode.Ignore;
        frame.Add(layer);
        return layer;
    }

    private void AddMapBands(VisualElement layer)
    {
        Color[] colors =
        {
            new Color(0.11f, 0.18f, 0.14f, 0.82f),
            new Color(0.14f, 0.13f, 0.10f, 0.72f),
            new Color(0.08f, 0.14f, 0.17f, 0.60f),
            new Color(0.13f, 0.16f, 0.11f, 0.62f)
        };

        for (int i = 0; i < colors.Length; i++)
        {
            VisualElement band = new VisualElement();
            band.style.position = Position.Absolute;
            band.style.left = Length.Percent(0);
            band.style.right = Length.Percent(0);
            band.style.top = Length.Percent(i * 25f);
            band.style.height = Length.Percent(28f);
            band.style.backgroundColor = new StyleColor(colors[i]);
            layer.Add(band);
        }

        VisualElement haze = new VisualElement();
        haze.style.position = Position.Absolute;
        haze.style.left = Length.Percent(8);
        haze.style.top = Length.Percent(12);
        haze.style.width = Length.Percent(84);
        haze.style.height = Length.Percent(70);
        haze.style.backgroundColor = new StyleColor(new Color(0.16f, 0.19f, 0.14f, 0.18f));
        haze.style.borderTopLeftRadius = 90;
        haze.style.borderTopRightRadius = 90;
        haze.style.borderBottomLeftRadius = 90;
        haze.style.borderBottomRightRadius = 90;
        layer.Add(haze);
    }

    private void AddRoadNetwork(VisualElement layer, bool large)
    {
        Color road = large
            ? new Color(0.62f, 0.55f, 0.42f, 0.34f)
            : new Color(0.62f, 0.55f, 0.42f, 0.22f);

        AddMapRoute(layer, 8f, 54f, 86f, 47f, road, large ? 5f : 3f);
        AddMapRoute(layer, 18f, 18f, 78f, 80f, road, large ? 4f : 2f);
        AddMapRoute(layer, 35f, 9f, 62f, 91f, road, large ? 3f : 2f);
        AddMapRoute(layer, 7f, 76f, 92f, 22f, new Color(0.44f, 0.57f, 0.60f, large ? 0.26f : 0.18f), large ? 3f : 2f);
    }

    private void AddMapRoute(VisualElement layer, float startX, float startY, float endX, float endY, Color color, float thickness)
    {
        float dx = endX - startX;
        float dy = endY - startY;
        float length = Mathf.Sqrt(dx * dx + dy * dy);
        float angle = Mathf.Atan2(dy, dx) * Mathf.Rad2Deg;

        VisualElement route = new VisualElement();
        route.style.position = Position.Absolute;
        route.style.left = Length.Percent(startX);
        route.style.top = Length.Percent(startY);
        route.style.width = Length.Percent(length);
        route.style.height = thickness;
        route.style.backgroundColor = new StyleColor(color);
        route.style.rotate = new Rotate(new Angle(angle, AngleUnit.Degree));
        route.style.borderTopLeftRadius = thickness * 0.5f;
        route.style.borderTopRightRadius = thickness * 0.5f;
        route.style.borderBottomLeftRadius = thickness * 0.5f;
        route.style.borderBottomRightRadius = thickness * 0.5f;
        layer.Add(route);
    }

    private void AddGrid(VisualElement layer, bool large)
    {
        int lines = Mathf.Clamp(gridLineCount, 2, 16);
        Color lineColor = large
            ? new Color(1f, 1f, 1f, 0.13f)
            : new Color(1f, 1f, 1f, 0.08f);

        for (int i = 1; i < lines; i++)
        {
            float pct = i * 100f / lines;
            AddLine(layer, pct, true, lineColor);
            AddLine(layer, pct, false, lineColor);
        }
    }

    private void AddLine(VisualElement layer, float pct, bool vertical, Color color)
    {
        VisualElement line = new VisualElement();
        line.style.position = Position.Absolute;
        line.style.backgroundColor = new StyleColor(color);

        if (vertical)
        {
            line.style.left = Length.Percent(pct);
            line.style.top = 0;
            line.style.bottom = 0;
            line.style.width = 1;
        }
        else
        {
            line.style.left = 0;
            line.style.right = 0;
            line.style.top = Length.Percent(pct);
            line.style.height = 1;
        }

        layer.Add(line);
    }

    private void AddCompass(VisualElement layer, bool large)
    {
        Label north = new Label("N");
        north.style.position = Position.Absolute;
        north.style.top = large ? 12 : 6;
        north.style.left = Length.Percent(50);
        north.style.unityFontStyleAndWeight = FontStyle.Bold;
        north.style.fontSize = large ? 14 : 10;
        north.style.color = new StyleColor(new Color(1f, 1f, 1f, 0.72f));
        layer.Add(north);
    }

    private void AddSectorLabels(VisualElement layer, bool large)
    {
        if (!large)
            return;

        string[] labels = { "A1", "A2", "B1", "B2" };
        Vector2[] positions =
        {
            new Vector2(8f, 10f),
            new Vector2(80f, 10f),
            new Vector2(8f, 84f),
            new Vector2(80f, 84f)
        };

        for (int i = 0; i < labels.Length; i++)
        {
            Label label = new Label(labels[i]);
            label.style.position = Position.Absolute;
            label.style.left = Length.Percent(positions[i].x);
            label.style.top = Length.Percent(positions[i].y);
            label.style.fontSize = 11;
            label.style.unityFontStyleAndWeight = FontStyle.Bold;
            label.style.color = new StyleColor(new Color(0.78f, 0.74f, 0.62f, 0.42f));
            layer.Add(label);
        }
    }

    private void RefreshWorldBounds()
    {
        if (!autoFitWorldBounds)
            return;

        Bounds bounds = new Bounds();
        bool hasBounds = false;

        Terrain[] terrains = Terrain.activeTerrains;
        for (int i = 0; i < terrains.Length; i++)
        {
            Terrain terrain = terrains[i];
            if (terrain == null || terrain.terrainData == null)
                continue;

            Vector3 size = terrain.terrainData.size;
            Bounds terrainBounds = new Bounds(terrain.transform.position + size * 0.5f, size);
            if (!hasBounds)
            {
                bounds = terrainBounds;
                hasBounds = true;
            }
            else
            {
                bounds.Encapsulate(terrainBounds);
            }
        }

        StorageContainer[] containers = FindObjectsByType<StorageContainer>(FindObjectsSortMode.None);
        for (int i = 0; i < containers.Length; i++)
        {
            if (containers[i] == null)
                continue;

            if (!hasBounds)
            {
                bounds = new Bounds(containers[i].transform.position, Vector3.one);
                hasBounds = true;
            }
            else
            {
                bounds.Encapsulate(containers[i].transform.position);
            }
        }

        GameObject[] roots = gameObject.scene.GetRootGameObjects();
        for (int i = 0; i < roots.Length; i++)
            EncapsulateHouseBoundsRecursive(roots[i].transform, ref bounds, ref hasBounds);

        if (player != null)
        {
            if (!hasBounds)
            {
                bounds = new Bounds(player.transform.position, Vector3.one);
                hasBounds = true;
            }
            else
            {
                bounds.Encapsulate(player.transform.position);
            }
        }

        if (!hasBounds)
            return;

        float padding = Mathf.Max(bounds.size.x, bounds.size.z) * 0.08f + 20f;
        worldOrigin = new Vector2(bounds.min.x - padding, bounds.min.z - padding);
        worldSize = new Vector2(Mathf.Max(1f, bounds.size.x + padding * 2f), Mathf.Max(1f, bounds.size.z + padding * 2f));
    }

    private void EncapsulateHouseBoundsRecursive(Transform target, ref Bounds bounds, ref bool hasBounds)
    {
        if (target == null)
            return;

        if (target.CompareTag("House"))
        {
            if (!hasBounds)
            {
                bounds = new Bounds(target.position, Vector3.one);
                hasBounds = true;
            }
            else
            {
                bounds.Encapsulate(target.position);
            }

            return;
        }

        for (int i = 0; i < target.childCount; i++)
            EncapsulateHouseBoundsRecursive(target.GetChild(i), ref bounds, ref hasBounds);
    }

    private void RefreshLandmarks()
    {
        landmarks.Clear();

        StorageContainer[] containers = FindObjectsByType<StorageContainer>(FindObjectsSortMode.None);
        for (int i = 0; i < containers.Length; i++)
        {
            StorageContainer container = containers[i];
            if (container == null)
                continue;

            landmarks.Add(new MapLandmark
            {
                transform = container.transform,
                label = "Loot",
                color = new Color(0.86f, 0.62f, 0.28f, 1f),
                size = 7f,
                showOnMinimap = false
            });
        }

        GameObject[] roots = gameObject.scene.GetRootGameObjects();
        for (int i = 0; i < roots.Length; i++)
            AddHouseLandmarksRecursive(roots[i].transform);
    }

    private void AddHouseLandmarksRecursive(Transform target)
    {
        if (target == null)
            return;

        if (target.CompareTag("House"))
        {
            landmarks.Add(new MapLandmark
            {
                transform = target,
                label = "House",
                color = new Color(0.54f, 0.67f, 0.74f, 1f),
                size = 9f,
                showOnMinimap = true
            });

            return;
        }

        for (int i = 0; i < target.childCount; i++)
            AddHouseLandmarksRecursive(target.GetChild(i));
    }

    private void UpdateLandmarkMarkers()
    {
        UpdateLandmarkLayer(minimapMarkerLayer, minimapFrame, false);
        UpdateLandmarkLayer(bigMapMarkerLayer, bigMapFrame, true);
    }

    private void UpdateLandmarkLayer(VisualElement layer, VisualElement frame, bool large)
    {
        if (layer == null || frame == null)
            return;

        layer.Clear();

        float width = GetFrameWidth(frame);
        float height = GetFrameHeight(frame);
        if (width <= 0f || height <= 0f)
            return;

        for (int i = 0; i < landmarks.Count; i++)
        {
            MapLandmark landmark = landmarks[i];
            if (landmark.transform == null || (!large && !landmark.showOnMinimap))
                continue;

            Vector2 normalized = large
                ? GetNormalizedPosition(landmark.transform.position)
                : GetMinimapNormalizedPosition(landmark.transform.position);

            if (!large && (normalized.x < 0f || normalized.x > 1f || normalized.y < 0f || normalized.y > 1f))
                continue;

            float x = Mathf.Clamp01(normalized.x) * width;
            float y = Mathf.Clamp01(1f - normalized.y) * height;
            float size = large ? landmark.size + 3f : landmark.size;

            VisualElement marker = new VisualElement();
            marker.style.position = Position.Absolute;
            marker.style.left = x - size * 0.5f;
            marker.style.top = y - size * 0.5f;
            marker.style.width = size;
            marker.style.height = size;
            marker.style.backgroundColor = new StyleColor(landmark.color);
            marker.style.borderTopLeftRadius = 2;
            marker.style.borderTopRightRadius = 2;
            marker.style.borderBottomLeftRadius = 2;
            marker.style.borderBottomRightRadius = 2;
            marker.style.borderTopWidth = 1;
            marker.style.borderRightWidth = 1;
            marker.style.borderBottomWidth = 1;
            marker.style.borderLeftWidth = 1;
            marker.style.borderTopColor = new StyleColor(new Color(1f, 1f, 1f, 0.22f));
            marker.style.borderRightColor = new StyleColor(new Color(1f, 1f, 1f, 0.22f));
            marker.style.borderBottomColor = new StyleColor(new Color(0f, 0f, 0f, 0.36f));
            marker.style.borderLeftColor = new StyleColor(new Color(0f, 0f, 0f, 0.36f));
            layer.Add(marker);

            if (!large)
                continue;

            Label label = new Label(landmark.label);
            label.style.position = Position.Absolute;
            label.style.left = x + size * 0.7f;
            label.style.top = y - 7;
            label.style.fontSize = 9;
            label.style.color = new StyleColor(new Color(0.88f, 0.86f, 0.78f, 0.88f));
            layer.Add(label);
        }
    }

    private void UpdateCoordinateLabels()
    {
        if (player == null)
            return;

        string coords = $"{Mathf.RoundToInt(player.transform.position.x)}, {Mathf.RoundToInt(player.transform.position.z)}";

        if (bigMapCoordinateLabel == null && bigMapPanel != null)
        {
            bigMapCoordinateLabel = new Label();
            bigMapCoordinateLabel.style.fontSize = 11;
            bigMapCoordinateLabel.style.color = new StyleColor(new Color(0.86f, 0.84f, 0.76f, 0.92f));
            bigMapCoordinateLabel.style.marginTop = 8;
            bigMapPanel.Add(bigMapCoordinateLabel);
        }

        if (bigMapCoordinateLabel != null)
            bigMapCoordinateLabel.text = $"Position: {coords}    Loot: amber    Houses: blue";
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

    private Vector2 GetMinimapNormalizedPosition(Vector3 worldPosition)
    {
        if (player == null)
            return GetNormalizedPosition(worldPosition);

        float span = Mathf.Max(50f, minimapWorldSpan);
        Vector3 delta = worldPosition - player.transform.position;
        float x = 0.5f + delta.x / span;
        float z = 0.5f + delta.z / span;
        return new Vector2(x, z);
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
