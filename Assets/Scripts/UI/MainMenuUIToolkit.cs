using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;

[DisallowMultipleComponent]
public class MainMenuUIToolkit : MonoBehaviour
{
    private const string PrefSelectedSlotIndex = "save.selectedSlotIndex";
    private const int FixedSaveSlotCount = 3;
    private const string PrefMasterVolume = "settings.masterVolume";
    private const string PrefFullscreen = "settings.fullscreen";
    private const string PrefResolutionWidth = "settings.resolutionWidth";
    private const string PrefResolutionHeight = "settings.resolutionHeight";
    private const string MainMenuBackgroundResourcePath = "Art/MainBG";

    [Header("References")]
    [SerializeField] private UIDocument uiDocument;
    [SerializeField] private Texture2D mainMenuBackgroundTexture;

    [Header("Scene Flow")]
    [SerializeField] private string gameplaySceneName = "Main";

    [Header("Save Slots")]
    [SerializeField] private int saveSlotCount = 3;
    [SerializeField] private string saveFilePrefix = "save_slot_";

    [Header("Defaults")]
    [SerializeField] private float defaultMasterVolume = 1f;

    [Header("Polish")]
    [SerializeField] private float startupFadeDuration = 0.7f;
    [SerializeField] private float uiTransitionSpeed = 10f;
    [SerializeField] private float buttonHoverSpeed = 14f;
    [SerializeField] private float buttonHoverScale = 1.04f;
    [SerializeField] private float buttonPressedScale = 0.98f;

    private VisualElement root;
    private VisualElement backgroundImage;
    private VisualElement mainPanel;
    private VisualElement optionsPanel;
    private VisualElement saveSlotsPanel;
    private ScrollView saveSlotsList;
    private VisualElement loadingPanel;
    private Label loadingTitleLabel;
    private Label loadingStatusLabel;
    private ProgressBar loadingProgressBar;

    private Button playButton;
    private Button optionsButton;
    private Button quitButton;
    private Button optionsBackButton;
    private Button saveSlotsBackButton;

    private Slider masterVolumeSlider;
    private Toggle fullscreenToggle;
    private DropdownField resolutionDropdown;
    private VisualElement deleteConfirmOverlay;
    private Label deleteConfirmMessageLabel;
    private Button deleteConfirmYesButton;
    private Button deleteConfirmNoButton;

    private readonly List<VisualElement> slotRows = new List<VisualElement>();
    private readonly List<int> slotRowIndexes = new List<int>();
    private readonly List<Vector2Int> resolutionOptions = new List<Vector2Int>();

    private readonly Dictionary<Button, float> buttonHoverTargets = new Dictionary<Button, float>();
    private readonly Dictionary<Button, float> buttonHoverValues = new Dictionary<Button, float>();
    private readonly HashSet<Button> pressedButtons = new HashSet<Button>();

    private float startupFadeTimer;
    private float optionsPanelBlend;
    private float optionsPanelBlendTarget;
    private AsyncOperation loadingOperation;
    private bool isLoadingWorld;
    private float loadingVisualProgress;
    private float finishLoadingDelay;
    private int pendingDeleteSlotIndex = -1;

    private void Awake()
    {
        if (uiDocument == null)
            uiDocument = GetComponent<UIDocument>();

        if (uiDocument == null)
        {
            Debug.LogWarning("[MainMenuUIToolkit] UIDocument is missing.");
            enabled = false;
            return;
        }

        root = uiDocument.rootVisualElement;

        CacheUi();
        ApplyBackgroundImage();
        HookUi();
        InitializeOptionsUi();

        ShowMainPanel();
        EnsureMenuState();
        InitializeVisualPolish();
    }

    private void Update()
    {
        float dt = Time.unscaledDeltaTime;

        UpdateStartupFade(dt);
        UpdatePanelTransition(dt);
        UpdateButtonAnimations(dt);
        UpdateLoadingScreen(dt);

        // Check if finish loading delay is complete
        if (isLoadingWorld && finishLoadingDelay <= 0 && loadingOperation != null && loadingOperation.isDone)
        {
            FinishLoadingScreen();
        }

        if (Keyboard.current != null
            && Keyboard.current.escapeKey.wasPressedThisFrame
            && !isLoadingWorld
            && (optionsPanelBlendTarget > 0.5f || (saveSlotsPanel != null && saveSlotsPanel.style.display == DisplayStyle.Flex)))
        {
            if (deleteConfirmOverlay != null && deleteConfirmOverlay.style.display == DisplayStyle.Flex)
            {
                HideDeleteConfirmDialog();
                return;
            }

            ShowMainPanel();
        }
    }

