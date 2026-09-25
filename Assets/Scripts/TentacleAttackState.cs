using System;

// Deterministic scheduler. It owns time/distance, not input, rendering or collisions.
public sealed class TentacleAttackState
{
    public enum Stage { Chase, PreparingHunt, HuntPrelude, Hunt, LeavingHunt, Recovery }
    public enum Action { Idle, Waiting, Warning, Extend, Hold, Retract }
    public enum Kind { HighSweep, GroundStab, CeilingStab }
    [Serializable]
    public sealed class Settings
    {
        public bool enabled = true;
#if UNITY_5_3_OR_NEWER
        [UnityEngine.Header("Hunt Distance (odometer meters)")]
#endif
        public float firstHuntMeters = 120f;
        public float huntLengthMeters = 60f;
        public float distanceBetweenHunts = 120f;
        public float huntPreludeSeconds = 1.5f;
        public float recoverySeconds = 6f;
        public float obstacleResumeDelay = 1.5f;
        public float exitBreathingDistance = 4f;
#if UNITY_5_3_OR_NEWER
        [UnityEngine.Header("Attack Scheduling (seconds)")]
        [UnityEngine.Tooltip("Intervals begin after the previous tentacle has fully retracted.")]
#endif
        public float normalAttackInterval = 15f;
        public float huntAttackInterval = 1f;
        public float arenaClearDelay = .35f;
        public float afterInjuryDelay = 1.5f;
#if UNITY_5_3_OR_NEWER
        [UnityEngine.Header("Frequency Progression")]
        [UnityEngine.Tooltip("Cooldown reduction after each hunt. Uses the stronger of hunt and distance progression, never multiplies them together. Warnings and injury recovery stay unchanged.")]
#endif
        public float intervalMultiplierPerHunt = .8f;
        public float minimumNormalAttackInterval = 6f;
        public float minimumHuntAttackInterval = .35f;
#if UNITY_5_3_OR_NEWER
        [UnityEngine.Header("Readable Warning")]
        [UnityEngine.Tooltip("Warning duration stays the same in hunts and at higher running speed.")]
#endif
        public float warningSeconds = 1.1f;
        public float warningReachViewport = .055f;
        public float flattenOtherWaves = .94f;
#if UNITY_5_3_OR_NEWER
        [UnityEngine.Header("High Sweep (hold crouch)")]
#endif
        public float highCenterHeight = 1.85f;
        public float highHalfHeight = .3f;
        public float highExtendSeconds = .1f;
        public float highHoldSeconds = .6f;
        public float highRetractSeconds = .1f;
#if UNITY_5_3_OR_NEWER
        [UnityEngine.Header("Ground Stab (jump)")]
        [UnityEngine.Tooltip("Ground attack timing is capped by the current jump's clearance window.")]
#endif
        public float lowCenterHeight = .2f;
        public float lowHalfHeight = .2f;
        public float lowExtendSeconds = .08f;
        public float lowHoldSeconds = .03f;
        public float lowRetractSeconds = .08f;
#if UNITY_5_3_OR_NEWER
        [UnityEngine.Tooltip("Screen-space endpoint: 0.98 reaches almost to the right edge, independent of aspect ratio.")]
#endif
        public float reachViewport = .98f;
#if UNITY_5_3_OR_NEWER
        [UnityEngine.Header("Ceiling Stab - Hunt Only (horizontal dodge)")]
#endif
#if UNITY_5_3_OR_NEWER
        [UnityEngine.Header("Ceiling Volleys - Count and Warning Progression")]
#endif
        public OverheadPressure.Settings ceilingPressure = new OverheadPressure.Settings();
        public bool ceilingEnabled = true;
        public int ceilingEveryAttacks = 3;
        public float ceilingWarningSeconds = 1.8f;
        public float ceilingHalfWidth = .35f;
        public float ceilingExtendSeconds = .12f;
        public float ceilingHoldSeconds = .12f;
        public float ceilingRetractSeconds = .12f;
        public float ceilingTipViewport = .05f;
        public float ceilingWarningDepth = .09f;
        public float ceilingReactionSeconds = .3f;
        public float ceilingEscapePadding = .18f;
        public float ceilingSafetyReserve = 4f;
#if UNITY_5_3_OR_NEWER
        [UnityEngine.Header("Ceiling Warning - Black Liquid")]
#endif
        public float liquidDropsPerSecond = 8f;
        public float liquidDropSize = 1f;
#if UNITY_5_3_OR_NEWER
        [UnityEngine.Header("Collision Forgiveness")]
        [UnityEngine.Tooltip("Vertical player hurtbox inset in world units. Avoids perspective-projected foot corners causing a hit after the feet have cleared a low strike.")]
#endif
        public float playerHitboxInset = .08f;

