using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

[DefaultExecutionOrder(1400)]
public class GameModeController : MonoBehaviour
{
    public enum Mode { Menu, Running }
    public Mode CurrentMode { get; private set; }
    public bool HasLost => player != null && player.HasLost;
    public bool IsPlaying => CurrentMode != Mode.Menu && player != null && !player.HasLost;

    [Header("Scene References")]
    [SerializeField] private LoseCondition player;
    [SerializeField] private TMP_Text textTemplate;
    [SerializeField] private TMP_FontAsset menuFont;

    [Header("Pursuit - Reserve, Breathing and Rhythm")]
    [Tooltip("Reserve is measured in ordinary collision costs, including injury slowdown. Recovery Rate is exponential per second. Sprint affects recovery, never the pursuit rhythm. All distances below are chase units, not odometer meters.")]
    [SerializeField] private ChaseState.Settings pursuit = new ChaseState.Settings();

    [Header("Tentacles - Attacks and Distance Hunts")]
    [SerializeField] private TentacleAttackState.Settings tentacleAttacks = new TentacleAttackState.Settings();
    private CurtainTentacles tentacles;
    public bool IsHunting => tentacles != null && tentacles.State != null
        && (tentacles.State.CurrentStage == TentacleAttackState.Stage.HuntPrelude
            || tentacles.State.CurrentStage == TentacleAttackState.Stage.Hunt
            || tentacles.State.CurrentStage == TentacleAttackState.Stage.LeavingHunt);
    public bool TentaclesBlockSpawns => (tentacles != null && tentacles.BlocksSpawns) || (ambush != null && (ambush.Reserved || ambush.Engaged));

    [Header("Hunt Ambush - Reaction and Escape (real seconds)")]
    [SerializeField] private HuntAmbushState.Settings huntAmbush = new HuntAmbushState.Settings();
    [Tooltip("Three distinct letter keys shared by tentacle ambushes and the vehicle lock.")]
    [SerializeField] private KeyCode[] ambushKeys = { KeyCode.Q, KeyCode.E, KeyCode.R };
    private HuntAmbush ambush;
    public bool AmbushOwnsPlayer => ambush != null && ambush.BlocksPlayer;
    public bool AmbushBound => ambush != null && ambush.Engaged
        && (ambush.State.Current == HuntAmbushState.Phase.Bind
            || ambush.State.Current == HuntAmbushState.Phase.Struggle
            || ambush.State.Current == HuntAmbushState.Phase.Consume);
    public float AmbushStress => ambush != null ? ambush.Stress : 0f;

    [Header("Distance Difficulty - Encounter Frequency")]
    [SerializeField] private DistanceFrequency.Settings distanceFrequency = new DistanceFrequency.Settings();
    public float EncounterIntervalMultiplier => DistanceFrequency.Multiplier(RunDistanceMeters, distanceFrequency);

    [Header("Hunt Atmosphere - Decorative Borders")]
    [SerializeField] private HuntAtmosphereGraphic.Settings huntAtmosphere = new HuntAtmosphereGraphic.Settings();
    private HuntAtmosphereGraphic huntAtmosphereGraphic;

    [Header("Tentacle Hit Fragments")]
    [SerializeField, Range(0, 80)] private int tentacleHitParticleCount = 16;
    [SerializeField, Min(.1f)] private float tentacleHitParticleLifetime = .65f;

    [Header("Injury - Screen Edge Flash")]
    [SerializeField] private Color injuryFlashColor = new Color(1f, .025f, .045f, 1f);
    [SerializeField, Range(0f, 1f)] private float injuryFlashStrength = .6f;
    [SerializeField, Min(.08f)] private float injuryFlashSeconds = .4f;
    [SerializeField, Range(.05f, .4f)] private float injuryFlashWidth = .18f;
    private InjuryVignetteGraphic injuryFlash;

    [Header("Pursuit - Screen Mapping") ]
    [Tooltip("Visual distance scale, separate from the allowed escape distance.")]
    [SerializeField, Min(1f)] private float distanceForLeftEdge = 80f;
    [Tooltip("Seconds for the curtain to ease toward its new position after a hit or recovery.")]
    [SerializeField, Min(0.01f)] private float curtainSmoothTime = 0.35f;
    [SerializeField, Min(0.01f)] private float curtainEntranceSmoothTime = 0.65f;
    private bool curtainEntering;
    private float curtainTarget;
    private float curtainVelocity;
    [Header("Curtain Flow")]
    [SerializeField, Range(0f, 0.05f)] private float curtainWaveAmplitude = 0.03f;
    [SerializeField, Range(0.05f, 2f)] private float curtainFlowSpeed = 1.2f;
    [SerializeField, Range(0f, 0.03f)] private float swallowReactionStrength = 0.03f;
    [SerializeField, Min(0.1f)] private float swallowReactionDuration = 1.4f;
    [SerializeField, Range(0, 80)] private int breakParticleCount = 30;
    private ViscousCurtain curtainShape;

    [Header("Chase - Escape Speed")]
    [SerializeField, Min(0f)] private float startingSpeed = 6f;
    [SerializeField, Min(0f)] private float minimumSpeed = 2f;
    [SerializeField, Min(0f)] private float maximumSpeed = 7f;
    [SerializeField, Min(0f)] private float hitSpeedLoss = 1.8f;
    [SerializeField, Min(0f)] private float normalDodgeSpeedGain = 0.6f;
    [SerializeField, Min(0f)] private float nearMissSpeedGain = 1f;
    [SerializeField, Min(0f)] private float nearMissClearance = 0.24f;