    private void CacheUi()
    {
        if (root == null)
            return;

        backgroundImage = root.Q<VisualElement>("mainmenu-background-image");
        mainPanel = root.Q<VisualElement>("mainmenu-main-panel");
        optionsPanel = root.Q<VisualElement>("mainmenu-options-panel");
        saveSlotsPanel = root.Q<VisualElement>("mainmenu-save-slots-panel");
        loadingPanel = root.Q<VisualElement>("mainmenu-loading-panel");

        playButton = root.Q<Button>("mainmenu-play-btn");
        optionsButton = root.Q<Button>("mainmenu-options-btn");
        quitButton = root.Q<Button>("mainmenu-quit-btn");
        optionsBackButton = root.Q<Button>("mainmenu-options-back-btn");
        saveSlotsBackButton = root.Q<Button>("mainmenu-save-slots-back-btn");

        masterVolumeSlider = root.Q<Slider>("mainmenu-master-volume-slider");
        fullscreenToggle = root.Q<Toggle>("mainmenu-fullscreen-toggle");
        resolutionDropdown = root.Q<DropdownField>("mainmenu-resolution-dropdown");

        saveSlotsList = root.Q<ScrollView>("mainmenu-save-slots-list");
        loadingTitleLabel = root.Q<Label>("mainmenu-loading-title");
        loadingStatusLabel = root.Q<Label>("mainmenu-loading-status");
        loadingProgressBar = root.Q<ProgressBar>("mainmenu-loading-progress");
    }

    private void ApplyBackgroundImage()
    {
        if (backgroundImage == null)
            return;

        Texture2D texture = TryGetBackgroundTexture();
        if (texture == null)
            return;

        backgroundImage.style.backgroundImage = new StyleBackground(texture);
    }

    private Texture2D TryGetBackgroundTexture()
    {
        if (mainMenuBackgroundTexture != null)
            return mainMenuBackgroundTexture;

        Texture2D resourceTexture = Resources.Load<Texture2D>("MainBG");
        if (resourceTexture != null)
            return resourceTexture;

        resourceTexture = Resources.Load<Texture2D>(MainMenuBackgroundResourcePath);
        if (resourceTexture != null)
            return resourceTexture;

#if UNITY_EDITOR
        Texture2D assetTexture = UnityEditor.AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Resources/Art/MainBG.png");
        if (assetTexture != null)
        {
            mainMenuBackgroundTexture = assetTexture;
            return assetTexture;
        }
#endif

        Debug.LogWarning($"[MainMenuUIToolkit] Could not find main menu background texture. Assign it in the inspector or place it at Resources/{MainMenuBackgroundResourcePath}.");
        return null;
    }

    private void HookUi()
    {
        if (playButton != null)
            playButton.clicked += ShowSaveSlotsPanel;

        if (optionsButton != null)
            optionsButton.clicked += ShowOptionsPanel;

        if (quitButton != null)
            quitButton.clicked += QuitGame;

        if (optionsBackButton != null)
            optionsBackButton.clicked += ShowMainPanel;

        if (saveSlotsBackButton != null)
            saveSlotsBackButton.clicked += ShowMainPanel;

        if (masterVolumeSlider != null)
            masterVolumeSlider.RegisterValueChangedCallback(evt => ApplyMasterVolume(evt.newValue, true));

        if (fullscreenToggle != null)
            fullscreenToggle.RegisterValueChangedCallback(evt => ApplyFullscreen(evt.newValue, true));

        if (resolutionDropdown != null)
            resolutionDropdown.RegisterValueChangedCallback(evt => ApplyResolution(evt.newValue, true));

        ConfigureMainButtonHover(playButton);
        ConfigureMainButtonHover(optionsButton);
        ConfigureMainButtonHover(quitButton);
        BuildDeleteConfirmDialog();
    }

    private void ConfigureMainButtonHover(Button button)
    {
        if (button == null)
            return;

        buttonHoverTargets[button] = 0f;
        buttonHoverValues[button] = 0f;

        button.RegisterCallback<MouseEnterEvent>(_ => buttonHoverTargets[button] = 1f);
        button.RegisterCallback<MouseLeaveEvent>(_ =>
        {
            buttonHoverTargets[button] = 0f;
            pressedButtons.Remove(button);
        });

        button.RegisterCallback<MouseDownEvent>(_ => pressedButtons.Add(button));
        button.RegisterCallback<MouseUpEvent>(_ => pressedButtons.Remove(button));

        ApplyButtonStyle(button, 0f, false);
    }

