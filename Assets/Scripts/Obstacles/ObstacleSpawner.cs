using UnityEngine;

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

    void Start()
    {
        spawnTimer = GetRandomSpawnInterval();
    }

    void Update()
    {
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
    }

    // Sets up the range of possible random values for spawn rate
    // Interval is (spawnRate +/- spawnVariance)
    float GetRandomSpawnInterval()
    {
        return spawnRate + Random.Range(-spawnVariance, spawnVariance);
    }
}
