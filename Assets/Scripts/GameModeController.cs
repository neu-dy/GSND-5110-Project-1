using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

[DefaultExecutionOrder(1000)]
public class GameModeController : MonoBehaviour
{
    public enum Mode { Menu, Acceleration, Chase }
    public Mode CurrentMode { get; private set; }
    public bool IsPlaying => CurrentMode != Mode.Menu && player != null && !player.HasLost;

    [Header("Scene References")]
    [SerializeField] private LoseCondition player;
    [SerializeField] private TMP_Text textTemplate;

    [Header("Chase - Distance (0 = swallowed)")]
    [SerializeField, Min(1f)] private float startDistance = 65f;
    [SerializeField, Min(1f)] private float maxDistance = 80f;
    [SerializeField, Min(0f)] private float pursuerSpeed = 6.1f;
    [SerializeField, Min(0f)] private float distanceChangeScale = 5f;

    [Header("Chase - Escape Speed")]
    [SerializeField, Min(0f)] private float startingSpeed = 6f;
    [SerializeField, Min(0f)] private float minimumSpeed = 2f;
    [SerializeField, Min(0f)] private float maximumSpeed = 7f;
    [SerializeField, Min(0f)] private float hitSpeedLoss = 1.8f;
    [SerializeField, Min(0f)] private float hitDistanceLoss = 6f;
    [SerializeField, Min(0f)] private float normalDodgeSpeedGain = 0.6f;
    [SerializeField, Min(0f)] private float nearMissSpeedGain = 1f;
    [SerializeField, Min(0f)] private float nearMissClearance = 0.2f;
    [SerializeField, Min(0f)] private float hitProtectionSeconds = 0.7f;

    [Header("Test Feedback")]
    [SerializeField] private bool showChaseStats = true;
    [SerializeField, Min(0f)] private float noticeSeconds = 1.5f;

    [Header("Swallow Effect")]
    [SerializeField, Min(0.1f)] private float deathSweepSeconds = 1.8f;
    [SerializeField, Range(0, 80)] private int swallowParticleCount = 22;
    [SerializeField, Min(0.1f)] private float swallowParticleLifetime = 0.8f;
    public bool IsDeathTransition => swallowing && deathElapsed < Mathf.Max(0.1f, deathSweepSeconds);
    private SwallowParticles particles;
    private bool swallowing;
    private float deathElapsed;
    private float deathStartEdge;
    private Camera frozenCamera;
    private Vector3 frozenCameraPosition;
    private Quaternion frozenCameraRotation;
    private float frozenFov;
    private float frozenOrthoSize;

    private ChaseState chase;
    private GameObject menu;
    private RectTransform curtain;
    private TMP_Text chaseHUD;
    private float nextHitTime;
    private float noticeUntil;
    private string notice;
    private int dodges;
    private Collider playerCollider;

    void Awake()
    {
        CurrentMode = Mode.Menu;
        Time.timeScale = 0f;
    }

    void Start()
    {
        if (player == null) player = FindAnyObjectByType<LoseCondition>();
        playerCollider = player.GetComponent<Collider>();
        BuildInterface();
    }

    public void StartMode(bool chaseMode)
    {
        if (CurrentMode != Mode.Menu) return;
        CurrentMode = chaseMode ? Mode.Chase : Mode.Acceleration;
        chase = new ChaseState(Mathf.Clamp(startingSpeed, minimumSpeed, maximumSpeed),
            Mathf.Min(startDistance, maxDistance));
        menu.SetActive(false);
        curtain.gameObject.SetActive(chaseMode);
        chaseHUD.gameObject.SetActive(chaseMode && showChaseStats);
        Time.timeScale = 1f;
        UpdateChaseDisplay();
    }

    public void TakeHit()
    {
        if (!IsPlaying || CurrentMode != Mode.Chase || Time.time < nextHitTime) return;
        nextHitTime = Time.time + hitProtectionSeconds;
        chase.Hit(hitSpeedLoss, minimumSpeed, hitDistanceLoss);
        SetNotice("INJURED - slowing down");
        UpdateChaseDisplay();
        if (chase.IsCaught) player.Die();
    }

    public void SuccessfulDodge(float clearance)
    {
        if (!IsPlaying || CurrentMode != Mode.Chase) return;
        bool near = clearance > 0f && clearance <= nearMissClearance;
        dodges++;
        chase.Dodge(near ? nearMissSpeedGain : normalDodgeSpeedGain, maximumSpeed);
        SetNotice(near ? "NEAR MISS - speed recovered" : "DODGED - speed recovered");
    }

