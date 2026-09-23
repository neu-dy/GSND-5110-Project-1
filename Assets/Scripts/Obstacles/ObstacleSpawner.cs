using UnityEngine;
using TMPro;

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

    private float spawnTimer;
    private GameObject currentObstacle;

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

    public float SpeedMultiplier => speedMultiplier;

    public bool HasPassedPlayer(Collider obstacleCollider)
    {
        return player != null && !player.HasLost && playerCollider != null
            && obstacleCollider != null && Time.timeScale > 0f
            && obstacleCollider.bounds.max.x < playerCollider.bounds.min.x;
    }

    public void RegisterDodge()
    {
        if (player == null || player.HasLost || Time.timeScale <= 0f)
            return;
        dodgedCount++;
        RefreshDifficulty();
    }

    private void RefreshDifficulty()
    {
        float nextMultiplier = increaseSpeed
            ? Mathf.Min(Mathf.Max(1f, maxSpeedMultiplier),
                1f + (dodgedCount / Mathf.Max(1, dodgesPerSpeedIncrease)) * Mathf.Max(0f, speedMultiplierStep))
            : 1f;
        if (nextMultiplier > speedMultiplier)
            noticeUntil = Time.unscaledTime + speedNoticeDuration;
        speedMultiplier = nextMultiplier;

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
        if (player == null)
            player = FindAnyObjectByType<LoseCondition>();
        if (player != null)
            playerCollider = player.GetComponent<Collider>();
        RefreshDifficulty();
        spawnTimer = GetRandomSpawnInterval();
    }

    void Update()
    {
        RefreshDifficulty();
        if (player != null && player.HasLost)
            return;
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
        spawnTimer -= Time.deltaTime;
        if (spawnTimer <= 0f)
        {
            SpawnObstacle();
            spawnTimer = GetRandomSpawnInterval();
        }
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
        // Guard clause (populate prefabs first)
        if (obstaclePrefabs.Length == 0)
        {
            return;
        }

        int index = Random.Range(0, obstaclePrefabs.Length);
        currentObstacle = Instantiate(obstaclePrefabs[index]);
        ObstacleMovement movement = currentObstacle.GetComponent<ObstacleMovement>();
        if (movement != null)
            movement.SetSpawner(this);
    }

    // Sets up the range of possible random values for spawn rate
    // Interval is (spawnRate +/- spawnVariance)
    float GetRandomSpawnInterval()
    {
        return spawnRate + Random.Range(-spawnVariance, spawnVariance);
    }
}
