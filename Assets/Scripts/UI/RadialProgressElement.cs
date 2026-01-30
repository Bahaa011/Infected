using UnityEngine;
using UnityEngine.UIElements;

public class RadialProgressElement : VisualElement
{
    private const int SegmentCount = 64;

    private float progress = 0f;
    private float thickness = 8f;
    private Color trackColor = new Color(0f, 0f, 0f, 0.5f);
    private Color fillColor = new Color(0.2f, 0.8f, 1f, 1f);
    private float startAngleDegrees = -90f;

    public RadialProgressElement()
    {
        AddToClassList("radial-progress");
        generateVisualContent += OnGenerateVisualContent;
    }

    public void SetProgress(float value)
    {
        progress = Mathf.Clamp01(value);
        MarkDirtyRepaint();
    }

    public void SetThickness(float value)
    {
        thickness = Mathf.Max(1f, value);
        MarkDirtyRepaint();
    }

    public void SetTrackColor(Color color)
    {
        trackColor = color;
        MarkDirtyRepaint();
    }

    public void SetFillColor(Color color)
    {
        fillColor = color;
        MarkDirtyRepaint();
    }

    public void SetStartAngle(float degrees)
    {
        startAngleDegrees = degrees;
        MarkDirtyRepaint();
    }

    private void OnGenerateVisualContent(MeshGenerationContext context)
    {
        var rect = contentRect;
        if (rect.width <= 0 || rect.height <= 0)
            return;

        Vector2 center = rect.center;
        float radius = Mathf.Min(rect.width, rect.height) * 0.5f - (thickness * 0.5f);
        if (radius <= 0)
            return;

        var painter = context.painter2D;
        painter.lineWidth = thickness;
        painter.lineCap = LineCap.Round;

        // Track (full circle)
        painter.strokeColor = trackColor;
        painter.BeginPath();
        for (int i = 0; i <= SegmentCount; i++)
        {
            float t = i / (float)SegmentCount;
            float angle = (startAngleDegrees + t * 360f) * Mathf.Deg2Rad;
            Vector2 point = center + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * radius;
            if (i == 0)
                painter.MoveTo(point);
            else
                painter.LineTo(point);
        }
        painter.ClosePath();
        painter.Stroke();

        // Fill (progress arc)
        if (progress > 0.001f)
        {
            painter.strokeColor = fillColor;
            painter.BeginPath();

            int endSegments = Mathf.Max(1, Mathf.RoundToInt(SegmentCount * progress));
            for (int i = 0; i <= endSegments; i++)
            {
                float t = i / (float)SegmentCount;
                float angle = (startAngleDegrees + t * 360f) * Mathf.Deg2Rad;
                Vector2 point = center + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * radius;
                if (i == 0)
                    painter.MoveTo(point);
                else
                    painter.LineTo(point);
            }

            painter.Stroke();
        }
    }
}
