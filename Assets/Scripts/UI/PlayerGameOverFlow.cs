using System.IO;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public class PlayerGameOverFlow : MonoBehaviour
{
    private const string PrefSelectedSlotIndex = "save.selectedSlotIndex";

    private static PlayerGameOverFlow instance;

    [SerializeField] private string mainMenuSceneName = "MainMenu";
    [SerializeField] private string saveFilePrefix = "save_slot_";

    public static void Show(string menuSceneName, string savePrefix = "save_slot_")
    {
        if (instance != null)
            return;

        GameObject go = new GameObject("PlayerGameOverFlow");
        instance = go.AddComponent<PlayerGameOverFlow>();
        instance.mainMenuSceneName = string.IsNullOrWhiteSpace(menuSceneName) ? "MainMenu" : menuSceneName;
        instance.saveFilePrefix = string.IsNullOrWhiteSpace(savePrefix) ? "save_slot_" : savePrefix;
        DontDestroyOnLoad(go);

        instance.BuildUi();
        instance.EnsureEventSystem();
        instance.ApplyInputStateForMenu();
    }

    private void BuildUi()
    {
        Canvas canvas = new GameObject("GameOverCanvas").AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 5000;
        canvas.transform.SetParent(transform, false);

        CanvasScaler scaler = canvas.gameObject.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.matchWidthOrHeight = 0.5f;

        canvas.gameObject.AddComponent<GraphicRaycaster>();

        Font font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        if (font == null)
            font = Resources.GetBuiltinResource<Font>("Arial.ttf");

        GameObject panelObj = new GameObject("GameOverPanel");
        panelObj.transform.SetParent(canvas.transform, false);
        Image panelImage = panelObj.AddComponent<Image>();
        panelImage.color = new Color(0f, 0f, 0f, 0.88f);

        RectTransform panelRect = panelObj.GetComponent<RectTransform>();
        panelRect.anchorMin = Vector2.zero;
        panelRect.anchorMax = Vector2.one;
        panelRect.offsetMin = Vector2.zero;
        panelRect.offsetMax = Vector2.zero;

        GameObject titleObj = new GameObject("GameOverTitle");
        titleObj.transform.SetParent(panelObj.transform, false);
        Text title = titleObj.AddComponent<Text>();
        title.text = "GAME OVER";
        title.alignment = TextAnchor.MiddleCenter;
        title.font = font;
        title.fontSize = 78;
        title.color = new Color(0.95f, 0.2f, 0.2f, 1f);

        RectTransform titleRect = titleObj.GetComponent<RectTransform>();
        titleRect.anchorMin = new Vector2(0.5f, 0.6f);
        titleRect.anchorMax = new Vector2(0.5f, 0.6f);
        titleRect.sizeDelta = new Vector2(900f, 120f);
        titleRect.anchoredPosition = Vector2.zero;

        GameObject buttonObj = new GameObject("ReturnToMenuButton");
        buttonObj.transform.SetParent(panelObj.transform, false);

        Image buttonImage = buttonObj.AddComponent<Image>();
        buttonImage.color = new Color(0.2f, 0.2f, 0.2f, 0.95f);
        Button button = buttonObj.AddComponent<Button>();
        button.targetGraphic = buttonImage;
        button.onClick.AddListener(OnReturnToMenuClicked);

        RectTransform buttonRect = buttonObj.GetComponent<RectTransform>();
        buttonRect.anchorMin = new Vector2(0.5f, 0.44f);
        buttonRect.anchorMax = new Vector2(0.5f, 0.44f);
        buttonRect.sizeDelta = new Vector2(520f, 72f);
        buttonRect.anchoredPosition = Vector2.zero;

        GameObject buttonTextObj = new GameObject("ButtonText");
        buttonTextObj.transform.SetParent(buttonObj.transform, false);
        Text buttonText = buttonTextObj.AddComponent<Text>();
        buttonText.text = "RETURN TO MAIN MENU";
        buttonText.alignment = TextAnchor.MiddleCenter;
        buttonText.font = font;
        buttonText.fontSize = 28;
        buttonText.color = new Color(0.94f, 0.94f, 0.94f, 1f);

        RectTransform buttonTextRect = buttonTextObj.GetComponent<RectTransform>();
        buttonTextRect.anchorMin = Vector2.zero;
        buttonTextRect.anchorMax = Vector2.one;
        buttonTextRect.offsetMin = Vector2.zero;
        buttonTextRect.offsetMax = Vector2.zero;
    }

    private void EnsureEventSystem()
    {
        EventSystem existing = EventSystem.current;
        if (existing != null)
        {
            if (existing.GetComponent<InputSystemUIInputModule>() == null)
                existing.gameObject.AddComponent<InputSystemUIInputModule>();
            return;
        }

        GameObject eventSystemGo = new GameObject("EventSystem");
        eventSystemGo.AddComponent<EventSystem>();
        eventSystemGo.AddComponent<InputSystemUIInputModule>();
        DontDestroyOnLoad(eventSystemGo);
    }

    private void ApplyInputStateForMenu()
    {
        Time.timeScale = 1f;
        AudioListener.pause = false;

        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;

        PlayerInput[] inputs = FindObjectsByType<PlayerInput>();
        for (int i = 0; i < inputs.Length; i++)
        {
            if (inputs[i] != null)
                inputs[i].enabled = false;
        }
    }

    private void OnReturnToMenuClicked()
    {
        DeleteCurrentSaveSlot();
        LoadMainMenuScene();
    }

    private void DeleteCurrentSaveSlot()
    {
        int slotIndex = Mathf.Max(1, PlayerPrefs.GetInt(PrefSelectedSlotIndex, 1));

        GameSaveManager saveManager = FindAnyObjectByType<GameSaveManager>();
        if (saveManager != null)
        {
            saveManager.DeleteSlot(slotIndex);
            return;
        }

        string fallbackPath = Path.Combine(Application.persistentDataPath, $"{saveFilePrefix}{slotIndex:00}.json");
        if (File.Exists(fallbackPath))
            File.Delete(fallbackPath);
    }

    private void LoadMainMenuScene()
    {
        Time.timeScale = 1f;
        AudioListener.pause = false;

        string[] candidates = new[]
        {
            mainMenuSceneName,
            "MainMenu",
            "Main Menu"
        };

        for (int i = 0; i < candidates.Length; i++)
        {
            string sceneName = candidates[i];
            if (string.IsNullOrWhiteSpace(sceneName))
                continue;

            if (!Application.CanStreamedLevelBeLoaded(sceneName))
                continue;

            SceneManager.LoadScene(sceneName, LoadSceneMode.Single);
            Destroy(gameObject);
            return;
        }

        Debug.LogError($"[PlayerGameOverFlow] Could not load main menu scene. Tried: '{mainMenuSceneName}', 'MainMenu', 'Main Menu'. Add the menu scene to Build Settings or set Player.mainMenuSceneName correctly.");
    }
}