    [Header("Near Miss - Gold Outline")]
    [SerializeField] private Color nearMissOutlineColor = new Color(1f, .64f, .08f, 1f);
    [SerializeField, Range(.08f, .6f)] private float nearMissOutlineSeconds = .3f;
    [SerializeField, Range(.005f, .12f)] private float nearMissOutlineWidth = .08f;
    private NearMissOutline nearMissOutline;

    [Header("Temporary Speed - Return to Starting Speed")]
    [Tooltip("Speed lost per second above base speed. 0.6 makes a normal +0.6 boost last one second.")]
    [SerializeField, Min(0.01f)] private float boostDecayPerSecond = 0.6f;
    [Tooltip("Speed recovered per second while below base speed after injury.")]
    [SerializeField, Min(0.01f)] private float injuryRecoveryPerSecond = 0.6f;

    [Header("Run Distance")]
    [SerializeField] private bool showRunDistance = true;
    [Tooltip("Distance-counting pace at normal escape speed, in km/h. Does not change gameplay movement.")]
    [SerializeField, Min(.1f)] private float joggingSpeedKmh = 10f;
    [Tooltip("Distance-counting pace at normal escape speed with full sprint effort, including at the screen boundary, in km/h.")]
    [SerializeField, Min(.1f)] private float fastRunningSpeedKmh = 16f;
    [Tooltip("Optional distance multiplier after pace calibration. Keep at 1 for the configured km/h values.")]
    [SerializeField, Min(.01f)] private float metersPerUnit = 1f;
    private readonly RunDistanceState runDistance = new RunDistanceState();
    public double RunDistanceMeters => runDistance.Meters;
    public float LevelFinishMeters => vehicleEscapeSettings != null
        ? Mathf.Max(1f, vehicleEscapeSettings.finishMeters) : 1000f;
    public float RunElapsedSeconds { get; private set; }
    private TMP_Text runDistanceHUD;

    [Header("Level Finish - Vehicle Escape")]
    [SerializeField] private VehicleEscapeState.Settings vehicleEscapeSettings = new VehicleEscapeState.Settings();
    private VehicleEscapeController vehicleEscape;
    public bool VehicleEscapeActive => vehicleEscape != null && vehicleEscape.Active;
    public bool VehicleEscapeApproaching => VehicleEscapeActive
        && vehicleEscape.State.Current == VehicleEscapeState.Phase.Arrival;

    [Header("Test Feedback")]
    [Tooltip("Hide verbose prototype statistics during play. Disable to restore the prototype statistics.")]
    [SerializeField] private bool minimalHUD = true;
    [SerializeField] private bool showChaseStats = true;
    [SerializeField, Min(0f)] private float noticeSeconds = 1.5f;

    [Header("Swallow Effect")]
    [SerializeField, Min(0.1f)] private float deathSweepSeconds = 1.8f;
    [SerializeField, Range(0, 80)] private int swallowParticleCount = 22;
    [SerializeField, Min(0.1f)] private float swallowParticleLifetime = 0.8f;
    public bool IsDeathTransition => swallowing && deathElapsed < Mathf.Max(0.1f, deathSweepSeconds);
    private SwallowParticles particles;
    private bool swallowing;
    private float deathElapsed;
    private float deathStartEdge;
    private Camera frozenCamera;
    private Vector3 frozenCameraPosition;
    private Quaternion frozenCameraRotation;
    private float frozenFov;
    private float frozenOrthoSize;

    private ChaseState chase;
    private PlayerSprint sprint;
    private Vector3 curtainReferencePosition;
    public float SafetyReserve => chase != null ? chase.Reserve : 0f;
    public float TemporaryBreathingDistance => chase != null ? chase.TemporaryDistance : 0f;
    public ChaseState.PursuitPhase PursuitPhase => chase != null ? chase.Phase : ChaseState.PursuitPhase.Rest;

    public float CurtainDanger(float calmGap)
    {
        if(VehicleEscapeActive)return vehicleEscape.Danger(calmGap);
        if (!IsPlaying || Camera.main == null || curtainShape == null) return 0f;
        Vector3 position = Camera.main.WorldToViewportPoint(PlayerHurtbox.BoundsFor(playerCollider).center);
        float gap = position.x - curtainShape.EdgeAt(position.y);
        return 1f - Mathf.Clamp01(gap / Mathf.Max(.01f, calmGap));
    }

    [Header("Menu - Button Devouring")]
    [SerializeField, Min(.2f)] private float menuReachSeconds = .65f;
    [SerializeField, Min(.1f)] private float menuGripSeconds = .3f;
    [SerializeField, Min(.2f)] private float menuDragSeconds = .95f;
    private MenuDevourGraphic menuDevour;
    private UnityEngine.UI.Button startButton;
    private RectTransform startButtonRect;
    private Vector2 startButtonPosition;
    private TMP_Text[] menuControlHints;
    private RectTransform menuControlBackdrop;
    private MenuControlCollapse menuControlCollapse;
    private float menuElapsed;
    private bool menuStarting;
    private bool menuWaitingForRecovery;
    private ClazyPlayerAnimationDriver menuAnimator;
    private HeartbeatHUD heartbeatHUD;
    public bool IsMenuStarting => menuStarting;
    private GameObject menu;
    private RectTransform curtain;
    private TMP_Text chaseHUD;
    private float noticeUntil;
    private string notice;
    private int dodges;
    private Collider playerCollider;
    private ObstacleSpawner spawner;

    void Awake()
    {
        CurrentMode = Mode.Menu;
        Time.timeScale = 0f;
    }

