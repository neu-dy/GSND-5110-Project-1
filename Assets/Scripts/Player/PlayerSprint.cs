using UnityEngine;

// Vertical input resolves first, so the takeoff frame cannot add ground acceleration.
[DefaultExecutionOrder(100)]
[RequireComponent(typeof(PlayerVerticalMovement))]
public class PlayerSprint : MonoBehaviour
{
    [Header("Sprint - Hold on Ground")]
    [SerializeField] private KeyCode sprintKey = KeyCode.LeftShift;
    [SerializeField, Min(.1f)] private float maximumRightSpeed = 2.7f;
    [SerializeField, Min(.1f)] private float acceleration = 6.5f;
    [SerializeField, Min(.1f)] private float returnSpeed = 1.3f;
    [SerializeField, Min(.1f)] private float returnAcceleration = 5.2f;
    [SerializeField, Range(.35f, .65f)] private float rightmostViewport = .5f;

    [Header("Dash - Tap on Ground")]
    [Tooltip("Independent of the hold-to-sprint Shift key. A dash gives no invulnerability.")]
    [SerializeField] private KeyCode dashKey = KeyCode.LeftControl;
    [SerializeField, Min(.05f)] private float dashDuration = .28f;
    [SerializeField, Min(.1f)] private float dashSpeed = 5f;
    [SerializeField, Min(.1f)] private float dashAcceleration = 28f;
    [SerializeField, Min(.1f)] private float dashBrakeAcceleration = 12f;
    [SerializeField, Min(.1f)] private float dashCooldown = 1.25f;
    [SerializeField, Min(0f)] private float dashBpmCost = 8f;
    [Tooltip("One dash near the ordinary right edge may briefly cross it by this fraction of screen width.")]
    [SerializeField, Range(0f, .15f)] private float dashOverrunViewport = .08f;
    [Tooltip("The edge-crossing dash becomes available again only after returning this close to home.")]
    [SerializeField, Min(0f)] private float dashOverrunRearmDistance = .35f;

    [Header("Heartbeat - BPM")]
    [SerializeField] private float restingBpm = 80f;
    [SerializeField] private float threatBpm = 135f;
    [SerializeField] private float overloadBpm = 175f;
    [SerializeField] private float resumeBpm = 150f;
    [SerializeField, Min(.1f)] private float sprintBpmPerSecond = 20f;
    [SerializeField, Min(.1f)] private float recoveryBpmPerSecond = 15f;
    [SerializeField, Min(.1f)] private float proximityResponse = 25f;
    [Tooltip("Visible horizontal gap, in viewport width, at which environmental anxiety subsides.")]
    [SerializeField, Range(.05f, .5f)] private float calmViewportGap = .22f;
    [Tooltip("Minimum actual heart rate during the hunt. Proximity can still raise it further.")]
    [SerializeField, Min(30f)] private float huntMinimumBpm = 96f;

    [Header("Sprint Recovery and Overload")]
    [Tooltip("Exertion only recovers after this long without sprint effort or rightward motion.")]
    [SerializeField, Min(0f)] private float recoveryDelay = .6f;
    [Tooltip("Temporary extra BPM after overload. Repeated overload refreshes it without stacking.")]
    [SerializeField, Range(0f, 30f)] private float overloadResidualBpm = 10f;
    [Tooltip("Seconds for residual strain to fade, independently of normal heart recovery.")]
    [SerializeField, Min(.1f)] private float overloadResidualDuration = 6f;

