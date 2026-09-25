using UnityEngine;

public class PlayerVerticalMovement : MonoBehaviour
{
    // [ -- PlayerVerticalMovement -- ]
    // Contains the functions necessary for sidescrolling player movement
    // Three possible states: running while Grounded; Ducking ; Jumping 
    private enum VerticalState { Grounded, Ducking, Jumping }

    // Edit via Inspector
    [Header("Jump Attributes")]
    [SerializeField] private float jumpVelocity = 18f;
    [SerializeField] private float gravityStrength = -80f;
    [Tooltip("0 keeps airtime fixed; 1 keeps the obstacle distance covered fixed. Intermediate values allow longer coverage at higher speed.")]
    [SerializeField, Range(0f, 1f)] private float jumpSpeedInfluence = 0.35f;
    [SerializeField] private ObstacleSpawner obstacleSpawner;

    // Edit via Inspector
    [Header("Duck Attributes")]
    [SerializeField] private float duckScale = 0.5f;
    [SerializeField] private float duckVelocity = 10f;
    public bool IsAirborne => state == VerticalState.Jumping;
    public bool IsDucking => state == VerticalState.Ducking;
    public bool IsFullyStanding => state == VerticalState.Grounded
        && Mathf.Abs(currentScale - standingScale) < .001f && airHeight <= .001f;

    // A grab can temporarily own the transform; synchronize the movement state
    // when it releases ownership so no half-crouched pose survives the QTE.
    public void RestoreAfterAmbush()
    {
        state = VerticalState.Grounded;
        airHeight = verticalVelocity = activeJumpGravity = 0f;
        currentScale = standingScale;
        Vector3 scale = transform.localScale;
        scale.y = standingScale;
        transform.localScale = scale;
        ApplyVerticalPosition();
    }
    public float GroundY => groundBaseY;
    public float JumpClearanceSeconds(float height)
    {
        float speed = obstacleSpawner != null ? Mathf.Max(1f, obstacleSpawner.SpeedMultiplier) : 1f;
        float scale = Mathf.Pow(speed, jumpSpeedInfluence);
        float v = Mathf.Max(.1f, jumpVelocity) * scale;
        float g = Mathf.Max(.1f, Mathf.Abs(gravityStrength)) * scale * scale;
        float discriminant = v * v - 2f * g * Mathf.Max(0f, height);
        return discriminant > 0f ? 2f * Mathf.Sqrt(discriminant) / g : 0f;
    }

    private const float halfScale = 0.5f;

    private float groundBaseY;
    private float standingScale;
    private float currentScale;

    private float airHeight;
    private float verticalVelocity;
    private float activeJumpGravity;
    private GameModeController modes;

    // Start on the ground
    private VerticalState state = VerticalState.Grounded;

    void Start()
    {
        modes=FindAnyObjectByType<GameModeController>();
        if (obstacleSpawner == null) obstacleSpawner = FindAnyObjectByType<ObstacleSpawner>();
        // Initialize all default state values
        standingScale = transform.localScale.y;
        currentScale = standingScale;
        groundBaseY = transform.position.y - (standingScale * halfScale);
    }

    void Update()
    {
        if (Time.timeScale <= 0f || (modes != null && (modes.AmbushOwnsPlayer || modes.VehicleEscapeActive))) return;
        HandleInput();
        HandleState();
        ApplyVerticalPosition();
    }

    // Check for player input, which then sets information about the player state for HandleState
    void HandleInput()
    {
        // Holding S on landing immediately returns to a crouch. Release to stand.
        if (state != VerticalState.Jumping)
            state = Input.GetKey(KeyCode.S) ? VerticalState.Ducking : VerticalState.Grounded;
        // Jump only allowed from Grounded
        if (Input.GetKeyDown(KeyCode.Space) && state == VerticalState.Grounded)
        {
            // Sample speed at takeoff so a midair difficulty increase cannot jerk the trajectory.
            float speed = obstacleSpawner != null ? Mathf.Max(1f, obstacleSpawner.SpeedMultiplier) : 1f;
            float timeScale = Mathf.Pow(speed, jumpSpeedInfluence);
            verticalVelocity = Mathf.Max(0.1f, jumpVelocity) * timeScale;
            activeJumpGravity = -Mathf.Max(0.1f, Mathf.Abs(gravityStrength)) * timeScale * timeScale;
            state = VerticalState.Jumping;
        }

    }

    // Determine the player's current state based on input status
    void HandleState()
    {
        // [-- Ducking State --]
        float targetScale;
        if (state == VerticalState.Ducking)
        {
            targetScale = duckScale;
        }
        else
        {
            targetScale = standingScale;
        }
        currentScale = Mathf.MoveTowards(currentScale, targetScale, duckVelocity * Time.deltaTime);

        Vector3 scale = transform.localScale;
        scale.y = currentScale;
        transform.localScale = scale;

        // ------------------

        // [ --Jumping State --]
        if (state == VerticalState.Jumping)
        {
            float dt = Time.deltaTime;
            airHeight += verticalVelocity * dt + 0.5f * activeJumpGravity * dt * dt;
            verticalVelocity += activeJumpGravity * dt;

            if (airHeight <= 0f)
            {
                airHeight = 0f;
                verticalVelocity = 0f;
                state = Input.GetKey(KeyCode.S) ? VerticalState.Ducking : VerticalState.Grounded;
            }
        }
        // ----------------
    }

    // Set player to appropriate state based on HandleState
    void ApplyVerticalPosition()
    {
        Vector3 pos = transform.position;
        pos.y = groundBaseY + (currentScale * halfScale) + airHeight;
        transform.position = pos;
    }
}
