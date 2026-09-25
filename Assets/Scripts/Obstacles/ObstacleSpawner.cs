using UnityEngine;
using TMPro;

[DefaultExecutionOrder(300)] // Falling collision observes this frame's jump and sprint input.
public class ObstacleSpawner : MonoBehaviour
{
    // [ -- ObstacleSpawner -- ]
    // Creates instances of a random obstacle at semi-random intervals
    // Toggle only one obstacle spawn vs. multiple allowed at once on screen: allowMultipleObstacles
    // Drag in all prefabs that are considered an "obstacle" into the Inspector    

    [Header("Obstacle Spawning")]
    [SerializeField] private GameObject[] obstaclePrefabs;
    [SerializeField] private float spawnRate = 3f;      // average seconds between spawns
    [SerializeField] private float spawnVariance = 1f;  // +/- range around spawnRate
    [SerializeField] private bool allowMultipleObstacles = false; // TRUE = spawn new regardless of existing spawn
    [Tooltip("Wait until the last obstacle clears the player before timing the next spawn.")]
    [SerializeField] private bool separateObstacleActions = true;
    [SerializeField, Min(0.1f)] private float minimumActionGap = 0.35f;

    private float spawnTimer;
    private GameObject currentObstacle;

    [Header("Falling Obstacles - Horizontal Dodge")]
    [SerializeField] private FallingObstacleDirector.Settings fallingObstacles = new FallingObstacleDirector.Settings();
    private FallingObstacleDirector falling;

    [Header("Wide Head Obstacle Safety")]
    [SerializeField] private GameObject wideHeadObstacle;
    [SerializeField] private GameObject shortHeadObstacle;
    [SerializeField, Min(0f)] private float crouchSafetyReserve = 4f;
    [SerializeField, Min(0f)] private float crouchTimingPadding = 0.15f;

    [Header("Difficulty - Successful Dodges")]
    [SerializeField] private LoseCondition player;
    [SerializeField] private bool increaseSpeed = true;
    [SerializeField, Min(1)] private int dodgesPerSpeedIncrease = 5;
    [SerializeField, Min(0f)] private float speedMultiplierStep = 0.2f;
    [SerializeField, Min(1f)] private float maxSpeedMultiplier = 3f;

    [Header("Test HUD")]
    [SerializeField] private TMP_Text difficultyText;
    [SerializeField] private bool showDifficultyUI = true;
    [SerializeField, Min(0f)] private float speedNoticeDuration = 2f;

    private Collider playerCollider;
    private int dodgedCount;
    private float speedMultiplier = 1f;
    private float noticeUntil;
    private GameModeController modes;

    public bool HasPendingObstacle(float safeLeft)
    {
        if (falling != null && falling.Active) return true;
        foreach (ObstacleMovement obstacle in ObstacleMovement.Active)
            if (obstacle != null && obstacle.OccupiesLaneAheadOf(safeLeft)) return true;
        return false;
    }

    public float SpeedMultiplier => speedMultiplier;
    public float SpawnIntervalMultiplier => modes != null ? modes.EncounterIntervalMultiplier : 1f;
    public string DifficultySummary => showDifficultyUI
        ? $"Obstacle speed: x{speedMultiplier:0.00}" + (Time.unscaledTime < noticeUntil ? "  SPEED UP!" : "")
        : "";

    public bool HasPassedPlayer(Collider obstacleCollider)
    {
        return player != null && !player.HasLost && playerCollider != null
            && obstacleCollider != null && Time.timeScale > 0f
            && obstacleCollider.bounds.max.x < playerCollider.bounds.min.x;
    }

    public float MeasureClearance(Bounds previous, Bounds current)
    {
        if (playerCollider == null) return float.PositiveInfinity;
        Bounds target = playerCollider.bounds;
        // Sweep horizontal travel so fast obstacles cannot skip the sampling zone.
        if (Mathf.Min(previous.min.x, current.min.x) > target.max.x
            || Mathf.Max(previous.max.x, current.max.x) < target.min.x)
            return float.PositiveInfinity;
        float y = Mathf.Max(0f, Mathf.Max(current.min.y - target.max.y, target.min.y - current.max.y));
        float z = Mathf.Max(0f, Mathf.Max(current.min.z - target.max.z, target.min.z - current.max.z));
        return Mathf.Sqrt(y * y + z * z);
    }

    public void RegisterDodge(float clearance)
    {
        if (player == null || player.HasLost || Time.timeScale <= 0f)
            return;
        dodgedCount++;
        if (modes != null && modes.CurrentMode == GameModeController.Mode.Running)
            modes.SuccessfulDodge(clearance);
        RefreshDifficulty();
    }

    private void RefreshDifficulty()
    {
        if (modes != null && modes.CurrentMode == GameModeController.Mode.Menu)
        {
            speedMultiplier = 1f;
            if (difficultyText != null) difficultyText.gameObject.SetActive(false);
            return;
        }
        float nextMultiplier = increaseSpeed
            ? Mathf.Min(Mathf.Max(1f, maxSpeedMultiplier),
                1f + (dodgedCount / Mathf.Max(1, dodgesPerSpeedIncrease)) * Mathf.Max(0f, speedMultiplierStep))
            : 1f;
        if (nextMultiplier > speedMultiplier)
            noticeUntil = Time.unscaledTime + speedNoticeDuration;
        speedMultiplier = nextMultiplier;

        // The combined HUD displays both chase state and obstacle acceleration.
        if (modes != null)
        {
            if (difficultyText != null) difficultyText.gameObject.SetActive(false);
            return;
        }

        if (difficultyText == null)
            return;
        difficultyText.gameObject.SetActive(showDifficultyUI);
        if (showDifficultyUI)
        {
            bool showNotice = Time.unscaledTime < noticeUntil;
            difficultyText.text = $"Dodged: {dodgedCount}   Speed: x{speedMultiplier:0.00}"
                + (showNotice ? "\nSPEED UP!" : "");
            difficultyText.color = showNotice ? Color.yellow : Color.white;
        }
    }

