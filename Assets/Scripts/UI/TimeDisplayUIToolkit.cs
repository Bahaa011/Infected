using UnityEngine;
using UnityEngine.UIElements;

public class TimeDisplayUIToolkit : MonoBehaviour
{
    [SerializeField] private UIDocument uiDocument;
    [SerializeField] private DayNightManager dayNightManager;

    private VisualElement root;
    private Label timeLabel;
    private Label dayPhaseLabel;
    private int lastScreenWidth;
    private int lastScreenHeight;

    private void Awake()
    {
        if (uiDocument == null)
            uiDocument = GetComponent<UIDocument>();

        root = uiDocument.rootVisualElement;
        timeLabel = root.Q<Label>(className: "time-display");
        dayPhaseLabel = root.Q<Label>(className: "day-phase");
        RefreshResponsiveScale(true);
    }

    private void OnEnable()
    {
        if (dayNightManager == null)
            dayNightManager = FindFirstObjectByType<DayNightManager>();
    }

    private void Update()
    {
        RefreshResponsiveScale();

        if (dayNightManager == null || timeLabel == null) return;

        timeLabel.text = dayNightManager.GetTimeFormatted();

        if (dayPhaseLabel != null)
        {
            dayPhaseLabel.text = dayNightManager.IsDay() ? "DAY" : "NIGHT";
            dayPhaseLabel.style.color = dayNightManager.IsDay() ? 
                new StyleColor(new Color(0.67f, 0.92f, 0.49f, 1f)) : 
                new StyleColor(new Color(0.72f, 0.58f, 1f, 1f));
        }
    }

    private void RefreshResponsiveScale(bool force = false)
    {
        if (!force && Screen.width == lastScreenWidth && Screen.height == lastScreenHeight)
            return;

        lastScreenWidth = Screen.width;
        lastScreenHeight = Screen.height;

        if (root == null)
            return;

        // Root is fullscreen; transform scaling causes anchored elements to drift.
        root.style.scale = new Scale(Vector3.one);
    }
}
