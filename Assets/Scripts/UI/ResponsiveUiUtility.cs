using UnityEngine;

public static class ResponsiveUiUtility
{
    private const float ReferenceWidth = 1920f;
    private const float ReferenceHeight = 1080f;

    public static float GetScaleFactor(float min = 0.75f, float max = 1.35f)
    {
        float widthScale = Screen.width / ReferenceWidth;
        float heightScale = Screen.height / ReferenceHeight;
        return Mathf.Clamp(Mathf.Min(widthScale, heightScale), min, max);
    }

    public static float Scale(float value, float min = 0.75f, float max = 1.35f)
    {
        return value * GetScaleFactor(min, max);
    }

    public static Vector2 Scale(Vector2 value, float min = 0.75f, float max = 1.35f)
    {
        return value * GetScaleFactor(min, max);
    }
}