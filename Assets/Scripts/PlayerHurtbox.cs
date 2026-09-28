using System;
using UnityEngine;

/// <summary>Forgiving, bone-driven damage shapes in the side-scroller's fixed depth lane.</summary>
[DefaultExecutionOrder(1150)]
[DisallowMultipleComponent]
public sealed class PlayerHurtbox : MonoBehaviour
{
    [Serializable]
    public sealed class Settings
    {
        [Tooltip("Head half-width as a fraction of the original standing height.")]
        [Range(.04f, .12f)] public float headHalfWidth = .07f;
        [Range(.08f, .2f)] public float headHeight = .14f;
        [Tooltip("Lift from the Humanoid head joint to the skull centre, relative to standing height.")]
        [Range(0f, .1f)] public float headLift = .055f;
        [Tooltip("Torso padding around hips, chest and neck. Arms are excluded.")]
        [Range(.03f, .12f)] public float torsoHalfWidth = .065f;
        [Range(.02f, .12f)] public float torsoEndPadding = .065f;
        [Tooltip("Narrow lower-body band preserves low-obstacle collision without counting swinging feet.")]
        [Range(.03f, .12f)] public float lowerBodyHalfWidth = .075f;
        [Tooltip("Small vertical allowance at the soles, relative to standing height.")]
        [Range(0f, .05f)] public float soleForgiveness = .015f;
    }

    public const int MaximumParts = 3;
    private readonly BoxCollider[] parts = new BoxCollider[MaximumParts];
    private Animator animator;
    private BoxCollider legacy;
    private PlayerVerticalMovement vertical;
    private Settings settings;
    private Transform head, hips, chest, neck;
    private Transform[] feet;
    private float standingHeight, laneDepth;
    private bool legacyWasEnabled;
    public bool Ready { get; private set; }

    // The animation driver owns these settings in edit mode, so no scene rewrite is needed.
    public void Initialize(Animator rig, Settings configuration)
    {
        if (Ready || rig == null || !rig.isHuman) return;
        legacy = GetComponent<BoxCollider>();
        if (legacy == null) return;
        animator = rig;
        head = rig.GetBoneTransform(HumanBodyBones.Head);
        hips = rig.GetBoneTransform(HumanBodyBones.Hips);
        chest = rig.GetBoneTransform(HumanBodyBones.UpperChest);
        if (chest == null) chest = rig.GetBoneTransform(HumanBodyBones.Chest);
        if (chest == null) chest = rig.GetBoneTransform(HumanBodyBones.Spine);
        neck = rig.GetBoneTransform(HumanBodyBones.Neck);
        if (neck == null) neck = chest;
        if (head == null || hips == null || chest == null) return;
        settings = configuration ?? new Settings();
        vertical = GetComponent<PlayerVerticalMovement>();
        feet = new[] { rig.GetBoneTransform(HumanBodyBones.LeftFoot),
            rig.GetBoneTransform(HumanBodyBones.RightFoot), rig.GetBoneTransform(HumanBodyBones.LeftToes),
            rig.GetBoneTransform(HumanBodyBones.RightToes) };
        Bounds original = Geometry(legacy);
        standingHeight = Mathf.Max(.1f, original.size.y);
        laneDepth = Mathf.Max(.05f, original.size.z);
        string[] names = { "Hurtbox Head", "Hurtbox Torso", "Hurtbox Lower Body" };
        for (int i = 0; i < parts.Length; i++)
        {
            var part = new GameObject(names[i]);
            part.layer = gameObject.layer;
            part.transform.SetParent(transform, false);
            parts[i] = part.AddComponent<BoxCollider>();
            parts[i].isTrigger = true;
        }
        legacyWasEnabled = legacy.enabled;
        Ready = true;
        RefreshPose();
        // Keep the old box's size/centre for movement and visual foot anchoring only.
        legacy.enabled = false;
    }

    private void LateUpdate()
    {
        RefreshPose();
        if (Ready) Physics.SyncTransforms();
    }

