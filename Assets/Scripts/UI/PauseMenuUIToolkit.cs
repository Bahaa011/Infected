using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

[DisallowMultipleComponent]
public class PauseMenuUIToolkit : MonoBehaviour
{
    public static bool IsPaused { get; private set; }

    private const string PrefMasterVolume = "settings.masterVolume";
    private const string PrefMouseSensitivity = "settings.mouseSensitivity";
    private const string PrefFullscreen = "settings.fullscreen";
    private const string PrefQualityIndex = "settings.qualityIndex";

    [Header("References")]
    [SerializeField] private UIDocument uiDocument;
    [SerializeField] private GameSaveManager saveManager;
    [SerializeField] private ThirdPersonController thirdPersonController;

    [Header("Defaults")]
    [SerializeField] private float defaultMasterVolume = 1f;
    [SerializeField] private float defaultMouseSensitivity = 2f;

    private VisualElement root;
    private VisualElement overlay;
    private VisualElement mainPanel;
    private VisualElement saveSlotsPanel;
    private VisualElement optionsPanel;

    private Button resumeButton;
    private Button saveButton;
    private Button saveSlotsButton;
    private Button optionsButton;
    private Button quitButton;
    private Button backButton;
    private Button saveSlotsBackButton;

    private ScrollView saveSlotsList;

    private Slider masterVolumeSlider;
    private Slider mouseSensitivitySlider;
    private Toggle fullscreenToggle;
    private DropdownField qualityDropdown;

    private Label activeSlotLabel;

    private readonly List<Button> slotSelectButtons = new List<Button>();
    private readonly List<Button> slotSaveButtons = new List<Button>();
    private readonly List<Button> slotLoadButtons = new List<Button>();
    private readonly List<Button> slotDeleteButtons = new List<Button>();

    private readonly List<PlayerInput> pausedInputs = new List<PlayerInput>();
    private readonly List<bool> pausedInputStates = new List<bool>();

    private void Awake()
    {
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
        HookUi();
        InitializeOptionsUi();

        SetVisible(false);
    }

    private void Update()
    {
        if (Keyboard.current == null)
            return;

        if (!Keyboard.current.escapeKey.wasPressedThisFrame)
            return;

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
        quitButton = root.Q<Button>("pause-quit-btn");
        backButton = root.Q<Button>("pause-options-back-btn");
        saveSlotsBackButton = root.Q<Button>("pause-save-slots-back-btn");

        saveSlotsList = root.Q<ScrollView>("pause-save-slots-list");
        activeSlotLabel = root.Q<Label>("pause-active-slot-label");

        masterVolumeSlider = root.Q<Slider>("pause-master-volume-slider");
        mouseSensitivitySlider = root.Q<Slider>("pause-mouse-sensitivity-slider");
        fullscreenToggle = root.Q<Toggle>("pause-fullscreen-toggle");
        qualityDropdown = root.Q<DropdownField>("pause-quality-dropdown");
    }

    private void HookUi()
    {
        if (resumeButton != null)
            resumeButton.clicked += ResumeGame;

        if (saveButton != null)
            saveButton.clicked += () => saveManager?.SaveGame();

        if (saveSlotsButton != null)
            saveSlotsButton.clicked += ShowSaveSlotsPanel;

        if (optionsButton != null)
            optionsButton.clicked += ShowOptionsPanel;

        if (quitButton != null)
            quitButton.clicked += QuitGame;

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

        if (qualityDropdown != null)
            qualityDropdown.RegisterValueChangedCallback(evt => ApplyQuality(evt.newValue, true));

        BuildSaveSlotsList();
    }

    private void InitializeOptionsUi()
    {
        if (qualityDropdown != null)
            qualityDropdown.choices = new List<string>(QualitySettings.names);

        float masterVolume = PlayerPrefs.GetFloat(PrefMasterVolume, Mathf.Clamp01(defaultMasterVolume));
        float mouseSensitivity = PlayerPrefs.GetFloat(PrefMouseSensitivity, Mathf.Max(0.1f, defaultMouseSensitivity));
        bool fullscreen = PlayerPrefs.GetInt(PrefFullscreen, Screen.fullScreen ? 1 : 0) == 1;

        int qualityCount = Mathf.Max(1, QualitySettings.names.Length);
        int qualityIndex = Mathf.Clamp(PlayerPrefs.GetInt(PrefQualityIndex, QualitySettings.GetQualityLevel()), 0, qualityCount - 1);

        ApplyMasterVolume(masterVolume, false);
        ApplyMouseSensitivity(mouseSensitivity, false);
        ApplyFullscreen(fullscreen, false);

        if (qualityDropdown != null)
        {
            qualityDropdown.index = qualityIndex;
            ApplyQualityByIndex(qualityIndex, false);
        }

        if (masterVolumeSlider != null)
            masterVolumeSlider.value = masterVolume;

        if (mouseSensitivitySlider != null)
            mouseSensitivitySlider.value = mouseSensitivity;

        if (fullscreenToggle != null)
            fullscreenToggle.value = fullscreen;

        RefreshSaveSlotsUi();
    }

