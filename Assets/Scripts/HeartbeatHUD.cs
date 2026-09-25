using TMPro;
using UnityEngine;

public class HeartbeatHUD : MonoBehaviour
{
    private PlayerSprint runner;
    private GameModeController modes;
    private RectTransform group;
    private HeartGraphic heart;
    private HeartGlowGraphic glow;
    private float introStress;
    private TMP_Text digits;
    private CanvasGroup opacity;
    private float phase;
    private float variationTime;

    public void Initialize(PlayerSprint source, GameModeController controller, TMP_FontAsset font)
    {
        runner = source;
        modes = controller;
        group = (RectTransform)transform;
        group.anchorMin = group.anchorMax = new Vector2(1f, 1f);
        group.anchoredPosition = new Vector2(-115f, -70f);
        group.sizeDelta = new Vector2(170f, 70f);
        opacity = gameObject.AddComponent<CanvasGroup>();
        opacity.blocksRaycasts = false;
        opacity.interactable = false;

        var halo = new GameObject("Heartbeat Flash", typeof(RectTransform), typeof(CanvasRenderer), typeof(HeartGlowGraphic));
        halo.transform.SetParent(transform, false);
        glow = halo.GetComponent<HeartGlowGraphic>();
        glow.raycastTarget = false;
        glow.rectTransform.sizeDelta = new Vector2(52f, 52f);
        glow.rectTransform.anchoredPosition = new Vector2(-48f, 0f);

        var icon = new GameObject("Heart", typeof(RectTransform), typeof(CanvasRenderer), typeof(HeartGraphic));
        icon.transform.SetParent(transform, false);
        heart = icon.GetComponent<HeartGraphic>();
        heart.raycastTarget = false;
        heart.rectTransform.sizeDelta = new Vector2(52f, 52f);
        heart.rectTransform.anchoredPosition = new Vector2(-48f, 0f);
        var number = new GameObject("Heart Rate", typeof(RectTransform), typeof(TextMeshProUGUI));
        number.transform.SetParent(transform, false);
        digits = number.GetComponent<TextMeshProUGUI>();
        if (font != null) digits.font = font;
        digits.fontSize = 38f;
        digits.fontStyle = FontStyles.Bold;
        digits.alignment = TextAlignmentOptions.Center;
        digits.raycastTarget = false;
        digits.rectTransform.sizeDelta = new Vector2(100f, 65f);
        digits.rectTransform.anchoredPosition = new Vector2(32f, 0f);
        digits.outlineWidth = .15f;
        digits.outlineColor = new Color32(20, 28, 30, 220);
        opacity.alpha = 0f;
    }

    void LateUpdate()
    {
        if (runner == null || runner.Heart == null || modes == null)
        {
            if (opacity != null) opacity.alpha = 0f;
            return;
        }
        if (modes.HasLost)
        {
            // Keep the final reading visible over the opaque death sweep.
            // No pulse, variation, overload flash or residual UI scale after death.
            opacity.alpha = 1f;
            phase = introStress = 0f;
            glow.SetIntensity(0f);
            heart.color = digits.color = new Color(.65f, .06f, .07f, 1f);
            digits.SetText("0");
            heart.rectTransform.localScale = Vector3.one;
            group.localScale = Vector3.one;
            return;
        }
        HeartbeatState state = runner.Heart;
        bool menu = modes.CurrentMode == GameModeController.Mode.Menu;
        float dt = menu || (modes.AmbushOwnsPlayer && Time.timeScale>0) ? Time.unscaledDeltaTime : Time.deltaTime;
        introStress = Mathf.MoveTowards(introStress, modes.IsMenuStarting ? 1f : 0f,
            dt / Mathf.Max(.1f, modes.IsMenuStarting ? runner.MenuHeartRiseSeconds : runner.MenuHeartSettleSeconds));
        variationTime += dt;
        float angle = variationTime * 2f * Mathf.PI / Mathf.Max(2f, runner.VariationPeriod);
        float wave = .7f * Mathf.Sin(angle) + .3f * Mathf.Sin(angle * 1.73f);
        // Fade variation at the active lock/unlock boundary so the number agrees
        // with the visual overload signal. This never feeds back into HeartbeatState.
        float boundaryGap = state.Overheated ? state.Bpm - state.ResumeBpm : state.LimitBpm - state.Bpm;
        float envelope = Mathf.Clamp01(boundaryGap / (runner.BpmVariation + 1f));
        float displayedBpm = state.Bpm + wave * runner.BpmVariation * envelope;
        displayedBpm = state.Overheated
            ? Mathf.Clamp(displayedBpm, state.ResumeBpm + 1f, state.LimitBpm)
            : Mathf.Clamp(displayedBpm, 30f, state.LimitBpm - 1f);
        // Ambush anxiety is presentation only; it cannot secretly overload sprint.
        displayedBpm=Mathf.Max(displayedBpm,Mathf.Lerp(state.RestBpm,state.LimitBpm-3f,modes.AmbushStress));
        // The opening performance never changes actual exertion or sprint limits.
        float introBpm = Mathf.Lerp(state.RestBpm, Mathf.Clamp(runner.MenuPeakBpm, state.RestBpm, state.LimitBpm-1f), introStress);
        displayedBpm = Mathf.Max(displayedBpm, introBpm);
        phase = Mathf.Repeat(phase + dt * displayedBpm / 60f, 1f);
        float first = Mathf.Exp(-Mathf.Pow((phase - .12f) / .065f, 2f));
        float second = .45f * Mathf.Exp(-Mathf.Pow((phase - .32f) / .08f, 2f));
        float pulse = first + second;
        float stress = Mathf.InverseLerp(state.RestBpm, state.LimitBpm, displayedBpm);
        Color green = new Color(.25f, .85f, .4f);
        Color yellow = new Color(1f, .72f, .12f);
        Color red = new Color(1f, .15f, .16f);
        Color color = stress < .55f ? Color.Lerp(green, yellow, stress/.55f)
            : Color.Lerp(yellow, red, (stress-.55f)/.45f);
        float flashWidth = Mathf.Max(.02f, runner.OverloadFlashSeconds) * displayedBpm / 60f;
        float flash = Mathf.Exp(-Mathf.Pow((phase - .12f) / flashWidth, 2f));
        // A steady red base with a short warm highlight, never a dark trough.
        if (state.Overheated) color = Color.Lerp(red, new Color(1f, .52f, .3f), flash);
        glow.SetIntensity(state.Overheated ? flash * runner.OverloadGlowStrength : 0f);
        heart.color = color;
        digits.color = color;
        digits.SetText("{0:0}", displayedBpm);
        heart.rectTransform.localScale = Vector3.one * (1f + runner.BeatScale * pulse);
        glow.rectTransform.localScale = heart.rectTransform.localScale;
        float emphasis = state.Overheated ? runner.OverloadScale + .08f * first
            : runner.NeedsSprintRelease ? 1.06f + .04f * first : 1f;
        group.localScale = Vector3.one * emphasis;
        // Keep the pale scene from bleeding through the warning.
        opacity.alpha = 1f;
    }
}
