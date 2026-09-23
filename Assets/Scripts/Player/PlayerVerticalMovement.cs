using UnityEngine;

public class PlayerVerticalMovement : MonoBehaviour
{
    // [ -- PlayerVerticalMovement -- ]
    // Contains the functions necessary for sidescrolling player movement
    // Three possible states: running while Grounded; Ducking ; Jumping 
    private enum VerticalState { Grounded, Ducking, Jumping }

    // Edit via Inspector
    [Header("Jump Attributes")]
    [SerializeField] private float jumpVelocity = 9f;
    [SerializeField] private float gravityStrength = -16f;

    // Edit via Inspector
    [Header("Duck Attributes")]
    [SerializeField] private float duckScale = 0.5f;
    [SerializeField] private float duckVelocity = 10f;
    [SerializeField] private float duckDuration = 0.5f;

    private const float halfScale = 0.5f;
    private float duckTimer;

    private float groundBaseY;
    private float standingScale;
    private float currentScale;

    private float airHeight;
    private float verticalVelocity;

    // Start on the ground
    private VerticalState state = VerticalState.Grounded;

    void Start()
    {
        // Initialize all default state values
        standingScale = transform.localScale.y;
        currentScale = standingScale;
        groundBaseY = transform.position.y - (standingScale * halfScale);
    }

    void Update()
    {
        if (Time.timeScale <= 0f) return;
        HandleInput();
        HandleState();
        ApplyVerticalPosition();
    }

    // Check for player input, which then sets information about the player state for HandleState
    void HandleInput()
    {
        // Jump only allowed from Grounded
        if (Input.GetKeyDown(KeyCode.Space) && state == VerticalState.Grounded)
        {
            verticalVelocity = jumpVelocity;
            state = VerticalState.Jumping;
        }

        // Duck triggers from Grounded — returns to standing (Grounded) after duckDuration
        if (Input.GetKeyDown(KeyCode.S) && state == VerticalState.Grounded)
        {
            state = VerticalState.Ducking;
            duckTimer = duckDuration;
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

        if (state == VerticalState.Ducking)
        {
            duckTimer -= Time.deltaTime;
            if (duckTimer <= 0f)
            {
                state = VerticalState.Grounded;
            }
        }
        // ------------------

        // [ --Jumping State --]
        if (state == VerticalState.Jumping)
        {
            verticalVelocity += gravityStrength * Time.deltaTime;
            airHeight += verticalVelocity * Time.deltaTime;

            if (airHeight <= 0f)
            {
                airHeight = 0f;
                verticalVelocity = 0f;
                state = VerticalState.Grounded;
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