    private void ApplyButtonStyle(Button button, float hoverT, bool pressed)
    {
        if (button == null)
            return;

        Color normalBackground = new Color(16f / 255f, 16f / 255f, 16f / 255f, 0.82f);
        Color hoverBackground = new Color(138f / 255f, 75f / 255f, 32f / 255f, 0.72f);
        Color normalText = new Color(250f / 255f, 250f / 255f, 250f / 255f, 0.95f);
        Color hoverText = Color.white;
        Color normalBorder = new Color(230f / 255f, 230f / 255f, 230f / 255f, 0.32f);
        Color hoverBorder = new Color(236f / 255f, 161f / 255f, 83f / 255f, 0.95f);

        float targetScale = pressed ? buttonPressedScale : Mathf.Lerp(1f, buttonHoverScale, hoverT);

        button.style.backgroundColor = Color.Lerp(normalBackground, hoverBackground, hoverT);
        button.style.color = Color.Lerp(normalText, hoverText, hoverT);
        button.style.borderTopColor = Color.Lerp(normalBorder, hoverBorder, hoverT);
        button.style.borderRightColor = Color.Lerp(normalBorder, hoverBorder, hoverT);
        button.style.borderBottomColor = Color.Lerp(normalBorder, hoverBorder, hoverT);
        button.style.borderLeftColor = Color.Lerp(normalBorder, hoverBorder, hoverT);
        button.style.scale = new Scale(new Vector3(targetScale, targetScale, 1f));
    }

    private void InitializeVisualPolish()
    {
        startupFadeTimer = 0f;
        optionsPanelBlend = 0f;
        optionsPanelBlendTarget = 0f;

        if (root != null)
            root.style.opacity = 0f;

        ApplyPanelBlendImmediate(0f);
    }

    private void UpdateStartupFade(float dt)
    {
        if (root == null)
            return;

        startupFadeTimer += dt;
        float duration = Mathf.Max(0.01f, startupFadeDuration);
        float t = Mathf.Clamp01(startupFadeTimer / duration);
        root.style.opacity = Mathf.SmoothStep(0f, 1f, t);
    }

    private void UpdatePanelTransition(float dt)
    {
        float lerp = Mathf.Clamp01(dt * uiTransitionSpeed);
        optionsPanelBlend = Mathf.Lerp(optionsPanelBlend, optionsPanelBlendTarget, lerp);

        if (Mathf.Abs(optionsPanelBlend - optionsPanelBlendTarget) < 0.001f)
            optionsPanelBlend = optionsPanelBlendTarget;

        ApplyPanelBlendImmediate(optionsPanelBlend);
    }

    private void ApplyPanelBlendImmediate(float blend)
    {
        if (mainPanel != null)
        {
            mainPanel.style.opacity = 1f - blend;
            mainPanel.style.display = blend >= 0.999f ? DisplayStyle.None : DisplayStyle.Flex;
            mainPanel.pickingMode = blend >= 0.5f ? PickingMode.Ignore : PickingMode.Position;
        }

        if (optionsPanel != null)
        {
            optionsPanel.style.opacity = blend;
            optionsPanel.style.display = blend <= 0.001f ? DisplayStyle.None : DisplayStyle.Flex;
            optionsPanel.pickingMode = blend <= 0.5f ? PickingMode.Ignore : PickingMode.Position;
        }
    }

    private void UpdateButtonAnimations(float dt)
    {
        float lerp = Mathf.Clamp01(dt * buttonHoverSpeed);

        foreach (KeyValuePair<Button, float> pair in buttonHoverTargets)
        {
            Button button = pair.Key;
            float target = pair.Value;

            if (!buttonHoverValues.TryGetValue(button, out float current))
                current = 0f;

            current = Mathf.Lerp(current, target, lerp);
            buttonHoverValues[button] = current;

            bool pressed = pressedButtons.Contains(button);
            ApplyButtonStyle(button, current, pressed);
        }
    }

    private void InitializeOptionsUi()
    {
        BuildResolutionOptions();

        float masterVolume = PlayerPrefs.GetFloat(PrefMasterVolume, Mathf.Clamp01(defaultMasterVolume));
        bool fullscreen = PlayerPrefs.GetInt(PrefFullscreen, Screen.fullScreen ? 1 : 0) == 1;
        int resolutionWidth = PlayerPrefs.GetInt(PrefResolutionWidth, Screen.width);
        int resolutionHeight = PlayerPrefs.GetInt(PrefResolutionHeight, Screen.height);

        ApplyMasterVolume(masterVolume, false);
        ApplyResolution(resolutionWidth, resolutionHeight, fullscreen, false);

        if (masterVolumeSlider != null)
            masterVolumeSlider.value = masterVolume;

        if (fullscreenToggle != null)
            fullscreenToggle.value = fullscreen;

        if (resolutionDropdown != null)
            resolutionDropdown.SetValueWithoutNotify(FormatResolution(GetClosestResolutionIndex(resolutionWidth, resolutionHeight)));

        BuildSaveSlotsList();
        RefreshSaveSlotsUi();
    }

    private void EnsureMenuState()
    {
        Time.timeScale = 1f;
        AudioListener.pause = false;
        UnityEngine.Cursor.lockState = CursorLockMode.None;
        UnityEngine.Cursor.visible = true;
    }

    private void ShowMainPanel()
    {
        HideDeleteConfirmDialog();

        if (saveSlotsPanel != null)
            saveSlotsPanel.style.display = DisplayStyle.None;

        optionsPanelBlendTarget = 0f;

        if (mainPanel != null)
            mainPanel.style.display = DisplayStyle.Flex;

        if (optionsPanel != null)
            optionsPanel.style.display = DisplayStyle.Flex;
    }