    void Start()
    {
        spawner = GetComponent<ObstacleSpawner>();
        if (player == null) player = FindAnyObjectByType<LoseCondition>();
        playerCollider = player.GetComponent<Collider>();
        sprint = player.GetComponent<PlayerSprint>();
        menuAnimator = player.GetComponent<ClazyPlayerAnimationDriver>();
        nearMissOutline = player.GetComponent<NearMissOutline>();
        if (nearMissOutline == null) nearMissOutline = player.gameObject.AddComponent<NearMissOutline>();
        nearMissOutline.Initialize(nearMissOutlineColor, nearMissOutlineSeconds, nearMissOutlineWidth);
        curtainReferencePosition = player.transform.position;
        BuildInterface();
    }

    public void StartGame()
    {
        if (CurrentMode != Mode.Menu || menuStarting || menuWaitingForRecovery) return;
        if (menuAnimator != null && !menuAnimator.MenuReadyToStart)
        {
            menuWaitingForRecovery = true;
            startButton.interactable = false;
            menuAnimator.FinishMenuKnockDownFast();
            return;
        }
        BeginMenuEntrance();
    }

    private void BeginMenuEntrance()
    {
        menuStarting = true;
        menuElapsed = 0f;
        startButtonPosition = startButtonRect.anchoredPosition;
        startButton.interactable = false;
        menuDevour.gameObject.SetActive(true);
        menuControlCollapse.Begin();
        TickMenuEntrance(0f);
    }

    private void TickMenuEntrance(float dt)
    {
        menuElapsed += Mathf.Max(0f, dt);
        float reach = Mathf.Max(.2f, menuReachSeconds);
        float grip = Mathf.Max(.1f, menuGripSeconds);
        float drag = Mathf.Max(.2f, menuDragSeconds);
        float pull = Mathf.Clamp01((menuElapsed - reach - grip) / drag);
        float eased = Mathf.SmoothStep(0f, 1f, pull);
        RectTransform parent = (RectTransform)startButtonRect.parent;
        float offscreenX = -startButtonRect.anchorMin.x * parent.rect.width - startButtonRect.rect.width;
        startButtonRect.anchoredPosition = Vector2.Lerp(startButtonPosition,
            new Vector2(offscreenX, startButtonPosition.y - parent.rect.height * .035f), eased);
        startButtonRect.localRotation = Quaternion.Euler(0, 0, -12f * Mathf.Sin(pull * Mathf.PI));
        startButtonRect.localScale = Vector3.one * Mathf.Lerp(1f, .55f, eased);
        menuControlCollapse.Tick(menuElapsed, dt);
        menuDevour.Show(startButtonRect, menuElapsed / reach,
            (menuElapsed - reach) / grip, pull, menuElapsed);
        if (menuElapsed < reach + grip + drag + .15f) return;
        menuStarting = false;
        menuDevour.gameObject.SetActive(false);
        BeginRunning();
    }

    private void BeginRunning()
    {
        vehicleEscape?.Dispose();vehicleEscape=null;
        CurrentMode = Mode.Running;
        chase = new ChaseState(Mathf.Clamp(startingSpeed, minimumSpeed, maximumSpeed), pursuit);
        runDistance.Reset();RunElapsedSeconds=0f;
        tentacles.Begin();
        ambush.Reset();
        huntAtmosphereGraphic.ResetVisual();
        RefreshRunDistance();
        menu.SetActive(false);
        curtain.gameObject.SetActive(true);
        Time.timeScale = 1f;
        UpdateChaseDisplay();
        // Reveal the opaque curtain from the left edge instead of popping it in.
        curtain.anchorMax = new Vector2(0f, 1f);
        RefreshCurtainShape();
        curtainVelocity = 0f;
        curtainEntering = true;
    }

    public void TakeHit()
    {
        if (!TryTakeHit() || Camera.main == null || playerCollider == null) return;
        Vector3 point = Camera.main.WorldToViewportPoint(PlayerHurtbox.BoundsFor(playerCollider).center);
        EmitPlayerHit(point);
    }

    public void TakeTentacleHit(Vector2 contactViewport)
    {
        if (!TryTakeHit() || particles == null) return;
        EmitPlayerHit(contactViewport);
    }

    private void EmitPlayerHit(Vector2 contactViewport)
    {
        if (particles == null || player == null) return;
        particles.Burst(contactViewport, CharacterAppearanceColor.Dominant(player.transform),
            tentacleHitParticleCount, tentacleHitParticleLifetime, false, new Vector2(.012f, .025f));
    }

    private bool TryTakeHit()
    {
        if (!IsPlaying || CurrentMode != Mode.Running || VehicleEscapeActive || (ambush != null && ambush.Engaged)) return false;
        chase.Hit(hitSpeedLoss, minimumSpeed, injuryRecoveryPerSecond);
        if (injuryFlash != null)
            injuryFlash.Flash(injuryFlashColor, injuryFlashStrength, injuryFlashSeconds, injuryFlashWidth);
        tentacles?.NotifyHit();
        SetNotice("INJURED - slowing down");
        UpdateChaseDisplay();
        return true;
    }

    public void SuccessfulDodge(float clearance)
    {
        if (!IsPlaying || CurrentMode != Mode.Running || VehicleEscapeActive || (ambush != null && ambush.Engaged)) return;
        bool near = clearance > 0f && clearance <= nearMissClearance;
        dodges++;
        chase.Dodge(near ? nearMissSpeedGain : normalDodgeSpeedGain, maximumSpeed, near);
        if (near) nearMissOutline?.Flash();
        SetNotice(near ? "NEAR MISS - temporary boost" : "DODGED - temporary recovery");
    }

