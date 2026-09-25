using UnityEngine;
using System.Collections.Generic;

[DefaultExecutionOrder(200)]
public class ObstacleMovement : MonoBehaviour
{
    [Header("Obstacle Movement")]
    [SerializeField] private Vector3 spawnPosition;
    [SerializeField] private float obstacleSpeed = 6f;
    [SerializeField] private float despawnX = -15f;
    public float BaseObstacleSpeed => obstacleSpeed;
    public Vector3 SpawnPosition => spawnPosition;
    public bool HitPlayer { get; private set; }
    private static readonly HashSet<ObstacleMovement> active = new HashSet<ObstacleMovement>();
    public static IEnumerable<ObstacleMovement> Active => active;
    void OnEnable() => active.Add(this);
    void OnDisable() => active.Remove(this);
    public bool OccupiesLaneAheadOf(float x)
    {
        if (broken) return false;
        foreach (Collider part in obstacleColliders)
            if (part != null && part.enabled && part.bounds.max.x >= x) return true;
        return false;
    }

    private ObstacleSpawner spawner;
    private GameModeController modes;
    private Collider[] obstacleColliders;
    private Renderer[] bodies;
    private Bounds[] previousBounds;
    private bool[] swallowEmitted;
    private bool[] swallowed;
    private bool countedAsDodged;
    private bool broken;
    private float closestClearance = float.PositiveInfinity;

    void Awake()
    {
        obstacleColliders = GetComponentsInChildren<Collider>();
        bodies = GetComponentsInChildren<Renderer>();
        previousBounds = new Bounds[obstacleColliders.Length];
        swallowEmitted = new bool[bodies.Length];
        swallowed = new bool[bodies.Length];
    }

    public void MarkPlayerHit() => HitPlayer = true;
    public void SetSpawner(ObstacleSpawner owner) => spawner = owner;

    // Use the whole group for passing/spawning, but each solid part for clearance.
    // An enclosing collision box would incorrectly fill an airborne opening.
    public bool HasPassedPlayer()
    {
        if (spawner == null || obstacleColliders.Length == 0) return false;
        foreach (Collider part in obstacleColliders)
            if (part != null && part.enabled && !spawner.HasPassedPlayer(part)) return false;
        return true;
    }

    public void BreakPart(Collider touched)
    {
        if (broken || touched == null || !touched.enabled
            || System.Array.IndexOf(obstacleColliders, touched) < 0) return;
        HitPlayer = true;
        Transform solid = touched.transform;
        for (int i = 0; i < bodies.Length; i++)
        {
            if (bodies[i] == null || !bodies[i].transform.IsChildOf(solid)) continue;
            if (modes != null && !swallowed[i]) modes.EmitObstacleBreak(bodies[i]);
            bodies[i].enabled = false;
            swallowed[i] = true;
        }
        foreach (Collider part in obstacleColliders)
            if (part != null && part.transform.IsChildOf(solid)) part.enabled = false;
        // Keep the shared moving root while another solid section remains.
        foreach (Collider part in obstacleColliders)
            if (part != null && part.enabled) return;
        broken = true;
        enabled = false;
        Destroy(gameObject);
    }

    public void BreakApart()
    {
        if (broken) return;
        broken = true;
        HitPlayer = true;
        for (int i = 0; i < bodies.Length; i++)
        {
            if (modes != null && !swallowed[i]) modes.EmitObstacleBreak(bodies[i]);
            bodies[i].enabled = false;
        }
        foreach (Collider part in obstacleColliders) part.enabled = false;
        enabled = false;
        Destroy(gameObject);
    }

    void Start()
    {
        modes = FindAnyObjectByType<GameModeController>();
        Vector3 position = spawnPosition;
        transform.position = position;
        Camera gameCamera = Camera.main;
        if (gameCamera != null && bodies.Length > 0)
        {
            Bounds bounds = bodies[0].bounds;
            for (int i = 1; i < bodies.Length; i++) bounds.Encapsulate(bodies[i].bounds);
            float depth = gameCamera.WorldToViewportPoint(bounds.center).z + bounds.extents.z;
            if (depth > gameCamera.nearClipPlane)
            {
                float rightEdge = gameCamera.ViewportToWorldPoint(new Vector3(1f, 0.5f, depth)).x;
                position.x += rightEdge + 0.5f - bounds.min.x;
            }
        }
        transform.position = position;
    }

    void Update()
    {
        if (Time.timeScale <= 0f) return;
        for (int i = 0; i < obstacleColliders.Length; i++)
            previousBounds[i] = obstacleColliders[i].bounds;
        float multiplier = spawner != null ? spawner.SpeedMultiplier : 1f;
        transform.Translate(Vector3.left * obstacleSpeed * multiplier * Time.deltaTime, Space.World);

        bool allSwallowed = bodies.Length > 0;
        for (int i = 0; i < bodies.Length; i++)
        {
            if (!swallowed[i] && modes != null
                && modes.TrySwallowObstacle(bodies[i], ref swallowEmitted[i]))
            {
                swallowed[i] = true;
                bodies[i].enabled = false;
            }
            allSwallowed &= swallowed[i];
        }
        if (allSwallowed)
        {
            Destroy(gameObject);
            return;
        }

        if (!countedAsDodged && spawner != null)
        {
            for (int i = 0; i < obstacleColliders.Length; i++)
            {
                if (obstacleColliders[i] == null || !obstacleColliders[i].enabled) continue;
                closestClearance = Mathf.Min(closestClearance,
                    spawner.MeasureClearance(previousBounds[i], obstacleColliders[i].bounds));
            }
            if (HasPassedPlayer())
            {
                countedAsDodged = true;
                if (!HitPlayer && closestClearance > 0f && !float.IsPositiveInfinity(closestClearance))
                    spawner.RegisterDodge(closestClearance);
            }
        }
        if (transform.position.x <= despawnX) Destroy(gameObject);
    }
}
