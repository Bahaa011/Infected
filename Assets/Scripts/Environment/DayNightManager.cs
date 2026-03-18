using UnityEngine;
using UnityEngine.Events;

public class DayNightManager : MonoBehaviour
{
    [Header("Time Settings")]
    [SerializeField] private float dayLengthInSeconds = 1200f; // 20 minutes = 1 full day
    [SerializeField] private float startTimeInHours = 6f; // Start at 6 AM

    [Header("Light Settings")]
    [SerializeField] private Light sunLight;
    [SerializeField] private AnimationCurve sunIntensityCurve = AnimationCurve.EaseInOut(0, 0, 1, 1);
    [SerializeField] private Color nightAmbientColor = new Color(0.1f, 0.1f, 0.2f);
    [SerializeField] private Color dayAmbientColor = new Color(1f, 1f, 1f);

    [Header("Events")]
    public UnityEvent onSunrise; // 6 AM
    public UnityEvent onNoon; // 12 PM
    public UnityEvent onSunset; // 6 PM
    public UnityEvent onMidnight; // 12 AM

    private float currentTimeInHours; // 0-24 format
    private float timeScale = 1f; // Multiplier for time progression

    private void Awake()
    {
        currentTimeInHours = startTimeInHours;

        if (sunLight == null)
        {
            sunLight = FindFirstObjectByType<Light>();
            if (sunLight != null && sunLight.type != LightType.Directional)
                sunLight = null;
        }
    }

    private void Update()
    {
        ProgressTime();
        UpdateSunLight();
        UpdateAmbientLight();
    }

    private void ProgressTime()
    {
        // Progress time: dayLengthInSeconds = 24 hours
        float hoursPerSecond = 24f / dayLengthInSeconds;
        currentTimeInHours += hoursPerSecond * Time.deltaTime * timeScale;

        // Wrap around after 24 hours
        if (currentTimeInHours >= 24f)
            currentTimeInHours -= 24f;

        CheckTimeEvents();
    }

    private void UpdateSunLight()
    {
        if (sunLight == null) return;

        // Calculate sun rotation: 0° at midnight, 180° at noon
        // Sunrise at 6 AM (90°), Sunset at 6 PM (270°)
        float sunRotation = (currentTimeInHours / 24f) * 360f;
        sunLight.transform.rotation = Quaternion.Euler(sunRotation - 90f, 0, 0);

        // Calculate intensity based on sun angle
        // Peak at noon (12 hours), low at night
        float normalizedTime = currentTimeInHours / 24f; // 0-1
        float intensityCurveValue = sunIntensityCurve.Evaluate(normalizedTime);
        sunLight.intensity = Mathf.Clamp01(intensityCurveValue);
    }

    private void UpdateAmbientLight()
    {
        // Blend between night and day ambient colors based on sun intensity
        float sunIntensity = sunLight != null ? sunLight.intensity : 0f;
        RenderSettings.ambientLight = Color.Lerp(nightAmbientColor, dayAmbientColor, sunIntensity);
    }

    private void CheckTimeEvents()
    {
        // Check if we just passed these times (with small tolerance for floating point)
        CheckEventTime(6f, onSunrise, "Sunrise");
        CheckEventTime(12f, onNoon, "Noon");
        CheckEventTime(18f, onSunset, "Sunset");
        CheckEventTime(0f, onMidnight, "Midnight");
    }

    private void CheckEventTime(float hour, UnityEvent eventToTrigger, string eventName)
    {
        // Simple check - fires once per day when time passes this hour
        float tolerance = (24f / dayLengthInSeconds) * 2f; // 2 frames worth of time
        if (Mathf.Abs(currentTimeInHours - hour) < tolerance && Time.deltaTime < 0.1f)
        {
            eventToTrigger?.Invoke();
        }
    }

    // Getters
    public float GetCurrentTime() => currentTimeInHours;

    public string GetTimeFormatted()
    {
        int hours = (int)currentTimeInHours;
        int minutes = (int)((currentTimeInHours % 1f) * 60f);
        string formatted = $"{hours:D2}:{minutes:D2}";
        return formatted;
    }

    public bool IsDay() => currentTimeInHours >= 6f && currentTimeInHours < 18f;
    public bool IsNight() => !IsDay();
    public float GetDayLengthInSeconds() => Mathf.Max(0.01f, dayLengthInSeconds);
    public float GetTimeScale() => timeScale;

    // In-game days progressed per real second at current settings
    public float GetGameDaysPerSecond()
    {
        return Mathf.Max(0f, timeScale) / GetDayLengthInSeconds();
    }

    // Setters
    public void SetTime(float hourOfDay)
    {
        currentTimeInHours = Mathf.Clamp(hourOfDay, 0f, 24f);
    }

    public void SetTimeScale(float scale)
    {
        timeScale = Mathf.Max(0f, scale);
    }

    public void AddHours(float hours)
    {
        currentTimeInHours += hours;
        if (currentTimeInHours >= 24f)
            currentTimeInHours -= 24f;
    }

    public void PauseTime()
    {
        timeScale = 0f;
    }

    public void ResumeTime()
    {
        timeScale = 1f;
    }
}
