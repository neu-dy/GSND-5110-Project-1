using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>Flashes a gold silhouette without changing the animated model or its hitbox.</summary>
[DisallowMultipleComponent]
[DefaultExecutionOrder(1500)]
public sealed class NearMissOutline : MonoBehaviour
{
    private sealed class Source
    {
        public Renderer original;
        public Renderer outline;
    }

    private static readonly int OutlineColor = Shader.PropertyToID("_OutlineColor");
    private static readonly int OutlineWidth = Shader.PropertyToID("_OutlineWidth");
    private readonly List<Source> sources = new List<Source>();
    private MaterialPropertyBlock properties;
    private Material material;
    private Color color;
    private float duration;
    private float activeDuration;
    private float width;
    private float remaining;

    public void Initialize(Color tint, float seconds, float worldWidth)
    {
        properties = new MaterialPropertyBlock();
        color = tint;
        duration = Mathf.Max(.01f, seconds);
        width = Mathf.Max(.001f, worldWidth);
        Shader shader = Resources.Load<Shader>("NearMissGoldOutline");
        if (shader == null)
        {
            Debug.LogError("Near-miss outline shader is missing from Resources.", this);
            return;
        }
        material = new Material(shader) { name = "Near Miss Gold Outline (Runtime)" };
        CacheVisibleMeshes();
    }

    public void Flash()
    {
        if (material == null) return;
        activeDuration = remaining = duration;
    }

    [ContextMenu("Preview Gold Outline")]
    private void PreviewGoldOutline()
    {
        Flash();
        if (material != null) activeDuration = remaining = Mathf.Max(2f, duration);
    }

    public void Stop()
    {
        remaining = 0f;
        foreach (Source source in sources)
            if (source.outline != null) source.outline.enabled = false;
    }

    private void CacheVisibleMeshes()
    {
        SkinnedMeshRenderer[] skins = GetComponentsInChildren<SkinnedMeshRenderer>(true);
        bool hasVisibleSkin = false;
        foreach (SkinnedMeshRenderer skin in skins)
            if (skin.enabled && skin.gameObject.activeInHierarchy && skin.sharedMesh != null)
                hasVisibleSkin = true;

        if (hasVisibleSkin)
        {
            foreach (SkinnedMeshRenderer skin in skins)
            {
                if (!skin.enabled || !skin.gameObject.activeInHierarchy || skin.sharedMesh == null) continue;
                GameObject shellObject = MakeShell(skin);
                SkinnedMeshRenderer shell = shellObject.AddComponent<SkinnedMeshRenderer>();
                shell.sharedMesh = skin.sharedMesh;
                shell.bones = skin.bones;
                shell.rootBone = skin.rootBone;
                shell.localBounds = skin.localBounds;
                shell.updateWhenOffscreen = true;
                ConfigureRenderer(shell, skin, skin.sharedMesh.subMeshCount);
                sources.Add(new Source { original = skin, outline = shell });
            }
            return;
        }

        foreach (MeshRenderer body in GetComponentsInChildren<MeshRenderer>(true))
        {
            if (!body.enabled || !body.gameObject.activeInHierarchy) continue;
            MeshFilter filter = body.GetComponent<MeshFilter>();
            if (filter == null || filter.sharedMesh == null) continue;
            GameObject shellObject = MakeShell(body);
            shellObject.AddComponent<MeshFilter>().sharedMesh = filter.sharedMesh;
            MeshRenderer shell = shellObject.AddComponent<MeshRenderer>();
            ConfigureRenderer(shell, body, filter.sharedMesh.subMeshCount);
            sources.Add(new Source { original = body, outline = shell });
        }
    }

    private GameObject MakeShell(Renderer original)
    {
        GameObject shell = new GameObject("Near Miss Gold Outline");
        shell.transform.SetParent(original.transform, false);
        shell.layer = original.gameObject.layer;
        shell.AddComponent<NearMissOutlineShell>();
        return shell;
    }

    private void ConfigureRenderer(Renderer outline, Renderer original, int submeshes)
    {
        Material[] materials = new Material[Mathf.Max(1, submeshes)];
        for (int i = 0; i < materials.Length; i++) materials[i] = material;
        outline.sharedMaterials = materials;
        outline.shadowCastingMode = ShadowCastingMode.Off;
        outline.receiveShadows = false;
        outline.sortingLayerID = original.sortingLayerID;
        outline.sortingOrder = original.sortingOrder;
        outline.enabled = false;
    }

    private void LateUpdate()
    {
        if (remaining <= 0f || material == null) return;
        float progress = 1f - remaining / activeDuration;
        Color pulse = color;
        pulse.a *= Mathf.Sin(Mathf.PI * Mathf.Clamp01(progress));
        properties.Clear();
        properties.SetColor(OutlineColor, pulse);
        properties.SetFloat(OutlineWidth, width);
        foreach (Source source in sources)
        {
            if (source.outline == null) continue;
            source.outline.enabled = source.original != null && source.original.enabled
                && source.original.gameObject.activeInHierarchy;
            if (source.outline.enabled) source.outline.SetPropertyBlock(properties);
        }
        remaining = Mathf.Max(0f, remaining - Time.unscaledDeltaTime);
        if (remaining <= 0f) Stop();
    }

    private void OnDestroy()
    {
        if (material != null) Destroy(material);
    }
}

/// <summary>Excludes the temporary gold shell from appearance-color sampling.</summary>
public sealed class NearMissOutlineShell : MonoBehaviour { }
