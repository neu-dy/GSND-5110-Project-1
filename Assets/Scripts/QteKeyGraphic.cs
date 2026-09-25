using UnityEngine;
using UnityEngine.UI;

// Shared key frame for hunt ambushes and the vehicle finale.
[RequireComponent(typeof(CanvasRenderer))]
public sealed class QteKeyGraphic : MaskableGraphic
{
    private Vector2 position;
    private bool visible,space;
    private float radius,remaining,scale;
    private Color foreground=Color.white;
    public void Show(Vector2 center,bool mash,float ringRadius,float timeRemaining,float keyScale,Color tint)
    {
        position=center;space=mash;radius=ringRadius;remaining=Mathf.Clamp01(timeRemaining);
        scale=keyScale;foreground=tint;visible=true;SetVerticesDirty();
    }
    public void Hide(){if(!visible)return;visible=false;SetVerticesDirty();}
    protected override void OnPopulateMesh(VertexHelper mesh)
    {
        mesh.Clear();if(visible)DrawFrame(mesh,rectTransform.rect,position,space,radius,remaining,scale,foreground);
    }
    public static float PulseScale(bool mash,float time,float pressPulse,float frequency,float amount)
    {
        float pulse=mash?amount*(.5f-.5f*Mathf.Cos(time*Mathf.PI*2*frequency)):0;
        return 1+pulse+.12f*pressPulse;
    }
    public static void DrawFrame(VertexHelper mesh,Rect rect,Vector2 center,bool mash,float radius,float remaining,float scale,Color foreground)
    {
        float aspect=rect.height/Mathf.Max(1,rect.width);
        if(mash)
        {
            float h=.033f*scale,w=h*2.5f*aspect;
            Vector2 a=center+new Vector2(-w,-h),b=center+new Vector2(w,-h),c=center+new Vector2(w,h),d=center+new Vector2(-w,h);
            Line(mesh,rect,a,b,.008f,Color.black);Line(mesh,rect,b,c,.008f,Color.black);
            Line(mesh,rect,c,d,.008f,Color.black);Line(mesh,rect,d,a,.008f,Color.black);
            Line(mesh,rect,a,b,.003f,foreground);Line(mesh,rect,b,c,.003f,foreground);
            Line(mesh,rect,c,d,.003f,foreground);Line(mesh,rect,d,a,.003f,foreground);
            return;
        }
        // The endpoint retreats counterclockwise. The caller chooses whether this
        // represents a single reaction window or a shared interaction deadline.
        Arc(mesh,rect,center,radius,remaining,.01f,Color.black);
        Arc(mesh,rect,center,radius,remaining,.004f,foreground);
    }
    private static void Arc(VertexHelper mesh,Rect rect,Vector2 center,float radius,float remaining,float width,Color tint)
    {
        float aspect=rect.height/Mathf.Max(1,rect.width);
        const int segments=96;
        for(int i=0;i<segments;i++)
        {
            float a=(90-360*remaining*i/segments)*Mathf.Deg2Rad,b=(90-360*remaining*(i+1)/segments)*Mathf.Deg2Rad;
            Line(mesh,rect,center+new Vector2(Mathf.Cos(a)*aspect,Mathf.Sin(a))*radius,
                center+new Vector2(Mathf.Cos(b)*aspect,Mathf.Sin(b))*radius,width,tint);
        }
    }
    private static void Line(VertexHelper mesh,Rect r,Vector2 from,Vector2 to,float width,Color tint)
    {
        Vector2 a=new Vector2(r.xMin+from.x*r.width,r.yMin+from.y*r.height),b=new Vector2(r.xMin+to.x*r.width,r.yMin+to.y*r.height);
        Vector2 direction=b-a;if(direction.sqrMagnitude<.00001f)return;
        float pixels=tint.r>0?Mathf.Max(1.5f,width*r.height):width*r.height;
        Vector2 n=new Vector2(-direction.y,direction.x).normalized*pixels*.5f;
        int v=mesh.currentVertCount;
        mesh.AddVert(a-n,tint,Vector2.zero);mesh.AddVert(a+n,tint,Vector2.zero);
        mesh.AddVert(b+n,tint,Vector2.zero);mesh.AddVert(b-n,tint,Vector2.zero);
        mesh.AddTriangle(v,v+1,v+2);mesh.AddTriangle(v,v+2,v+3);
    }
}
