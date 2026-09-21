using UnityEngine;

public class PlayerDuck : MonoBehaviour
{
    [Header("Player Duck Attributes")]
    [SerializeField] private float duckScale = 0.5f;
    [SerializeField] private float duckVelocity = 10f;
    
    private float standingScale;
    private float standingBase;
    private bool isDucking = false;
    private const float halfScale = 0.5f;

    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        standingScale = transform.localScale.y;
        standingBase = transform.position.y - (standingScale * halfScale);
    }

    // Update is called once per frame
    void Update()
    {
        // Set ducking state to true on W press
        if (Input.GetKeyDown(KeyCode.W))
        {
            isDucking = !isDucking;
        }

        // Set player Y-scale to duck or standing state scale
        float targetScale;
        if (isDucking)
        {
            targetScale = duckScale;
        }
        else
        {
            targetScale = standingScale;
        }

        // 
        Vector3 scale = transform.localScale;
        scale.y = Mathf.MoveTowards(scale.y, targetScale, duckVelocity * Time.deltaTime);
        transform.localScale = scale;

        // Keep feet planted: recompute Y from the fixed ground base each frame
        Vector3 pos = transform.position;
        pos.y = standingBase + (scale.y * halfScale);
        transform.position = pos;
    }
}
