using UnityEngine;
using UnityEngine.Rendering;

// Model-only scenery for testing depth and motion in the side-scrolling stage.
// Everything generated here is visual: no colliders or gameplay triggers.
[ExecuteAlways]
public sealed class StageGreyboxBackdrop : MonoBehaviour
{
    [Header("Visual scroll")]
    [SerializeField, Min(0f)] private float baseScrollSpeed = 6f;
    [SerializeField, Range(0f, 1f)] private float distantSpeedRatio = 0.16f;
    [SerializeField, Range(0f, 1f)] private float streetSpeedRatio = 0.42f;
    [SerializeField, Range(0f, 1f)] private float floorSpeedRatio = 1f;
    [SerializeField] private bool followObstacleSpeed = true;

    private const float DistantWidth = 5.6f;
    private const float StreetWidth = 5.8f;
    private const float FloorWidth = 1.5f;
    private const int DistantCount = 16;
    private const int StreetCount = 12;
    private const int FloorCount = 24;

    private enum Layer { Distant, Street, Floor }

    private sealed class Tile
    {
        public Transform root;
        public Transform[] parts;
        public Renderer[] renderers;
        public int index;
    }

    private Transform generatedRoot;
    private Tile[] distant;
    private Tile[] street;
    private Tile[] floor;
    private Material[] palette;
    private GameModeController modes;
    private ObstacleSpawner spawner;

    private void OnEnable() => Build();

    private void Update()
    {
        if (generatedRoot == null) Build();
        if (!Application.isPlaying || modes == null || !modes.IsPlaying
            || modes.VehicleEscapeActive || Time.timeScale <= 0f) return;

        float speed = baseScrollSpeed * (followObstacleSpeed && spawner != null
            ? spawner.SpeedMultiplier : 1f) * Time.deltaTime;
        Scroll(distant, DistantWidth, speed * distantSpeedRatio, Layer.Distant);
        Scroll(street, StreetWidth, speed * streetSpeedRatio, Layer.Street);
        Scroll(floor, FloorWidth, speed * floorSpeedRatio, Layer.Floor);
    }

    private void Build()
    {
        if (generatedRoot != null) return;
        if (Application.isPlaying)
        {
            modes = FindAnyObjectByType<GameModeController>();
            spawner = FindAnyObjectByType<ObstacleSpawner>();
        }

        var root = new GameObject("Generated Greybox Scenery");
        root.hideFlags = HideFlags.DontSaveInEditor | HideFlags.DontSaveInBuild;
        generatedRoot = root.transform;
        generatedRoot.SetParent(transform, true);
        generatedRoot.position = Vector3.zero;
        palette = CreatePalette();
        distant = CreateTiles(Layer.Distant, DistantCount, DistantWidth, 8);
        street = CreateTiles(Layer.Street, StreetCount, StreetWidth, 12);
        floor = CreateTiles(Layer.Floor, FloorCount, FloorWidth, 3);
    }

    private static Material[] CreatePalette()
    {
        Shader shader = Shader.Find("Universal Render Pipeline/Lit");
        if (shader == null) shader = Shader.Find("Standard");
        Color[] colors =
        {
            new Color(0.20f, 0.25f, 0.30f), // far silhouette
            new Color(0.27f, 0.32f, 0.35f), // far alternate
            new Color(0.22f, 0.27f, 0.29f), // street wall
            new Color(0.36f, 0.38f, 0.37f), // structural edge
            new Color(0.78f, 0.54f, 0.28f), // sparse warm windows
            new Color(0.49f, 0.56f, 0.56f), // unlit/cool windows
            new Color(0.41f, 0.40f, 0.35f)  // floor markings
        };
        var result = new Material[colors.Length];
        for (int i = 0; i < colors.Length; i++)
        {
            result[i] = new Material(shader) { name = "Greybox Scenery " + i };
            result[i].hideFlags = HideFlags.DontSaveInEditor | HideFlags.DontSaveInBuild;
            if (result[i].HasProperty("_BaseColor")) result[i].SetColor("_BaseColor", colors[i]);
            if (result[i].HasProperty("_Color")) result[i].SetColor("_Color", colors[i]);
            if (result[i].HasProperty("_Smoothness")) result[i].SetFloat("_Smoothness", 0f);
            // URP Lit controls shadow reception on the material, not only on MeshRenderer.
            if (result[i].HasProperty("_ReceiveShadows"))
            {
                result[i].SetFloat("_ReceiveShadows", 0f);
                result[i].EnableKeyword("_RECEIVE_SHADOWS_OFF");
            }
        }
        return result;
    }

    private Tile[] CreateTiles(Layer layer, int count, float width, int partCount)
    {
        var tiles = new Tile[count];
        for (int i = 0; i < count; i++)
        {
            var tileRoot = new GameObject(layer + " " + i).transform;
            tileRoot.SetParent(generatedRoot, false);
            tileRoot.localPosition = new Vector3((i - count / 2f + 0.5f) * width, 0f, 0f);
            var tile = new Tile
            {
                root = tileRoot,
                parts = new Transform[partCount],
                renderers = new Renderer[partCount],
                index = i
            };
            for (int p = 0; p < partCount; p++)
            {
                GameObject box = GameObject.CreatePrimitive(PrimitiveType.Cube);
                box.name = "Model " + p;
                box.transform.SetParent(tileRoot, false);
                Collider collision = box.GetComponent<Collider>();
                collision.enabled = false;
                RemoveGeneratedObject(collision);
                tile.parts[p] = box.transform;
                tile.renderers[p] = box.GetComponent<Renderer>();
                tile.renderers[p].shadowCastingMode = ShadowCastingMode.Off;
                tile.renderers[p].receiveShadows = false;
            }
            Configure(tile, layer);
            tiles[i] = tile;
        }
        return tiles;
    }

