using UnityEngine;

public class ObstacleMovement : MonoBehaviour
{
    // [ -- ObstacleMovement -- ]
    // Handles movement for obstacle right-to-left movement
    // Attach this to every obstacle that moves

    // Can be changed freely for each obstacle via Inspector
    [Header("Obstacle Movement")]
    [SerializeField] private Vector3 spawnPosition;
    [SerializeField] private float obstacleSpeed = 6f;
    [SerializeField] private float despawnX = -15f; // world X coord where object is destroyed
    private ObstacleSpawner spawner;
    private Collider obstacleCollider;
    private bool countedAsDodged;
    private float closestClearance = float.PositiveInfinity;
    private GameModeController modes;
    private Renderer body;
    private bool swallowEmitted;
    public bool HitPlayer { get; private set; }
    public void MarkPlayerHit() => HitPlayer = true;

    public void SetSpawner(ObstacleSpawner owner)
    {
        spawner = owner;
    }

    void Start()
    {
        modes = FindAnyObjectByType<GameModeController>();
        body = GetComponent<Renderer>();
        obstacleCollider = GetComponent<Collider>();
        Vector3 position = spawnPosition;
        Camera gameCamera = Camera.main;
        if (gameCamera != null)
        {
            // Keep the prefab's gameplay height/depth, but start beyond the
            // right edge at any Game view aspect ratio (including perspective).
            transform.position = position;
            Renderer obstacleRenderer = GetComponent<Renderer>();
            Bounds bounds = obstacleRenderer != null
                ? obstacleRenderer.bounds
                : new Bounds(position, Vector3.zero);
            float depth = gameCamera.WorldToViewportPoint(bounds.center).z
                + bounds.extents.z;
            if (depth > gameCamera.nearClipPlane)
            {
                float rightEdge = gameCamera.ViewportToWorldPoint(
                    new Vector3(1f, 0.5f, depth)).x;
                position.x += rightEdge + 0.5f - bounds.min.x;
            }
        }
        transform.position = position;
    }

    void Update()
    {
        if (Time.timeScale <= 0f) return;
        Bounds previousBounds = obstacleCollider != null ? obstacleCollider.bounds : default;
        float multiplier = spawner != null ? spawner.SpeedMultiplier : 1f;
        transform.Translate(Vector3.left * obstacleSpeed * multiplier * Time.deltaTime, Space.World);
        if (modes != null && modes.TrySwallowObstacle(body, ref swallowEmitted))
        {
            Destroy(gameObject);
            return;
        }

        if (!countedAsDodged && spawner != null && obstacleCollider != null)
            closestClearance = Mathf.Min(closestClearance,
                spawner.MeasureClearance(previousBounds, obstacleCollider.bounds));

        if (!countedAsDodged && spawner != null && spawner.HasPassedPlayer(obstacleCollider))
        {
            countedAsDodged = true;
            if (!HitPlayer && closestClearance > 0f)
                spawner.RegisterDodge(closestClearance);
        }

        if (transform.position.x <= despawnX)
        {
            Destroy(gameObject);
        }
    }
}
