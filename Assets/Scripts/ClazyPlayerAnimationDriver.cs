using UnityEngine;

/// <summary>Bridges the existing side-scroller movement states to the character's Animator.</summary>
[DefaultExecutionOrder(1100)]
public sealed class ClazyPlayerAnimationDriver : MonoBehaviour
{
    [SerializeField] private Animator animator;
    [SerializeField] private PlayerVerticalMovement vertical;
    [SerializeField] private GameModeController modes;
    [SerializeField] private Transform visualRoot;
    private bool wasAirborne;
    private bool wasVehicleStationary;
    private enum MenuKnockStage { None, Falling, Down, GettingUp }
    private MenuKnockStage menuKnockStage;
    private float menuDownElapsed;
    [SerializeField, Range(1f, 4f)] private float menuFastGetUpSpeed = 2.2f;
    private bool menuFastFinish;
    private bool recoveringFromMenuKnock;
    private float menuGroundVelocity;
    private float menuRecoveryElapsed;
    [Tooltip("World-space distance from the lowest foot/toe bone to the shoe sole, calibrated by Fit Character to Player.")]
    [SerializeField, Min(0f)] private float soleOffset = .03f;
    private BoxCollider hitbox;
    private Transform[] footBones;
    private float groundedPoseOffset;
    private float landingPoseVelocity;
    private bool blendingLandingPose;
    [Header("Landing Animation")]
    [Tooltip("Smooths the shoe-height correction as the landing clip blends into the running cycle.")]
    [SerializeField, Range(.04f, .3f)] private float landingPoseBlendSeconds = .12f;
    private PlayerSprint sprint;

    [Header("Body Collision - Fractions of Standing Height")]
    [SerializeField] private PlayerHurtbox.Settings bodyCollision = new PlayerHurtbox.Settings();
    private PlayerHurtbox bodyHurtbox;

    private static readonly int Running = Animator.StringToHash("Running");
    private static readonly int Airborne = Animator.StringToHash("Airborne");
    private static readonly int Jump = Animator.StringToHash("Jump");
    private static readonly int Ducking = Animator.StringToHash("Ducking");
    private static readonly int VerticalVelocity = Animator.StringToHash("VerticalVelocity");
    private static readonly int Land = Animator.StringToHash("Land");
    private static readonly int Sprint = Animator.StringToHash("Sprint");
    private static readonly int Dashing = Animator.StringToHash("Dashing");
    private static readonly int Grabbed = Animator.StringToHash("Grabbed");
    private static readonly int Idle = Animator.StringToHash("Base Layer.Idle");
    private static readonly int LandingState = Animator.StringToHash("Land");
    private static readonly int MenuFall = Animator.StringToHash("Base Layer.Menu Fall");
    private static readonly int MenuGetUp = Animator.StringToHash("Base Layer.Menu Get Up");

    public bool MenuReadyToStart => menuKnockStage == MenuKnockStage.None && !recoveringFromMenuKnock
        && (animator == null || (!animator.IsInTransition(0)
            && animator.GetCurrentAnimatorStateInfo(0).fullPathHash == Idle));

    public void FinishMenuKnockDownFast()
    {
        menuFastFinish = true;
        if (menuKnockStage == MenuKnockStage.Down) BeginMenuGetUp();
        else if (menuKnockStage != MenuKnockStage.None) animator.speed = menuFastGetUpSpeed;
    }

    public bool TryMenuKnockDown()
    {
        if (animator == null || modes == null || modes.CurrentMode != GameModeController.Mode.Menu
            || modes.IsMenuStarting || menuKnockStage != MenuKnockStage.None
            || !animator.HasState(0, MenuFall) || !animator.HasState(0, MenuGetUp)) return false;
        menuKnockStage = MenuKnockStage.Falling;
        menuFastFinish = false;
        recoveringFromMenuKnock = false;
        animator.speed = 1f;
        animator.CrossFadeInFixedTime(MenuFall, .06f, 0, 0f);
        return true;
    }

    public void CancelMenuKnockDown()
    {
        if (menuKnockStage == MenuKnockStage.None) return;
        menuKnockStage = MenuKnockStage.None;
        menuFastFinish = false;
        recoveringFromMenuKnock = true;
        menuGroundVelocity = 0f;
        menuRecoveryElapsed = 0f;
        animator.speed = 1f;
        animator.CrossFadeInFixedTime(Idle, .08f, 0, 0f);
    }

