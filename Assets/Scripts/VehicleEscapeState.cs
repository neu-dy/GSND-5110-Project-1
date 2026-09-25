using System;
using UnityEngine;

// One shared deadline for lockpicking, boarding and ignition. Key prompts have no timers.
public sealed class VehicleEscapeState
{
    [Serializable]
    public sealed class Settings
    {
        public bool enabled = true;
        [Min(1f)] public float finishMeters = 1200f;
        [Header("Shared Deadline")]
        [Min(3f)] public float deadlineSeconds = 10f;
        [SerializeField, HideInInspector] private int timingRevision;
        [Range(.08f,.4f)] public float minimumCurtainGap = .22f;
        [Header("Lock and Ignition")]
        public KeyCode[] lockKeys = { KeyCode.Q, KeyCode.E, KeyCode.R, KeyCode.F };
        [Range(2,16)] public int lockPresses = 6;
        [Range(2,40)] public int ignitionPresses = 12;
        [Header("Cinematic (seconds)")]
        [Min(.2f)] public float arrivalSeconds = 1.6f;
        [Min(.1f)] public float boardingSeconds = .65f;
        [Min(.5f)] public float departureSeconds = 2.2f;
        [Min(.2f)] public float safeMessageSeconds = 1.8f;
        [Min(.2f)] public float surgeSeconds = 1.2f;
        [Min(0f)] public float blackHoldSeconds = .6f;
        [Min(1f)] public float returnCountdownSeconds = 3f;
        public void Validate()
        {
            // Migrate the previous prototype default, preserving custom deadlines.
            if(timingRevision<1){if(Mathf.Approximately(deadlineSeconds,18f))deadlineSeconds=10f;timingRevision=1;}
            finishMeters=Mathf.Max(1,finishMeters);deadlineSeconds=Mathf.Max(3,deadlineSeconds);
            minimumCurtainGap=Mathf.Clamp(minimumCurtainGap,.08f,.4f);
            lockPresses=Mathf.Clamp(lockPresses,2,16);ignitionPresses=Mathf.Clamp(ignitionPresses,2,40);
            arrivalSeconds=Mathf.Max(.2f,arrivalSeconds);boardingSeconds=Mathf.Max(.1f,boardingSeconds);
            departureSeconds=Mathf.Max(.5f,departureSeconds);safeMessageSeconds=Mathf.Max(.2f,safeMessageSeconds);
            surgeSeconds=Mathf.Max(.2f,surgeSeconds);blackHoldSeconds=Mathf.Max(0,blackHoldSeconds);
            returnCountdownSeconds=Mathf.Max(1,returnCountdownSeconds);
            bool valid=lockKeys!=null&&lockKeys.Length>=2&&lockKeys.Length<=12;
            if(valid)for(int i=0;i<lockKeys.Length;i++)
                if(lockKeys[i]<KeyCode.A||lockKeys[i]>KeyCode.Z||Array.IndexOf(lockKeys,lockKeys[i])!=i)valid=false;
            if(!valid)lockKeys=new[]{KeyCode.Q,KeyCode.E,KeyCode.R,KeyCode.F};
        }
        public Settings Copy()
        {
            var copy=(Settings)MemberwiseClone();
            copy.lockKeys=lockKeys==null?null:(KeyCode[])lockKeys.Clone();copy.Validate();return copy;
        }
    }
    public enum Phase { Arrival, Lockpick, Boarding, Ignition, Departure, SafeMessage, Surge, BlackHold, Countdown, Complete, Failed }
    public readonly Settings Configuration;
    private readonly int[] sequence;
    public Phase Current { get; private set; } = Phase.Arrival;
    public float PhaseSeconds { get; private set; }
    public float DeadlineElapsed { get; private set; }
    public float DeadlineProgress => Mathf.Clamp01(DeadlineElapsed/Configuration.deadlineSeconds);
    public int LockProgress { get; private set; }
    public int EngineProgress { get; private set; }
    public int Mistakes { get; private set; }
    public int RequiredKey => sequence[Math.Min(LockProgress,sequence.Length-1)];
    public bool UnderThreat => Current==Phase.Lockpick||Current==Phase.Boarding||Current==Phase.Ignition;
    public bool Finished => Current==Phase.Complete||Current==Phase.Failed;
    public float Progress => Mathf.Clamp01(PhaseSeconds/Mathf.Max(.001f,Duration(Current)));
    public VehicleEscapeState(Settings settings,int[] keys)
    {
        Configuration=(settings??new Settings()).Copy();
        if(keys==null||keys.Length!=Configuration.lockPresses)throw new ArgumentException("Invalid lock sequence");
        sequence=(int[])keys.Clone();
        for(int i=0;i<sequence.Length;i++)
            if(sequence[i]<0||sequence[i]>=Configuration.lockKeys.Length||(i>0&&sequence[i]==sequence[i-1]))
                throw new ArgumentException("Lock keys must alternate");
    }
    public void Tick(float seconds,int key=-1,bool space=false)
    {
        if(seconds<=0||Finished)return;
        Phase inputPhase=Current;
        float remaining=seconds;
        while(remaining>0&&!Finished)
        {
            float step=Mathf.Min(remaining,Mathf.Max(0,Duration(Current)-PhaseSeconds));
            if(UnderThreat)step=Mathf.Min(step,Mathf.Max(0,Configuration.deadlineSeconds-DeadlineElapsed));
            PhaseSeconds+=step;remaining-=step;
            if(UnderThreat)DeadlineElapsed+=step;
            // Reaching the shared deadline wins ties against the last input.
            if(UnderThreat&&DeadlineElapsed>=Configuration.deadlineSeconds){Enter(Phase.Failed);break;}
            if(PhaseSeconds>=Duration(Current)){Enter((Phase)((int)Current+1));continue;}
            break;
        }
        // Never reuse a key edge across a cinematic / interaction boundary.
        if(Current!=inputPhase||Finished)return;
        if(Current==Phase.Lockpick&&key!=-1)
        {
            if(key==RequiredKey){LockProgress++;if(LockProgress==sequence.Length)Enter(Phase.Boarding);}
            else {LockProgress=0;Mistakes++;}
        }
        else if(Current==Phase.Ignition&&space)
        {
            EngineProgress++;if(EngineProgress>=Configuration.ignitionPresses)Enter(Phase.Departure);
        }
    }
    private void Enter(Phase phase){Current=phase;PhaseSeconds=0;}
    private float Duration(Phase phase)
    {
        switch(phase)
        {
            case Phase.Arrival:return Configuration.arrivalSeconds;
            case Phase.Boarding:return Configuration.boardingSeconds;
            case Phase.Departure:return Configuration.departureSeconds;
            case Phase.SafeMessage:return Configuration.safeMessageSeconds;
            case Phase.Surge:return Configuration.surgeSeconds;
            case Phase.BlackHold:return Configuration.blackHoldSeconds;
            case Phase.Countdown:return Configuration.returnCountdownSeconds;
            default:return float.PositiveInfinity;
        }
    }
}