    private void SetNotice(string value)
    {
        notice = value;
        noticeUntil = Time.unscaledTime + noticeSeconds;
    }

    void Update()
    {
        if (CurrentMode == Mode.Menu) return;
        if (Input.GetKeyDown(KeyCode.Escape)) ReturnToMenu();
        if (swallowing)
        {
            deathElapsed += Time.unscaledDeltaTime;
            float t = Mathf.Clamp01(deathElapsed / Mathf.Max(0.1f, deathSweepSeconds));
            curtain.anchorMax = new Vector2(Mathf.Lerp(deathStartEdge, 1f, Mathf.SmoothStep(0f, 1f, t)), 1f);
            return;
        }
        if (CurrentMode != Mode.Chase || !IsPlaying) return;
        chase.Tick(Time.deltaTime, pursuerSpeed, distanceChangeScale, maxDistance);
        UpdateChaseDisplay();
        if (chase.IsCaught) player.Die();
    }

    public void ReturnToMenu()
    {
        Time.timeScale = 1f;
        SceneManager.LoadScene(gameObject.scene.path);
    }

    public bool BeginSwallowDeath()
    {
        if (CurrentMode != Mode.Chase) return false;
        swallowing = true;
        deathElapsed = 0f;
        deathStartEdge = curtain.anchorMax.x;
        chaseHUD.gameObject.SetActive(false);
        frozenCamera = Camera.main;
        if (frozenCamera != null)
        {
            frozenCameraPosition = frozenCamera.transform.position;
            frozenCameraRotation = frozenCamera.transform.rotation;
            frozenFov = frozenCamera.fieldOfView;
            frozenOrthoSize = frozenCamera.orthographicSize;
        }
        Renderer body = player.GetComponentInChildren<Renderer>();
        if (body != null) EmitSwallow(body);
        foreach (Renderer renderer in player.GetComponentsInChildren<Renderer>()) renderer.enabled = false;
        foreach (Collider collider in player.GetComponentsInChildren<Collider>()) collider.enabled = false;
        var movement = player.GetComponent<PlayerVerticalMovement>();
        if (movement != null) movement.enabled = false;
        return true;
    }

    void LateUpdate()
    {
        if (!swallowing || frozenCamera == null) return;
        frozenCamera.transform.SetPositionAndRotation(frozenCameraPosition, frozenCameraRotation);
        frozenCamera.fieldOfView = frozenFov;
        frozenCamera.orthographicSize = frozenOrthoSize;
    }

    public bool TrySwallowObstacle(Renderer body, ref bool emitted)
    {
        if (CurrentMode != Mode.Chase || body == null || Camera.main == null) return false;
        Camera camera = Camera.main;
        Bounds bounds = body.bounds;
        float left = float.PositiveInfinity;
        float right = float.NegativeInfinity;
        for (int x = -1; x <= 1; x += 2)
        for (int y = -1; y <= 1; y += 2)
        for (int z = -1; z <= 1; z += 2)
        {
            Vector3 corner = bounds.center + Vector3.Scale(bounds.extents, new Vector3(x, y, z));
            float screenX = camera.WorldToViewportPoint(corner).x;
            left = Mathf.Min(left, screenX);
            right = Mathf.Max(right, screenX);
        }
        if (!emitted && left <= curtain.anchorMax.x)
        {
            emitted = true;
            EmitSwallow(body);
        }
        return right <= curtain.anchorMax.x;
    }

    private void EmitSwallow(Renderer body)
    {
        if (Camera.main == null) return;
        Material material = body.sharedMaterial;
        Color color = Color.white;
        if (material != null)
        {
            if (material.HasProperty("_BaseColor")) color = material.GetColor("_BaseColor");
            else if (material.HasProperty("_Color")) color = material.GetColor("_Color");
        }
        Vector3 point = Camera.main.WorldToViewportPoint(body.bounds.center);
        particles.Burst(new Vector2(curtain.anchorMax.x, Mathf.Clamp01(point.y)),
            color, swallowParticleCount, swallowParticleLifetime);
    }