    private void PauseGame()
    {
        IsPaused = true;
        Time.timeScale = 0f;
        AudioListener.pause = true;

        StoreAndDisablePlayerInputs();

        UnityEngine.Cursor.lockState = CursorLockMode.None;
        UnityEngine.Cursor.visible = true;

        ShowMainPanel();
        SetVisible(true);
    }

    private void ResumeGame()
    {
        IsPaused = false;
        Time.timeScale = 1f;
        AudioListener.pause = false;

        RestorePlayerInputs();

        UnityEngine.Cursor.lockState = CursorLockMode.Locked;
        UnityEngine.Cursor.visible = false;

        SetVisible(false);
    }

    private void StoreAndDisablePlayerInputs()
    {
        pausedInputs.Clear();
        pausedInputStates.Clear();

        PlayerInput[] allInputs = FindObjectsByType<PlayerInput>(FindObjectsSortMode.None);
        for (int i = 0; i < allInputs.Length; i++)
        {
            PlayerInput input = allInputs[i];
            if (input == null)
                continue;

            pausedInputs.Add(input);
            pausedInputStates.Add(input.enabled);

            if (input.enabled)
                input.enabled = false;
        }
    }

    private void RestorePlayerInputs()
    {
        for (int i = 0; i < pausedInputs.Count; i++)
        {
            PlayerInput input = pausedInputs[i];
            if (input == null)
                continue;

            bool wasEnabled = i < pausedInputStates.Count && pausedInputStates[i];
            input.enabled = wasEnabled;
        }

        pausedInputs.Clear();
        pausedInputStates.Clear();
    }

    private void SetVisible(bool visible)
    {
        if (overlay != null)
            overlay.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
    }

    private void ShowMainPanel()
    {
        if (mainPanel != null)
            mainPanel.style.display = DisplayStyle.Flex;

        if (optionsPanel != null)
            optionsPanel.style.display = DisplayStyle.None;

        if (saveSlotsPanel != null)
            saveSlotsPanel.style.display = DisplayStyle.None;
    }

    private void ShowOptionsPanel()
    {
        if (mainPanel != null)
            mainPanel.style.display = DisplayStyle.None;

        if (saveSlotsPanel != null)
            saveSlotsPanel.style.display = DisplayStyle.None;

        if (optionsPanel != null)
            optionsPanel.style.display = DisplayStyle.Flex;
    }

    private void ShowSaveSlotsPanel()
    {
        if (mainPanel != null)
            mainPanel.style.display = DisplayStyle.None;

        if (optionsPanel != null)
            optionsPanel.style.display = DisplayStyle.None;

        if (saveSlotsPanel != null)
            saveSlotsPanel.style.display = DisplayStyle.Flex;

        RefreshSaveSlotsUi();
    }

