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
    private const string PrefMasterVolume = "settings.masterVolume";
    private const string PrefFullscreen = "settings.fullscreen";
    private const string PrefQualityIndex = "settings.qualityIndex";

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
    private DropdownField qualityDropdown;

    private readonly List<VisualElement> slotRows = new List<VisualElement>();
    private readonly List<int> slotRowIndexes = new List<int>();

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
        qualityDropdown = root.Q<DropdownField>("mainmenu-quality-dropdown");

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

        resourceTexture = Resources.Load<Texture2D>("Art/MainBG");
        if (resourceTexture != null)
            return resourceTexture;

#if UNITY_EDITOR
        Texture2D assetTexture = UnityEditor.AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Art/MainBG.png");
        if (assetTexture != null)
        {
            mainMenuBackgroundTexture = assetTexture;
            return assetTexture;
        }
#endif

        Debug.LogWarning("[MainMenuUIToolkit] Could not find main menu background texture. Assign it in the inspector.");
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

        if (qualityDropdown != null)
            qualityDropdown.RegisterValueChangedCallback(evt => ApplyQuality(evt.newValue, true));

        ConfigureMainButtonHover(playButton);
        ConfigureMainButtonHover(optionsButton);
        ConfigureMainButtonHover(quitButton);
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

        Color normalBackground = new Color(0f, 0f, 0f, 0.2f);
        Color hoverBackground = new Color(1f, 1f, 1f, 0.16f);
        Color normalText = new Color(250f / 255f, 250f / 255f, 250f / 255f, 0.95f);
        Color hoverText = Color.white;
        Color normalBorder = new Color(230f / 255f, 230f / 255f, 230f / 255f, 0.55f);
        Color hoverBorder = new Color(1f, 1f, 1f, 0.98f);

        float targetScale = pressed ? buttonPressedScale : Mathf.Lerp(1f, buttonHoverScale, hoverT);

        button.style.backgroundColor = Color.Lerp(normalBackground, hoverBackground, hoverT);
        button.style.color = Color.Lerp(normalText, hoverText, hoverT);
        button.style.borderTopColor = Color.Lerp(normalBorder, hoverBorder, hoverT);
        button.style.borderBottomColor = Color.Lerp(normalBorder, hoverBorder, hoverT);
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
        if (qualityDropdown != null)
            qualityDropdown.choices = new List<string>(QualitySettings.names);

        float masterVolume = PlayerPrefs.GetFloat(PrefMasterVolume, Mathf.Clamp01(defaultMasterVolume));
        bool fullscreen = PlayerPrefs.GetInt(PrefFullscreen, Screen.fullScreen ? 1 : 0) == 1;

        int qualityCount = Mathf.Max(1, QualitySettings.names.Length);
        int qualityIndex = Mathf.Clamp(PlayerPrefs.GetInt(PrefQualityIndex, QualitySettings.GetQualityLevel()), 0, qualityCount - 1);

        ApplyMasterVolume(masterVolume, false);
        ApplyFullscreen(fullscreen, false);
        ApplyQualityByIndex(qualityIndex, false);

        if (masterVolumeSlider != null)
            masterVolumeSlider.value = masterVolume;

        if (fullscreenToggle != null)
            fullscreenToggle.value = fullscreen;

        if (qualityDropdown != null)
            qualityDropdown.index = qualityIndex;

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
        slotIndex = Mathf.Clamp(slotIndex, 1, Mathf.Max(1, saveSlotCount));

        PlayerPrefs.SetInt(PrefSelectedSlotIndex, slotIndex);
        PlayerPrefs.Save();

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

    private void StartWorldLoad(int slotIndex)
    {
        if (isLoadingWorld)
            return;

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

        int count = Mathf.Max(1, saveSlotCount);
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
            deleteBtn.clicked += () => DeleteSaveSlot(deleteSlot);
            
            buttonContainer.Add(deleteBtn);
            row.Add(buttonContainer);

            slotRows.Add(row);
            slotRowIndexes.Add(slotIndex);
            saveSlotsList.Add(row);
        }
    }

    private void RefreshSaveSlotsUi()
    {
        int selectedSlot = GetSelectedSlotIndex();
        int count = Mathf.Max(1, saveSlotCount);

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
        return Mathf.Clamp(PlayerPrefs.GetInt(PrefSelectedSlotIndex, 1), 1, Mathf.Max(1, saveSlotCount));
    }

    private string GetSaveFilePath(int slotIndex)
    {
        int clamped = Mathf.Clamp(slotIndex, 1, Mathf.Max(1, saveSlotCount));
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
        Screen.fullScreen = value;

        if (persist)
        {
            PlayerPrefs.SetInt(PrefFullscreen, value ? 1 : 0);
            PlayerPrefs.Save();
        }
    }

    private void ApplyQuality(string qualityName, bool persist)
    {
        if (string.IsNullOrWhiteSpace(qualityName))
            return;

        int index = System.Array.IndexOf(QualitySettings.names, qualityName);
        if (index < 0)
            return;

        ApplyQualityByIndex(index, persist);
    }

    private void ApplyQualityByIndex(int index, bool persist)
    {
        int qualityCount = Mathf.Max(1, QualitySettings.names.Length);
        int clamped = Mathf.Clamp(index, 0, qualityCount - 1);
        QualitySettings.SetQualityLevel(clamped, true);

        if (persist)
        {
            PlayerPrefs.SetInt(PrefQualityIndex, clamped);
            PlayerPrefs.Save();
        }
    }
}