        public void Validate()
        {
            firstHuntMeters = Math.Max(1f, firstHuntMeters);
            huntLengthMeters = Math.Max(1f, huntLengthMeters);
            distanceBetweenHunts = Math.Max(1f, distanceBetweenHunts);
            huntPreludeSeconds = Math.Max(.5f, huntPreludeSeconds);
            recoverySeconds = Math.Max(1f, recoverySeconds);
            obstacleResumeDelay = Math.Max(.35f, Math.Min(recoverySeconds, obstacleResumeDelay));
            exitBreathingDistance = Math.Max(0f, exitBreathingDistance);
            normalAttackInterval = Math.Max(3f, normalAttackInterval);
            huntAttackInterval = Math.Max(1f, huntAttackInterval);
            intervalMultiplierPerHunt = Clamp(intervalMultiplierPerHunt,.1f,1f);
            minimumNormalAttackInterval = Clamp(minimumNormalAttackInterval,3f,normalAttackInterval);
            minimumHuntAttackInterval = Clamp(minimumHuntAttackInterval,.15f,huntAttackInterval);
            arenaClearDelay = Math.Max(.15f, arenaClearDelay);
            afterInjuryDelay = Math.Max(.7f, afterInjuryDelay);
            warningSeconds = Math.Max(.7f, warningSeconds);
            warningReachViewport = Clamp(warningReachViewport,.02f,.12f);
            flattenOtherWaves = Clamp(flattenOtherWaves,0f,1f);
            highHalfHeight = Math.Max(.1f,highHalfHeight);
            highCenterHeight = Math.Max(highHalfHeight,highCenterHeight);
            lowHalfHeight = Math.Max(.05f,lowHalfHeight);
            lowCenterHeight = Math.Max(lowHalfHeight,lowCenterHeight);
            highExtendSeconds = Math.Max(.1f,highExtendSeconds);
            highHoldSeconds = Math.Max(.1f,highHoldSeconds);
            highRetractSeconds = Math.Max(.1f,highRetractSeconds);
            lowExtendSeconds = Math.Max(.05f,lowExtendSeconds);
            lowHoldSeconds = Math.Max(0f,lowHoldSeconds);
            lowRetractSeconds = Math.Max(.05f,lowRetractSeconds);
            reachViewport = Clamp(reachViewport,.6f,1f);
            if(ceilingPressure==null)ceilingPressure=new OverheadPressure.Settings();ceilingPressure.Validate();
            ceilingEveryAttacks = Math.Max(2,ceilingEveryAttacks);
            ceilingWarningSeconds = Math.Max(1f,ceilingWarningSeconds);
            ceilingHalfWidth = Clamp(ceilingHalfWidth,.15f,.6f);
            ceilingExtendSeconds = Math.Max(.08f,ceilingExtendSeconds);
            ceilingHoldSeconds = Math.Max(.03f,ceilingHoldSeconds);
            ceilingRetractSeconds = Math.Max(.08f,ceilingRetractSeconds);
            ceilingTipViewport = Clamp(ceilingTipViewport,0,.15f);
            ceilingWarningDepth = Clamp(ceilingWarningDepth,.05f,.15f);
            ceilingReactionSeconds = Clamp(ceilingReactionSeconds,0,ceilingWarningSeconds-.4f);
            ceilingEscapePadding = Math.Max(0,ceilingEscapePadding);
            ceilingSafetyReserve = Math.Max(0,ceilingSafetyReserve);
            liquidDropsPerSecond = Clamp(liquidDropsPerSecond,2f,30f);
            liquidDropSize = Clamp(liquidDropSize,.3f,3f);
            playerHitboxInset = Clamp(playerHitboxInset,0f,.15f);
        }
    }

