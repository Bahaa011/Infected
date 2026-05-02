using UnityEngine;
using UnityEngine.Events;

public class DayNightManager : MonoBehaviour
{
    [Header("Time Settings")]
    [SerializeField] private float dayLengthInSeconds = 1200f; // 20 minutes = 1 full day
    [SerializeField] private float startTimeInHours = 6f; // Start at 6 AM
    [SerializeField] private float dawnStartHour = 5f;
    [SerializeField] private float dayStartHour = 7f;
    [SerializeField] private float duskStartHour = 18f;
    [SerializeField] private float nightStartHour = 20f;

    [Header("Light Settings")]
    [SerializeField] private Light sunLight;
    [SerializeField] private AnimationCurve sunIntensityCurve = AnimationCurve.EaseInOut(0, 0, 1, 1);
    [SerializeField] private Color nightAmbientColor = new Color(0.25f, 0.25f, 0.35f);
    [SerializeField] private Color dayAmbientColor = new Color(0.85f, 0.85f, 0.95f);
    [SerializeField] private float nightSunIntensity = 0.05f;

    [Header("Sky Over Time")]
    [SerializeField] private bool driveSkyboxFromTime = true;
    [SerializeField] private Color nightSkyTint = new Color(0.08f, 0.1f, 0.2f);
    [SerializeField] private Color daySkyTint = new Color(0.5f, 0.65f, 0.85f);
    [SerializeField] private float nightSkyExposure = 0.45f;
    [SerializeField] private float daySkyExposure = 1.15f;

    [Header("Fog Over Time")]
    [SerializeField] private bool driveFogFromTime = true;
    [SerializeField] private Color nightFogColor = new Color(0.14f, 0.16f, 0.22f);
    [SerializeField] private Color dayFogColor = new Color(0.67f, 0.71f, 0.76f);
    [SerializeField] private float nightFogStartDistance = 35f;
    [SerializeField] private float dayFogStartDistance = 90f;
    [SerializeField] private float nightFogEndDistance = 150f;
    [SerializeField] private float dayFogEndDistance = 320f;
    [SerializeField] private float nightFogDensity = 0.02f;
    [SerializeField] private float dayFogDensity = 0.006f;

    [Header("Events")]
    public UnityEvent onSunrise; // 6 AM
    public UnityEvent onNoon; // 12 PM
    public UnityEvent onSunset; // 6 PM
    public UnityEvent onMidnight; // 12 AM

    private float currentTimeInHours; // 0-24 format
    private float timeScale = 1f; // Multiplier for time progression
    private int elapsedDayCount;

    private void Awake()
    {
        currentTimeInHours = startTimeInHours;
        elapsedDayCount = 0;

        if (sunLight == null)
        {
            sunLight = FindAnyObjectByType<Light>();
            if (sunLight != null && sunLight.type != LightType.Directional)
                sunLight = null;
        }
    }

    private void Update()
    {
        ProgressTime();
        UpdateSunLight();
        UpdateAmbientLight();
        UpdateSkybox();
        UpdateFog();
    }

    private void ProgressTime()
    {
        // Progress time: dayLengthInSeconds = 24 hours
        float hoursPerSecond = 24f / dayLengthInSeconds;
        currentTimeInHours += hoursPerSecond * Time.deltaTime * timeScale;

        // Wrap around after 24 hours
        while (currentTimeInHours >= 24f)
        {
            currentTimeInHours -= 24f;
            elapsedDayCount++;
        }

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
        float sunBlend = GetDayNightLerp();
        sunLight.intensity = Mathf.Lerp(nightSunIntensity, 1f, sunBlend);
    }

    private void UpdateAmbientLight()
    {
        // Blend between night and day ambient colors based on sun intensity
        float sunIntensity = GetDayNightLerp();
        RenderSettings.ambientLight = Color.Lerp(nightAmbientColor, dayAmbientColor, sunIntensity);
    }

    private void UpdateFog()
    {
        if (!driveFogFromTime)
            return;

        if (!RenderSettings.fog)
            return;

        float t = GetDayNightLerp();

        RenderSettings.fogColor = Color.Lerp(nightFogColor, dayFogColor, t);

        if (RenderSettings.fogMode == FogMode.Linear)
        {
            RenderSettings.fogStartDistance = Mathf.Lerp(nightFogStartDistance, dayFogStartDistance, t);
            RenderSettings.fogEndDistance = Mathf.Lerp(nightFogEndDistance, dayFogEndDistance, t);
            return;
        }

        RenderSettings.fogDensity = Mathf.Lerp(nightFogDensity, dayFogDensity, t);
    }

    private void UpdateSkybox()
    {
        if (!driveSkyboxFromTime)
            return;

        Material sky = RenderSettings.skybox;
        if (sky == null)
            return;

        float t = GetDayNightLerp();
        Color skyTint = Color.Lerp(nightSkyTint, daySkyTint, t);
        float exposure = Mathf.Lerp(nightSkyExposure, daySkyExposure, t);

        if (sky.HasProperty("_SkyTint"))
            sky.SetColor("_SkyTint", skyTint);

        if (sky.HasProperty("_Tint"))
            sky.SetColor("_Tint", skyTint);

        if (sky.HasProperty("_GroundColor"))
            sky.SetColor("_GroundColor", Color.Lerp(skyTint * 0.35f, skyTint * 0.6f, t));

        if (sky.HasProperty("_Exposure"))
            sky.SetFloat("_Exposure", exposure);
    }

    private float GetDayNightLerp()
    {
        float h = Mathf.Repeat(currentTimeInHours, 24f);

        float dawn = Mathf.Clamp(dawnStartHour, 0f, 24f);
        float day = Mathf.Clamp(dayStartHour, 0f, 24f);
        float dusk = Mathf.Clamp(duskStartHour, 0f, 24f);
        float night = Mathf.Clamp(nightStartHour, 0f, 24f);

        day = Mathf.Max(day, dawn + 0.01f);
        dusk = Mathf.Max(dusk, day + 0.01f);
        night = Mathf.Max(night, dusk + 0.01f);

        if (h < dawn || h >= night)
            return 0f;

        if (h < day)
        {
            float t = Mathf.InverseLerp(dawn, day, h);
            return Mathf.SmoothStep(0f, 1f, t);
        }

        if (h < dusk)
            return 1f;

        float sunsetT = Mathf.InverseLerp(dusk, night, h);
        return Mathf.SmoothStep(1f, 0f, sunsetT);
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
    public int GetElapsedDayCount() => Mathf.Max(0, elapsedDayCount);
    public float GetTotalGameDays() => GetElapsedDayCount() + (currentTimeInHours / 24f);

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

    public void SetTotalGameDays(float totalDays)
    {
        float safeDays = Mathf.Max(0f, totalDays);
        int wholeDays = Mathf.FloorToInt(safeDays);
        float fractional = safeDays - wholeDays;

        elapsedDayCount = wholeDays;
        currentTimeInHours = Mathf.Clamp(fractional * 24f, 0f, 23.999f);
    }

    public void SetTimeScale(float scale)
    {
        timeScale = Mathf.Max(0f, scale);
    }

    public void AddHours(float hours)
    {
        currentTimeInHours += hours;
        while (currentTimeInHours >= 24f)
        {
            currentTimeInHours -= 24f;
            elapsedDayCount++;
        }

        while (currentTimeInHours < 0f)
        {
            currentTimeInHours += 24f;
            elapsedDayCount = Mathf.Max(0, elapsedDayCount - 1);
        }
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