    public bool CanAffordCrouch(float arrivalSeconds, float crouchSeconds, float reserve)
    {
        if (chase == null || !IsPlaying || chase.IsCaught) return false;
        ChaseState forecast = chase.Copy();
        // Simulate without future dodge rewards, preserving current injury and boost decay.
        for (int phase = 0; phase < 2; phase++)
        {
            float remaining = phase == 0 ? arrivalSeconds : crouchSeconds;
            while (remaining > 0f)
            {
                float step = Mathf.Min(0.02f, remaining);
                // Conservatively assume no sprint separation and no new dodge reward.
                // Includes the upcoming active-pursuit phase, not just current speed.
                forecast.Tick(step, Mathf.Max(0.01f, boostDecayPerSecond),
                    Mathf.Max(0.01f, injuryRecoveryPerSecond));
                if (forecast.IsCaught || forecast.Distance < Mathf.Max(0f, reserve)) return false;
                remaining -= step;
            }
        }
        return forecast.Distance >= Mathf.Max(0f, reserve);
    }

    private void SetNotice(string value)
    {
        notice = value;
        noticeUntil = Time.unscaledTime + noticeSeconds;
    }

    void Update()
    {
        if (CurrentMode == Mode.Menu)
        {
            if (menuWaitingForRecovery)
            {
                if (menuAnimator == null || menuAnimator.MenuReadyToStart)
                {
                    menuWaitingForRecovery = false;
                    BeginMenuEntrance();
                }
            }
            else if (menuStarting) TickMenuEntrance(Time.unscaledDeltaTime);
            else if (Input.GetMouseButtonDown(0) && PointerOverMenuCharacter(Input.mousePosition))
            {
                if (menuAnimator != null && menuAnimator.TryMenuKnockDown())
                    heartbeatHUD?.NotifyMenuKnockDown();
            }
            return;
        }
        if (Input.GetKeyDown(KeyCode.Escape)) { ReturnToMenu(); return; }
        if (swallowing)
        {
            UpdateHuntAtmosphere(Time.unscaledDeltaTime);
            deathElapsed += Time.unscaledDeltaTime;
            float t = Mathf.Clamp01(deathElapsed / Mathf.Max(0.1f, deathSweepSeconds));
            curtain.anchorMax = new Vector2(Mathf.Lerp(deathStartEdge, 1f, Mathf.SmoothStep(0f, 1f, t)), 1f);
            RefreshCurtainShape();
            return;
        }
        if (CurrentMode != Mode.Running || !IsPlaying) return;
        if(VehicleEscapeActive)
        {
            vehicleEscape.Tick(Time.unscaledDeltaTime);
            UpdateHuntAtmosphere(Time.timeScale>0?Time.unscaledDeltaTime:0);
            RefreshCurtainShape();return;
        }
        if(TryBeginVehicleEscape())return;
        if (!ambush.Engaged || ambush.ControlsReleased) RunElapsedSeconds += Time.deltaTime;
        if (ambush.Engaged)
        {
            ambush.Tick(Time.unscaledDeltaTime);
            if(ambush.ControlsReleased && Time.deltaTime>0f)
            {
                // Player movement is live during the short protected retreat.
                // Count that running without resuming hazards or reserve drain.
                runDistance.AddAtHumanPace(chase.Speed*Time.deltaTime,chase.BaseSpeed,
                    (sprint!=null?sprint.DistanceSprintIntensity:0f)*Time.deltaTime,
                    joggingSpeedKmh,fastRunningSpeedKmh,metersPerUnit);
                RefreshRunDistance();
                if(TryBeginVehicleEscape())return;
            }
            UpdateHuntAtmosphere(Time.timeScale>0 ? Time.unscaledDeltaTime : 0f);
            RefreshCurtainShape();
            if (ambush.Engaged && ambush.State.Finished)
            {
                bool dead=ambush.State.Current==HuntAmbushState.Phase.Dead;
                ambush.Cancel(!dead);curtainVelocity=0;
                if(dead)player.Die();
            }
            // QTE owns its escape deadline; no passive reserve drain, healing,
            // distance farming or ordinary swallowing during this interaction.
            return;
        }
        bool reserved=ambush.Prepare(tentacles.State,runDistance.Meters,tentacleAttacks.huntLengthMeters);
        if(ambush.Engaged){RefreshCurtainShape();return;}
        if(!reserved)tentacles.Tick(Time.deltaTime, runDistance.Meters);
        UpdateHuntAtmosphere(Time.deltaTime);
        // Sprint creates separation through the player's position only. Counting it
        // again here would turn sprint into permanent reserve and cancel its return cost.
        chase.Tick(Time.deltaTime, Mathf.Max(0.01f, boostDecayPerSecond),
            Mathf.Max(0.01f, injuryRecoveryPerSecond), sprint != null ? sprint.DistanceSprintIntensity : 0f);
        if (Time.deltaTime > 0f)
            runDistance.AddAtHumanPace(chase.LastTravel, chase.BaseSpeed,
                (sprint != null ? sprint.DistanceSprintIntensity : 0f) * Time.deltaTime,
                joggingSpeedKmh, fastRunningSpeedKmh, metersPerUnit);
        RefreshRunDistance();
        if(TryBeginVehicleEscape())return;
        UpdateChaseDisplay();
        float edge = Mathf.SmoothDamp(curtain.anchorMax.x, curtainTarget,
            ref curtainVelocity, Mathf.Max(0.01f, curtainEntering ? curtainEntranceSmoothTime : curtainSmoothTime),
            Mathf.Infinity, Time.deltaTime);
        curtain.anchorMax = new Vector2(edge, 1f);
        RefreshCurtainShape();
        if (curtainEntering && Mathf.Abs(edge - curtainTarget) < 0.001f)
            curtainEntering = false;
    }

