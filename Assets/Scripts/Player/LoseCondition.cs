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
    private const string DeathTitle = "CONSUMED BY MISFORTUNE...";
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
        if (HasLost || !other.enabled || !other.CompareTag("Obstacle") || (modes != null && !modes.IsPlaying)) return;
        var movement = other.GetComponentInParent<ObstacleMovement>();
        // Any contact disqualifies the whole obstacle from earning a dodge reward.
        if (movement != null)
        {
            movement.MarkPlayerHit();
        }
        if (modes != null && modes.CurrentMode == GameModeController.Mode.Running)
            modes.TakeHit();
        else Die();
        if (movement != null) movement.BreakPart(other);
    }

    public void Die()
    {
        if (HasLost) return;
        HasLost = true;
        if (loseText != null) loseText.SetActive(true);
        if (message != null)
        {
            message.text = DeathTitle;
            message.color = new Color(1f, .16f, .12f);
            message.enableAutoSizing = true;
            message.fontSizeMax = message.fontSize;
            message.fontSizeMin = 18f;
        }
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
                    ? DeathTitle + "\n<size=55%><color=#D9D9D9>Main menu in " + Mathf.CeilToInt(remaining) + "s</color></size>"
                    : DeathTitle;
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

