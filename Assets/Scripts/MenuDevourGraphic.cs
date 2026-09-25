using UnityEngine;
using UnityEngine.UI;

// Menu-only silhouette. Uses unscaled elapsed time while the world remains frozen.
public sealed class MenuDevourGraphic : MaskableGraphic
{
    private readonly Vector3[] corners = new Vector3[4];
    private Vector2 center, half;
    private float reach, grip, pull, elapsed, angle;

    public void Show(RectTransform target, float reachProgress, float gripProgress, float pullProgress, float seconds)
    {
        target.GetWorldCorners(corners);
        Vector2 min = rectTransform.InverseTransformPoint(corners[0]);
        Vector2 max = rectTransform.InverseTransformPoint(corners[2]);
        center = (min + max) * .5f;
        half = target.rect.size * target.localScale.x * .5f;
        angle = target.localEulerAngles.z * Mathf.Deg2Rad;
        reach = Mathf.Clamp01(reachProgress);
        grip = Mathf.Clamp01(gripProgress);
        pull = Mathf.Clamp01(pullProgress);
        elapsed = seconds;
        SetVerticesDirty();
    }

    private Vector2 OnButton(Vector2 offset)
    {
        float c = Mathf.Cos(angle), s = Mathf.Sin(angle);
        return center + new Vector2(offset.x * c - offset.y * s, offset.x * s + offset.y * c);
    }

    protected override void OnPopulateMesh(VertexHelper mesh)
    {
        mesh.Clear();
        Rect r = rectTransform.rect;
        float unit = Mathf.Min(r.width, r.height);
        for (int i = 0; i < 4; i++)
        {
            float extension = Mathf.SmoothStep(0, 1, Mathf.Clamp01((reach - i * .065f) / (1 - i * .065f)));
            float lane = (i - 1.5f) / 1.5f;
            Vector2 root = new Vector2(r.xMin - unit * .07f, r.center.y + lane * r.height * .32f);
            Vector2 target = OnButton(new Vector2(half.x * (-.65f + i * .43f), (i % 2 == 0 ? 1 : -1) * half.y * .8f));
            float span = Mathf.Max(unit * .12f, target.x - root.x);
            Vector2 a = root + new Vector2(span * .38f, Mathf.Sin(elapsed * 2f + i) * unit * .025f);
            Vector2 b = target - new Vector2(span * .24f, -lane * unit * .08f);
            Curve(mesh, root, a, b, target, extension, unit * .045f);

            // Curl over the button after contact, then hold it during the drag.
            if (grip <= 0f) continue;
            int start = mesh.currentVertCount;
            const int steps = 56;
            for (int j = 0; j <= steps; j++)
            {
                float t = j / (float)steps;
                float theta = t * grip * Mathf.PI * 2.1f + (i % 2 == 0 ? Mathf.PI * .5f : -Mathf.PI * .5f);
                float x = half.x * (-.65f + i * .43f) + Mathf.Cos(theta) * half.x * .16f;
                Vector2 point = OnButton(new Vector2(x, Mathf.Sin(theta) * half.y * 1.18f));
                float width = unit * .009f * Mathf.Pow(1 - t, .45f);
                mesh.AddVert(point + Vector2.left * width, Color.black, Vector2.zero);
                mesh.AddVert(point + Vector2.right * width, Color.black, Vector2.zero);
                if (j > 0) Join(mesh, start + j * 2);
            }
        }
    }

    private static void Curve(VertexHelper mesh, Vector2 a, Vector2 b, Vector2 c, Vector2 d, float extension, float width)
    {
        if (extension <= 0f) return;
        int start = mesh.currentVertCount;
        const int steps = 64;
        for (int i = 0; i <= steps; i++)
        {
            float t = i / (float)steps * extension, s = 1 - t;
            Vector2 point = s*s*s*a + 3*s*s*t*b + 3*s*t*t*c + t*t*t*d;
            Vector2 tangent = 3*s*s*(b-a) + 6*s*t*(c-b) + 3*t*t*(d-c);
            Vector2 normal = new Vector2(-tangent.y, tangent.x).normalized * width * .5f * Mathf.Pow(1 - i/(float)steps, .75f);
            mesh.AddVert(point - normal, Color.black, Vector2.zero);
            mesh.AddVert(point + normal, Color.black, Vector2.zero);
            if (i > 0) Join(mesh, start + i * 2);
        }
    }

    private static void Join(VertexHelper mesh, int v)
    {
        mesh.AddTriangle(v-2, v-1, v);
        mesh.AddTriangle(v-1, v+1, v);
    }
}
