using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;

public class LoseCondition : MonoBehaviour
{
    [SerializeField] private GameObject loseText;
    [SerializeField] private bool autoRestart = true;
    [SerializeField, Min(0f)] private float restartDelay = 3f;
    [SerializeField] private bool showRestartCountdown = true;

    private bool hasLost;
    public bool HasLost => hasLost;
    private TMP_Text message;
    private string originalMessage;

    void Start()
    {
        if (loseText != null)
        {
            message = loseText.GetComponentInChildren<TMP_Text>(true);
            if (message != null)
                originalMessage = message.text;
            loseText.SetActive(false);
        }
    }

    void OnTriggerEnter(Collider other)
    {
        if (hasLost || !other.CompareTag("Obstacle"))
            return;

        hasLost = true;
        if (loseText != null)
            loseText.SetActive(true);

        Time.timeScale = 0f;
        if (autoRestart)
            StartCoroutine(RestartAfterDelay());
    }

    private IEnumerator RestartAfterDelay()
    {
        // The game is paused, so scaled time would never finish this wait.
        float remaining = Mathf.Max(0f, restartDelay);
        while (remaining > 0f)
        {
            if (showRestartCountdown && message != null)
                message.text = originalMessage + "\nRestart in " + Mathf.CeilToInt(remaining) + "s";
            yield return null;
            remaining -= Time.unscaledDeltaTime;
        }
        Time.timeScale = 1f;
        SceneManager.LoadScene(gameObject.scene.path);
    }

    void OnDestroy()
    {
        // Also restore time when leaving the scene during the death screen.
        if (hasLost)
            Time.timeScale = 1f;
    }
}
