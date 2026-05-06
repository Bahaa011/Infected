using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;

[DisallowMultipleComponent]
public class PauseMenuUIToolkit : MonoBehaviour
{
    public static bool IsPaused { get; private set; }

    private const string PrefMasterVolume = "settings.masterVolume";
    private const string PrefMouseSensitivity = "settings.mouseSensitivity";
    private const string PrefFullscreen = "settings.fullscreen";
    private const string PrefResolutionWidth = "settings.resolutionWidth";
    private const string PrefResolutionHeight = "settings.resolutionHeight";

    [Header("References")]
    [SerializeField] private UIDocument uiDocument;
    [SerializeField] private GameSaveManager saveManager;
    [SerializeField] private ThirdPersonController thirdPersonController;

    [Header("Defaults")]
    [SerializeField] private float defaultMasterVolume = 1f;
    [SerializeField] private float defaultMouseSensitivity = 2f;
    [SerializeField] private string mainMenuSceneName = "Main Menu";

    private VisualElement root;
    private VisualElement overlay;
    private VisualElement mainPanel;
    private VisualElement saveSlotsPanel;
    private VisualElement optionsPanel;

    private Button resumeButton;
    private Button saveButton;
    private Button saveSlotsButton;
    private Button optionsButton;
    private Button mainMenuButton;
    private Button backButton;
    private Button saveSlotsBackButton;

    private ScrollView saveSlotsList;

    private Slider masterVolumeSlider;
    private Slider mouseSensitivitySlider;
    private Toggle fullscreenToggle;
    private DropdownField resolutionDropdown;

    private Label activeSlotLabel;

    private readonly List<VisualElement> slotRows = new List<VisualElement>();
    private readonly List<int> slotRowIndexes = new List<int>();
    private readonly List<Vector2Int> resolutionOptions = new List<Vector2Int>();

    private VisualElement saveConfirmOverlay;
    private Label saveConfirmMessageLabel;
    private Button saveConfirmYesButton;
    private Button saveConfirmNoButton;
    private int pendingSaveSlotIndex = -1;

    private readonly Dictionary<UIDocument, PickingMode> suppressedOtherDocumentPickingModes = new Dictionary<UIDocument, PickingMode>();
    private float originalDocumentSortingOrder;
    private bool hasCapturedDocumentSortingOrder;

    private void Awake()
    {
        InventoryUIToolkit.EnsureRuntimeUIInput();

        if (uiDocument == null)
            uiDocument = GetComponent<UIDocument>();

        if (saveManager == null)
            saveManager = FindAnyObjectByType<GameSaveManager>();

        if (thirdPersonController == null)
            thirdPersonController = FindAnyObjectByType<ThirdPersonController>();

        if (uiDocument == null)
        {
            Debug.LogWarning("[PauseMenuUIToolkit] UIDocument is missing.");
            enabled = false;
            return;
        }

        root = uiDocument.rootVisualElement;
        CacheUi();
        ConfigureOverlayPicking();
        HookUi();
        InitializeOptionsUi();

        SetVisible(false);
    }

    private void Update()
    {
        if (IsPaused)
        {
            if (UnityEngine.Cursor.lockState != CursorLockMode.None)
                UnityEngine.Cursor.lockState = CursorLockMode.None;
            if (!UnityEngine.Cursor.visible)
                UnityEngine.Cursor.visible = true;
        }

        if (Keyboard.current == null)
            return;

        if (!Keyboard.current.escapeKey.wasPressedThisFrame)
            return;

        if (saveConfirmOverlay != null && saveConfirmOverlay.style.display == DisplayStyle.Flex)
        {
            HideSaveConfirmDialog();
            return;
        }

        if (!IsPaused)
        {
            PauseGame();
            return;
        }

        if ((optionsPanel != null && optionsPanel.style.display == DisplayStyle.Flex)
            || (saveSlotsPanel != null && saveSlotsPanel.style.display == DisplayStyle.Flex))
        {
            ShowMainPanel();
            return;
        }

        ResumeGame();
    }

    private void OnDestroy()
    {
        if (IsPaused)
            ResumeGame();
    }

