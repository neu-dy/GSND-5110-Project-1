using System.Collections.Generic;
using UnityEngine;

// Scene adapter: one scheduler coordinates ordinary obstacles and all strikes.
public sealed class CurtainTentacles
{
    public TentacleAttackState State { get; private set; }
    public bool BlocksSpawns => State != null && State.BlocksSpawns;
    public float Focus { get; private set; }
    public float Relief { get; private set; }
    public float ExtraBreathing => Relief*settings.exitBreathingDistance;
    private readonly GameModeController modes;
    private readonly TentacleAttackState.Settings settings;
    private readonly ObstacleSpawner spawner;
    private readonly Collider player;
    private readonly PlayerVerticalMovement vertical;
    private readonly PlayerSprint sprint;
    private readonly ViscousCurtain curtain;
    private readonly TentacleGraphic graphic;
    private readonly DropWarningGraphic ceilingWarning;
    private readonly Vector3 home;
    private readonly CeilingVolley ceilingVolley;
    private OverheadPressure.Plan ceilingPlan;
    private readonly List<ObstacleMovement> obstacles = new List<ObstacleMovement>();
    private int lockedAttack,completed;
    private float lockedY,lockedRadius,lockedRootRadius,lockedTip,previewTip;


    private bool hadExposure;
    private float closestClearance;