    private void ShowOptionsPanel()
    {
        HideDeleteConfirmDialog();

        if (saveSlotsPanel != null)
            saveSlotsPanel.style.display = DisplayStyle.None;

        optionsPanelBlendTarget = 1f;

        if (mainPanel != null)
            mainPanel.style.display = DisplayStyle.Flex;

        if (optionsPanel != null)
            optionsPanel.style.display = DisplayStyle.Flex;
    }

    private void ShowSaveSlotsPanel()
    {
        if (isLoadingWorld)
            return;

        if (mainPanel != null)
            mainPanel.style.display = DisplayStyle.None;

        if (optionsPanel != null)
            optionsPanel.style.display = DisplayStyle.None;

        optionsPanelBlendTarget = 0f;

        if (saveSlotsPanel != null)
            saveSlotsPanel.style.display = DisplayStyle.Flex;

        BuildSaveSlotsList();
        RefreshSaveSlotsUi();
    }

    private void StartGameFromSlot(int slotIndex)
    {
        slotIndex = Mathf.Clamp(slotIndex, 1, FixedSaveSlotCount);

        PlayerPrefs.SetInt(PrefSelectedSlotIndex, slotIndex);
        PlayerPrefs.Save();

        if (!File.Exists(GetSaveFilePath(slotIndex)))
            StorageContainer.ClearPersistentLootRuntimeState();

        if (string.IsNullOrWhiteSpace(gameplaySceneName))
        {
            Debug.LogWarning("[MainMenuUIToolkit] Gameplay scene name is empty.");
            return;
        }

        if (!Application.CanStreamedLevelBeLoaded(gameplaySceneName))
        {
            Debug.LogWarning($"[MainMenuUIToolkit] Scene '{gameplaySceneName}' is not in Build Settings.");
            return;
        }

        StartWorldLoad(slotIndex);
    }

    private void RequestDeleteSaveSlot(int slotIndex)
    {
        string savePath = GetSaveFilePath(slotIndex);
        if (!File.Exists(savePath))
            return;

        pendingDeleteSlotIndex = Mathf.Clamp(slotIndex, 1, FixedSaveSlotCount);

        if (deleteConfirmMessageLabel != null)
            deleteConfirmMessageLabel.text = $"Delete Slot {pendingDeleteSlotIndex}? This cannot be undone.";

        if (deleteConfirmOverlay != null)
            deleteConfirmOverlay.style.display = DisplayStyle.Flex;
    }

    private void ConfirmDeleteSaveSlot()
    {
        if (pendingDeleteSlotIndex <= 0)
            return;

        int slotIndex = pendingDeleteSlotIndex;
        pendingDeleteSlotIndex = -1;
        HideDeleteConfirmDialog();
        DeleteSaveSlot(slotIndex);
    }

    private void DeleteSaveSlot(int slotIndex)
    {
        string savePath = GetSaveFilePath(slotIndex);
        
        if (File.Exists(savePath))
        {
            File.Delete(savePath);
            Debug.Log($"[MainMenuUIToolkit] Deleted save slot {slotIndex}: {savePath}");
        }
        
        // Refresh the UI to show updated slot state
        BuildSaveSlotsList();
        RefreshSaveSlotsUi();
    }

    private void HideDeleteConfirmDialog()
    {
        pendingDeleteSlotIndex = -1;

        if (deleteConfirmOverlay != null)
            deleteConfirmOverlay.style.display = DisplayStyle.None;
    }

    private void StartWorldLoad(int slotIndex)
    {
        if (isLoadingWorld)
            return;

        HideDeleteConfirmDialog();

        WorldLoadingState.BeginLoading();
        isLoadingWorld = true;
        loadingVisualProgress = 0f;

        // Show cursor during loading
        UnityEngine.Cursor.lockState = CursorLockMode.None;
        UnityEngine.Cursor.visible = true;

        if (mainPanel != null)
            mainPanel.style.display = DisplayStyle.None;

        if (optionsPanel != null)
            optionsPanel.style.display = DisplayStyle.None;

        if (saveSlotsPanel != null)
            saveSlotsPanel.style.display = DisplayStyle.None;

        optionsPanelBlendTarget = 0f;

        if (loadingTitleLabel != null)
            loadingTitleLabel.text = slotIndex >= 1 ? $"LOADING SLOT {slotIndex}" : "LOADING";

        if (loadingStatusLabel != null)
            loadingStatusLabel.text = "Preparing world...";

        if (loadingProgressBar != null)
            loadingProgressBar.value = 0f;

        if (loadingPanel != null)
            loadingPanel.style.display = DisplayStyle.Flex;

        loadingOperation = SceneManager.LoadSceneAsync(gameplaySceneName, LoadSceneMode.Single);
        if (loadingOperation == null)
        {
            Debug.LogError($"[MainMenuUIToolkit] Failed to start async load for scene '{gameplaySceneName}'.");
            isLoadingWorld = false;
            if (loadingPanel != null)
                loadingPanel.style.display = DisplayStyle.None;
        }
    }

