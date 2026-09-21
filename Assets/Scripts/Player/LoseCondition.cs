using UnityEngine;

public class LoseCondition : MonoBehaviour
{
    // [ -- LoseCondition -- ]
    // Sets up game response to lose on obstacle collision
    
    [SerializeField] private GameObject loseText; // Drag in the UI element

    void Start()
    {
        loseText.SetActive(false);
    }

    void OnTriggerEnter(Collider other)
    {
        if (other.CompareTag("Obstacle"))
        {
            loseText.SetActive(true);

            // This should be changed later; sets Time.Deltatime to not work
            Time.timeScale = 0f;
        }
    }
}
