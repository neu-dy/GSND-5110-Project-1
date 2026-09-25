using UnityEngine;
using System.Collections.Generic;

// All lanes are committed and warned together. Delayed strikes never retarget.
public sealed class CeilingVolley
{
    private readonly GameModeController modes;
    private readonly Collider player;
    private readonly TentacleAttackState.Settings settings;
    private readonly TentacleGraphic[] graphics=new TentacleGraphic[3];
    private readonly DropWarningGraphic[] warnings=new DropWarningGraphic[3];
    private readonly float[] x=new float[3],radius=new float[3];
    private readonly bool[] contacted=new bool[3];
    private readonly Rect[] landing=new Rect[3];
    private OverheadPressure.Plan plan;
    private readonly List<ObstacleMovement> obstacles=new List<ObstacleMovement>();
    private float previousTime;
    public float Closest { get; private set; }
    public bool Exposed { get; private set; }
    public CeilingVolley(GameModeController modes,Collider player,TentacleAttackState.Settings settings,Transform parent)
    {
        this.modes=modes;this.player=player;this.settings=settings;
        for(int i=0;i<3;i++)
        {
            graphics[i]=Make<TentacleGraphic>("Ceiling Tendril "+(i+1),parent);
            warnings[i]=Make<DropWarningGraphic>("Ceiling Liquid "+(i+1),parent);
            warnings[i].Style=DropWarningGraphic.Cue.BlackLiquid;
        }
    }
    private static T Make<T>(string name,Transform parent) where T:UnityEngine.UI.MaskableGraphic
    {
        var go=new GameObject(name,typeof(RectTransform),typeof(CanvasRenderer),typeof(T));go.transform.SetParent(parent,false);
        var r=go.GetComponent<RectTransform>();r.anchorMin=Vector2.zero;r.anchorMax=Vector2.one;r.offsetMin=r.offsetMax=Vector2.zero;
        var g=go.GetComponent<T>();g.raycastTarget=false;return g;
    }
    public void Begin(OverheadPressure.Plan value,float ground)
    {
        Hide();plan=value;previousTime=0;Closest=float.PositiveInfinity;Exposed=false;
        System.Array.Clear(contacted,0,contacted.Length);
        Camera camera=Camera.main;
        for(int i=0;i<plan.Centers.Length;i++)
        {
            Vector3 p=new Vector3(plan.Centers[i],player.bounds.center.y,player.bounds.center.z);
            x[i]=camera.WorldToViewportPoint(p).x;
            radius[i]=Mathf.Abs(camera.WorldToViewportPoint(p+Vector3.right*settings.ceilingHalfWidth).x-x[i]);
            landing[i]=CurtainTentacles.ViewportBounds(camera,new Bounds(new Vector3(p.x,ground+.02f,p.z),
                new Vector3(settings.ceilingHalfWidth*2,.02f,Mathf.Max(.7f,player.bounds.size.z))));
        }
    }
    public void Hide()
    {
        for(int i=0;i<3;i++)
        {
            if(graphics[i]!=null)graphics[i].SetShape(0,0,0,0,0,false);
            if(warnings[i]!=null)warnings[i].Hide();
        }
        plan=null;
    }
    public void Render(TentacleAttackState state)
    {
        if(plan==null||Camera.main==null)return;
        Physics.SyncTransforms();float elapsed=state.AttackElapsed;
        Rect body=CurtainTentacles.ViewportBounds(Camera.main,player.bounds);
        for(int i=0;i<plan.Centers.Length;i++)
        {
            float warning=plan.Warning+i*plan.Stagger,t=elapsed-warning;
            float end=settings.ceilingExtendSeconds+settings.ceilingHoldSeconds+settings.ceilingRetractSeconds;
            bool telegraph=t<0;
            float tip=Tip(t);
            graphics[i].SetVerticalShape(1.01f,tip,x[i],radius[i]*(telegraph?2.56f:1.6f),radius[i],telegraph);
            if(telegraph)
            {
                warnings[i].ParticlesPerSecond=settings.liquidDropsPerSecond;warnings[i].ParticleSize=settings.liquidDropSize;
                warnings[i].LiquidSourceDepth=settings.ceilingWarningDepth;
                warnings[i].Show(landing[i],elapsed/warning,elapsed,warning,false);
            }
            else warnings[i].Hide();
            if(t<0||previousTime>=warning+end)continue;
            // Include the deepest point crossed in this frame, even on a slow frame.
            float collisionTip=Mathf.Min(tip,Tip(previousTime-warning));
            if(previousTime-warning<=settings.ceilingExtendSeconds+settings.ceilingHoldSeconds && t>=settings.ceilingExtendSeconds)
                collisionTip=settings.ceilingTipViewport;
            graphics[i].SetVerticalShape(1.01f,collisionTip,x[i],radius[i]*1.6f,radius[i],false);
            if(collisionTip<=body.yMax)
            {
                Exposed=true;
                float gap=Mathf.Max(0,Mathf.Max(body.xMin-x[i]-radius[i],x[i]-radius[i]-body.xMax));
                Closest=Mathf.Min(Closest,gap*settings.ceilingHalfWidth/Mathf.Max(.0001f,radius[i]));
            }
            obstacles.Clear();foreach(var obstacle in ObstacleMovement.Active)if(obstacle!=null)obstacles.Add(obstacle);
            foreach(var obstacle in obstacles)
                if(obstacle!=null)
                    foreach(var renderer in obstacle.GetComponentsInChildren<Renderer>())
                        if(renderer.enabled&&graphics[i].Intersects(CurtainTentacles.ViewportBounds(Camera.main,renderer.bounds))){obstacle.BreakApart();break;}
            bool hit=graphics[i].Intersects(body);
            graphics[i].SetVerticalShape(1.01f,tip,x[i],radius[i]*1.6f,radius[i],false);
            if(hit&&!contacted[i])
            {
                contacted[i]=true;state.NotifyHit();
                modes.TakeTentacleHit(new Vector2(Mathf.Clamp(x[i],body.xMin,body.xMax),body.yMax));
            }
        }
        previousTime=elapsed;
    }
    private float Tip(float t)
    {
        float root=1.01f,preview=1f-settings.ceilingWarningDepth;
        if(t<0)return Mathf.Lerp(root,preview,Mathf.SmoothStep(0,1,1+t/(plan!=null?plan.Warning:1)));
        if(t<settings.ceilingExtendSeconds)return Mathf.Lerp(preview,settings.ceilingTipViewport,Mathf.SmoothStep(0,1,t/settings.ceilingExtendSeconds));
        t-=settings.ceilingExtendSeconds;
        if(t<settings.ceilingHoldSeconds)return settings.ceilingTipViewport;
        return Mathf.Lerp(settings.ceilingTipViewport,root,Mathf.SmoothStep(0,1,(t-settings.ceilingHoldSeconds)/settings.ceilingRetractSeconds));
    }
}
