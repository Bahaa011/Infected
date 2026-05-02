using UnityEngine;
using UnityEngine.UIElements;

public class CrosshairUIToolkit : MonoBehaviour
{
    private static CrosshairUIToolkit instance;

    [SerializeField] private UIDocument uiDocument;
    [SerializeField] private Player player;
    [SerializeField] private EquipmentManager equipmentManager;

    [Header("Visual")]
    [SerializeField] private Color crosshairColor = new Color(0.96f, 0.96f, 0.96f, 0.95f);

    [Header("Hit Feedback")]
    [SerializeField] private Color hitMarkerColor = new Color(1f, 1f, 1f, 0.95f);
    [SerializeField] private float hitMarkerDuration = 0.12f;
    [SerializeField] private float hitMarkerStartScale = 1.2f;
    [SerializeField] private float hitMarkerEndScale = 1f;
    [SerializeField] private AudioClip[] zombieHitClips;
    [SerializeField] private float zombieHitVolume = 0.75f;
    [SerializeField] private float minHitSoundInterval = 0.03f;

    private VisualElement root;
    private VisualElement hitMarkerRoot;
    private bool isVisible;
    private float hitMarkerTimer;
    private float lastHitSoundTime = -10f;
    private AudioSource audioSource;
    private int lastScreenWidth;
    private int lastScreenHeight;

    private void Awake()
    {
        instance = this;

        if (uiDocument == null)
            uiDocument = GetComponent<UIDocument>();

        if (uiDocument == null)
            return;

        audioSource = GetComponent<AudioSource>();
        if (audioSource == null)
            audioSource = gameObject.AddComponent<AudioSource>();

        audioSource.playOnAwake = false;
        audioSource.spatialBlend = 0f;

        root = uiDocument.rootVisualElement?.Q<VisualElement>(className: "crosshair-root");
        hitMarkerRoot = uiDocument.rootVisualElement?.Q<VisualElement>("hit-marker-root");

        if (root != null)
        {
            root.pickingMode = PickingMode.Ignore;
            root.style.display = DisplayStyle.None;
            ApplyColor();
        }

        if (hitMarkerRoot != null)
        {
            hitMarkerRoot.pickingMode = PickingMode.Ignore;
            hitMarkerRoot.style.display = DisplayStyle.None;
            ApplyHitMarkerColor();
        }

        RefreshResponsiveScale(true);
    }

    private void OnDestroy()
    {
        if (instance == this)
            instance = null;
    }

    private void OnEnable()
    {
        if (player == null)
            player = FindAnyObjectByType<Player>();

        if (equipmentManager == null)
            equipmentManager = FindAnyObjectByType<EquipmentManager>();

        SetVisible(false);
    }

    private void Update()
    {
        RefreshResponsiveScale();
        UpdateHitMarker();

        if (root == null)
            return;

        if (player == null)
            player = FindAnyObjectByType<Player>();

        if (equipmentManager == null)
            equipmentManager = FindAnyObjectByType<EquipmentManager>();

        bool hasGunInHand = equipmentManager != null && equipmentManager.GetCurrentWeapon() != null;
        bool shouldShow = !InventoryUIToolkit.IsInventoryOpen && hasGunInHand && player != null && player.IsAiming();

        if (shouldShow != isVisible)
            SetVisible(shouldShow);
    }

    public static void RegisterZombieHit()
    {
        if (instance == null)
            return;

        instance.ShowHitMarker();
        instance.PlayHitConfirmSound();
    }

    private void SetVisible(bool visible)
    {
        isVisible = visible;
        if (root != null)
            root.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
    }

    private void ApplyColor()
    {
        if (root == null)
            return;

        SetElementColor("crosshair-line-top");
        SetElementColor("crosshair-line-bottom");
        SetElementColor("crosshair-line-left");
        SetElementColor("crosshair-line-right");
        SetElementColor("crosshair-dot");
    }

    private void ApplyHitMarkerColor()
    {
        if (hitMarkerRoot == null)
            return;

        SetHitMarkerElementColor("hit-marker-line-1");
        SetHitMarkerElementColor("hit-marker-line-2");
        SetHitMarkerElementColor("hit-marker-line-3");
        SetHitMarkerElementColor("hit-marker-line-4");
    }

    private void SetElementColor(string name)
    {
        var element = root.Q<VisualElement>(name);
        if (element != null)
            element.style.backgroundColor = new StyleColor(crosshairColor);
    }

    private void SetHitMarkerElementColor(string name)
    {
        var element = hitMarkerRoot.Q<VisualElement>(name);
        if (element != null)
            element.style.backgroundColor = new StyleColor(hitMarkerColor);
    }

    private void ShowHitMarker()
    {
        if (hitMarkerRoot == null)
            return;

        hitMarkerTimer = hitMarkerDuration;
        hitMarkerRoot.style.display = DisplayStyle.Flex;
        hitMarkerRoot.style.opacity = 1f;
        hitMarkerRoot.style.scale = new Scale(new Vector3(hitMarkerStartScale, hitMarkerStartScale, 1f));
    }

    private void UpdateHitMarker()
    {
        if (hitMarkerRoot == null)
            return;

        if (hitMarkerTimer <= 0f)
            return;

        hitMarkerTimer -= Time.unscaledDeltaTime;
        float t = 1f - Mathf.Clamp01(hitMarkerTimer / Mathf.Max(0.0001f, hitMarkerDuration));
        float alpha = Mathf.Lerp(1f, 0f, t);
        float scale = Mathf.Lerp(hitMarkerStartScale, hitMarkerEndScale, t);

        hitMarkerRoot.style.opacity = alpha;
        hitMarkerRoot.style.scale = new Scale(new Vector3(scale, scale, 1f));

        if (hitMarkerTimer <= 0f)
        {
            hitMarkerRoot.style.display = DisplayStyle.None;
        }
    }

    private void PlayHitConfirmSound()
    {
        if (audioSource == null)
            return;

        if (Time.unscaledTime - lastHitSoundTime < minHitSoundInterval)
            return;

        if (zombieHitClips == null || zombieHitClips.Length == 0)
            return;

        int clipIndex = Random.Range(0, zombieHitClips.Length);
        AudioClip clip = zombieHitClips[clipIndex];
        if (clip == null)
            return;

        audioSource.PlayOneShot(clip, zombieHitVolume);
        lastHitSoundTime = Time.unscaledTime;
    }

    private void RefreshResponsiveScale(bool force = false)
    {
        if (!force && Screen.width == lastScreenWidth && Screen.height == lastScreenHeight)
            return;

        lastScreenWidth = Screen.width;
        lastScreenHeight = Screen.height;

        if (root != null)
            root.style.scale = new Scale(Vector3.one);

        if (hitMarkerRoot != null)
            hitMarkerRoot.style.scale = new Scale(Vector3.one);
    }
}