    public Stage CurrentStage { get; private set; }
    public Action CurrentAction { get; private set; }
    public Kind AttackKind { get; private set; }
    public int AttackId { get; private set; }
    public int CompletedAttacks { get; private set; }
    public int CompletedHunts { get; private set; }
    public float NormalAttackInterval => ProgressedInterval(settings.normalAttackInterval,settings.minimumNormalAttackInterval);
    public float HuntAttackInterval => ProgressedInterval(settings.huntAttackInterval,settings.minimumHuntAttackInterval);
    public bool HitThisAttack { get; private set; }
    private float ceilingWarning, ceilingTail;
    public float AttackElapsed { get; private set; }
    public float WarningDuration => AttackKind == Kind.CeilingStab ? (ceilingWarning>0?ceilingWarning:settings.ceilingWarningSeconds) : settings.warningSeconds;
    public void ConfigureCeilingVolley(float warning,float tail)
    {
        if(AttackKind!=Kind.CeilingStab||CurrentAction!=Action.Warning)return;
        ceilingWarning=Math.Max(1f,warning);ceilingTail=Math.Max(0,tail);
    }
    public float WarningProgress => CurrentAction == Action.Warning ? Clamp(actionTime/WarningDuration,0,1) : 0;
    public bool WantsCeilingAttack => settings.ceilingEnabled && CurrentStage == Stage.Hunt
        && (huntAttackIndex+1) % settings.ceilingEveryAttacks == 0;
    public float Extension { get; private set; }
    public float RecoveryElapsed { get; private set; }
    public double HuntStartMeters { get; private set; }
    public double NextHuntMeters { get; private set; }
    public bool IsAttacking => CurrentAction == Action.Extend || CurrentAction == Action.Hold || CurrentAction == Action.Retract;
    public bool BlocksSpawns => settings.enabled && (CurrentStage == Stage.PreparingHunt || CurrentStage == Stage.HuntPrelude
        || CurrentStage == Stage.Hunt || CurrentStage == Stage.LeavingHunt
        || CurrentAction != Action.Idle || (CurrentStage == Stage.Recovery && RecoveryElapsed < settings.obstacleResumeDelay));
    private readonly Settings settings;
    private int huntAttackIndex;
    private float distanceMultiplier = 1f;
    private float cooldown, clearTime, actionTime, stageTime, injuryWait, lowScale = 1f, retractFrom = 1f;

    public TentacleAttackState(Settings settings)
    {
        this.settings = settings ?? new Settings(); this.settings.Validate();
        NextHuntMeters = this.settings.firstHuntMeters;
        cooldown = NormalAttackInterval;
    }

    public void Tick(float seconds, double meters, bool arenaClear, bool grounded, float jumpClearanceSeconds,
        bool canCeilingAttack = false, float intervalMultiplier = 1f)
    {
        if(!settings.enabled) { Cancel(); return; }
        if(seconds<=0)return;
        float oldInterval=CurrentStage==Stage.Hunt?HuntAttackInterval:NormalAttackInterval;
        distanceMultiplier=Clamp(intervalMultiplier,.1f,1f);
        float nextInterval=CurrentStage==Stage.Hunt?HuntAttackInterval:NormalAttackInterval;
        if(nextInterval<oldInterval)cooldown*=nextInterval/oldInterval;
        float remaining = Math.Max(0,seconds);
        // Small steps keep warning/attack timing stable even on a slow frame.
        while(remaining > .000001f)
        {
            float dt = Math.Min(remaining,1f/240f); remaining -= dt;
            Step(dt,meters,arenaClear,grounded,jumpClearanceSeconds,canCeilingAttack);
        }
    }