    private void UpdateChaseDisplay()
    {
        if (CurrentMode != Mode.Chase) return;
        Camera camera = Camera.main;
        float playerX = camera != null ? camera.WorldToViewportPoint(playerCollider.bounds.center).x : 0.3f;
        float danger = 1f - Mathf.Clamp01(chase.Distance / Mathf.Max(1f, maxDistance));
        // A screen-space curtain reaches the player's centre when distance is zero.
        curtain.anchorMax = new Vector2(Mathf.Lerp(0.015f, Mathf.Clamp01(playerX + 0.015f), danger), 1f);
        chaseHUD.gameObject.SetActive(showChaseStats);
        chaseHUD.text = $"CHASE   Distance: {chase.Distance:0}   Speed: {chase.Speed:0.0}\n"
            + $"Hits: {chase.Hits}   Dodged: {dodges}"
            + (Time.unscaledTime < noticeUntil ? "\n" + notice : "");
        chaseHUD.color = chase.Distance < 25f ? new Color(1f, 0.55f, 0.45f) : Color.white;
    }

    private void BuildInterface()
    {
        var root = new GameObject("Mode Interface", typeof(RectTransform), typeof(Canvas),
            typeof(CanvasScaler), typeof(GraphicRaycaster));
        Canvas canvas = root.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 10;
        CanvasScaler scaler = root.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1280, 720);
        scaler.matchWidthOrHeight = 0.5f;

        curtain = Panel("Black Curtain", root.transform, Color.black);
        curtain.gameObject.SetActive(false);
        var particleRoot = new GameObject("Swallow Particles", typeof(RectTransform), typeof(SwallowParticles));
        particleRoot.transform.SetParent(root.transform, false);
        var particleRect = particleRoot.GetComponent<RectTransform>();
        particleRect.anchorMin = Vector2.zero;
        particleRect.anchorMax = Vector2.one;
        particleRect.offsetMin = particleRect.offsetMax = Vector2.zero;
        particles = particleRoot.GetComponent<SwallowParticles>();
        chaseHUD = Text("Chase HUD", root.transform, "", new Vector2(0, -80), new Vector2(800, 125), 24);
        chaseHUD.rectTransform.anchorMin = chaseHUD.rectTransform.anchorMax = new Vector2(0.5f, 1f);
        chaseHUD.gameObject.SetActive(false);

        menu = Panel("Main Menu", root.transform, Color.clear).gameObject;
        Button("Acceleration", "ACCELERATION MODE", 45, false);
        Button("Chase", "CHASE MODE", -90, true);
    }

    private RectTransform Panel(string name, Transform parent, Color color)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(Image));
        go.transform.SetParent(parent, false);
        var rect = go.GetComponent<RectTransform>();
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = rect.offsetMax = Vector2.zero;
        go.GetComponent<Image>().color = color;
        go.GetComponent<Image>().raycastTarget = false;
        return rect;
    }

    private TMP_Text Text(string name, Transform parent, string value, Vector2 position, Vector2 size, int fontSize)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(TextMeshProUGUI));
        go.transform.SetParent(parent, false);
        var text = go.GetComponent<TextMeshProUGUI>();
        if (textTemplate != null) text.font = textTemplate.font;
        text.text = value;
        text.fontSize = fontSize;
        text.alignment = TextAlignmentOptions.Center;
        text.raycastTarget = false;
        text.rectTransform.anchorMin = text.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
        text.rectTransform.anchoredPosition = position;
        text.rectTransform.sizeDelta = size;
        return text;
    }

    private void Button(string name, string label, float y, bool chaseMode)
    {
        var rect = Panel(name, menu.transform, new Color(0.14f, 0.19f, 0.24f));
        float playerX = Camera.main != null ? Camera.main.WorldToViewportPoint(playerCollider.bounds.center).x : 0.25f;
        rect.anchorMin = rect.anchorMax = new Vector2(Mathf.Clamp(1f - playerX, 0.6f, 0.78f), 0.5f);
        rect.sizeDelta = new Vector2(360, 65);
        rect.anchoredPosition = new Vector2(0, y);
        Image image = rect.GetComponent<Image>();
        image.raycastTarget = true;
        var button = rect.gameObject.AddComponent<UnityEngine.UI.Button>();
        button.targetGraphic = image;
        button.onClick.AddListener(() => StartMode(chaseMode));
        Text(name + " Label", rect, label, Vector2.zero, new Vector2(350, 60), 23);
    }

    void OnValidate()
    {
        maxDistance = Mathf.Max(1f, maxDistance);
        startDistance = Mathf.Clamp(startDistance, 1f, maxDistance);
        minimumSpeed = Mathf.Max(0f, minimumSpeed);
        maximumSpeed = Mathf.Max(minimumSpeed, maximumSpeed);
        startingSpeed = Mathf.Clamp(startingSpeed, minimumSpeed, maximumSpeed);
        nearMissSpeedGain = Mathf.Max(normalDodgeSpeedGain, nearMissSpeedGain);
    }
}