    [Header("Heartbeat UI")]
    [SerializeField, Range(0f, .4f)] private float beatScale = .14f;
    [SerializeField, Range(1f, 1.6f)] private float overloadScale = 1.2f;
    [Tooltip("Small visual BPM variation; never changes sprint availability.")]
    [SerializeField, Range(0f, 5f)] private float bpmVariation = 2f;
    [Tooltip("Approximate duration of the slower breathing-like fluctuation, in seconds.")]
    [SerializeField, Range(2f, 12f)] private float variationPeriod = 4.5f;
    [Header("Heartbeat UI - Intro and Flash")]
    [SerializeField, Min(30f)] private float menuPeakBpm = 142f;
    [SerializeField, Min(.1f)] private float menuHeartRiseSeconds = .45f;
    [SerializeField, Min(.1f)] private float menuHeartSettleSeconds = .85f;
    [SerializeField, Range(.02f, .12f)] private float overloadFlashSeconds = .04f;
    [SerializeField, Range(0f, 1f)] private float overloadGlowStrength = .75f;
    public float MenuPeakBpm => menuPeakBpm;
    public float MenuHeartRiseSeconds => menuHeartRiseSeconds;
    public float MenuHeartSettleSeconds => menuHeartSettleSeconds;
    public float OverloadFlashSeconds => overloadFlashSeconds;
    public float OverloadGlowStrength => overloadGlowStrength;
    public float BpmVariation => bpmVariation;
    public float VariationPeriod => variationPeriod;
    public HeartbeatState Heart { get; private set; }
    public float HorizontalSpeed { get; private set; }
    public float MaximumRightSpeed => maximumRightSpeed;
    // Running effort for the odometer, independent of the screen-position boundary.
    public float DistanceSprintIntensity { get; private set; }
    public bool NeedsSprintRelease { get; private set; }
    public bool IsDashing => dashRemaining > 0f;
    public float DashCooldownRemaining => dashCooldownRemaining;
    public Vector3 HomePosition { get; private set; }
    public float BeatScale => beatScale;
    public float OverloadScale => overloadScale;
    private PlayerVerticalMovement vertical;
    private GameModeController modes;
    private float dashRemaining;
    private float dashCooldownRemaining;
    private bool dashOverrunActive;
    private bool dashOverrunSpent;

    void Start()
    {
        HomePosition = transform.position;
        vertical = GetComponent<PlayerVerticalMovement>();
        modes = FindAnyObjectByType<GameModeController>();
        Heart = new HeartbeatState(restingBpm, threatBpm, overloadBpm, resumeBpm,
            recoveryDelay, overloadResidualBpm, overloadResidualDuration);
    }

    void Update()
    {
        if (Time.timeScale <= 0f || modes == null || !modes.IsPlaying) return;
        Step(Time.deltaTime, Input.GetKey(sprintKey), vertical.IsAirborne, vertical.IsDucking,
            Input.GetKeyDown(dashKey));
    }

    // Shared by live input and gameplay validation. No airborne key can change momentum.
    public void Step(float dt, bool held, bool airborne, bool crouching, bool dashPressed = false)
    {
        if (dt <= 0f || Heart == null) return;
        dashCooldownRemaining = Mathf.Max(0f, dashCooldownRemaining - dt);
        // Releasing after overload re-arms input, even while the heart is still recovering.
        // Holding the key throughout recovery must never start the next sprint automatically.
        if (!held) NeedsSprintRelease = false;
        if (modes != null && (modes.AmbushOwnsPlayer || modes.VehicleEscapeActive))
        {
            dashRemaining = 0f;
            return;
        }
        if (airborne || crouching) dashRemaining = 0f;
        float maxX = RightLimitX();
        float x = transform.position.x;
        if (x <= HomePosition.x + dashOverrunRearmDistance) dashOverrunSpent = false;
        if (dashOverrunActive && dashRemaining <= 0f && x <= maxX) dashOverrunActive = false;
        float expectedDashTravel = dashSpeed * dashDuration * .65f;
        bool needsOverrun = maxX - x < expectedDashTravel;
        if (dashPressed && !airborne && !crouching && dashRemaining <= 0f
            && dashCooldownRemaining <= 0f && !Heart.Overheated
            && Heart.Bpm + dashBpmCost < Heart.LimitBpm - .01f
            && (!needsOverrun || (!dashOverrunSpent && dashOverrunViewport > 0f)))
        {
            if (needsOverrun)
            {
                dashOverrunActive = true;
                dashOverrunSpent = true;
            }
            dashRemaining = dashDuration;
            dashCooldownRemaining = dashCooldown;
            Heart.AddExertion(dashBpmCost);
        }
        bool wantsSprint = !airborne && !crouching && held && !NeedsSprintRelease;
        bool wasOverheated = Heart.Overheated;
        // Carrying sprint momentum still costs effort; repeated jumps cannot bypass exhaustion.
        bool exerting = airborne ? HorizontalSpeed > .05f : (wantsSprint || IsDashing) && !wasOverheated;
        bool recoveryAllowed = HorizontalSpeed <= .05f && !exerting;
        float danger = modes != null ? modes.CurtainDanger(calmViewportGap) : 0f;
        Heart.Tick(dt, danger, exerting, sprintBpmPerSecond, recoveryBpmPerSecond, proximityResponse,
            recoveryAllowed, modes != null && modes.IsHunting ? huntMinimumBpm : 0f);
        if (!wasOverheated && Heart.Overheated) NeedsSprintRelease = held;
        if (Heart.Overheated) dashRemaining = 0f;

        if (!airborne)
        {
            bool sprinting = wantsSprint && !Heart.Overheated && !NeedsSprintRelease;
            bool dashing = IsDashing;
            // Beyond the normal edge, even a held Shift yields to a smooth return.
            if (x > maxX && !dashing) sprinting = false;
            float rate = dashing ? dashAcceleration : sprinting ? acceleration : returnAcceleration;
            DistanceSprintIntensity = Mathf.MoveTowards(DistanceSprintIntensity, sprinting || dashing ? 1f : 0f,
                rate / Mathf.Max(.1f, maximumRightSpeed) * dt);
            HorizontalSpeed = dashing ? NextDashSpeed(x, HorizontalSpeed,
                    dashOverrunActive ? ExtendedLimitX() : maxX, dt)
                : NextGroundSpeed(x, HorizontalSpeed, maxX, sprinting, dt);
            dashRemaining = Mathf.Max(0f, dashRemaining - dt);
        }
        else
        {
            // No new sprint in air. Count only inherited motion, never an airborne key press.
            DistanceSprintIntensity = Mathf.Min(DistanceSprintIntensity,
                Mathf.Clamp01(HorizontalSpeed / Mathf.Max(.1f, maximumRightSpeed)));
        }
        Vector3 position = transform.position;
        float travelLimit = dashOverrunActive ? ExtendedLimitX() : maxX;
        position.x = Mathf.Clamp(x + HorizontalSpeed * dt, HomePosition.x, travelLimit);
        if ((position.x >= travelLimit && HorizontalSpeed > 0f) || (position.x <= HomePosition.x && HorizontalSpeed < 0f))
            HorizontalSpeed = 0f;
        transform.position = position;
    }

