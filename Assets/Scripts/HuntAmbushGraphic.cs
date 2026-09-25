using UnityEngine;
using UnityEngine.UI;

// Opaque tapered grabs, with the reaction cue in the gap before contact.
[RequireComponent(typeof(CanvasRenderer))]
public sealed class HuntAmbushGraphic : MaskableGraphic
{
    public enum AttackSide { Left, Top, Right }
    public ViscousCurtain Curtain { get; set; }
    private AttackSide side;
    private HuntAmbushState state;
    private Rect player;
    private float edge;
    private Vector2 prompt;
    private bool showKey,spaceKey;
    public void Show(HuntAmbushState value,Rect playerBounds,float curtainEdge,Vector2 keyPosition,bool keyVisible,bool space,AttackSide attackSide)
    {state=value;player=playerBounds;edge=curtainEdge;prompt=keyPosition;showKey=keyVisible;spaceKey=space;side=attackSide;SetVerticesDirty();}
    public void Hide(){state=null;SetVerticesDirty();}
    public static float KeyScale(HuntAmbushState value)
    {
        bool struggle=value.Current==HuntAmbushState.Phase.Bind || value.Current==HuntAmbushState.Phase.Struggle;
        float time=value.PhaseTime+(value.Current==HuntAmbushState.Phase.Struggle?value.Configuration.bindSeconds:0);
        return QteKeyGraphic.PulseScale(struggle,time,value.PressPulse,value.Configuration.strugglePulseHz,value.Configuration.strugglePulseAmount);
    }
    // Aspect converts a screen-height distance to viewport X, preserving circles.
    public static void StrikePath(Rect body,float curtainEdge,AttackSide direction,int step,float aspect,float radius,
        out Vector2 from,out Vector2 controlA,out Vector2 controlB,out Vector2 tip,out Vector2 key)
    {
        float y=Mathf.Lerp(body.yMin,body.yMax,step==1?.3f:.7f);
        float gap=radius+.012f;
        if(direction==AttackSide.Top)
        {
            key=new Vector2(body.center.x,body.yMax+gap);
            tip=key+Vector2.up*(radius+.012f);
            from=new Vector2(Mathf.Clamp(body.center.x+(step-1)*.12f,.08f,.92f),1.015f);
            controlA=new Vector2(from.x+.035f,Mathf.Lerp(from.y,tip.y,.4f));
            controlB=tip+new Vector2(-.02f,.045f);
        }
        else
        {
            float sign=direction==AttackSide.Left?-1f:1f;
            key=new Vector2((sign<0?body.xMin:body.xMax)+sign*gap*aspect,y);
            tip=key+Vector2.right*(sign*(radius+.012f)*aspect);
            from=new Vector2(sign<0?curtainEdge-.006f:1.015f,step==0?.72f:step==1?.26f:.55f);
            controlA=new Vector2(Mathf.Lerp(from.x,tip.x,.38f),from.y+.055f);
            controlB=tip+new Vector2(sign*.07f,.035f);
        }
    }
    protected override void OnPopulateMesh(VertexHelper mesh)
    {
        mesh.Clear();if(state==null || !state.Engaged)return;
        var phase=state.Current;
        bool bound=phase==HuntAmbushState.Phase.Bind || phase==HuntAmbushState.Phase.Struggle || phase==HuntAmbushState.Phase.Consume
            || (phase==HuntAmbushState.Phase.Release && state.WasBound);
        if(bound)DrawBindings(mesh);
        else if(phase==HuntAmbushState.Phase.Lunge || phase==HuntAmbushState.Phase.Focus || phase==HuntAmbushState.Phase.Response || phase==HuntAmbushState.Phase.Evade)
        {
            float aspect=rectTransform.rect.height/Mathf.Max(1,rectTransform.rect.width);
            StrikePath(player,edge,side,state.Step,aspect,state.Configuration.reactionRingRadius,
                out Vector2 from,out Vector2 a,out Vector2 b,out Vector2 to,out _);
            if(side==AttackSide.Left)AttachLeftRoot(ref from,ref a,state.Configuration.grabRootWidth);
            float extension=phase==HuntAmbushState.Phase.Lunge?Mathf.SmoothStep(0,1,state.Progress)
                :phase==HuntAmbushState.Phase.Evade?1-Mathf.SmoothStep(0,1,state.Progress):1;
            Curve(mesh,from,a,b,to,extension,state.Configuration.grabRootWidth);
        }
        if(showKey)DrawKeyFrame(mesh);
    }
    private void DrawBindings(VertexHelper mesh)
    {
        float strength=state.Current==HuntAmbushState.Phase.Bind?Mathf.SmoothStep(0,1,state.Progress):1f;
        if(state.Current==HuntAmbushState.Phase.Release)strength=1-Mathf.SmoothStep(0,1,state.Progress);
        float remaining=1f-state.EscapeProgress*.8f;
        for(int i=0;i<9;i++)
        {
            float strand=Mathf.Clamp01(remaining*9f-i)*strength;if(strand<=0)continue;
            float f=i/8f,y=Mathf.Lerp(player.yMin-.008f,player.yMax+.008f,f);
            AttackSide bindingSide=(AttackSide)(((int)side+i)%3);
            Vector2 from,to,a,b;
            if(bindingSide==AttackSide.Top)
            {
                from=new Vector2(Mathf.Clamp(player.center.x+(f-.5f)*.35f,.06f,.94f),1.015f);
                to=new Vector2(player.center.x+Mathf.Sin(i)*player.width*.55f,y);
                a=new Vector2(from.x+.035f,Mathf.Lerp(from.y,to.y,.4f));b=to+new Vector2(-.03f,.08f);
            }
            else
            {
                bool left=bindingSide==AttackSide.Left;float sign=left?-1:1;
                from=new Vector2(left?edge-.008f:1.015f,Mathf.Lerp(.18f,.82f,f));
                to=new Vector2(left?player.xMin-.004f:player.xMax+.004f,y);
                a=new Vector2(Mathf.Lerp(from.x,to.x,.35f),from.y+.06f);b=to+new Vector2(sign*.035f,-.025f);
            }
            if(bindingSide==AttackSide.Left)AttachLeftRoot(ref from,ref a,state.Configuration.grabRootWidth*.65f);
            Curve(mesh,from,a,b,to,strand,state.Configuration.grabRootWidth*.65f);
            const int samples=32;
            for(int j=0;j<samples;j++)
            {
                float t=j/(float)samples,u=(j+1f)/samples;
                Vector2 coilA=Coil(t,i,y),coilB=Coil(u,i,y);
                Line(mesh,coilA,coilB,.009f*strand*(1-.2f*state.PressPulse),Color.black);
            }
        }
    }
    private void AttachLeftRoot(ref Vector2 from, ref Vector2 control, float width)
    {
        if(Curtain==null)return;
        float attached=Curtain.AttachmentX(from.y,width);
        control.x += (attached-from.x)*.62f;
        from.x=attached;
    }
    private Vector2 Coil(float t,int strand,float y)
    {
        float a=t*Mathf.PI*2f;
        return new Vector2(player.center.x+Mathf.Cos(a)*(player.width*.66f+.004f),
            y+Mathf.Sin(a)*player.height*.045f+Mathf.Cos(a+strand+state.PhaseTime*2f)*player.height*.028f);
    }
    private void DrawKeyFrame(VertexHelper mesh)
    {
        float remaining=state.Current==HuntAmbushState.Phase.Response?1-state.Progress:1;
        QteKeyGraphic.DrawFrame(mesh,rectTransform.rect,prompt,spaceKey,state.Configuration.reactionRingRadius,
            remaining,KeyScale(state),Color.white);
    }
    private void Curve(VertexHelper mesh,Vector2 a,Vector2 b,Vector2 c,Vector2 d,float extension,float width)
    {
        if(extension<=.0001f)return;
        const int segments=48;int start=mesh.currentVertCount;Rect r=rectTransform.rect;
        for(int i=0;i<=segments;i++)
        {
            float t=i/(float)segments*extension;
            Vector2 p=Bezier(a,b,c,d,t);
            Vector2 derivative=3*(1-t)*(1-t)*(b-a)+6*(1-t)*t*(c-b)+3*t*t*(d-c);
            derivative=Vector2.Scale(derivative,new Vector2(r.width,r.height));
            Vector2 n=new Vector2(-derivative.y,derivative.x).normalized*width*r.height*.5f*Mathf.Pow(1-i/(float)segments,.8f);
            Vector2 pixel=new Vector2(r.xMin+p.x*r.width,r.yMin+p.y*r.height);
            mesh.AddVert(pixel-n,Color.black,Vector2.zero);mesh.AddVert(pixel+n,Color.black,Vector2.zero);
            if(i==0)continue;int v=start+i*2;
            mesh.AddTriangle(v-2,v-1,v);mesh.AddTriangle(v-1,v+1,v);
        }
    }
    private static Vector2 Bezier(Vector2 a,Vector2 b,Vector2 c,Vector2 d,float t)
    {float s=1-t;return s*s*s*a+3*s*s*t*b+3*s*t*t*c+t*t*t*d;}
    private void Line(VertexHelper mesh,Vector2 from,Vector2 to,float width,Color color)
    {
        Rect r=rectTransform.rect;
        Vector2 a=new Vector2(r.xMin+from.x*r.width,r.yMin+from.y*r.height),b=new Vector2(r.xMin+to.x*r.width,r.yMin+to.y*r.height);
        Vector2 direction=b-a;if(direction.sqrMagnitude<.00001f)return;
        float pixels=color.r>0?Mathf.Max(1.5f,width*r.height):width*r.height;
        Vector2 n=new Vector2(-direction.y,direction.x).normalized*pixels*.5f;
        int v=mesh.currentVertCount;
        mesh.AddVert(a-n,color,Vector2.zero);mesh.AddVert(a+n,color,Vector2.zero);
        mesh.AddVert(b+n,color,Vector2.zero);mesh.AddVert(b-n,color,Vector2.zero);
        mesh.AddTriangle(v,v+1,v+2);mesh.AddTriangle(v,v+2,v+3);
    }
}
