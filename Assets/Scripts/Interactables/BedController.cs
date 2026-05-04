using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

[DisallowMultipleComponent]
[RequireComponent(typeof(Collider))]
public class BedController : MonoBehaviour, IInteractionPromptSource
{
    [Header("Interaction")]
    [SerializeField] private float interactionRange = 2.5f;
    [SerializeField] private string bedDisplayName = "bed";

    [Header("Sleep Time")]
    [SerializeField] private float earliestSleepHour = 20f;
    [SerializeField] private float wakeUpHour = 6f;

    [Header("Fade")]
    [SerializeField] private float fadeToBlackSeconds = 0.75f;
    [SerializeField] private float blackHoldSeconds = 0.8f;
    [SerializeField] private float fadeFromBlackSeconds = 0.9f;

    private Player player;
    private DayNightManager dayNightManager;
    private Canvas fadeCanvas;
    private Image fadeImage;
    private bool isSleeping;

    private void Awake()
    {
        ResolveReferences();
    }

    private void Update()
    {
        ResolveReferences();

        if (isSleeping || player == null)
            return;

        if (!IsViewerWithinInteractionDistance(player.transform))
            return;

        if (Keyboard.current == null || !Keyboard.current.eKey.wasPressedThisFrame)
            return;

        if (!IsPlayerPointingAtThisBed())
            return;

        TrySleep();
    }

    public bool TryGetInteractionPrompt(Transform viewer, out string prompt)
    {
        prompt = string.Empty;

        if (isSleeping || viewer == null || !IsViewerWithinInteractionDistance(viewer))
            return false;

        if (!CanSleepNow())
        {
            prompt = "You can sleep after 20:00";
            return true;
        }

        string displayName = string.IsNullOrWhiteSpace(bedDisplayName) ? "bed" : bedDisplayName;
        prompt = $"Press E to sleep in {displayName}";
        return true;
    }

    private void TrySleep()
    {
        if (!CanSleepNow())
            return;

        StartCoroutine(SleepRoutine());
    }

    private IEnumerator SleepRoutine()
    {
        isSleeping = true;
        SetPlayerInputEnabled(false);
        EnsureFadeOverlay();

        yield return FadeBlack(0f, 1f, fadeToBlackSeconds);
        AdvanceToWakeUpTime();
        yield return new WaitForSecondsRealtime(Mathf.Max(0f, blackHoldSeconds));
        yield return FadeBlack(1f, 0f, fadeFromBlackSeconds);

        SetPlayerInputEnabled(true);
        isSleeping = false;
    }

    private void AdvanceToWakeUpTime()
    {
        if (dayNightManager == null)
            return;

        float currentHour = dayNightManager.GetCurrentTime();
        float targetHour = Mathf.Repeat(wakeUpHour, 24f);
        float hoursToAdvance = targetHour - currentHour;
        if (hoursToAdvance <= 0f)
            hoursToAdvance += 24f;

        dayNightManager.AddHours(hoursToAdvance);
    }

    private bool CanSleepNow()
    {
        if (dayNightManager == null)
            return false;

        float currentHour = dayNightManager.GetCurrentTime();
        float startHour = Mathf.Clamp(earliestSleepHour, 0f, 23.99f);
        float targetHour = Mathf.Repeat(wakeUpHour, 24f);

        if (startHour > targetHour)
            return currentHour >= startHour || currentHour < targetHour;

        return currentHour >= startHour && currentHour < targetHour;
    }

    private IEnumerator FadeBlack(float from, float to, float duration)
    {
        if (fadeImage == null)
            yield break;

        float safeDuration = Mathf.Max(0.01f, duration);
        float elapsed = 0f;

        while (elapsed < safeDuration)
        {
            elapsed += Time.unscaledDeltaTime;
            SetFadeAlpha(Mathf.Lerp(from, to, Mathf.Clamp01(elapsed / safeDuration)));
            yield return null;
        }

        SetFadeAlpha(to);
    }

    private void EnsureFadeOverlay()
    {
        if (fadeCanvas != null && fadeImage != null)
            return;

        GameObject canvasObject = new GameObject("Sleep Fade Overlay");
        fadeCanvas = canvasObject.AddComponent<Canvas>();
        fadeCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
        fadeCanvas.sortingOrder = 32760;

        CanvasScaler scaler = canvasObject.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);

        canvasObject.AddComponent<GraphicRaycaster>();

        GameObject imageObject = new GameObject("Blackout");
        imageObject.transform.SetParent(canvasObject.transform, false);
        fadeImage = imageObject.AddComponent<Image>();
        fadeImage.color = new Color(0f, 0f, 0f, 0f);
        fadeImage.raycastTarget = false;

        RectTransform rect = fadeImage.rectTransform;
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
    }

    private void SetFadeAlpha(float alpha)
    {
        if (fadeImage == null)
            return;

        Color color = fadeImage.color;
        color.a = Mathf.Clamp01(alpha);
        fadeImage.color = color;
    }

    private void SetPlayerInputEnabled(bool enabled)
    {
        if (player == null)
            return;

        PlayerInput input = player.GetComponent<PlayerInput>();
        if (input != null)
            input.enabled = enabled;
    }

    private bool IsViewerWithinInteractionDistance(Transform viewer)
    {
        if (viewer == null)
            return false;

        return Vector3.Distance(transform.position, viewer.position) <= Mathf.Max(0.25f, interactionRange);
    }

    private bool IsPlayerPointingAtThisBed()
    {
        if (player == null)
            return false;

        ThirdPersonController controller = player.GetComponent<ThirdPersonController>();
        Camera cam = controller != null && controller.MainUnityCamera != null
            ? controller.MainUnityCamera
            : Camera.main;

        if (cam == null)
            return false;

        Ray ray = new Ray(cam.transform.position, cam.transform.forward);
        float maxDistance = Mathf.Max(0.25f, interactionRange);
        if (!Physics.Raycast(ray, out RaycastHit hit, maxDistance, ~0, QueryTriggerInteraction.Collide))
            return false;

        return hit.collider != null && hit.collider.transform.IsChildOf(transform);
    }

    private void ResolveReferences()
    {
        if (player == null)
            player = FindAnyObjectByType<Player>();

        if (dayNightManager == null)
            dayNightManager = FindAnyObjectByType<DayNightManager>();
    }
}