    private void CacheUi()
    {
        if (root == null)
            return;

        overlay = root.Q<VisualElement>("pause-overlay");
        mainPanel = root.Q<VisualElement>("pause-main-panel");
        saveSlotsPanel = root.Q<VisualElement>("pause-save-slots-panel");
        optionsPanel = root.Q<VisualElement>("pause-options-panel");

        resumeButton = root.Q<Button>("pause-resume-btn");
        saveButton = root.Q<Button>("pause-save-btn");
        saveSlotsButton = root.Q<Button>("pause-save-slots-btn");
        optionsButton = root.Q<Button>("pause-options-btn");
        mainMenuButton = root.Q<Button>("pause-main-menu-btn");
        backButton = root.Q<Button>("pause-options-back-btn");
        saveSlotsBackButton = root.Q<Button>("pause-save-slots-back-btn");

        saveSlotsList = root.Q<ScrollView>("pause-save-slots-list");
        activeSlotLabel = root.Q<Label>("pause-active-slot-label");

        masterVolumeSlider = root.Q<Slider>("pause-master-volume-slider");
        mouseSensitivitySlider = root.Q<Slider>("pause-mouse-sensitivity-slider");
        fullscreenToggle = root.Q<Toggle>("pause-fullscreen-toggle");
        resolutionDropdown = root.Q<DropdownField>("pause-resolution-dropdown");
    }

    private void HookUi()
    {
        if (resumeButton != null)
            resumeButton.clicked += ResumeGame;

        if (saveButton != null)
            saveButton.clicked += SaveCurrentSlot;

        if (saveSlotsButton != null)
        {
            saveSlotsButton.text = "Save";
            saveSlotsButton.clicked += SaveCurrentSlot;
        }

        if (optionsButton != null)
        {
            optionsButton.clicked += ShowOptionsPanel;
            optionsButton.RegisterCallback<ClickEvent>(HandleOptionsButtonClick);
        }

        if (mainMenuButton != null)
            mainMenuButton.clicked += QuitToMainMenu;

        if (backButton != null)
            backButton.clicked += ShowMainPanel;

        if (saveSlotsBackButton != null)
            saveSlotsBackButton.clicked += ShowMainPanel;

        if (masterVolumeSlider != null)
            masterVolumeSlider.RegisterValueChangedCallback(evt => ApplyMasterVolume(evt.newValue, true));

        if (mouseSensitivitySlider != null)
            mouseSensitivitySlider.RegisterValueChangedCallback(evt => ApplyMouseSensitivity(evt.newValue, true));

        if (fullscreenToggle != null)
            fullscreenToggle.RegisterValueChangedCallback(evt => ApplyFullscreen(evt.newValue, true));

        if (resolutionDropdown != null)
            resolutionDropdown.RegisterValueChangedCallback(evt => ApplyResolution(evt.newValue, true));

        StyleSaveSlotsScrollBar();
        BuildSaveConfirmDialog();
        BuildSaveSlotsList();
    }

    private void ConfigureOverlayPicking()
    {
        if (overlay == null)
            return;

        for (int i = 0; i < overlay.childCount; i++)
        {
            VisualElement child = overlay[i];
            if (child == mainPanel || child == saveSlotsPanel || child == optionsPanel)
                continue;

            child.pickingMode = PickingMode.Ignore;
        }
    }

    private void HandleOptionsButtonClick(ClickEvent evt)
    {
        ShowOptionsPanel();
        evt.StopPropagation();
    }

    private void SaveCurrentSlot()
    {
        if (saveManager == null)
            saveManager = FindRuntimeObject<GameSaveManager>();

        if (saveManager == null)
        {
            Debug.LogWarning("[PauseMenuUIToolkit] Save button pressed, but no GameSaveManager was found in the loaded scenes.");
            return;
        }

        if (!saveManager.TrySaveGame())
            Debug.LogWarning("[PauseMenuUIToolkit] Save button pressed, but GameSaveManager could not write the save. Check the player log for the GameSaveManager warning/error above this one.");

        RefreshSaveSlotsUi();
    }

