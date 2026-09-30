using System;
using UnityEngine;

// A piece of the curtain grows into an ordinary approaching obstacle.
// The added solid stays part of the original moving/breakable obstacle.
[DefaultExecutionOrder(1250)]
public sealed class ObstacleMorph : MonoBehaviour
{
    [Serializable]
    public sealed class Settings
    {
        public bool enabled = true;
        [Range(0f, 1f)] public float chance = .18f;
        [Tooltip("Maximum chance for Low/High obstacles to become wider; Choice keeps the base Chance.")]
        [Range(0f, 1f)] public float maximumWidthChance = .5f;
        [Tooltip("Fraction of the level finish distance at which width-changing chance reaches its maximum.")]
        [Range(.1f, .95f)] public float widthChancePeakAtFinishFraction = .7f;
        [Min(0f)] public float delaySeconds = .05f;
        [Tooltip("The colored obstacle must reach this horizontal screen position before the black fill begins.")]
        [Range(.7f, .98f)] public float revealViewport = .86f;
        [Min(.28f)] public float growthSeconds = .3f;
        [Tooltip("Time left to react after the black shape has finished growing.")]
        [Min(.1f)] public float minimumReactionSeconds = .3f;
        [Min(.1f)] public float widthExtension = .8f;
        [Tooltip("Makes the upper fill too tall to jump over, leaving the crouch route.")]
        [Min(1f)] public float upperFillHeight = 3.2f;

        public void Validate()
        {
            chance = Mathf.Clamp01(chance);
            maximumWidthChance = Mathf.Clamp(maximumWidthChance <= 0f ? .5f : maximumWidthChance,
                chance, 1f);
            widthChancePeakAtFinishFraction = Mathf.Clamp(
                widthChancePeakAtFinishFraction <= 0f ? .7f : widthChancePeakAtFinishFraction,
                .1f, .95f);
            delaySeconds = Mathf.Max(0f, delaySeconds);
            if (revealViewport <= 0f) revealViewport = .86f;
            revealViewport = Mathf.Clamp(revealViewport, .7f, .98f);
            growthSeconds = Mathf.Max(.28f, growthSeconds);
            minimumReactionSeconds = Mathf.Max(.1f, minimumReactionSeconds);
            widthExtension = Mathf.Max(.1f, widthExtension);
            upperFillHeight = Mathf.Max(1f, upperFillHeight);
        }

        public Settings Copy() => (Settings)MemberwiseClone();

        public float ChanceAtDistance(ObstacleMovement.CorruptionKind kind, double meters,
            float finishMeters)
        {
            if (kind != ObstacleMovement.CorruptionKind.Low
                && kind != ObstacleMovement.CorruptionKind.High) return chance;
            return Mathf.Lerp(chance, maximumWidthChance,
                Mathf.Clamp01((float)(Math.Max(0d, meters) /
                    (Math.Max(1f, finishMeters) * widthChancePeakAtFinishFraction))));
        }
    }

    private static Material blackMaterial;
    private ObstacleMovement movement;
    private ObstacleSpawner spawner;
    private Collider player;
    private GameModeController modes;
    private BoxCollider core;
    private Settings settings;
    private Transform fill;
    private Collider fillCollider;
    private Renderer fillBody;
    private float elapsed;
    private float growth;
    private bool started;
    private bool fillBelow;

    public void Initialize(ObstacleMovement obstacle, ObstacleSpawner owner, Collider target,
        GameModeController controller, Settings configuration)
    {
        movement = obstacle;
        spawner = owner;
        player = target;
        modes = controller;
        settings = configuration.Copy();
        settings.Validate();
    }

    private void Start()
    {
        core = GetComponent<BoxCollider>();
        if (movement == null || spawner == null || player == null || core == null
            || settings == null || !settings.enabled)
            enabled = false;
    }

