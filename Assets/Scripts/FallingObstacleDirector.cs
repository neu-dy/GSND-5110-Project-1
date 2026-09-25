using System;
using UnityEngine;
using UnityEngine.UI;

// One committed drop at a time, sharing the lane reservation with normal obstacles
// and tentacles. The cube uses swept collision so fast falls cannot tunnel.
public sealed class FallingObstacleDirector
{
    [Serializable]
    public sealed class Settings
    {
        public bool enabled = true;
        public OverheadPressure.Settings pressure = new OverheadPressure.Settings();
        [Min(0f)] public float firstDropMeters = 35f;
        [Min(3f)] public float intervalSeconds = 12f;
        [Min(0f)] public float intervalVariance = 2f;
        [Min(1f)] public float minimumIntervalSeconds = 4f;
        [Min(.8f)] public float warningSeconds = 1.8f;
        [Min(.25f)] public float fallSeconds = .6f;
        [Range(.3f,1.6f)] public float width = .7f;
        [Range(.5f,3f)] public float height = 1.2f;
        [Min(.1f)] public float recoverySeconds = .8f;
        [Tooltip("Reaction time reserved before forecasting a sprint or release escape.")]
        [Min(0f)] public float reactionSeconds = .3f;
        [Min(0f)] public float escapePadding = .18f;
        [Tooltip("Skip drops when the predicted home-lane pursuit reserve is too small.")]
        [Min(0f)] public float curtainSafetyReserve = 4f;
        [Header("Debris and Ground Shadow Warning")]
        [Range(2f,30f)] public float debrisPerSecond = 10f;
        [Range(.3f,3f)] public float debrisSize = 1f;
        [Range(.5f,2f)] public float shadowScale = 1.15f;
        [Range(.1f,1f)] public float shadowOpacity = .6f;
        public void Validate()
        {
            if(pressure==null)pressure=new OverheadPressure.Settings();pressure.Validate();
            firstDropMeters=Mathf.Max(0,firstDropMeters);intervalSeconds=Mathf.Max(3,intervalSeconds);
            intervalVariance=Mathf.Clamp(intervalVariance,0,intervalSeconds-1);
            minimumIntervalSeconds=Mathf.Clamp(minimumIntervalSeconds,1,intervalSeconds);
            warningSeconds=Mathf.Max(.8f,warningSeconds);fallSeconds=Mathf.Max(.25f,fallSeconds);
            width=Mathf.Clamp(width,.3f,1.6f);height=Mathf.Clamp(height,.5f,3f);
            recoverySeconds=Mathf.Max(.1f,recoverySeconds);
            reactionSeconds=Mathf.Clamp(reactionSeconds,0,warningSeconds-.4f);
            escapePadding=Mathf.Max(0,escapePadding);curtainSafetyReserve=Mathf.Max(0,curtainSafetyReserve);
            debrisPerSecond=Mathf.Clamp(debrisPerSecond,2f,30f);debrisSize=Mathf.Clamp(debrisSize,.3f,3f);
            shadowScale=Mathf.Clamp(shadowScale,.5f,2f);shadowOpacity=Mathf.Clamp(shadowOpacity,.1f,1f);
        }
    }
    public enum Phase { Idle, Warning, Falling, Recovery }
    public Phase CurrentPhase { get; private set; }
    public bool Active => CurrentPhase != Phase.Idle;
    public bool BlocksSpawns => Active || waiting;
    public float LockedX { get; private set; }
    public int ActiveCount => plan!=null ? plan.Centers.Length : 0;
    public OverheadPressure.Plan CurrentPlan => plan;
    private sealed class Drop
    {
        public GameObject cube;
        public Renderer body;
        public DropWarningGraphic warning;
        public Vector3 position;
        public Bounds previousPlayer;
        public bool finished;
    }
    private readonly ObstacleSpawner owner;
    private readonly GameModeController modes;
    private readonly Collider player;
    private readonly PlayerSprint sprint;
    private readonly PlayerVerticalMovement vertical;
    private readonly Settings settings;
    private readonly GameObject ui;
    private readonly Drop[] drops=new Drop[3];
    private Material material;
    private OverheadPressure.Plan plan;
    private float cooldown,elapsed,groundY,startY,endY,lockedFall,closest;
    private Vector3 size;
    private bool waiting,hitThisWave;
    public FallingObstacleDirector(ObstacleSpawner owner,GameModeController modes,Collider player,Settings settings)
    {
        this.owner=owner;this.modes=modes;this.player=player;this.settings=settings;
        settings.Validate();cooldown=NextInterval(false);
        if(player!=null){sprint=player.GetComponent<PlayerSprint>();vertical=player.GetComponent<PlayerVerticalMovement>();}
        ui=new GameObject("Falling Obstacle Warning",typeof(RectTransform),typeof(Canvas),typeof(CanvasScaler));
        var canvas=ui.GetComponent<Canvas>();canvas.renderMode=RenderMode.ScreenSpaceOverlay;canvas.sortingOrder=8;
        var scaler=ui.GetComponent<CanvasScaler>();scaler.uiScaleMode=CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution=new Vector2(1280,720);scaler.matchWidthOrHeight=.5f;
        for(int i=0;i<drops.Length;i++)
        {
            var go=new GameObject("Falling Debris and Shadow "+(i+1),typeof(RectTransform),typeof(CanvasRenderer),typeof(DropWarningGraphic));
            go.transform.SetParent(ui.transform,false);
            var rect=go.GetComponent<RectTransform>();rect.anchorMin=Vector2.zero;rect.anchorMax=Vector2.one;rect.offsetMin=rect.offsetMax=Vector2.zero;
            drops[i]=new Drop{warning=go.GetComponent<DropWarningGraphic>()};drops[i].warning.raycastTarget=false;
        }
    }
    public void Tick(float dt)
    {
        if(!settings.enabled||modes==null||!modes.IsPlaying){Cancel();return;}
        if(dt<=0)return;Physics.SyncTransforms();
        if(Active){Advance(dt);return;}
        cooldown-=dt;waiting=false;
        if(cooldown>0||modes.RunDistanceMeters<settings.firstDropMeters||modes.TentaclesBlockSpawns)return;
        waiting=true;
        if(player==null||sprint==null||vertical==null||Camera.main==null){waiting=false;return;}
        if(owner.HasPendingObstacle(sprint.HomePosition.x-player.bounds.extents.x))return;
        plan=OverheadPressure.Choose(settings.pressure,modes.RunDistanceMeters,modes.RunElapsedSeconds,
            player.bounds.center.x,sprint.HorizontalHomeX,sprint.HorizontalLimitX,settings.width,settings.warningSeconds,settings.fallSeconds,
            UnityEngine.Random.value<settings.pressure.simultaneousChance,UnityEngine.Random.value<.5f,
            (lanes,warning,total)=>sprint.CanAvoidOverhead(lanes,settings.width*.5f,warning,total,settings.reactionSeconds,settings.escapePadding)
                && modes.CanAffordCrouch(total,0,settings.curtainSafetyReserve));
        if(plan==null){waiting=false;cooldown=2f;return;}
        Begin();
    }
    private void Begin()
    {
        waiting=false;hitThisWave=false;elapsed=0;CurrentPhase=Phase.Warning;
        LockedX=plan.Centers[0];groundY=vertical.GroundY;lockedFall=settings.fallSeconds;
        size=new Vector3(settings.width,settings.height,Mathf.Max(.7f,player.bounds.size.z));
        float depth=Camera.main.WorldToViewportPoint(player.bounds.center).z;
        startY=Mathf.Max(player.bounds.max.y+3f,Camera.main.ViewportToWorldPoint(new Vector3(.5f,1.05f,depth)).y+size.y);
        endY=groundY+size.y*.5f;closest=float.PositiveInfinity;
        for(int i=0;i<plan.Centers.Length;i++)
        {
            Drop d=drops[i];d.finished=false;d.previousPlayer=player.bounds;
            d.position=new Vector3(plan.Centers[i],startY,player.bounds.center.z);
            d.cube=GameObject.CreatePrimitive(PrimitiveType.Cube);d.cube.name="Falling Obstacle "+(i+1);
            d.cube.GetComponent<Collider>().enabled=false;d.cube.transform.position=d.position;d.cube.transform.localScale=size;
            d.body=d.cube.GetComponent<Renderer>();d.body.enabled=false;
            if(material==null)
            {
                var source=player.GetComponentInChildren<Renderer>();material=new Material(source!=null?source.sharedMaterial:d.body.sharedMaterial);
                Color pink=new Color(1f,.12f,.62f,1);
                if(material.HasProperty("_BaseColor"))material.SetColor("_BaseColor",pink);
                if(material.HasProperty("_Color"))material.SetColor("_Color",pink);
            }
            d.body.sharedMaterial=material;
            d.warning.DebrisColor=material.HasProperty("_BaseColor")?material.GetColor("_BaseColor"):material.color;
            RefreshWarning(d,i,false);
        }
    }
    private void Advance(float dt)
    {
        elapsed+=dt;
        if(CurrentPhase==Phase.Recovery)
        {
            if(elapsed>=settings.recoverySeconds){CurrentPhase=Phase.Idle;plan=null;cooldown=NextInterval(true);}
            return;
        }
        bool done=true;
        for(int i=0;i<plan.Centers.Length;i++)
        {
            Drop d=drops[i];if(d.finished)continue;
            float warning=plan.Warning+i*plan.Stagger;
            if(elapsed<warning){d.previousPlayer=player.bounds;RefreshWarning(d,i,false);done=false;continue;}
            CurrentPhase=Phase.Falling;d.body.enabled=true;
            Vector3 previous=d.position;float t=Mathf.Clamp01((elapsed-warning)/lockedFall);
            d.position.y=Mathf.Lerp(startY,endY,t*t);d.cube.transform.position=d.position;
            Bounds target=player.bounds;
            if(SweptHit(previous,d.position,size,d.previousPlayer,target))
            {
                hitThisWave=true;modes.TakeHit();Remove(d,true);
                continue;
            }
            if(d.position.y-size.y*.5f<=target.max.y && previous.y+size.y*.5f>=target.min.y)
                closest=Mathf.Min(closest,Mathf.Max(0,Mathf.Abs(target.center.x-plan.Centers[i])-target.extents.x-size.x*.5f));
            d.previousPlayer=target;RefreshWarning(d,i,true);
            if(t>=1)Remove(d,true);else done=false;
        }
        if(done)Finish(!hitThisWave);
    }
    private void RefreshWarning(Drop d,int index,bool falling)
    {
        if(Camera.main==null)return;
        Rect landing=CurtainTentacles.ViewportBounds(Camera.main,new Bounds(new Vector3(plan.Centers[index],groundY+.02f,d.position.z),new Vector3(size.x,.02f,size.z)));
        float duration=plan.Warning+index*plan.Stagger;
        d.warning.ParticlesPerSecond=settings.debrisPerSecond;d.warning.ParticleSize=settings.debrisSize;
        d.warning.ShadowScale=settings.shadowScale;d.warning.ShadowOpacity=settings.shadowOpacity;
        d.warning.Show(landing,falling?1f:elapsed/duration,elapsed,duration,falling);
    }
    private void Remove(Drop d,bool fragments)
    {
        if(d.cube!=null)
        {
            if(fragments&&d.body!=null&&d.body.enabled&&modes!=null)modes.EmitObstacleBreak(d.body);
            d.cube.SetActive(false);UnityEngine.Object.Destroy(d.cube);
        }
        d.cube=null;d.body=null;
        // Scene teardown may destroy the warning canvas before the spawner.
        // Unity's null comparison also detects an already-destroyed component.
        if(d.warning!=null)d.warning.Hide();
        d.finished=true;
    }
    private void Finish(bool dodged)
    {
        foreach(Drop d in drops)Remove(d,true);
        CurrentPhase=Phase.Recovery;elapsed=0;
        // Reward only a completely avoided wave; each drop resolves independently.
        if(dodged)owner.RegisterDodge(closest);
    }
    public void Cancel()
    {
        foreach(Drop d in drops)if(d!=null)Remove(d,false);
        waiting=false;plan=null;CurrentPhase=Phase.Idle;cooldown=NextInterval(false);
    }
    private float NextInterval(bool randomize)
    {
        float interval=settings.intervalSeconds+(randomize?UnityEngine.Random.Range(-settings.intervalVariance,settings.intervalVariance):0);
        return Mathf.Max(Mathf.Min(settings.intervalSeconds,settings.minimumIntervalSeconds),interval*(modes!=null?modes.EncounterIntervalMultiplier:1f));
    }
    public void Dispose(){Cancel();if(material!=null)UnityEngine.Object.Destroy(material);if(ui!=null)UnityEngine.Object.Destroy(ui);}
    public static bool SweptHit(Vector3 from,Vector3 to,Vector3 size,Bounds previousPlayer,Bounds currentPlayer)
    {
        Vector3 origin=from-previousPlayer.center;
        Vector3 travel=to-from-(currentPlayer.center-previousPlayer.center);
        Vector3 extent=size*.5f+Vector3.Max(previousPlayer.extents,currentPlayer.extents);
        float enter=0,exit=1;
        for(int axis=0;axis<3;axis++)
        {
            if(Mathf.Abs(travel[axis])<.000001f){if(Mathf.Abs(origin[axis])>extent[axis])return false;continue;}
            float a=(-extent[axis]-origin[axis])/travel[axis],b=(extent[axis]-origin[axis])/travel[axis];
            enter=Mathf.Max(enter,Mathf.Min(a,b));exit=Mathf.Min(exit,Mathf.Max(a,b));
            if(enter>exit)return false;
        }
        return true;
    }
}