    private void InitializeOptionsUi()
    {
        BuildResolutionOptions();

        float masterVolume = PlayerPrefs.GetFloat(PrefMasterVolume, Mathf.Clamp01(defaultMasterVolume));
        float mouseSensitivity = PlayerPrefs.GetFloat(PrefMouseSensitivity, Mathf.Max(0.1f, defaultMouseSensitivity));
        bool fullscreen = PlayerPrefs.GetInt(PrefFullscreen, Screen.fullScreen ? 1 : 0) == 1;
        int resolutionWidth = PlayerPrefs.GetInt(PrefResolutionWidth, Screen.width);
        int resolutionHeight = PlayerPrefs.GetInt(PrefResolutionHeight, Screen.height);

        ApplyMasterVolume(masterVolume, false);
        ApplyMouseSensitivity(mouseSensitivity, false);
        ApplyResolution(resolutionWidth, resolutionHeight, fullscreen, false);

        if (masterVolumeSlider != null)
            masterVolumeSlider.value = masterVolume;

        if (mouseSensitivitySlider != null)
            mouseSensitivitySlider.value = mouseSensitivity;

        if (fullscreenToggle != null)
            fullscreenToggle.value = fullscreen;

        if (resolutionDropdown != null)
            resolutionDropdown.SetValueWithoutNotify(FormatResolution(GetClosestResolutionIndex(resolutionWidth, resolutionHeight)));

        RefreshSaveSlotsUi();
    }

    private void PauseGame()
    {
        CloseInventoryIfOpen();

        IsPaused = true;
        Time.timeScale = 0f;
        AudioListener.pause = true;

        UnityEngine.Cursor.lockState = CursorLockMode.None;
        UnityEngine.Cursor.visible = true;

        ShowMainPanel();
        SetVisible(true);
        SetPauseDocumentPriority(true);
        SuppressOtherUIDocumentPicking(true);
    }

    private void ResumeGame()
    {
        IsPaused = false;
        Time.timeScale = 1f;
        AudioListener.pause = false;

        UnityEngine.Cursor.lockState = CursorLockMode.Locked;
        UnityEngine.Cursor.visible = false;

        SuppressOtherUIDocumentPicking(false);
        SetPauseDocumentPriority(false);
        SetVisible(false);
    }

    private void SetVisible(bool visible)
    {
        if (overlay != null)
        {
            overlay.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
            overlay.pickingMode = visible ? PickingMode.Position : PickingMode.Ignore;
        }

        if (root != null)
            root.pickingMode = visible ? PickingMode.Position : PickingMode.Ignore;
    }

    private void ShowMainPanel()
    {
        if (mainPanel != null)
        {
            mainPanel.style.display = DisplayStyle.Flex;
            mainPanel.BringToFront();
            mainPanel.pickingMode = PickingMode.Position;
        }

        if (optionsPanel != null)
        {
            optionsPanel.style.display = DisplayStyle.None;
            optionsPanel.pickingMode = PickingMode.Ignore;
        }

        if (saveSlotsPanel != null)
        {
            saveSlotsPanel.style.display = DisplayStyle.None;
            saveSlotsPanel.pickingMode = PickingMode.Ignore;
        }
    }

    private void ShowOptionsPanel()
    {
        if (mainPanel != null)
        {
            mainPanel.style.display = DisplayStyle.None;
            mainPanel.pickingMode = PickingMode.Ignore;
        }

        if (saveSlotsPanel != null)
        {
            saveSlotsPanel.style.display = DisplayStyle.None;
            saveSlotsPanel.pickingMode = PickingMode.Ignore;
        }

        if (optionsPanel != null)
        {
            optionsPanel.style.display = DisplayStyle.Flex;
            optionsPanel.BringToFront();
            optionsPanel.pickingMode = PickingMode.Position;
        }
    }

    private void ShowSaveSlotsPanel()
    {
        if (mainPanel != null)
        {
            mainPanel.style.display = DisplayStyle.None;
            mainPanel.pickingMode = PickingMode.Ignore;
        }

        if (optionsPanel != null)
        {
            optionsPanel.style.display = DisplayStyle.None;
            optionsPanel.pickingMode = PickingMode.Ignore;
        }

        if (saveSlotsPanel != null)
        {
            saveSlotsPanel.style.display = DisplayStyle.Flex;
            saveSlotsPanel.BringToFront();
            saveSlotsPanel.pickingMode = PickingMode.Position;
        }

        StyleSaveSlotsScrollBar();
        RefreshSaveSlotsUi();
    }

