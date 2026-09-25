using UnityEngine;

// Vertical input resolves first, so the takeoff frame cannot add ground acceleration.
[DefaultExecutionOrder(100)]
[RequireComponent(typeof(PlayerVerticalMovement))]
public class PlayerSprint : MonoBehaviour
{
    [Header("Sprint - Hold on Ground")]
    [SerializeField] private KeyCode sprintKey = KeyCode.LeftShift;
    [SerializeField, Min(.1f)] private float maximumRightSpeed = 2.4f;
    [SerializeField, Min(.1f)] private float acceleration = 6f;
    [SerializeField, Min(.1f)] private float returnSpeed = 1.1f;
    [SerializeField, Min(.1f)] private float returnAcceleration = 4.5f;
    [SerializeField, Range(.35f, .65f)] private float rightmostViewport = .5f;

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
    public Vector3 HomePosition { get; private set; }
    public float BeatScale => beatScale;
    public float OverloadScale => overloadScale;
    private PlayerVerticalMovement vertical;
    private GameModeController modes;

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
        Step(Time.deltaTime, Input.GetKey(sprintKey), vertical.IsAirborne, vertical.IsDucking);
    }

    // Shared by live input and gameplay validation. No airborne key can change momentum.
    public void Step(float dt, bool held, bool airborne, bool crouching)
    {
        if (dt <= 0f || Heart == null) return;
        // Releasing after overload re-arms input, even while the heart is still recovering.
        // Holding the key throughout recovery must never start the next sprint automatically.
        if (!held) NeedsSprintRelease = false;
        if (modes != null && (modes.AmbushOwnsPlayer || modes.VehicleEscapeActive)) return;
        bool wantsSprint = !airborne && !crouching && held && !NeedsSprintRelease;
        bool wasOverheated = Heart.Overheated;
        // Carrying sprint momentum still costs effort; repeated jumps cannot bypass exhaustion.
        bool exerting = airborne ? HorizontalSpeed > .05f : wantsSprint && !wasOverheated;
        bool recoveryAllowed = HorizontalSpeed <= .05f && !exerting;
        float danger = modes != null ? modes.CurtainDanger(calmViewportGap) : 0f;
        Heart.Tick(dt, danger, exerting, sprintBpmPerSecond, recoveryBpmPerSecond, proximityResponse,
            recoveryAllowed);
        if (!wasOverheated && Heart.Overheated) NeedsSprintRelease = held;

        float maxX = RightLimitX();
        float x = transform.position.x;
        if (!airborne)
        {
            bool sprinting = wantsSprint && !Heart.Overheated && !NeedsSprintRelease;
            float rate = sprinting ? acceleration : returnAcceleration;
            DistanceSprintIntensity = Mathf.MoveTowards(DistanceSprintIntensity, sprinting ? 1f : 0f,
                rate / Mathf.Max(.1f, maximumRightSpeed) * dt);
            HorizontalSpeed = NextGroundSpeed(x, HorizontalSpeed, maxX, sprinting, dt);
        }
        else
        {
            // No new sprint in air. Count only inherited motion, never an airborne key press.
            DistanceSprintIntensity = Mathf.Min(DistanceSprintIntensity,
                Mathf.Clamp01(HorizontalSpeed / Mathf.Max(.1f, maximumRightSpeed)));
        }
        Vector3 position = transform.position;
        position.x = Mathf.Clamp(x + HorizontalSpeed * dt, HomePosition.x, maxX);
        if ((position.x >= maxX && HorizontalSpeed > 0f) || (position.x <= HomePosition.x && HorizontalSpeed < 0f))
            HorizontalSpeed = 0f;
        transform.position = position;
    }

    public void TickStationaryHeartbeat(float dt,float danger)
    {
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
        float rate = sprinting ? acceleration : returnAcceleration;
        float remaining = sprinting ? maxX - x : x - HomePosition.x;
        float target = Mathf.Min(sprinting ? maximumRightSpeed : returnSpeed,
            Mathf.Sqrt(2f * rate * Mathf.Max(0f, remaining)));
        return Mathf.MoveTowards(speed, sprinting ? target : -target, rate * dt);
    }

    // A drop only commits when at least one grounded escape fits inside its warning.
    // Forecast uses the same acceleration/boundary code, without changing live state.
    public bool CanEvadeDrop(float clearance, float warningSeconds, float reactionSeconds)
    {
        if (Heart == null || vertical == null || vertical.IsAirborne || vertical.IsDucking) return false;
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

    public float HorizontalHomeX => HomePosition.x + (GetComponent<Collider>().bounds.center.x-transform.position.x);
    public float HorizontalLimitX => RightLimitX() + (GetComponent<Collider>().bounds.center.x-transform.position.x);

    // Prove one continuous release/hold route stays outside every committed lane
    // from the first strike until the last retract. Uses live momentum and heart debt.
    public bool CanAvoidOverhead(float[] lanes, float halfWidth, float warning, float total, float reaction, float padding)
    {
        if(Heart==null || vertical==null || !vertical.IsFullyStanding)return false;
        float limit=RightLimitX();
        var body=GetComponent<Collider>();float offset=body.bounds.center.x-transform.position.x;
        float clearance=halfWidth+body.bounds.extents.x+Mathf.Max(0,padding);
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
    }
}