    private void UpdateLoadingScreen(float dt)
    {
        if (!isLoadingWorld)
            return;

        // If we're in the finish delay, just wait
        if (finishLoadingDelay > 0)
        {
            finishLoadingDelay -= dt;
            return;
        }

        float target = 0f;
        string status = "Preparing world...";

        if (loadingOperation != null)
        {
            target = Mathf.Clamp01(loadingOperation.progress / 0.9f);

            if (loadingOperation.progress >= 0.9f)
                status = "Finalizing...";
            else if (target < 0.33f)
                status = "Loading world...";
            else if (target < 0.66f)
                status = "Setting up systems...";
            else
                status = "Almost ready...";

            if (loadingOperation.isDone)
            {
                // Start the finish delay
                if (finishLoadingDelay <= 0)
                    finishLoadingDelay = 0.1f; // 100ms delay to let scene fully activate
            }
        }

        loadingVisualProgress = Mathf.MoveTowards(loadingVisualProgress, target, dt * 0.75f);

        if (loadingProgressBar != null)
            loadingProgressBar.value = loadingVisualProgress * 100f;

        if (loadingStatusLabel != null)
            loadingStatusLabel.text = status;
    }

    private void FinishLoadingScreen()
    {
        isLoadingWorld = false;
        loadingOperation = null;

        if (loadingPanel != null)
            loadingPanel.style.display = DisplayStyle.None;

        // Lock cursor and hide it when gameplay starts
        UnityEngine.Cursor.lockState = CursorLockMode.Locked;
        UnityEngine.Cursor.visible = false;

        // Mark world as ready so player can move
        WorldLoadingState.MarkWorldReady();

        // Destroy menu UI to free resources
        Destroy(gameObject);
    }

    private void BuildSaveSlotsList()
    {
        if (saveSlotsList == null)
            return;

        saveSlotsList.Clear();
        slotRows.Clear();
        slotRowIndexes.Clear();

        int count = FixedSaveSlotCount;
        for (int slotIndex = 1; slotIndex <= count; slotIndex++)
        {
            GameSaveData saveData = ReadSaveData(GetSaveFilePath(slotIndex));

            VisualElement row = new VisualElement();
            row.style.flexDirection = FlexDirection.Column;
            row.style.paddingTop = 10;
            row.style.paddingRight = 10;
            row.style.paddingBottom = 10;
            row.style.paddingLeft = 10;
            row.style.marginBottom = 6;
            row.style.backgroundColor = new StyleColor(new Color(0.08f, 0.08f, 0.08f, 0.78f));
            row.style.borderTopLeftRadius = 2;
            row.style.borderTopRightRadius = 2;
            row.style.borderBottomLeftRadius = 2;
            row.style.borderBottomRightRadius = 2;
            row.style.borderTopWidth = 1;
            row.style.borderRightWidth = 1;
            row.style.borderBottomWidth = 1;
            row.style.borderLeftWidth = 1;
            row.style.borderTopColor = new StyleColor(new Color(0.72f, 0.45f, 0.24f, 0.2f));
            row.style.borderRightColor = new StyleColor(new Color(0.72f, 0.45f, 0.24f, 0.2f));
            row.style.borderBottomColor = new StyleColor(new Color(0.2f, 0.2f, 0.2f, 0.28f));
            row.style.borderLeftColor = new StyleColor(new Color(0.2f, 0.2f, 0.2f, 0.28f));

            int capturedSlot = slotIndex;
            row.RegisterCallback<ClickEvent>(evt =>
            {
                if (evt.target is Button)
                    return;

                StartGameFromSlot(capturedSlot);
            });

            row.RegisterCallback<MouseEnterEvent>(_ =>
            {
                row.style.backgroundColor = new StyleColor(new Color(0.14f, 0.13f, 0.12f, 0.88f));
                row.style.borderTopColor = new StyleColor(new Color(0.95f, 0.7f, 0.45f, 0.4f));
                row.style.borderRightColor = new StyleColor(new Color(0.95f, 0.7f, 0.45f, 0.4f));
            });
            row.RegisterCallback<MouseLeaveEvent>(_ => RefreshSingleSlotStyle(row, capturedSlot, saveData != null, GetSelectedSlotIndex()));

            Label title = new Label();
            title.name = $"mainmenu-slot-title-{slotIndex}";
            title.style.fontSize = 16;
            title.style.unityFontStyleAndWeight = FontStyle.Bold;
            title.style.color = new StyleColor(new Color(0.95f, 0.94f, 0.92f, 0.95f));
            row.Add(title);

            Label sub = new Label();
            sub.name = $"mainmenu-slot-sub-{slotIndex}";
            sub.style.fontSize = 11;
            sub.style.color = new StyleColor(new Color(0.72f, 0.72f, 0.72f, 0.84f));
            row.Add(sub);

            // Add a horizontal container for delete button
            VisualElement buttonContainer = new VisualElement();
            buttonContainer.style.flexDirection = FlexDirection.Row;
            buttonContainer.style.justifyContent = Justify.FlexEnd;
            buttonContainer.style.marginTop = 6;

            Button deleteBtn = new Button();
            deleteBtn.text = "DELETE";
            deleteBtn.style.paddingTop = 4;
            deleteBtn.style.paddingBottom = 4;
            deleteBtn.style.paddingLeft = 8;
            deleteBtn.style.paddingRight = 8;
            deleteBtn.style.fontSize = 10;
            deleteBtn.style.backgroundColor = new StyleColor(new Color(0.6f, 0.2f, 0.2f, 0.7f));
            deleteBtn.style.color = new StyleColor(new Color(0.95f, 0.9f, 0.88f, 0.95f));
            deleteBtn.style.borderTopLeftRadius = 2;
            deleteBtn.style.borderTopRightRadius = 2;
            deleteBtn.style.borderBottomLeftRadius = 2;
            deleteBtn.style.borderBottomRightRadius = 2;
            
            int deleteSlot = slotIndex;
            deleteBtn.clicked += () => RequestDeleteSaveSlot(deleteSlot);
            
            buttonContainer.Add(deleteBtn);
            row.Add(buttonContainer);

            slotRows.Add(row);
            slotRowIndexes.Add(slotIndex);
            saveSlotsList.Add(row);
        }
    }