    private void BuildSaveSlotsList()
    {
        if (saveSlotsList == null || saveManager == null)
            return;

        saveSlotsList.Clear();
        slotRows.Clear();
        slotRowIndexes.Clear();

        GameSaveManager.SaveSlotInfo[] infos = saveManager.GetSaveSlotInfos();
        for (int i = 0; i < infos.Length; i++)
        {
            GameSaveManager.SaveSlotInfo info = infos[i];
            if (info == null)
                continue;

            var row = new VisualElement();
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
            row.RegisterCallback<ClickEvent>(evt =>
            {
                if (evt.target is Button)
                    return;

                SaveSlot(info.slotIndex);
            });

            var title = new Label();
            title.name = $"pause-slot-title-{info.slotIndex}";
            title.style.fontSize = ResponsiveUiUtility.Scale(11f);
            title.style.unityFontStyleAndWeight = FontStyle.Bold;
            title.style.color = new StyleColor(new Color(0.9f, 0.9f, 0.9f, 0.95f));
            row.Add(title);

            var sub = new Label();
            sub.name = $"pause-slot-sub-{info.slotIndex}";
            sub.style.fontSize = ResponsiveUiUtility.Scale(10f);
            sub.style.color = new StyleColor(new Color(0.72f, 0.72f, 0.72f, 0.84f));
            row.Add(sub);

            slotRows.Add(row);
            slotRowIndexes.Add(info.slotIndex);
            saveSlotsList.Add(row);
        }

        RefreshSaveSlotsUi();
    }

    private void RefreshSaveSlotsUi()
    {
        if (saveManager != null && activeSlotLabel != null)
            activeSlotLabel.text = $"Current Slot: {saveManager.ActiveSlotIndex}";

        if (saveManager == null)
            return;

        GameSaveManager.SaveSlotInfo[] infos = saveManager.GetSaveSlotInfos();
        for (int i = 0; i < infos.Length; i++)
        {
            GameSaveManager.SaveSlotInfo info = infos[i];
            if (info == null)
                continue;

            Label title = root.Q<Label>($"pause-slot-title-{info.slotIndex}");
            Label sub = root.Q<Label>($"pause-slot-sub-{info.slotIndex}");

            if (title != null)
                title.text = info.corrupted ? $"Slot {info.slotIndex} (Corrupted)" : (info.exists ? info.slotName : $"Slot {info.slotIndex} (Empty)");

            if (sub != null)
            {
                if (!info.exists)
                    sub.text = "No save present";
                else if (info.corrupted)
                    sub.text = "Save file could not be read";
                else
                    sub.text = $"Saved: {info.savedAtUtc} | Day: {info.totalGameDays:0.##}";
            }

        }

        StyleSlotButtons();
    }

    private void StyleSlotButtons()
    {
        if (saveManager == null)
            return;

        int activeSlot = saveManager.ActiveSlotIndex;

        for (int i = 0; i < slotRows.Count; i++)
        {
            if (slotRows[i] != null)
            {
                int slotIndex = i < slotRowIndexes.Count ? slotRowIndexes[i] : i + 1;
                bool isSelected = slotIndex == activeSlot;

                if (isSelected)
                {
                    slotRows[i].style.backgroundColor = new StyleColor(new Color(0.16f, 0.12f, 0.08f, 0.92f));
                    slotRows[i].style.borderTopColor = new StyleColor(new Color(0.95f, 0.7f, 0.45f, 0.52f));
                    slotRows[i].style.borderRightColor = new StyleColor(new Color(0.95f, 0.7f, 0.45f, 0.52f));
                    slotRows[i].style.borderBottomColor = new StyleColor(new Color(0.2f, 0.2f, 0.2f, 0.34f));
                    slotRows[i].style.borderLeftColor = new StyleColor(new Color(0.2f, 0.2f, 0.2f, 0.34f));
                }
                else
                {
                    slotRows[i].style.backgroundColor = new StyleColor(new Color(0.08f, 0.08f, 0.08f, 0.78f));
                    slotRows[i].style.borderTopColor = new StyleColor(new Color(0.72f, 0.45f, 0.24f, 0.2f));
                    slotRows[i].style.borderRightColor = new StyleColor(new Color(0.72f, 0.45f, 0.24f, 0.2f));
                    slotRows[i].style.borderBottomColor = new StyleColor(new Color(0.2f, 0.2f, 0.2f, 0.28f));
                    slotRows[i].style.borderLeftColor = new StyleColor(new Color(0.2f, 0.2f, 0.2f, 0.28f));
                }
            }
        }
    }

