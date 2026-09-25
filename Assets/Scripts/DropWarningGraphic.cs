using UnityEngine;
using UnityEngine.UI;

// Projected scene effects: no arrows, brackets, lane guides or timing bars.
// The attack's scaled clock drives motion, so pausing also freezes the cue.
[RequireComponent(typeof(CanvasRenderer))]
public sealed class DropWarningGraphic : MaskableGraphic
{
    public enum Cue { Debris, BlackLiquid }
    public Cue Style { get; set; }
    public Color DebrisColor { get; set; } = new Color(1f,.12f,.62f,1f);
    public float ParticlesPerSecond { get; set; } = 10f;
    public float ParticleSize { get; set; } = 1f;
    public float ShadowScale { get; set; } = 1.15f;
    public float ShadowOpacity { get; set; } = .6f;
    public float LiquidSourceDepth { get; set; } = .09f;
    private Rect landing;
    private float progress, elapsed, duration;
    private bool visible, dropping;

    public void Show(Rect area,float warningProgress,float seconds,float warningDuration,bool falling)
    {
        landing=area;progress=Mathf.Clamp01(warningProgress);elapsed=Mathf.Max(0,seconds);
        duration=Mathf.Max(.1f,warningDuration);dropping=falling;visible=true;
        SetVerticesDirty();
    }
    public void Hide() { visible=false;SetVerticesDirty(); }

    protected override void OnPopulateMesh(VertexHelper mesh)
    {
        mesh.Clear();if(!visible)return;
        Rect r=rectTransform.rect;
        float x=r.xMin+landing.center.x*r.width, floor=r.yMin+landing.center.y*r.height;
        float width=Mathf.Max(r.height*.025f,landing.width*r.width);
        float unit=r.height*.006f*Mathf.Clamp(ParticleSize,.3f,3f);
        bool liquid=Style==Cue.BlackLiquid;
        if(!liquid)
        {
            float approach=dropping?1f:Mathf.SmoothStep(0,1,progress);
            float radius=width*.5f*Mathf.Clamp(ShadowScale,.5f,2f)*Mathf.Lerp(.32f,1f,approach);
            float depth=Mathf.Max(r.height*.012f,landing.height*r.height*.8f)*Mathf.Lerp(.45f,1f,approach);
            Color shade=new Color(0,0,0,Mathf.Clamp01(ShadowOpacity)*Mathf.Lerp(.28f,1f,approach));
            Ellipse(mesh,new Vector2(x,floor),radius,depth,shade,true);
        }
        float rate=Mathf.Clamp(ParticlesPerSecond,2f,30f);
        // Stateless emission: resizing/rebuilding cannot re-roll fragments.
        // Existing fragments finish falling after the heavy obstacle is released.
        int last=Mathf.FloorToInt(Mathf.Min(elapsed,duration-.001f)*rate);
        int first=Mathf.Max(0,last-48);
        for(int i=first;i<=last;i++)
        {
            float birth=i/rate,age=elapsed-birth;
            float seed=Noise(i+1),side=(Noise(i+19)-.5f)*width*.76f;
            float travel=liquid?Mathf.Lerp(.6f,.85f,seed):Mathf.Lerp(.48f,.72f,seed);
            const float settle=.24f;
            if(age<0 || age>travel+settle)continue;
            float size=unit*Mathf.Lerp(.65f,1.25f,Noise(i+41));
            float source=liquid?Mathf.Lerp(1.01f,1f-LiquidSourceDepth,Mathf.SmoothStep(0,1,birth/duration)):1.02f;
            float top=r.yMin+source*r.height;
            if(age<travel)
            {
                float t=age/travel,y=Mathf.Lerp(top,floor,t*t);
                if(liquid)
                    Drop(mesh,new Vector2(x+side,y),size*.85f,size*Mathf.Lerp(1.6f,3.8f,t));
                else
                {
                    float drift=Mathf.Sin(age*5f+seed*6f)*width*.07f;
                    Color chip=Color.Lerp(DebrisColor,new Color(.25f,.22f,.24f,1),seed*.45f);
                    Shard(mesh,new Vector2(x+side+drift,y),size,seed*6f+age*(seed-.5f)*10f,chip);
                }
            }
            else
            {
                float t=(age-travel)/settle,remain=1f-t;
                if(liquid)
                {
                    // Opaque splats collapse into the floor, never a target ring.
                    Ellipse(mesh,new Vector2(x+side,floor),size*(1f+3f*t)*remain,size*.55f*remain,Color.black,false);
                    for(int j=0;j<2;j++)
                    {
                        float sign=j==0?-1:1;
                        Ellipse(mesh,new Vector2(x+side+sign*size*4f*t,floor+size*3f*Mathf.Sin(t*Mathf.PI)),
                            size*.4f*remain,size*.65f*remain,Color.black,false);
                    }
                }
                else
                {
                    Color chip=DebrisColor;chip.a=remain;
                    Shard(mesh,new Vector2(x+side+(seed-.5f)*size*6f*t,floor+size*2f*Mathf.Sin(t*Mathf.PI)),
                        size*remain,seed*6f+t*2f,chip);
                }
            }
        }
    }
    private static float Noise(int i) => Mathf.Repeat(Mathf.Sin(i*127.1f)*43758.5453f,1f);
    private static void Shard(VertexHelper mesh,Vector2 centre,float size,float angle,Color color)
    {
        int start=mesh.currentVertCount;
        for(int i=0;i<5;i++)
        {
            float a=angle+i*Mathf.PI*.4f,scale=i%2==0?1f:.65f;
            mesh.AddVert(centre+new Vector2(Mathf.Cos(a),Mathf.Sin(a))*size*scale,color,Vector2.zero);
        }
        for(int i=1;i<4;i++)mesh.AddTriangle(start,start+i,start+i+1);
    }
    private static void Drop(VertexHelper mesh,Vector2 centre,float width,float length)
    {
        const int segments=16;int start=mesh.currentVertCount;
        mesh.AddVert(centre,Color.black,Vector2.zero);
        for(int i=0;i<=segments;i++)
        {
            float a=i*Mathf.PI*2f/segments,s=Mathf.Sin(a),c=Mathf.Cos(a);
            mesh.AddVert(centre+new Vector2(c*width*(s>0?1f-.75f*s:1f),s*(s>0?length:width)),Color.black,Vector2.zero);
            if(i>0)mesh.AddTriangle(start,start+i,start+i+1);
        }
    }
    private static void Ellipse(VertexHelper mesh,Vector2 centre,float rx,float ry,Color color,bool soft)
    {
        const int segments=32;int start=mesh.currentVertCount;
        mesh.AddVert(centre,color,Vector2.zero);
        for(int i=0;i<=segments;i++)
        {
            float a=i*Mathf.PI*2f/segments;
            Vector2 d=new Vector2(Mathf.Cos(a)*rx,Mathf.Sin(a)*ry);
            Color edge=color;if(soft)edge.a=0;
            mesh.AddVert(centre+d*.65f,color,Vector2.zero);
            mesh.AddVert(centre+d,edge,Vector2.zero);
            if(i==0)continue;int v=start+1+i*2;
            mesh.AddTriangle(start,v-2,v);mesh.AddTriangle(v-2,v-1,v);mesh.AddTriangle(v-1,v+1,v);
        }
    }
}
