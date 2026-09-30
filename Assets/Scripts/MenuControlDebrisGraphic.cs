using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

// Draws every menu-hint fragment in one UI mesh, avoiding a separate Canvas
// object and TextMeshPro renderer for each tiny piece.
public sealed class MenuControlDebrisGraphic : MaskableGraphic
{
    private struct Fragment
    {
        public Vector2 position;
        public Vector2 a;
        public Vector2 b;
        public Vector2 c;
        public Vector2 velocity;
        public Color color;
        public float angle;
        public float spin;
        public float age;
        public float lifetime;
        public float floorOffset;
        public int bounces;
    }

    private readonly List<Fragment> fragments = new List<Fragment>();

    public void ResetDebris()
    {
        fragments.Clear();
        SetVerticesDirty();
    }

    public void AddTriangle(Vector2 a, Vector2 b, Vector2 c, Color tint,
        Vector2 velocity, float spin, float lifetime)
    {
        Vector2 center = (a + b + c) / 3f;
        Vector2 offset = velocity.sqrMagnitude > 1f ? velocity.normalized * 1.8f : Vector2.zero;
        fragments.Add(new Fragment
        {
            position = center + offset,
            a = a - center,
            b = b - center,
            c = c - center,
            velocity = velocity,
            color = tint,
            spin = spin,
            lifetime = lifetime,
            floorOffset = Mathf.Max(2f, center.y - Mathf.Min(a.y, b.y, c.y))
        });
        SetVerticesDirty();
    }

    public void Tick(float deltaTime, float floor)
    {
        if (fragments.Count == 0) return;
        float dt = Mathf.Max(0f, deltaTime);
        for (int i = fragments.Count - 1; i >= 0; i--)
        {
            Fragment piece = fragments[i];
            piece.age += dt;
            if (piece.age >= piece.lifetime)
            {
                fragments.RemoveAt(i);
                continue;
            }
            piece.velocity.y -= 920f * dt;
            piece.position += piece.velocity * dt;
            float bottom = floor + piece.floorOffset;
            if (piece.position.y < bottom)
            {
                piece.position.y = bottom;
                if (piece.bounces < 2 && piece.velocity.y < -55f)
                {
                    piece.velocity.y *= -.25f;
                    piece.velocity.x *= .62f;
                    piece.bounces++;
                }
                else piece.velocity = Vector2.zero;
            }
            piece.angle += piece.spin * Mathf.Deg2Rad * dt;
            fragments[i] = piece;
        }
        SetVerticesDirty();
    }

    protected override void OnPopulateMesh(VertexHelper mesh)
    {
        mesh.Clear();
        UIVertex vertex = UIVertex.simpleVert;
        for (int i = 0; i < fragments.Count; i++)
        {
            Fragment piece = fragments[i];
            float fade = Mathf.Clamp01((piece.lifetime - piece.age) / .42f);
            Color tint = piece.color;
            tint.a *= fade;
            vertex.color = tint;
            float sine = Mathf.Sin(piece.angle);
            float cosine = Mathf.Cos(piece.angle);
            int first = mesh.currentVertCount;
            vertex.position = Rotate(piece.a, sine, cosine) + piece.position;
            mesh.AddVert(vertex);
            vertex.position = Rotate(piece.b, sine, cosine) + piece.position;
            mesh.AddVert(vertex);
            vertex.position = Rotate(piece.c, sine, cosine) + piece.position;
            mesh.AddVert(vertex);
            mesh.AddTriangle(first, first + 1, first + 2);
        }
    }

    private static Vector2 Rotate(Vector2 point, float sine, float cosine)
    {
        return new Vector2(point.x * cosine - point.y * sine,
            point.x * sine + point.y * cosine);
    }
}