    private void StyleSaveSlotsScrollBar()
    {
        if (saveSlotsList == null)
            return;

        var scroller = saveSlotsList.verticalScroller;
        if (scroller == null)
            return;

        scroller.style.width = ResponsiveUiUtility.Scale(10f);
        scroller.style.minWidth = ResponsiveUiUtility.Scale(10f);
        scroller.style.marginLeft = ResponsiveUiUtility.Scale(8f);
        scroller.style.backgroundColor = new StyleColor(new Color(0f, 0f, 0f, 0f));

        VisualElement lowButton = scroller.Q<VisualElement>(className: "unity-scroller__low-button");
        if (lowButton != null)
            lowButton.style.display = DisplayStyle.None;

        VisualElement highButton = scroller.Q<VisualElement>(className: "unity-scroller__high-button");
        if (highButton != null)
            highButton.style.display = DisplayStyle.None;

        VisualElement tracker = scroller.Q<VisualElement>(className: "unity-base-slider__tracker");
        if (tracker != null)
        {
            tracker.style.backgroundColor = new StyleColor(new Color(0.1f, 0.1f, 0.1f, 0.85f));
            tracker.style.borderTopLeftRadius = 5;
            tracker.style.borderTopRightRadius = 5;
            tracker.style.borderBottomLeftRadius = 5;
            tracker.style.borderBottomRightRadius = 5;
        }

        VisualElement dragger = scroller.Q<VisualElement>(className: "unity-base-slider__dragger");
        if (dragger != null)
        {
            dragger.style.backgroundColor = new StyleColor(new Color(0.72f, 0.45f, 0.24f, 0.92f));
            dragger.style.borderTopLeftRadius = 5;
            dragger.style.borderTopRightRadius = 5;
            dragger.style.borderBottomLeftRadius = 5;
            dragger.style.borderBottomRightRadius = 5;
            dragger.style.minHeight = 36;
        }
    }

