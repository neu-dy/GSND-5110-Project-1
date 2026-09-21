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

    void Start()
    {
        transform.position = spawnPosition;
    }

    void Update()
    {
        transform.Translate(Vector3.left * obstacleSpeed * Time.deltaTime, Space.World);

        if (transform.position.x <= despawnX)
        {
            Destroy(gameObject);
        }
    }
}