    public void RefreshPose()
    {
        if (!Ready || animator == null || head == null || hips == null || chest == null) return;
        float h = standingHeight;
        Vector3 skull = head.position + Vector3.up * (h * settings.headLift);
        SetPart(0, new Bounds(skull, new Vector3(h * settings.headHalfWidth * 2f,
            h * settings.headHeight, laneDepth)));
        Vector3 low = Vector3.Min(hips.position, chest.position);
        Vector3 high = Vector3.Max(hips.position, chest.position);
        if (neck != null) { low = Vector3.Min(low, neck.position); high = Vector3.Max(high, neck.position); }
        low -= new Vector3(h * settings.torsoHalfWidth, h * settings.torsoEndPadding, 0f);
        high += new Vector3(h * settings.torsoHalfWidth, h * settings.torsoEndPadding, 0f);
        SetPart(1, new Bounds((low + high) * .5f, high - low));

        // Feet only supply vertical clearance in air; their horizontal swing never widens the shape.
        float floor = Geometry(legacy).min.y;
        if (vertical != null && !vertical.IsAirborne) floor = vertical.GroundY;
        else
        {
            floor = float.PositiveInfinity;
            foreach (Transform foot in feet) if (foot != null) floor = Mathf.Min(floor, foot.position.y);
            if (float.IsPositiveInfinity(floor)) floor = Geometry(legacy).min.y;
        }
        floor += h * settings.soleForgiveness;
        float top = Mathf.Max(floor + h * .04f, hips.position.y + h * settings.torsoEndPadding);
        SetPart(2, new Bounds(new Vector3(hips.position.x, (floor + top) * .5f, transform.position.z),
            new Vector3(h * settings.lowerBodyHalfWidth * 2f, top - floor, laneDepth)));
    }

    private void SetPart(int index, Bounds world)
    {
        // Animation may sway in Z. Gameplay stays in one 2D depth lane.
        Vector3 centre = world.center;
        centre.z = transform.TransformPoint(legacy.center).z;
        Transform part = parts[index].transform;
        part.SetPositionAndRotation(centre, Quaternion.identity);
        Vector3 scale = transform.lossyScale;
        part.localScale = new Vector3(1f / Mathf.Max(.001f, Mathf.Abs(scale.x)),
            1f / Mathf.Max(.001f, Mathf.Abs(scale.y)), 1f / Mathf.Max(.001f, Mathf.Abs(scale.z)));
        parts[index].center = Vector3.zero;
        parts[index].size = new Vector3(Mathf.Max(.02f, world.size.x), Mathf.Max(.02f, world.size.y), laneDepth);
    }

    private void OnDisable()
    {
        foreach (var part in parts) if (part != null) part.enabled = false;
        if (Ready && legacy != null) legacy.enabled = legacyWasEnabled;
    }
    private void OnEnable()
    {
        if (!Ready) return;
        foreach (var part in parts) if (part != null) part.enabled = true;
        if (legacy != null) legacy.enabled = false;
    }

    // Calculate geometry directly: disabled legacy boxes have empty Collider.bounds,
    // and physics bounds can lag behind this frame's Transform/animation changes.
    private static Bounds Geometry(BoxCollider box)
    {
        Vector3 scale = box.transform.lossyScale;
        return new Bounds(box.transform.TransformPoint(box.center),
            Vector3.Scale(box.size, new Vector3(Mathf.Abs(scale.x), Mathf.Abs(scale.y), Mathf.Abs(scale.z))));
    }
    public static int PartCount(Collider player)
    {
        var body = player != null ? player.GetComponent<PlayerHurtbox>() : null;
        return body != null && body.isActiveAndEnabled && body.Ready ? MaximumParts : 1;
    }
    public static Bounds PartBounds(Collider player, int index)
    {
        if (player == null) return default;
        var body = player.GetComponent<PlayerHurtbox>();
        if (body != null && body.isActiveAndEnabled && body.Ready) return Geometry(body.parts[index]);
        return player is BoxCollider box ? Geometry(box) : player.bounds;
    }
    public static Bounds BoundsFor(Collider player)
    {
        Bounds bounds = PartBounds(player, 0);
        for (int i = 1; i < PartCount(player); i++) bounds.Encapsulate(PartBounds(player, i));
        return bounds;
    }
    public void DrawShapes()
    {
        if (!Ready) return;
        Color[] colors = { new Color(1f, .75f, .1f), new Color(.2f, 1f, .5f), new Color(.2f, .7f, 1f) };
        for (int i = 0; i < parts.Length; i++)
        {
            Gizmos.color = colors[i];
            Bounds bounds = Geometry(parts[i]);
            Gizmos.DrawWireCube(bounds.center, bounds.size);
        }
    }
}
