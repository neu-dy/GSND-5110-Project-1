using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Serialization;

public class LoseCondition : MonoBehaviour
{
    [SerializeField] private GameObject loseText;
    [FormerlySerializedAs("restartDelay")]
    [SerializeField, Min(0f)] private float menuReturnDelay = 3f;
    [FormerlySerializedAs("showRestartCountdown")]
    [SerializeField] private bool showReturnCountdown = true;
    public bool HasLost { get; private set; }
    private TMP_Text message;
    private GameModeController modes;

    void Start()
    {
        modes = FindAnyObjectByType<GameModeController>();
        if (loseText != null)
        {
            message = loseText.GetComponentInChildren<TMP_Text>(true);
            loseText.SetActive(false);
        }
    }

    void OnTriggerEnter(Collider other)
    {
        if (HasLost || !other.CompareTag("Obstacle") || (modes != null && !modes.IsPlaying)) return;
        var movement = other.GetComponentInParent<ObstacleMovement>();
        // Touching an obstacle during protection also disqualifies its dodge reward.
        if (movement != null)
        {
            if (movement.HitPlayer) return;
            movement.MarkPlayerHit();
        }
        if (modes != null && modes.CurrentMode == GameModeController.Mode.Chase)
            modes.TakeHit();
        else Die();
    }

    public void Die()
    {
        if (HasLost) return;
        HasLost = true;
        if (loseText != null) loseText.SetActive(true);
        if (message != null) message.text = "Game over";
        // Keep the death UI above the curtain as it consumes the whole screen.
        if (loseText != null)
        {
            Canvas canvas = loseText.GetComponent<Canvas>();
            if (canvas == null) canvas = loseText.AddComponent<Canvas>();
            canvas.overrideSorting = true;
            canvas.sortingOrder = 30;
        }
        bool swallowDeath = modes != null && modes.BeginSwallowDeath();
        if (!swallowDeath) Time.timeScale = 0f;
        StartCoroutine(ReturnAfterDelay());
    }

    private IEnumerator ReturnAfterDelay()
    {
        float remaining = Mathf.Max(0f, menuReturnDelay);
        while (remaining > 0f || (modes != null && modes.IsDeathTransition))
        {
            if (showReturnCountdown && message != null)
                message.text = remaining > 0f
                    ? "Game over\nMain menu in " + Mathf.CeilToInt(remaining) + "s"
                    : "Game over";
            yield return null;
            remaining -= Time.unscaledDeltaTime;
        }
        Time.timeScale = 1f;
        if (modes != null) modes.ReturnToMenu();
        else SceneManager.LoadScene(gameObject.scene.path);
    }

    void OnDestroy()
    {
        if (HasLost) Time.timeScale = 1f;
    }
}