    private void Awake()
    {
        if (vertical == null) vertical = GetComponent<PlayerVerticalMovement>();
        if (modes == null) modes = FindAnyObjectByType<GameModeController>();
        if (animator == null && visualRoot != null) animator = visualRoot.GetComponentInChildren<Animator>();
        hitbox = GetComponent<BoxCollider>();
        sprint = GetComponent<PlayerSprint>();
        if (animator != null) RefreshAnimationClock();
        if (animator != null && animator.isHuman)
            footBones = new[] {
                animator.GetBoneTransform(HumanBodyBones.LeftFoot),
                animator.GetBoneTransform(HumanBodyBones.RightFoot),
                animator.GetBoneTransform(HumanBodyBones.LeftToes),
                animator.GetBoneTransform(HumanBodyBones.RightToes)
            };
        wasAirborne = vertical != null && vertical.IsAirborne;
        if (animator != null && animator.isHuman)
        {
            bodyHurtbox = GetComponent<PlayerHurtbox>();
            if (bodyHurtbox == null) bodyHurtbox = gameObject.AddComponent<PlayerHurtbox>();
            bodyHurtbox.Initialize(animator, bodyCollision);
        }
    }

    private void Update()
    {
        if (animator == null || vertical == null) return;

        RefreshAnimationClock();

        if (menuKnockStage != MenuKnockStage.None)
        {
            TickMenuKnockDown();
            return;
        }

        bool grabbed = modes != null && modes.AmbushBound;
        bool vehicleEscape = modes != null && modes.VehicleEscapeActive;
        bool vehicleApproach = vehicleEscape && modes.VehicleEscapeApproaching;
        bool vehicleStationary = vehicleEscape && !vehicleApproach;
        bool airborne = vertical.IsAirborne && !grabbed && !vehicleEscape;
        bool ducking = vertical.IsDucking && !airborne && !grabbed && !vehicleEscape;
        // Locked controls during a reaction cue do not mean the character stopped running.
        bool running = modes != null && modes.IsPlaying && (!vehicleEscape || vehicleApproach) && !grabbed;

        animator.SetBool(Grabbed, grabbed);
        animator.SetBool(Running, running);
        animator.SetBool(Airborne, airborne);
        animator.SetBool(Ducking, ducking);
        animator.SetBool(Dashing, running && !airborne && !ducking && sprint != null && sprint.IsDashing);
        animator.SetFloat(VerticalVelocity, vertical.VerticalVelocity);
        // Use actual accepted effort, including overload and acceleration, rather than raw Shift input.
        // Held sprint continues to blend Jog/Mvm_Dash; a tap uses its own ChargeBoost state.
        animator.SetFloat(Sprint, running && !airborne && !ducking && sprint != null
            ? sprint.DistanceSprintIntensity : 0f, .12f, Time.unscaledDeltaTime);
        if (grabbed || vehicleEscape) { animator.ResetTrigger(Jump); animator.ResetTrigger(Land); }
        else
        {
            if (!wasAirborne && airborne)
            {
                blendingLandingPose = false;
                landingPoseVelocity = 0f;
                animator.SetTrigger(Jump);
            }
            if (wasAirborne && !airborne)
            {
                blendingLandingPose = true;
                landingPoseVelocity = 0f;
                animator.SetTrigger(Land);
            }
        }
        // Keep running while approaching the driver's door, then settle into idle
        // only when the lock interaction begins.
        if (vehicleStationary && !wasVehicleStationary)
            animator.CrossFadeInFixedTime(Idle, .08f, 0, 0f);
        wasVehicleStationary = vehicleStationary;
        wasAirborne = airborne;
    }

    private void TickMenuKnockDown()
    {
        AnimatorStateInfo state = animator.GetCurrentAnimatorStateInfo(0);
        if (menuKnockStage == MenuKnockStage.Falling && state.fullPathHash == MenuFall
            && state.normalizedTime >= .92f)
        {
            if (menuFastFinish) BeginMenuGetUp();
            else
            {
                animator.speed = 0f;
                menuDownElapsed = 0f;
                menuKnockStage = MenuKnockStage.Down;
            }
        }
        else if (menuKnockStage == MenuKnockStage.Down)
        {
            menuDownElapsed += Time.unscaledDeltaTime;
            if (menuDownElapsed >= .45f)
                BeginMenuGetUp();
        }
        else if (menuKnockStage == MenuKnockStage.GettingUp && state.fullPathHash == MenuGetUp
            && state.normalizedTime >= .8f)
        {
            menuKnockStage = MenuKnockStage.None;
            recoveringFromMenuKnock = true;
            menuGroundVelocity = 0f;
            menuRecoveryElapsed = 0f;
            animator.speed = 1f;
            // Finish blending before the imported get-up clip loops back to the floor.
            animator.CrossFadeInFixedTime(Idle, .18f, 0, 0f);
        }
    }