    private void BuildDeleteConfirmDialog()
    {
        if (saveSlotsPanel == null || deleteConfirmOverlay != null)
            return;

        deleteConfirmOverlay = new VisualElement();
        deleteConfirmOverlay.style.position = Position.Absolute;
        deleteConfirmOverlay.style.left = 0;
        deleteConfirmOverlay.style.top = 0;
        deleteConfirmOverlay.style.right = 0;
        deleteConfirmOverlay.style.bottom = 0;
        deleteConfirmOverlay.style.justifyContent = Justify.Center;
        deleteConfirmOverlay.style.alignItems = Align.Center;
        deleteConfirmOverlay.style.backgroundColor = new StyleColor(new Color(0f, 0f, 0f, 0.62f));
        deleteConfirmOverlay.style.display = DisplayStyle.None;

        VisualElement panel = new VisualElement();
        panel.style.width = 430;
        panel.style.paddingTop = 16;
        panel.style.paddingRight = 16;
        panel.style.paddingBottom = 16;
        panel.style.paddingLeft = 16;
        panel.style.backgroundColor = new StyleColor(new Color(0.12f, 0.1f, 0.08f, 0.98f));
        panel.style.borderTopLeftRadius = 4;
        panel.style.borderTopRightRadius = 4;
        panel.style.borderBottomLeftRadius = 4;
        panel.style.borderBottomRightRadius = 4;
        panel.style.borderTopWidth = 1;
        panel.style.borderRightWidth = 1;
        panel.style.borderBottomWidth = 1;
        panel.style.borderLeftWidth = 1;
        panel.style.borderTopColor = new StyleColor(new Color(0.95f, 0.7f, 0.45f, 0.55f));
        panel.style.borderRightColor = new StyleColor(new Color(0.95f, 0.7f, 0.45f, 0.55f));
        panel.style.borderBottomColor = new StyleColor(new Color(0f, 0f, 0f, 0.48f));
        panel.style.borderLeftColor = new StyleColor(new Color(0f, 0f, 0f, 0.48f));

        Label title = new Label("Confirm Delete");
        title.style.fontSize = 16;
        title.style.unityFontStyleAndWeight = FontStyle.Bold;
        title.style.color = new StyleColor(new Color(0.95f, 0.9f, 0.82f, 1f));
        panel.Add(title);

        deleteConfirmMessageLabel = new Label("Delete this save?");
        deleteConfirmMessageLabel.style.fontSize = 12;
        deleteConfirmMessageLabel.style.color = new StyleColor(new Color(0.87f, 0.82f, 0.77f, 0.92f));
        deleteConfirmMessageLabel.style.marginTop = 6;
        deleteConfirmMessageLabel.style.marginBottom = 12;
        panel.Add(deleteConfirmMessageLabel);

        VisualElement buttonsRow = new VisualElement();
        buttonsRow.style.flexDirection = FlexDirection.Row;

        deleteConfirmYesButton = new Button(ConfirmDeleteSaveSlot);
        deleteConfirmYesButton.text = "Delete";
        deleteConfirmYesButton.style.flexGrow = 1;
        deleteConfirmYesButton.style.height = 34;
        ApplyDeleteConfirmButtonStyle(deleteConfirmYesButton, new Color(0.6f, 0.2f, 0.2f, 0.95f), new Color(0.98f, 0.93f, 0.9f, 1f));
        buttonsRow.Add(deleteConfirmYesButton);

        deleteConfirmNoButton = new Button(HideDeleteConfirmDialog);
        deleteConfirmNoButton.text = "Cancel";
        deleteConfirmNoButton.style.flexGrow = 1;
        deleteConfirmNoButton.style.height = 34;
        deleteConfirmNoButton.style.marginLeft = 8;
        ApplyDeleteConfirmButtonStyle(deleteConfirmNoButton, new Color(0.27f, 0.29f, 0.31f, 0.95f), new Color(0.92f, 0.9f, 0.86f, 1f));
        buttonsRow.Add(deleteConfirmNoButton);

        panel.Add(buttonsRow);
        deleteConfirmOverlay.Add(panel);
        saveSlotsPanel.Add(deleteConfirmOverlay);
    }

