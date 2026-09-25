using System;

// Logical escape margin. Screen-space sprint displacement is applied by the controller,
// never fed back into this reserve or into the independent pursuit rhythm.
public sealed class ChaseState
{
    public enum PursuitPhase { Rest, Warning, Approach, Hold, Retreat }

    [Serializable]
    public sealed class Settings
    {
#if UNITY_5_3_OR_NEWER
        [UnityEngine.Header("Safety Reserve (ordinary injuries)")]
        [UnityEngine.Tooltip("Opening reserve. 2.4 normally survives two close injuries; a third can exhaust it.")]
#endif
        public float startingReserve = 2.4f;
        public float maximumReserve = 3f;
#if UNITY_5_3_OR_NEWER
        [UnityEngine.Tooltip("Chase distance per reserve unit. This changes visual separation, not the m/km counter.")]
#endif
        public float distancePerReserve = 12.5f;
        public float hitReserveCost = 1f;
#if UNITY_5_3_OR_NEWER
        [UnityEngine.Tooltip("Fraction paid on impact. The rest pays for actual injury slowdown, with a capped budget; dodge recovery can reduce that cost.")]
#endif
        public float immediateHitFraction = .65f;
#if UNITY_5_3_OR_NEWER
        [UnityEngine.Header("Clean-Run Recovery")]
        [UnityEngine.Tooltip("Seconds without another collision before reserve recovery begins. Sprint does not reset this timer.")]
#endif
        public float recoveryDelay = 4f;
#if UNITY_5_3_OR_NEWER
        [UnityEngine.Tooltip("Fraction of missing reserve recovered per second, using exponential smoothing. 0.11 regains roughly one injury in 9 seconds after the delay at the opening reserve.")]
#endif
        public float recoveryRate = .11f;
#if UNITY_5_3_OR_NEWER
        [UnityEngine.Tooltip("Recovery multiplier at full sprint effort, including at the right boundary. 0.5 means half-speed recovery.")]
#endif
        public float sprintRecoveryMultiplier = .5f;
#if UNITY_5_3_OR_NEWER
        [UnityEngine.Header("Independent Pursuit Rhythm (seconds)")]
#endif
        public float restSeconds = 5f;
        public float warningSeconds = 1.5f;
        public float approachSeconds = 4f;
        public float holdSeconds = 3f;
        public float retreatSeconds = 4f;
#if UNITY_5_3_OR_NEWER
        [UnityEngine.Header("Temporary Breathing (chase distance)")]
        [UnityEngine.Tooltip("Extra gap during rest. Approach removes only this gap; it never drains safety reserve.")]
#endif
        public float breathingDistance = 6f;
        public float normalDodgeBreathing = .5f;
        public float nearMissBreathing = 2f;
        public float maximumDodgeBreathing = 3f;
        public float dodgeBreathingDecay = 1f;

        public Settings Copy() => (Settings)MemberwiseClone();

        public void Validate()
        {
            maximumReserve = Math.Max(.1f, maximumReserve);
            startingReserve = Clamp(startingReserve, 0f, maximumReserve);
            distancePerReserve = Math.Max(.1f, distancePerReserve);
            hitReserveCost = Math.Max(0f, hitReserveCost);
            immediateHitFraction = Clamp(immediateHitFraction, 0f, 1f);
            recoveryDelay = Math.Max(0f, recoveryDelay);
            recoveryRate = Math.Max(0f, recoveryRate);
            sprintRecoveryMultiplier = Clamp(sprintRecoveryMultiplier, 0f, 1f);
            restSeconds = Math.Max(.1f, restSeconds);
            warningSeconds = Math.Max(.1f, warningSeconds);
            approachSeconds = Math.Max(.1f, approachSeconds);
            holdSeconds = Math.Max(.1f, holdSeconds);
            retreatSeconds = Math.Max(.1f, retreatSeconds);
            breathingDistance = Math.Max(0f, breathingDistance);
            normalDodgeBreathing = Math.Max(0f, normalDodgeBreathing);
            nearMissBreathing = Math.Max(normalDodgeBreathing, nearMissBreathing);
            maximumDodgeBreathing = Math.Max(0f, maximumDodgeBreathing);
            dodgeBreathingDecay = Math.Max(.01f, dodgeBreathingDecay);
        }
    }

    public float Speed { get; private set; }
    public float LastTravel { get; private set; }
    public float BaseSpeed { get; private set; }
    public float Reserve { get; private set; }
    public float PendingInjuryLoss { get; private set; }
    public float DodgeBreathing { get; private set; }
    public float Pressure { get; private set; }
    public float Telegraph { get; private set; }
    public PursuitPhase Phase { get; private set; }
    public float SecondsSinceHit { get; private set; }
    public int Hits { get; private set; }
    public float TemporaryDistance => settings.breathingDistance * (1f - Pressure) + DodgeBreathing;
    // Signed: a negative margin can still be rescued by physical sprint separation.
    public float Distance => Reserve * settings.distancePerReserve + TemporaryDistance;
    public bool IsCaught => Distance <= 0f;
    private Settings settings;
    private double rhythmTime;
    private float injuryLossPerTravel;

