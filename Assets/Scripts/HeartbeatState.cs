using System;

// Independent of UI and input so proximity alone cannot exhaust sprint capacity.
public sealed class HeartbeatState
{
    // Residual strain can briefly exceed the cap internally, without jumping the display.
    // Do not erase exertion to make room for it: that would reward reaching overload.
    public float Bpm => Math.Min(LimitBpm, BaseBpm + Exertion + ResidualBpm);
    public float BaseBpm { get; private set; }
    public float Exertion { get; private set; }
    public float ResidualBpm => ResidualAmount * ResidualSecondsRemaining / ResidualDuration;
    public float ResidualSecondsRemaining { get; private set; }
    public float RecoveryDelayRemaining { get; private set; }
    public bool Overheated { get; private set; }
    public readonly float RestBpm, ThreatBpm, LimitBpm, ResumeBpm;
    public readonly float RecoveryDelay, ResidualAmount, ResidualDuration;

    public HeartbeatState(float rest, float threat, float limit, float resume,
        float recoveryDelay = .6f, float residualAmount = 10f, float residualDuration = 6f)
    {
        RestBpm = Math.Max(30f, rest);
        LimitBpm = Math.Max(RestBpm + 30f, limit);
        ResumeBpm = Math.Max(RestBpm + 10f, Math.Min(LimitBpm - 5f, resume));
        ThreatBpm = Math.Max(RestBpm, Math.Min(ResumeBpm - 5f, threat));
        RecoveryDelay = Math.Max(0f, recoveryDelay);
        ResidualAmount = Math.Max(0f, Math.Min(LimitBpm - RestBpm, residualAmount));
        ResidualDuration = Math.Max(.1f, residualDuration);
        BaseBpm = RestBpm;
    }

    public HeartbeatState Copy() => (HeartbeatState)MemberwiseClone();

    public void Tick(float dt, float danger, bool exerting, float rise, float recovery, float response,
        bool recoveryAllowed = true, float minimumBaseBpm = 0f)
    {
        if (dt <= 0f) return;
        float target = Math.Max(RestBpm + (ThreatBpm - RestBpm) * Math.Max(0f, Math.Min(1f, danger)),
            Math.Max(RestBpm, Math.Min(ThreatBpm, minimumBaseBpm)));
        float delta = target - BaseBpm;
        BaseBpm += Math.Sign(delta) * Math.Min(Math.Abs(delta), Math.Max(.1f, response) * dt);
        // This debt fades with elapsed game time, never with the ordinary recovery rate.
        ResidualSecondsRemaining = Math.Max(0f, ResidualSecondsRemaining - dt);

        bool working = exerting && !Overheated;
        if (working || !recoveryAllowed)
        {
            RecoveryDelayRemaining = RecoveryDelay;
            if (working) Exertion += Math.Max(0f, rise) * dt;
        }
        else
        {
            // Only the portion of this frame after the waiting period may recover.
            float recoveryTime = Math.Max(0f, dt - RecoveryDelayRemaining);
            RecoveryDelayRemaining = Math.Max(0f, RecoveryDelayRemaining - dt);
            Exertion -= Math.Max(.1f, recovery) * recoveryTime;
        }
        Exertion = Math.Max(0f, Math.Min(LimitBpm - BaseBpm, Exertion));
        RefreshOverheat();
    }

    // A dash is one short pulse of effort, subject to the same overload and recovery rules.
    public void AddExertion(float bpm)
    {
        Exertion = Math.Max(0f, Math.Min(LimitBpm - BaseBpm, Exertion + Math.Max(0f, bpm)));
        RecoveryDelayRemaining = RecoveryDelay;
        RefreshOverheat();
    }

    private void RefreshOverheat()
    {
        if (Overheated)
        {
            if (Bpm <= ResumeBpm) Overheated = false;
        }
        else if (Bpm >= LimitBpm - .001f)
        {
            Overheated = true;
            RecoveryDelayRemaining = RecoveryDelay;
            // Refresh one penalty, never add another stack.
            ResidualSecondsRemaining = ResidualDuration;
        }
    }
}