    public void TickStationaryHeartbeat(float dt,float danger)
    {
        dashRemaining = 0f;
        HorizontalSpeed=DistanceSprintIntensity=0f;
        Heart?.Tick(dt,danger,false,sprintBpmPerSecond,recoveryBpmPerSecond,proximityResponse,true);
    }

    private float RightLimitX()
    {
        float maxX = HomePosition.x + 3f;
        Camera camera = Camera.main;
        if (camera != null)
        {
            float depth = camera.WorldToViewportPoint(HomePosition).z;
            if (depth > camera.nearClipPlane)
                maxX = Mathf.Max(HomePosition.x, camera.ViewportToWorldPoint(new Vector3(rightmostViewport, .5f, depth)).x);
        }
        return maxX;
    }

    private float NextGroundSpeed(float x, float speed, float maxX, bool sprinting, float dt)
    {
        float rate = sprinting ? acceleration : speed > maximumRightSpeed ? dashBrakeAcceleration : returnAcceleration;
        float remaining = sprinting ? maxX - x : x - HomePosition.x;
        float target = Mathf.Min(sprinting ? maximumRightSpeed : returnSpeed,
            Mathf.Sqrt(2f * rate * Mathf.Max(0f, remaining)));
        return Mathf.MoveTowards(speed, sprinting ? target : -target, rate * dt);
    }

    private float ExtendedLimitX()
    {
        Camera camera = Camera.main;
        if (camera == null) return RightLimitX() + 1f;
        float depth = camera.WorldToViewportPoint(HomePosition).z;
        if (depth <= camera.nearClipPlane) return RightLimitX() + 1f;
        float viewport = Mathf.Min(.8f, rightmostViewport + dashOverrunViewport);
        return Mathf.Max(RightLimitX(), camera.ViewportToWorldPoint(new Vector3(viewport, .5f, depth)).x);
    }

    private float NextDashSpeed(float x, float speed, float maxX, float dt)
    {
        float remaining = Mathf.Max(0f, maxX - x);
        float target = Mathf.Min(dashSpeed, Mathf.Sqrt(2f * dashAcceleration * remaining));
        return Mathf.MoveTowards(speed, target, dashAcceleration * dt);
    }