    private void BuildSaveSlotsList()
    {
        if (saveSlotsList == null || saveManager == null)
            return;

        saveSlotsList.Clear();
        slotSelectButtons.Clear();
        slotSaveButtons.Clear();
        slotLoadButtons.Clear();
        slotDeleteButtons.Clear();

        GameSaveManager.SaveSlotInfo[] infos = saveManager.GetSaveSlotInfos();
        for (int i = 0; i < infos.Length; i++)
        {
            GameSaveManager.SaveSlotInfo info = infos[i];
            if (info == null)
                continue;

            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Column;
            row.style.paddingTop = 8;
            row.style.paddingRight = 8;
            row.style.paddingBottom = 8;
            row.style.paddingLeft = 8;
            row.style.marginBottom = 8;
            row.style.backgroundColor = new StyleColor(new Color(0.08f, 0.08f, 0.08f, 0.95f));
            row.style.borderTopLeftRadius = 6;
            row.style.borderTopRightRadius = 6;
            row.style.borderBottomLeftRadius = 6;
            row.style.borderBottomRightRadius = 6;
            row.style.borderTopWidth = 1;
            row.style.borderRightWidth = 1;
            row.style.borderBottomWidth = 1;
            row.style.borderLeftWidth = 1;
            row.style.borderTopColor = new StyleColor(new Color(0.45f, 0.45f, 0.45f, 0.16f));
            row.style.borderRightColor = new StyleColor(new Color(0.45f, 0.45f, 0.45f, 0.16f));
            row.style.borderBottomColor = new StyleColor(new Color(0.45f, 0.45f, 0.45f, 0.1f));
            row.style.borderLeftColor = new StyleColor(new Color(0.45f, 0.45f, 0.45f, 0.1f));

            var title = new Label();
            title.name = $"pause-slot-title-{info.slotIndex}";
            title.style.fontSize = 13;
            title.style.unityFontStyleAndWeight = FontStyle.Bold;
            title.style.color = new StyleColor(new Color(0.96f, 0.96f, 0.96f, 1f));
            row.Add(title);

            var sub = new Label();
            sub.name = $"pause-slot-sub-{info.slotIndex}";
            sub.style.fontSize = 10;
            sub.style.color = new StyleColor(new Color(0.8f, 0.8f, 0.8f, 0.95f));
            sub.style.marginBottom = 6;
            row.Add(sub);

            var buttons = new VisualElement();
            buttons.style.flexDirection = FlexDirection.Row;
            buttons.style.gap = 6;

            Button selectButton = new Button(() => SelectSlot(info.slotIndex));
            selectButton.text = "Select";
            selectButton.style.flexGrow = 1;
            buttons.Add(selectButton);
            slotSelectButtons.Add(selectButton);

            Button saveSlotButton = new Button(() => SaveSlot(info.slotIndex));
            saveSlotButton.text = "Save";
            saveSlotButton.style.flexGrow = 1;
            buttons.Add(saveSlotButton);
            slotSaveButtons.Add(saveSlotButton);

            Button loadSlotButton = new Button(() => LoadSlot(info.slotIndex));
            loadSlotButton.text = "Load";
            loadSlotButton.style.flexGrow = 1;
            buttons.Add(loadSlotButton);
            slotLoadButtons.Add(loadSlotButton);

            Button deleteSlotButton = new Button(() => DeleteSlot(info.slotIndex));
            deleteSlotButton.text = "Delete";
            deleteSlotButton.style.flexGrow = 1;
            buttons.Add(deleteSlotButton);
            slotDeleteButtons.Add(deleteSlotButton);

            row.Add(buttons);
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

        for (int i = 0; i < slotSelectButtons.Count; i++)
        {
            if (slotSelectButtons[i] != null)
                slotSelectButtons[i].text = i + 1 == activeSlot ? "Selected" : "Select";
        }
    }

    private void SelectSlot(int slotIndex)
    {
        saveManager?.SetActiveSlot(slotIndex);
        RefreshSaveSlotsUi();
    }

    private void SaveSlot(int slotIndex)
    {
        saveManager?.SaveGameToSlot(slotIndex);
        RefreshSaveSlotsUi();
    }

    private void LoadSlot(int slotIndex)
    {
        saveManager?.LoadGameFromSlot(slotIndex);
        RefreshSaveSlotsUi();
    }

    private void DeleteSlot(int slotIndex)
    {
        saveManager?.DeleteSlot(slotIndex);
        RefreshSaveSlotsUi();
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
        Screen.fullScreen = enabled;

        if (persist)
        {
            PlayerPrefs.SetInt(PrefFullscreen, enabled ? 1 : 0);
            PlayerPrefs.Save();
        }
    }

    private void ApplyQuality(string qualityName, bool persist)
    {
        if (string.IsNullOrWhiteSpace(qualityName))
            return;

        int index = -1;
        string[] qualityNames = QualitySettings.names;
        for (int i = 0; i < qualityNames.Length; i++)
        {
            if (qualityNames[i] == qualityName)
            {
                index = i;
                break;
            }
        }

        if (index >= 0)
            ApplyQualityByIndex(index, persist);
    }

    private void ApplyQualityByIndex(int index, bool persist)
    {
        int safeIndex = Mathf.Clamp(index, 0, Mathf.Max(0, QualitySettings.names.Length - 1));
        QualitySettings.SetQualityLevel(safeIndex, true);

        if (qualityDropdown != null)
            qualityDropdown.index = safeIndex;

        if (persist)
        {
            PlayerPrefs.SetInt(PrefQualityIndex, safeIndex);
            PlayerPrefs.Save();
        }
    }

    private static void QuitGame()
    {
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }
}