    public CurtainTentacles(GameModeController modes,TentacleAttackState.Settings settings,
        ObstacleSpawner spawner,Collider player,ViscousCurtain curtain,Transform uiRoot)
    {
        this.modes=modes;this.settings=settings;this.spawner=spawner;this.player=player;this.curtain=curtain;
        vertical=player.GetComponent<PlayerVerticalMovement>();
        sprint=player.GetComponent<PlayerSprint>();
        home=player.transform.position;
        ceilingVolley=new CeilingVolley(modes,player,settings,uiRoot);
        var go=new GameObject("Curtain Tentacle",typeof(RectTransform),typeof(CanvasRenderer),typeof(TentacleGraphic));
        go.transform.SetParent(uiRoot,false);
        var rect=go.GetComponent<RectTransform>();rect.anchorMin=Vector2.zero;rect.anchorMax=Vector2.one;
        rect.offsetMin=rect.offsetMax=Vector2.zero;
        graphic=go.GetComponent<TentacleGraphic>();graphic.raycastTarget=false;
        go.SetActive(false);
        var cue=new GameObject("Ceiling Black Liquid",typeof(RectTransform),typeof(CanvasRenderer),typeof(DropWarningGraphic));
        cue.transform.SetParent(uiRoot,false);
        var cueRect=cue.GetComponent<RectTransform>();cueRect.anchorMin=Vector2.zero;cueRect.anchorMax=Vector2.one;
        cueRect.offsetMin=cueRect.offsetMax=Vector2.zero;
        ceilingWarning=cue.GetComponent<DropWarningGraphic>();ceilingWarning.raycastTarget=false;
        ceilingWarning.Style=DropWarningGraphic.Cue.BlackLiquid;
    }
    public void Begin()
    {
        State=new TentacleAttackState(settings);lockedAttack=completed=0;Focus=Relief=0;
        graphic.gameObject.SetActive(true);
        ceilingWarning.Hide();ceilingVolley.Hide();
    }
    public void NotifyHit() { State?.NotifyHit(); }
    public void Cancel()
    {
        State?.Cancel();Focus=Relief=0;
        if(graphic!=null){graphic.SetShape(0,0,0,0,0,false);graphic.gameObject.SetActive(false);}
        if(ceilingWarning!=null)ceilingWarning.Hide();
        ceilingVolley.Hide();
    }
    public void Tick(float dt,double meters)
    {
        if(State==null || dt<=0)return;
        float safeLeft=home.x-player.bounds.extents.x;
        bool clear=spawner==null || !spawner.HasPendingObstacle(safeLeft);
        bool grounded=vertical==null || !vertical.IsAirborne;
        float jumpWindow=vertical!=null ? vertical.JumpClearanceSeconds(settings.lowCenterHeight+settings.lowHalfHeight*1.6f) : 0;
        bool canCeiling=false;
        if(clear && State.WantsCeilingAttack && State.CurrentAction==TentacleAttackState.Action.Waiting && sprint!=null && Camera.main!=null)
        {
            Physics.SyncTransforms();
            float active=settings.ceilingExtendSeconds+settings.ceilingHoldSeconds+settings.ceilingRetractSeconds;
            ceilingPlan=OverheadPressure.Choose(settings.ceilingPressure,meters,modes.RunElapsedSeconds,
                player.bounds.center.x,sprint.HorizontalHomeX,sprint.HorizontalLimitX,settings.ceilingHalfWidth*2,
                settings.ceilingWarningSeconds,active,UnityEngine.Random.value<settings.ceilingPressure.simultaneousChance,UnityEngine.Random.value<.5f,
                (lanes,warning,total)=>sprint.CanAvoidOverhead(lanes,settings.ceilingHalfWidth,warning,total,settings.ceilingReactionSeconds,settings.ceilingEscapePadding)
                    && modes.CanAffordCrouch(total,0,settings.ceilingSafetyReserve));
            canCeiling=ceilingPlan!=null;
        }
        State.Tick(dt,meters,clear,grounded,jumpWindow,canCeiling,modes.EncounterIntervalMultiplier);
        if(State.CompletedAttacks!=completed)
        {
            completed=State.CompletedAttacks;
            if(hadExposure && !State.HitThisAttack)
                modes.SuccessfulDodge(closestClearance);
            hadExposure=false;
        }
        if(State.AttackId!=lockedAttack)
        {
            lockedAttack=State.AttackId;LockAttack();
            hadExposure=false;closestClearance=float.PositiveInfinity;
        }
        bool focusing=State.CurrentAction==TentacleAttackState.Action.Warning;
        float target=focusing ? 1f : State.CurrentStage==TentacleAttackState.Stage.HuntPrelude ? .55f : 0f;
        Focus=Mathf.MoveTowards(Focus,target,dt*4);
        Relief=Mathf.MoveTowards(Relief,State.CurrentStage==TentacleAttackState.Stage.Recovery ? 1f : 0f,dt);
    }
    private void LockAttack()
    {
        Camera camera=Camera.main;if(camera==null)return;
        if(State.AttackKind==TentacleAttackState.Kind.CeilingStab)
        {
            if(ceilingPlan==null)return;
            State.ConfigureCeilingVolley(ceilingPlan.Warning,ceilingPlan.Stagger*(ceilingPlan.Centers.Length-1));
            ceilingVolley.Begin(ceilingPlan,vertical!=null?vertical.GroundY:home.y-1);
            return;
        }
        bool high=State.AttackKind==TentacleAttackState.Kind.HighSweep;
        float height=high?settings.highCenterHeight:settings.lowCenterHeight;
        float radius=high?settings.highHalfHeight:settings.lowHalfHeight;
        float ground=vertical!=null?vertical.GroundY:home.y-1;
        Vector3 center=new Vector3(home.x,ground+height,home.z);
        lockedY=camera.WorldToViewportPoint(center).y;
        lockedRadius=Mathf.Abs(camera.WorldToViewportPoint(center+Vector3.up*radius).y-lockedY);
        lockedRootRadius=lockedRadius*1.6f;
        // The strike lane is committed at warning start. It never tracks input.
        lockedTip=settings.reachViewport;
        float left=camera.WorldToViewportPoint(new Vector3(home.x-player.bounds.extents.x,ground+height,home.z)).x;
        previewTip=Mathf.Min(left-.018f,curtain.rectTransform.anchorMax.x+settings.warningReachViewport);
    }
    public void RenderAndCollide()
    {
        if(State==null || !settings.enabled || !modes.IsPlaying) { graphic.SetShape(0,0,0,0,0,false);ceilingWarning.Hide();ceilingVolley.Hide();return; }
        Camera camera=Camera.main;if(camera==null)return;
        bool warning=State.CurrentAction==TentacleAttackState.Action.Warning;
        if(!warning && !State.IsAttacking) { graphic.SetShape(0,0,0,0,0,false);ceilingWarning.Hide();ceilingVolley.Hide();return; }
        bool ceiling=State.AttackKind==TentacleAttackState.Kind.CeilingStab;
        if(ceiling)
        {
            graphic.SetShape(0,0,0,0,0,false);ceilingWarning.Hide();
            ceilingVolley.Render(State);hadExposure=ceilingVolley.Exposed;closestClearance=ceilingVolley.Closest;return;
        }
        ceilingVolley.Hide();
        float root=curtain.EdgeAt(lockedY)-.008f;
        float preview=Mathf.Max(root,previewTip);
        float progress=warning?Mathf.SmoothStep(0,1,State.WarningProgress):1f;
        float tip=warning?Mathf.Lerp(root,preview,progress):Mathf.Lerp(preview,lockedTip,State.Extension);
        if(State.CurrentAction==TentacleAttackState.Action.Retract)tip=Mathf.Lerp(root,lockedTip,State.Extension);
        float rootRadius=lockedRootRadius*(warning?1.6f:1f);
        graphic.SetShape(root,tip,lockedY,rootRadius,lockedRadius,warning);
        graphic.SetRootAttachment(curtain.AttachmentX(lockedY,rootRadius));
        ceilingWarning.Hide();
        if(warning)return;
        // Vertical input and sprint moved this frame; use their current colliders,
        // rather than the previous fixed-physics step, for the short ground strike.
        Physics.SyncTransforms();
        Bounds hurtbox=player.bounds;
        hurtbox.Expand(new Vector3(0,-2f*Mathf.Min(settings.playerHitboxInset,hurtbox.extents.y*.25f),0));
        Rect bounds=ViewportBounds(camera,hurtbox);
        if(tip>=bounds.xMin && root<=bounds.xMax)
        {
            hadExposure=true;
            float gap=Mathf.Max(0,Mathf.Max(bounds.yMin-(lockedY+lockedRadius),(lockedY-lockedRadius)-bounds.yMax));
            float worldPerViewport=lockedRadius>0 ? (State.AttackKind==TentacleAttackState.Kind.HighSweep?settings.highHalfHeight:settings.lowHalfHeight)/lockedRadius : 1;
            closestClearance=Mathf.Min(closestClearance,gap*worldPerViewport);
        }
        // Only residual obstacles should remain here; still shatter any solid part
        // actually touched by the strike. BreakApart disqualifies dodge rewards.
        obstacles.Clear();foreach(var obstacle in ObstacleMovement.Active)if(obstacle!=null)obstacles.Add(obstacle);
        foreach(var obstacle in obstacles)
        {
            foreach(var body in obstacle.GetComponentsInChildren<Renderer>())
                if(body.enabled && graphic.Intersects(ViewportBounds(camera,body.bounds))) { obstacle.BreakApart();break; }
        }
        if(!State.HitThisAttack && graphic.Intersects(bounds))
        {
            State.NotifyHit();
            modes.TakeTentacleHit(new Vector2(bounds.xMin,Mathf.Clamp(lockedY,bounds.yMin,bounds.yMax)));
        }
    }
    public static Rect ViewportBounds(Camera camera,Bounds bounds)
    {
        Vector2 min=new Vector2(float.PositiveInfinity,float.PositiveInfinity),max=new Vector2(float.NegativeInfinity,float.NegativeInfinity);
        for(int x=-1;x<=1;x+=2)for(int y=-1;y<=1;y+=2)for(int z=-1;z<=1;z+=2)
        {
            Vector3 p=camera.WorldToViewportPoint(bounds.center+Vector3.Scale(bounds.extents,new Vector3(x,y,z)));
            min=Vector2.Min(min,p);max=Vector2.Max(max,p);
        }
        return Rect.MinMaxRect(min.x,min.y,max.x,max.y);
    }
}
