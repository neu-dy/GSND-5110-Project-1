using UnityEngine;
using UnityEngine.UI;

[RequireComponent(typeof(CanvasRenderer))]
public class HeartGraphic : MaskableGraphic
{
    protected override void OnPopulateMesh(VertexHelper mesh)
    {
        mesh.Clear();
        Rect rect = rectTransform.rect;
        const int segments = 96;
        mesh.AddVert(new Vector3(rect.center.x, rect.center.y, 0), color, Vector2.zero);
        for (int i = 0; i <= segments; i++)
        {
            float t = i * Mathf.PI * 2f / segments;
            float x = 16f * Mathf.Pow(Mathf.Sin(t), 3f);
            float y = 13f * Mathf.Cos(t) - 5f * Mathf.Cos(2f*t) - 2f * Mathf.Cos(3f*t) - Mathf.Cos(4f*t);
            mesh.AddVert(new Vector3(rect.center.x + x / 32f * rect.width,
                rect.center.y + (y + 2.5f) / 30f * rect.height, 0), color, Vector2.zero);
            if (i > 0) mesh.AddTriangle(0, i, i + 1);
        }
    }
}