    private bool PointerOverMenuCharacter(Vector3 pointer)
    {
        Camera camera = Camera.main;
        if (camera == null || player == null) return false;
        float left = float.PositiveInfinity, right = float.NegativeInfinity;
        float bottom = float.PositiveInfinity, top = float.NegativeInfinity;
        foreach (Renderer body in player.GetComponentsInChildren<Renderer>())
        {
            if (!body.enabled) continue;
            Bounds bounds = body.bounds;
            for (int x = 0; x < 2; x++)
                for (int y = 0; y < 2; y++)
                    for (int z = 0; z < 2; z++)
                    {
                        Vector3 corner = new Vector3(x == 0 ? bounds.min.x : bounds.max.x,
                            y == 0 ? bounds.min.y : bounds.max.y,
                            z == 0 ? bounds.min.z : bounds.max.z);
                        Vector3 screen = camera.WorldToScreenPoint(corner);
                        if (screen.z <= 0f) continue;
                        left = Mathf.Min(left, screen.x); right = Mathf.Max(right, screen.x);
                        bottom = Mathf.Min(bottom, screen.y); top = Mathf.Max(top, screen.y);
                    }
        }
        const float padding = 12f;
        return pointer.x >= left - padding && pointer.x <= right + padding
            && pointer.y >= bottom - padding && pointer.y <= top + padding;
    }

    private void ResolveBodyContact()
    {
        // The Animator, shoe alignment and bone-driven hurtboxes have now finished.
        if (!IsPlaying || swallowing || VehicleEscapeActive || ambush == null || ambush.Engaged) return;
        tentacles?.RenderAndCollide();
        Camera camera = Camera.main;
        float playerX = camera != null ? camera.WorldToViewportPoint(PlayerHurtbox.BoundsFor(playerCollider).center).x : 0.3f;
        // Wait for the visible edge to reach the player; distance loss must not
        // make the player disappear ahead of the smoothly advancing curtain.
        float playerY = camera != null ? camera.WorldToViewportPoint(PlayerHurtbox.BoundsFor(playerCollider).center).y : 0.5f;
        if (curtainShape.EdgeAt(playerY) >= Mathf.Clamp01(playerX)) player.Die();
    }

    private bool TryBeginVehicleEscape()
    {
        if(VehicleEscapeActive)return true;
        if(!vehicleEscapeSettings.enabled||RunDistanceMeters<vehicleEscapeSettings.finishMeters||!IsPlaying||Camera.main==null)return false;
        // Finish owns all controls and timing; no announced hazard can leak into it.
        nearMissOutline?.Stop();
        ambush?.Cancel(false);tentacles?.Cancel();spawner?.StopForVehicleEscape();
        runDistance.LimitTo(vehicleEscapeSettings.finishMeters);RefreshRunDistance();
        chaseHUD.gameObject.SetActive(false);
        VehicleEscapeState.Settings escapeConfig = vehicleEscapeSettings.Copy();
        escapeConfig.lockKeys = (KeyCode[])ambushKeys.Clone();
        vehicleEscape=new VehicleEscapeController(this,escapeConfig,playerCollider,curtain,particles,
            textTemplate!=null?textTemplate.font:null,huntAmbush);
        return true;
    }
    public void FailVehicleEscape()
    {
        if(VehicleEscapeActive&&player!=null&&!player.HasLost)player.Die();
    }

    public void ReturnToMenu()
    {
        ambush?.Cancel();
        Time.timeScale = 1f;
        SceneManager.LoadScene(gameObject.scene.path);
    }

    public bool BeginSwallowDeath()
    {
        if (CurrentMode != Mode.Running) return false;
        swallowing = true;
        nearMissOutline?.Stop();
        ambush?.Cancel(false);
        tentacles?.Cancel();
        deathElapsed = 0f;
        deathStartEdge = curtain.anchorMax.x;
        chaseHUD.gameObject.SetActive(false);
        frozenCamera = Camera.main;
        if (frozenCamera != null)
        {
            frozenCameraPosition = frozenCamera.transform.position;
            frozenCameraRotation = frozenCamera.transform.rotation;
            frozenFov = frozenCamera.fieldOfView;
            frozenOrthoSize = frozenCamera.orthographicSize;
        }
        if (Camera.main != null && playerCollider != null)
        {
            // Resolve the current skin before hiding it. The original green mesh is disabled.
            Color skinColor = CharacterAppearanceColor.Dominant(player.transform);
            Bounds bounds = PlayerHurtbox.BoundsFor(playerCollider);
            Vector3 point = Camera.main.WorldToViewportPoint(bounds.center);
            curtainShape.React(point.y);
            particles.Burst(new Vector2(curtainShape.EdgeAt(point.y), Mathf.Clamp01(point.y)),
                skinColor, swallowParticleCount, swallowParticleLifetime);
        }
        foreach (Renderer renderer in player.GetComponentsInChildren<Renderer>()) renderer.enabled = false;
        foreach (Collider collider in player.GetComponentsInChildren<Collider>()) collider.enabled = false;
        var movement = player.GetComponent<PlayerVerticalMovement>();
        if (movement != null) movement.enabled = false;
        return true;
    }

    void LateUpdate()
    {
        ResolveBodyContact();
        if(VehicleEscapeActive&&!swallowing)vehicleEscape.LateUpdate();
        if (!swallowing || frozenCamera == null) return;
        frozenCamera.transform.SetPositionAndRotation(frozenCameraPosition, frozenCameraRotation);
        frozenCamera.fieldOfView = frozenFov;
        frozenCamera.orthographicSize = frozenOrthoSize;
    }