    void Start()
    {
        modes = GetComponent<GameModeController>();
        if (player == null)
            player = FindAnyObjectByType<LoseCondition>();
        if (player != null)
            playerCollider = player.GetComponent<Collider>();
        RefreshDifficulty();
        spawnTimer = GetRandomSpawnInterval();
        falling = new FallingObstacleDirector(this, modes, playerCollider, fallingObstacles);
    }

    void Update()
    {
        if(modes!=null&&modes.VehicleEscapeActive)return;
        RefreshDifficulty();
        if (modes != null && !modes.IsPlaying) { falling?.Cancel(); return; }
        if (Time.timeScale <= 0f) return;
        if (player != null && player.HasLost)
            return;
        falling?.Tick(Time.deltaTime);
        if ((modes != null && modes.TentaclesBlockSpawns) || (falling != null && falling.BlocksSpawns))
        {
            // Resume with a full approach interval, never a queued instant spawn.
            spawnTimer = Mathf.Max(spawnTimer, Mathf.Max(minimumActionGap, spawnRate * SpawnIntervalMultiplier));
            return;
        }
        // Toggles spawning method
        if (allowMultipleObstacles)
        {
            SpawnContinuously();
        }
        else
        {
            SpawnAfterDeload();
        }
    }

    // Can have multiple obstacles on screen at once
    // Will still wait for a spawn timer to prevent impossible playstates
    void SpawnContinuously()
    {
        if (separateObstacleActions && currentObstacle != null && playerCollider != null)
        {
            ObstacleMovement previous = currentObstacle.GetComponent<ObstacleMovement>();
            if (previous != null && !previous.HasPassedPlayer())
                return;
        }
        spawnTimer -= Time.deltaTime;
        if (spawnTimer <= 0f)
        {
            SpawnObstacle();
            spawnTimer = GetRandomSpawnInterval();
        }
    }

    public void StopForVehicleEscape()
    {
        falling?.Cancel();
        var remaining=new System.Collections.Generic.List<ObstacleMovement>(ObstacleMovement.Active);
        foreach(var obstacle in remaining)
        {
            if(obstacle==null)continue;
            obstacle.gameObject.SetActive(false);Destroy(obstacle.gameObject);
        }
        currentObstacle=null;
    }

    // One obstacle at a time only
    // Counts down only after nothing is on screen
    void SpawnAfterDeload()
    {
        if (currentObstacle != null)
        {
            return;
        }

        spawnTimer -= Time.deltaTime;
        if (spawnTimer <= 0f)
        {
            SpawnObstacle();
            spawnTimer = GetRandomSpawnInterval();
        }
    }

    // Handles obstacle spawning based on array of prefabs in Inspector
    void SpawnObstacle()
    {
        if ((modes != null && modes.TentaclesBlockSpawns) || (falling != null && falling.BlocksSpawns)) return;
        // Guard clause (populate prefabs first)
        if (obstaclePrefabs == null || obstaclePrefabs.Length == 0)
        {
            return;
        }

        int index = Random.Range(0, obstaclePrefabs.Length);
        GameObject selected = obstaclePrefabs[index];
        if (selected == wideHeadObstacle && !CanSafelyPassHead(selected))
        {
            selected = shortHeadObstacle;
            // If even the short version is unaffordable, leave this spawn slot empty.
            if (selected == null || !CanSafelyPassHead(selected)) return;
        }
        currentObstacle = Instantiate(selected);
        ObstacleMovement movement = currentObstacle.GetComponent<ObstacleMovement>();
        if (movement != null)
            movement.SetSpawner(this);
    }

    // Sets up the range of possible random values for spawn rate
    private bool CanSafelyPassHead(GameObject prefab)
    {
        if (modes == null || playerCollider == null || Camera.main == null) return false;
        var movement = prefab.GetComponent<ObstacleMovement>();
        var box = prefab.GetComponent<BoxCollider>();
        if (movement == null || box == null) return false;
        float speed = movement.BaseObstacleSpeed * SpeedMultiplier;
        if (speed <= 0f) return false;
        Vector3 size = Vector3.Scale(box.size, prefab.transform.localScale);
        Vector3 centre = movement.SpawnPosition + Vector3.Scale(box.center, prefab.transform.localScale);
        float depth = Camera.main.WorldToViewportPoint(centre).z + Mathf.Abs(size.z) * 0.5f;
        if (depth <= Camera.main.nearClipPlane) return false;
        // Same right-edge spawn placement used by ObstacleMovement.
        float leadingEdge = Camera.main.ViewportToWorldPoint(new Vector3(1f, 0.5f, depth)).x + 0.5f;
        float arrival = Mathf.Max(0f, (leadingEdge - playerCollider.bounds.max.x) / speed - crouchTimingPadding);
        float duration = (Mathf.Abs(size.x) + playerCollider.bounds.size.x) / speed + 2f * crouchTimingPadding;
        return modes.CanAffordCrouch(arrival, duration, crouchSafetyReserve);
    }

    // Interval is (spawnRate +/- spawnVariance)
    float GetRandomSpawnInterval()
    {
        return Mathf.Max(Mathf.Max(0.1f, minimumActionGap),
            (spawnRate + Random.Range(-spawnVariance, spawnVariance)) * SpawnIntervalMultiplier);
    }

    void OnDestroy() => falling?.Dispose();
    void OnValidate()
    {
        if (fallingObstacles == null) fallingObstacles = new FallingObstacleDirector.Settings();
        fallingObstacles.Validate();
    }
}