    // A drop only commits when at least one grounded escape fits inside its warning.
    // Forecast uses the same acceleration/boundary code, without changing live state.
    public bool CanEvadeDrop(float clearance, float warningSeconds, float reactionSeconds)
    {
        if (Heart == null || vertical == null || vertical.IsAirborne || vertical.IsDucking
            || transform.position.x > RightLimitX() + .001f) return false;
        float maxX = RightLimitX(), start = transform.position.x;
        for (int route = 0; route < 2; route++)
        {
            bool right = route == 1;
            // Budget at maximum environmental anxiety, ignoring beneficial recovery.
            if (right && (Heart.Overheated || NeedsSprintRelease
                || Mathf.Max(Heart.BaseBpm, Heart.ThreatBpm) + Heart.Exertion + Heart.ResidualBpm
                    + sprintBpmPerSecond * warningSeconds + 2f >= Heart.LimitBpm)) continue;
            float x = start, speed = HorizontalSpeed, elapsed = 0f;
            while (elapsed < warningSeconds)
            {
                float dt = Mathf.Min(1f / 120f, warningSeconds - elapsed);
                if (elapsed >= reactionSeconds) speed = NextGroundSpeed(x, speed, maxX, right, dt);
                x = Mathf.Clamp(x + speed * dt, HomePosition.x, maxX);
                if ((x >= maxX && speed > 0) || (x <= HomePosition.x && speed < 0)) speed = 0;
                elapsed += dt;
            }
            if (right ? x - start >= clearance : start - x >= clearance) return true;
        }
        return false;
    }

    public float HorizontalHomeX => HomePosition.x + (PlayerHurtbox.BoundsFor(GetComponent<Collider>()).center.x-transform.position.x);
    public float HorizontalLimitX => RightLimitX() + (PlayerHurtbox.BoundsFor(GetComponent<Collider>()).center.x-transform.position.x);

    // Prove one continuous release/hold route stays outside every committed lane
    // from the first strike until the last retract. Uses live momentum and heart debt.
    public bool CanAvoidOverhead(float[] lanes, float halfWidth, float warning, float total, float reaction, float padding)
    {
        if(Heart==null || vertical==null || !vertical.IsFullyStanding
            || transform.position.x>RightLimitX()+.001f)return false;
        float limit=RightLimitX();
        var body=GetComponent<Collider>();float offset=PlayerHurtbox.BoundsFor(body).center.x-transform.position.x;
        float clearance=halfWidth+PlayerHurtbox.BoundsFor(body).extents.x+Mathf.Max(0,padding);
        for(int route=0;route<2;route++)
        {
            bool right=route==1;
            if(right && (Heart.Overheated||NeedsSprintRelease))continue;
            var heart=Heart.Copy();float x=transform.position.x,speed=HorizontalSpeed,t=0;
            bool failed=false,locked=false;
            while(t<total)
            {
                float dt=Mathf.Min(1f/120f,total-t),old=x;
                bool responding=t>=Mathf.Max(.3f,reaction);
                bool exerting=responding?right&&!heart.Overheated&&!locked:speed>.05f;
                heart.Tick(dt,1f,exerting,sprintBpmPerSecond,recoveryBpmPerSecond,proximityResponse,speed<=.05f&&!exerting);
                if(heart.Overheated)locked=true;
                if(responding)speed=NextGroundSpeed(x,speed,limit,right&&!locked,dt);
                x=Mathf.Clamp(x+speed*dt,HomePosition.x,limit);
                if((x>=limit&&speed>0)||(x<=HomePosition.x&&speed<0))speed=0;
                t+=dt;
                if(t>=warning)
                    foreach(float lane in lanes)
                        if(Mathf.Max(old,x)+offset>=lane-clearance && Mathf.Min(old,x)+offset<=lane+clearance){failed=true;break;}
                if(failed)break;
            }
            if(!failed)return true;
        }
        return false;
    }

    void OnValidate()
    {
        var validated = new HeartbeatState(restingBpm, threatBpm, overloadBpm, resumeBpm,
            recoveryDelay, overloadResidualBpm, overloadResidualDuration);
        restingBpm = validated.RestBpm;
        threatBpm = validated.ThreatBpm;
        overloadBpm = validated.LimitBpm;
        resumeBpm = validated.ResumeBpm;
        recoveryDelay = validated.RecoveryDelay;
        overloadResidualBpm = validated.ResidualAmount;
        overloadResidualDuration = validated.ResidualDuration;
        dashDuration = Mathf.Max(.05f, dashDuration);
        dashSpeed = Mathf.Max(.1f, dashSpeed);
        dashAcceleration = Mathf.Max(.1f, dashAcceleration);
        dashBrakeAcceleration = Mathf.Max(.1f, dashBrakeAcceleration);
        dashCooldown = Mathf.Max(dashDuration, dashCooldown);
        dashBpmCost = Mathf.Max(0f, dashBpmCost);
        dashOverrunRearmDistance = Mathf.Max(0f, dashOverrunRearmDistance);
        huntMinimumBpm = Mathf.Clamp(huntMinimumBpm, restingBpm, threatBpm);
    }
}