    private void Step(float dt, double meters, bool clear, bool grounded, float jumpWindow, bool canCeiling)
    {
        injuryWait = Math.Max(0,injuryWait-dt);
        cooldown = Math.Max(0,cooldown-dt);
        if(CurrentStage == Stage.Chase && meters >= NextHuntMeters)
        {
            CurrentStage = Stage.PreparingHunt;
            if(CurrentAction == Action.Waiting) CurrentAction = Action.Idle;
        }
        if(CurrentStage == Stage.Hunt && meters-HuntStartMeters >= settings.huntLengthMeters)
        {
            CurrentStage = Stage.LeavingHunt;
            if(CurrentAction == Action.Waiting) CurrentAction = Action.Idle;
        }
        bool ready = clear && grounded && injuryWait <= 0;
        clearTime = ready ? clearTime+dt : 0;
        if(CurrentStage == Stage.PreparingHunt && CurrentAction == Action.Idle && clearTime >= settings.arenaClearDelay)
        { CurrentStage = Stage.HuntPrelude; stageTime = 0; }
        else if(CurrentStage == Stage.HuntPrelude)
        {
            stageTime += dt;
            if(stageTime >= settings.huntPreludeSeconds && ready)
            { CurrentStage = Stage.Hunt; HuntStartMeters = meters; huntAttackIndex=0; cooldown = 0; }
        }
        if(CurrentStage == Stage.LeavingHunt && CurrentAction == Action.Idle)
        {
            CompletedHunts++;
            CurrentStage = Stage.Recovery; RecoveryElapsed = 0;
            NextHuntMeters = meters + settings.distanceBetweenHunts;
            cooldown = NormalAttackInterval;
        }
        if(CurrentStage == Stage.Recovery)
        {
            RecoveryElapsed += dt;
            if(RecoveryElapsed >= settings.recoverySeconds) CurrentStage = Stage.Chase;
        }
        if(CurrentAction == Action.Idle && cooldown <= 0
            && (CurrentStage == Stage.Chase || CurrentStage == Stage.Hunt))
        { CurrentAction = Action.Waiting; clearTime = 0; }
        if(CurrentAction == Action.Waiting && clearTime >= settings.arenaClearDelay)
        {
            AttackKind = AttackId % 2 == 0 ? Kind.HighSweep : Kind.GroundStab;
            if(WantsCeilingAttack && canCeiling) AttackKind = Kind.CeilingStab;
            if(CurrentStage==Stage.Hunt)huntAttackIndex++;
            // If the current jump cannot clear a low strike, use the crouch attack.
            if(AttackKind == Kind.GroundStab && jumpWindow < .12f) AttackKind = Kind.HighSweep;
            float lowTotal = settings.lowExtendSeconds + settings.lowHoldSeconds + settings.lowRetractSeconds;
            lowScale = Math.Min(1f, Math.Max(.01f,jumpWindow*.8f) / lowTotal);
            AttackElapsed=0;ceilingWarning=0;ceilingTail=0;
            AttackId++; HitThisAttack = false; Extension = 0;
            CurrentAction = Action.Warning; actionTime = 0;
        }
        if(CurrentAction == Action.Idle || CurrentAction == Action.Waiting) return;
        actionTime += dt;AttackElapsed += dt;
        bool high = AttackKind == Kind.HighSweep;
        bool ceiling = AttackKind == Kind.CeilingStab;
        float extend = ceiling ? settings.ceilingExtendSeconds : high ? settings.highExtendSeconds : settings.lowExtendSeconds*lowScale;
        float hold = ceiling ? settings.ceilingHoldSeconds+ceilingTail : high ? settings.highHoldSeconds : settings.lowHoldSeconds*lowScale;
        float retract = ceiling ? settings.ceilingRetractSeconds : high ? settings.highRetractSeconds : settings.lowRetractSeconds*lowScale;
        if(CurrentAction == Action.Warning && actionTime >= WarningDuration)
        { CurrentAction = Action.Extend; actionTime = 0; }
        else if(CurrentAction == Action.Extend)
        {
            Extension = Smooth(actionTime/extend);
            if(actionTime >= extend) { CurrentAction = Action.Hold; actionTime = 0; Extension = 1; }
        }
        else if(CurrentAction == Action.Hold && ((!ceiling && HitThisAttack) || actionTime >= hold))
        { CurrentAction = Action.Retract; actionTime = 0; retractFrom = 1; }
        else if(CurrentAction == Action.Retract)
        {
            Extension = retractFrom*(1-Smooth(actionTime/retract));
            if(actionTime >= retract)
            {
                CurrentAction = Action.Idle; Extension = 0; CompletedAttacks++;
                cooldown = CurrentStage == Stage.Hunt ? HuntAttackInterval : NormalAttackInterval;
            }
        }
    }

    public void NotifyHit()
    {
        injuryWait = settings.afterInjuryDelay;
        if(IsAttacking || CurrentAction == Action.Warning)
        {
            HitThisAttack = true;
            // Every ceiling lane must finish its committed warning and strike.
            // Contact still disqualifies the wave's dodge reward.
            if(AttackKind == Kind.CeilingStab) return;
            // Finish a committed lunge even on contact, then retract without a hold.
            // Repeated notifications must not restart the retraction animation.
            if(CurrentAction == Action.Warning || CurrentAction == Action.Hold)
            {
                retractFrom = Extension;
                CurrentAction = Action.Retract; actionTime = 0;
            }
        }
    }
    public void Cancel() { CurrentAction = Action.Idle; CurrentStage = Stage.Chase; Extension = 0; }
    private float ProgressedInterval(float initial,float minimum) =>
        Math.Max(minimum,initial*Math.Min(distanceMultiplier,(float)Math.Pow(settings.intervalMultiplierPerHunt,CompletedHunts)));
    private static float Smooth(float t) { t=Clamp(t,0,1); return t*t*(3-2*t); }
    private static float Clamp(float v,float min,float max) => Math.Max(min,Math.Min(max,v));
}