    public bool TrySwallowObstacle(Renderer body, ref bool emitted)
    {
        if (CurrentMode != Mode.Running || body == null || Camera.main == null) return false;
        Camera camera = Camera.main;
        Bounds bounds = body.bounds;
        float left = float.PositiveInfinity;
        float right = float.NegativeInfinity;
        float bottom = float.PositiveInfinity;
        float top = float.NegativeInfinity;
        for (int x = -1; x <= 1; x += 2)
        for (int y = -1; y <= 1; y += 2)
        for (int z = -1; z <= 1; z += 2)
        {
            Vector3 corner = bounds.center + Vector3.Scale(bounds.extents, new Vector3(x, y, z));
            Vector3 screen = camera.WorldToViewportPoint(corner);
            float screenX = screen.x;
            bottom = Mathf.Min(bottom, screen.y);
            top = Mathf.Max(top, screen.y);
            left = Mathf.Min(left, screenX);
            right = Mathf.Max(right, screenX);
        }
        curtainShape.EdgeRange(bottom, top, out float nearestEdge, out float farthestEdge);
        if (!emitted && left <= farthestEdge)
        {
            emitted = true;
            EmitSwallow(body);
        }
        return right <= nearestEdge;
    }

    private void EmitSwallow(Renderer body)
    {
        if (Camera.main == null) return;
        Vector3 point = Camera.main.WorldToViewportPoint(body.bounds.center);
        curtainShape.React(point.y);
        particles.Burst(new Vector2(curtainShape.EdgeAt(point.y), Mathf.Clamp01(point.y)),
            BodyColor(body), swallowParticleCount, swallowParticleLifetime);
    }

    public void EmitObstacleBreak(Renderer body)
    {
        Camera camera = Camera.main;
        if (camera == null || body == null) return;
        Bounds bounds = body.bounds;
        Vector3 centre = camera.WorldToViewportPoint(bounds.center);
        Vector3 min = camera.WorldToViewportPoint(bounds.min);
        Vector3 max = camera.WorldToViewportPoint(bounds.max);
        particles.Burst(centre, BodyColor(body), breakParticleCount, 0.9f, false,
            new Vector2(Mathf.Abs(max.x - min.x), Mathf.Abs(max.y - min.y)));
    }

    private static Color BodyColor(Renderer body)
    {
        Material material = body.sharedMaterial;
        Color color = Color.white;
        if (material != null)
        {
            if (material.HasProperty("_BaseColor")) color = material.GetColor("_BaseColor");
            else if (material.HasProperty("_Color")) color = material.GetColor("_Color");
        }
        return color;
    }

    private void UpdateChaseDisplay()
    {
        if (CurrentMode != Mode.Running) return;
        Camera camera = Camera.main;
        float homeX = camera != null ? camera.WorldToViewportPoint(curtainReferencePosition).x : 0.3f;
        // Positive margin always keeps the collision edge behind the home position.
        // Signed distance lets depleted reserve threaten a sprinting player, while
        // recovery remains possible until the visible edge actually catches them.
        curtainTarget = Mathf.Clamp(homeX - homeX * (chase.Distance + (tentacles != null ? tentacles.ExtraBreathing : 0f)) / Mathf.Max(1f, distanceForLeftEdge), .015f, 1f);
        if (minimalHUD)
        {
            chaseHUD.gameObject.SetActive(false);
            return;
        }
        string difficulty = spawner != null ? spawner.DifficultySummary : "";
        chaseHUD.gameObject.SetActive(showChaseStats || difficulty.Length > 0);
        chaseHUD.text = showChaseStats
            ? $"Reserve: {chase.Reserve:0.00}   Breathing: {chase.TemporaryDistance:0.0}   {chase.Phase}\nEscape speed: {chase.Speed:0.0}   Hits: {chase.Hits}   Dodged: {dodges}"
            : "";
        if (difficulty.Length > 0)
            chaseHUD.text += (chaseHUD.text.Length > 0 ? "\n" : "") + difficulty;
        if (showChaseStats && Time.unscaledTime < noticeUntil)
            chaseHUD.text += "\n" + notice;
        chaseHUD.color = chase.Distance < 25f ? new Color(1f, 0.55f, 0.45f) : Color.white;
    }

    private void UpdateHuntAtmosphere(float dt)
    {
        var state = tentacles != null ? tentacles.State : null;
        bool surrounding = IsPlaying && !swallowing && tentacleAttacks.enabled && state != null
            && (state.CurrentStage == TentacleAttackState.Stage.HuntPrelude
                || state.CurrentStage == TentacleAttackState.Stage.Hunt
                || state.CurrentStage == TentacleAttackState.Stage.LeavingHunt);
        huntAtmosphereGraphic.Tick(surrounding, dt, huntAtmosphere, ambush != null && ambush.Engaged ? 0f : tentacles != null ? tentacles.Focus : 0f);
    }

    private void RefreshRunDistance()
    {
        runDistanceHUD.gameObject.SetActive(showRunDistance);
        runDistanceHUD.text = runDistance.Display;
    }

    private void RefreshCurtainShape()
    {
        float warning = !swallowing && !VehicleEscapeActive && chase != null ? chase.Telegraph : 0f;
        curtainShape.Amplitude = curtainWaveAmplitude * Mathf.Lerp(1f, 1.15f, warning);
        curtainShape.FlowSpeed = curtainFlowSpeed * Mathf.Lerp(1f, 1.35f, warning);
        // Swallow ripples may reach the logical front, but cannot consume reserve.
        curtainShape.ForwardLimit = swallowing ? 1f : curtain.anchorMax.x;
        curtainShape.AttackFocus = ambush != null && ambush.Engaged ? 0f
            : tentacles != null ? tentacles.Focus * tentacleAttacks.flattenOtherWaves : 0f;
        curtainShape.ReactionStrength = swallowReactionStrength;
        curtainShape.ReactionDuration = swallowReactionDuration;
        curtainShape.RefreshEdge();
    }

