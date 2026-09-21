using UnityEngine;

public class PlayerJump : MonoBehaviour
{
    [Header("Player Jump Attributes")]
    [SerializeField] private float jumpVelocity = 9f;
    [SerializeField] private float gravityStrength = -16f;

    private float groundHeight = 0.375f;
    private float verticalVelocity;
    private bool isGrounded = true;
    
    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        
    }

    // Update is called once per frame
    void Update()
    {
        // Character can only jump once at a time
        if (Input.GetKeyDown(KeyCode.Space) && isGrounded)
        {
            verticalVelocity = jumpVelocity;
            isGrounded = false;
        }

        // Set jump acceleration
        verticalVelocity += gravityStrength * Time.deltaTime;
        transform.Translate(Vector3.up * verticalVelocity * Time.deltaTime, Space.World);

        // Check if grounded - character can only jump from ground
        if (transform.position.y <= groundHeight)
        {
            Vector3 pos = transform.position;
            pos.y = groundHeight;
            transform.position = pos; // prevent clipping through floor
            verticalVelocity = 0f;
            isGrounded = true;
        }
    }
}
