using UnityEngine;
using UnityEngine.UI;

// A colored falloff outside the solid heart. No white overlay or fading core.
public sealed class HeartGlowGraphic : MaskableGraphic
{
    public float Intensity { get; private set; }
    public void SetIntensity(float value)
    {
        value = Mathf.Clamp01(value);
        if (Mathf.Approximately(value, Intensity)) return;
        Intensity = value;
        SetVerticesDirty();
    }

    protected override void OnPopulateMesh(VertexHelper mesh)
    {
        mesh.Clear();
        if (Intensity < .002f) return;
        Rect r = rectTransform.rect;
        const int segments = 96, layers = 10;
        for (int layer = 0; layer <= layers; layer++)
        {
            float u = layer / (float)layers;
            float scale = Mathf.Lerp(.97f, 1.62f, u);
            Color tint = new Color(1f, .12f, .025f, Intensity * .7f * (1-u) * (1-u));
            for (int i = 0; i <= segments; i++)
            {
                float t = i * Mathf.PI * 2f / segments;
                float x = 16f * Mathf.Pow(Mathf.Sin(t), 3f);
                float y = 13f*Mathf.Cos(t)-5f*Mathf.Cos(2*t)-2f*Mathf.Cos(3*t)-Mathf.Cos(4*t);
                mesh.AddVert(new Vector2(r.center.x + x/32f*r.width*scale,
                    r.center.y + (y+2.5f)/30f*r.height*scale), tint, Vector2.zero);
                if (layer == 0 || i == 0) continue;
                int v = layer*(segments+1)+i, previous = v-segments-1;
                mesh.AddTriangle(previous-1, previous, v-1);
                mesh.AddTriangle(previous, v, v-1);
            }
        }
    }
}