    public ChaseState(float speed, Settings settings)
    {
        this.settings = settings ?? new Settings();
        this.settings.Validate();
        Speed = BaseSpeed = Math.Max(0f, speed);
        Reserve = this.settings.startingReserve;
        UpdateRhythm();
    }

    public void Tick(float seconds, float boostDecayPerSecond, float injuryRecoveryPerSecond,
        float sprintIntensity = 0f)
    {
        float dt = Math.Max(0f, seconds);
        LastTravel = 0f;
        if (dt <= 0f) return;
        float rate = Math.Max(0f, Speed > BaseSpeed ? boostDecayPerSecond : injuryRecoveryPerSecond);
        float difference = BaseSpeed - Speed;
        float transition = rate > 0f ? Math.Min(dt, Math.Abs(difference) / rate) : 0f;
        float endSpeed = Speed + Math.Sign(difference) * rate * transition;
        LastTravel = (Speed + endSpeed) * .5f * transition + endSpeed * (dt - transition);
        // Pay only the actual slowing cost, capped by the remaining injury budget.
        // A dodge that shortens the injury can avoid some of this cost.
        float loss = Math.Min(PendingInjuryLoss, Math.Max(0f, BaseSpeed * dt - LastTravel) * injuryLossPerTravel);
        Reserve -= loss;
        PendingInjuryLoss -= loss;
        Speed = endSpeed;
        if (Speed >= BaseSpeed - .0001f) PendingInjuryLoss = 0f;

        float previousTime = SecondsSinceHit;
        SecondsSinceHit += dt;
        float recoveryTime = Math.Max(0f, SecondsSinceHit - Math.Max(previousTime, settings.recoveryDelay));
        float recoveryFactor = 1f + (settings.sprintRecoveryMultiplier - 1f) * Clamp(sprintIntensity, 0f, 1f);
        // Exponential recovery: fast when depleted, diminishing toward a finite cap.
        if (Reserve < settings.maximumReserve)
            Reserve = settings.maximumReserve - (settings.maximumReserve - Reserve)
                * (float)Math.Exp(-settings.recoveryRate * recoveryFactor * recoveryTime);
        DodgeBreathing = Math.Max(0f, DodgeBreathing - settings.dodgeBreathingDecay * dt);
        rhythmTime += dt;
        UpdateRhythm();
    }

    public void Hit(float speedLoss, float minSpeed, float injuryRecoveryPerSecond)
    {
        Hits++;
        SecondsSinceHit = 0f;
        Reserve -= settings.hitReserveCost * settings.immediateHitFraction;
        PendingInjuryLoss += settings.hitReserveCost * (1f - settings.immediateHitFraction);
        float loss = Math.Max(0f, speedLoss);
        Speed = Math.Max(Math.Max(0f, minSpeed), Speed - loss);
        // Area below base speed for one ordinary injury, v^2 / (2a).
        float singleInjuryTravel = loss * loss / (2f * Math.Max(.01f, injuryRecoveryPerSecond));
        injuryLossPerTravel = singleInjuryTravel > .0001f
            ? settings.hitReserveCost * (1f - settings.immediateHitFraction) / singleInjuryTravel : 0f;
        if (singleInjuryTravel <= .0001f || Speed >= BaseSpeed) PendingInjuryLoss = 0f;
    }

    public void Dodge(float speedGain, float maxSpeed, bool nearMiss)
    {
        float gain = Math.Max(0f, speedGain);
        Speed = Speed < BaseSpeed ? Math.Min(BaseSpeed, Speed + gain)
            : Math.Min(Math.Max(BaseSpeed, maxSpeed), Math.Max(Speed, BaseSpeed + gain));
        DodgeBreathing = Math.Min(settings.maximumDodgeBreathing,
            DodgeBreathing + (nearMiss ? settings.nearMissBreathing : settings.normalDodgeBreathing));
    }

    public ChaseState Copy()
    {
        var copy = (ChaseState)MemberwiseClone();
        copy.settings = settings.Copy();
        return copy;
    }

    private void UpdateRhythm()
    {
        double total = settings.restSeconds + settings.warningSeconds + settings.approachSeconds
            + settings.holdSeconds + settings.retreatSeconds;
        double t = rhythmTime % total;
        Pressure = Telegraph = 0f;
        if (t < settings.restSeconds) { Phase = PursuitPhase.Rest; return; }
        t -= settings.restSeconds;
        if (t < settings.warningSeconds)
        {
            Phase = PursuitPhase.Warning;
            Telegraph = Smooth((float)t / settings.warningSeconds);
            return;
        }
        t -= settings.warningSeconds;
        Telegraph = 1f;
        if (t < settings.approachSeconds)
        {
            Phase = PursuitPhase.Approach;
            Pressure = Smooth((float)t / settings.approachSeconds);
            return;
        }
        t -= settings.approachSeconds;
        if (t < settings.holdSeconds) { Phase = PursuitPhase.Hold; Pressure = 1f; return; }
        t -= settings.holdSeconds;
        Phase = PursuitPhase.Retreat;
        Pressure = Telegraph = 1f - Smooth((float)t / settings.retreatSeconds);
    }

    private static float Smooth(float t) { t = Clamp(t, 0f, 1f); return t * t * (3f - 2f * t); }
    private static float Clamp(float value, float min, float max) => Math.Max(min, Math.Min(max, value));
}
