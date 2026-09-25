using System;

// Real-time QTE clock, independent of Unity's bullet-time scale and chase reserve.
public sealed class HuntAmbushState
{
    public enum Phase { Idle, Lunge, Focus, Response, Evade, Bind, Struggle, Release, Recovery, Consume, Complete, Dead }
    [Serializable]
    public sealed class Settings
    {
        public bool enabled=true;
#if UNITY_5_3_OR_NEWER
        [UnityEngine.Tooltip("At most one event per hunt; wait for committed hazards to clear first.")]
        [UnityEngine.Range(.2f,.9f)]
#endif
        public float huntProgress=.65f;
#if UNITY_5_3_OR_NEWER
        [UnityEngine.Range(0f,1f)]
#endif
        public float chancePerHunt=1f;
        public float lungeSeconds=.28f;
        public float readSeconds=.18f;
        public float responseSeconds=.9f;
        public float evadeSeconds=.22f;
        public float bulletTimeScale=.12f;
        public float bindSeconds=.35f;
        public float struggleSeconds=3f;
        public int pressesToEscape=6;
        public float releaseSeconds=.45f;
        public float recoverySeconds=1.25f;
        public float consumeSeconds=.4f;
#if UNITY_5_3_OR_NEWER
        [UnityEngine.Header("Grab Shape and Key Feedback")]
#endif
        public float grabRootWidth=.036f;
        public float reactionRingRadius=.036f;
        public float strugglePulseHz=2.4f;
        public float strugglePulseAmount=.2f;
        public void Validate()
        {
            huntProgress=Clamp(huntProgress,.2f,.9f);chancePerHunt=Clamp(chancePerHunt,0,1);
            lungeSeconds=Math.Max(.15f,lungeSeconds);readSeconds=Math.Max(.1f,readSeconds);
            responseSeconds=Math.Max(.4f,responseSeconds);evadeSeconds=Math.Max(.15f,evadeSeconds);
            bulletTimeScale=Clamp(bulletTimeScale,.03f,.5f);bindSeconds=Math.Max(.2f,bindSeconds);
            struggleSeconds=Math.Max(1f,struggleSeconds);pressesToEscape=Math.Max(2,Math.Min(20,pressesToEscape));
            releaseSeconds=Math.Max(.25f,releaseSeconds);recoverySeconds=Math.Max(.7f,recoverySeconds);
            consumeSeconds=Math.Max(.2f,consumeSeconds);
            grabRootWidth=Clamp(grabRootWidth,.012f,.08f);reactionRingRadius=Clamp(reactionRingRadius,.025f,.06f);
            strugglePulseHz=Clamp(strugglePulseHz,.5f,5f);strugglePulseAmount=Clamp(strugglePulseAmount,.05f,.4f);
        }
        public Settings Copy() => (Settings)MemberwiseClone();
    }
    public Phase Current { get; private set; }
    public float PhaseTime { get; private set; }
    public int Step { get; private set; }
    public int EscapePresses { get; private set; }
    public bool WasBound { get; private set; }
    public float PressPulse { get; private set; }
    public bool Engaged => Current!=Phase.Idle;
    public bool Finished => Current==Phase.Complete || Current==Phase.Dead;
    public int RequiredKey => sequence[Step];
    public float Progress => Clamp(PhaseTime/Duration,0,1);
    public float EscapeProgress => EscapePresses/(float)settings.pressesToEscape;
    public float TimeScale => Current==Phase.Focus || Current==Phase.Response ? settings.bulletTimeScale : 1f;
    public Settings Configuration => settings;
    private readonly Settings settings;
    private readonly int[] sequence=new int[3];
    public HuntAmbushState(Settings configuration)
    {
        settings=(configuration ?? new Settings()).Copy();settings.Validate();
    }
    public void Begin(int first,int second,int third)
    {
        sequence[0]=first;sequence[1]=second;sequence[2]=third;
        Step=EscapePresses=0;WasBound=false;PressPulse=0;Enter(Phase.Lunge);
    }
    // key: -1=no edge, -2=wrong/simultaneous keys; a held key never calls this twice.
    public void Tick(float realSeconds,int key=-1,bool spaceDown=false)
    {
        if(!Engaged || Finished || realSeconds<=0)return;
        PressPulse=Math.Max(0,PressPulse-realSeconds*6f);
        if(Current==Phase.Focus && key==RequiredKey) { PressPulse=1;Enter(Phase.Evade);return; }
        if(Current==Phase.Response && key!=-1)
        {
            if(key==RequiredKey){PressPulse=1;Enter(Phase.Evade);}
            else {WasBound=true;Enter(Phase.Bind);}
            return;
        }
        if(Current==Phase.Struggle && spaceDown)
        {
            EscapePresses++;PressPulse=1;
            if(EscapePresses>=settings.pressesToEscape){Enter(Phase.Release);return;}
        }
        // Do not carry an input edge across phase boundaries. Large frames still
        // advance the deadline, but a new prompt always gets its own read window.
        PhaseTime+=realSeconds;
        if(PhaseTime<Duration)return;
        switch(Current)
        {
            case Phase.Lunge:Enter(Phase.Focus);break;
            case Phase.Focus:Enter(Phase.Response);break;
            case Phase.Response:WasBound=true;Enter(Phase.Bind);break;
            case Phase.Evade:if(Step<2){Step++;Enter(Phase.Lunge);}else Enter(Phase.Release);break;
            case Phase.Bind:Enter(Phase.Struggle);break;
            case Phase.Struggle:Enter(Phase.Consume);break;
            case Phase.Release:Enter(Phase.Recovery);break;
            case Phase.Recovery:Enter(Phase.Complete);break;
            case Phase.Consume:Enter(Phase.Dead);break;
        }
    }
    public void Cancel(){Enter(Phase.Idle);PressPulse=0;}
    private void Enter(Phase phase){Current=phase;PhaseTime=0;}
    private float Duration
    {
        get
        {
            switch(Current)
            {
                case Phase.Lunge:return settings.lungeSeconds;
                case Phase.Focus:return settings.readSeconds;
                case Phase.Response:return settings.responseSeconds;
                case Phase.Evade:return settings.evadeSeconds;
                case Phase.Bind:return settings.bindSeconds;
                case Phase.Struggle:return settings.struggleSeconds;
                case Phase.Release:return settings.releaseSeconds;
                case Phase.Recovery:return settings.recoverySeconds;
                case Phase.Consume:return settings.consumeSeconds;
                default:return 1f;
            }
        }
    }
    private static float Clamp(float v,float low,float high)=>Math.Max(low,Math.Min(high,v));
}
