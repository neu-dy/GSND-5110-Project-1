using UnityEngine;
using UnityEngine.UI;

/// <summary>A short red glow around the viewport; the centre stays transparent.</summary>
[RequireComponent(typeof(CanvasRenderer))]
public sealed class InjuryVignetteGraphic : MaskableGraphic
{
    private Color tint;
    private float strength, duration, width, age = float.PositiveInfinity;

    public void Flash(Color value, float peak, float seconds, float bandWidth)
    {
        // Every accepted collision retriggers feedback. This never grants hit immunity.
        float previous = Envelope();
        tint = value;
        strength = Mathf.Clamp01(Mathf.Max(peak, previous + peak * .25f));
        duration = Mathf.Max(.08f, seconds);
        width = Mathf.Clamp(bandWidth, .05f, .4f);
        age = 0f;
        SetVerticesDirty();
    }

    private void Update()
    {
        if (age >= duration) return;
        age += Time.unscaledDeltaTime;
        SetVerticesDirty();
    }

    private float Envelope()
    {
        if (age >= duration || duration <= 0f) return 0f;
        float progress = Mathf.Clamp01(age / duration);
        // Immediate flash followed by a smooth fade, with no full-screen white overlay.
        return strength * (1f - progress) * (1f - progress);
    }

    protected override void OnPopulateMesh(VertexHelper mesh)
    {
        mesh.Clear();
        float alpha = Envelope();
        if (alpha <= .001f) return;
        const int columns = 32, rows = 20;
        Rect rect = rectTransform.rect;
        for (int y = 0; y <= rows; y++)
        for (int x = 0; x <= columns; x++)
        {
            float u = x / (float)columns, v = y / (float)rows;
            float dx = Mathf.Abs(u * 2f - 1f), dy = Mathf.Abs(v * 2f - 1f);
            // Rounded rectangle: stronger at the corners, soft inward falloff.
            float radius = Mathf.Pow(Mathf.Pow(dx, 4f) + Mathf.Pow(dy, 4f), .25f);
            float edge = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(1f - width, 1f, radius));
            Color vertex = tint; vertex.a *= alpha * edge;
            mesh.AddVert(new Vector3(rect.xMin + u * rect.width, rect.yMin + v * rect.height), vertex, Vector2.zero);
            if (x == 0 || y == 0) continue;
            int current = y * (columns + 1) + x;
            mesh.AddTriangle(current - columns - 2, current - columns - 1, current);
            mesh.AddTriangle(current - columns - 2, current, current - 1);
        }
    }
}