    private void BeginMenuGetUp()
    {
        animator.speed = menuFastFinish ? menuFastGetUpSpeed : 1f;
        animator.CrossFadeInFixedTime(MenuGetUp, .08f, 0, 0f);
        menuKnockStage = MenuKnockStage.GettingUp;
    }

    private void RefreshAnimationClock()
    {
        // The menu pauses gameplay at timeScale=0, but the character should still breathe.
        // During play use scaled time so jumps and bullet-time QTE remain synchronized.
        AnimatorUpdateMode mode = modes != null && modes.CurrentMode == GameModeController.Mode.Menu
            ? AnimatorUpdateMode.UnscaledTime : AnimatorUpdateMode.Normal;
        if (animator.updateMode != mode) animator.updateMode = mode;
    }

    private void LateUpdate()
    {
        if (visualRoot == null) return;
        // Cancel ALL inherited hitbox scaling. The model is fitted uniformly in world units.
        Vector3 parentScale = transform.lossyScale;
        visualRoot.localScale = new Vector3(
            1f / Mathf.Max(.001f, parentScale.x),
            1f / Mathf.Max(.001f, parentScale.y),
            1f / Mathf.Max(.001f, parentScale.z));
        if (hitbox == null) return;
        Vector3 anchor = hitbox.center - Vector3.up * (hitbox.size.y * .5f);
        visualRoot.localPosition = anchor;

        // Grounded clips have different imported pelvis/root heights. Align their supporting
        // shoe after Animator evaluation, rather than trusting the FBX origin to be the floor.
        // In air keep the last correction: leg tucking must not cancel the gameplay jump.
        bool externallyPositioned = modes != null && (modes.AmbushBound || modes.VehicleEscapeActive);
        if (!externallyPositioned && menuKnockStage == MenuKnockStage.None
            && vertical != null && !vertical.IsAirborne && footBones != null)
        {
            float lowest = float.PositiveInfinity;
            foreach (Transform foot in footBones)
                if (foot != null) lowest = Mathf.Min(lowest, foot.position.y);
            if (!float.IsPositiveInfinity(lowest))
            {
                float targetOffset = transform.TransformPoint(anchor).y - (lowest - soleOffset);
                if (recoveringFromMenuKnock)
                {
                    menuRecoveryElapsed += Time.unscaledDeltaTime;
                    groundedPoseOffset = Mathf.SmoothDamp(groundedPoseOffset, targetOffset,
                        ref menuGroundVelocity, .15f, Mathf.Infinity, Time.unscaledDeltaTime);
                    if ((Mathf.Abs(groundedPoseOffset - targetOffset) < .005f
                        && Mathf.Abs(menuGroundVelocity) < .02f) || menuRecoveryElapsed >= .5f)
                    {
                        groundedPoseOffset = targetOffset;
                        recoveringFromMenuKnock = false;
                    }
                }
                else if (blendingLandingPose)
                {
                    groundedPoseOffset = Mathf.SmoothDamp(groundedPoseOffset, targetOffset,
                        ref landingPoseVelocity, landingPoseBlendSeconds, Mathf.Infinity, Time.deltaTime);
                    AnimatorStateInfo state = animator.GetCurrentAnimatorStateInfo(0);
                    if (!animator.IsInTransition(0) && state.shortNameHash != LandingState
                        && Mathf.Abs(groundedPoseOffset - targetOffset) < .005f
                        && Mathf.Abs(landingPoseVelocity) < .03f)
                        blendingLandingPose = false;
                }
                else groundedPoseOffset = targetOffset;
            }
        }
        visualRoot.position += Vector3.up * groundedPoseOffset;
    }

    private void OnDrawGizmosSelected()
    {
        if (bodyHurtbox != null) bodyHurtbox.DrawShapes();
    }
}