    private void BuildInterface()
    {
        var root = new GameObject("Mode Interface", typeof(RectTransform), typeof(Canvas),
            typeof(CanvasScaler), typeof(GraphicRaycaster));
        Canvas canvas = root.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 10;
        CanvasScaler scaler = root.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1280, 720);
        scaler.matchWidthOrHeight = 0.5f;

        var atmosphereObject = new GameObject("Hunt Atmosphere", typeof(RectTransform), typeof(CanvasRenderer), typeof(HuntAtmosphereGraphic));
        atmosphereObject.transform.SetParent(root.transform, false);
        var atmosphereRect = atmosphereObject.GetComponent<RectTransform>();
        atmosphereRect.anchorMin = Vector2.zero;
        atmosphereRect.anchorMax = Vector2.one;
        atmosphereRect.offsetMin = atmosphereRect.offsetMax = Vector2.zero;
        huntAtmosphereGraphic = atmosphereObject.GetComponent<HuntAtmosphereGraphic>();
        huntAtmosphereGraphic.raycastTarget = false;

        var curtainObject = new GameObject("Black Curtain", typeof(RectTransform), typeof(CanvasRenderer), typeof(ViscousCurtain));
        curtainObject.transform.SetParent(root.transform, false);
        curtain = curtainObject.GetComponent<RectTransform>();
        curtain.anchorMin = Vector2.zero;
        curtain.anchorMax = Vector2.one;
        curtain.offsetMin = curtain.offsetMax = Vector2.zero;
        curtainShape = curtainObject.GetComponent<ViscousCurtain>();
        curtainShape.raycastTarget = false;
        curtain.gameObject.SetActive(false);
        tentacles = new CurtainTentacles(this, tentacleAttacks, spawner, playerCollider, curtainShape, root.transform);
        var particleRoot = new GameObject("Swallow Particles", typeof(RectTransform), typeof(SwallowParticles));
        particleRoot.transform.SetParent(root.transform, false);
        var particleRect = particleRoot.GetComponent<RectTransform>();
        particleRect.anchorMin = Vector2.zero;
        particleRect.anchorMax = Vector2.one;
        particleRect.offsetMin = particleRect.offsetMax = Vector2.zero;
        particles = particleRoot.GetComponent<SwallowParticles>();
        ValidateAmbushKeys();
        ambush = new HuntAmbush(this,huntAmbush,ambushKeys,playerCollider,spawner,curtain,root.transform,
            textTemplate != null ? textTemplate.font : null);
        chaseHUD = Text("Chase HUD", root.transform, "", new Vector2(0, -80), new Vector2(800, 125), 24);
        chaseHUD.rectTransform.anchorMin = chaseHUD.rectTransform.anchorMax = new Vector2(0.5f, 1f);
        chaseHUD.gameObject.SetActive(false);

        runDistanceHUD = Text("Run Distance", root.transform, "0 m", new Vector2(135f, -70f), new Vector2(220f, 60f), 28);
        runDistanceHUD.rectTransform.anchorMin = runDistanceHUD.rectTransform.anchorMax = new Vector2(0f, 1f);
        runDistanceHUD.alignment = TextAlignmentOptions.Left;
        runDistanceHUD.outlineWidth = .15f;
        runDistanceHUD.outlineColor = new Color32(20, 28, 30, 220);
        runDistanceHUD.gameObject.SetActive(false);

        if (sprint != null)
        {
            var heartObject = new GameObject("Heartbeat HUD", typeof(RectTransform), typeof(HeartbeatHUD));
            heartObject.transform.SetParent(root.transform, false);
            heartbeatHUD = heartObject.GetComponent<HeartbeatHUD>();
            heartbeatHUD.Initialize(sprint, this, textTemplate != null ? textTemplate.font : null);
        }

        menu = Panel("Main Menu", root.transform, Color.clear).gameObject;
        Button("Start Game", "ESCAPE FATE", 0);
        // Small prototype controls share the button's right-side anchor. Each label
        // falls independently when the start button is dragged into the curtain.
        menuControlBackdrop = Panel("Control Hint Backdrop", menu.transform,
            new Color(.08f, .12f, .16f, .95f));
        menuControlBackdrop.anchorMin = menuControlBackdrop.anchorMax = startButtonRect.anchorMin;
        menuControlBackdrop.sizeDelta = new Vector2(400f, 112f);
        menuControlBackdrop.anchoredPosition = new Vector2(0f, -100f);
        string[] controls = { "SPACE  JUMP", "HOLD S  SLIDE", "HOLD SHIFT  SPRINT", "CTRL  DASH", "QTE  FOLLOW PROMPTS" };
        menuControlHints = new TMP_Text[controls.Length];
        for (int i = 0; i < controls.Length; i++)
        {
            Vector2 position = i == 4 ? new Vector2(0, -128f)
                : new Vector2(i % 2 == 0 ? -100f : 100f, -69f - (i / 2) * 31f);
            TMP_Text hint = Text("Control Hint " + i, menu.transform, controls[i], position,
                new Vector2(i == 4 ? 390f : 195f, 28f), i == 4 ? 16 : 18);
            hint.rectTransform.anchorMin = hint.rectTransform.anchorMax = startButtonRect.anchorMin;
            if (menuFont != null) hint.font = menuFont;
            hint.color = new Color(1f, 1f, 1f, .85f);
            menuControlHints[i] = hint;
        }
        menuControlCollapse = new MenuControlCollapse((RectTransform)menu.transform,
            menuControlBackdrop, menuControlHints);
        var devour = new GameObject("Menu Devouring Tentacles", typeof(RectTransform), typeof(CanvasRenderer), typeof(MenuDevourGraphic));
        devour.transform.SetParent(menu.transform, false);
        var devourRect = devour.GetComponent<RectTransform>();
        devourRect.anchorMin = Vector2.zero; devourRect.anchorMax = Vector2.one;
        devourRect.offsetMin = devourRect.offsetMax = Vector2.zero;
        menuDevour = devour.GetComponent<MenuDevourGraphic>();
        menuDevour.raycastTarget = false;
        devour.SetActive(false);