    private void ApplyDeleteConfirmButtonStyle(Button button, Color backgroundColor, Color textColor)
    {
        if (button == null)
            return;

        button.style.fontSize = 11;
        button.style.backgroundColor = new StyleColor(backgroundColor);
        button.style.color = new StyleColor(textColor);
        button.style.borderTopLeftRadius = 3;
        button.style.borderTopRightRadius = 3;
        button.style.borderBottomLeftRadius = 3;
        button.style.borderBottomRightRadius = 3;
        button.style.borderTopWidth = 1;
        button.style.borderRightWidth = 1;
        button.style.borderBottomWidth = 1;
        button.style.borderLeftWidth = 1;
        button.style.borderTopColor = new StyleColor(new Color(0.72f, 0.45f, 0.24f, 0.3f));
        button.style.borderRightColor = new StyleColor(new Color(0.72f, 0.45f, 0.24f, 0.3f));
        button.style.borderBottomColor = new StyleColor(new Color(0f, 0f, 0f, 0.42f));
        button.style.borderLeftColor = new StyleColor(new Color(0f, 0f, 0f, 0.42f));
    }

    private void RefreshSaveSlotsUi()
    {
        int selectedSlot = GetSelectedSlotIndex();
        int count = FixedSaveSlotCount;

        for (int slotIndex = 1; slotIndex <= count; slotIndex++)
        {
            string path = GetSaveFilePath(slotIndex);
            GameSaveData data = ReadSaveData(path);
            bool exists = File.Exists(path) && data != null;
            bool corrupted = File.Exists(path) && data == null;

            Label title = root.Q<Label>($"mainmenu-slot-title-{slotIndex}");
            Label sub = root.Q<Label>($"mainmenu-slot-sub-{slotIndex}");

            if (title != null)
                title.text = corrupted ? $"Slot {slotIndex} (Corrupted)" : (exists ? data.slotName : $"Slot {slotIndex} (Empty)");

            if (sub != null)
            {
                if (!exists && !corrupted)
                    sub.text = "Empty slot - start a new game";
                else if (corrupted)
                    sub.text = "Save file could not be read";
                else
                    sub.text = $"Saved: {data.savedAtUtc} | Day: {data.totalGameDays:0.##}";
            }

            int rowIndex = slotIndex - 1;
            if (rowIndex >= 0 && rowIndex < slotRows.Count)
                RefreshSingleSlotStyle(slotRows[rowIndex], slotIndex, exists, selectedSlot);
        }
    }

    private void RefreshSingleSlotStyle(VisualElement row, int slotIndex, bool hasSave, int selectedSlot)
    {
        if (row == null)
            return;

        bool selected = slotIndex == selectedSlot;
        if (selected)
        {
            row.style.backgroundColor = new StyleColor(new Color(0.16f, 0.12f, 0.08f, 0.92f));
            row.style.borderTopColor = new StyleColor(new Color(0.95f, 0.7f, 0.45f, 0.52f));
            row.style.borderRightColor = new StyleColor(new Color(0.95f, 0.7f, 0.45f, 0.52f));
            row.style.borderBottomColor = new StyleColor(new Color(0.2f, 0.2f, 0.2f, 0.34f));
            row.style.borderLeftColor = new StyleColor(new Color(0.2f, 0.2f, 0.2f, 0.34f));
            return;
        }

        row.style.backgroundColor = new StyleColor(hasSave ? new Color(0.08f, 0.08f, 0.08f, 0.78f) : new Color(0.06f, 0.08f, 0.1f, 0.76f));
        row.style.borderTopColor = new StyleColor(new Color(0.72f, 0.45f, 0.24f, 0.2f));
        row.style.borderRightColor = new StyleColor(new Color(0.72f, 0.45f, 0.24f, 0.2f));
        row.style.borderBottomColor = new StyleColor(new Color(0.2f, 0.2f, 0.2f, 0.28f));
        row.style.borderLeftColor = new StyleColor(new Color(0.2f, 0.2f, 0.2f, 0.28f));
    }