    private void Update()
    {
        if (Time.timeScale <= 0f || modes == null || !modes.IsPlaying) return;
        if (movement.HitPlayer || !core.enabled) { enabled = false; return; }
        if (!started)
        {
            elapsed += Time.deltaTime;
            if (elapsed < settings.delaySeconds) return;
            Bounds obstacle = core.bounds;
            Camera camera = Camera.main;
            if (camera == null) { enabled = false; return; }
            // Let the original color enter the frame before the curtain visibly fills it.
            Vector3 leadingFace = new Vector3(obstacle.min.x, obstacle.center.y, obstacle.center.z);
            if (camera.WorldToViewportPoint(leadingFace).x > settings.revealViewport) return;
            float speed = movement.BaseObstacleSpeed * spawner.SpeedMultiplier;
            float arrival = (obstacle.min.x - PlayerHurtbox.BoundsFor(player).max.x) / Mathf.Max(.1f, speed);
            // Never change a committed jump/crouch after its fair reaction window.
            if (arrival < settings.growthSeconds + settings.minimumReactionSeconds)
            {
                enabled = false;
                return;
            }
            if (movement.MorphKind == ObstacleMovement.CorruptionKind.High
                && !spawner.CanSafelyWidenHead(obstacle, settings.widthExtension, speed))
            {
                enabled = false;
                return;
            }
            if (movement.MorphKind == ObstacleMovement.CorruptionKind.Choice)
            {
                fillBelow = UnityEngine.Random.value < .5f;
                if (fillBelow)
                {
                    PlayerVerticalMovement vertical = player.GetComponent<PlayerVerticalMovement>();
                    if (vertical == null || vertical.JumpClearanceSeconds(obstacle.max.y + .08f - vertical.GroundY) <= 0f)
                    { enabled = false; return; }
                }
                else if (!spawner.CanSafelyWidenHead(obstacle, 0f, speed))
                {
                    // The crouch-only route also needs enough pursuit reserve.
                    enabled = false;
                    return;
                }
            }
            CreateFill();
            started = true;
        }
        growth = Mathf.Min(settings.growthSeconds, growth + Time.deltaTime);
        UpdateFill(Mathf.SmoothStep(0f, 1f, growth / settings.growthSeconds));
        if (fillCollider != null)
        {
            movement.RegisterAddedPart(fillCollider, fillBody);
            fillCollider = null;
        }
        if (growth >= settings.growthSeconds) enabled = false;
    }

    private void CreateFill()
    {
        GameObject piece = GameObject.CreatePrimitive(PrimitiveType.Cube);
        piece.name = "Curtain-filled obstacle";
        piece.tag = "Obstacle";
        piece.transform.SetParent(transform, true);
        fill = piece.transform;
        fillBody = piece.GetComponent<Renderer>();
        fillBody.sharedMaterial = BlackMaterial();
        fillCollider = piece.GetComponent<Collider>();
    }

    private void UpdateFill(float amount)
    {
        Bounds original = core.bounds;
        Vector3 center = original.center;
        Vector3 size = original.size;
        const float overlap = .025f;
        switch (movement.MorphKind)
        {
            case ObstacleMovement.CorruptionKind.Low:
            case ObstacleMovement.CorruptionKind.High:
                float width = Mathf.Max(.01f, settings.widthExtension * amount);
                center.x = original.max.x + width * .5f - overlap;
                size.x = width + overlap * 2f;
                break;
            case ObstacleMovement.CorruptionKind.Choice:
                float height;
                if (fillBelow)
                {
                    PlayerVerticalMovement vertical = player.GetComponent<PlayerVerticalMovement>();
                    float ground = vertical != null ? vertical.GroundY : 0f;
                    height = Mathf.Max(.01f, original.min.y - ground) * amount;
                    center.y = original.min.y - height * .5f + overlap;
                }
                else
                {
                    height = settings.upperFillHeight * amount;
                    center.y = original.max.y + height * .5f - overlap;
                }
                size.y = Mathf.Max(.01f, height + overlap * 2f);
                size.x += overlap * 2f;
                break;
        }
        fill.position = center;
        Vector3 parentScale = transform.lossyScale;
        fill.localScale = new Vector3(size.x / Mathf.Max(.001f, Mathf.Abs(parentScale.x)),
            size.y / Mathf.Max(.001f, Mathf.Abs(parentScale.y)),
            size.z / Mathf.Max(.001f, Mathf.Abs(parentScale.z)));
    }

    private static Material BlackMaterial()
    {
        if (blackMaterial != null) return blackMaterial;
        Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
        if (shader == null) shader = Shader.Find("Unlit/Color");
        if (shader == null) shader = Shader.Find("Standard");
        blackMaterial = new Material(shader) { color = Color.black, hideFlags = HideFlags.HideAndDontSave };
        if (blackMaterial.HasProperty("_BaseColor")) blackMaterial.SetColor("_BaseColor", Color.black);
        return blackMaterial;
    }
}