        var flashObject = new GameObject("Injury Edge Flash", typeof(RectTransform), typeof(CanvasRenderer), typeof(InjuryVignetteGraphic));
        flashObject.transform.SetParent(root.transform, false);
        var flashRect = flashObject.GetComponent<RectTransform>();
        flashRect.anchorMin = Vector2.zero; flashRect.anchorMax = Vector2.one;
        flashRect.offsetMin = flashRect.offsetMax = Vector2.zero;
        injuryFlash = flashObject.GetComponent<InjuryVignetteGraphic>();
        injuryFlash.raycastTarget = false;
    }

    private RectTransform Panel(string name, Transform parent, Color color)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(Image));
        go.transform.SetParent(parent, false);
        var rect = go.GetComponent<RectTransform>();
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = rect.offsetMax = Vector2.zero;
        go.GetComponent<Image>().color = color;
        go.GetComponent<Image>().raycastTarget = false;
        return rect;
    }

    private TMP_Text Text(string name, Transform parent, string value, Vector2 position, Vector2 size, int fontSize)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(TextMeshProUGUI));
        go.transform.SetParent(parent, false);
        var text = go.GetComponent<TextMeshProUGUI>();
        if (textTemplate != null) text.font = textTemplate.font;
        text.text = value;
        text.fontSize = fontSize;
        text.alignment = TextAlignmentOptions.Center;
        text.raycastTarget = false;
        text.rectTransform.anchorMin = text.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
        text.rectTransform.anchoredPosition = position;
        text.rectTransform.sizeDelta = size;
        return text;
    }

    private void Button(string name, string label, float y)
    {
        var rect = Panel(name, menu.transform, new Color(0.14f, 0.19f, 0.24f));
        float playerX = Camera.main != null ? Camera.main.WorldToViewportPoint(PlayerHurtbox.BoundsFor(playerCollider).center).x : 0.25f;
        rect.anchorMin = rect.anchorMax = new Vector2(Mathf.Clamp(1f - playerX, 0.6f, 0.78f), 0.5f);
        rect.sizeDelta = new Vector2(360, 65);
        rect.anchoredPosition = new Vector2(0, y);
        Image image = rect.GetComponent<Image>();
        image.raycastTarget = true;
        var button = rect.gameObject.AddComponent<UnityEngine.UI.Button>();
        startButton = button; startButtonRect = rect;
        button.targetGraphic = image;
        button.onClick.AddListener(StartGame);
        TMP_Text buttonText = Text(name + " Label", rect, label, Vector2.zero, new Vector2(350, 60), 23);
        if (menuFont != null) buttonText.font = menuFont;
    }

    void OnValidate()
    {
        if(vehicleEscapeSettings==null)vehicleEscapeSettings=new VehicleEscapeState.Settings();
        vehicleEscapeSettings.Validate();
        if(huntAmbush==null)huntAmbush=new HuntAmbushState.Settings();
        huntAmbush.Validate();ValidateAmbushKeys();
        if (distanceFrequency == null) distanceFrequency = new DistanceFrequency.Settings();
        distanceFrequency.Validate();
        if (pursuit == null) pursuit = new ChaseState.Settings();
        pursuit.Validate();
        if (tentacleAttacks == null) tentacleAttacks = new TentacleAttackState.Settings();
        tentacleAttacks.Validate();
        if (huntAtmosphere == null) huntAtmosphere = new HuntAtmosphereGraphic.Settings();
        huntAtmosphere.Validate();
        minimumSpeed = Mathf.Max(0f, minimumSpeed);
        maximumSpeed = Mathf.Max(minimumSpeed, maximumSpeed);
        startingSpeed = Mathf.Clamp(startingSpeed, minimumSpeed, maximumSpeed);
        nearMissSpeedGain = Mathf.Max(normalDodgeSpeedGain, nearMissSpeedGain);
        joggingSpeedKmh = Mathf.Max(.1f, joggingSpeedKmh);
        fastRunningSpeedKmh = Mathf.Max(joggingSpeedKmh, fastRunningSpeedKmh);
    }
    private void ValidateAmbushKeys()
    {
        if(ambushKeys==null || ambushKeys.Length!=3)ambushKeys=new[]{KeyCode.Q,KeyCode.E,KeyCode.R};
        for(int i=0;i<3;i++)
        {
            bool duplicate=false;for(int j=0;j<i;j++)if(ambushKeys[j]==ambushKeys[i])duplicate=true;
            if(ambushKeys[i]>=KeyCode.A && ambushKeys[i]<=KeyCode.Z && !duplicate)continue;
            KeyCode key=KeyCode.A;
            while(System.Array.IndexOf(ambushKeys,key)>=0)key=(KeyCode)((int)key+1);
            ambushKeys[i]=key;
        }
    }
    void OnApplicationFocus(bool focused) { ambush?.FocusChanged(focused);vehicleEscape?.FocusChanged(focused); }
    void OnDisable() => ambush?.Cancel();
    void OnDestroy() { ambush?.Cancel();vehicleEscape?.Dispose(); }
}
