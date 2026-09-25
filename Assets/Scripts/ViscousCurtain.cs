using UnityEngine;
using UnityEngine.UI;
using System.Collections.Generic;

// Opaque procedural silhouette; the same sampled edge drives swallowing checks.
[RequireComponent(typeof(CanvasRenderer))]
public class ViscousCurtain : MaskableGraphic
{
    private const int Segments = 192;
    public float Amplitude = 0.03f;
    public float FlowSpeed = 1.2f;
    public float ReactionStrength = 0.03f;
    public float ReactionDuration = 1.4f;
    public float ForwardLimit = 1f;
    public float AttackFocus;
    private float flowTime;
    private float lastFlowUpdate = -1f;
    private struct Ripple { public float y; public float start; }
    private readonly List<Ripple> ripples = new List<Ripple>();

    public void React(float viewportY)
    {
        if (ripples.Count >= 8) ripples.RemoveAt(0);
        ripples.Add(new Ripple { y = Mathf.Clamp01(viewportY), start = Time.unscaledTime });
    }
    private readonly float[] edge = new float[Segments + 1];

    public void RefreshEdge()
    {
        float front = rectTransform.anchorMax.x;
        float envelope = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(front / 0.04f))
            * Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((1f - front) / 0.06f));
        // Integrate changing flow speed; multiplying absolute time would jump the
        // wave phase whenever the pursuit warning changes the speed.
        float now = Time.unscaledTime;
        if (lastFlowUpdate >= 0f) flowTime += Mathf.Max(0f, now - lastFlowUpdate) * FlowSpeed;
        lastFlowUpdate = now;
        float time = flowTime;
        float duration = Mathf.Max(0.1f, ReactionDuration);
        ripples.RemoveAll(r => Time.unscaledTime - r.start >= duration);
        for (int i = 0; i <= Segments; i++)
        {
            float y = i / (float)Segments;
            // Low-amplitude waves travel along the edge, with a slower trailing layer.
            float drift = y + time * 0.055f;
            float wave = 0.5f + 0.24f * Mathf.Sin(drift * 10f + time * 0.7f)
                + 0.16f * Mathf.Sin(drift * 21f - time * 0.35f + 1.7f)
                + 0.06f * Mathf.Sin(drift * 34f + time * 0.45f + 0.8f);
            float reaction = 0f;
            foreach (Ripple ripple in ripples)
            {
                float age = Mathf.Clamp01((Time.unscaledTime - ripple.start) / duration);
                float width = Mathf.Lerp(0.07f, 0.17f, age);
                float offset = (y - ripple.y) / width;
                float swell = Mathf.Sin(Mathf.PI * age) * (1f - age);
                reaction += ReactionStrength * swell * Mathf.Exp(-offset * offset * 0.5f);
            }
            edge[i] = Mathf.Clamp01(Mathf.Min(ForwardLimit,
                front + envelope * Mathf.Lerp(1f, .65f, Mathf.Clamp01(AttackFocus)) * (Mathf.Min(reaction, ReactionStrength) - Amplitude * wave)));
        }
        SetVerticesDirty();
    }

    // Embed the whole root cross-section behind the deepest nearby trough.
    // The extra overlap also covers a diagonal root normal and flowing waves.
    public float AttachmentX(float y, float halfHeight)
    {
        EdgeRange(y - halfHeight, y + halfHeight, out float minimum, out _);
        return minimum - Mathf.Max(.012f, Amplitude) - .008f;
    }

    public float EdgeAt(float viewportY)
    {
        float row = Mathf.Clamp01(viewportY) * Segments;
        int index = Mathf.Min(Segments - 1, Mathf.FloorToInt(row));
        return Mathf.Lerp(edge[index], edge[index + 1], row - index);
    }

    public void EdgeRange(float bottom, float top, out float minimum, out float maximum)
    {
        minimum = Mathf.Min(EdgeAt(bottom), EdgeAt(top));
        maximum = Mathf.Max(EdgeAt(bottom), EdgeAt(top));
        int start = Mathf.CeilToInt(Mathf.Clamp01(bottom) * Segments);
        int end = Mathf.FloorToInt(Mathf.Clamp01(top) * Segments);
        for (int i = start; i <= end; i++)
        {
            minimum = Mathf.Min(minimum, edge[i]);
            maximum = Mathf.Max(maximum, edge[i]);
        }
    }

    protected override void OnPopulateMesh(VertexHelper mesh)
    {
        mesh.Clear();
        Rect rect = rectTransform.rect;
        RectTransform parent = rectTransform.parent as RectTransform;
        if (parent == null) return;
        float width = parent.rect.width;
        for (int i = 0; i <= Segments; i++)
        {
            float y = Mathf.Lerp(rect.yMin, rect.yMax, i / (float)Segments);
            mesh.AddVert(new Vector3(rect.xMin, y), Color.black, Vector2.zero);
            mesh.AddVert(new Vector3(rect.xMin + edge[i] * width, y), Color.black, Vector2.zero);
            if (i == 0) continue;
            int v = i * 2;
            mesh.AddTriangle(v - 2, v, v - 1);
            mesh.AddTriangle(v - 1, v, v + 1);
        }
    }
}