    private static float Variation(int index, int salt)
    {
        uint n = (uint)(index * 747796405 + salt * 2891336453L);
        n ^= n >> 16;
        n *= 2246822519u;
        n ^= n >> 13;
        return (n & 65535u) / 65535f;
    }

    private void Configure(Tile tile, Layer layer)
    {
        for (int i = 0; i < tile.parts.Length; i++) tile.parts[i].gameObject.SetActive(false);
        int n = tile.index;
        if (layer == Layer.Distant)
        {
            for (int b = 0; b < 2; b++)
            {
                float height = 6f + 3.5f * Variation(n, b * 7 + 1);
                float width = 1.65f + 0.85f * Variation(n, b * 7 + 2);
                float x = b == 0 ? -1.33f : 1.33f;
                Box(tile, b, new Vector3(x, height * 0.5f - 0.15f, 5.8f + b * 0.45f),
                    new Vector3(width, height, 0.7f), (n + b) % 3 == 0 ? 1 : 0);
                if (Variation(n, b * 7 + 3) > 0.4f)
                    Box(tile, 2 + b, new Vector3(x, height - 0.05f, 5.8f + b * 0.45f),
                        new Vector3(width + 0.14f, 0.14f, 0.83f), 3);
                if (Variation(n, b * 7 + 4) > 0.67f)
                    Box(tile, 4 + b, new Vector3(x + width * 0.19f, height + 0.42f, 5.8f + b * 0.45f),
                        new Vector3(0.07f, 0.85f, 0.07f), 3);
                if (Variation(n, b * 7 + 5) > 0.48f)
                    Box(tile, 6 + b, new Vector3(x - width * 0.2f, height * 0.57f, 5.38f + b * 0.45f),
                        new Vector3(0.13f, 0.2f, 0.025f), Variation(n, b * 7 + 6) > 0.5f ? 4 : 5);
            }
        }
        else if (layer == Layer.Street)
        {
            float height = 3.7f + 1.6f * Variation(n, 21);
            float x = -0.35f + 0.7f * Variation(n, 22);
            float width = 3.65f + 1.2f * Variation(n, 23);
            Box(tile, 0, new Vector3(x, height * 0.5f - 0.1f, 2.7f),
                new Vector3(width, height, 0.75f), 2);
            Box(tile, 1, new Vector3(x, height - 0.07f, 2.7f),
                new Vector3(width + 0.32f, 0.2f, 0.88f), 3);
            float columnX = x + (n % 2 == 0 ? -width * 0.35f : width * 0.35f);
            Box(tile, 2, new Vector3(columnX, height * 0.48f, 2.19f),
                new Vector3(0.24f, height * 0.95f, 0.2f), 3);
            for (int w = 0; w < 6; w++)
            {
                if (Variation(n, 30 + w) < 0.24f) continue;
                Box(tile, 3 + w, new Vector3(x - width * 0.25f + (w % 3) * width * 0.25f,
                    height * (0.37f + (w / 3) * 0.36f), 2.29f),
                    new Vector3(0.32f, 0.48f, 0.025f),
                    Variation(n, 40 + w) > 0.63f ? 4 : 5);
            }
            // A taller, unmistakable landmark occasionally interrupts the roof line.
            if (n % 11 == 7)
            {
                Box(tile, 9, new Vector3(x + width * 0.3f, height + 0.61f, 2.7f),
                    new Vector3(0.14f, 1.25f, 0.15f), 3);
                Box(tile, 10, new Vector3(x + width * 0.3f, height + 1.18f, 2.7f),
                    new Vector3(0.72f, 0.12f, 0.18f), 3);
            }
            // A thin kerb sits behind the active obstacle plane.
            Box(tile, 11, new Vector3(0f, 0.045f, 1.32f),
                new Vector3(StreetWidth - 0.04f, 0.09f, 0.17f), 3);
        }
        else
        {
            float offset = Variation(n, 50);
            Box(tile, 0, new Vector3(-0.18f + offset * 0.25f, 0.014f, -0.35f),
                new Vector3(0.63f + offset * 0.22f, 0.025f, 0.075f), 6);
            if (n % 3 == 0)
                Box(tile, 1, new Vector3(0.28f, 0.014f, 0.48f),
                    new Vector3(0.33f, 0.025f, 0.055f), 6);
        }
    }

    private void Box(Tile tile, int slot, Vector3 localPosition, Vector3 scale, int material)
    {
        tile.parts[slot].gameObject.SetActive(true);
        tile.parts[slot].localPosition = localPosition;
        tile.parts[slot].localScale = scale;
        tile.renderers[slot].sharedMaterial = palette[material];
    }

    private void Scroll(Tile[] tiles, float width, float distance, Layer layer)
    {
        if (tiles == null || distance <= 0f) return;
        float left = -tiles.Length * width * 0.5f;
        for (int i = 0; i < tiles.Length; i++)
        {
            Tile tile = tiles[i];
            Vector3 position = tile.root.localPosition;
            position.x -= distance;
            while (position.x < left - width * 0.5f)
            {
                position.x += tiles.Length * width;
                tile.index += tiles.Length;
                Configure(tile, layer);
            }
            tile.root.localPosition = position;
        }
    }

    private void OnDisable()
    {
        if (generatedRoot != null) RemoveGeneratedObject(generatedRoot.gameObject);
        generatedRoot = null;
        distant = null;
        street = null;
        floor = null;
        if (palette == null) return;
        foreach (Material material in palette) if (material != null) RemoveGeneratedObject(material);
        palette = null;
    }

    private static void RemoveGeneratedObject(Object item)
    {
        if (Application.isPlaying) Destroy(item);
        else DestroyImmediate(item);
    }
}