    private int GetSelectedSlotIndex()
    {
        return Mathf.Clamp(PlayerPrefs.GetInt(PrefSelectedSlotIndex, 1), 1, FixedSaveSlotCount);
    }

    private string GetSaveFilePath(int slotIndex)
    {
        int clamped = Mathf.Clamp(slotIndex, 1, FixedSaveSlotCount);
        return Path.Combine(Application.persistentDataPath, $"{saveFilePrefix}{clamped:00}.json");
    }

    private GameSaveData ReadSaveData(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            return null;

        try
        {
            return JsonUtility.FromJson<GameSaveData>(File.ReadAllText(path));
        }
        catch
        {
            return null;
        }
    }

    private void QuitGame()
    {
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }

    private void ApplyMasterVolume(float value, bool persist)
    {
        float clamped = Mathf.Clamp01(value);
        AudioListener.volume = clamped;

        if (persist)
        {
            PlayerPrefs.SetFloat(PrefMasterVolume, clamped);
            PlayerPrefs.Save();
        }
    }

    private void ApplyFullscreen(bool value, bool persist)
    {
        int width = PlayerPrefs.GetInt(PrefResolutionWidth, Screen.width);
        int height = PlayerPrefs.GetInt(PrefResolutionHeight, Screen.height);
        ApplyResolution(width, height, value, false);

        if (persist)
        {
            PlayerPrefs.SetInt(PrefFullscreen, value ? 1 : 0);
            PlayerPrefs.Save();
        }
    }

    private void BuildResolutionOptions()
    {
        resolutionOptions.Clear();

        Resolution[] supportedResolutions = Screen.resolutions;
        for (int i = 0; i < supportedResolutions.Length; i++)
            AddResolutionOption(supportedResolutions[i].width, supportedResolutions[i].height);

        AddResolutionOption(Screen.currentResolution.width, Screen.currentResolution.height);
        AddResolutionOption(Screen.width, Screen.height);

        resolutionOptions.Sort((a, b) =>
        {
            int widthCompare = a.x.CompareTo(b.x);
            return widthCompare != 0 ? widthCompare : a.y.CompareTo(b.y);
        });

        if (resolutionDropdown != null)
        {
            List<string> choices = new List<string>();
            for (int i = 0; i < resolutionOptions.Count; i++)
                choices.Add(FormatResolution(i));

            resolutionDropdown.choices = choices;
        }
    }

    private void AddResolutionOption(int width, int height)
    {
        if (width <= 0 || height <= 0)
            return;

        for (int i = 0; i < resolutionOptions.Count; i++)
        {
            if (resolutionOptions[i].x == width && resolutionOptions[i].y == height)
                return;
        }

        resolutionOptions.Add(new Vector2Int(width, height));
    }

    private string FormatResolution(int index)
    {
        if (resolutionOptions.Count == 0)
            return $"{Screen.width} x {Screen.height}";

        int safeIndex = Mathf.Clamp(index, 0, resolutionOptions.Count - 1);
        Vector2Int resolution = resolutionOptions[safeIndex];
        return $"{resolution.x} x {resolution.y}";
    }

    private int GetClosestResolutionIndex(int width, int height)
    {
        if (resolutionOptions.Count == 0)
            return 0;

        int closestIndex = 0;
        int closestDistance = int.MaxValue;

        for (int i = 0; i < resolutionOptions.Count; i++)
        {
            int widthDelta = resolutionOptions[i].x - width;
            int heightDelta = resolutionOptions[i].y - height;
            int distance = widthDelta * widthDelta + heightDelta * heightDelta;

            if (distance < closestDistance)
            {
                closestDistance = distance;
                closestIndex = i;
            }
        }

        return closestIndex;
    }

    private void ApplyResolution(string resolutionName, bool persist)
    {
        if (string.IsNullOrWhiteSpace(resolutionName))
            return;

        int index = resolutionDropdown != null ? resolutionDropdown.choices.IndexOf(resolutionName) : -1;
        if (index < 0 || index >= resolutionOptions.Count)
            return;

        Vector2Int resolution = resolutionOptions[index];
        ApplyResolution(resolution.x, resolution.y, Screen.fullScreen, persist);
    }

    private void ApplyResolution(int width, int height, bool fullscreen, bool persist)
    {
        int closestIndex = GetClosestResolutionIndex(width, height);
        Vector2Int resolution = resolutionOptions.Count > 0
            ? resolutionOptions[closestIndex]
            : new Vector2Int(Mathf.Max(1, width), Mathf.Max(1, height));

        Screen.SetResolution(resolution.x, resolution.y, fullscreen);

        if (resolutionDropdown != null)
            resolutionDropdown.SetValueWithoutNotify(FormatResolution(closestIndex));

        if (persist)
        {
            PlayerPrefs.SetInt(PrefResolutionWidth, resolution.x);
            PlayerPrefs.SetInt(PrefResolutionHeight, resolution.y);
            PlayerPrefs.Save();
        }
    }
}
