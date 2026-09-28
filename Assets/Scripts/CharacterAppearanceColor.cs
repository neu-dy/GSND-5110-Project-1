using UnityEngine;

/// <summary>Finds the dominant albedo of the currently visible skin, ignoring hidden placeholders.</summary>
public static class CharacterAppearanceColor
{
    private const int Bins = 8;
    private const int TextureSize = 32;

    public static Color Dominant(Transform character)
    {
        var weights = new float[Bins * Bins * Bins];
        var colors = new Vector3[weights.Length];
        Renderer[] renderers = character.GetComponentsInChildren<Renderer>();
        bool visibleSkin = false;
        foreach (Renderer renderer in renderers)
            if (renderer.enabled && renderer.gameObject.activeInHierarchy
                && (renderer is MeshRenderer || renderer is SkinnedMeshRenderer)) visibleSkin = true;
        foreach (Renderer renderer in renderers)
        {
            // Boarding the vehicle temporarily hides the entire rig. In that case the active
            // skinned body still describes the current appearance; never use the hidden cube.
            if (!renderer.gameObject.activeInHierarchy
                || (!renderer.enabled && (visibleSkin || !(renderer is SkinnedMeshRenderer)))) continue;
            Mesh mesh = renderer is SkinnedMeshRenderer skin ? skin.sharedMesh
                : renderer is MeshRenderer ? renderer.GetComponent<MeshFilter>()?.sharedMesh : null;
            if (mesh == null) continue;
            Material[] materials = renderer.sharedMaterials;
            Vector3[] vertices = mesh.isReadable ? mesh.vertices : null;
            Vector2[] uv = mesh.isReadable ? mesh.uv : null;
            Color[] vertexColors = mesh.isReadable ? mesh.colors : null;
            var globalProperties = new MaterialPropertyBlock();
            renderer.GetPropertyBlock(globalProperties);
            for (int submesh = 0; submesh < Mathf.Min(mesh.subMeshCount, materials.Length); submesh++)
            {
                Material material = materials[submesh];
                if (material == null) continue;
                var properties = new MaterialPropertyBlock();
                renderer.GetPropertyBlock(properties, submesh);
                // Per-slot property blocks replace the renderer-level block.
                if (properties.isEmpty) properties = globalProperties;
                string colorProperty = material.HasProperty("_BaseColor") ? "_BaseColor" : "_Color";
                Color tint = material.HasProperty(colorProperty) ? material.GetColor(colorProperty) : Color.white;
                int colorId = Shader.PropertyToID(colorProperty);
                if (properties.HasColor(colorId)) tint = properties.GetColor(colorId);
                string textureProperty = material.HasProperty("_BaseMap") ? "_BaseMap" : "_MainTex";
                Texture texture = material.HasProperty(textureProperty) ? material.GetTexture(textureProperty) : null;
                int textureId = Shader.PropertyToID(textureProperty);
                if (properties.HasTexture(textureId)) texture = properties.GetTexture(textureId);
                Color[] pixels = texture != null ? ReadTexture(texture) : null;
                Vector2 tiling = material.HasProperty(textureProperty) ? material.GetTextureScale(textureProperty) : Vector2.one;
                Vector2 offset = material.HasProperty(textureProperty) ? material.GetTextureOffset(textureProperty) : Vector2.zero;
                if (vertices == null)
                {
                    // Non-readable meshes still adapt to material/tint and texture changes.
                    float area = renderer.bounds.size.sqrMagnitude / Mathf.Max(1, materials.Length);
                    if (pixels == null) Add(tint, area, weights, colors);
                    else foreach (Color pixel in pixels) Add(pixel * tint, area / pixels.Length, weights, colors);
                    continue;
                }
                int[] triangles = mesh.GetTriangles(submesh);
                for (int index = 0; index < triangles.Length; index += 3)
                {
                    int a = triangles[index], b = triangles[index + 1], c = triangles[index + 2];
                    Vector3 ab = renderer.transform.TransformVector(vertices[b] - vertices[a]);
                    Vector3 ac = renderer.transform.TransformVector(vertices[c] - vertices[a]);
                    float area = Vector3.Cross(ab, ac).magnitude * .5f;
                    Color vertexTint = vertexColors != null && vertexColors.Length == vertices.Length
                        ? (vertexColors[a] + vertexColors[b] + vertexColors[c]) / 3f : Color.white;
                    if (pixels == null || uv == null || uv.Length != vertices.Length)
                    { Add(tint * vertexTint, area, weights, colors); continue; }
                    // Three interior samples per triangle avoid counting unused atlas space.
                    for (int sample = 0; sample < 3; sample++)
                    {
                        Vector2 point = sample == 0 ? uv[a] * .6f + uv[b] * .2f + uv[c] * .2f
                            : sample == 1 ? uv[a] * .2f + uv[b] * .6f + uv[c] * .2f
                            : uv[a] * .2f + uv[b] * .2f + uv[c] * .6f;
                        point = Vector2.Scale(point, tiling) + offset;
                        int x = Pixel(point.x, texture.wrapModeU), y = Pixel(point.y, texture.wrapModeV);
                        Add(pixels[y * TextureSize + x] * tint * vertexTint, area / 3f, weights, colors);
                    }
                }
            }
        }
        int dominant = 0;
        for (int i = 1; i < weights.Length; i++) if (weights[i] > weights[dominant]) dominant = i;
        if (weights[dominant] <= 0f) return Color.white;
        Vector3 rgb = colors[dominant] / weights[dominant];
        return new Color(rgb.x, rgb.y, rgb.z, 1f);
    }

    private static int Pixel(float value, TextureWrapMode wrap)
    {
        value = wrap == TextureWrapMode.Clamp ? Mathf.Clamp01(value)
            : wrap == TextureWrapMode.Mirror ? Mathf.PingPong(value, 1f) : Mathf.Repeat(value, 1f);
        return Mathf.Clamp(Mathf.FloorToInt(value * TextureSize), 0, TextureSize - 1);
    }

    private static void Add(Color color, float area, float[] weights, Vector3[] colors)
    {
        if (color.a < .1f || area <= 0f) return;
        int r = Mathf.Min(Bins - 1, Mathf.FloorToInt(Mathf.Clamp01(color.r) * Bins));
        int g = Mathf.Min(Bins - 1, Mathf.FloorToInt(Mathf.Clamp01(color.g) * Bins));
        int b = Mathf.Min(Bins - 1, Mathf.FloorToInt(Mathf.Clamp01(color.b) * Bins));
        int index = (r * Bins + g) * Bins + b;
        float weight = area * color.a;
        weights[index] += weight;
        colors[index] += new Vector3(color.r, color.g, color.b) * weight;
    }

    private static Color[] ReadTexture(Texture source)
    {
        // GPU copy works even when an imported skin texture has Read/Write disabled.
        RenderTexture previous = RenderTexture.active;
        RenderTexture temporary = RenderTexture.GetTemporary(TextureSize, TextureSize, 0,
            RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
        var readable = new Texture2D(TextureSize, TextureSize, TextureFormat.RGBA32, false);
        try
        {
            Graphics.Blit(source, temporary);
            RenderTexture.active = temporary;
            readable.ReadPixels(new Rect(0, 0, TextureSize, TextureSize), 0, 0);
            readable.Apply();
            return readable.GetPixels();
        }
        finally
        {
            RenderTexture.active = previous;
            RenderTexture.ReleaseTemporary(temporary);
            Object.Destroy(readable);
        }
    }
}
