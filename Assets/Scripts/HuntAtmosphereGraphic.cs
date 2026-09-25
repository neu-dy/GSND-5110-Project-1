using System;
using UnityEngine;
using UnityEngine.UI;

// Decorative screen borders only. Never consulted by collision or swallowing.
[RequireComponent(typeof(CanvasRenderer))]
public sealed class HuntAtmosphereGraphic : MaskableGraphic
{
    [Serializable]
    public sealed class Settings
    {
        public bool enabled = true;
        [Tooltip("Border depth as a fraction of the shorter screen dimension. 0.035 is 3.5%, keeping all three sides equally thin.")]
        [Range(0f, .1f)] public float topDepth = .035f;
        [Range(0f, .1f)] public float bottomDepth = .035f;
        [Range(0f, .1f)] public float rightDepth = .035f;
        [Range(0f, .02f)] public float waveDepth = .007f;
        [Min(0f)] public float flowSpeed = .65f;
        [Min(.1f)] public float enterSeconds = 1.2f;
        [Min(.1f)] public float exitSeconds = 1.6f;

        public void Validate()
        {
            topDepth = Mathf.Clamp(topDepth, 0f, .1f);
            bottomDepth = Mathf.Clamp(bottomDepth, 0f, .1f);
            rightDepth = Mathf.Clamp(rightDepth, 0f, .1f);
            waveDepth = Mathf.Clamp(waveDepth, 0f, .02f);
            flowSpeed = Mathf.Max(0f, flowSpeed);
            enterSeconds = Mathf.Max(.1f, enterSeconds);
            exitSeconds = Mathf.Max(.1f, exitSeconds);
        }
    }

    private const int Segments = 96;
    private Settings settings;
    private float amount, flowTime, warningFocus;
    public float Amount => amount;

    public void ResetVisual()
    {
        amount = flowTime = warningFocus = 0f;
        SetVerticesDirty();
    }

    public void Tick(bool huntActive, float dt, Settings configuration, float focus)
    {
        settings = configuration;
        if (settings == null || dt <= 0f) return;
        bool entering = huntActive && settings.enabled;
        float previous = amount;
        amount = Mathf.MoveTowards(amount, entering ? 1f : 0f,
            dt / Mathf.Max(.1f, entering ? settings.enterSeconds : settings.exitSeconds));
        flowTime += dt * Mathf.Max(0f, settings.flowSpeed);
        // Keep attention on the attack crest when a tentacle is announced.
        warningFocus = Mathf.Clamp01(focus);
        if (amount > 0f || previous > 0f) SetVerticesDirty();
    }

    protected override void OnPopulateMesh(VertexHelper mesh)
    {
        mesh.Clear();
        if (settings == null || amount <= 0f) return;
        Rect r = rectTransform.rect;
        float unit = Mathf.Min(r.width, r.height);
        float reveal = Mathf.SmoothStep(0f, 1f, amount);
        AddBorder(mesh, r, unit, reveal, settings.topDepth, 0);
        AddBorder(mesh, r, unit, reveal, settings.bottomDepth, 1);
        AddBorder(mesh, r, unit, reveal, settings.rightDepth, 2);
    }

    private void AddBorder(VertexHelper mesh, Rect r, float unit, float reveal, float depth, int side)
    {
        if (depth <= 0f) return;
        int start = mesh.currentVertCount;
        for (int i = 0; i <= Segments; i++)
        {
            float u = i / (float)Segments;
            float phase = side * 2.3f;
            float wave = .6f * Mathf.Sin(u * 12f + flowTime + phase)
                + .28f * Mathf.Sin(u * 25f - flowTime * .7f + phase)
                + .12f * Mathf.Sin(u * 39f + flowTime * .4f + phase);
            float thickness = unit * reveal * (depth + wave
                * Mathf.Min(depth * .45f, Mathf.Max(0f, settings.waveDepth))
                * Mathf.Lerp(1f, .65f, warningFocus));
            Vector2 outer, inner;
            if (side == 2)
            {
                outer = new Vector2(r.xMax, Mathf.Lerp(r.yMin, r.yMax, u));
                inner = outer + Vector2.left * thickness;
            }
            else
            {
                outer = new Vector2(Mathf.Lerp(r.xMin, r.xMax, u), side == 0 ? r.yMax : r.yMin);
                inner = outer + (side == 0 ? Vector2.down : Vector2.up) * thickness;
            }
            mesh.AddVert(outer, Color.black, Vector2.zero);
            mesh.AddVert(inner, Color.black, Vector2.zero);
            if (i == 0) continue;
            int v = start + i * 2;
            mesh.AddTriangle(v - 2, v, v - 1);
            mesh.AddTriangle(v - 1, v, v + 1);
        }
    }
}