    private void ApplySlotButtonBaseStyle(Button button, Color backgroundColor, Color textColor)
    {
        if (button == null)
            return;

        button.style.fontSize = ResponsiveUiUtility.Scale(11f);
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

    private void SaveSlot(int slotIndex)
    {
        GameSaveManager.SaveSlotInfo info = saveManager != null ? saveManager.GetSlotInfo(slotIndex) : null;
        bool hasData = info != null && info.exists && !info.corrupted;
        ShowSaveConfirmDialog(slotIndex, hasData);
    }

    private void ConfirmSaveToSlot()
    {
        if (pendingSaveSlotIndex <= 0)
            return;

        saveManager?.SetActiveSlot(pendingSaveSlotIndex);
        if (saveManager == null || !saveManager.SaveGameToSlot(pendingSaveSlotIndex))
            Debug.LogWarning($"[PauseMenuUIToolkit] Could not save to slot {pendingSaveSlotIndex}.");

        pendingSaveSlotIndex = -1;
        HideSaveConfirmDialog();
        RefreshSaveSlotsUi();
    }

    private void CloseInventoryIfOpen()
    {
        if (!InventoryUIToolkit.IsInventoryOpen)
            return;

        InventoryUIToolkit inventoryUi = FindAnyObjectByType<InventoryUIToolkit>();
        if (inventoryUi != null)
            inventoryUi.CloseFromExternal();
    }

    private void SetPauseDocumentPriority(bool pauseOpen)
    {
        if (uiDocument == null)
            return;

        if (pauseOpen)
        {
            if (!hasCapturedDocumentSortingOrder)
            {
                originalDocumentSortingOrder = uiDocument.sortingOrder;
                hasCapturedDocumentSortingOrder = true;
            }

            uiDocument.sortingOrder = Mathf.Max(uiDocument.sortingOrder, 10000);
            root?.BringToFront();
            overlay?.BringToFront();
        }
        else if (hasCapturedDocumentSortingOrder)
        {
            uiDocument.sortingOrder = originalDocumentSortingOrder;
        }
    }

    private void SuppressOtherUIDocumentPicking(bool suppress)
    {
        if (suppress)
        {
            suppressedOtherDocumentPickingModes.Clear();
            UIDocument[] allDocuments = FindObjectsByType<UIDocument>(FindObjectsSortMode.None);
            for (int i = 0; i < allDocuments.Length; i++)
            {
                UIDocument doc = allDocuments[i];
                if (doc == null || doc == uiDocument)
                    continue;

                VisualElement documentRoot = doc.rootVisualElement;
                if (documentRoot == null)
                    continue;

                suppressedOtherDocumentPickingModes[doc] = documentRoot.pickingMode;
                documentRoot.pickingMode = PickingMode.Ignore;
            }

            return;
        }

        foreach (var kvp in suppressedOtherDocumentPickingModes)
        {
            if (kvp.Key == null)
                continue;

            VisualElement documentRoot = kvp.Key.rootVisualElement;
            if (documentRoot == null)
                continue;

            documentRoot.pickingMode = kvp.Value;
        }

        suppressedOtherDocumentPickingModes.Clear();
    }

    private static T FindRuntimeObject<T>() where T : UnityEngine.Object
    {
        T[] objects = FindObjectsByType<T>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        for (int i = 0; i < objects.Length; i++)
        {
            T obj = objects[i];
            if (obj == null)
                continue;

            if (obj is Component component && component.gameObject.scene.IsValid())
                return obj;

            if (obj is GameObject gameObject && gameObject.scene.IsValid())
                return obj;
        }

        return null;
    }

    private void ShowSaveConfirmDialog(int slotIndex, bool hasExistingSave)
    {
        pendingSaveSlotIndex = slotIndex;

        if (saveConfirmMessageLabel != null)
        {
            saveConfirmMessageLabel.text = hasExistingSave
                ? $"Slot {slotIndex} already has a save. Override it?"
                : $"Save your current progress to Slot {slotIndex}?";
        }

        if (saveConfirmOverlay != null)
            saveConfirmOverlay.style.display = DisplayStyle.Flex;
    }

    private void HideSaveConfirmDialog()
    {
        pendingSaveSlotIndex = -1;

        if (saveConfirmOverlay != null)
            saveConfirmOverlay.style.display = DisplayStyle.None;
    }

    private void BuildSaveConfirmDialog()
    {
        if (overlay == null || saveConfirmOverlay != null)
            return;

        saveConfirmOverlay = new VisualElement();
        saveConfirmOverlay.style.position = Position.Absolute;
        saveConfirmOverlay.style.left = 0;
        saveConfirmOverlay.style.top = 0;
        saveConfirmOverlay.style.right = 0;
        saveConfirmOverlay.style.bottom = 0;
        saveConfirmOverlay.style.justifyContent = Justify.Center;
        saveConfirmOverlay.style.alignItems = Align.Center;
        saveConfirmOverlay.style.backgroundColor = new StyleColor(new Color(0f, 0f, 0f, 0.6f));
        saveConfirmOverlay.style.display = DisplayStyle.None;

        VisualElement panel = new VisualElement();
        panel.style.width = ResponsiveUiUtility.Scale(430f);
        panel.style.paddingTop = ResponsiveUiUtility.Scale(16f);
        panel.style.paddingRight = ResponsiveUiUtility.Scale(16f);
        panel.style.paddingBottom = ResponsiveUiUtility.Scale(16f);
        panel.style.paddingLeft = ResponsiveUiUtility.Scale(16f);
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

        Label title = new Label("Confirm Save");
        title.style.fontSize = ResponsiveUiUtility.Scale(16f);
        title.style.unityFontStyleAndWeight = FontStyle.Bold;
        title.style.color = new StyleColor(new Color(0.95f, 0.9f, 0.82f, 1f));
        panel.Add(title);

        saveConfirmMessageLabel = new Label("Save to this slot?");
        saveConfirmMessageLabel.style.fontSize = ResponsiveUiUtility.Scale(12f);
        saveConfirmMessageLabel.style.color = new StyleColor(new Color(0.87f, 0.82f, 0.77f, 0.92f));
        saveConfirmMessageLabel.style.marginTop = 6;
        saveConfirmMessageLabel.style.marginBottom = 12;
        panel.Add(saveConfirmMessageLabel);

        VisualElement buttonsRow = new VisualElement();
        buttonsRow.style.flexDirection = FlexDirection.Row;

        saveConfirmYesButton = new Button(ConfirmSaveToSlot);
        saveConfirmYesButton.text = "Yes";
        saveConfirmYesButton.style.flexGrow = 1;
        saveConfirmYesButton.style.height = ResponsiveUiUtility.Scale(34f);
        ApplySlotButtonBaseStyle(saveConfirmYesButton, new Color(0.47f, 0.3f, 0.16f, 0.95f), new Color(0.98f, 0.93f, 0.86f, 1f));
        buttonsRow.Add(saveConfirmYesButton);

        saveConfirmNoButton = new Button(HideSaveConfirmDialog);
        saveConfirmNoButton.text = "No";
        saveConfirmNoButton.style.flexGrow = 1;
        saveConfirmNoButton.style.height = ResponsiveUiUtility.Scale(34f);
        saveConfirmNoButton.style.marginLeft = 8;
        ApplySlotButtonBaseStyle(saveConfirmNoButton, new Color(0.27f, 0.29f, 0.31f, 0.95f), new Color(0.92f, 0.9f, 0.86f, 1f));
        buttonsRow.Add(saveConfirmNoButton);

        panel.Add(buttonsRow);
        saveConfirmOverlay.Add(panel);
        overlay.Add(saveConfirmOverlay);
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

    private void ApplyMouseSensitivity(float value, bool persist)
    {
        float clamped = Mathf.Clamp(value, 0.1f, 20f);

        if (thirdPersonController == null)
            thirdPersonController = FindAnyObjectByType<ThirdPersonController>();

        if (thirdPersonController != null)
            thirdPersonController.SetCameraSensitivity(clamped);

        if (persist)
        {
            PlayerPrefs.SetFloat(PrefMouseSensitivity, clamped);
            PlayerPrefs.Save();
        }
    }

    private void ApplyFullscreen(bool enabled, bool persist)
    {
        int width = PlayerPrefs.GetInt(PrefResolutionWidth, Screen.width);
        int height = PlayerPrefs.GetInt(PrefResolutionHeight, Screen.height);
        ApplyResolution(width, height, enabled, false);

        if (persist)
        {
            PlayerPrefs.SetInt(PrefFullscreen, enabled ? 1 : 0);
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

    private void QuitToMainMenu()
    {
        SaveCurrentSlot();

        IsPaused = false;
        Time.timeScale = 1f;
        AudioListener.pause = false;

        UnityEngine.Cursor.lockState = CursorLockMode.None;
        UnityEngine.Cursor.visible = true;

        SuppressOtherUIDocumentPicking(false);
        SetPauseDocumentPriority(false);
        SetVisible(false);
        LoadMainMenuScene();
    }

    private void LoadMainMenuScene()
    {
        string[] sceneNames =
        {
            mainMenuSceneName,
            "Main Menu",
            "MainMenu"
        };

        for (int i = 0; i < sceneNames.Length; i++)
        {
            string sceneName = sceneNames[i];
            if (string.IsNullOrWhiteSpace(sceneName) || !Application.CanStreamedLevelBeLoaded(sceneName))
                continue;

            SceneManager.LoadScene(sceneName, LoadSceneMode.Single);
            return;
        }

        Debug.LogError($"[PauseMenuUIToolkit] Could not load main menu scene. Tried: '{mainMenuSceneName}', 'Main Menu', 'MainMenu'. Add the menu scene to Build Settings or set PauseMenuUIToolkit.mainMenuSceneName correctly.");
    }
}
